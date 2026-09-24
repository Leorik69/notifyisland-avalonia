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
/// Left-click: show/hide island. Right-click: NativeMenu. Double-click / menu: Action Center.
/// Pill single-click pins; pill double-click opens Action Center (see OverlayWindow).
/// </summary>
internal sealed class TrayService : IDisposable
{
    private readonly OverlayWindow _overlay;
    private readonly TrayIcon _tray;
    private readonly TrayIcons _icons;
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

        var timerMenu = new NativeMenuItem("Таймер");
        var timerSub = new NativeMenu();
        void AddPreset(string label, int min)
        {
            var item = new NativeMenuItem(label);
            item.Click += (_, _) => Dispatcher.UIThread.Post(() => _overlay.StartCountdownMinutes(min));
            timerSub.Add(item);
        }
        AddPreset("1 мин", 1);
        AddPreset("5 мин", 5);
        AddPreset("10 мин", 10);
        AddPreset("25 мин", 25);
        var custom = new NativeMenuItem("По умолчанию…");
        custom.Click += (_, _) => Dispatcher.UIThread.Post(() =>
            _overlay.StartCountdownMinutes(_overlay.Settings.TimerDefaultMinutes));
        timerSub.Add(custom);
        var cancel = new NativeMenuItem("Отменить");
        cancel.Click += (_, _) => Dispatcher.UIThread.Post(_overlay.CancelTimer);
        timerSub.Add(cancel);
        timerMenu.Menu = timerSub;

        var exit = new NativeMenuItem("Выход");
        exit.Click += (_, _) =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
                d.Shutdown();
        };

        var actionCenter = new NativeMenuItem("Центр уведомлений");
        actionCenter.Click += (_, _) => OpenActionCenter();

        var menu = new NativeMenu();
        menu.Add(settings);
        menu.Add(actionCenter);
        menu.Add(_toggleIsland);
        menu.Add(_toggleDemo);
        menu.Add(_toggleWeather);
        menu.Add(timerMenu);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exit);
        _tray.Menu = menu;

        _tray.Clicked += OnTrayClicked;
        RefreshIcon(0);
        RefreshLabels();

        // Keep a strong reference — TrayIcons must not be GC'd while tray is live.
        _icons = new TrayIcons { _tray };
        if (Application.Current is { } app)
        {
            TrayIcon.SetIcons(app, _icons);
            AppLog.Warn($"TrayService registered icons count={_icons.Count}");
        }
        else
            AppLog.Warn("TrayService: Application.Current is null — tray may not show");
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
            WindowIcon? icon = null;
            // Prefer file next to exe (reliable on Win11 publish layout)
            var name = unread > 0 ? "tray-unread.png" : "tray.png";
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
            if (File.Exists(path))
                icon = new WindowIcon(path);
            else
            {
                var uri = unread > 0
                    ? new Uri("avares://NotifyIsland/Assets/tray-unread.png")
                    : new Uri("avares://NotifyIsland/Assets/tray.png");
                using var stream = AssetLoader.Open(uri);
                icon = new WindowIcon(stream);
            }

            _tray.Icon = icon;
            _tray.IsVisible = true;
            _tray.ToolTipText = unread > 0
                ? $"NotifyIsland — непрочитанных: {unread}"
                : "NotifyIsland";
        }
        catch (Exception ex)
        {
            AppLog.Warn("TrayService.RefreshIcon failed", ex);
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
        DispatcherTimer.RunOnce(() =>
        {
            if (_lastClickUtc == DateTime.MinValue) return;
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
