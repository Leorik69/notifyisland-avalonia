using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Avalonia.Threading;

namespace NotifyIsland;

/// <summary>
/// Win32/WinForms NotifyIcon — reliable on Win11 Sandbox where Avalonia TrayIcon may stay hidden.
/// </summary>
internal sealed class WinFormsTray : IDisposable
{
    private readonly OverlayWindow _overlay;
    private readonly NotifyIcon _notify;
    private readonly ToolStripMenuItem _toggleIsland;
    private readonly ToolStripMenuItem _toggleWeather;
    private readonly ToolStripMenuItem _toggleDemo;
    private DateTime _lastClickUtc = DateTime.MinValue;
    private bool _disposed;

    public WinFormsTray(OverlayWindow overlay)
    {
        _overlay = overlay;
        _notify = new NotifyIcon
        {
            Visible = true,
            Text = "NotifyIsland",
            Icon = LoadIcon(unread: false)
        };

        var menu = new ContextMenuStrip();
        var settings = new ToolStripMenuItem("Открыть настройки");
        settings.Click += (_, _) => Ui(_overlay.OpenSettings);

        _toggleIsland = new ToolStripMenuItem("Скрыть островок");
        _toggleIsland.Click += (_, _) => Ui(_overlay.ToggleIslandVisible);

        _toggleDemo = new ToolStripMenuItem("Демо вкл");
        _toggleDemo.Click += (_, _) => Ui(_overlay.ToggleDemoFromTray);

        _toggleWeather = new ToolStripMenuItem("Погода вкл");
        _toggleWeather.Click += (_, _) => Ui(_overlay.ToggleWeatherFromTray);

        var timerMenu = new ToolStripMenuItem("Таймер");
        void AddPreset(string label, int min)
        {
            var item = new ToolStripMenuItem(label);
            var minutes = min;
            item.Click += (_, _) => Ui(() => _overlay.StartCountdownMinutes(minutes));
            timerMenu.DropDownItems.Add(item);
        }
        AddPreset("1 мин", 1);
        AddPreset("5 мин", 5);
        AddPreset("10 мин", 10);
        AddPreset("25 мин", 25);
        var custom = new ToolStripMenuItem("По умолчанию…");
        custom.Click += (_, _) => Ui(() =>
            _overlay.StartCountdownMinutes(_overlay.Settings.TimerDefaultMinutes));
        timerMenu.DropDownItems.Add(custom);
        var cancel = new ToolStripMenuItem("Отменить");
        cancel.Click += (_, _) => Ui(_overlay.CancelTimer);
        timerMenu.DropDownItems.Add(cancel);

        var exit = new ToolStripMenuItem("Выход");
        exit.Click += (_, _) =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d)
                Dispatcher.UIThread.Post(() => d.Shutdown());
        };

        var actionCenter = new ToolStripMenuItem("Центр уведомлений");
        actionCenter.Click += (_, _) => OpenActionCenter();

        menu.Items.Add(settings);
        menu.Items.Add(actionCenter);
        menu.Items.Add(_toggleIsland);
        menu.Items.Add(_toggleDemo);
        menu.Items.Add(_toggleWeather);
        menu.Items.Add(timerMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);
        _notify.ContextMenuStrip = menu;

        _notify.MouseClick += OnMouseClick;
        _notify.MouseDoubleClick += (_, _) => OpenActionCenter();
        RefreshLabels();
        AppLog.Warn("WinFormsTray visible");
    }

    private static void Ui(Action act) => Dispatcher.UIThread.Post(act);

    public void RefreshLabels()
    {
        var s = _overlay.Settings;
        _toggleIsland.Text = s.IslandVisible ? "Скрыть островок" : "Показать островок";
        _toggleWeather.Text = s.WeatherEnabled ? "Погода выкл" : "Погода вкл";
        _toggleDemo.Text = _overlay.IsDemoRunning ? "Демо выкл" : "Демо вкл";
    }

    public void RefreshIcon(int unread)
    {
        try
        {
            var old = _notify.Icon;
            _notify.Icon = LoadIcon(unread > 0);
            old?.Dispose();
            _notify.Text = unread > 0
                ? $"NotifyIsland ({Math.Min(unread, 99)})"
                : "NotifyIsland";
            _notify.Visible = true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("WinFormsTray.RefreshIcon failed", ex);
        }
        RefreshLabels();
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var now = DateTime.UtcNow;
        if ((now - _lastClickUtc).TotalMilliseconds < 400)
        {
            _lastClickUtc = DateTime.MinValue;
            OpenActionCenter();
            return;
        }
        _lastClickUtc = now;
        var stamp = _lastClickUtc;
        var timer = new System.Windows.Forms.Timer { Interval = 380 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            if (_lastClickUtc == stamp)
                Ui(_overlay.ToggleIslandVisible);
        };
        timer.Start();
    }

    private static void OpenActionCenter()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "ms-actioncenter:", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("OpenActionCenter failed", ex);
        }
    }

    private static Icon LoadIcon(bool unread)
    {
        var name = unread ? "tray-unread.png" : "tray.png";
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
        if (!File.Exists(path))
            return SystemIcons.Application;
        using var bmp = new Bitmap(path);
        var hIcon = bmp.GetHicon();
        // Copy so disposing Bitmap does not invalidate the tray icon handle.
        using var tmp = Icon.FromHandle(hIcon);
        return (Icon)tmp.Clone();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _notify.Visible = false;
            _notify.Dispose();
        }
        catch { /* ignore */ }
    }
}
