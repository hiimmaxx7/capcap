using System.Windows.Forms;

namespace Oculus;

/// <summary>Invisible message-only window used to receive global WM_HOTKEY messages.</summary>
internal sealed class HotkeyWindow : NativeWindow
{
    public event Action<int>? HotKeyPressed;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams());
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            HotKeyPressed?.Invoke(m.WParam.ToInt32());
        }
        base.WndProc(ref m);
    }
}
