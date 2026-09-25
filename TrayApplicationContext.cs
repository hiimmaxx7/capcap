using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Capcap;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const int HOTKEY_START = 1;
    private const int HOTKEY_STOP = 2;
    private const int HOTKEY_PAUSE = 3;

    private readonly NotifyIcon _trayIcon;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly Recorder _recorder = new();

    private readonly Dictionary<CaptureMode, ToolStripMenuItem> _modeItems = new();
    private readonly Dictionary<int, ToolStripMenuItem> _fpsItems = new();
    private ToolStripMenuItem _toggleItem = null!;
    private ToolStripMenuItem _soundItem = null!;
    private ToolStripMenuItem _systemAudioItem = null!;

    private CaptureMode _selectedMode = CaptureMode.FullScreen;
    private int _selectedFps = 60;
    private bool _soundsEnabled = true;
    private bool _systemAudioEnabled = false;
    private Rectangle? _lastRegion = AppSettings.LoadLastRegion();

    private CountdownOverlayForm? _countdownOverlay;
    private System.Windows.Forms.Timer? _countdownTimer;
    private bool IsCountingDown => _countdownTimer is not null;

    private BorderOverlayForm? _borderOverlay;
    private System.Windows.Forms.Timer? _borderTimer;

    private static readonly Dictionary<CaptureMode, string> ModeLabels = new()
    {
        [CaptureMode.FullScreen] = "Toàn màn hình",
        [CaptureMode.FullScreenNoTaskbar] = "Toàn màn hình (ẩn taskbar)",
        [CaptureMode.Region] = "Chọn vùng",
        [CaptureMode.Vertical9x16Follow] = "Dọc 9:16 - bám theo con trỏ"
    };

    public TrayApplicationContext()
    {
        _hotkeyWindow = new HotkeyWindow();
        _hotkeyWindow.HotKeyPressed += OnHotKeyPressed;
        const uint mod = NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT;
        bool okStart = NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, HOTKEY_START, mod, (uint)Keys.Home);
        bool okStop = NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, HOTKEY_STOP, mod, (uint)Keys.End);
        bool okPause = NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, HOTKEY_PAUSE, mod, (uint)Keys.P);

        _trayIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _trayIcon.DoubleClick += (_, _) => ToggleRecording();
        UpdateUiState();

        if (!okStart || !okStop || !okPause)
        {
            var failed = new List<string>();
            if (!okStart) failed.Add("Ctrl+Home");
            if (!okStop) failed.Add("Ctrl+End");
            if (!okPause) failed.Add("Ctrl+P");
            _trayIcon.ShowBalloonTip(4000, "Capcap",
                $"Không đăng ký được phím tắt: {string.Join(", ", failed)} (có thể đã bị app khác dùng). " +
                "Bạn vẫn có thể bấm đúp vào icon này để bắt đầu/dừng quay, hoặc dùng menu chuột phải.",
                ToolTipIcon.Warning);
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        _toggleItem = new ToolStripMenuItem("Bắt đầu quay  (Ctrl+Home)");
        _toggleItem.Click += (_, _) => ToggleRecording();
        menu.Items.Add(_toggleItem);

        menu.Items.Add(new ToolStripSeparator());

        var modeMenu = new ToolStripMenuItem("Chế độ quay");
        AddModeItem(modeMenu, CaptureMode.FullScreen, "Toàn màn hình");
        AddModeItem(modeMenu, CaptureMode.FullScreenNoTaskbar, "Toàn màn hình (ẩn taskbar)");
        AddModeItem(modeMenu, CaptureMode.Region, "Chọn vùng (kéo chuột)...");
        AddModeItem(modeMenu, CaptureMode.Vertical9x16Follow, "Dọc 9:16 - bám theo con trỏ");
        menu.Items.Add(modeMenu);

        var fpsMenu = new ToolStripMenuItem("Tốc độ khung hình");
        AddFpsItem(fpsMenu, 30);
        AddFpsItem(fpsMenu, 60);
        menu.Items.Add(fpsMenu);

        _soundItem = new ToolStripMenuItem("Âm thanh khi click / gõ phím") { CheckOnClick = true, Checked = _soundsEnabled };
        _soundItem.Click += (_, _) => _soundsEnabled = _soundItem.Checked;
        menu.Items.Add(_soundItem);

        _systemAudioItem = new ToolStripMenuItem("Ghi âm thanh hệ thống") { CheckOnClick = true, Checked = _systemAudioEnabled };
        _systemAudioItem.Click += (_, _) => _systemAudioEnabled = _systemAudioItem.Checked;
        menu.Items.Add(_systemAudioItem);

        menu.Items.Add(new ToolStripSeparator());

        var openFolder = new ToolStripMenuItem("Mở thư mục lưu video");
        openFolder.Click += (_, _) => Process.Start(new ProcessStartInfo
        {
            FileName = RecordingOptions.GetDefaultOutputFolder(),
            UseShellExecute = true
        });
        menu.Items.Add(openFolder);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Thoát");
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void AddModeItem(ToolStripMenuItem parent, CaptureMode mode, string text)
    {
        var item = new ToolStripMenuItem(text) { Checked = mode == _selectedMode };
        item.Click += (_, _) => SelectMode(mode);
        _modeItems[mode] = item;
        parent.DropDownItems.Add(item);
    }

    private void AddFpsItem(ToolStripMenuItem parent, int fps)
    {
        var item = new ToolStripMenuItem($"{fps} fps") { Checked = fps == _selectedFps };
        item.Click += (_, _) =>
        {
            _selectedFps = fps;
            foreach (var kv in _fpsItems) kv.Value.Checked = kv.Key == fps;
        };
        _fpsItems[fps] = item;
        parent.DropDownItems.Add(item);
    }

    private void SelectMode(CaptureMode mode)
    {
        if (_recorder.IsRecording || IsCountingDown) return;

        if (mode == CaptureMode.Region)
        {
            // Picking a region is itself the "start" action: drag -> confirm -> countdown -> record.
            if (TryPickRegion(out var region)) OnRegionPicked(region);
            return;
        }

        _selectedMode = mode;
        foreach (var kv in _modeItems) kv.Value.Checked = kv.Key == mode;
        _trayIcon.ShowBalloonTip(1200, "Chế độ quay đã đổi", ModeLabels[mode], ToolTipIcon.Info);
    }

    private void OnRegionPicked(Rectangle region)
    {
        RememberRegion(region);
        _selectedMode = CaptureMode.Region;
        foreach (var kv in _modeItems) kv.Value.Checked = kv.Key == CaptureMode.Region;

        using var confirm = new ConfirmStartForm(region);
        if (confirm.ShowDialog() != DialogResult.OK) return;

        RunCountdownThenStart(region);
    }

    private void RememberRegion(Rectangle region)
    {
        _lastRegion = region;
        AppSettings.SaveLastRegion(region);
    }

    /// <summary>Shows 3-2-1 over the area about to be recorded, then starts. The overlay is
    /// closed before Recorder.Start(), so it never ends up in the video.</summary>
    private void RunCountdownThenStart(Rectangle targetRegion)
    {
        if (IsCountingDown || _recorder.IsRecording) return;

        _countdownOverlay = new CountdownOverlayForm(targetRegion, 3);
        _countdownOverlay.Show();
        int remaining = 3;

        _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _countdownTimer.Tick += (_, _) =>
        {
            remaining--;
            if (remaining <= 0)
            {
                EndCountdown();
                StartRecording();
            }
            else
            {
                _countdownOverlay?.SetCount(remaining);
            }
        };
        _countdownTimer.Start();
        UpdateUiState();
    }

    private void EndCountdown()
    {
        _countdownTimer?.Stop();
        _countdownTimer?.Dispose();
        _countdownTimer = null;

        _countdownOverlay?.Close();
        _countdownOverlay?.Dispose();
        _countdownOverlay = null;
        UpdateUiState();
    }

    private void CancelCountdown()
    {
        if (!IsCountingDown) return;
        EndCountdown();
        _trayIcon.ShowBalloonTip(1000, "Capcap", "Đã hủy đếm ngược", ToolTipIcon.Info);
    }

    /// <summary>Where the 3-2-1 goes for the current mode: over the region, over the 9:16 strip
    /// that will start centered on the cursor, or in the middle of the cursor's monitor.</summary>
    private Rectangle CountdownTarget()
    {
        var screen = Screen.FromPoint(Cursor.Position).Bounds;
        switch (_selectedMode)
        {
            case CaptureMode.Region when _lastRegion is { } region:
                return region;
            case CaptureMode.Vertical9x16Follow:
                int w = Math.Min(screen.Width, (int)Math.Round(screen.Height * 9.0 / 16.0));
                int x = Math.Max(screen.Left, Math.Min(screen.Right - w, Cursor.Position.X - w / 2));
                return new Rectangle(x, screen.Top, w, screen.Height);
            default:
                return screen;
        }
    }

    private bool TryPickRegion(out Rectangle region)
    {
        using var form = new RegionSelectForm(_lastRegion);
        var result = form.ShowDialog();
        if (result == DialogResult.OK && form.SelectedRegion.HasValue)
        {
            region = form.SelectedRegion.Value;
            return true;
        }
        region = default;
        return false;
    }

    private void OnHotKeyPressed(int id)
    {
        switch (id)
        {
            case HOTKEY_START:
                if (!_recorder.IsRecording) StartRecordingFlow();
                break;
            case HOTKEY_STOP:
                if (IsCountingDown) CancelCountdown();
                else if (_recorder.IsRecording) StopRecording();
                break;
            case HOTKEY_PAUSE:
                TogglePause();
                break;
        }
    }

    private void ToggleRecording()
    {
        if (IsCountingDown)
        {
            CancelCountdown();
        }
        else if (_recorder.IsRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecordingFlow();
        }
    }

    private void TogglePause()
    {
        if (!_recorder.IsRecording) return;

        if (_recorder.IsPaused)
        {
            _recorder.Resume();
            _borderOverlay?.SetPaused(false);
            _trayIcon.ShowBalloonTip(1000, "Capcap", "Tiếp tục quay", ToolTipIcon.Info);
        }
        else
        {
            _recorder.Pause();
            _borderOverlay?.SetPaused(true);
            _trayIcon.ShowBalloonTip(1000, "Capcap", "Đã tạm dừng (Ctrl+P để tiếp tục)", ToolTipIcon.Info);
        }
        UpdateUiState();
    }

    private void StartRecordingFlow()
    {
        if (IsCountingDown) return;

        if (_selectedMode == CaptureMode.Region && _lastRegion is null)
        {
            if (!TryPickRegion(out var region)) return;
            RememberRegion(region);
        }
        RunCountdownThenStart(CountdownTarget());
    }

    private void StartRecording()
    {
        var opts = new RecordingOptions
        {
            Mode = _selectedMode,
            RegionBounds = _lastRegion,
            Fps = _selectedFps,
            RecordClickKeySounds = _soundsEnabled,
            RecordSystemAudio = _systemAudioEnabled
        };

        try
        {
            _recorder.Start(opts);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể bắt đầu quay:\n{ex.Message}", "Capcap",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        UpdateUiState();
        StartBorderOverlayIfNeeded();
        _trayIcon.ShowBalloonTip(1500, "Capcap",
            $"Đang quay ({ModeLabels[_selectedMode]})... Ctrl+End để dừng, Ctrl+P để tạm dừng", ToolTipIcon.Info);
    }

    private void StopRecording()
    {
        _recorder.Stop();
        StopBorderOverlay();
        UpdateUiState();

        if (_recorder.LastOutputPath is { } path && File.Exists(path))
        {
            long size = new FileInfo(path).Length;
            using var dialog = new SaveDiscardForm(path, size);
            var result = dialog.ShowDialog();
            if (result != DialogResult.OK)
            {
                try { File.Delete(path); } catch { }
            }
        }
    }

    private void StartBorderOverlayIfNeeded()
    {
        if (_selectedMode == CaptureMode.FullScreen) return;

        _borderOverlay = new BorderOverlayForm();
        _borderOverlay.Show();

        _borderTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _borderTimer.Tick += (_, _) =>
        {
            if (_recorder.GetLiveGrabRect() is { } rect)
            {
                _borderOverlay.SetTargetRect(rect);
            }
        };
        _borderTimer.Start();
    }

    private void StopBorderOverlay()
    {
        _borderTimer?.Stop();
        _borderTimer?.Dispose();
        _borderTimer = null;

        _borderOverlay?.Close();
        _borderOverlay?.Dispose();
        _borderOverlay = null;
    }

    private void UpdateUiState()
    {
        bool recording = _recorder.IsRecording;
        bool paused = _recorder.IsPaused;

        _toggleItem.Text = recording
            ? $"Dừng quay  (Ctrl+End) - {ModeLabels[_selectedMode]}{(paused ? " [Tạm dừng]" : "")}"
            : IsCountingDown
                ? "Hủy đếm ngược  (Ctrl+End)"
                : "Bắt đầu quay  (Ctrl+Home)";
        _trayIcon.Text = recording
            ? $"Capcap - {(paused ? "Tạm dừng" : "Đang quay")} ({ModeLabels[_selectedMode]})"
            : "Capcap - Ctrl+Home bắt đầu, Ctrl+End dừng, Ctrl+P tạm dừng";
    }

    private void ExitApp()
    {
        EndCountdown();
        if (_recorder.IsRecording) StopRecording();
        NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_START);
        NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_STOP);
        NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_PAUSE);
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        ExitThread();
    }
}
