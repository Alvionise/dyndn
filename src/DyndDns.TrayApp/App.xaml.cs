using System.Diagnostics;
using System.IO;
using DyndDns.TrayApp.ViewModels;

namespace DyndDns.TrayApp;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        // AppContext.BaseDirectory points at the executable for single-file builds,
        // so the config folder stays next to dyndns.exe.
        var configDir = Path.Combine(AppContext.BaseDirectory, "config");
        Directory.CreateDirectory(configDir);
        ConfigureLogging(configDir);

        var vm = new MainViewModel(configDir);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);
    }

    private static void ConfigureLogging(string configDir)
    {
        Trace.Listeners.Add(new TextWriterTraceListener(Path.Combine(configDir, "dyndns.log")));
        Trace.AutoFlush = true;
    }
}
