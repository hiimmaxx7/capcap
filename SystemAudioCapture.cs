using NAudio.Wave;

namespace Capcap;

/// <summary>
/// Records whatever is playing through the system's default output device
/// (WASAPI loopback) to its own WAV file — separate from the synthetic
/// click/key/scroll tick track, so both can be mixed independently at mux time.
/// Writing pauses while the recording is paused, matching the video's own
/// paused-time gap so the two tracks stay in sync.
/// </summary>
internal sealed class SystemAudioCapture : IDisposable
{
    private readonly WasapiLoopbackCapture _capture;
    private WaveFileWriter? _writer;
    private volatile bool _paused;

    public string OutputPath { get; }

    public SystemAudioCapture(string outputPath)
    {
        OutputPath = outputPath;
        try
        {
            _capture = new WasapiLoopbackCapture();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Không tìm thấy thiết bị phát âm thanh mặc định trên máy — không thể ghi âm thanh hệ thống.", ex);
        }
        _capture.DataAvailable += OnDataAvailable;
    }

    public void Start()
    {
        _writer = new WaveFileWriter(OutputPath, _capture.WaveFormat);
        _capture.StartRecording();
    }

    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    public void Stop()
    {
        _capture.StopRecording();
        _writer?.Dispose();
        _writer = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_paused || _writer is null) return;
        _writer.Write(e.Buffer, 0, e.BytesRecorded);
    }

    public void Dispose()
    {
        Stop();
        _capture.Dispose();
    }
}
