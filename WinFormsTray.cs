using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Avalonia.Threading;

namespace NotifyIsland;

/// <summary>
/// Win32/WinForms NotifyIcon — reliable on Win11 Sandbox where Avalonia TrayIcon may stay hidden.
/// </summary>
internal sealed class WinFormsTray : IDisposable
{
    private readonly OverlayWindow _overlay;
    private readonly ClipboardHistory _clipboard;
    private readonly NotifyIcon _notify;
    private readonly ToolStripMenuItem _toggleIsland;
    private readonly ToolStripMenuItem _toggleWeather;
    private DateTime _lastClickUtc = DateTime.MinValue;
    private bool _trayUnread;         // which of the two icons the shell currently holds
    private bool _disposed;

    public WinFormsTray(OverlayWindow overlay, ClipboardHistory clipboard)
    {
        _overlay = overlay;
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
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

        _toggleWeather = new ToolStripMenuItem("Погода вкл");
        _toggleWeather.Click += (_, _) => Ui(_overlay.ToggleWeatherFromTray);

        var timerMenu = BuildTimerMenu();
        var clipboardMenu = BuildClipboardSubmenu();

        var exit = new ToolStripMenuItem("Выход");
        exit.Click += (_, _) =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d)
                Dispatcher.UIThread.Post(() => d.Shutdown());
        };

        var actionCenter = new ToolStripMenuItem("Центр уведомлений");
        actionCenter.Click += (_, _) => OpenActionCenter();

        // Plan §Task 10 Step 6 (mirrored from TrayService):
        //   Toggle island → sep → Action Center → clipboard submenu → sep
        //   → weather toggle → timer submenu → sep → Settings → Exit.
        menu.Items.Add(_toggleIsland);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(actionCenter);
        menu.Items.Add(clipboardMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggleWeather);
        menu.Items.Add(timerMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settings);
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
    }

    public void RefreshIcon(int unread)
    {
        try
        {
            // There are two icons and the unread state is a bool, so most calls land here with
            // nothing to change. Reassigning re-arms the shell's icon machinery for no reason, and
            // used to also pay the file read and PNG decode.
            //
            // The tooltip is NOT written here. It used to be, and the privacy-pause path overwrote
            // the whole string, so whichever ran last won and the two states could not both be
            // seen. The window owns the tooltip now — see OverlayWindow.UpdateTrayTooltip.
            if (_notify.Icon is not null && _trayUnread == (unread > 0))
                return;

            var old = _notify.Icon;
            _notify.Icon = LoadIcon(unread > 0);
            _trayUnread = unread > 0;
            old?.Dispose();
            _notify.Visible = true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("WinFormsTray.RefreshIcon failed", ex);
        }
        RefreshLabels();
    }

    /// <summary>
    /// Override the tray tooltip without changing the icon. Used by the privacy-pause
    /// status («NotifyIsland — пауза 30 мин»). WinForms NotifyIcon.Text has a 127-char cap;
    /// we truncate just in case a future caller hands in something longer.
    /// </summary>
    public void SetTooltip(string text)
    {
        try
        {
            _notify.Text = text.Length <= 127 ? text : text[..127];
        }
        catch (Exception ex)
        {
            AppLog.Warn("WinFormsTray.SetTooltip failed", ex);
        }
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

    /// <summary>Timer submenu — same presets the keyboard / context menu already expose.</summary>
    private ToolStripMenuItem BuildTimerMenu()
    {
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
        return timerMenu;
    }

    /// <summary>
    /// Clipboard submenu — last <see cref="OverlayTokens.TrayClipboardSubmenuItems"/> items
    /// from <see cref="ClipboardHistory"/>, newest-first. Clicking re-copies via
    /// <see cref="WindowsClipboardWriter"/>. Empty state shows "(пусто)".
    /// </summary>
    private ToolStripMenuItem BuildClipboardSubmenu()
    {
        var clipMenu = new ToolStripMenuItem("Буфер обмена");
        var items = _clipboard.SnapshotNewestFirst()
            .Take(OverlayTokens.TrayClipboardSubmenuItems)
            .ToList();
        if (items.Count == 0)
        {
            var empty = new ToolStripMenuItem("(пусто)") { Enabled = false };
            clipMenu.DropDownItems.Add(empty);
            return clipMenu;
        }
        foreach (var entry in items)
        {
            var captured = entry;
            var label = MakeClipboardLabel(captured);
            var item = new ToolStripMenuItem(label);
            item.Click += (_, _) =>
            {
                var ok = captured.Kind switch
                {
                    ClipboardItemKind.Text => WindowsClipboardWriter.WriteText(captured.Text ?? ""),
                    ClipboardItemKind.File or ClipboardItemKind.MultiFile =>
                        WindowsClipboardWriter.WriteFiles(captured.Paths ?? new List<string>()),
                    _ => false
                };
                if (ok)
                {
                    AppLog.Info($"WinFormsTray clipboard re-copy: kind={captured.Kind} ok");
                    RefreshIcon(0);
                }
                else
                    AppLog.Warn($"WinFormsTray clipboard re-copy failed: kind={captured.Kind}");
            };
            clipMenu.DropDownItems.Add(item);
        }
        return clipMenu;
    }

    private static string MakeClipboardLabel(ClipboardEntry e)
    {
        string label = e.Kind switch
        {
            ClipboardItemKind.Text => (e.Text ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim(),
            ClipboardItemKind.File => Path.GetFileName(e.Paths is { Count: > 0 } ? e.Paths[0] : ""),
            ClipboardItemKind.MultiFile => $"{e.Paths?.Count ?? 0} файлов",
            _ => ""
        };
        if (label.Length > 40) label = label[..39] + "…";
        return label;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// 2026-10-02 perf pass, two fixes in one method.
    /// <para>
    /// The leak: <c>Bitmap.GetHicon()</c> hands back a GDI handle the CALLER owns and must
    /// release with <c>DestroyIcon</c>. <c>Icon.FromHandle</c> does not take ownership and
    /// disposing it does not free the handle, so every single call leaked one HICON — and
    /// RefreshIcon runs on every unread change and every settings apply, for as long as the
    /// island is up.
    /// </para>
    /// <para>
    /// The cost: there are only two possible icons, but each call re-read the PNG from disk and
    /// re-decoded it. They are decoded once now and handed out as clones, which is a memory copy
    /// rather than a file read plus a decode.
    /// </para>
    /// </summary>
    private static Icon? _iconPlain;
    private static Icon? _iconUnread;

    private static Icon LoadIcon(bool unread)
    {
        var cached = unread ? _iconUnread : _iconPlain;
        if (cached is not null)
            return (Icon)cached.Clone();

        var name = unread ? "tray-unread.png" : "tray.png";
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
        if (!File.Exists(path))
            return SystemIcons.Application;

        using var bmp = new Bitmap(path);
        var hIcon = bmp.GetHicon();
        try
        {
            // Copy so disposing Bitmap does not invalidate the tray icon handle.
            using var tmp = Icon.FromHandle(hIcon);
            var icon = (Icon)tmp.Clone();
            if (unread) _iconUnread = icon; else _iconPlain = icon;
            return (Icon)icon.Clone();
        }
        finally
        {
            // The GetHicon handle is ours and nothing else took it — it must be destroyed or it
            // stays in the process's GDI table until exit.
            DestroyIcon(hIcon);
        }
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
