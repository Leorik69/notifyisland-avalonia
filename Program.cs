using Avalonia;
using System;

namespace NotifyIsland;

internal static class Program
{
    public static bool DemoMode { get; private set; }
    public static bool SettingsMode { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        foreach (var a in args)
        {
            if (string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase))
                DemoMode = true;
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
