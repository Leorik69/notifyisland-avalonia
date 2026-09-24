using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace NotifyIsland;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            var overlay = new OverlayWindow();
            desktop.MainWindow = overlay;
            // Tray is created on OverlayWindow.Opened (needs window handle / assets)
        }

        base.OnFrameworkInitializationCompleted();
    }
}
