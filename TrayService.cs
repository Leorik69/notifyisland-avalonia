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
/// System tray: outline notify icon (Assets/tray*.png), quick NativeMenu,
/// double-click → Action Center, tooltip with unread count.
/// </summary>
internal sealed class TrayService : IDisposable
{
    private readonly OverlayWindow _overlay;
    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _toggleIsland;
    private readonly NativeMenuItem _toggleWeather;
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

        _toggleIsland = new NativeMenuItem("Скрыть островок");
        _toggleIsland.Click += (_, _) => _overlay.ToggleIslandVisible();

        _toggleWeather = new NativeMenuItem("Погода выкл");
        _toggleWeather.Click += (_, _) => _overlay.ToggleWeatherFromTray();

        var openAc = new NativeMenuItem("Открыть центр уведомлений");
        openAc.Click += (_, _) => OpenActionCenter();

        var settings = new NativeMenuItem("Настройки…");
        settings.Click += (_, _) => _overlay.OpenSettings();

        var exit = new NativeMenuItem("Выход");
        exit.Click += (_, _) =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
                d.Shutdown();
        };

        var menu = new NativeMenu();
        menu.Add(openAc);
        menu.Add(_toggleIsland);
        menu.Add(_toggleWeather);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(settings);
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
            // Fallback: try file path next to exe
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
        // Single click: NativeMenu is shown by the platform when Menu is set;
        // on some hosts Clicked fires without opening menu — no-op is fine.
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
