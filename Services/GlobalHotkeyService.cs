using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ZoomItToolbar.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9000;

    [Flags]
    public enum Modifiers : uint
    {
        None = 0x0,
        Alt = 0x1,
        Control = 0x2,
        Shift = 0x4,
        Win = 0x8
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private bool _registered;

    public event Action? HotkeyPressed;

    /// <param name="hwndSource">The HwndSource of the (already created) window
    /// that should receive the WM_HOTKEY message.</param>
    public GlobalHotkeyService(HwndSource hwndSource)
    {
        _source = hwndSource;
        _source.AddHook(WndProc);
    }

    /// <param name="modifiers">Bitmask of <see cref="Modifiers"/> (matches Win32 MOD_* values,
    /// which also happen to match WPF's ModifierKeys bit values — no conversion needed).</param>
    /// <param name="virtualKey">Win32 virtual-key code, e.g. from
    /// <see cref="System.Windows.Input.KeyInterop.VirtualKeyFromKey"/>.</param>
    public bool Register(uint modifiers, uint virtualKey)
    {
        _registered = RegisterHotKey(_source.Handle, HOTKEY_ID, modifiers, virtualKey);
        return _registered;
    }

    /// <summary>Unregisters the current hotkey (if any) and registers a new one.
    /// Returns false — leaving no hotkey registered — if the combination is
    /// already claimed by another application.</summary>
    public bool ChangeHotkey(uint modifiers, uint virtualKey)
    {
        if (_registered)
            UnregisterHotKey(_source.Handle, HOTKEY_ID);
        return Register(modifiers, virtualKey);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered)
            UnregisterHotKey(_source.Handle, HOTKEY_ID);
        _source.RemoveHook(WndProc);
    }
}
