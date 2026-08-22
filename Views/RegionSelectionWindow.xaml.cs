using System.Windows;
using System.Windows.Input;
using WPFPoint = System.Windows.Point;
using System.Windows.Controls;

namespace RedShot.Views;

public partial class RegionSelectionWindow : Window
{
    private WPFPoint? _startPoint;

    public Int32Rect? SelectedArea { get; private set; }

    public RegionSelectionWindow()
    {
        InitializeComponent();

        var screen = SystemInformation.VirtualScreen;

        Left = screen.Left;
        Top = screen.Top;
        Width = screen.Width;
        Height = screen.Height;
    }

    private void Window_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(SelectionCanvas);

        SelectionRectangle.Visibility =
            Visibility.Visible;

        CaptureMouse();
    }

    private void Window_MouseMove(
        object sender,
        System.Windows.Input.MouseEventArgs e)
    {
        if (_startPoint is null ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current =
            e.GetPosition(SelectionCanvas);

        var x =
            Math.Min(_startPoint.Value.X, current.X);

        var y =
            Math.Min(_startPoint.Value.Y, current.Y);

        var width =
            Math.Abs(current.X - _startPoint.Value.X);

        var height =
            Math.Abs(current.Y - _startPoint.Value.Y);

        Canvas.SetLeft(SelectionRectangle, x);
        Canvas.SetTop(SelectionRectangle, y);

        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;
    }

    private void Window_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_startPoint is null)
            return;

        ReleaseMouseCapture();

        var end =
            e.GetPosition(SelectionCanvas);

        var left =
            Math.Min(_startPoint.Value.X, end.X);

        var top =
            Math.Min(_startPoint.Value.Y, end.Y);

        var width =
            Math.Abs(end.X - _startPoint.Value.X);

        var height =
            Math.Abs(end.Y - _startPoint.Value.Y);

        if (width < 2 || height < 2)
        {
            DialogResult = false;
            Close();
            return;
        }

        var source =
            PresentationSource.FromVisual(this);

        var scaleX =
            source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

        var scaleY =
            source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        SelectedArea = new Int32Rect(
            (int)Math.Round((Left + left) * scaleX),
            (int)Math.Round((Top + top) * scaleY),
            (int)Math.Round(width * scaleX),
            (int)Math.Round(height * scaleY));

        DialogResult = true;
        Close();
    }

    private void Window_KeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        DialogResult = false;
        Close();
    }
}
