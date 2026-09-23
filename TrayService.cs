using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;

namespace NotifyIsland;

/// <summary>
/// System tray (notify area): outline IslandIcons-style tray.png / tray-unread.png.
/// Left-click: show/hide island. Right-click: NativeMenu. Double-click: Action Center.
/// </summary>
internal sealed class TrayService : IDisposable
{
    private readonly OverlayWindow _overlay;
    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _toggleIsland;
    private readonly NativeMenuItem _toggleWeather;
    private readonly NativeMenuItem _toggleDemo;
    private DateTime _lastClickUtc = DateTime.MinValue;
    private bool _disposed;

    public TrayService(OverlayWindow overlay)
    {
        _overlay = overlay;
        _tray = new TrayIcon
        {
            IsVisible = true,
            ToolTipText = "NotifyIsland"
        };

        var settings = new NativeMenuItem("Открыть настройки");
        settings.Click += (_, _) => Dispatcher.UIThread.Post(_overlay.OpenSettings);

        _toggleIsland = new NativeMenuItem("Показать/скрыть островок");
        _toggleIsland.Click += (_, _) => Dispatcher.UIThread.Post(_overlay.ToggleIslandVisible);

        _toggleDemo = new NativeMenuItem("Демо вкл");
        _toggleDemo.Click += (_, _) => Dispatcher.UIThread.Post(_overlay.ToggleDemoFromTray);

        _toggleWeather = new NativeMenuItem("Погода вкл");
        _toggleWeather.Click += (_, _) => Dispatcher.UIThread.Post(_overlay.ToggleWeatherFromTray);

        var exit = new NativeMenuItem("Выход");
        exit.Click += (_, _) =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
                d.Shutdown();
        };

        var menu = new NativeMenu();
        menu.Add(settings);
        menu.Add(_toggleIsland);
        menu.Add(_toggleDemo);
        menu.Add(_toggleWeather);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exit);
        _tray.Menu = menu;

        _tray.Clicked += OnTrayClicked;
        RefreshIcon(0);
        RefreshLabels();

        if (Application.Current is { } app)
            TrayIcon.SetIcons(app, new TrayIcons { _tray });
    }

    public void RefreshLabels()
    {
        var s = _overlay.Settings;
        _toggleIsland.Header = s.IslandVisible ? "Скрыть островок" : "Показать островок";
        _toggleWeather.Header = s.WeatherEnabled ? "Погода выкл" : "Погода вкл";
        _toggleDemo.Header = _overlay.IsDemoRunning ? "Демо выкл" : "Демо вкл";
    }

    public void RefreshIcon(int unread)
    {
        try
        {
            var uri = unread > 0
                ? new Uri("avares://NotifyIsland/Assets/tray-unread.png")
                : new Uri("avares://NotifyIsland/Assets/tray.png");
            using var stream = AssetLoader.Open(uri);
            _tray.Icon = new WindowIcon(stream);
            _tray.ToolTipText = unread > 0
                ? $"NotifyIsland — непрочитанных: {unread}"
                : "NotifyIsland";
        }
        catch (Exception ex)
        {
            AppLog.Warn("TrayService.RefreshIcon failed", ex);
            try
            {
                var name = unread > 0 ? "tray-unread.png" : "tray.png";
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
                if (File.Exists(path))
                    _tray.Icon = new WindowIcon(path);
            }
            catch { /* ignore */ }
        }
        RefreshLabels();
    }

    private void OnTrayClicked(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastClickUtc).TotalMilliseconds < 400)
        {
            _lastClickUtc = DateTime.MinValue;
            OpenActionCenter();
            return;
        }

        _lastClickUtc = now;
        // Deferred single-click: toggle island if no second click arrives.
        DispatcherTimer.RunOnce(() =>
        {
            if (_lastClickUtc == DateTime.MinValue) return; // double-click consumed
            if ((DateTime.UtcNow - _lastClickUtc).TotalMilliseconds >= 350)
                _overlay.ToggleIslandVisible();
        }, TimeSpan.FromMilliseconds(380));
    }

    internal static void OpenActionCenter()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-actioncenter:",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn("OpenActionCenter failed", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _tray.Clicked -= OnTrayClicked;
            _tray.IsVisible = false;
            _tray.Dispose();
            if (Application.Current is { } app)
                TrayIcon.SetIcons(app, new TrayIcons());
        }
        catch { /* ignore */ }
    }
}
