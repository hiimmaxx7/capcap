using System.Diagnostics;

namespace Capcap;

internal enum InputSoundKind { Click, Key, Scroll }

/// <summary>
/// Global low-level mouse/keyboard hooks used only to log timestamps of clicks
/// and keystrokes while recording, so the matching click/typing/scroll sample can be
/// mixed into the output audio track (no system audio / mic is captured).
/// Must be started/stopped from the thread that owns the app's message loop.
/// </summary>
internal sealed class InputSoundLogger : IDisposable
{
    public readonly List<(double TimeSec, InputSoundKind Kind)> Events = new();

    private readonly Stopwatch _clock;
    private readonly NativeMethods.HookProc _mouseProc;
    private readonly NativeMethods.HookProc _keyProc;
    private IntPtr _mouseHookId = IntPtr.Zero;
    private IntPtr _keyHookId = IntPtr.Zero;

    public InputSoundLogger(Stopwatch clock)
    {
        _clock = clock;
        _mouseProc = MouseHookCallback;
        _keyProc = KeyHookCallback;
    }

    public void Start()
    {
        _mouseHookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, IntPtr.Zero, 0);
        _keyHookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyProc, IntPtr.Zero, 0);
    }

    public void Stop()
    {
        if (_mouseHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }
        if (_keyHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyHookId);
            _keyHookId = IntPtr.Zero;
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        int msg = wParam.ToInt32();
        if (nCode >= 0 && (msg == NativeMethods.WM_LBUTTONDOWN || msg == NativeMethods.WM_RBUTTONDOWN))
        {
            lock (Events) Events.Add((_clock.Elapsed.TotalSeconds, InputSoundKind.Click));
        }
        else if (nCode >= 0 && msg == NativeMethods.WM_MOUSEWHEEL)
        {
            lock (Events) Events.Add((_clock.Elapsed.TotalSeconds, InputSoundKind.Scroll));
        }
        return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private IntPtr KeyHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        int msg = wParam.ToInt32();
        if (nCode >= 0 && (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN))
        {
            lock (Events) Events.Add((_clock.Elapsed.TotalSeconds, InputSoundKind.Key));
        }
        return NativeMethods.CallNextHookEx(_keyHookId, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
