using WPFSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using WPFClipboard = System.Windows.Clipboard;
using System.Windows.Media.Imaging;
using WPFMessageBox = System.Windows.MessageBox;

namespace RedShot.Views;

public partial class EditorWindow : Window
{
    private readonly Bitmap _bitmap;

    public EditorWindow(Bitmap bitmap)
    {
        InitializeComponent();

        _bitmap = bitmap;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is not null)
            Title = $"RedShot {version.Major}.{version.Minor}.{version.Build}";

        PreviewImage.Source =
            BitmapToBitmapSource(bitmap);
    }

    private static BitmapSource BitmapToBitmapSource(
        Bitmap bitmap)
    {
        using var memory = new MemoryStream();

        bitmap.Save(
            memory,
            ImageFormat.Png);

        memory.Position = 0;

        var source = new BitmapImage();

        source.BeginInit();
        source.CacheOption =
            BitmapCacheOption.OnLoad;
        source.StreamSource = memory;
        source.EndInit();
        source.Freeze();

        return source;
    }


    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton selected)
            return;

        foreach (var button in FindVisualChildren<ToggleButton>(this))
        {
            if (button.Tag is not null)
                button.IsChecked = ReferenceEquals(button, selected);
        }
    }

    private void NotImplemented_Click(object sender, RoutedEventArgs e)
    {
        // Der Button ist bewusst bereits in der Oberfläche vorhanden.
        // Die jeweilige Funktion wird in den nächsten Schritten ergänzt.
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        if (parent is null)
            yield break;

        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

            if (child is T typedChild)
                yield return typedChild;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private void Copy_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (PreviewImage.Source is BitmapSource source)
        {
            WPFClipboard.SetImage(source);
        }
    }

    private void SaveAs_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new WPFSaveFileDialog
        {
            Title = "Screenshot speichern",
            Filter =
                "PNG-Bild (*.png)|*.png|" +
                "JPEG-Bild (*.jpg)|*.jpg|" +
                "Bitmap (*.bmp)|*.bmp",
            FileName =
                $"RedShot_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var extension =
            Path.GetExtension(dialog.FileName)
                .ToLowerInvariant();

        var format = extension switch
        {
            ".jpg" or ".jpeg" =>
                ImageFormat.Jpeg,

            ".bmp" =>
                ImageFormat.Bmp,

            _ =>
                ImageFormat.Png
        };

        _bitmap.Save(
            dialog.FileName,
            format);
    }


    private void About_Click(object sender, RoutedEventArgs e)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version is null
            ? "unbekannt"
            : $"{version.Major}.{version.Minor}.{version.Build}";

        WPFMessageBox.Show(
            this,
            $"RedShot\nVersion {versionText}\n\nEditor-Layout: FixedSidebar-40-Scroll",
            "Über RedShot",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _bitmap.Dispose();
        base.OnClosed(e);
    }
}
