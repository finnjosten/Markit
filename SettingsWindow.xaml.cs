using System.Windows;
using System.Windows.Input;
using Markit.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Markit;

public partial class SettingsWindow : Window
{
    private enum HotkeySlot { None, Start, Resume }

    private readonly Func<uint, uint, bool> _tryApplyStart;
    private readonly Func<uint, uint, bool> _tryApplyResume;

    private readonly Action<string> _applySaveFolder;

    private uint _startModifiers;
    private uint _startVirtualKey;
    private uint _resumeModifiers;
    private uint _resumeVirtualKey;
    private string _saveFolder;
    private HotkeySlot _capturing = HotkeySlot.None;

    public SettingsWindow(
        uint startModifiers, uint startVirtualKey, Func<uint, uint, bool> tryApplyStart,
        uint resumeModifiers, uint resumeVirtualKey, Func<uint, uint, bool> tryApplyResume,
        string saveFolder, Action<string> applySaveFolder)
    {
        InitializeComponent();

        _startModifiers = startModifiers;
        _startVirtualKey = startVirtualKey;
        _tryApplyStart = tryApplyStart;

        _resumeModifiers = resumeModifiers;
        _resumeVirtualKey = resumeVirtualKey;
        _tryApplyResume = tryApplyResume;

        _saveFolder = saveFolder;
        _applySaveFolder = applySaveFolder;
        SaveFolderTextBox.Text = _saveFolder;

        UpdateButtonText(HotkeySlot.Start);
        UpdateButtonText(HotkeySlot.Resume);
    }

    private void BrowseSaveFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            SelectedPath = _saveFolder,
            Description = "Choose where screenshots are saved"
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        _saveFolder = dialog.SelectedPath;
        SaveFolderTextBox.Text = _saveFolder;
        _applySaveFolder(_saveFolder);
    }

    private void UpdateButtonText(HotkeySlot slot)
    {
        bool capturing = _capturing == slot;
        if (slot == HotkeySlot.Start)
            StartHotkeyButton.Content = capturing ? "Press a key combination..." : FormatHotkey(_startModifiers, _startVirtualKey);
        else
            ResumeHotkeyButton.Content = capturing ? "Press a key combination..." : FormatHotkey(_resumeModifiers, _resumeVirtualKey);
    }

    private void StartHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        _capturing = HotkeySlot.Start;
        UpdateButtonText(HotkeySlot.Start);
    }

    private void ResumeHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        _capturing = HotkeySlot.Resume;
        UpdateButtonText(HotkeySlot.Resume);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturing == HotkeySlot.None)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return; // still waiting for a non-modifier key
        }

        var slot = _capturing;
        _capturing = HotkeySlot.None;

        if (key == Key.Escape)
        {
            UpdateButtonText(slot); // cancel — revert to the current combination
            return;
        }

        uint modifiers = (uint)Keyboard.Modifiers;
        uint virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);

        if (modifiers == 0)
        {
            System.Windows.MessageBox.Show(this,
                "Choose a combination that includes at least one modifier key (Ctrl/Alt/Shift/Win).",
                "Markit", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            bool ok = slot == HotkeySlot.Start
                ? _tryApplyStart(modifiers, virtualKey)
                : _tryApplyResume(modifiers, virtualKey);

            if (ok)
            {
                if (slot == HotkeySlot.Start) { _startModifiers = modifiers; _startVirtualKey = virtualKey; }
                else { _resumeModifiers = modifiers; _resumeVirtualKey = virtualKey; }
            }
            else
            {
                System.Windows.MessageBox.Show(this,
                    "That combination is already in use by another application.",
                    "Markit", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        UpdateButtonText(slot);
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
