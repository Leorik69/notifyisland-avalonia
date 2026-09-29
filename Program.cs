using Avalonia;
using System;

namespace NotifyIsland;

internal static class Program
{
    public static bool SettingsMode { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        // 1.12.3: the app was dying on a plain click with NOTHING in the log — the tray
        // icon just vanished. An unhandled exception on a UI callback takes the process
        // down and, unless something catches it, leaves no trace here. Two handlers cover
        // the two ways it can escape: AppDomain for the CLR background/finalizer path,
        // and TaskScheduler.UnobservedTaskException for a faulted task nobody awaits.
        // Both log and then let the process go, which is still the honest behaviour -- but
        // now the log says why.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try { AppLog.Error("Fatal: unhandled exception", e.ExceptionObject as Exception); }
            catch { /* the logger is the last thing standing; do not throw from here */ }
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try { AppLog.Error("Fatal: unobserved task exception", e.Exception); }
            catch { /* as above */ }
            e.SetObserved();
        };

        foreach (var a in args)
        {
            if (string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase))
                SettingsMode = true;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
