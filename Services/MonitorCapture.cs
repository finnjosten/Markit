using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Media.Imaging;

namespace Markit.Services;

/// <summary>A single freeze-frame of one monitor. <paramref name="DeviceName"/>
/// (e.g. "\\.\DISPLAY1") identifies the physical monitor for per-display ink
/// persistence — stable across sessions as long as monitor arrangement doesn't change.</summary>
public sealed record MonitorSnapshot(BitmapSource Image, Rectangle PhysicalBounds, string DeviceName);

/// <summary>
/// Captures a still image of whichever monitor the mouse cursor is
/// currently on, in physical pixels (correct across mixed-DPI setups).
/// </summary>
public static class MonitorCapture
{
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public static MonitorSnapshot CaptureMonitorUnderCursor()
    {
        var screen = Screen.FromPoint(Cursor.Position);
        var bounds = screen.Bounds;

        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }

        IntPtr hBitmap = bitmap.GetHbitmap();
        try
        {
            var image = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return new MonitorSnapshot(image, bounds, screen.DeviceName);
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }
}
