using System.Drawing;
using System.Windows.Forms;

namespace Oculus;

/// <summary>
/// Click-through, always-on-top border drawn right at the edge of the capture
/// rectangle. Uses SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) so Windows
/// composites this window OUT of any screen capture (including our own GDI
/// grab), which is what actually keeps it out of the recording — position
/// alone can't guarantee that once the border has to follow a moving crop
/// (9:16 follow mode) faster than the UI thread can reposition it.
/// </summary>
internal sealed class BorderOverlayForm : Form
{
    private const int BorderThickness = 3;
    private static readonly Color KeyColor = Color.FromArgb(255, 1, 254, 1);
    private bool _paused;

    public BorderOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = KeyColor;
        TransparencyKey = KeyColor; // only the drawn (red) border pixels stay visible
        DoubleBuffered = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TRANSPARENT = 0x00000020;
            const int WS_EX_LAYERED = 0x00080000;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_NOACTIVATE = 0x08000000;

            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Belt-and-suspenders: even if timing ever let the border rect overlap the
        // capture rect, this makes the window itself invisible to any screen grab.
        NativeMethods.SetWindowDisplayAffinity(Handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
    }

    public void SetTargetRect(Rectangle captureRect)
    {
        if (Bounds != captureRect)
        {
            Bounds = captureRect;
            Invalidate();
        }
    }

    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var pen = new Pen(_paused ? Color.Orange : Color.Red, BorderThickness);
        int half = BorderThickness / 2;
        e.Graphics.DrawRectangle(pen, half, half, Width - BorderThickness - 1, Height - BorderThickness - 1);
    }
}
