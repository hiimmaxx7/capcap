using System.Threading;
using System.Windows.Forms;

namespace Capcap;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Prevent two tray icons from running at once — a second instance would
        // silently fail to grab the global hotkeys, making mode/hotkey changes on
        // the "wrong" tray icon look like they have no effect.
        using var singleInstanceMutex = new Mutex(true, "Local\\Capcap_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Capcap đang chạy rồi. Tìm icon của nó ở khay hệ thống (góc phải taskbar, có thể trong mục icon ẩn).",
                "Capcap", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Per-Monitor-V2 DPI awareness is declared in app.manifest instead of via
        // Application.SetHighDpiMode — that API doesn't exist in .NET Framework WinForms.
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApplicationContext());
    }
}
