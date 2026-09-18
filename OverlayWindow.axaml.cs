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
    private readonly DispatcherTimer _demo = new() { Interval = TimeSpan.FromSeconds(2.4) };
    private bool _demoOn;
    private OverlayKind _lastKind = OverlayKind.Idle;
    private double _lastW = OverlayTokens.CollapsedW;
    private double _lastH = OverlayTokens.CollapsedH;

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
        _clock.Start();
        _tick.Start();
        ApplyTheme();
        TickClock();
        ApplySize();
        Paint();
        if (Program.DemoMode) StartDemo();
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
        PlayMotion(true);
    }

    private void OnPrefsChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyTheme();
            Paint();
            PlayMotion(false);
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
        ClockText.Text = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (_machine.Snapshot().Kind is OverlayKind.Idle or OverlayKind.Collapsed)
            ClockText.IsVisible = true;
    }

    private void ApplyTheme()
    {
        var pal = PaletteCatalog.Get(PrefsStore.Current.PaletteId);
        var font = FontCatalog.Get(PrefsStore.Current.FontId);
        var family = new FontFamily(font.Family);
        var opacity = PrefsStore.Current.Opacity;
        Pill.Background = new SolidColorBrush(pal.Background, opacity);
        Pill.BorderBrush = new SolidColorBrush(pal.Border);
        Pill.BorderThickness = new Thickness(1);
        Pill.BoxShadow = new BoxShadows(new BoxShadow
        {
            Blur = 16,
            Spread = 0,
            OffsetX = 0,
            OffsetY = 0,
            Color = Color.FromArgb(90, pal.Glow.R, pal.Glow.G, pal.Glow.B)
        });
        Glow.Background = new SolidColorBrush(pal.Glow, 0.22);
        ClockText.Foreground = new SolidColorBrush(pal.Text);
        ClockText.FontFamily = family;
        OverlayTitle.FontFamily = family;
        OverlaySubtitle.FontFamily = family;
        OverlayTimer.FontFamily = family;
        OverlayTitle.Foreground = new SolidColorBrush(pal.Text);
        OverlaySubtitle.Foreground = new SolidColorBrush(pal.TextSecondary);
        OverlayProgress.Foreground = new SolidColorBrush(pal.Accent);
        KindIcon.Fill = new SolidColorBrush(pal.Accent);
        KindGlyph.Foreground = new SolidColorBrush(pal.Accent);
        MediaPlayIcon.Fill = new SolidColorBrush(pal.Accent);
        MediaPlayGlyph.Foreground = new SolidColorBrush(pal.Accent);
    }

    private void ApplySize()
    {
        var snap = _machine.Snapshot();
        var toW = snap.Width;
        var toH = snap.Height;
        Width = toW;
        Height = toH;
        var anim = PrefsStore.Current.Animation;
        if (anim == "morph" && (_lastW != toW || _lastH != toH))
            IslandAnimator.Morph(Pill, _lastW, _lastH, toW, toH, OverlayTokens.MorphMs);
        else
        {
            Pill.Width = toW;
            Pill.Height = toH;
        }
        Pill.CornerRadius = new CornerRadius(toH / 2);
        Glow.Width = toW + 10;
        Glow.Height = toH + 8;
        Glow.CornerRadius = new CornerRadius((toH + 8) / 2);
        _lastW = toW;
        _lastH = toH;
        PlaceTopCenter();
        if (snap.Kind != _lastKind)
        {
            PlayMotion(true);
            _lastKind = snap.Kind;
        }
    }

    private void PlayMotion(bool kindChanged)
    {
        var anim = PrefsStore.Current.Animation;
        if (anim == "none") return;
        if (anim == "pulse" && kindChanged) IslandAnimator.Pulse(Pill);
        if (anim == "breathe") IslandAnimator.Breathe(Glow);
        if (anim == "morph" && kindChanged) IslandAnimator.Pulse(Pill);
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
            or OverlayKind.Timer or OverlayKind.Error or OverlayKind.Expanded;
        OverlayPanel.IsVisible = overlayOn;
        ClockText.IsVisible = !overlayOn;
        OverlayTitle.Text = string.IsNullOrWhiteSpace(p.Title) ? Fallback(kind) : p.Title;
        OverlaySubtitle.Text = string.IsNullOrWhiteSpace(p.Subtitle) ? p.Body : p.Subtitle;
        OverlayProgress.Value = p.Progress * 100;
        OverlayProgress.IsVisible = kind is OverlayKind.Progress or OverlayKind.Media;
        OverlayTimer.IsVisible = kind == OverlayKind.Timer;
        OverlayTimer.Text = kind == OverlayKind.Timer ? TimeSpan.FromSeconds(Math.Ceiling(p.RemainingSeconds)).ToString(@"mm\:ss") : "";
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
        OverlayKind.Progress => "Progress",
        OverlayKind.Media => "Untitled",
        OverlayKind.Timer => "Timer",
        OverlayKind.Error => "Error",
        OverlayKind.Expanded => "Overview",
        _ => ""
    };

    private void OnPillPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        var menu = new ContextMenu();
        menu.Items.Add(Menu("Settings", IslandHost.OpenSettings));
        menu.Items.Add(Menu(_demoOn ? "Stop demo" : "Demo F9", ToggleDemo));
        menu.Items.Add(Menu("Hide overlay", IslandHost.ToggleOverlay));
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
