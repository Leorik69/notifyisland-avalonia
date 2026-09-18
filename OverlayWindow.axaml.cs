using System;
using System.Globalization;
using Avalonia;
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
    private readonly DispatcherTimer _demo = new() { Interval = TimeSpan.FromSeconds(2.6) };
    private readonly DispatcherTimer _winFit = new() { Interval = TimeSpan.FromMilliseconds(20) };
    private bool _demoOn;
    private OverlayKind _lastKind = OverlayKind.Idle;
    private double _lastW = OverlayTokens.CollapsedW;
    private double _lastH = OverlayTokens.CollapsedH;
    private int _lastTimerSec = -1;
    private bool _hiding;

    public OverlayWindow()
    {
        InitializeComponent();
        IslandHost.Overlay = this;
        Opened += (_, _) =>
        {
            Win32Overlay.ApplyNoActivate(this);
            PlaceTopCenter();
        };
        KeyDown += OnKey;
        PrefsStore.Changed += OnPrefsChanged;
        Closed += (_, _) => PrefsStore.Changed -= OnPrefsChanged;
        _clock.Tick += (_, _) => TickClock();
        _tick.Tick += (_, _) =>
        {
            var before = _machine.Snapshot().Kind;
            _machine.Tick(200);
            Paint();
            if (before != _machine.Snapshot().Kind) ApplySize();
        };
        _demo.Tick += (_, _) => { _machine.Dispatch(OverlayCommand.DemoNext); ApplySize(); Paint(); };
        _winFit.Tick += (_, _) =>
        {
            _winFit.Stop();
            Width = _lastW;
            Height = _lastH;
            PlaceTopCenter();
        };
        _clock.Start();
        _tick.Start();
        ApplyTheme();
        TickClock();
        ApplySize();
        Paint();
        if (Program.DemoMode) StartDemo();
        if (Program.OpenSettingsOnStart)
            Dispatcher.UIThread.Post(IslandHost.OpenSettings, DispatcherPriority.Background);
        if (!PrefsStore.Current.OverlayVisible) Hide();
    }

    public bool DemoRunning => _demoOn;

    public void ToggleDemo()
    {
        if (_demoOn) StopDemo();
        else StartDemo();
    }

    public void PreviewAnimation()
    {
        _machine.Dispatch(OverlayCommand.Notify, new OverlayPayload { Title = "Preview", Body = "Theme animation" });
        ApplySize();
        Paint();
        IslandAnimator.Pulse(Pill, Motion.PulseMs);
    }

    public async void SetVisibleAnimated(bool visible)
    {
        if (visible)
        {
            _hiding = false;
            Opacity = 0;
            Show();
            PrefsStore.Mutate(p => p.OverlayVisible = true);
            IslandAnimator.WireOpacity(this, Motion.FadeMs);
            Opacity = 1;
            IslandAnimator.Pulse(Pill, Motion.PulseMs);
            return;
        }
        if (_hiding) return;
        _hiding = true;
        IslandAnimator.WireOpacity(this, Motion.FadeMs);
        Opacity = 0;
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Delay(Motion.FadeMs + 20);
            if (!_hiding) return;
            Hide();
            Opacity = 1;
            PrefsStore.Mutate(p => p.OverlayVisible = false);
            _hiding = false;
        });
    }

    private void OnPrefsChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyTheme();
            ApplySize();
            Paint();
        });
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F9) { ToggleDemo(); e.Handled = true; }
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
        var fmt = PrefsStore.Current.ClockFormat;
        ClockText.Text = DateTime.Now.ToString(fmt, CultureInfo.InvariantCulture);
        if (_machine.Snapshot().Kind is OverlayKind.Idle or OverlayKind.Collapsed)
            ClockText.Opacity = 1;
    }

    private void ApplyTheme()
    {
        IslandAnimator.WireLayout(Pill, Motion.MorphMs);
        IslandAnimator.WireLayout(Glow, Motion.MorphMs);
        IslandAnimator.WireOpacity(ClockText, Motion.FadeMs);
        IslandAnimator.WireOpacity(OverlayPanel, Motion.FadeMs);
        IslandAnimator.WireOpacity(OverlayProgress, Motion.FadeMs);
        IslandAnimator.WireOpacity(OverlayTimer, Motion.FadeMs);
        IslandAnimator.WireOpacity(OverlayStack, Motion.FadeMs);
        IslandAnimator.WireProgress(OverlayProgress, Motion.ProgressMs);
        IslandAnimator.WireOpacity(this, Motion.FadeMs);

        var pal = PaletteCatalog.Get(PrefsStore.Current.PaletteId);
        var font = FontCatalog.Get(PrefsStore.Current.FontId);
        var family = new FontFamily(font.Family);
        var prefs = PrefsStore.Current;
        var opacity = prefs.Opacity;
        var glowA = (byte)Math.Clamp(40 + prefs.GlowStrength * 140, 0, 255);
        Pill.Background = new SolidColorBrush(pal.Background, opacity);
        Pill.BorderBrush = new SolidColorBrush(pal.Border);
        Pill.BorderThickness = new Thickness(prefs.BorderThickness);
        Pill.BoxShadow = new BoxShadows(new BoxShadow
        {
            Blur = 6 + prefs.GlowStrength * 22,
            Spread = 0,
            OffsetX = 0,
            OffsetY = 0,
            Color = Color.FromArgb(glowA, pal.Glow.R, pal.Glow.G, pal.Glow.B)
        });
        Glow.Background = new SolidColorBrush(pal.Glow, 0.12 + prefs.GlowStrength * 0.28);
        ClockText.Foreground = new SolidColorBrush(pal.Text);
        ClockText.FontFamily = family;
        OverlayTitle.FontFamily = family;
        OverlaySubtitle.FontFamily = family;
        OverlayStack.FontFamily = family;
        OverlayTimer.FontFamily = family;
        OverlayTitle.Foreground = new SolidColorBrush(pal.Text);
        OverlaySubtitle.Foreground = new SolidColorBrush(pal.TextSecondary);
        OverlayStack.Foreground = new SolidColorBrush(pal.TextSecondary);
        OverlayProgress.Foreground = new SolidColorBrush(pal.Accent);
        KindIcon.Fill = new SolidColorBrush(pal.Accent);
        KindGlyph.Foreground = new SolidColorBrush(pal.Accent);
        MediaPlayIcon.Fill = new SolidColorBrush(pal.Accent);
        MediaPlayGlyph.Foreground = new SolidColorBrush(pal.Accent);
    }

    private void ApplySize()
    {
        var snap = _machine.Snapshot();
        var prefs = PrefsStore.Current;
        var toW = snap.Kind is OverlayKind.Idle or OverlayKind.Collapsed ? prefs.IdleWidth : snap.Width;
        var toH = snap.Kind is OverlayKind.Idle or OverlayKind.Collapsed ? prefs.IdleHeight : snap.Height;
        if (toW > Width || toH > Height)
        {
            Width = toW;
            Height = toH;
            PlaceTopCenter();
        }
        else
        {
            _winFit.Interval = TimeSpan.FromMilliseconds(Motion.MorphMs);
            _winFit.Stop();
            _winFit.Start();
        }

        Pill.Width = toW;
        Pill.Height = toH;
        var radius = prefs.CornerRadius <= 0 ? toH / 2 : prefs.CornerRadius;
        Pill.CornerRadius = new CornerRadius(radius);
        Glow.Width = toW + 10 + prefs.GlowStrength * 8;
        Glow.Height = toH + 8 + prefs.GlowStrength * 6;
        Glow.CornerRadius = new CornerRadius(radius + 4);
        _lastW = toW;
        _lastH = toH;
        PlaceTopCenter();
        if (snap.Kind != _lastKind)
        {
            PlayMotion(snap.Kind);
            _lastKind = snap.Kind;
        }
    }

    private void PlayMotion(OverlayKind kind)
    {
        var extra = PrefsStore.Current.Animation;
        if (extra is "pulse" or "morph")
            IslandAnimator.Pulse(Pill, Motion.PulseMs);
        if (extra == "breathe")
            IslandAnimator.Breathe(Glow, Motion.BreatheMs);
        if (kind is OverlayKind.Notification or OverlayKind.Stack)
            IslandAnimator.Pulse(Glow, Motion.PulseMs);
    }

    private void PlaceTopCenter()
    {
        var screen = Screens.Primary ?? Screens.ScreenFromWindow(this);
        if (screen is null) return;
        var wa = screen.WorkingArea;
        var scale = RenderScaling;
        var pw = (int)Math.Round(Width * scale);
        Position = new PixelPoint(wa.X + (wa.Width - pw) / 2, wa.Y + 8);
    }

    private void Paint()
    {
        var pal = PaletteCatalog.Get(PrefsStore.Current.PaletteId);
        var snap = _machine.Snapshot();
        var kind = snap.Kind; var p = snap.Payload;
        var overlayOn = kind is OverlayKind.Notification or OverlayKind.Progress or OverlayKind.Media
            or OverlayKind.Timer or OverlayKind.Error or OverlayKind.Expanded or OverlayKind.Stack;
        OverlayPanel.Opacity = overlayOn ? 1 : 0;
        OverlayPanel.IsHitTestVisible = overlayOn;
        ClockText.Opacity = overlayOn ? 0 : 1;
        OverlayTitle.Text = string.IsNullOrWhiteSpace(p.Title) ? Fallback(kind) : p.Title;
        OverlaySubtitle.Text = string.IsNullOrWhiteSpace(p.Subtitle) ? p.Body : p.Subtitle;
        OverlayStack.Text = p.Line2;
        OverlayStack.Opacity = kind == OverlayKind.Stack && !string.IsNullOrWhiteSpace(p.Line2) ? 1 : 0;
        OverlayProgress.Opacity = kind is OverlayKind.Progress or OverlayKind.Media ? 1 : 0;
        OverlayProgress.Value = p.Progress * 100;
        OverlayTimer.Opacity = kind == OverlayKind.Timer ? 1 : 0;
        var sec = (int)Math.Ceiling(p.RemainingSeconds);
        OverlayTimer.Text = kind == OverlayKind.Timer ? TimeSpan.FromSeconds(sec).ToString(@"mm\:ss") : "";
        if (kind == OverlayKind.Timer && sec != _lastTimerSec)
        {
            _lastTimerSec = sec;
            IslandAnimator.TickPop(OverlayTimer);
        }
        OverlayTitle.Foreground = new SolidColorBrush(kind == OverlayKind.Error ? pal.Error : pal.Text);
        MediaPlay.IsVisible = kind == OverlayKind.Media;
        var glyph = IslandIcons.ForKind(kind, p.Playing);
        var style = PrefsStore.Current.IconStyle;
        if (style == "mdl2")
        {
            KindIcon.IsVisible = false;
            KindGlyph.IsVisible = overlayOn;
            KindGlyph.Text = IslandIcons.Mdl2(glyph);
            MediaPlayIcon.IsVisible = false;
            MediaPlayGlyph.IsVisible = true;
            MediaPlayGlyph.Text = IslandIcons.Mdl2(p.Playing ? IslandGlyph.MediaPause : IslandGlyph.MediaPlay);
        }
        else
        {
            KindGlyph.IsVisible = false;
            KindIcon.IsVisible = overlayOn;
            KindIcon.Data = IslandIcons.Geometry(glyph, style);
            MediaPlayGlyph.IsVisible = false;
            MediaPlayIcon.IsVisible = true;
            MediaPlayIcon.Data = IslandIcons.Geometry(p.Playing ? IslandGlyph.MediaPause : IslandGlyph.MediaPlay, style);
        }
        ToolTip.SetTip(this, kind == OverlayKind.Idle ? "NotifyIsland" : OverlayTitle.Text);
    }

    private static string Fallback(OverlayKind kind) => kind switch
    {
        OverlayKind.Notification => "Notification",
        OverlayKind.Stack => "Queue",
        OverlayKind.Progress => "Progress",
        OverlayKind.Media => "Untitled",
        OverlayKind.Timer => "Timer",
        OverlayKind.Error => "Error",
        OverlayKind.Expanded => "Overview",
        _ => ""
    };

    private void OnPillPressed(object? sender, PointerPressedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        if (pt.Properties.IsLeftButtonPressed)
        {
            var kind = _machine.Snapshot().Kind;
            if (kind is OverlayKind.Idle or OverlayKind.Collapsed)
            {
                _machine.Dispatch(OverlayCommand.Expand, new OverlayPayload
                {
                    Title = DateTime.Now.ToString("dddd", CultureInfo.CurrentCulture),
                    Body = DateTime.Now.ToString("d MMM", CultureInfo.CurrentCulture)
                });
                ApplySize(); Paint();
            }
            else if (kind == OverlayKind.Expanded)
            {
                _machine.Dispatch(OverlayCommand.Collapse); ApplySize(); Paint();
            }
            e.Handled = true;
            return;
        }
        if (!pt.Properties.IsRightButtonPressed) return;
        var menu = new ContextMenu();
        menu.Items.Add(Menu("Settings", IslandHost.OpenSettings));
        menu.Items.Add(Menu(_demoOn ? "Stop demo" : "Demo F9", ToggleDemo));
        menu.Items.Add(Menu("Hide overlay", () => IslandHost.ToggleOverlay()));
        menu.Items.Add(Menu("Exit", IslandHost.Exit));
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
