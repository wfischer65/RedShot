using System.Windows;
using RedShot.Services;
using WPFApplication = System.Windows.Application;

namespace RedShot;

public partial class App : WPFApplication
{
    private TrayService? _trayService;
    private HotkeyService? _hotkeyService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _trayService = new TrayService();
        _hotkeyService = new HotkeyService();

        _trayService.CaptureRegionRequested += CaptureRegion;
        _trayService.CaptureScreenRequested += CaptureScreen;
        _trayService.ExitRequested += ExitApplication;

        _hotkeyService.CaptureRegionRequested += CaptureRegion;
        _hotkeyService.CaptureScreenRequested += CaptureScreen;

        _hotkeyService.Start();
    }

    private void CaptureRegion()
    {
        Dispatcher.Invoke(async () =>
        {
            var selection = new Views.RegionSelectionWindow();

            if (selection.ShowDialog() != true ||
                selection.SelectedArea is null)
            {
                return;
            }

            var bitmap = CaptureService.CaptureRectangle(
                selection.SelectedArea.Value);

            var editor = new Views.EditorWindow(bitmap);
            editor.Show();
        });
    }

    private void CaptureScreen()
    {
        Dispatcher.Invoke(() =>
        {
            var bitmap = CaptureService.CaptureVirtualScreen();

            var editor = new Views.EditorWindow(bitmap);
            editor.Show();
        });
    }

    private void ExitApplication()
    {
        _hotkeyService?.Dispose();
        _trayService?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayService?.Dispose();

        base.OnExit(e);
    }
}
