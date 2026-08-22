using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using RedShot.Interop;
using WPFMessageBox = System.Windows.MessageBox;

namespace RedShot.Services;

public sealed class HotkeyService : IDisposable
{
    private const int RegionHotkeyId = 1001;
    private const int ScreenHotkeyId = 1002;

    private HwndSource? _source;

    public event Action? CaptureRegionRequested;
    public event Action? CaptureScreenRequested;

    public void Start()
    {
        if (_source is not null)
            return;

        var parameters = new HwndSourceParameters("RedShotHotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        var regionOk = NativeMethods.RegisterHotKey(
            _source.Handle,
            RegionHotkeyId,
            NativeMethods.MOD_NONE,
            NativeMethods.VK_SNAPSHOT);

        var screenOk = NativeMethods.RegisterHotKey(
            _source.Handle,
            ScreenHotkeyId,
            NativeMethods.MOD_CONTROL,
            NativeMethods.VK_SNAPSHOT);

        if (!regionOk)
        {
            WPFMessageBox.Show(
                "Die Druck-Taste konnte nicht als RedShot-Hotkey registriert werden.\n\n" +
                "Unter Windows 11 ist möglicherweise „Drucktaste zum Öffnen der Bildschirmaufnahme verwenden“ aktiviert.\n" +
                "Du kannst RedShot trotzdem über das Tray-Symbol verwenden.",
                "RedShot – Hotkey",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        if (!screenOk)
        {
            // Nicht kritisch; Vollbild bleibt über Tray erreichbar.
        }
    }

    private nint WndProc(
        nint hwnd,
        int msg,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (msg != NativeMethods.WM_HOTKEY)
            return 0;

        var id = wParam.ToInt32();

        if (id == RegionHotkeyId)
        {
            CaptureRegionRequested?.Invoke();
            handled = true;
        }
        else if (id == ScreenHotkeyId)
        {
            CaptureScreenRequested?.Invoke();
            handled = true;
        }

        return 0;
    }

    public void Dispose()
    {
        if (_source is null)
            return;

        NativeMethods.UnregisterHotKey(
            _source.Handle,
            RegionHotkeyId);

        NativeMethods.UnregisterHotKey(
            _source.Handle,
            ScreenHotkeyId);

        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }
}
