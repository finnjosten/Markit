using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Markit.Services;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using PixelFormats = System.Windows.Media.PixelFormats;
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

    /// <summary>One undoable gesture: strokes added and/or removed by it. Covers plain
    /// drawing (Added only) and erasing (Removed for a fully-erased stroke, or both
    /// Added+Removed when EraseByPoint splits a stroke into shorter pieces).</summary>
    private sealed record UndoAction(StrokeCollection Added, StrokeCollection Removed);

    private readonly MonitorSnapshot _snapshot;
    private readonly List<UndoAction> _undoStack = new();
    private StrokeCollection? _gestureAdded;
    private StrokeCollection? _gestureRemoved;
    private bool _isApplyingUndo;

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

    /// <summary>Fired when the radial menu's Save button is clicked. The toolbar
    /// subscribes and calls <see cref="SaveAndCopy"/> with the configured folder,
    /// since this window doesn't know that setting.</summary>
    public event Action? SaveRequested;

    public DrawOverlayWindow(MonitorSnapshot snapshot)
    {
        InitializeComponent();
        _snapshot = snapshot;
        Screenshot.Source = snapshot.Image;
        ApplyAttributes();
        DrawCanvas.Strokes.StrokesChanged += OnStrokesChanged;
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
    public void LoadStrokes(StrokeCollection strokes)
    {
        // Assigning Strokes swaps in a whole new collection instance, so the
        // StrokesChanged subscription (attached to the old instance) must move too.
        DrawCanvas.Strokes.StrokesChanged -= OnStrokesChanged;
        DrawCanvas.Strokes = strokes;
        DrawCanvas.Strokes.StrokesChanged += OnStrokesChanged;
        _undoStack.Clear(); // nothing before the loaded baseline is meaningful to undo back to
    }

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

    /// <summary>Undoes the last gesture — a drawn stroke, or an erase (full removal
    /// or a partial erase that split a stroke into shorter pieces).</summary>
    public void Undo()
    {
        if (_undoStack.Count == 0)
            return;

        var action = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        _isApplyingUndo = true;
        try
        {
            foreach (var stroke in action.Added)
                DrawCanvas.Strokes.Remove(stroke);
            foreach (var stroke in action.Removed)
                DrawCanvas.Strokes.Add(stroke);
        }
        finally
        {
            _isApplyingUndo = false;
        }
    }

    public void ClearAll()
    {
        DrawCanvas.Strokes.Clear();
        _undoStack.Clear(); // otherwise a later Undo could try to remove an already-gone stroke
    }

    /// <summary>A freshly-drawn stroke is recorded here directly — not via the mouse-gesture
    /// batching below — because <see cref="StrokeCollected"/> fires exactly when InkCanvas
    /// commits it, whereas the tunneling PreviewMouseLeftButtonUp fires earlier (before that
    /// commit), which was silently dropping every drawn stroke's undo entry.</summary>
    private void DrawCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        _undoStack.Add(new UndoAction(new StrokeCollection { e.Stroke }, new StrokeCollection()));
    }

    /// <summary>Tracks an eraser drag's net stroke changes (which can span several
    /// StrokesChanged events as the eraser passes over multiple strokes) as one undo
    /// entry, so Ctrl+Z reverses "one erase gesture" at a time.</summary>
    private void DrawCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _gestureAdded = new StrokeCollection();
        _gestureRemoved = new StrokeCollection();
    }

    private void DrawCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_gestureAdded is { Count: > 0 } || _gestureRemoved is { Count: > 0 })
            _undoStack.Add(new UndoAction(_gestureAdded!, _gestureRemoved!));

        _gestureAdded = null;
        _gestureRemoved = null;
    }

    private void OnStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        // Drawn strokes are handled by StrokeCollected instead — without this guard
        // a normal pen stroke could get recorded twice (once here, once there).
        if (_isApplyingUndo || DrawCanvas.EditingMode != InkCanvasEditingMode.EraseByPoint)
            return;

        if (_gestureAdded is null || _gestureRemoved is null)
            return; // outside a tracked erase gesture (ClearAll/LoadStrokes/Undo itself)

        foreach (var stroke in e.Added)
            _gestureAdded.Add(stroke);
        foreach (var stroke in e.Removed)
            _gestureRemoved.Add(stroke);
    }

    public void RequestExit() => ExitRequested?.Invoke();

    /// <summary>Composites the frozen screenshot + ink (not the border/watermark chrome
    /// or radial menu) into one image, copies it to the clipboard, and saves it as a PNG
    /// into <paramref name="folder"/> (created if it doesn't exist yet).</summary>
    public void SaveAndCopy(string folder)
    {
        var bitmap = RenderContent();

        try
        {
            Clipboard.SetImage(bitmap);
        }
        catch
        {
            // Another app can transiently hold the clipboard open — not fatal, still try to save.
        }

        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"Markit_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this,
                $"Could not save the screenshot to \"{folder}\": {ex.Message}",
                "Markit", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Renders <see cref="ContentLayer"/> (screenshot + ink) at the monitor's
    /// actual physical pixel resolution, regardless of the window's current DPI scale.</summary>
    private BitmapSource RenderContent() => RenderContentScaled(1.0);

    /// <summary>A small preview of the current drawing, for the History browser.</summary>
    public BitmapSource RenderThumbnail(int maxDimension = 240)
    {
        double longestSide = Math.Max(_snapshot.PhysicalBounds.Width, _snapshot.PhysicalBounds.Height);
        double scale = Math.Min(1.0, maxDimension / longestSide);
        return RenderContentScaled(scale);
    }

    private BitmapSource RenderContentScaled(double scale)
    {
        double dpi = 96.0 * VisualTreeHelper.GetDpi(this).DpiScaleX * scale;
        int pixelWidth = Math.Max(1, (int)(_snapshot.PhysicalBounds.Width * scale));
        int pixelHeight = Math.Max(1, (int)(_snapshot.PhysicalBounds.Height * scale));

        var render = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        render.Render(ContentLayer);
        render.Freeze();
        return render;
    }

    private bool IsRadialMenuOpen => RadialMenuLayer.Visibility == Visibility.Visible;

    /// <summary>Opens the radial menu centered on the current mouse position. The
    /// button layout is computed from however many buttons are in the array below,
    /// so adding more tools later (shapes) is just adding another button + case.</summary>
    private void OpenRadialMenu()
    {
        var center = Mouse.GetPosition(RadialMenuCanvas);
        var buttons = new[] { RadialPenButton, RadialHighlighterButton, RadialEraserButton, RadialSaveButton };
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

    private void RadialSaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveRequested?.Invoke();
        CloseRadialMenu();
    }

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
