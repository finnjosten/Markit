using System.Runtime.InteropServices;

namespace Markit.Services;

/// <summary>
/// Applies the extended window styles that let the toolbar float above
/// the fullscreen draw overlay, stay off the taskbar/alt-tab, and receive
/// clicks without stealing keyboard focus from the overlay.
/// </summary>
public static class WindowStyles
{
    private const int GWL_EXSTYLE = -20;

    private const int WS_EX_TOOLWINDOW = 0x00000080; // hide from taskbar / alt-tab
    private const int WS_EX_NOACTIVATE = 0x08000000; // clicking does not activate the window
    private const int WS_EX_TOPMOST = 0x00000008;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    /// <summary>Call once, from OnSourceInitialized, after the HWND exists.</summary>
    public static void MakeToolbarWindow(IntPtr hwnd)
    {
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);

        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Call once, from OnSourceInitialized, after the HWND exists.
    /// Unlike <see cref="MakeToolbarWindow"/>, deliberately omits
    /// WS_EX_NOACTIVATE — the draw overlay needs real keyboard/mouse focus
    /// since there's no other app's focus left to protect.</summary>
    public static void MakeOverlayWindow(IntPtr hwnd)
    {
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
    }

    /// <summary>Positions and sizes a window to exactly match a physical-pixel
    /// rectangle (e.g. a specific monitor's bounds). Bypasses WPF's DIP-based
    /// Left/Top/Width/Height, which are unreliable across DPI boundaries.</summary>
    public static void PositionExact(IntPtr hwnd, System.Drawing.Rectangle physicalBounds)
    {
        SetWindowPos(hwnd, HWND_TOPMOST,
            physicalBounds.X, physicalBounds.Y, physicalBounds.Width, physicalBounds.Height,
            SWP_NOACTIVATE);
    }

    /// <summary>Re-assert topmost without moving/resizing or stealing focus. Call this
    /// whenever the overlay window might have reshuffled the topmost z-order band (e.g.
    /// on every click into the overlay) to bring the toolbar back above it.</summary>
    public static void BringToTop(IntPtr hwnd)
    {
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
