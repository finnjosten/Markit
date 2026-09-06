using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ZoomItToolbar.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace ZoomItToolbar;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();

    private GlobalHotkeyService? _hotkeyService;
    private IntPtr _hwnd;
    private DrawOverlayWindow? _activeOverlay;
    private System.Windows.Point? _dragStartPoint;

    private Color _currentColor;
    private double _currentWidth;

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            _currentColor = (Color)ColorConverter.ConvertFromString(_settings.LastColor)!;
        }
        catch
        {
            _currentColor = (Color)ColorConverter.ConvertFromString("#FF4B4B")!;
        }
        _currentWidth = _settings.LastWidth;

        PenSwatch.Background = new SolidColorBrush(_currentColor);
        ThicknessSlider.Value = _currentWidth;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)
                     ?? throw new InvalidOperationException("HwndSource not available.");
        _hwnd = source.Handle;

        // Apply topmost / tool-window / no-activate styles before first show.
        WindowStyles.MakeToolbarWindow(_hwnd);

        _hotkeyService = new GlobalHotkeyService(source);
        bool ok = _hotkeyService.Register(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey);
        if (!ok)
        {
            System.Windows.MessageBox.Show(
                "Could not register the global hotkey. Another app may already be using it. " +
                "Open Settings from the tray icon to choose a different one.",
                "ZoomIt Toolbar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

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
            StartDrawSession();
        else
            EndDrawSession();
    }

    /// <summary>Also called from the tray icon's "Toggle draw mode" menu item.</summary>
    public void ToggleDrawSession() => OnHotkeyPressed();

    /// <summary>Opens the settings window, letting the user change the global hotkey.</summary>
    public void OpenSettings()
    {
        var window = new SettingsWindow(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey, TryApplyHotkey)
        {
            Owner = this
        };
        window.ShowDialog();
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

    /// <summary>Flushes the last-used pen color/width to disk. Called on app exit
    /// as a safety net, in case the thickness slider was moved but no draw
    /// session has ended since (which is the other point this gets saved).</summary>
    public void FlushSettings() => PersistPenSettings();

    private void PersistPenSettings()
    {
        _settings.LastColor = $"#{_currentColor.R:X2}{_currentColor.G:X2}{_currentColor.B:X2}";
        _settings.LastWidth = _currentWidth;
        _settings.Save();
    }

    private void StartDrawSession()
    {
        // Make sure the toolbar itself isn't part of the screenshot.
        Hide();

        var snapshot = MonitorCapture.CaptureMonitorUnderCursor();

        _activeOverlay = new DrawOverlayWindow(snapshot);
        _activeOverlay.ExitRequested += OnOverlayExitRequested;
        _activeOverlay.UserInteracted += ReassertToolbarTopmost;
        _activeOverlay.Show();
        _activeOverlay.SetPen(_currentColor, Highlighter, _currentWidth);

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
            _activeOverlay.ExitRequested -= OnOverlayExitRequested;
            _activeOverlay.UserInteracted -= ReassertToolbarTopmost;
            _activeOverlay.Close();
            _activeOverlay = null;
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

    private bool Highlighter => HighlighterToggle.IsChecked == true;

    private void ApplyPen() => _activeOverlay?.SetPen(_currentColor, Highlighter, _currentWidth);

    private void PenSwatch_Click(object sender, RoutedEventArgs e)
    {
        PalettePanel.Visibility = PalettePanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void PaletteColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Background: SolidColorBrush brush })
        {
            _currentColor = brush.Color;
            PenSwatch.Background = brush;
            ApplyPen();
            PersistPenSettings();
        }
    }

    private void HighlighterToggle_Changed(object sender, RoutedEventArgs e) => ApplyPen();

    private void ThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _currentWidth = e.NewValue;
        ApplyPen();
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => _activeOverlay?.Undo();
    private void Clear_Click(object sender, RoutedEventArgs e) => _activeOverlay?.ClearAll();

    private void Exit_Click(object sender, RoutedEventArgs e) => EndDrawSession();

    protected override void OnClosed(EventArgs e)
    {
        _hotkeyService?.Dispose();
        base.OnClosed(e);
    }
}
