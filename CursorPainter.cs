using System.Drawing;
using System.Runtime.InteropServices;

namespace Oculus;

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
