using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Capcap;

internal enum CaptureMode
{
    FullScreen,
    FullScreenNoTaskbar,
    Region,
    Vertical9x16Follow
}

internal sealed class RecordingOptions
{
    public CaptureMode Mode = CaptureMode.FullScreen;
    public Rectangle? RegionBounds;   // required when Mode == Region (virtual-screen coords)
    public int Fps = 60;
    public int Crf = 32;              // higher = smaller file, lower quality
    public bool RecordClickKeySounds = true;
    public bool RecordSystemAudio = false; // WASAPI loopback (what's playing through speakers)
    public double CursorScale = 2.0;  // multiplier on top of the DPI-corrected cursor size
    public string OutputFolder = GetDefaultOutputFolder();

    public static string GetDefaultOutputFolder()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "capcap");
        Directory.CreateDirectory(folder);
        return folder;
    }
}

internal sealed class Recorder
{
    // How many grabbed-but-not-yet-encoded frames we'll buffer before dropping new
    // ones. Decouples the capture cadence from ffmpeg's encode throughput so a slow
    // encode never blocks (and stalls the timing of) the capture loop.
    private const int QueueCapacity = 20;

    public bool IsRecording { get; private set; }
    public bool IsPaused { get; private set; }
    public string? LastOutputPath { get; private set; }

    private volatile bool _paused;
    private Thread? _captureThread;
    private Thread? _writerThread;
    private volatile bool _stopRequested;
    private Process? _ffmpeg;
    private Stream? _ffmpegStdin;

    private Rectangle _screenClampBounds; // monitor bounds used to clamp the follow-crop
    private Rectangle _fixedGrabRect;     // used by FullScreen / FullScreenNoTaskbar / Region
    private Size _outputSize;
    private bool _isFollowMode;
    private int _frameByteCount;
    private double _cursorScale = 1.0;
    private Size _cursorTargetSize = new(32, 32);
    private double _frameIntervalSec = 1.0 / 60;

    private DxgiScreenCapture? _dxgiCapture;
    private bool _useDxgi;
    private int _bytesPerPixel = 3;

    private double _followCropLeft;

    private volatile bool _firstFrameSeen;
    private double _firstFrameOffsetSec;

    private readonly Stopwatch _clock = new();
    private InputSoundLogger? _soundLogger;
    private SystemAudioCapture? _systemAudioCapture;
    private RecordingOptions _opts = new();

    private string _tempVideoPath = "";
    private string _tempWavPath = "";
    private string _tempSystemAudioPath = "";

    private readonly object _rectLock = new();
    private Rectangle _liveGrabRect;

    private BlockingCollection<byte[]>? _frameQueue;
    private ConcurrentBag<byte[]>? _bufferPool;

    /// <summary>Current on-screen capture rectangle (virtual-screen coords), updated every frame.
    /// Used by the UI to draw a border around the area being recorded. Null when not recording.</summary>
    public Rectangle? GetLiveGrabRect()
    {
        if (!IsRecording) return null;
        lock (_rectLock) return _liveGrabRect;
    }

    public void Start(RecordingOptions opts)
    {
        if (IsRecording) return;
        _opts = opts;
        _cursorScale = opts.CursorScale;
        _frameIntervalSec = 1.0 / Math.Max(1, opts.Fps);

        var startScreen = Screen.FromPoint(Cursor.Position);

        // DPI/pointer-size can't change mid-recording, so compute the cursor draw size
        // once here instead of querying it (MonitorFromPoint + GetDpiForMonitor) every frame.
        _cursorTargetSize = CursorPainter.ComputeTargetCursorSize(Cursor.Position, opts.CursorScale);

        switch (opts.Mode)
        {
            case CaptureMode.FullScreen:
                _fixedGrabRect = MakeEven(startScreen.Bounds);
                _isFollowMode = false;
                break;
            case CaptureMode.FullScreenNoTaskbar:
                _fixedGrabRect = MakeEven(startScreen.WorkingArea);
                _isFollowMode = false;
                break;
            case CaptureMode.Region:
                if (opts.RegionBounds is null) throw new InvalidOperationException("RegionBounds is required for Region mode.");
                _fixedGrabRect = MakeEven(opts.RegionBounds.Value);
                _isFollowMode = false;
                break;
            case CaptureMode.Vertical9x16Follow:
                _screenClampBounds = startScreen.Bounds;
                int h = MakeEven(_screenClampBounds.Height);
                int w = MakeEven(Math.Min(_screenClampBounds.Width, (int)Math.Round(h * 9.0 / 16.0)));
                _outputSize = new Size(w, h);
                _followCropLeft = Cursor.Position.X - w / 2.0;
                _isFollowMode = true;
                break;
        }

        if (!_isFollowMode)
        {
            _outputSize = _fixedGrabRect.Size;
        }

        // Try GPU-accelerated capture first — GDI's CopyFromScreen cost scales with the
        // captured area and can't reliably sustain 60fps at full 1080p+ on some hardware.
        // Falls back to GDI transparently if DXGI can't be initialized for this monitor
        // (e.g. no compatible adapter, or a virtualized/remote display with no real GPU output).
        try
        {
            _dxgiCapture?.Dispose();
            _dxgiCapture = new DxgiScreenCapture(startScreen.Bounds);
            _useDxgi = true;
            _bytesPerPixel = 4;
        }
        catch
        {
            _dxgiCapture = null;
            _useDxgi = false;
            _bytesPerPixel = 3;
        }

        _frameByteCount = _outputSize.Width * _bytesPerPixel * _outputSize.Height;
        _frameQueue = new BlockingCollection<byte[]>(QueueCapacity);
        _bufferPool = new ConcurrentBag<byte[]>();

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Directory.CreateDirectory(opts.OutputFolder);
        _tempVideoPath = Path.Combine(opts.OutputFolder, $"_tmp_{stamp}.mp4");
        _tempWavPath = Path.Combine(opts.OutputFolder, $"_tmp_{stamp}.wav");
        _tempSystemAudioPath = Path.Combine(opts.OutputFolder, $"_tmpsys_{stamp}.wav");
        LastOutputPath = Path.Combine(opts.OutputFolder, $"capcap_{stamp}.mp4");

        StartFfmpeg(_outputSize, opts.Fps, opts.Crf, _tempVideoPath, _useDxgi ? "bgra" : "bgr24");

        _clock.Restart();

        if (opts.RecordClickKeySounds)
        {
            _soundLogger = new InputSoundLogger(_clock);
            _soundLogger.Start();
        }

        if (opts.RecordSystemAudio)
        {
            _systemAudioCapture = new SystemAudioCapture(_tempSystemAudioPath);
            _systemAudioCapture.Start();
        }

        _stopRequested = false;
        _firstFrameSeen = false;
        _firstFrameOffsetSec = 0;
        IsRecording = true;

        _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "FfmpegWriterLoop" };
        _writerThread.Start();

        _captureThread = new Thread(() => CaptureLoop(opts.Fps)) { IsBackground = true, Name = "ScreenCaptureLoop" };
        _captureThread.Start();
    }

    public void Stop()
    {
        if (!IsRecording) return;
        _stopRequested = true;
        _captureThread?.Join();
        _frameQueue?.CompleteAdding();
        _writerThread?.Join();
        _clock.Stop();

        _soundLogger?.Stop();
        _systemAudioCapture?.Stop();

        try { _ffmpegStdin?.Flush(); } catch { /* ffmpeg may have already exited */ }
        try { _ffmpegStdin?.Close(); } catch { }
        try { _ffmpeg?.WaitForExit(15000); } catch { }

        double durationSec = Math.Max(0, _clock.Elapsed.TotalSeconds - _firstFrameOffsetSec);

        var audioTracks = new List<string>();

        if (_opts.RecordClickKeySounds && _soundLogger is not null)
        {
            List<(double TimeSec, InputSoundKind Kind)> rawEvents;
            lock (_soundLogger.Events) rawEvents = new(_soundLogger.Events);

            // Shift every event by the same startup offset so the audio track's zero
            // point lines up with the video's actual first frame instead of Start().
            var events = rawEvents.ConvertAll(e => (TimeSec: Math.Max(0, e.TimeSec - _firstFrameOffsetSec), e.Kind));

            WavBuilder.WriteEventTrack(_tempWavPath, durationSec, events);
            audioTracks.Add(_tempWavPath);
        }

        if (_opts.RecordSystemAudio && File.Exists(_tempSystemAudioPath))
        {
            audioTracks.Add(_tempSystemAudioPath);
        }

        if (audioTracks.Count > 0)
        {
            MuxAudio(_tempVideoPath, audioTracks, LastOutputPath!);
            TryDelete(_tempVideoPath);
            foreach (var track in audioTracks) TryDelete(track);
        }
        else
        {
            TryDelete(LastOutputPath!);
            File.Move(_tempVideoPath, LastOutputPath!);
        }

        _soundLogger?.Dispose();
        _soundLogger = null;
        _systemAudioCapture?.Dispose();
        _systemAudioCapture = null;
        _dxgiCapture?.Dispose();
        _dxgiCapture = null;
        _ffmpeg?.Dispose();
        _ffmpeg = null;
        _ffmpegStdin = null;
        _frameQueue?.Dispose();
        _frameQueue = null;
        _bufferPool = null;
        IsRecording = false;
    }

    public void Pause()
    {
        if (!IsRecording || _paused) return;
        _paused = true;
        IsPaused = true;
        // Stopwatch.Stop() halts accumulation without resetting it, so the "active"
        // (pause-excluding) elapsed time stays correct for both audio-event timestamps
        // and the final duration used to size the WAV track.
        _clock.Stop();
        _systemAudioCapture?.Pause();
    }

    public void Resume()
    {
        if (!IsRecording || !_paused) return;
        _paused = false;
        IsPaused = false;
        _clock.Start();
        _systemAudioCapture?.Resume();
    }

    private void CaptureLoop(int fps)
    {
        // Cap on how many duplicate frames we'll insert per catch-up burst, so a very
        // long stall doesn't flood the pipe — beyond this we just accept falling behind.
        const int maxCatchUpDuplicates = 10;

        var frameTimer = Stopwatch.StartNew();
        long framesSent = 0;

        // GDI fallback path only — unused (but harmless to allocate) when DXGI is active.
        using var frameBmp = _useDxgi ? null : new Bitmap(_outputSize.Width, _outputSize.Height, PixelFormat.Format24bppRgb);
        using var frameGfx = frameBmp is null ? null : Graphics.FromImage(frameBmp);

        while (!_stopRequested)
        {
            if (_paused)
            {
                // Keep framesSent pinned to "now" so resuming doesn't try to burst-catch-up
                // on all the frames that would otherwise have been due during the pause.
                Thread.Sleep(50);
                framesSent = (long)(frameTimer.ElapsedMilliseconds * fps / 1000.0);
                continue;
            }

            long targetFrames = (long)(frameTimer.ElapsedMilliseconds * fps / 1000.0);
            if (targetFrames <= framesSent)
            {
                Thread.Sleep(1);
                continue;
            }

            Point grabOrigin = _isFollowMode ? ComputeFollowOrigin() : _fixedGrabRect.Location;
            lock (_rectLock) _liveGrabRect = new Rectangle(grabOrigin, _outputSize);

            try
            {
                byte[] buffer = RentBuffer();

                if (_useDxgi)
                {
                    _dxgiCapture!.CaptureRegion(new Rectangle(grabOrigin, _outputSize), buffer);
                    DrawCursorOntoBuffer(buffer, grabOrigin);
                }
                else
                {
                    frameGfx!.CopyFromScreen(grabOrigin, Point.Empty, _outputSize, CopyPixelOperation.SourceCopy);
                    CursorPainter.DrawCursorOnFrame(frameGfx, grabOrigin, _cursorTargetSize);
                    CopyBitmapToBuffer(frameBmp!, buffer);
                }

                if (!_firstFrameSeen)
                {
                    // ffmpeg/thread startup means some real time passes before this very
                    // first frame — which becomes t=0 in the video regardless. Recording
                    // that offset lets us shift click/key sound timestamps by the same
                    // amount so the audio track lines up with the video instead of lagging.
                    _firstFrameSeen = true;
                    _firstFrameOffsetSec = _clock.Elapsed.TotalSeconds;
                }

                EnqueueFrame(buffer);
                framesSent++;

                // We're behind schedule (encoding or capture couldn't keep up) — repeat this
                // same frame to fill the missing slots instead of silently compressing time,
                // which is what made recordings play back faster than real time / go out of
                // sync with the audio track under load.
                long behind = Math.Min(targetFrames - framesSent, maxCatchUpDuplicates);
                for (int i = 0; i < behind; i++)
                {
                    byte[] dup = RentBuffer();
                    Array.Copy(buffer, dup, buffer.Length);
                    EnqueueFrame(dup);
                    framesSent++;
                }
            }
            catch
            {
                // ffmpeg pipe closed or capture hiccup — stop cleanly instead of crashing the app.
                _stopRequested = true;
            }
        }
    }

    private void EnqueueFrame(byte[] buffer)
    {
        // Encoding can't keep up right now — drop this frame rather than block and let
        // capture timing (and therefore playback smoothness) fall behind.
        if (!_frameQueue!.TryAdd(buffer))
        {
            ReturnBuffer(buffer);
        }
    }

    private void WriterLoop()
    {
        try
        {
            foreach (var buffer in _frameQueue!.GetConsumingEnumerable())
            {
                try
                {
                    _ffmpegStdin!.Write(buffer, 0, buffer.Length);
                }
                catch
                {
                    // ffmpeg exited early; stop trying to feed it and let Stop() finish cleanup.
                    break;
                }
                finally
                {
                    ReturnBuffer(buffer);
                }
            }
        }
        catch (ObjectDisposedException) { }
    }

    private byte[] RentBuffer()
    {
        if (_bufferPool!.TryTake(out var buffer) && buffer.Length == _frameByteCount) return buffer;
        return new byte[_frameByteCount];
    }

    private void ReturnBuffer(byte[] buffer) => _bufferPool?.Add(buffer);

    private Point ComputeFollowOrigin()
    {
        var cursor = Cursor.Position;

        // Dead-zone: only start panning once the cursor gets within 1/6 of the crop's
        // width from its left/right edge — small movements near the middle don't move
        // the frame at all, which is what "chỉ di chuyển khi tới gần mép" asked for.
        double margin = _outputSize.Width / 6.0;
        double targetLeft = _followCropLeft;

        if (cursor.X < _followCropLeft + margin)
        {
            targetLeft = cursor.X - margin;
        }
        else if (cursor.X > _followCropLeft + _outputSize.Width - margin)
        {
            targetLeft = cursor.X - _outputSize.Width + margin;
        }

        // Ease toward that target: a proportional term for a smooth stop, PLUS a minimum
        // speed floor so a big jump (cursor dragged well past the edge) catches up
        // promptly instead of crawling — the dead-zone above already absorbs small moves,
        // so this only ever engages for a deliberate move and should track it closely.
        const double timeConstantSec = 0.15;
        double alpha = 1.0 - Math.Exp(-_frameIntervalSec / timeConstantSec);
        double diff = targetLeft - _followCropLeft;
        double proportionalStep = diff * alpha;

        double minSpeedPxPerSec = _outputSize.Width / 0.3; // close a full crop-width gap in ~0.3s at minimum
        double minStep = Math.Sign(diff) * minSpeedPxPerSec * _frameIntervalSec;

        double step = Math.Abs(proportionalStep) > Math.Abs(minStep) ? proportionalStep : minStep;
        if (Math.Abs(step) > Math.Abs(diff)) step = diff; // never overshoot the target

        _followCropLeft += step;

        int x = (int)Math.Round(_followCropLeft);
        int maxX = Math.Max(_screenClampBounds.Left, _screenClampBounds.Right - _outputSize.Width);
        x = Math.Min(maxX, Math.Max(_screenClampBounds.Left, x));
        return new Point(x, _screenClampBounds.Top);
    }

    /// <summary>Composites the real cursor directly into a tightly-packed BGRA32 buffer
    /// (the DXGI capture path) by pinning it and viewing it as a GDI bitmap in place —
    /// reuses <see cref="CursorPainter"/> as-is instead of a separate D3D-based renderer.</summary>
    private void DrawCursorOntoBuffer(byte[] buffer, Point frameOrigin)
    {
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            using var bmp = new Bitmap(_outputSize.Width, _outputSize.Height, _outputSize.Width * 4,
                PixelFormat.Format32bppRgb, handle.AddrOfPinnedObject());
            using var g = Graphics.FromImage(bmp);
            CursorPainter.DrawCursorOnFrame(g, frameOrigin, _cursorTargetSize);
        }
        finally
        {
            handle.Free();
        }
    }

    private static void CopyBitmapToBuffer(Bitmap bmp, byte[] dest)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        BitmapData bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            int rowBytes = bmp.Width * 3;
            if (bd.Stride == rowBytes)
            {
                // No row padding (common when width is a multiple of 4) — copy in one shot.
                Marshal.Copy(bd.Scan0, dest, 0, rowBytes * bmp.Height);
            }
            else
            {
                for (int y = 0; y < bmp.Height; y++)
                {
                    Marshal.Copy(bd.Scan0 + y * bd.Stride, dest, y * rowBytes, rowBytes);
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
    }

    private void StartFfmpeg(Size size, int fps, int crf, string outputPath, string pixFmt)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-y -f rawvideo -pix_fmt {pixFmt} -s {size.Width}x{size.Height} -r {fps} -i - " +
                        $"-an -c:v libx264 -preset ultrafast -tune zerolatency -crf {crf} -pix_fmt yuv420p -movflags +faststart \"{outputPath}\"",
            RedirectStandardInput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        _ffmpeg = Process.Start(psi) ?? throw new InvalidOperationException("Không khởi động được ffmpeg. Kiểm tra ffmpeg đã có trong PATH chưa.");
        _ffmpegStdin = _ffmpeg.StandardInput.BaseStream;
    }

    private static void MuxAudio(string videoPath, List<string> audioPaths, string outputPath)
    {
        string inputs = $"-i \"{videoPath}\" " + string.Join(" ", audioPaths.ConvertAll(p => $"-i \"{p}\""));

        string audioMapArgs;
        if (audioPaths.Count == 1)
        {
            audioMapArgs = "-c:a aac -b:a 128k -shortest";
        }
        else
        {
            // Multiple audio sources (e.g. click/key ticks + system audio loopback) —
            // mix them into one track rather than only keeping the first.
            string inputRefs = string.Join("", Enumerable.Range(1, audioPaths.Count).Select(i => $"[{i}:a]"));
            audioMapArgs = $"-filter_complex \"{inputRefs}amix=inputs={audioPaths.Count}:duration=longest[aout]\" -map 0:v -map \"[aout]\" -c:a aac -b:a 128k";
        }

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-y {inputs} -c:v copy {audioMapArgs} \"{outputPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(30000);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static Rectangle MakeEven(Rectangle r) =>
        new(r.X, r.Y, MakeEven(r.Width), MakeEven(r.Height));

    private static int MakeEven(int v) => v % 2 == 0 ? v : v - 1;
}
