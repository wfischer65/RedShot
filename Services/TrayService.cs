using System.Drawing;
using System.Windows.Forms;

namespace RedShot.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public event Action? CaptureRegionRequested;
    public event Action? CaptureScreenRequested;
    public event Action? ExitRequested;

    public TrayService()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(
            "Bereich aufnehmen",
            null,
            (_, _) => CaptureRegionRequested?.Invoke());

        menu.Items.Add(
            "Gesamten Desktop aufnehmen",
            null,
            (_, _) => CaptureScreenRequested?.Invoke());

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(
            "Beenden",
            null,
            (_, _) => ExitRequested?.Invoke());

        _notifyIcon = new NotifyIcon
        {
            Text = "RedShot",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick +=
            (_, _) => CaptureRegionRequested?.Invoke();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
