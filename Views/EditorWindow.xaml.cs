using WPFSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using WPFClipboard = System.Windows.Clipboard;
using System.Windows.Media.Imaging;

namespace RedShot.Views;

public partial class EditorWindow : Window
{
    private readonly Bitmap _bitmap;

    public EditorWindow(Bitmap bitmap)
    {
        InitializeComponent();

        _bitmap = bitmap;
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
