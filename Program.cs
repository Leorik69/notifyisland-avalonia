using Avalonia;
using Avalonia.Win32;
using System;

namespace NotifyIsland;

internal static class Program
{
    public static bool DemoMode { get; private set; }
    public static bool OpenSettingsOnStart { get; private set; }

    public static void AbsorbArgs(string[] args)
    {
        foreach (var a in args)
        {
            if (string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "-demo", StringComparison.OrdinalIgnoreCase))
                DemoMode = true;
            if (string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase))
                OpenSettingsOnStart = true;
        }
    }

    [STAThread]
    public static void Main(string[] args)
    {
        AbsorbArgs(args);
        AbsorbArgs(Environment.GetCommandLineArgs());
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
