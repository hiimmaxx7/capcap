using System.Drawing;
using System.Globalization;

namespace Capcap;

/// <summary>
/// Tiny key=value settings file in %AppData%\Capcap\settings.ini. Currently only remembers the
/// last drag-selected region, so a recurring size (e.g. a 608x1080 9:16 strip) doesn't have to
/// be re-drawn by hand every session.
/// </summary>
internal static class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Capcap", "settings.ini");

    public static Rectangle? LoadLastRegion()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0 || line.Substring(0, eq).Trim() != "LastRegion") continue;

                var parts = line.Substring(eq + 1).Split(',');
                if (parts.Length != 4) return null;
                var v = new int[4];
                for (int i = 0; i < 4; i++)
                {
                    if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v[i])) return null;
                }
                if (v[2] <= 8 || v[3] <= 8) return null;
                return new Rectangle(v[0], v[1], v[2], v[3]);
            }
        }
        catch
        {
            // Unreadable/corrupt settings just mean "nothing remembered yet".
        }
        return null;
    }

    public static void SaveLastRegion(Rectangle r)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, string.Format(CultureInfo.InvariantCulture,
                "LastRegion={0},{1},{2},{3}\r\n", r.X, r.Y, r.Width, r.Height));
        }
        catch
        {
            // Not being able to persist the region shouldn't block recording.
        }
    }
}
