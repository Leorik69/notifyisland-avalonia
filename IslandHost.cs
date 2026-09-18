using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace NotifyIsland;

internal static class IslandHost
{
    public static OverlayWindow? Overlay { get; set; }
    public static SettingsWindow? Settings { get; set; }

    public static void ToggleOverlay()
    {
        if (Overlay is null) return;
        if (Overlay.IsVisible)
        {
            Overlay.Hide();
            PrefsStore.Mutate(p => p.OverlayVisible = false);
        }
        else
        {
            Overlay.Show();
            PrefsStore.Mutate(p => p.OverlayVisible = true);
        }
    }

    public static void OpenSettings()
    {
        if (Settings is { IsVisible: true })
        {
            Settings.Activate();
            return;
        }
        Settings = new SettingsWindow();
        Settings.Closed += (_, _) => Settings = null;
        Settings.Show();
    }

    public static void ToggleDemo()
    {
        Overlay?.ToggleDemo();
    }

    public static void Exit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desk)
            desk.Shutdown();
    }
}
