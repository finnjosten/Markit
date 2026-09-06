using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Markit.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Markit;

public partial class MainWindow : Window
{
    private enum ToolMode { Pen, Highlighter, Eraser }

    private readonly AppSettings _settings = AppSettings.Load();

    private GlobalHotkeyService? _hotkeyService;
    private GlobalHotkeyService? _resumeHotkeyService;
    private IntPtr _hwnd;
    private DrawOverlayWindow? _activeOverlay;
    private string? _activeMonitorDeviceName;
    private System.Windows.Point? _dragStartPoint;

    private ToolMode _activeTool = ToolMode.Pen;
    private Color _penColor;
    private double _penWidth;
    private Color _highlighterColor;
    private double _highlighterWidth;
    private double _eraserWidth;

    public MainWindow()
    {
        InitializeComponent();

        _penColor = ParseColorOrDefault(_settings.LastPenColor, "#BF5AF2");
        _penWidth = _settings.LastPenWidth;
        _highlighterColor = ParseColorOrDefault(_settings.LastHighlighterColor, "#FFD54F");
        _highlighterWidth = _settings.LastHighlighterWidth;
        _eraserWidth = _settings.LastEraserWidth;

        PenTipIndicator.Background = new SolidColorBrush(_penColor);
        HighlighterTipIndicator.Background = new SolidColorBrush(_highlighterColor);

        // Set (not XAML) so this fires Checked -> SelectTool(Pen) only after the whole
        // tree — including PalettePanel/ThicknessSlider, declared later — actually exists.
        PenToolButton.IsChecked = true;
    }

    private static Color ParseColorOrDefault(string hex, string fallbackHex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex)!;
        }
        catch
        {
            return (Color)ColorConverter.ConvertFromString(fallbackHex)!;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)
                     ?? throw new InvalidOperationException("HwndSource not available.");
        _hwnd = source.Handle;

        // Apply topmost / tool-window / no-activate styles before first show.
        WindowStyles.MakeToolbarWindow(_hwnd);

        _hotkeyService = new GlobalHotkeyService(source, hotkeyId: 9000);
        bool ok = _hotkeyService.Register(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey);
        if (!ok)
        {
            System.Windows.MessageBox.Show(
                "Could not register the global hotkey. Another app may already be using it. " +
                "Open Settings from the tray icon to choose a different one.",
                "Markit", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

        _resumeHotkeyService = new GlobalHotkeyService(source, hotkeyId: 9001);
        bool resumeOk = _resumeHotkeyService.Register(_settings.ResumeHotkeyModifiers, _settings.ResumeHotkeyVirtualKey);
        if (!resumeOk)
        {
            System.Windows.MessageBox.Show(
                "Could not register the \"resume last edits\" hotkey. Another app may already be using it. " +
                "Open Settings from the tray icon to choose a different one.",
                "Markit", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _resumeHotkeyService.HotkeyPressed += OnResumeHotkeyPressed;

        PositionTopCenter();
        Hide(); // start hidden until the hotkey is pressed
    }

    private void PositionTopCenter()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + 24;
    }

    private void PositionOverMonitor(System.Drawing.Rectangle physicalBounds)
    {
        double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double left = physicalBounds.X / dpiScale;
        double top = physicalBounds.Y / dpiScale;
        double widthDip = physicalBounds.Width / dpiScale;

        Left = left + (widthDip - Width) / 2;
        Top = top + 24;
    }

    private void OnHotkeyPressed()
    {
        // Runs on the UI thread already, since WM_HOTKEY arrives via the
        // window's own message loop.
        if (_activeOverlay == null)
            StartDrawSession(restoreLastEdits: false);
        else
            EndDrawSession();
    }

    private void OnResumeHotkeyPressed()
    {
        if (_activeOverlay == null)
            StartDrawSession(restoreLastEdits: true);
        else
            EndDrawSession();
    }

    /// <summary>Also called from the tray icon's "Toggle draw mode" menu item.</summary>
    public void ToggleDrawSession() => OnHotkeyPressed();

    /// <summary>Opens the settings window, letting the user change either global hotkey.</summary>
    public void OpenSettings()
    {
        var window = new SettingsWindow(
            _settings.HotkeyModifiers, _settings.HotkeyVirtualKey, TryApplyHotkey,
            _settings.ResumeHotkeyModifiers, _settings.ResumeHotkeyVirtualKey, TryApplyResumeHotkey,
            _settings.SaveFolder, ApplySaveFolder)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void ApplySaveFolder(string folder)
    {
        _settings.SaveFolder = folder;
        _settings.Save();
    }

    private bool TryApplyHotkey(uint modifiers, uint virtualKey)
    {
        if (_hotkeyService == null || !_hotkeyService.ChangeHotkey(modifiers, virtualKey))
            return false;

        _settings.HotkeyModifiers = modifiers;
        _settings.HotkeyVirtualKey = virtualKey;
        _settings.Save();
        return true;
    }

    private bool TryApplyResumeHotkey(uint modifiers, uint virtualKey)
    {
        if (_resumeHotkeyService == null || !_resumeHotkeyService.ChangeHotkey(modifiers, virtualKey))
            return false;

        _settings.ResumeHotkeyModifiers = modifiers;
        _settings.ResumeHotkeyVirtualKey = virtualKey;
        _settings.Save();
        return true;
    }

    /// <summary>Flushes the last-used pen/highlighter color+width to disk. Called on app
    /// exit as a safety net, in case the thickness slider was moved but no draw session
    /// has ended since (which is the other point this gets saved).</summary>
    public void FlushSettings() => PersistPenSettings();

    private void PersistPenSettings()
    {
        _settings.LastPenColor = ToHex(_penColor);
        _settings.LastPenWidth = _penWidth;
        _settings.LastHighlighterColor = ToHex(_highlighterColor);
        _settings.LastHighlighterWidth = _highlighterWidth;
        _settings.LastEraserWidth = _eraserWidth;
        _settings.Save();
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private void StartDrawSession(bool restoreLastEdits)
    {
        // Make sure the toolbar itself isn't part of the screenshot.
        Hide();

        var snapshot = MonitorCapture.CaptureMonitorUnderCursor();
        _activeMonitorDeviceName = snapshot.DeviceName;

        _activeOverlay = new DrawOverlayWindow(snapshot);
        _activeOverlay.ExitRequested += OnOverlayExitRequested;
        _activeOverlay.UserInteracted += ReassertToolbarTopmost;
        _activeOverlay.ToolSelected += OnRadialToolSelected;
        _activeOverlay.SaveRequested += OnRadialSaveRequested;
        _activeOverlay.Show();
        ApplyActiveToolToOverlay();

        if (restoreLastEdits && InkStore.Load(snapshot.DeviceName) is { } strokes)
            _activeOverlay.LoadStrokes(strokes);

        PositionOverMonitor(snapshot.PhysicalBounds);
        Show();
        ReassertToolbarTopmost();
    }

    private void OnOverlayExitRequested() => EndDrawSession();

    private void ReassertToolbarTopmost()
    {
        if (_hwnd != IntPtr.Zero)
            WindowStyles.BringToTop(_hwnd);
    }

    private void EndDrawSession()
    {
        if (_activeOverlay != null)
        {
            if (_activeMonitorDeviceName != null)
                InkStore.Save(_activeMonitorDeviceName, _activeOverlay.GetStrokes());

            _activeOverlay.ExitRequested -= OnOverlayExitRequested;
            _activeOverlay.UserInteracted -= ReassertToolbarTopmost;
            _activeOverlay.ToolSelected -= OnRadialToolSelected;
            _activeOverlay.SaveRequested -= OnRadialSaveRequested;
            _activeOverlay.Close();
            _activeOverlay = null;
            _activeMonitorDeviceName = null;
        }

        PersistPenSettings();
        Hide();
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        ((UIElement)sender).CaptureMouse();
    }

    private void Border_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStartPoint is not { } start || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = e.GetPosition(this);
        Left += current.X - start.X;
        Top += current.Y - start.Y;
    }

    private void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = null;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    /// <summary>Selects the active tool (mutually exclusive with the other two —
    /// enforced by the RadioButtons sharing GroupName="Tool" in the XAML) and pushes
    /// its color/width/eraser state to the overlay.</summary>
    private void SelectTool(ToolMode mode)
    {
        _activeTool = mode;
        ColorGrid.Visibility = mode == ToolMode.Eraser ? Visibility.Collapsed : Visibility.Visible;

        switch (mode)
        {
            case ToolMode.Pen:
                ThicknessSlider.Value = _penWidth;
                break;
            case ToolMode.Highlighter:
                ThicknessSlider.Value = _highlighterWidth;
                break;
            case ToolMode.Eraser:
                ThicknessSlider.Value = _eraserWidth;
                break;
        }

        ApplyActiveToolToOverlay();
    }

    private void ApplyActiveToolToOverlay()
    {
        switch (_activeTool)
        {
            case ToolMode.Pen:
                _activeOverlay?.SetEraser(false);
                _activeOverlay?.SetPen(_penColor, highlighter: false, _penWidth);
                break;
            case ToolMode.Highlighter:
                _activeOverlay?.SetEraser(false);
                _activeOverlay?.SetPen(_highlighterColor, highlighter: true, _highlighterWidth);
                break;
            case ToolMode.Eraser:
                _activeOverlay?.SetEraser(true, _eraserWidth);
                break;
        }

        // Keep the radial menu's icons showing the real current color of each
        // tool, independent of which one happens to be active right now.
        _activeOverlay?.SetToolColors(_penColor, _highlighterColor);

        ReclaimOverlayFocus();
    }

    private void OnRadialToolSelected(RadialTool tool)
    {
        // Setting IsChecked fires the matching *_Checked handler, which calls
        // SelectTool — same single code path as clicking the toolbar's own buttons.
        switch (tool)
        {
            case RadialTool.Pen: PenToolButton.IsChecked = true; break;
            case RadialTool.Highlighter: HighlighterToolButton.IsChecked = true; break;
            case RadialTool.Eraser: EraserToolButton.IsChecked = true; break;
        }
    }

    /// <summary>The toolbar has several focusable controls (the slider especially) —
    /// clicking/dragging one can pull Win32 keyboard focus onto the toolbar's window
    /// instead of the overlay, silently breaking Esc/R/G/B/.../Ctrl+Z there. Reclaiming
    /// focus this way can itself bump the overlay back above the toolbar in the topmost
    /// z-order band, so re-assert the toolbar's position right after.</summary>
    private void ReclaimOverlayFocus()
    {
        _activeOverlay?.Focus();
        ReassertToolbarTopmost();
    }

    private void PenToolButton_Checked(object sender, RoutedEventArgs e) => SelectTool(ToolMode.Pen);
    private void HighlighterToolButton_Checked(object sender, RoutedEventArgs e) => SelectTool(ToolMode.Highlighter);
    private void EraserToolButton_Checked(object sender, RoutedEventArgs e) => SelectTool(ToolMode.Eraser);

    private void PaletteColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Background: SolidColorBrush brush })
            return;

        if (_activeTool == ToolMode.Highlighter)
        {
            _highlighterColor = brush.Color;
            HighlighterTipIndicator.Background = brush;
        }
        else
        {
            _penColor = brush.Color;
            PenTipIndicator.Background = brush;
        }

        ApplyActiveToolToOverlay();
        PersistPenSettings();
    }

    private void ThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        switch (_activeTool)
        {
            case ToolMode.Highlighter: _highlighterWidth = e.NewValue; break;
            case ToolMode.Eraser: _eraserWidth = e.NewValue; break;
            default: _penWidth = e.NewValue; break;
        }

        ApplyActiveToolToOverlay();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        _activeOverlay?.Undo();
        ReclaimOverlayFocus();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _activeOverlay?.ClearAll();
        ReclaimOverlayFocus();
    }

    private void OnRadialSaveRequested()
    {
        _activeOverlay?.SaveAndCopy(_settings.SaveFolder);
        ReclaimOverlayFocus();
    }

    private void SaveAndCopy_Click(object sender, RoutedEventArgs e)
    {
        _activeOverlay?.SaveAndCopy(_settings.SaveFolder);
        ReclaimOverlayFocus();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => EndDrawSession();

    protected override void OnClosed(EventArgs e)
    {
        _hotkeyService?.Dispose();
        _resumeHotkeyService?.Dispose();
        base.OnClosed(e);
    }
}
