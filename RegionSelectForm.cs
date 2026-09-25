using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Capcap;

/// <summary>
/// Fullscreen click-drag overlay used to pick a rectangle region to record. When a region was
/// picked before, a frame of that same size follows the cursor: a plain click reuses it right
/// there, Enter reuses it at its exact old position, and dragging draws a new one (holding Shift
/// keeps the old aspect ratio).
/// </summary>
internal sealed class RegionSelectForm : Form
{
    private const int DragThreshold = 6;

    public Rectangle? SelectedRegion { get; private set; }

    private readonly Rectangle? _previous; // virtual-screen coords
    private Point _dragStart;
    private Rectangle _current;            // client coords
    private bool _mouseDown;
    private bool _dragging;

    public RegionSelectForm(Rectangle? previousRegion = null)
    {
        _previous = previousRegion;

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

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        Invalidate();
    }

    /// <summary>The previous region's size, centered on the cursor and kept inside the cursor's monitor (client coords).</summary>
    private Rectangle? GhostRect()
    {
        if (_previous is not { } prev) return null;

        var cursor = Cursor.Position;
        var mon = Screen.FromPoint(cursor).Bounds;
        int w = Math.Min(prev.Width, mon.Width);
        int h = Math.Min(prev.Height, mon.Height);
        int x = Math.Max(mon.Left, Math.Min(mon.Right - w, cursor.X - w / 2));
        int y = Math.Max(mon.Top, Math.Min(mon.Bottom - h, cursor.Y - h / 2));
        return new Rectangle(PointToClient(new Point(x, y)), new Size(w, h));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _mouseDown = true;
        _dragging = false;
        _dragStart = e.Location;
        _current = new Rectangle(e.Location, Size.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_mouseDown && !_dragging &&
            (Math.Abs(e.X - _dragStart.X) > DragThreshold || Math.Abs(e.Y - _dragStart.Y) > DragThreshold))
        {
            _dragging = true;
        }

        if (_dragging)
        {
            int w = Math.Abs(e.X - _dragStart.X);
            int h = Math.Abs(e.Y - _dragStart.Y);

            if ((ModifierKeys & Keys.Shift) != 0 && _previous is { } prev)
            {
                double ratio = prev.Width / (double)prev.Height;
                if (w / (double)Math.Max(1, h) > ratio) w = (int)Math.Round(h * ratio);
                else h = (int)Math.Round(w / ratio);
            }

            int x = e.X >= _dragStart.X ? _dragStart.X : _dragStart.X - w;
            int y = e.Y >= _dragStart.Y ? _dragStart.Y : _dragStart.Y - h;
            _current = new Rectangle(x, y, w, h);
        }

        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_mouseDown) return;
        _mouseDown = false;

        if (_dragging)
        {
            _dragging = false;
            if (_current.Width > 8 && _current.Height > 8)
            {
                Finish(new Rectangle(PointToScreen(_current.Location), _current.Size));
            }
            else
            {
                Invalidate();
            }
            return;
        }

        // A plain click drops the remembered-size frame right where it's shown.
        if (GhostRect() is { } ghost)
        {
            Finish(new Rectangle(PointToScreen(ghost.Location), ghost.Size));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            SelectedRegion = null;
            DialogResult = DialogResult.Cancel;
            Close();
        }
        else if (e.KeyCode == Keys.Enter && _previous is { } prev)
        {
            var mon = Screen.FromRectangle(prev).Bounds;
            var clipped = Rectangle.Intersect(prev, mon);
            if (clipped.Width > 8 && clipped.Height > 8) Finish(clipped);
        }
        else if (e.KeyCode == Keys.ShiftKey)
        {
            Invalidate();
        }
    }

    private void Finish(Rectangle screenRect)
    {
        SelectedRegion = screenRect;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using var font = new Font("Segoe UI", 10, FontStyle.Bold);

        DrawHint(g);

        if (_dragging)
        {
            if (_current.Width <= 0 || _current.Height <= 0) return;
            using var pen = new Pen(Color.Lime, 2);
            using var brush = new SolidBrush(Color.FromArgb(40, Color.Lime));
            g.FillRectangle(brush, _current);
            g.DrawRectangle(pen, _current);
            DrawSizeLabel(g, font, _current, Brushes.Lime, "");
        }
        else if (GhostRect() is { } ghost)
        {
            using var pen = new Pen(Color.Gold, 2) { DashStyle = DashStyle.Dash };
            using var brush = new SolidBrush(Color.FromArgb(30, Color.Gold));
            g.FillRectangle(brush, ghost);
            g.DrawRectangle(pen, ghost);
            DrawSizeLabel(g, font, ghost, Brushes.Gold, "  - click để dùng lại");
        }
    }

    private void DrawHint(Graphics g)
    {
        string hint = _previous is { } prev
            ? $"Click: dùng lại khung {prev.Width}x{prev.Height}   ·   Enter: đúng vị trí cũ   ·   Kéo: vẽ vùng mới (giữ Shift = giữ tỉ lệ cũ)   ·   Esc: hủy"
            : "Kéo chuột để chọn vùng quay   ·   Esc: hủy";

        var mon = Screen.FromPoint(Cursor.Position).Bounds;
        var topCenter = PointToClient(new Point(mon.Left + mon.Width / 2, mon.Top + 16));

        using var font = new Font("Segoe UI", 11, FontStyle.Bold);
        var size = g.MeasureString(hint, font);
        var box = new RectangleF(topCenter.X - size.Width / 2 - 10, topCenter.Y, size.Width + 20, size.Height + 10);
        using var bg = new SolidBrush(Color.FromArgb(200, 20, 20, 20));
        g.FillRectangle(bg, box);
        g.DrawString(hint, font, Brushes.White, box.X + 10, box.Y + 5);
    }

    private static void DrawSizeLabel(Graphics g, Font font, Rectangle r, Brush brush, string suffix)
    {
        string ratio = DescribeRatio(r.Width, r.Height);
        string label = $"{r.Width} x {r.Height}{(ratio.Length > 0 ? $" ({ratio})" : "")}{suffix}";
        var pos = new Point(r.Left, r.Top >= 22 ? r.Top - 22 : r.Top + 4);
        g.DrawString(label, font, brush, pos);
    }

    private static string DescribeRatio(int w, int h)
    {
        if (w <= 0 || h <= 0) return "";
        (int A, int B)[] known = { (16, 9), (9, 16), (4, 3), (3, 4), (1, 1), (21, 9), (4, 5) };
        double r = w / (double)h;
        foreach (var (a, b) in known)
        {
            if (Math.Abs(r - a / (double)b) / (a / (double)b) < 0.01) return $"{a}:{b}";
        }
        return "";
    }
}
