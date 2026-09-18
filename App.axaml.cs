using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace NotifyIsland;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        PrefsStore.Load();
        Ui.Apply(PrefsStore.Current.UiLanguage);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = new OverlayWindow();
            if (TrayIcon.GetIcons(this) is { Count: > 0 } icons)
            {
                icons[0].Clicked += (_, _) => IslandHost.OpenSettings();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrayToggle(object? sender, System.EventArgs e) => IslandHost.ToggleOverlay();

    private void OnTraySettings(object? sender, System.EventArgs e) => IslandHost.OpenSettings();

    private void OnTrayDemo(object? sender, System.EventArgs e) => IslandHost.ToggleDemo();

    private void OnTrayExit(object? sender, System.EventArgs e) => IslandHost.Exit();
}
