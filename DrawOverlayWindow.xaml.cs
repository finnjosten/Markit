using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using Markit.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Markit;

/// <summary>Tools selectable from the radial menu. Kept separate from MainWindow's
/// private ToolMode enum since DrawOverlayWindow doesn't otherwise know about it.</summary>
public enum RadialTool { Pen, Highlighter, Eraser }

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
    private double _eraserWidth = 16;

    private IntPtr _hwnd;

    public event Action? ExitRequested;

    /// <summary>Fired on every click into the overlay. The toolbar (a separate
    /// window) uses this to re-assert its own topmost position, since clicking
    /// into the overlay can push it above the toolbar in the topmost z-order band.</summary>
    public event Action? UserInteracted;

    /// <summary>Fired when a tool is picked from the radial menu. The toolbar
    /// subscribes to keep its own tool buttons/state in sync.</summary>
    public event Action<RadialTool>? ToolSelected;

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

    /// <summary>Sets the pen color, highlighter mode, and nib width in one call.
    /// Does not change eraser mode — callers that mean "switch to this color"
    /// (as opposed to just adjusting thickness/highlighter mid-erase) should
    /// also call <see cref="SetEraser"/>(false).</summary>
    public void SetPen(Color color, bool highlighter, double width)
    {
        _color = color;
        _highlighter = highlighter;
        _width = width;
        ApplyAttributes();
    }

    public void SetEraser(bool enabled, double width = 16)
    {
        _eraserWidth = width;
        DrawCanvas.EditingMode = enabled ? InkCanvasEditingMode.EraseByPoint : InkCanvasEditingMode.Ink;
        if (enabled)
            DrawCanvas.EraserShape = new EllipseStylusShape(width, width);
    }

    /// <summary>The current strokes, for persisting to <see cref="Services.InkStore"/> on exit.</summary>
    public StrokeCollection GetStrokes() => DrawCanvas.Strokes;

    /// <summary>Replaces the current strokes, e.g. when resuming a previous session's edits.</summary>
    public void LoadStrokes(StrokeCollection strokes) => DrawCanvas.Strokes = strokes;

    /// <summary>Keeps the radial menu's Pen/Highlighter icons showing the actual
    /// current color of each tool, independent of which one is presently active.</summary>
    public void SetToolColors(Color penColor, Color highlighterColor)
    {
        RadialPenTipIndicator.Background = new SolidColorBrush(penColor);
        RadialHighlighterTipIndicator.Background = new SolidColorBrush(highlighterColor);
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

    private bool IsRadialMenuOpen => RadialMenuLayer.Visibility == Visibility.Visible;

    /// <summary>Opens the radial menu centered on the current mouse position. The
    /// button layout is computed from however many buttons are in the array below,
    /// so adding more tools later (shapes) is just adding another button + case.</summary>
    private void OpenRadialMenu()
    {
        var center = Mouse.GetPosition(RadialMenuCanvas);
        var buttons = new[] { RadialPenButton, RadialHighlighterButton, RadialEraserButton };
        const double radius = 55;

        for (int i = 0; i < buttons.Length; i++)
        {
            double angle = (Math.PI * 2 / buttons.Length * i) - Math.PI / 2; // start pointing up
            double x = center.X + radius * Math.Cos(angle) - buttons[i].Width / 2;
            double y = center.Y + radius * Math.Sin(angle) - buttons[i].Height / 2;
            Canvas.SetLeft(buttons[i], x);
            Canvas.SetTop(buttons[i], y);
        }

        RadialMenuLayer.Visibility = Visibility.Visible;
    }

    private void CloseRadialMenu() => RadialMenuLayer.Visibility = Visibility.Collapsed;

    private void RadialMenuScrim_MouseDown(object sender, MouseButtonEventArgs e) => CloseRadialMenu();

    private void SelectRadialTool(RadialTool tool)
    {
        ToolSelected?.Invoke(tool);
        CloseRadialMenu();
    }

    private void RadialPenButton_Click(object sender, RoutedEventArgs e) => SelectRadialTool(RadialTool.Pen);
    private void RadialHighlighterButton_Click(object sender, RoutedEventArgs e) => SelectRadialTool(RadialTool.Highlighter);
    private void RadialEraserButton_Click(object sender, RoutedEventArgs e) => SelectRadialTool(RadialTool.Eraser);

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e) => UserInteracted?.Invoke();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            if (IsRadialMenuOpen) CloseRadialMenu();
            else OpenRadialMenu();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (IsRadialMenuOpen)
                CloseRadialMenu();
            else
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

        if (e.Key == Key.X)
        {
            SetEraser(DrawCanvas.EditingMode != InkCanvasEditingMode.EraseByPoint, _eraserWidth);
            e.Handled = true;
            return;
        }

        if (ColorKeys.TryGetValue(e.Key, out var color))
        {
            bool highlighter = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            SetEraser(false);
            SetPen(color, highlighter, _width);
            e.Handled = true;
        }
    }
}
