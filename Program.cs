using Avalonia;
using Avalonia.Win32;
using System;

namespace NotifyIsland;

internal static class Program
{
    public static bool DemoMode { get; private set; }
    public static bool OpenSettingsOnStart { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        foreach (var a in args)
        {
            if (string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase))
                DemoMode = true;
            if (string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase))
                OpenSettingsOnStart = true;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new Win32PlatformOptions
            {
                CompositionMode = new[] { Win32CompositionMode.WinUIComposition, Win32CompositionMode.DirectComposition }
            })
            .LogToTrace();
}
