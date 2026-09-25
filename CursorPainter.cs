using System.Drawing;
using System.Runtime.InteropServices;

namespace Capcap;

/// <summary>
/// Draws the real, currently-displayed system cursor onto a captured frame: correct
/// shape (custom/animated cursors included) AND correct on-screen size. Passing 0,0 to
/// DrawIconEx draws the cursor at its native resource size (often 32x32), which ignores
/// both the monitor's DPI scale and the user's "pointer size" accessibility setting —
/// that's why the recorded cursor used to look smaller/blockier than what's really on
/// screen. The DPI-corrected target size is computed ONCE per recording (it can't change
/// mid-session on a given monitor) rather than re-queried every frame, since the extra
/// MonitorFromPoint/GetDpiForMonitor calls turned out to add real per-frame overhead.
/// </summary>
internal static class CursorPainter
{
    /// <summary>Computes the on-screen cursor size to draw at, once per recording session.</summary>
    public static Size ComputeTargetCursorSize(Point atScreenPoint, double extraScale)
    {
        var monitor = NativeMethods.MonitorFromPoint(
            new NativeMethods.POINT { X = atScreenPoint.X, Y = atScreenPoint.Y },
            NativeMethods.MONITOR_DEFAULTTONEAREST);

        uint dpiX = 96, dpiY = 96;
        if (monitor != IntPtr.Zero)
        {
            NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);
        }

        int w = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXCURSOR, dpiX);
        int h = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CYCURSOR, dpiY);
        if (w <= 0) w = 32;
        if (h <= 0) h = 32;

        w = Math.Max(1, (int)Math.Round(w * extraScale));
        h = Math.Max(1, (int)Math.Round(h * extraScale));
        return new Size(w, h);
    }

    /// <summary>A system cursor loaded straight from its .cur file at one of the sizes stored
    /// in it, so it can be drawn 1:1 instead of stretched.</summary>
    private sealed class SharpCursor
    {
        public IntPtr Handle;
        public int Width, Height, HotspotX, HotspotY;
    }

    // Standard cursor IDs (IDC_*) and their value names under HKCU\Control Panel\Cursors.
    private static readonly (int Id, string RegName)[] SystemCursors =
    {
        (32512, "Arrow"), (32513, "IBeam"), (32514, "Wait"), (32515, "Crosshair"), (32516, "UpArrow"),
        (32642, "SizeNWSE"), (32643, "SizeNESW"), (32644, "SizeWE"), (32645, "SizeNS"), (32646, "SizeAll"),
        (32648, "No"), (32649, "Hand"), (32650, "AppStarting"), (32651, "Help"), (32671, "Pin"), (32672, "Person")
    };

    private static Dictionary<IntPtr, SharpCursor>? _sharpCursors;

    /// <summary>
    /// Loads high-resolution copies of the standard system cursors for this recording. The
    /// live cursor handle only holds one small bitmap (e.g. 48px), and stretching that to the
    /// 1.5x-3x record size is what made the recorded cursor look jagged/blocky. The cursor
    /// scheme's .cur files, though, carry several sizes (Windows' pointer-size files have
    /// 48/72/96/144/192px), so pick the stored size closest to the target and draw it 1:1 —
    /// as sharp as the real on-screen cursor. Cursors apps create themselves aren't in the
    /// scheme and still fall back to stretching.
    /// </summary>
    public static void PrepareSharpCursors(Size targetSize)
    {
        ReleaseSharpCursors();
        var map = new Dictionary<IntPtr, SharpCursor>();
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors");
            foreach (var (id, regName) in SystemCursors)
            {
                IntPtr shared = NativeMethods.LoadCursor(IntPtr.Zero, new IntPtr(id));
                if (shared == IntPtr.Zero || map.ContainsKey(shared)) continue;

                string file = Environment.ExpandEnvironmentVariables(key?.GetValue(regName) as string ?? "");
                if (file.Length == 0 || !File.Exists(file)) continue;

                var size = PickStoredSize(file, targetSize);
                IntPtr h = NativeMethods.LoadImage(IntPtr.Zero, file, NativeMethods.IMAGE_CURSOR,
                    size.Width, size.Height, NativeMethods.LR_LOADFROMFILE);
                if (h == IntPtr.Zero) continue;

                var sharp = new SharpCursor { Handle = h, Width = size.Width, Height = size.Height };
                if (NativeMethods.GetIconInfo(h, out var info))
                {
                    sharp.HotspotX = info.xHotspot;
                    sharp.HotspotY = info.yHotspot;
                    if (info.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(info.hbmMask);
                    if (info.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(info.hbmColor);
                }
                map[shared] = sharp;
            }
        }
        catch
        {
            // Anything odd about the cursor scheme just means stretched drawing, as before.
        }
        _sharpCursors = map;
    }

    public static void ReleaseSharpCursors()
    {
        var map = _sharpCursors;
        _sharpCursors = null;
        if (map is null) return;
        foreach (var c in map.Values) NativeMethods.DestroyCursor(c.Handle);
    }

    /// <summary>The image size stored in a .cur file that's closest to <paramref name="target"/>
    /// (larger wins a tie). Animated .ani files etc. just get the target size.</summary>
    private static Size PickStoredSize(string curFile, Size target)
    {
        try
        {
            using var br = new BinaryReader(File.OpenRead(curFile));
            if (br.ReadUInt16() != 0 || br.ReadUInt16() != 2) return target; // not an ICONDIR of type cursor
            int count = br.ReadUInt16();
            Size best = target;
            int bestDiff = int.MaxValue;
            for (int i = 0; i < count; i++)
            {
                int w = br.ReadByte(), h = br.ReadByte();
                br.ReadBytes(14);
                if (w == 0) w = 256;
                if (h == 0) h = 256;
                int diff = Math.Abs(w - target.Width);
                if (diff < bestDiff || (diff == bestDiff && w > best.Width))
                {
                    bestDiff = diff;
                    best = new Size(w, h);
                }
            }
            return best;
        }
        catch
        {
            return target;
        }
    }

    /// <param name="g">Graphics of the destination frame bitmap.</param>
    /// <param name="frameOrigin">Top-left of the captured region, in virtual-screen coordinates.</param>
    /// <param name="targetSize">Cursor draw size, from <see cref="ComputeTargetCursorSize"/>.</param>
    public static void DrawCursorOnFrame(Graphics g, Point frameOrigin, Size targetSize)
    {
        var ci = new NativeMethods.CURSORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.CURSORINFO>()
        };

        if (!NativeMethods.GetCursorInfo(out ci)) return;
        if ((ci.flags & NativeMethods.CURSOR_SHOWING) == 0) return;
        if (ci.hCursor == IntPtr.Zero) return;

        if (_sharpCursors is { } sharpMap && sharpMap.TryGetValue(ci.hCursor, out var sharp))
        {
            IntPtr sharpHdc = g.GetHdc();
            try
            {
                NativeMethods.DrawIconEx(sharpHdc,
                    ci.ptScreenPos.X - sharp.HotspotX - frameOrigin.X,
                    ci.ptScreenPos.Y - sharp.HotspotY - frameOrigin.Y,
                    sharp.Handle, sharp.Width, sharp.Height, 0, IntPtr.Zero, NativeMethods.DI_NORMAL);
            }
            finally
            {
                g.ReleaseHdc(sharpHdc);
            }
            return;
        }

        int hotspotX = 0, hotspotY = 0;
        int nativeW = 32, nativeH = 32;

        if (NativeMethods.GetIconInfo(ci.hCursor, out var iconInfo))
        {
            hotspotX = iconInfo.xHotspot;
            hotspotY = iconInfo.yHotspot;

            var bmp = new NativeMethods.BITMAP();
            if (iconInfo.hbmColor != IntPtr.Zero &&
                NativeMethods.GetObject(iconInfo.hbmColor, Marshal.SizeOf<NativeMethods.BITMAP>(), ref bmp) != 0 &&
                bmp.bmWidth > 0 && bmp.bmHeight > 0)
            {
                nativeW = bmp.bmWidth;
                nativeH = bmp.bmHeight;
            }
            else if (iconInfo.hbmMask != IntPtr.Zero &&
                     NativeMethods.GetObject(iconInfo.hbmMask, Marshal.SizeOf<NativeMethods.BITMAP>(), ref bmp) != 0 &&
                     bmp.bmWidth > 0 && bmp.bmHeight > 0)
            {
                nativeW = bmp.bmWidth;
                nativeH = bmp.bmHeight / 2; // legacy mono cursors stack AND+XOR masks vertically
            }

            // GetIconInfo allocates copies of the mask/color bitmaps; free them once we're done reading.
            if (iconInfo.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(iconInfo.hbmMask);
            if (iconInfo.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(iconInfo.hbmColor);
        }

        double scaleX = (double)targetSize.Width / nativeW;
        double scaleY = (double)targetSize.Height / nativeH;
        int scaledHotspotX = (int)Math.Round(hotspotX * scaleX);
        int scaledHotspotY = (int)Math.Round(hotspotY * scaleY);

        int drawX = ci.ptScreenPos.X - scaledHotspotX - frameOrigin.X;
        int drawY = ci.ptScreenPos.Y - scaledHotspotY - frameOrigin.Y;

        IntPtr hdc = g.GetHdc();
        try
        {
            NativeMethods.DrawIconEx(hdc, drawX, drawY, ci.hCursor, targetSize.Width, targetSize.Height, 0, IntPtr.Zero, NativeMethods.DI_NORMAL);
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }
    }
}
