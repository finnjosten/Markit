using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using ZoomItToolbar.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ZoomItToolbar;

public partial class DrawOverlayWindow : Window
{
    // Keep in sync with the color-dot buttons in MainWindow.xaml.
    private static readonly Dictionary<Key, Color> ColorKeys = new()
    {
        [Key.R] = (Color)ColorConverter.ConvertFromString("#FF4B4B")!,
        [Key.G] = (Color)ColorConverter.ConvertFromString("#4CAF50")!,
        [Key.B] = (Color)ColorConverter.ConvertFromString("#4A90D9")!,
        [Key.O] = (Color)ColorConverter.ConvertFromString("#FF9800")!,
        [Key.Y] = (Color)ColorConverter.ConvertFromString("#FFD54F")!,
        [Key.P] = (Color)ColorConverter.ConvertFromString("#F48FB1")!,
    };

    private readonly MonitorSnapshot _snapshot;
    private Color _color = ColorKeys[Key.R];
    private bool _highlighter;
    private double _width = 4;

    private IntPtr _hwnd;

    public event Action? ExitRequested;

    /// <summary>Fired on every click into the overlay. The toolbar (a separate
    /// window) uses this to re-assert its own topmost position, since clicking
    /// into the overlay can push it above the toolbar in the topmost z-order band.</summary>
    public event Action? UserInteracted;

    public DrawOverlayWindow(MonitorSnapshot snapshot)
    {
        InitializeComponent();
        _snapshot = snapshot;
        Screenshot.Source = snapshot.Image;
        ApplyAttributes();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        WindowStyles.MakeOverlayWindow(_hwnd);
        WindowStyles.PositionExact(_hwnd, _snapshot.PhysicalBounds);
    }

    /// <summary>Sets the pen color, highlighter mode, and nib width in one call.</summary>
    public void SetPen(Color color, bool highlighter, double width)
    {
        _color = color;
        _highlighter = highlighter;
        _width = width;
        ApplyAttributes();
    }

    private void ApplyAttributes()
    {
        var attributes = DrawCanvas.DefaultDrawingAttributes;
        attributes.Color = _color;
        attributes.IsHighlighter = _highlighter;
        attributes.StylusTip = _highlighter ? StylusTip.Rectangle : StylusTip.Ellipse;

        double width = _highlighter ? _width * 3 : _width;
        attributes.Width = width;
        attributes.Height = width;
    }

    public void Undo()
    {
        if (DrawCanvas.Strokes.Count > 0)
            DrawCanvas.Strokes.RemoveAt(DrawCanvas.Strokes.Count - 1);
    }

    public void ClearAll() => DrawCanvas.Strokes.Clear();

    public void RequestExit() => ExitRequested?.Invoke();

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e) => UserInteracted?.Invoke();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            RequestExit();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            Undo();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.E)
        {
            ClearAll();
            e.Handled = true;
            return;
        }

        if (ColorKeys.TryGetValue(e.Key, out var color))
        {
            bool highlighter = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            SetPen(color, highlighter, _width);
            e.Handled = true;
        }
    }
}
