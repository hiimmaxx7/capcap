using System.Drawing;
using System.Windows.Forms;

namespace Capcap;

/// <summary>Shown right after a recording stops: keep the file, or throw it away.</summary>
internal sealed class SaveDiscardForm : Form
{
    public SaveDiscardForm(string path, long sizeBytes)
    {
        Text = "Capcap";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        ClientSize = new Size(380, 140);

        string sizeText = sizeBytes >= 1024 * 1024
            ? $"{sizeBytes / (1024.0 * 1024.0):F1} MB"
            : $"{sizeBytes / 1024.0:F0} KB";

        var label = new Label
        {
            Text = $"Đã dừng quay.\n{Path.GetFileName(path)}  ({sizeText})\n\nLưu video này hay xóa đi?",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(12, 12),
            Size = new Size(356, 70)
        };

        var saveButton = new Button
        {
            Text = "Lưu",
            DialogResult = DialogResult.OK,
            Location = new Point(70, 90),
            Size = new Size(110, 34)
        };

        var discardButton = new Button
        {
            Text = "Xóa (không lưu)",
            DialogResult = DialogResult.Cancel,
            Location = new Point(200, 90),
            Size = new Size(110, 34)
        };

        Controls.Add(label);
        Controls.Add(saveButton);
        Controls.Add(discardButton);

        AcceptButton = saveButton;
        CancelButton = discardButton;
    }
}
