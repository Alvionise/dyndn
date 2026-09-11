using System.Diagnostics;
using System.IO;
using DyndDns.TrayApp.Services;
using DyndDns.TrayApp.ViewModels;

namespace DyndDns.TrayApp;

public partial class App : System.Windows.Application
{
    private readonly SingleInstanceGuard _instanceGuard = new();

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        // The dialogs are WinForms windows and scale by the DPI asked for here; without the call the
        // process stays DPI-unaware, so Windows stretches their bitmap on a scaled display instead.
        // It has to happen before the first control exists, hence the very first statement.
        _ = System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.SystemAware);

        // AppContext.BaseDirectory points at the executable for single-file builds,
        // so the config folder stays next to dyndns.exe.
        var configDir = Path.Combine(AppContext.BaseDirectory, "config");
        Directory.CreateDirectory(configDir);
        ConfigureLogging(configDir);

        // A second copy (a double click while the autostart entry has already launched one) would work on
        // the same database and add a second tray icon, so it stops before anything is opened.
        if (!_instanceGuard.TryAcquire())
        {
            Trace.TraceWarning("Another instance is already running; this one stops.");
            Shutdown();
            return;
        }

        _ = new MainViewModel(configDir);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _instanceGuard.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureLogging(string configDir)
    {
        Trace.Listeners.Add(new TextWriterTraceListener(Path.Combine(configDir, "dyndns.log")));
        Trace.AutoFlush = true;
    }
}
