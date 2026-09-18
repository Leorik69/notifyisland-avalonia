using Avalonia;
using Avalonia.Win32;
using System;

namespace NotifyIsland;

internal static class Program
{
    public static bool DemoMode { get; private set; }
    public static bool OpenSettingsOnStart { get; private set; }
    public static bool MotionDebug { get; private set; }
    public static bool ForceDirectComposition { get; private set; }
    public static bool Diagnostics { get; private set; }

    public static string CompositionLabel =>
        ForceDirectComposition ? "DirectComposition" : "WinUIComposition+DComp-fallback";

    public static void AbsorbArgs(string[] args)
    {
        foreach (var a in args)
        {
            if (string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "-demo", StringComparison.OrdinalIgnoreCase))
                DemoMode = true;
            if (string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase))
                OpenSettingsOnStart = true;
            if (string.Equals(a, "--motion-debug", StringComparison.OrdinalIgnoreCase))
            {
                MotionDebug = true;
                DemoMode = true;
            }
            if (string.Equals(a, "--dcomp", StringComparison.OrdinalIgnoreCase))
                ForceDirectComposition = true;
            if (string.Equals(a, "--diagnostics", StringComparison.OrdinalIgnoreCase))
                Diagnostics = true;
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
                CompositionMode = ForceDirectComposition
                    ? new[] { Win32CompositionMode.DirectComposition }
                    : new[] { Win32CompositionMode.WinUIComposition, Win32CompositionMode.DirectComposition }
            })
            .LogToTrace();
}
