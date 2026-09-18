using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Capcap;

/// <summary>Fullscreen click-drag overlay used to pick a rectangle region to record.</summary>
internal sealed class RegionSelectForm : Form
{
    public Rectangle? SelectedRegion { get; private set; }

    private Point _dragStart;
    private Rectangle _current;
    private bool _dragging;

    public RegionSelectForm()
    {
        var vs = SystemInformation.VirtualScreen;
        Bounds = vs;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Opacity = 0.35;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _dragging = true;
        _dragStart = e.Location;
        _current = new Rectangle(e.Location, Size.Empty);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        int x = Math.Min(_dragStart.X, e.X);
        int y = Math.Min(_dragStart.Y, e.Y);
        int w = Math.Abs(e.X - _dragStart.X);
        int h = Math.Abs(e.Y - _dragStart.Y);
        _current = new Rectangle(x, y, w, h);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        if (_current.Width > 8 && _current.Height > 8)
        {
            // Convert client (local) coords back to virtual-screen coords.
            var topLeftScreen = PointToScreen(_current.Location);
            SelectedRegion = new Rectangle(topLeftScreen, _current.Size);
            DialogResult = DialogResult.OK;
        }
        else
        {
            SelectedRegion = null;
            DialogResult = DialogResult.Cancel;
        }
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            SelectedRegion = null;
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_current.Width <= 0 || _current.Height <= 0) return;

        using var pen = new Pen(Color.Lime, 2);
        e.Graphics.DrawRectangle(pen, _current);

        using var brush = new SolidBrush(Color.FromArgb(40, Color.Lime));
        e.Graphics.FillRectangle(brush, _current);

        string label = $"{_current.Width} x {_current.Height}";
        using var font = new Font("Segoe UI", 10, FontStyle.Bold);
        var textPos = new Point(_current.Left, Math.Max(0, _current.Top - 22));
        e.Graphics.DrawString(label, font, Brushes.Lime, textPos);
    }
}
