using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WPFClipboard = System.Windows.Clipboard;
using DrawingBitmap = System.Drawing.Bitmap;
using WPFColor = System.Windows.Media.Color;
using WPFBrushes = System.Windows.Media.Brushes;
using WPFCursor = System.Windows.Input.Cursor;
using WPFCursors = System.Windows.Input.Cursors;
using WPFMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WPFPoint = System.Windows.Point;
using WPFMessageBox = System.Windows.MessageBox;
using WPFRectangle = System.Windows.Shapes.Rectangle;
using WPFSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace RedShot.Views;

public partial class EditorWindow : Window
{
    private const double MinimumElementSize = 6;
    private const double HandleSize = 8;
    private readonly DrawingBitmap _bitmap;
    private readonly List<RectangleElement> _rectangles = [];
    private string _activeTool = "Select";
    private RectangleElement? _selectedRectangle;
    private RectangleElement? _newRectangle;
    private WPFPoint _operationStart;
    private Rect _operationStartBounds;
    private bool _isDrawing;
    private bool _isMoving;

    // Diese Werte werden spaeter durch das Farb-/Transparenz-Popup gesetzt.
    private WPFColor _foregroundColor = WPFColor.FromRgb(255, 0, 0);
    private WPFColor _backgroundColor = WPFColor.FromArgb(128, 255, 255, 0);

    public EditorWindow(DrawingBitmap bitmap)
    {
        InitializeComponent();
        _bitmap = bitmap;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is not null)
            Title = $"RedShot {version.Major}.{version.Minor}.{version.Build}";

        PreviewImage.Source = BitmapToBitmapSource(bitmap);
        EditorSurface.Width = bitmap.Width;
        EditorSurface.Height = bitmap.Height;
    }

    private static BitmapSource BitmapToBitmapSource(DrawingBitmap bitmap)
    {
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        memory.Position = 0;

        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.StreamSource = memory;
        source.EndInit();
        source.Freeze();
        return source;
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton selected && selected.Tag is string tool)
            SetActiveTool(tool, selected);
    }

    private void SetActiveTool(string tool, ToggleButton? selectedButton = null)
    {
        _activeTool = tool;

        foreach (var button in FindVisualChildren<ToggleButton>(this))
        {
            if (button.Tag is not null)
                button.IsChecked = selectedButton is not null
                    ? ReferenceEquals(button, selectedButton)
                    : string.Equals(button.Tag as string, tool, StringComparison.Ordinal);
        }

        var cursor = tool == "Rectangle" ? WPFCursors.Cross : WPFCursors.Arrow;
        EditorSurface.Cursor = cursor;
        AnnotationCanvas.Cursor = cursor;
        SelectionCanvas.Cursor = cursor;
        Mouse.OverrideCursor = tool == "Rectangle" ? WPFCursors.Cross : null;
        UpdateSelectionHandles();
    }

    private void EditorSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var position = ClampToSurface(e.GetPosition(EditorSurface));

        if (_activeTool == "Rectangle")
        {
            _operationStart = position;
            _newRectangle = CreateRectangle(new Rect(position, position));
            SelectRectangle(_newRectangle);
            _isDrawing = true;
            EditorSurface.CaptureMouse();
            e.Handled = true;
            return;
        }

        var clickedRectangle = FindRectangle(e.OriginalSource as DependencyObject);
        SelectRectangle(clickedRectangle);
        if (clickedRectangle is null)
            return;

        _operationStart = position;
        _operationStartBounds = clickedRectangle.Bounds;
        _isMoving = true;
        EditorSurface.CaptureMouse();
        e.Handled = true;
    }

    private void EditorSurface_MouseMove(object sender, WPFMouseEventArgs e)
    {
        var position = ClampToSurface(e.GetPosition(EditorSurface));

        if (_isDrawing && _newRectangle is not null)
        {
            SetBounds(_newRectangle, NormalizeRect(_operationStart, position));
            return;
        }

        if (!_isMoving || _selectedRectangle is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var delta = position - _operationStart;
        var bounds = _operationStartBounds;
        bounds.X = Math.Clamp(bounds.X + delta.X, 0, EditorSurface.Width - bounds.Width);
        bounds.Y = Math.Clamp(bounds.Y + delta.Y, 0, EditorSurface.Height - bounds.Height);
        SetBounds(_selectedRectangle, bounds);
    }

    private void EditorSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing && !_isMoving)
            return;

        EditorSurface.ReleaseMouseCapture();
        if (_isDrawing && _newRectangle is not null)
        {
            if (_newRectangle.Bounds.Width < MinimumElementSize ||
                _newRectangle.Bounds.Height < MinimumElementSize)
                RemoveRectangle(_newRectangle);
            else
                SelectRectangle(_newRectangle);

            _newRectangle = null;
            SetActiveTool("Select");
        }

        _isDrawing = false;
        _isMoving = false;
        e.Handled = true;
    }

    private RectangleElement CreateRectangle(Rect bounds)
    {
        var shape = new WPFRectangle
        {
            Stroke = new SolidColorBrush(_foregroundColor),
            StrokeThickness = 3,
            Fill = new SolidColorBrush(_backgroundColor),
            Cursor = WPFCursors.SizeAll
        };

        var element = new RectangleElement(shape, bounds, _foregroundColor, _backgroundColor);
        shape.Tag = element;
        _rectangles.Add(element);
        AnnotationCanvas.Children.Add(shape);
        SetBounds(element, bounds);
        return element;
    }

    private void RemoveRectangle(RectangleElement element)
    {
        AnnotationCanvas.Children.Remove(element.Shape);
        _rectangles.Remove(element);
        if (ReferenceEquals(_selectedRectangle, element))
            SelectRectangle(null);
    }

    private RectangleElement? FindRectangle(DependencyObject? source)
    {
        while (source is not null && !ReferenceEquals(source, EditorSurface))
        {
            if (source is WPFRectangle { Tag: RectangleElement element })
                return element;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void SelectRectangle(RectangleElement? element)
    {
        _selectedRectangle = element;
        UpdateSelectionHandles();
    }

    private void SetBounds(RectangleElement element, Rect bounds)
    {
        element.Bounds = bounds;
        Canvas.SetLeft(element.Shape, bounds.Left);
        Canvas.SetTop(element.Shape, bounds.Top);
        element.Shape.Width = Math.Max(0, bounds.Width);
        element.Shape.Height = Math.Max(0, bounds.Height);
        if (ReferenceEquals(_selectedRectangle, element))
            UpdateSelectionHandles();
    }

    private void UpdateSelectionHandles()
    {
        SelectionCanvas.Children.Clear();
        if (_selectedRectangle is null || _activeTool != "Select")
            return;

        var b = _selectedRectangle.Bounds;
        AddHandle(ResizeDirection.TopLeft, b.Left, b.Top, WPFCursors.SizeNWSE);
        AddHandle(ResizeDirection.Top, b.Left + b.Width / 2, b.Top, WPFCursors.SizeNS);
        AddHandle(ResizeDirection.TopRight, b.Right, b.Top, WPFCursors.SizeNESW);
        AddHandle(ResizeDirection.Right, b.Right, b.Top + b.Height / 2, WPFCursors.SizeWE);
        AddHandle(ResizeDirection.BottomRight, b.Right, b.Bottom, WPFCursors.SizeNWSE);
        AddHandle(ResizeDirection.Bottom, b.Left + b.Width / 2, b.Bottom, WPFCursors.SizeNS);
        AddHandle(ResizeDirection.BottomLeft, b.Left, b.Bottom, WPFCursors.SizeNESW);
        AddHandle(ResizeDirection.Left, b.Left, b.Top + b.Height / 2, WPFCursors.SizeWE);
    }

    private void AddHandle(ResizeDirection direction, double x, double y, WPFCursor cursor)
    {
        var handle = new Thumb
        {
            Width = HandleSize,
            Height = HandleSize,
            Background = WPFBrushes.White,
            BorderBrush = WPFBrushes.Black,
            BorderThickness = new Thickness(1),
            Cursor = cursor,
            Tag = direction
        };
        handle.DragDelta += ResizeHandle_DragDelta;
        Canvas.SetLeft(handle, x - HandleSize / 2);
        Canvas.SetTop(handle, y - HandleSize / 2);
        SelectionCanvas.Children.Add(handle);
    }

    private void ResizeHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_selectedRectangle is null || sender is not Thumb { Tag: ResizeDirection direction })
            return;

        var b = _selectedRectangle.Bounds;
        var left = b.Left;
        var top = b.Top;
        var right = b.Right;
        var bottom = b.Bottom;

        if (direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft)
            left = Math.Clamp(left + e.HorizontalChange, 0, right - MinimumElementSize);
        if (direction is ResizeDirection.Right or ResizeDirection.TopRight or ResizeDirection.BottomRight)
            right = Math.Clamp(right + e.HorizontalChange, left + MinimumElementSize, EditorSurface.Width);
        if (direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight)
            top = Math.Clamp(top + e.VerticalChange, 0, bottom - MinimumElementSize);
        if (direction is ResizeDirection.Bottom or ResizeDirection.BottomLeft or ResizeDirection.BottomRight)
            bottom = Math.Clamp(bottom + e.VerticalChange, top + MinimumElementSize, EditorSurface.Height);

        SetBounds(_selectedRectangle, new Rect(new WPFPoint(left, top), new WPFPoint(right, bottom)));
    }

    private WPFPoint ClampToSurface(WPFPoint point) => new(
        Math.Clamp(point.X, 0, EditorSurface.Width),
        Math.Clamp(point.Y, 0, EditorSurface.Height));

    private static Rect NormalizeRect(WPFPoint first, WPFPoint second) => new(
        new WPFPoint(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y)),
        new WPFPoint(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y)));

    private BitmapSource CreateCompositeBitmapSource()
    {
        var oldVisibility = SelectionCanvas.Visibility;
        SelectionCanvas.Visibility = Visibility.Collapsed;
        EditorSurface.UpdateLayout();

        var result = new RenderTargetBitmap(
            _bitmap.Width, _bitmap.Height, 96, 96, PixelFormats.Pbgra32);
        result.Render(EditorSurface);
        result.Freeze();

        SelectionCanvas.Visibility = oldVisibility;
        return result;
    }

    private void Copy_Click(object sender, RoutedEventArgs e) =>
        WPFClipboard.SetImage(CreateCompositeBitmapSource());

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WPFSaveFileDialog
        {
            Title = "Screenshot speichern",
            Filter = "PNG-Bild (*.png)|*.png|JPEG-Bild (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp",
            FileName = $"RedShot_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        BitmapEncoder encoder = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
            ".bmp" => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
        encoder.Frames.Add(BitmapFrame.Create(CreateCompositeBitmapSource()));
        using var stream = File.Create(dialog.FileName);
        encoder.Save(stream);
    }

    private void NotImplemented_Click(object sender, RoutedEventArgs e) { }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                yield return typedChild;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version is null ? "unbekannt" : $"{version.Major}.{version.Minor}.{version.Build}";
        WPFMessageBox.Show(this,
            $"RedShot\nVersion {versionText}\n\nEditor: Rectangle-Crosshair-3px",
            "Ueber RedShot", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        Mouse.OverrideCursor = null;
        _bitmap.Dispose();
        base.OnClosed(e);
    }

    private sealed class RectangleElement(
        WPFRectangle shape, Rect bounds, WPFColor foregroundColor, WPFColor backgroundColor)
    {
        public WPFRectangle Shape { get; } = shape;
        public Rect Bounds { get; set; } = bounds;
        public WPFColor ForegroundColor { get; set; } = foregroundColor;
        public WPFColor BackgroundColor { get; set; } = backgroundColor;
    }

    private enum ResizeDirection
    {
        TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left
    }
}
