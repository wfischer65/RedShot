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
using WPFKey = System.Windows.Input.Key;
using WPFKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WPFMessageBox = System.Windows.MessageBox;
using WPFRectangle = System.Windows.Shapes.Rectangle;
using WPFEllipse = System.Windows.Shapes.Ellipse;
using WPFLine = System.Windows.Shapes.Line;
using WPFShape = System.Windows.Shapes.Shape;
using WPFSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WinFormsColorDialog = System.Windows.Forms.ColorDialog;
using DrawingColor = System.Drawing.Color;
using WPFButton = System.Windows.Controls.Button;
using ClosingCancelEventArgs = System.ComponentModel.CancelEventArgs;

namespace RedShot.Views;

public partial class EditorWindow : Window
{
    private const double MinimumElementSize = 6;
    private const double HandleSize = 8;
    private readonly DrawingBitmap _bitmap;
    private readonly List<ShapeElement> _elements = [];
    private string _activeTool = "Select";
    private ShapeElement? _selectedElement;
    private ShapeElement? _newElement;
    private WPFPoint _operationStartLinePoint;
    private WPFPoint _operationEndLinePoint;
    private WPFPoint _operationStart;
    private Rect _operationStartBounds;
    private bool _isDrawing;
    private bool _isMoving;
    private bool _wasSavedOrCopied;

    // Diese Werte werden spaeter durch das Farb-/Transparenz-Popup gesetzt.
    private WPFColor _foregroundColor = WPFColor.FromRgb(255, 0, 0);
    private WPFColor _backgroundColor = WPFColor.FromArgb(128, 255, 255, 0);
    private double _strokeThickness = 3;
    private bool _updatingToolSettings;

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
        UpdateToolSettingsFromSelection();
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

    private void BackgroundColorButton_Click(object sender, RoutedEventArgs e) =>
        BackgroundColorPopup.IsOpen = true;

    private void ForegroundColorButton_Click(object sender, RoutedEventArgs e) =>
        ForegroundColorPopup.IsOpen = true;

    private void BackgroundPresetColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WPFButton { Tag: string value })
        {
            var color = ParseOpaqueColor(value);
            ApplyBackgroundColor(WPFColor.FromArgb(
                _backgroundColor.A, color.R, color.G, color.B));
        }
    }

    private void ForegroundPresetColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WPFButton { Tag: string value })
            ApplyForegroundColor(ParseOpaqueColor(value));
    }

    private void BackgroundMoreColor_Click(object sender, RoutedEventArgs e)
    {
        BackgroundColorPopup.IsOpen = false;
        using var dialog = new WinFormsColorDialog
        {
            Color = DrawingColor.FromArgb(_backgroundColor.R, _backgroundColor.G, _backgroundColor.B),
            FullOpen = true
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            ApplyBackgroundColor(WPFColor.FromArgb(
                _backgroundColor.A, dialog.Color.R, dialog.Color.G, dialog.Color.B));
    }

    private void ForegroundMoreColor_Click(object sender, RoutedEventArgs e)
    {
        ForegroundColorPopup.IsOpen = false;
        using var dialog = new WinFormsColorDialog
        {
            Color = DrawingColor.FromArgb(_foregroundColor.R, _foregroundColor.G, _foregroundColor.B),
            FullOpen = true
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            ApplyForegroundColor(WPFColor.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
    }

    private void BackgroundOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _updatingToolSettings)
            return;

        var alpha = (byte)Math.Round(e.NewValue * 255 / 100);
        ApplyBackgroundColor(WPFColor.FromArgb(
            alpha, _backgroundColor.R, _backgroundColor.G, _backgroundColor.B));
    }

    private void StrokeThicknessComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || _updatingToolSettings ||
            StrokeThicknessComboBox.SelectedItem is not ComboBoxItem { Tag: string value } ||
            !double.TryParse(value, out var thickness))
            return;

        _strokeThickness = thickness;
        if (_selectedElement is not null)
        {
            _selectedElement.StrokeThickness = thickness;
            _selectedElement.Shape.StrokeThickness = thickness;
        }
    }

    private static WPFColor ParseOpaqueColor(string value)
    {
        var color = (WPFColor)System.Windows.Media.ColorConverter.ConvertFromString(value)!;
        return WPFColor.FromRgb(color.R, color.G, color.B);
    }

    private void ApplyForegroundColor(WPFColor color)
    {
        _foregroundColor = WPFColor.FromRgb(color.R, color.G, color.B);
        ForegroundColorPreview.Background = new SolidColorBrush(_foregroundColor);
        if (_selectedElement is null)
            return;

        _selectedElement.ForegroundColor = _foregroundColor;
        _selectedElement.Shape.Stroke = new SolidColorBrush(_foregroundColor);
    }

    private void ApplyBackgroundColor(WPFColor color)
    {
        _backgroundColor = WPFColor.FromArgb(color.A, color.R, color.G, color.B);
        BackgroundColorPreview.Background = new SolidColorBrush(_backgroundColor);
        BackgroundOpacityText.Text = $"{Math.Round(_backgroundColor.A * 100d / 255)} %";
        if (_selectedElement is null)
            return;

        _selectedElement.BackgroundColor = _backgroundColor;
        if (_selectedElement.Kind != ElementKind.Line)
            _selectedElement.Shape.Fill = new SolidColorBrush(_backgroundColor);
    }

    private void UpdateToolSettingsFromSelection()
    {
        if (_selectedElement is not null)
        {
            _foregroundColor = _selectedElement.ForegroundColor;
            _backgroundColor = _selectedElement.BackgroundColor;
            _strokeThickness = _selectedElement.StrokeThickness;
        }

        _updatingToolSettings = true;
        ForegroundColorPreview.Background = new SolidColorBrush(_foregroundColor);
        BackgroundColorPreview.Background = new SolidColorBrush(_backgroundColor);
        BackgroundOpacitySlider.Value = _backgroundColor.A * 100d / 255;
        BackgroundOpacityText.Text = $"{Math.Round(BackgroundOpacitySlider.Value)} %";

        foreach (var item in StrokeThicknessComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string value && double.TryParse(value, out var thickness) &&
                Math.Abs(thickness - _strokeThickness) < 0.01)
            {
                StrokeThicknessComboBox.SelectedItem = item;
                break;
            }
        }
        _updatingToolSettings = false;
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

        var cursor = IsDrawingTool(tool) ? WPFCursors.Cross : WPFCursors.Arrow;
        EditorSurface.Cursor = cursor;
        AnnotationCanvas.Cursor = cursor;
        SelectionCanvas.Cursor = cursor;
        Mouse.OverrideCursor = IsDrawingTool(tool) ? WPFCursors.Cross : null;
        UpdateSelectionHandles();
    }

    private static bool IsDrawingTool(string tool) =>
        tool is "Rectangle" or "Ellipse" or "Line";

    private void EditorSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var position = ClampToSurface(e.GetPosition(EditorSurface));

        if (IsDrawingTool(_activeTool))
        {
            _operationStart = position;
            _newElement = CreateElement(_activeTool, position);
            SelectElement(_newElement);
            _isDrawing = true;
            EditorSurface.CaptureMouse();
            e.Handled = true;
            return;
        }

        var clickedElement = FindElement(e.OriginalSource as DependencyObject);
        SelectElement(clickedElement);
        if (clickedElement is null)
            return;

        _operationStart = position;
        _operationStartBounds = clickedElement.Bounds;
        _operationStartLinePoint = clickedElement.StartPoint;
        _operationEndLinePoint = clickedElement.EndPoint;
        _isMoving = true;
        EditorSurface.CaptureMouse();
        e.Handled = true;
    }

    private void EditorSurface_MouseMove(object sender, WPFMouseEventArgs e)
    {
        var position = ClampToSurface(e.GetPosition(EditorSurface));

        if (_isDrawing && _newElement is not null)
        {
            if (_newElement.Kind == ElementKind.Line)
                SetLinePoints(_newElement, _operationStart, position);
            else
                SetBounds(_newElement, NormalizeRect(_operationStart, position));
            return;
        }

        if (!_isMoving || _selectedElement is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var delta = position - _operationStart;
        if (_selectedElement.Kind == ElementKind.Line)
        {
            var minX = Math.Min(_operationStartLinePoint.X, _operationEndLinePoint.X);
            var maxX = Math.Max(_operationStartLinePoint.X, _operationEndLinePoint.X);
            var minY = Math.Min(_operationStartLinePoint.Y, _operationEndLinePoint.Y);
            var maxY = Math.Max(_operationStartLinePoint.Y, _operationEndLinePoint.Y);
            delta.X = Math.Clamp(delta.X, -minX, EditorSurface.Width - maxX);
            delta.Y = Math.Clamp(delta.Y, -minY, EditorSurface.Height - maxY);
            SetLinePoints(
                _selectedElement,
                _operationStartLinePoint + delta,
                _operationEndLinePoint + delta);
            return;
        }

        var bounds = _operationStartBounds;
        bounds.X = Math.Clamp(bounds.X + delta.X, 0, EditorSurface.Width - bounds.Width);
        bounds.Y = Math.Clamp(bounds.Y + delta.Y, 0, EditorSurface.Height - bounds.Height);
        SetBounds(_selectedElement, bounds);
    }

    private void EditorSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing && !_isMoving)
            return;

        EditorSurface.ReleaseMouseCapture();
        if (_isDrawing && _newElement is not null)
        {
            var isTooSmall = _newElement.Kind == ElementKind.Line
                ? (_newElement.EndPoint - _newElement.StartPoint).Length < MinimumElementSize
                : _newElement.Bounds.Width < MinimumElementSize ||
                  _newElement.Bounds.Height < MinimumElementSize;

            if (isTooSmall)
                RemoveElement(_newElement);
            else
                SelectElement(_newElement);

            _newElement = null;
        }

        _isDrawing = false;
        _isMoving = false;
        e.Handled = true;
    }

    private ShapeElement CreateElement(string tool, WPFPoint start)
    {
        var kind = tool switch
        {
            "Ellipse" => ElementKind.Ellipse,
            "Line" => ElementKind.Line,
            _ => ElementKind.Rectangle
        };

        WPFShape shape = kind switch
        {
            ElementKind.Ellipse => new WPFEllipse(),
            ElementKind.Line => new WPFLine(),
            _ => new WPFRectangle()
        };

        shape.Stroke = new SolidColorBrush(_foregroundColor);
        shape.StrokeThickness = _strokeThickness;
        shape.Cursor = WPFCursors.SizeAll;
        if (kind != ElementKind.Line)
            shape.Fill = new SolidColorBrush(_backgroundColor);

        var element = new ShapeElement(
            shape, kind, new Rect(start, start), _foregroundColor,
            _backgroundColor, _strokeThickness, start, start);
        shape.Tag = element;
        _elements.Add(element);
        AnnotationCanvas.Children.Add(shape);

        if (kind == ElementKind.Line)
            SetLinePoints(element, start, start);
        else
            SetBounds(element, element.Bounds);

        return element;
    }

    private void RemoveElement(ShapeElement element)
    {
        AnnotationCanvas.Children.Remove(element.Shape);
        _elements.Remove(element);
        if (ReferenceEquals(_selectedElement, element))
            SelectElement(null);
    }

    private ShapeElement? FindElement(DependencyObject? source)
    {
        while (source is not null && !ReferenceEquals(source, EditorSurface))
        {
            if (source is WPFShape { Tag: ShapeElement element })
                return element;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void SelectElement(ShapeElement? element)
    {
        _selectedElement = element;
        ToolSettingsBar.Visibility = element is null
            ? Visibility.Hidden
            : Visibility.Visible;
        UpdateSelectionHandles();
        UpdateToolSettingsFromSelection();
    }

    private void SetBounds(ShapeElement element, Rect bounds)
    {
        element.Bounds = bounds;
        Canvas.SetLeft(element.Shape, bounds.Left);
        Canvas.SetTop(element.Shape, bounds.Top);
        element.Shape.Width = Math.Max(0, bounds.Width);
        element.Shape.Height = Math.Max(0, bounds.Height);
        if (ReferenceEquals(_selectedElement, element))
            PositionSelectionHandles();
    }

    private void SetLinePoints(ShapeElement element, WPFPoint start, WPFPoint end)
    {
        element.StartPoint = start;
        element.EndPoint = end;
        element.Bounds = NormalizeRect(start, end);

        if (element.Shape is WPFLine line)
        {
            line.X1 = start.X;
            line.Y1 = start.Y;
            line.X2 = end.X;
            line.Y2 = end.Y;
        }

        if (ReferenceEquals(_selectedElement, element))
            PositionSelectionHandles();
    }

    private void UpdateSelectionHandles()
    {
        SelectionCanvas.Children.Clear();
        if (_selectedElement is null || _activeTool != "Select")
            return;

        if (_selectedElement.Kind == ElementKind.Line)
        {
            AddHandle(
                ResizeDirection.LineStart,
                _selectedElement.StartPoint.X,
                _selectedElement.StartPoint.Y,
                WPFCursors.Cross);
            AddHandle(
                ResizeDirection.LineEnd,
                _selectedElement.EndPoint.X,
                _selectedElement.EndPoint.Y,
                WPFCursors.Cross);
            return;
        }

        var b = _selectedElement.Bounds;
        AddHandle(ResizeDirection.TopLeft, b.Left, b.Top, WPFCursors.SizeNWSE);
        AddHandle(ResizeDirection.Top, b.Left + b.Width / 2, b.Top, WPFCursors.SizeNS);
        AddHandle(ResizeDirection.TopRight, b.Right, b.Top, WPFCursors.SizeNESW);
        AddHandle(ResizeDirection.Right, b.Right, b.Top + b.Height / 2, WPFCursors.SizeWE);
        AddHandle(ResizeDirection.BottomRight, b.Right, b.Bottom, WPFCursors.SizeNWSE);
        AddHandle(ResizeDirection.Bottom, b.Left + b.Width / 2, b.Bottom, WPFCursors.SizeNS);
        AddHandle(ResizeDirection.BottomLeft, b.Left, b.Bottom, WPFCursors.SizeNESW);
        AddHandle(ResizeDirection.Left, b.Left, b.Top + b.Height / 2, WPFCursors.SizeWE);
    }

    private void PositionSelectionHandles()
    {
        if (_selectedElement is null || SelectionCanvas.Children.Count == 0)
            return;

        var b = _selectedElement.Bounds;
        foreach (var child in SelectionCanvas.Children.OfType<Thumb>())
        {
            if (child.Tag is not ResizeDirection direction)
                continue;

            var (x, y) = direction switch
            {
                ResizeDirection.LineStart =>
                    (_selectedElement.StartPoint.X, _selectedElement.StartPoint.Y),
                ResizeDirection.LineEnd =>
                    (_selectedElement.EndPoint.X, _selectedElement.EndPoint.Y),
                ResizeDirection.TopLeft => (b.Left, b.Top),
                ResizeDirection.Top => (b.Left + b.Width / 2, b.Top),
                ResizeDirection.TopRight => (b.Right, b.Top),
                ResizeDirection.Right => (b.Right, b.Top + b.Height / 2),
                ResizeDirection.BottomRight => (b.Right, b.Bottom),
                ResizeDirection.Bottom => (b.Left + b.Width / 2, b.Bottom),
                ResizeDirection.BottomLeft => (b.Left, b.Bottom),
                ResizeDirection.Left => (b.Left, b.Top + b.Height / 2),
                _ => (b.Left, b.Top)
            };

            Canvas.SetLeft(child, x - HandleSize / 2);
            Canvas.SetTop(child, y - HandleSize / 2);
        }
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
        if (_selectedElement is null || sender is not Thumb { Tag: ResizeDirection direction })
            return;

        if (_selectedElement.Kind == ElementKind.Line)
        {
            if (direction == ResizeDirection.LineStart)
            {
                var start = ClampToSurface(new WPFPoint(
                    _selectedElement.StartPoint.X + e.HorizontalChange,
                    _selectedElement.StartPoint.Y + e.VerticalChange));
                SetLinePoints(_selectedElement, start, _selectedElement.EndPoint);
            }
            else if (direction == ResizeDirection.LineEnd)
            {
                var end = ClampToSurface(new WPFPoint(
                    _selectedElement.EndPoint.X + e.HorizontalChange,
                    _selectedElement.EndPoint.Y + e.VerticalChange));
                SetLinePoints(_selectedElement, _selectedElement.StartPoint, end);
            }
            return;
        }

        var b = _selectedElement.Bounds;
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

        SetBounds(_selectedElement, new Rect(new WPFPoint(left, top), new WPFPoint(right, bottom)));
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

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        WPFClipboard.SetImage(CreateCompositeBitmapSource());
        _wasSavedOrCopied = true;
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveImage();

    private bool SaveImage()
    {
        var dialog = new WPFSaveFileDialog
        {
            Title = "Screenshot speichern",
            Filter = "PNG-Bild (*.png)|*.png|JPEG-Bild (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp",
            FileName = $"RedShot_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        };
        if (dialog.ShowDialog(this) != true)
            return false;

        BitmapEncoder encoder = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
            ".bmp" => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
        encoder.Frames.Add(BitmapFrame.Create(CreateCompositeBitmapSource()));
        using var stream = File.Create(dialog.FileName);
        encoder.Save(stream);
        _wasSavedOrCopied = true;
        return true;
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
            $"RedShot\nVersion {versionText}\n\nEditor: CloseSavePrompt-V1",
            "Ueber RedShot", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnPreviewKeyDown(WPFKeyEventArgs e)
    {
        if (e.Key == WPFKey.Delete && _selectedElement is not null)
        {
            RemoveElement(_selectedElement);
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnClosing(ClosingCancelEventArgs e)
    {
        if (!_wasSavedOrCopied)
        {
            var result = WPFMessageBox.Show(
                this,
                "Das Bild wurde noch nicht gespeichert oder in die Zwischenablage kopiert.\n\nSoll es jetzt gespeichert werden?",
                "RedShot schließen",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel ||
                result == MessageBoxResult.Yes && !SaveImage())
            {
                e.Cancel = true;
                return;
            }
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Mouse.OverrideCursor = null;
        _bitmap.Dispose();
        base.OnClosed(e);
    }

    private sealed class ShapeElement(
        WPFShape shape, ElementKind kind, Rect bounds,
        WPFColor foregroundColor, WPFColor backgroundColor,
        double strokeThickness, WPFPoint startPoint, WPFPoint endPoint)
    {
        public WPFShape Shape { get; } = shape;
        public ElementKind Kind { get; } = kind;
        public Rect Bounds { get; set; } = bounds;
        public WPFColor ForegroundColor { get; set; } = foregroundColor;
        public WPFColor BackgroundColor { get; set; } = backgroundColor;
        public double StrokeThickness { get; set; } = strokeThickness;
        public WPFPoint StartPoint { get; set; } = startPoint;
        public WPFPoint EndPoint { get; set; } = endPoint;
    }

    private enum ElementKind
    {
        Rectangle,
        Ellipse,
        Line
    }

    private enum ResizeDirection
    {
        TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left,
        LineStart, LineEnd
    }
}
