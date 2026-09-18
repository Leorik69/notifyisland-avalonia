using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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
    private Border Pill = null!;
    private TextBlock ClockText = null!;
    private StackPanel OverlayPanel = null!;
    private TextBlock OverlayTitle = null!;
    private TextBlock OverlaySubtitle = null!;
    private ProgressBar OverlayProgress = null!;
    private TextBlock OverlayTimer = null!;
    private Button MediaPlay = null!;
    private TextBlock MediaPlayGlyph = null!;

    private void Bind()
    {
        Pill = this.FindControl<Border>("Pill")!;
        ClockText = this.FindControl<TextBlock>("ClockText")!;
        OverlayPanel = this.FindControl<StackPanel>("OverlayPanel")!;
        OverlayTitle = this.FindControl<TextBlock>("OverlayTitle")!;
        OverlaySubtitle = this.FindControl<TextBlock>("OverlaySubtitle")!;
        OverlayProgress = this.FindControl<ProgressBar>("OverlayProgress")!;
        OverlayTimer = this.FindControl<TextBlock>("OverlayTimer")!;
        MediaPlay = this.FindControl<Button>("MediaPlay")!;
        MediaPlayGlyph = this.FindControl<TextBlock>("MediaPlayGlyph")!;
    }

    public OverlayWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Bind();
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
        if (_machine.Snapshot().Kind is OverlayKind.Idle or OverlayKind.Collapsed)
            ClockText.IsVisible = true;
    }

    private void ApplySize()
    {
        var snap = _machine.Snapshot();
        Width = snap.Width; Height = snap.Height;
        Pill.Width = snap.Width; Pill.Height = snap.Height;
        Pill.CornerRadius = new CornerRadius(snap.Height / 2);
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
        Position = new PixelPoint(wa.X + (wa.Width - pw) / 2, wa.Y + 8);
    }

    private void Paint()
    {
        var snap = _machine.Snapshot();
        var kind = snap.Kind; var p = snap.Payload;
        var overlayOn = kind is OverlayKind.Notification or OverlayKind.Progress or OverlayKind.Media
            or OverlayKind.Timer or OverlayKind.Error or OverlayKind.Expanded;
        OverlayPanel.IsVisible = overlayOn;
        ClockText.IsVisible = !overlayOn;
        OverlayTitle.Text = string.IsNullOrWhiteSpace(p.Title) ? Fallback(kind) : p.Title;
        OverlaySubtitle.Text = string.IsNullOrWhiteSpace(p.Subtitle) ? p.Body : p.Subtitle;
        OverlayProgress.Value = p.Progress * 100;
        OverlayProgress.IsVisible = kind is OverlayKind.Progress or OverlayKind.Media;
        OverlayTimer.IsVisible = kind == OverlayKind.Timer;
        OverlayTimer.Text = kind == OverlayKind.Timer ? TimeSpan.FromSeconds(Math.Ceiling(p.RemainingSeconds)).ToString(@"mm\:ss") : "";
        OverlayTitle.Foreground = new SolidColorBrush(Color.Parse(kind == OverlayKind.Error ? OverlayTokens.ErrorHex : OverlayTokens.TextHex));
        MediaPlay.IsVisible = kind == OverlayKind.Media;
        MediaPlayGlyph.Text = p.Playing ? "||" : ">";
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
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        var menu = new ContextMenu();
        menu.Items.Add(Menu("Demo F9", () => { if (_demoOn) StopDemo(); else StartDemo(); }));
        menu.Items.Add(Menu("Свернуть", () => { _machine.Dispatch(OverlayCommand.Collapse); ApplySize(); Paint(); }));
        menu.Items.Add(Menu("Выход", () => (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown()));
        menu.Open(Pill);
        e.Handled = true;
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
