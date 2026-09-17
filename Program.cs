using System.Threading;
using System.Windows.Forms;

namespace Oculus;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Prevent two tray icons from running at once — a second instance would
        // silently fail to grab the global hotkeys, making mode/hotkey changes on
        // the "wrong" tray icon look like they have no effect.
        using var singleInstanceMutex = new Mutex(true, "Local\\ProjectOculus_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Project Oculus đang chạy rồi. Tìm icon của nó ở khay hệ thống (góc phải taskbar, có thể trong mục icon ẩn).",
                "Project Oculus", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApplicationContext());
    }
}
