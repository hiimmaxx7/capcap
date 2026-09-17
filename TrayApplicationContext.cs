using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Oculus;

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
    private readonly Dictionary<double, ToolStripMenuItem> _cursorScaleItems = new();
    private ToolStripMenuItem _toggleItem = null!;
    private ToolStripMenuItem _soundItem = null!;

    private CaptureMode _selectedMode = CaptureMode.FullScreen;
    private int _selectedFps = 60;
    private bool _soundsEnabled = true;
    private double _cursorScale = 2.0;
    private Rectangle? _lastRegion;

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
            Icon = SystemIcons.Application,
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
            _trayIcon.ShowBalloonTip(4000, "Project Oculus",
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

        var cursorMenu = new ToolStripMenuItem("Cỡ con trỏ chuột");
        AddCursorScaleItem(cursorMenu, 1.0, "Thật (1x)");
        AddCursorScaleItem(cursorMenu, 1.5, "1.5x");
        AddCursorScaleItem(cursorMenu, 2.0, "2x (mặc định)");
        AddCursorScaleItem(cursorMenu, 3.0, "3x");
        menu.Items.Add(cursorMenu);

        _soundItem = new ToolStripMenuItem("Âm thanh khi click / gõ phím") { CheckOnClick = true, Checked = _soundsEnabled };
        _soundItem.Click += (_, _) => _soundsEnabled = _soundItem.Checked;
        menu.Items.Add(_soundItem);

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

    private void AddCursorScaleItem(ToolStripMenuItem parent, double scale, string text)
    {
        var item = new ToolStripMenuItem(text) { Checked = scale == _cursorScale };
        item.Click += (_, _) =>
        {
            _cursorScale = scale;
            foreach (var kv in _cursorScaleItems) kv.Value.Checked = kv.Key == scale;
        };
        _cursorScaleItems[scale] = item;
        parent.DropDownItems.Add(item);
    }

    private void SelectMode(CaptureMode mode)
    {
        if (_recorder.IsRecording) return;

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
        _lastRegion = region;
        _selectedMode = CaptureMode.Region;
        foreach (var kv in _modeItems) kv.Value.Checked = kv.Key == CaptureMode.Region;

        using var confirm = new ConfirmStartForm(region);
        if (confirm.ShowDialog() != DialogResult.OK) return;

        RunCountdownThenStart(region);
    }

    private void RunCountdownThenStart(Rectangle targetRegion)
    {
        var overlay = new CountdownOverlayForm(targetRegion, 3);
        overlay.Show();
        int remaining = 3;

        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) =>
        {
            remaining--;
            if (remaining <= 0)
            {
                timer.Stop();
                timer.Dispose();
                overlay.Close();
                overlay.Dispose();
                StartRecording();
            }
            else
            {
                overlay.SetCount(remaining);
            }
        };
        timer.Start();
    }

    private bool TryPickRegion(out Rectangle region)
    {
        using var form = new RegionSelectForm();
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
                if (_recorder.IsRecording) StopRecording();
                break;
            case HOTKEY_PAUSE:
                TogglePause();
                break;
        }
    }

    private void ToggleRecording()
    {
        if (_recorder.IsRecording)
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
            _trayIcon.ShowBalloonTip(1000, "Project Oculus", "Tiếp tục quay", ToolTipIcon.Info);
        }
        else
        {
            _recorder.Pause();
            _borderOverlay?.SetPaused(true);
            _trayIcon.ShowBalloonTip(1000, "Project Oculus", "Đã tạm dừng (Ctrl+P để tiếp tục)", ToolTipIcon.Info);
        }
        UpdateUiState();
    }

    private void StartRecordingFlow()
    {
        if (_selectedMode == CaptureMode.Region && _lastRegion is null)
        {
            if (!TryPickRegion(out var region)) return;
            _lastRegion = region;
            RunCountdownThenStart(region);
            return;
        }
        StartRecording();
    }

    private void StartRecording()
    {
        var opts = new RecordingOptions
        {
            Mode = _selectedMode,
            RegionBounds = _lastRegion,
            Fps = _selectedFps,
            RecordClickKeySounds = _soundsEnabled,
            CursorScale = _cursorScale
        };

        try
        {
            _recorder.Start(opts);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể bắt đầu quay:\n{ex.Message}", "Project Oculus",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        UpdateUiState();
        StartBorderOverlayIfNeeded();
        _trayIcon.ShowBalloonTip(1500, "Project Oculus",
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
            : "Bắt đầu quay  (Ctrl+Home)";
        _trayIcon.Text = recording
            ? $"Project Oculus - {(paused ? "Tạm dừng" : "Đang quay")} ({ModeLabels[_selectedMode]})"
            : "Project Oculus - Ctrl+Home bắt đầu, Ctrl+End dừng, Ctrl+P tạm dừng";
    }

    private void ExitApp()
    {
        if (_recorder.IsRecording) StopRecording();
        NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_START);
        NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_STOP);
        NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_PAUSE);
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        ExitThread();
    }
}
