using System.Drawing;
using System.Globalization;

namespace Capcap;

/// <summary>
/// Tiny key=value settings file in %AppData%\Capcap\settings.ini. Remembers the last
/// drag-selected region (so a recurring size like a 608x1080 9:16 strip doesn't have to be
/// re-drawn every session) and the "record mouse track" toggle. Keys are preserved across saves.
/// </summary>
internal static class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Capcap", "settings.ini");

    private static Dictionary<string, string> ReadAll()
    {
        var d = new Dictionary<string, string>();
        try
        {
            if (!File.Exists(FilePath)) return d;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
        }
        catch
        {
            // Unreadable/corrupt settings just mean "nothing remembered yet".
        }
        return d;
    }

    private static void WriteAll(Dictionary<string, string> d)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllLines(FilePath, d.Select(kv => kv.Key + "=" + kv.Value));
        }
        catch
        {
            // Not being able to persist settings shouldn't block recording.
        }
    }

    public static Rectangle? LoadLastRegion()
    {
        if (!ReadAll().TryGetValue("LastRegion", out var value)) return null;
        var parts = value.Split(',');
        if (parts.Length != 4) return null;
        var v = new int[4];
        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v[i])) return null;
        }
        if (v[2] <= 8 || v[3] <= 8) return null;
        return new Rectangle(v[0], v[1], v[2], v[3]);
    }

    public static void SaveLastRegion(Rectangle r)
    {
        var d = ReadAll();
        d["LastRegion"] = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", r.X, r.Y, r.Width, r.Height);
        WriteAll(d);
    }

    /// <summary>Whether to write the .mouse.json sidecar next to each recording. Defaults to ON.</summary>
    public static bool LoadRecordMouse() => !ReadAll().TryGetValue("RecordMouse", out var v) || v != "0";

    public static void SaveRecordMouse(bool on)
    {
        var d = ReadAll();
        d["RecordMouse"] = on ? "1" : "0";
        WriteAll(d);
    }
}
