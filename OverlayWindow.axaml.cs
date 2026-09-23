using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace NotifyIsland;

public partial class OverlayWindow : Window
{
    private readonly OverlayMachine _machine = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _demo = new() { Interval = TimeSpan.FromSeconds(2.4) };
    private bool _demoOn;

    public OverlayWindow()
    {
        InitializeComponent();
        EnableMorphTransitions();
        Opened += (_, _) => { Win32Overlay.ApplyNoActivate(this); PlaceTopCenter(); };
        KeyDown += OnKey;
        _clock.Tick += (_, _) => TickClock();
        _tick.Tick += (_, _) =>
        {
            var before = _machine.Snapshot().Kind;
            _machine.Tick(200);
            Paint();
            if (before != _machine.Snapshot().Kind) ApplySize();
        };
        _demo.Tick += (_, _) => { _machine.Dispatch(OverlayCommand.DemoNext); ApplySize(); Paint(); };
        _clock.Start();
        _tick.Start();
        TickClock();
        ApplySize();
        Paint();
        if (Program.DemoMode) StartDemo();
    }

    private void EnableMorphTransitions()
    {
        var duration = TimeSpan.FromMilliseconds(OverlayTokens.MorphMs);
        var softOut = new CubicEaseOut();
        // Width-only morph; height stays fixed across all kinds.
        Transitions = new Transitions
        {
            new DoubleTransition { Property = WidthProperty, Duration = duration, Easing = softOut },
        };
        Pill.Transitions = new Transitions
        {
            new DoubleTransition { Property = Border.WidthProperty, Duration = duration, Easing = softOut },
        };
        UnreadDot.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = duration, Easing = softOut },
        };
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F9) { if (_demoOn) StopDemo(); else StartDemo(); e.Handled = true; }
        else if (e.Key == Key.Escape) { _machine.Dispatch(OverlayCommand.Collapse); ApplySize(); Paint(); e.Handled = true; }
    }

    private void StartDemo()
    {
        _demoOn = true; _demo.Start();
        _machine.Dispatch(OverlayCommand.DemoNext); ApplySize(); Paint();
    }

    private void StopDemo()
    {
        _demoOn = false; _demo.Stop();
        _machine.Dispatch(OverlayCommand.Clear); ApplySize(); Paint();
    }

    private void TickClock()
    {
        ClockText.Text = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        var kind = _machine.Snapshot().Kind;
        if (kind is OverlayKind.Idle or OverlayKind.Collapsed)
            CollapsedRow.IsVisible = true;
    }

    private void ApplySize()
    {
        var snap = _machine.Snapshot();
        var h = OverlayTokens.CollapsedH;
        Width = snap.Width;
        Height = h;
        Pill.Width = snap.Width;
        Pill.Height = h;
        Pill.CornerRadius = new CornerRadius(h / 2);
        PlaceTopCenter();
    }

    private void PlaceTopCenter()
    {
        var screen = Screens.Primary ?? Screens.ScreenFromWindow(this);
        if (screen is null) return;
        var wa = screen.WorkingArea;
        var scale = RenderScaling;
        var pw = (int)Math.Round(Width * scale);
        var ph = (int)Math.Round(Height * scale);
        var maxX = Math.Max(wa.X, wa.X + wa.Width - pw);
        var maxY = Math.Max(wa.Y, wa.Y + wa.Height - ph);
        var x = Math.Clamp(wa.X + (wa.Width - pw) / 2, wa.X, maxX);
        // Fixed Y — island never expands upward.
        var y = Math.Clamp(wa.Y + 8, wa.Y, maxY);
        Position = new PixelPoint(x, y);
    }

    private void Paint()
    {
        var snap = _machine.Snapshot();
        var kind = snap.Kind;
        var p = snap.Payload;
        var overlayOn = kind is OverlayKind.Notification or OverlayKind.Progress or OverlayKind.Media
            or OverlayKind.Timer or OverlayKind.Error or OverlayKind.Expanded;

        OverlayPanel.IsVisible = overlayOn;
        CollapsedRow.IsVisible = !overlayOn;

        OverlayTitle.Text = string.IsNullOrWhiteSpace(p.Title) ? Fallback(kind) : p.Title;
        var sub = string.IsNullOrWhiteSpace(p.Subtitle) ? p.Body : p.Subtitle;
        if (kind == OverlayKind.Timer)
            sub = TimeSpan.FromSeconds(Math.Ceiling(p.RemainingSeconds)).ToString(@"mm\:ss");
        else if (kind == OverlayKind.Progress && string.IsNullOrWhiteSpace(sub))
            sub = $"{(int)Math.Round(p.Progress * 100)}%";
        OverlaySubtitle.Text = sub;

        OverlayProgress.Value = p.Progress * 100;
        OverlayProgress.IsVisible = kind is OverlayKind.Progress or OverlayKind.Media;

        OverlayTitle.Foreground = new SolidColorBrush(Color.Parse(kind == OverlayKind.Error ? OverlayTokens.ErrorHex : OverlayTokens.TextHex));
        AppIcon.Background = new SolidColorBrush(Color.Parse(kind == OverlayKind.Error ? OverlayTokens.ErrorHex : OverlayTokens.AccentHex));
        AppIconGlyph.Text = kind switch
        {
            OverlayKind.Media => "♪",
            OverlayKind.Timer => "◷",
            OverlayKind.Progress => "↑",
            OverlayKind.Error => "!",
            OverlayKind.Notification => "●",
            _ => "●"
        };

        MediaPlay.IsVisible = kind == OverlayKind.Media;
        MediaPlayGlyph.Text = p.Playing ? "||" : "▶";

        var unread = snap.UnreadCount;
        var showBadge = overlayOn && unread > 0 && kind is OverlayKind.Notification or OverlayKind.Expanded;
        UnreadBadge.IsVisible = showBadge;
        BadgeText.Text = unread > 99 ? "99+" : unread.ToString(CultureInfo.InvariantCulture);

        // Glowing unread dot on collapsed island
        var showDot = !overlayOn && unread > 0;
        UnreadDot.Opacity = showDot ? 1.0 : 0.0;

        ToolTip.SetTip(this, kind == OverlayKind.Idle ? "NotifyIsland" : OverlayTitle.Text);
    }

    private static string Fallback(OverlayKind kind) => kind switch
    {
        OverlayKind.Notification => "Уведомление",
        OverlayKind.Progress => "Прогресс",
        OverlayKind.Media => "Без названия",
        OverlayKind.Timer => "Таймер",
        OverlayKind.Error => "Ошибка",
        OverlayKind.Expanded => "Обзор",
        _ => ""
    };

    private void OnPillPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            var menu = new ContextMenu();
            menu.Items.Add(Menu("Demo F9", () => { if (_demoOn) StopDemo(); else StartDemo(); }));
            menu.Items.Add(Menu("Свернуть", () => { _machine.Dispatch(OverlayCommand.Collapse); ApplySize(); Paint(); }));
            menu.Items.Add(Menu("Выход", () => (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown()));
            menu.Open(Pill);
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            var kind = _machine.Snapshot().Kind;
            if (kind is OverlayKind.Idle or OverlayKind.Collapsed)
            {
                OpenActionCenter();
                e.Handled = true;
            }
        }
    }

    private static void OpenActionCenter()
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

    private static MenuItem Menu(string header, Action act)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => act();
        return item;
    }

    private void OnMediaPlay(object? sender, RoutedEventArgs e)
    {
        var next = OverlayMachine.Sanitize(_machine.Snapshot().Payload);
        next.Playing = !next.Playing;
        _machine.Dispatch(OverlayCommand.SetMedia, next);
        Paint();
    }
}
