using Avalonia;
using System;

namespace NotifyIsland;

internal static class Program
{
    public static bool DemoMode { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        foreach (var a in args)
        {
            if (string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase))
                DemoMode = true;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
