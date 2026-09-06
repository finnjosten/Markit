using System.Windows;
using System.Windows.Input;
using ZoomItToolbar.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ZoomItToolbar;

public partial class SettingsWindow : Window
{
    private readonly Func<uint, uint, bool> _tryApplyHotkey;
    private uint _modifiers;
    private uint _virtualKey;
    private bool _capturing;

    public SettingsWindow(uint modifiers, uint virtualKey, Func<uint, uint, bool> tryApplyHotkey)
    {
        InitializeComponent();
        _modifiers = modifiers;
        _virtualKey = virtualKey;
        _tryApplyHotkey = tryApplyHotkey;
        UpdateButtonText();
    }

    private void UpdateButtonText()
    {
        HotkeyButton.Content = _capturing ? "Press a key combination..." : FormatHotkey(_modifiers, _virtualKey);
    }

    private void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        UpdateButtonText();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return; // still waiting for a non-modifier key
        }

        _capturing = false;

        if (key == Key.Escape)
        {
            UpdateButtonText(); // cancel — revert to the current combination
            return;
        }

        uint modifiers = (uint)Keyboard.Modifiers;
        uint virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);

        if (modifiers == 0)
        {
            System.Windows.MessageBox.Show(this,
                "Choose a combination that includes at least one modifier key (Ctrl/Alt/Shift/Win).",
                "ZoomIt Toolbar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else if (_tryApplyHotkey(modifiers, virtualKey))
        {
            _modifiers = modifiers;
            _virtualKey = virtualKey;
        }
        else
        {
            System.Windows.MessageBox.Show(this,
                "That combination is already in use by another application.",
                "ZoomIt Toolbar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        UpdateButtonText();
    }

    private static string FormatHotkey(uint modifiers, uint virtualKey)
    {
        var parts = new List<string>();
        if ((modifiers & (uint)GlobalHotkeyService.Modifiers.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & (uint)GlobalHotkeyService.Modifiers.Alt) != 0) parts.Add("Alt");
        if ((modifiers & (uint)GlobalHotkeyService.Modifiers.Shift) != 0) parts.Add("Shift");
        if ((modifiers & (uint)GlobalHotkeyService.Modifiers.Win) != 0) parts.Add("Win");

        parts.Add(KeyInterop.KeyFromVirtualKey((int)virtualKey).ToString());
        return string.Join("+", parts);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
