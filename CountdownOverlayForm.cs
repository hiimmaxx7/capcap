using System.Drawing;
using System.Windows.Forms;

namespace Capcap;

/// <summary>Big centered "3, 2, 1" shown over the target region before recording actually starts.
/// It always closes before Recorder.Start() is called, so it never ends up in the video.</summary>
internal sealed class CountdownOverlayForm : Form
{
    private readonly Label _label;

    public CountdownOverlayForm(Rectangle targetRegion, int startCount)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        Opacity = 0.75;
        Size = new Size(160, 160);
        Location = new Point(
            targetRegion.X + targetRegion.Width / 2 - Width / 2,
            targetRegion.Y + targetRegion.Height / 2 - Height / 2);

        _label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 56, FontStyle.Bold)
        };
        Controls.Add(_label);
        SetCount(startCount);
    }

    protected override bool ShowWithoutActivation => true;

    public void SetCount(int n) => _label.Text = n.ToString();
}
