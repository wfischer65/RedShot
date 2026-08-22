using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace RedShot.Services;

public static class CaptureService
{
    public static Bitmap CaptureVirtualScreen()
    {
        var bounds = SystemInformation.VirtualScreen;

        return CaptureRectangle(
            new System.Windows.Int32Rect(
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height));
    }

    public static Bitmap CaptureRectangle(
        System.Windows.Int32Rect area)
    {
        if (area.Width <= 0 || area.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(area));

        var bitmap = new Bitmap(
            area.Width,
            area.Height,
            PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(bitmap);

        graphics.CopyFromScreen(
            area.X,
            area.Y,
            0,
            0,
            new Size(area.Width, area.Height),
            CopyPixelOperation.SourceCopy);

        return bitmap;
    }
}
