using Avalonia;
using Avalonia.Win32;
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
            .With(new Win32PlatformOptions
            {
                CompositionMode = new[] { Win32CompositionMode.WinUIComposition, Win32CompositionMode.DirectComposition }
            })
            .LogToTrace();
}
