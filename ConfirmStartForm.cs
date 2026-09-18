using System.Drawing;
using System.Windows.Forms;

namespace Capcap;

/// <summary>Small "start recording this region?" confirmation shown right after a drag-select.</summary>
internal sealed class ConfirmStartForm : Form
{
    public ConfirmStartForm(Rectangle region)
    {
        Text = "Capcap";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(320, 130);

        var label = new Label
        {
            Text = $"Vùng đã chọn: {region.Width} x {region.Height}px\nBắt đầu quay? (đếm ngược 3 giây)",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(12, 12),
            Size = new Size(296, 60)
        };

        var startButton = new Button
        {
            Text = "Bắt đầu quay",
            DialogResult = DialogResult.OK,
            Location = new Point(48, 82),
            Size = new Size(110, 32)
        };

        var cancelButton = new Button
        {
            Text = "Hủy",
            DialogResult = DialogResult.Cancel,
            Location = new Point(166, 82),
            Size = new Size(110, 32)
        };

        Controls.Add(label);
        Controls.Add(startButton);
        Controls.Add(cancelButton);

        AcceptButton = startButton;
        CancelButton = cancelButton;
    }
}
