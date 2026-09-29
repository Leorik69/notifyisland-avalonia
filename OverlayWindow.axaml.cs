using System;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;

namespace NotifyIsland;

public partial class OverlayWindow : Window
{
    private readonly OverlayMachine _machine = new();
    private readonly AppSettings _settings;
    private WindowsWeatherSource _weather;
    private WindowsMediaSessionSource? _mediaSource;
    private bool _mediaFromSmtc;
    /// <summary>User clicked into Media while a timer might still be desired — SMTC may own island.</summary>
    private bool _userOpenedMedia;
    private WindowsPowerSource? _powerSource;
    private PowerStatusSnapshot? _lastPower;
    private int? _prevPowerPercent;
    private byte[]? _lastArtworkBytes;
    private ClipboardHistory _clipboardHistory = new();
    private WindowsClipboardSource? _clipboardSource;
    private SystemMonitorMachine? _statsMachine;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _demo = new() { Interval = TimeSpan.FromSeconds(1.8) };
    private readonly DispatcherTimer _weatherTimer = new() { Interval = TimeSpan.FromMilliseconds(OverlayTokens.WeatherRefreshMs) };
    private bool _demoOn;
    private CancellationTokenSource? _weatherCts;

    private Point _pressOrigin;
    private bool _pressing;
    private bool _weatherIconFlip;
    private string _lastWeatherIconKey = "";
    private readonly TranslateTransform _pillTranslate = new();
    private readonly Stopwatch _pressWatch = new();
    private OverlayKind _lastKind = OverlayKind.Idle;
    private SettingsWindow? _settingsWindow;
    private TrayService? _tray;
    private WinFormsTray? _winTray;
    private int _lastTrayUnread = -1;
    private Color _pillFill = Color.Parse("#080808");
    private IBrush _clockBrush = Avalonia.Media.Brushes.White;
    private double _clockDigitSize = 12;
    private double _idleFillA = 1.0;
    private readonly ScaleTransform _pillScale = new(1, 1);
    private readonly TransformGroup _pillTransforms = new();
    private readonly DispatcherTimer _pulseTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private double _pulsePhase;
    private bool _pulseActive;
    private bool _hoverWired;
    private readonly HoverPinMachine _hoverPin = new();
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromMilliseconds(OverlayTokens.FullscreenPollMs) };
    private bool _hiddenByFullscreen;
    private bool _clickThroughActive;
    private bool _pointerOverPill;
    private DateTime _lastPillClickUtc = DateTime.MinValue;
    private bool _peekSecondsActive;
    private SystemSnapshot? _lastStats;
    // 1.12.2: the split half's preview cap lives in ClipboardHalfPreview.TextMaxChars —
    // truncating text is a tested rule now, not a constant hiding in the view.

    // 1.12.3 goo blob. See docs/superpowers/specs/2026-09-29--notifyisland-goo-blob.md.
    // The ball has exactly ONE transform writer: a uniform scale for the ClickPop of the
    // detach. Its position is NOT a transform — it is the Margin, recomputed from the home
    // spot, the drag offset and the morph travel, because a layout-driven position keeps the
    // bridge's arithmetic and the hit test reading the same number. The bridge has no
    // transform at all: its outline is rebuilt from the capsule edge to the ball centre on
    // every frame, so the two shapes cannot come apart.
    private readonly ScaleTransform _blobScale = new(1, 1);
    /// <summary>Wall clock for the ball's breathe sine, started when the blob goes idle.</summary>
    private readonly Stopwatch _blobClock = new();
    /// <summary>Split state the ball is currently drawn for; the last settled value.</summary>
    private bool _splitApplied;
    /// <summary>0 = no blob transition in flight, +1 = detaching out, -1 = retracting.</summary>
    private int _blobDir;
    /// <summary>Ball offset from its home spot, already clamped to the BlobDragMaxPx disc.</summary>
    private double _blobDragAlong, _blobDragCross;
    private bool _blobDragging;
    private Point _blobPressOrigin;
    private double _blobDragStartAlong, _blobDragStartCross;
    private double _blobOpacity;
    /// <summary>Cross-axis breathe offset in DIP, written by the 200 ms tick.</summary>
    private double _blobBreathe;
    /// <summary>Extra long-axis length the capsule currently shows for phase A (DIP). Kept as
    /// a field because the window must be measured WITHOUT it: the island's screen position is
    /// computed from the capsule's own 170 DIP, so a temporarily wider capsule must not push the
    /// window around (see PlaceIsland and ApplyBlobPeek).</summary>
    private double _blobPeek;
    /// <summary>The capsule's own long-axis length, captured when a blob transition starts —
    /// the base phase A grows from. It is read, never written by the morph, so the peek cannot
    /// feed back into the length the island and the window are sized from.</summary>
    private double _blobPeekBase;

    // Explicit width/height morph (Avalonia Window Width Transitions are unreliable).
    // _morphFrom/To are the CAPSULE sizes; _winFrom/To are the WINDOW sizes. They are two
    // separate pairs on purpose: 1.12.3 leaves the capsule exactly the size the island wants
    // and grows the window around it for the ball, so an attach is a pure window change that
    // the capsule does not see. Both pairs ride the same eased progress, so the capsule and
    // the window it lives in can never be out of step mid-morph.
    private readonly DispatcherTimer _morphTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _morphWatch = new();
    private bool _morphActive;
    private double _morphFromW, _morphFromH, _morphToW, _morphToH;
    private double _winFromW, _winFromH, _winToW, _winToH;
    private int _morphDurationMs = OverlayTokens.MorphMs;
    private bool _morphInflate = true;
    private NotifyAppearStyle _morphAppear = NotifyAppearStyle.Inflate;
    private NotifyDismissStyle _morphDismiss = NotifyDismissStyle.Collapse;
    private bool _morphUsesNotifyStyle;
    private OverlayKind _prevKind = OverlayKind.Idle;
    private int _demoAppearStep;
    private static readonly NotifyAppearStyle[] DemoAppearCycle =
    [
        NotifyAppearStyle.Bounce,
        NotifyAppearStyle.SlideDown,
        NotifyAppearStyle.FadeScale,
        NotifyAppearStyle.Pop,
        NotifyAppearStyle.Inflate
    ];
    private static readonly NotifyDismissStyle[] DemoDismissCycle =
    [
        NotifyDismissStyle.Ragged,
        NotifyDismissStyle.Glitch,
        NotifyDismissStyle.SlideUp,
        NotifyDismissStyle.FadeScaleOut,
        NotifyDismissStyle.Collapse
    ];
    private readonly Random _morphRng = new();

    public AppSettings Settings => _settings;

    public OverlayWindow()
    {
        InitializeComponent();
        _pillTransforms.Children.Add(_pillScale);
        _pillTransforms.Children.Add(_pillTranslate);
        Pill.RenderTransform = _pillTransforms;
        // 1.12.3: the ball's only transform is the detach pop — see the field docs.
        Blob.RenderTransform = _blobScale;
        ApplyBlobRest(attached: false);
        _settings = AppSettings.Load();
        _settings.Normalize();
        _machine.WeatherEnabled = _settings.WeatherEnabled;
        _weather = new WindowsWeatherSource(_settings.Latitude, _settings.Longitude);
        _clipboardHistory = new ClipboardHistory(Math.Clamp(_settings.ClipboardMaxItems, 1, ClipboardHistory.HardCap));
        try
        {
            _clipboardSource = new WindowsClipboardSource(
                _clipboardHistory,
                action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
            _clipboardSource.Captured += OnClipboardCaptured;
        }
        catch (Exception ex)
        {
            AppLog.Warn("WindowsClipboardSource init failed", ex);
            _clipboardSource = null;
        }
        _pillFill = ParseColor(_settings.ColorCapsuleFill, OverlayTokens.FillHex);
        _idleFillA = _settings.Opacity;
        _statsMachine = new SystemMonitorMachine(
            new WindowsSystemMonitorSource(
                TimeSpan.FromMilliseconds(_settings.SystemStatsRefreshMs)))
        {
        };
        _statsMachine.OnSnapshot += OnStatsSnapshot;
        _statsMachine.SetIncludeAllInterfaces(_settings.SystemStatsAllInterfaces);

        EnableMorphTransitions();
        WirePointerClicks();
        WireBlobPointer();
        ConfigureHoverPinFromSettings();
        SeedIcons();
        ApplyWeatherSide();
        ApplyPalette();
        ApplyOpacity();
        ApplyIslandVisibility();

        // Prefer WinForms NotifyIcon (visible on Win11 Sandbox); Avalonia TrayIcon as fallback.
        try
        {
            _winTray ??= new WinFormsTray(this, _clipboardHistory);
            _winTray.RefreshIcon(_machine.UnreadCount);
            AppLog.Warn("Using WinFormsTray as primary tray");
        }
        catch (Exception ex)
        {
            AppLog.Warn("WinFormsTray ctor failed — falling back to Avalonia TrayService", ex);
            try
            {
                _tray ??= new TrayService(this, _clipboardHistory);
                _tray.RefreshIcon(_machine.UnreadCount);
            }
            catch (Exception ex2)
            {
                AppLog.Warn("TrayService ctor failed", ex2);
            }
        }

        Opened += (_, _) =>
        {
            // Window-handle work only. It must stay idempotent: the fullscreen-restore path
            // calls Show() again, which re-fires Opened, so this can run more than once per
            // process. (Do NOT put background-worker startup here — see the ctor note below.)
            Win32Overlay.ApplyNoActivate(this);
            Win32Overlay.ApplyZOrder(this, _settings.ZOrderMode);
            PlaceIsland();
            _ = RefreshWeatherAsync();
            EnsureMediaSource();
            EnsurePowerSource();
            SyncStatsRows();
            if (_winTray is null && _tray is null)
            {
                try
                {
                    _tray ??= new TrayService(this, _clipboardHistory);
                    _tray.RefreshIcon(_machine.UnreadCount);
                }
                catch (Exception ex)
                {
                    AppLog.Warn("TrayService Opened init failed", ex);
                }
            }
        };
        Closed += (_, _) =>
        {
            _statsMachine?.Dispose();
            _statsMachine = null;
        };
        KeyDown += OnKey;
        _clock.Tick += (_, _) => TickClock();
        _tick.Tick += (_, _) =>
        {
            var before = _machine.Snapshot().Kind;
            // 1.12.2: the split half has its own lifetime, so its expiry changes IsSplitClipboard
            // without changing Kind. Track it too or the pill would stay 370 DIP after the
            // half's 6 s timer runs out.
            var splitBefore = _machine.IsSplitClipboard;
            _machine.Tick(200);
            var after = _machine.Snapshot().Kind;
            var splitAfter = _machine.IsSplitClipboard;
            if (before != after) OnKindChanged(before, after);
            var hoverChanged = TickHoverPin(200);
            Paint();
            UpdateSecondsStrip();
            if (before != after || hoverChanged || splitBefore != splitAfter) ApplySize();
            // 1.12.3 idle breathe of the ball. Reuses this 200 ms tick (the one that already
            // expires the split) — a 2.4 s sine at 200 ms is 12 samples per period, smooth
            // enough for a ±0.5 DIP drift, and it adds no timer.
            ApplyBlobBreathe();
            var unread = _machine.UnreadCount;
            if (unread != _lastTrayUnread)
            {
                _lastTrayUnread = unread;
                _tray?.RefreshIcon(unread);
                _winTray?.RefreshIcon(unread);
            }
        };
        _demo.Tick += (_, _) =>
        {
            var before = _machine.Snapshot().Kind;
            _machine.Dispatch(OverlayCommand.DemoNext);
            var after = _machine.Snapshot().Kind;
            if (before != after) OnKindChanged(before, after);
            ApplySize();
            Paint();
        };
        _weatherTimer.Tick += (_, _) => _ = RefreshWeatherAsync();

        _fullscreenTimer.Tick += (_, _) => PollFullscreen();
        _clock.Start();
        _tick.Start();
        _weatherTimer.Start();
        _fullscreenTimer.Start();
        // Background workers start HERE, not in Opened.
        //
        // Neither the clipboard poller nor the stats sampler needs a window handle —
        // WindowsClipboardSource is GetClipboardSequenceNumber + OpenClipboard(IntPtr.Zero)
        // on a plain System.Threading.Timer, and SystemMonitorMachine is timer-driven too.
        //
        // They used to start from Opened, and that was a long-standing bug: Opened does not
        // fire for the window Avalonia shows when it is assigned to
        // IClassicDesktopStyleApplicationLifetime.MainWindow. The listener only ever came up
        // by accident, when the fullscreen-restore path called Show() and that re-fired
        // Opened — so "the clipboard works" depended on the user entering and leaving
        // fullscreen at least once per launch. Start() is idempotent, so calling it here and
        // (harmlessly) never again elsewhere is safe.
        if (_settings.ClipboardEnabled) _clipboardSource?.Start();
        if (_settings.SystemStatsEnabled) _statsMachine?.Start();
        // Same reason as above: the row set must exist before the first hover, or
        // ApplyHoverExpandedState sees zero rows and suppresses the stats surface. It only
        // appeared to work after touching the settings, because ApplySettingsFromUi happens
        // to call this too. Opened is not a reliable place for any of this.
        SyncStatsRows();
        TickClock();
        ApplySize();
        Paint();
        if (Program.DemoMode) StartDemo();
        if (Program.SettingsMode)
            Dispatcher.UIThread.Post(OpenSettings, DispatcherPriority.Background);
    }

    private void SeedIcons()
    {
        SetKindIcon(OverlayKind.Idle);
        SetWeatherIcons(WeatherCodes.IconKey(_machine.LastWeather.WeatherCode ?? 0), animate: false);
        ApplyTypography();
    }

    /// <summary>Apply FontSize + FontFamily to clock / titles / weather / badge; scale icons to FontSize.</summary>
    private void ApplyTypography()
    {
        var fs = Math.Clamp(_settings.FontSize <= 0 ? 12 : _settings.FontSize, 10, 18);
        var family = IslandFonts.Resolve(_settings.FontFamily);
        ClockText.FontSize = fs;
        ClockText.FontFamily = family;
        _clockDigitSize = fs;
        DateText.FontSize = Math.Max(10, fs - 1);
        DateText.FontFamily = family;
        WeatherTempText.FontSize = fs;
        WeatherTempText.FontFamily = family;
        OverlayTitle.FontSize = fs;
        OverlayTitle.FontFamily = family;
        BadgeText.FontSize = Math.Max(9, fs - 2);
        BadgeText.FontFamily = family;
        MediaPlayGlyph.FontSize = Math.Max(10, fs - 1);
        MediaPrevGlyph.FontSize = Math.Max(10, fs - 1);
        MediaNextGlyph.FontSize = Math.Max(10, fs - 1);
        ApplyIconSizes(fs);
    }

    /// <summary>Icon DIP = FontSize × k (collapsed ≈1.0, kind ≈0.92). Updates Viewbox hosts.</summary>
    private void ApplyIconSizes(double fontSize)
    {
        var collapsed = OverlayTokens.IconDip(fontSize, OverlayTokens.IconFontFactorCollapsed);
        var kind = OverlayTokens.IconDip(fontSize, OverlayTokens.IconFontFactorKind);
        WeatherIconA.Width = collapsed;
        WeatherIconA.Height = collapsed;
        WeatherIconB.Width = collapsed;
        WeatherIconB.Height = collapsed;
        if (WeatherIconA.Parent is Grid wxGrid)
        {
            wxGrid.Width = collapsed;
            wxGrid.Height = collapsed;
        }
        AppIconHost.Width = kind;
        AppIconHost.Height = kind;
        var badge = Math.Max(16, Math.Round(kind + 6));
        AppIcon.Width = badge;
        AppIcon.Height = badge;
    }

    private double CurrentIconCollapsed() =>
        OverlayTokens.IconDip(_settings.FontSize, OverlayTokens.IconFontFactorCollapsed);

    private double CurrentIconKind() =>
        OverlayTokens.IconDip(_settings.FontSize, OverlayTokens.IconFontFactorKind);

    private void EnableMorphTransitions()
    {
        ApplyAnimationSettings();
        if (_hoverWired) return;
        _hoverWired = true;
        Pill.PointerEntered += (_, _) =>
        {
            _pointerOverPill = true;
            Pill.BorderBrush = new SolidColorBrush(Color.Parse(
                _hoverPin.IsPinned ? "#88FFFFFF" : "#55FFFFFF"));
            Pill.Background = new SolidColorBrush(WithAlpha(_pillFill, Math.Min(1.0, _idleFillA + 0.06)));
            SyncBlobFill();
            IslandSounds.Play(IslandSoundKind.Hover, _settings);
            OnPillHoverEnter();
        };
        Pill.PointerExited += (_, _) =>
        {
            _pointerOverPill = false;
            if (!_hoverPin.IsPinned)
            {
                Pill.BorderBrush = new SolidColorBrush(Color.Parse("#28FFFFFF"));
                ApplyOpacity();
            }
            OnPillHoverLeave();
        };
    }

    /// <summary>
    /// Re-apply morph / fade / rubber durations from <see cref="AppSettings.AnimationSpeed"/>.
    /// Off → ~1 ms transitions and no pulse. Called from ctor and settings Apply.
    /// </summary>
    private void ApplyAnimationSettings()
    {
        var global = _settings.AnimationSpeed;
        var fadeSpeed = AnimationTiming.Effective(global, _settings.AnimMorphInflate);
        var rubberSpeed = AnimationTiming.Effective(global, _settings.AnimSwipeRubber);
        var hoverSpeed = AnimationTiming.Effective(global, _settings.AnimHover);
        var fade = TimeSpan.FromMilliseconds(AnimationTiming.ScaleMs(OverlayTokens.IconCrossfadeMs, fadeSpeed));
        var rubber = TimeSpan.FromMilliseconds(AnimationTiming.ScaleMs(OverlayTokens.SwipeRubberMs, rubberSpeed));
        var hover = TimeSpan.FromMilliseconds(AnimationTiming.ScaleMs(AnimationTiming.HoverMs, hoverSpeed));
        var softOut = new CubicEaseOut();
        var softInOut = new CubicEaseInOut();

        // Window Width/Height Transitions are unreliable on transparent borderless windows —
        // morph is driven by explicit timer in ApplySize/OnMorphTick.
        Transitions = null;

        // Pill: only brush hover transitions. Width/Height animated manually (see StartMorph).
        // Do NOT put Opacity/Scale transitions here — they fight pulse timers.
        Pill.Transitions = new Transitions
        {
            new BrushTransition { Property = Border.BorderBrushProperty, Duration = hover, Easing = softInOut },
            new BrushTransition { Property = Border.BackgroundProperty, Duration = hover, Easing = softInOut },
        };
        UnreadDot.Transitions = null;
        WeatherIconA.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = fade, Easing = softOut },
        };
        WeatherIconB.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = fade, Easing = softOut },
        };
        _pillTranslate.Transitions = new Transitions
        {
            new DoubleTransition { Property = TranslateTransform.XProperty, Duration = rubber, Easing = softOut },
            new DoubleTransition { Property = TranslateTransform.YProperty, Duration = rubber, Easing = softOut },
        };
        _pillScale.Transitions = null;

        if (!AnimationTiming.IsEnabled(global))
        {
            StopMorph(snapToTarget: true);
            StopUnreadPulse(resetOpacity: false);
        }
        else
        {
            var snap = _machine.Snapshot();
            var overlayOn = IsOverlayKind(snap.Kind);
            SyncUnreadPulse(!overlayOn && snap.UnreadCount > 0);
        }
    }

    private static bool IsOverlayKind(OverlayKind kind) =>
        kind is OverlayKind.Notification or OverlayKind.Progress or OverlayKind.Media
            or OverlayKind.Timer or OverlayKind.Error or OverlayKind.Expanded or OverlayKind.Weather
            or OverlayKind.Battery or OverlayKind.SystemStats;

    private void SyncUnreadPulse(bool shouldPulse)
    {
        var pulseSpeed = AnimationTiming.Effective(_settings.AnimationSpeed, _settings.AnimUnreadPulse);
        if (!shouldPulse || !_settings.AnimPulseEnabled || !AnimationTiming.IsEnabled(pulseSpeed))
        {
            StopUnreadPulse(resetOpacity: false);
            return;
        }
        if (_pulseActive) return;
        _pulseActive = true;
        _pulsePhase = 0;
        _pulseTimer.Tick -= OnPulseTick;
        _pulseTimer.Tick += OnPulseTick;
        _pulseTimer.Start();
    }

    private void StopUnreadPulse(bool resetOpacity)
    {
        if (_pulseActive)
        {
            _pulseTimer.Stop();
            _pulseTimer.Tick -= OnPulseTick;
            _pulseActive = false;
        }
        if (resetOpacity)
            UnreadDot.Opacity = 0;
    }

    private void OnPulseTick(object? sender, EventArgs e)
    {
        var pulseSpeed = AnimationTiming.Effective(_settings.AnimationSpeed, _settings.AnimUnreadPulse);
        if (!_settings.AnimPulseEnabled || !AnimationTiming.IsEnabled(pulseSpeed))
        {
            StopUnreadPulse(resetOpacity: false);
            return;
        }
        var period = AnimationTiming.ScaleMs(AnimationTiming.PulsePeriodMs, pulseSpeed);
        _pulsePhase += (Math.PI * 2.0) * (33.0 / Math.Max(1, period));
        if (_pulsePhase > Math.PI * 2.0) _pulsePhase -= Math.PI * 2.0;
        // Visible 0.40 ↔ 1.0 opacity pulse (no Opacity Transition fighting this timer)
        UnreadDot.Opacity = 0.70 + 0.30 * Math.Sin(_pulsePhase);
    }

    private static Color WithAlpha(Color c, double a) =>
        Color.FromArgb((byte)Math.Clamp((int)Math.Round(a * 255), 0, 255), c.R, c.G, c.B);

    private void ApplyOpacity()
    {
        _idleFillA = Math.Clamp(_settings.Opacity, 0.35, 1.0);
        Pill.Background = new SolidColorBrush(WithAlpha(_pillFill, _idleFillA));
        SyncBlobFill();
    }

    /// <summary>
    /// 1.12.3: the ball and its bridge are painted in the capsule's own colours, so they have
    /// to follow every brush change the capsule makes (palette, opacity slider, hover). The
    /// bridge in particular is a plain filled Path with no reference to the capsule, so without
    /// this it would stay the XAML default colour and the "one goo structure" reading would
    /// break as soon as the user changed the capsule colour.
    /// </summary>
    private void SyncBlobFill()
    {
        Blob.Background = Pill.Background;
        Blob.BorderBrush = Pill.BorderBrush;
        BlobBridge.Fill = Pill.Background;
        // The neck gets the capsule's outline too. It is the only thing separating the two
        // shapes on a dark desktop: filled with the capsule colour it was literally the same
        // value as the wallpaper behind it, so the connection was invisible and the pair read
        // as two loose circles. The outline is what makes it one goo structure.
        BlobBridge.Stroke = Pill.BorderBrush;
    }

    /// <summary>Apply user palette (capsule / accent / text) live from settings.</summary>
    private void ApplyPalette()
    {
        _pillFill = ParseColor(_settings.ColorCapsuleFill, OverlayTokens.FillHex);
        var text = ParseColor(_settings.ColorTextPrimary, OverlayTokens.TextHex);
        var textSec = ParseColor(_settings.ColorTextSecondary, OverlayTokens.TextSecondaryHex);
        var accent = ParseColor(_settings.ColorAccent, OverlayTokens.AccentHex);
        // Brighter glow variant for unread dot
        var glow = Color.FromArgb(0xE0,
            (byte)Math.Min(255, accent.R + 40),
            (byte)Math.Min(255, accent.G + 30),
            (byte)Math.Min(255, accent.B + 20));

        _clockBrush = new SolidColorBrush(text);
        ClockText.Foreground = _clockBrush;
        DateText.Foreground = new SolidColorBrush(textSec);
        WeatherTempText.Foreground = new SolidColorBrush(textSec);
        OverlayTitle.Foreground = new SolidColorBrush(text);
        BadgeText.Foreground = new SolidColorBrush(Colors.White);
        UnreadDot.Background = new SolidColorBrush(glow);
        UnreadDot.BoxShadow = new BoxShadows(new BoxShadow
        {
            Blur = 12, Spread = 4, Color = glow
        });
        UnreadBadge.Background = new SolidColorBrush(accent);
        AppIcon.Background = new SolidColorBrush(accent);
        OverlayProgress.Foreground = new SolidColorBrush(accent);
        MediaPlayGlyph.Foreground = new SolidColorBrush(accent);
        MediaPrevGlyph.Foreground = new SolidColorBrush(accent);
        MediaNextGlyph.Foreground = new SolidColorBrush(accent);
        TimerPauseGlyph.Foreground = new SolidColorBrush(accent);
        TimerPlusGlyph.Foreground = new SolidColorBrush(accent);
        // Soft “dot matrix” unread: slightly squarer corners + tighter glow
        UnreadDot.CornerRadius = new CornerRadius(2);
        UnreadDot.Width = 6;
        UnreadDot.Height = 6;
        ApplyOpacity();
        ApplyTypography();
    }

    private static Color ParseColor(string? hex, string fallback)
    {
        try { return Color.Parse(string.IsNullOrWhiteSpace(hex) ? fallback : hex); }
        catch { return Color.Parse(fallback); }
    }

    private void ApplyWeatherSide()
    {
        // Reorder: clock+date vs weather — Left = weather before clock
        var row = CollapsedRow;
        var clockText = ClockText;
        var digital = DigitalClockRow;
        var dateText = DateText;
        var weather = MinimalWeather;
        var battery = MinimalBattery;
        var dot = UnreadDot;
        row.Children.Clear();
        if (_settings.WeatherSide == WeatherSide.Left)
        {
            row.Children.Add(weather);
            row.Children.Add(clockText);
            row.Children.Add(digital);
            row.Children.Add(dateText);
            row.Children.Add(battery);
            row.Children.Add(dot);
        }
        else
        {
            row.Children.Add(clockText);
            row.Children.Add(digital);
            row.Children.Add(dateText);
            row.Children.Add(weather);
            row.Children.Add(battery);
            row.Children.Add(dot);
        }
    }

    private void ApplyOrientationLayout()
    {
        var vertical = IslandLayout.IsVertical(_settings.Orientation, _settings.Edge);
        CollapsedRow.Orientation = vertical ? Avalonia.Layout.Orientation.Vertical : Avalonia.Layout.Orientation.Horizontal;
        MinimalWeather.Orientation = vertical ? Avalonia.Layout.Orientation.Vertical : Avalonia.Layout.Orientation.Horizontal;
        ApplyBlobAnchor(vertical);
    }

    // -- 1.12.3 goo blob: anchor, hit test, drag ---------------------------------------

    /// <summary>
    /// True when the capsule's long axis is its height (Left/Right edges). The ball's home
    /// spot, its drag disc, the bridge geometry and the window growth all key off this one
    /// flag: the long axis is X on Top/Bottom and Y on Left/Right.
    /// </summary>
    private bool SplitIsVertical => IslandLayout.IsVertical(_settings.Orientation, _settings.Edge);

    /// <summary>
    /// Pin the capsule to its corner of the enlarged window: Leading on the long axis (the
    /// window only ever grows towards the ball) and Center on the cross axis (it grows evenly
    /// both ways). This is the layout half of "the island does not move" — the other half is
    /// IslandLayout.BlobWindowFor, which positions the window around the capsule. Together
    /// they keep the capsule on the same screen pixels for the whole morph, not just at rest.
    /// </summary>
    private void ApplyBlobAnchor(bool vertical)
    {
        Pill.HorizontalAlignment = vertical
            ? Avalonia.Layout.HorizontalAlignment.Center
            : Avalonia.Layout.HorizontalAlignment.Left;
        Pill.VerticalAlignment = vertical
            ? Avalonia.Layout.VerticalAlignment.Top
            : Avalonia.Layout.VerticalAlignment.Center;
    }

    /// <summary>Capsule's current long-axis extent: the height on a vertical edge, else the width.</summary>
    private double PillLongAxis()
    {
        var b = Pill.Bounds;
        return SplitIsVertical ? b.Height : b.Width;
    }

    /// <summary>
    /// Extent of the island along the long axis, which is what the ⅓/⅓/⅓ clipboard cycle
    /// zones are measured against. In 1.12.2 the capsule grew by a half and the zones had to
    /// stop at the divider; in 1.12.3 the capsule IS the island again (the clipboard moved out
    /// into the ball, which lives in the window around it), so the island's extent is simply
    /// the capsule's own length and the zones cannot move when the blob attaches.
    /// </summary>
    private double IslandHalfExtent()
    {
        // IslandLongAxis, not PillLongAxis: during phase A the capsule is temporarily longer,
        // but the ⅓/⅓/⅓ cycle zones belong to the island's OWN 170 DIP and must not slide
        // outboard just because the preview is on screen.
        var longAxis = IslandLongAxis();
        return longAxis > 0 ? longAxis : OverlayTokens.CollapsedW;
    }

    /// <summary>Wire the ball's own press/move/release. The bridge is not wired: it belongs to
    /// the ball's drag, and a hit area that follows the ball would swallow clicks meant for
    /// the desktop between the two shapes.</summary>
    private void WireBlobPointer()
    {
        Blob.PointerPressed += OnBlobPointerPressed;
        Blob.PointerMoved += OnBlobPointerMoved;
        Blob.PointerReleased += OnBlobPointerReleased;
        Blob.PointerCaptureLost += (_, _) => _blobDragging = false;
    }

    private void OnBlobPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            OpenContextMenu();
            e.Handled = true;
            return;
        }

        if (!props.IsLeftButtonPressed) return;

        _blobPressOrigin = e.GetPosition(this);
        _blobDragStartAlong = _blobDragAlong;
        _blobDragStartCross = _blobDragCross;
        _blobDragging = true;
        // Same capture as the capsule: the drag must survive the pointer leaving the ball,
        // which it always does as soon as it moves by more than the ball's own radius.
        e.Pointer.Capture(Blob);
        e.Handled = true;
    }

    private void OnBlobPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_blobDragging) return;
        var pos = e.GetPosition(this);
        // Circular clamp, not per-axis: a per-axis clamp would let a diagonal drag reach
        // BlobDragMaxPx·√2 ≈ 141 DIP and the ball would be clipped by the window edge,
        // because the window only reserves BlobDragMaxPx in every direction.
        (_blobDragAlong, _blobDragCross) = ClipboardBlob.ClampOffset(
            _blobDragStartAlong + (pos.X - _blobPressOrigin.X),
            _blobDragStartCross + (pos.Y - _blobPressOrigin.Y),
            OverlayTokens.BlobDragMaxPx);
        UpdateBlobVisual(travel: 1.0, opacity: _blobOpacity);
        e.Handled = true;
    }

    private void OnBlobPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_blobDragging) return;
        _blobDragging = false;
        e.Pointer.Capture(null);

        var pos = e.GetPosition(this);
        var dx = pos.X - _blobPressOrigin.X;
        var dy = pos.Y - _blobPressOrigin.Y;
        // The capsule's own click threshold, so "dragged" and "clicked" cannot be confused.
        var dist = Math.Sqrt(dx * dx + dy * dy);
        e.Handled = true;
        if (dist > OverlayTokens.ClickMaxPx) return;
        // Past a drag the ball simply stays where it was dropped; ApplyBlobRest resets the
        // offset when the blob detaches, so the home spot is always the resting definition.
        HandleBlobClick();
    }

    /// <summary>Clicks only (1.8.1). Swipe L/R/U/D cycle/collapse removed — CycleNext/Prev remain for API/tests/demo.</summary>
    private void WirePointerClicks()
    {
        Pill.PointerPressed += OnPillPointerPressed;
        Pill.PointerReleased += OnPillPointerReleased;
        Pill.PointerCaptureLost += (_, _) => ResetPressState();
    }

    private void OnPillPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            OpenContextMenu();
            e.Handled = true;
            return;
        }

        if (!props.IsLeftButtonPressed) return;

        _pressOrigin = e.GetPosition(this);
        // 1.12.3: no half to disambiguate any more — the capsule is the island again and the
        // ball is a separate element with its own handlers, so a press here is always the
        // island's. The two hit regions cannot overlap: the capsule is anchored to the
        // window's leading corner and the ball lives beyond its far edge.
        _pressing = true;
        _pressWatch.Restart();
        e.Pointer.Capture(Pill);
        e.Handled = true;
    }

    private void OnPillPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pressing) return;
        _pressing = false;
        e.Pointer.Capture(null);
        _pressWatch.Stop();

        var pos = e.GetPosition(this);
        var dx = pos.X - _pressOrigin.X;
        var dy = pos.Y - _pressOrigin.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);

        ResetPressState();

        // Clicks only — any drag beyond ClickMaxPx is ignored (no swipe cycle / expand / collapse).
        if (dist <= OverlayTokens.ClickMaxPx)
        {
            // Position on the capsule's long axis relative to its leading edge (0) so the
            // ⅓/⅓/⅓ clipboard-cycle zones keep their collapsed-pill positions. Since 1.12.3
            // the capsule never grows, so zoneExtent is just the capsule's own length and the
            // zones cannot slide outboard when the blob attaches.
            var zoneExtent = IslandHalfExtent();
            var zonePos = SplitIsVertical ? pos.Y : pos.X;

            var kind = _machine.Snapshot().Kind;
            try
            {
                AppLog.Info($"DBG pill click kind={kind} zonePos={zonePos:0.0} zoneExtent={zoneExtent:0.0}");
                if (kind is OverlayKind.Idle or OverlayKind.Collapsed)
                {
                    // 1.12.1: a single click in Idle is pin/unpin only. Metrics do not intercept
                    // clicks — SystemStats is entered by hover, never by a long-axis hit-test.
                    HandleIdlePillClick(zonePos, zoneExtent);
                }
                else if (kind == OverlayKind.SystemStats)
                {
                    // 1.12.1: the click no longer collapses the stats surface — exit is pointer-leave
                    // driven. The click still reaches the pin path so "hover, then click" pins
                    // instead of being swallowed by the metrics surface. Right click → context menu.
                    HandleIdlePillClick(zonePos, zoneExtent);
                }
            else if (kind == OverlayKind.Timer)
            {
                // Click keeps timer visible with controls (already expanded overlay).
                IslandSounds.Play(IslandSoundKind.Expand, _settings);
            }
            else if (kind == OverlayKind.Media)
            {
                // Explicit media focus — allow SMTC to keep ownership vs timer reclaim.
                _userOpenedMedia = true;
            }
            }
            catch (Exception ex)
            {
                AppLog.Error("pill click handler threw", ex);
            }
            e.Handled = true;
            return;
        }

        e.Handled = true;
    }

    private void ResetPressState()
    {
        _pressing = false;
    }

    /// <summary>
    /// 1.12.3: click on the ball. It must NOT fall through to the idle-pill click: the ball is
    /// a sibling of the capsule, not a child, and the press/release pair is handled and marked
    /// here, so HandleIdlePillClick is never reached and the island stays open.
    /// The history panel itself is the next stage (spec §«Панель истории»); until it exists
    /// the click is logged and acknowledged so the wiring is live and observable.
    /// </summary>
    private void HandleBlobClick()
    {
        AppLog.Info("Blob click: open clipboard history");
        IslandSounds.Play(IslandSoundKind.Hover, _settings);
    }

    private void OpenContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Menu("Центр уведомлений", TrayService.OpenActionCenter));
        menu.Items.Add(Menu("Demo F9", () => { if (_demoOn) StopDemo(); else StartDemo(); }));
        menu.Items.Add(Menu("Демо зарядки F10", DemoChargePill));
        menu.Items.Add(Menu("Демо низкий заряд F11", DemoLowBattery));
        if (_settings.TimerEnabled)
        {
            menu.Items.Add(Menu("Таймер 1 мин", () => StartCountdownMinutes(1)));
            menu.Items.Add(Menu("Таймер 5 мин", () => StartCountdownMinutes(5)));
            menu.Items.Add(Menu($"Таймер {_settings.TimerDefaultMinutes} мин (F12)", () => StartCountdownMinutes(_settings.TimerDefaultMinutes)));
            if (_machine.Snapshot().Kind == OverlayKind.Timer)
                menu.Items.Add(Menu("Отменить таймер", CancelTimer));
        }
        var weatherLabel = _settings.WeatherEnabled ? "Погода выкл" : "Погода вкл";
        menu.Items.Add(Menu(weatherLabel, ToggleWeather));
        menu.Items.Add(Menu("Настроить монитор…", () => OpenSettings("system")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Menu("Свернуть", () =>
        {
            CollapseFromUi();
            ApplySize(); Paint();
        }));
        menu.Items.Add(Menu("Настройки…", OpenSettings));
        menu.Items.Add(Menu("Выход", () =>
            (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown()));
        menu.Open(Pill);
    }

    public void OpenSettings() => OpenSettings(null);

    /// <summary>
    /// Opens the settings window, optionally navigating it to <paramref name="section"/>
    /// (a sidebar nav tag, e.g. "system" for «Монитор»). A null section keeps whatever
    /// section the window is on — including when it is already visible.
    /// </summary>
    public void OpenSettings(string? section)
    {
        try
        {
            if (_settingsWindow is { IsVisible: true })
            {
                if (section is not null) _settingsWindow.SelectSection(section);
                _settingsWindow.Activate();
                _settingsWindow.Topmost = true;
                _settingsWindow.Topmost = false;
                return;
            }

            _settingsWindow = new SettingsWindow(_settings, ApplySettingsFromUi, DemoChargePill,
                StartCountdownMinutes, StartStopwatchFromSettings, section);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        catch (Exception ex)
        {
            AppLog.Warn("OpenSettings failed", ex);
            throw;
        }
    }

    private void ApplySettingsFromUi(AppSettings draft)
    {
        draft.CopyTo(_settings);
        _settings.Normalize();
        ThemePresets.AutodetectCustom(_settings);
        _settings.Save();
        _machine.WeatherEnabled = _settings.WeatherEnabled;
        _weather = new WindowsWeatherSource(_settings.Latitude, _settings.Longitude);
        if (!_settings.WeatherEnabled && _machine.Snapshot().Kind == OverlayKind.Weather)
            _machine.Dispatch(OverlayCommand.Collapse);
        if (!_settings.TimerEnabled && _machine.Snapshot().Kind == OverlayKind.Timer)
            _machine.Dispatch(OverlayCommand.Clear);
        if (!_settings.ShowNowPlaying)
        {
            _mediaSource?.Stop();
            _mediaSource = null;
            if (_mediaFromSmtc && _machine.Snapshot().Kind == OverlayKind.Media)
            {
                _mediaFromSmtc = false;
                _machine.Dispatch(OverlayCommand.Clear);
            }
        }
        else
            EnsureMediaSource();
        if (_settings.ShowBatteryAlerts || _settings.ShowBatteryInCollapsed)
            EnsurePowerSource();
        else
        {
            _powerSource?.Stop();
            _powerSource?.Dispose();
            _powerSource = null;
        }
        _powerSource?.ResetLowLatch();
        TickClock();
        ApplyWeatherSide();
        ApplyOrientationLayout();
        ApplyPalette();
        ApplyOpacity();
        ApplyTypography();
        ApplyAnimationSettings();
        ConfigureHoverPinFromSettings();
        _hiddenByFullscreen = false;
        if (_clickThroughActive)
        {
            _clickThroughActive = false;
            Win32Overlay.ApplyClickThrough(this, false);
        }
        PollFullscreen();
        _lastWeatherIconKey = ""; // force weather icon reload for new pack
        SeedIcons();
        ApplyIslandVisibility();
        Win32Overlay.ApplyZOrder(this, _settings.ZOrderMode);
        if (_settings.SystemStatsEnabled) _statsMachine?.Start(); else _statsMachine?.Stop();
        _statsMachine?.SetInterval(TimeSpan.FromMilliseconds(_settings.SystemStatsRefreshMs));
        _statsMachine?.SetIncludeAllInterfaces(_settings.SystemStatsAllInterfaces);
        // Row set may have changed under an open surface — rebuild it and refresh the
        // values into the new rows before the size is recomputed from the row count.
        SyncStatsRows();
        // Empty surface while it is the current kind → collapse through the same route the
        // «Свернуть» menu item uses, instead of leaving an empty pill-sized island behind.
        if (StatsLayout.ShouldCollapseStatsSurface(
                _machine.Snapshot().Kind, _settings.SystemStatsEnabled, _statsRowKinds.Count))
            CollapseFromUi();
        if (_machine.Snapshot().Kind == OverlayKind.SystemStats && _lastStats is { } cur)
            ApplyStatsValues(cur);
        ApplySize();
        Paint();
        _tray?.RefreshLabels();
        _winTray?.RefreshLabels();
        _tray?.RefreshIcon(_machine.UnreadCount);
            _winTray?.RefreshIcon(_machine.UnreadCount);
        if (_settings.WeatherEnabled)
            _ = RefreshWeatherAsync();
    }

    public void ToggleIslandVisible()
    {
        _settings.IslandVisible = !_settings.IslandVisible;
        _settings.Save();
        ApplyIslandVisibility();
        _tray?.RefreshLabels();
        _winTray?.RefreshLabels();
    }

    public void ToggleWeatherFromTray() => ToggleWeather();

    public bool IsDemoRunning => _demoOn;

    public void ToggleDemoFromTray()
    {
        if (_demoOn) StopDemo();
        else StartDemo();
        _tray?.RefreshLabels();
        _winTray?.RefreshLabels();
    }

    private void ApplyIslandVisibility()
    {
        // Hide without closing — keep tray/settings alive
        Opacity = _settings.IslandVisible ? 1 : 0;
        IsHitTestVisible = _settings.IslandVisible;
        // 1.12.3: the window is much bigger than the capsule, so hit-testing is decided by the
        // root container (see the XAML comment) and has to follow island visibility too.
        Root.IsHitTestVisible = _settings.IslandVisible;
        ShowInTaskbar = false;
        if (_settings.IslandVisible)
        {
            Show();
            Win32Overlay.ApplyNoActivate(this);
            Win32Overlay.ApplyZOrder(this, _settings.ZOrderMode);
            PlaceIsland();
        }
        else
        {
            // Keep window open but invisible; alternative Hide() breaks some tray hosts
            // Use Hide for true disappearance from hit-testing/DWM
            Hide();
        }
    }

    private void ToggleWeather()
    {
        _settings.WeatherEnabled = !_settings.WeatherEnabled;
        _settings.Save();
        _machine.WeatherEnabled = _settings.WeatherEnabled;
        if (!_settings.WeatherEnabled && _machine.Snapshot().Kind == OverlayKind.Weather)
            _machine.Dispatch(OverlayCommand.Collapse);
        ApplySize();
        Paint();
        _tray?.RefreshLabels();
        _winTray?.RefreshLabels();
        if (_settings.WeatherEnabled)
            _ = RefreshWeatherAsync();
    }

    /// <summary>
    /// The single UI-side collapse route (context menu «Свернуть», an empty stats surface):
    /// dispatch <see cref="OverlayCommand.Collapse"/> and tell the window the kind changed.
    /// Callers are responsible for <see cref="ApplySize"/>/<see cref="Paint"/> afterwards.
    /// </summary>
    private void CollapseFromUi()
    {
        var before = _machine.Snapshot().Kind;
        _machine.Dispatch(OverlayCommand.Collapse);
        OnKindChanged(before, _machine.Snapshot().Kind);
    }

    private void OnKindChanged(OverlayKind before, OverlayKind after)
    {
        _prevKind = before;
        var wasCollapsed = before is OverlayKind.Idle or OverlayKind.Collapsed;
        var nowCollapsed = after is OverlayKind.Idle or OverlayKind.Collapsed;
        if (after is OverlayKind.Notification or OverlayKind.Battery)
            IslandSounds.Play(IslandSoundKind.Notify, _settings);
        else if (after == OverlayKind.Error)
            IslandSounds.Play(IslandSoundKind.Error, _settings);
        else if (wasCollapsed && !nowCollapsed)
            IslandSounds.Play(IslandSoundKind.Expand, _settings);
        else if (!wasCollapsed && nowCollapsed)
            IslandSounds.Play(IslandSoundKind.Collapse, _settings);
        _lastKind = after;
    }

    private async Task RefreshWeatherAsync()
    {
        _weatherCts?.Cancel();
        _weatherCts = new CancellationTokenSource();
        var ct = _weatherCts.Token;
        try
        {
            var payload = await _weather.GetWeatherAsync(ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;
            _machine.UpdateWeatherCache(payload);
            ApplySize();
            Paint();
        }
        catch (Exception ex)
        {
            AppLog.Warn("RefreshWeatherAsync failed", ex);
            _machine.UpdateWeatherCache(WeatherCodes.MockMoscow());
            ApplySize();
            Paint();
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F9) { if (_demoOn) StopDemo(); else StartDemo(); e.Handled = true; }
        else if (e.Key == Key.F10) { DemoChargePill(); e.Handled = true; }
        else if (e.Key == Key.F11) { DemoLowBattery(); e.Handled = true; }
        else if (e.Key == Key.F12)
        {
            if (_settings.TimerEnabled)
            {
                if (_settings.TimerStopwatchMode) StartStopwatchFromSettings();
                else StartCountdownMinutes(_settings.TimerDefaultMinutes);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_hoverPin.IsContentExpanded)
            {
                _hoverPin.EscapeOrUnpin();
                // Esc also closes the SystemStats surface opened by the hover peek.
                ApplyHoverExpandedState(false);
                e.Handled = true;
                return;
            }
            var before = _machine.Snapshot().Kind;
            _machine.Dispatch(OverlayCommand.Collapse);
            OnKindChanged(before, _machine.Snapshot().Kind);
            ApplySize(); Paint(); e.Handled = true;
        }
    }

    private void StartDemo()
    {
        _demoOn = true;
        _machine.Dispatch(OverlayCommand.Clear);
        var before = _machine.Snapshot().Kind;
        _machine.Dispatch(OverlayCommand.Notify, new OverlayPayload { Title = "Сообщение", Body = "Демо уведомление" });
        OnKindChanged(before, _machine.Snapshot().Kind);
        ApplySize(); Paint();
        _demo.Interval = TimeSpan.FromMilliseconds(OverlayTokens.DefaultNotifyMs + 1200);
        _demo.Start();
        void OnFirst(object? s, EventArgs e)
        {
            _demo.Tick -= OnFirst;
            _demo.Interval = TimeSpan.FromSeconds(1.8);
        }
        _demo.Tick += OnFirst;
    }

    private void StopDemo()
    {
        _demoOn = false; _demo.Stop();
        _machine.Dispatch(OverlayCommand.Clear); ApplySize(); Paint();
    }

    private void TickClock()
    {
        var now = DateTime.Now;
        var showSeconds = _settings.ShowClockSeconds || _hoverPin.IsContentExpanded;
        _peekSecondsActive = showSeconds && !_settings.ShowClockSeconds && _hoverPin.IsContentExpanded;
        var formatted = DigitalClockGlyphs.FormatTime(now, showSeconds);
        ClockText.Text = formatted;

        var digitalOn = _settings.DigitalClockEnabled;
        var digitalOk = false;
        if (digitalOn)
        {
            // Subtle colon blink once per second (lit on even seconds).
            var colonLit = (now.Second % 2) == 0;
            digitalOk = DigitalClockView.Apply(DigitalClockRow, formatted, _clockDigitSize, _clockBrush, colonLit);
        }
        ClockText.IsVisible = !digitalOn || !digitalOk;
        DigitalClockRow.IsVisible = digitalOn && digitalOk;

        // Clipboard-cycle preview wins over the clock when the user has at least one item.
        var snap = _machine.Snapshot();
        var cycleCount = snap.Payload.ClipboardCycleCount;
        if (cycleCount > 1)
        {
            CyclePreviewText.Text = snap.Payload.ClipboardCyclePreview;
            CyclePreviewText.IsVisible = true;
            CycleNavHint.Text = $"{snap.Payload.ClipboardCycleIndex + 1}/{cycleCount}";
            CycleNavHint.IsVisible = true;
            // Chevrons stay always visible — the dim feedback only matters in clipboard-cycle mode.
            CyclePrevButton.Opacity = snap.Payload.ClipboardCycleIndex > 0 ? 1.0 : 0.35;
            CycleNextButton.Opacity = snap.Payload.ClipboardCycleIndex < cycleCount - 1 ? 1.0 : 0.35;
            // When in clipboard-cycle mode, hijack the chevron handlers from island-slot cycle
            // by wiring them to clipboard history.
            CyclePrevButton.Click -= OnCyclePrevClick;
            CyclePrevButton.Click += OnClipboardCyclePrevClick;
            CycleNextButton.Click -= OnCycleNextClick;
            CycleNextButton.Click += OnClipboardCycleNextClick;
            // Replace the clock visual with the preview when cycle is active.
            ClockText.IsVisible = false;
            DigitalClockRow.IsVisible = false;
        }
        else
        {
            CyclePreviewText.IsVisible = false;
            CycleNavHint.IsVisible = false;
            // Restore default chevron → island-slot cycle when clipboard cycle is off.
            CyclePrevButton.Click -= OnClipboardCyclePrevClick;
            CyclePrevButton.Click += OnCyclePrevClick;
            CycleNextButton.Click -= OnClipboardCycleNextClick;
            CycleNextButton.Click += OnCycleNextClick;
            CyclePrevButton.Opacity = 1.0;
            CycleNextButton.Opacity = 1.0;
        }

        var dateFmt = _settings.DateFormat;
        if (dateFmt == DateFormat.Off)
        {
            DateText.IsVisible = false;
            DateText.Text = "";
        }
        else
        {
            DateText.Text = DateFormatHelper.Format(now, dateFmt);
            DateText.IsVisible = true;
        }
        var kind = _machine.Snapshot().Kind;
        if (kind is OverlayKind.Idle or OverlayKind.Collapsed)
            CollapsedRow.IsVisible = true;
        UpdateSecondsStrip();
    }

    private void ApplySize()
    {
        var snap = _machine.Snapshot();
        ApplyOrientationLayout();
        // 1.12.3: does this call carry a blob attach/retract? Both routes into a changed
        // IsSplitClipboard — the machine's own expiry in OverlayMachine.Tick and the capture
        // command — land here, because both callers follow the dispatch with ApplySize. The
        // capsule size morphs through the normal StartMorph path below; what is recorded here
        // is the DIRECTION, which drives the ball's own travel and scale per frame in
        // ApplyBlobMorph. The window size target is derived separately, below.
        if (snap.IsSplitClipboard != _splitApplied)
        {
            _blobDir = snap.IsSplitClipboard ? 1 : -1;
            _splitApplied = snap.IsSplitClipboard;
            _blobClock.Restart();
            // The capsule's own length is captured further down, once (w, h) are known —
            // see _blobPeekBase.
        }
        var batteryChip = _settings.ShowBatteryInCollapsed
            && _lastPower is { HasBattery: true }
            && snap.Kind is OverlayKind.Idle or OverlayKind.Collapsed;
        // 1.12.3: the capsule is the island again — SizeFor is asked for the plain island size
        // and NOT for the split long axis. The clipboard no longer takes a compartment of the
        // pill; it lives in the ball, in the window around it. This is what makes the whole
        // rest of the island (click zones, seconds strip, hit test) work off the capsule again.
        var (w, h) = IslandLayout.SizeFor(snap.Kind, snap.WeatherEnabled, _settings.Orientation,
            _settings.Edge, batteryChip, _machine.StatsMetricCount, _machine.StatsRowCount);
        if (snap.Kind is OverlayKind.Idle or OverlayKind.Collapsed)
        {
            var peek = _hoverPin.IsContentExpanded;
            var showSeconds = _settings.ShowClockSeconds || peek;
            if (showSeconds)
                w += DigitalClockGlyphs.SecondsExtraCollapsedW;
            if (peek)
                w += OverlayTokens.IdlePeekExtraW;
        }

        // 1.12.4: phase A grows the capsule from the island's OWN length, so the base is
        // captured on every ApplySize while a blob transition is in flight, not only on the
        // frame it starts. Without this a re-ApplySize mid-morph (the 200 ms tick does it
        // whenever the kind changes) would leave the peek growing off a stale base.
        if (_blobDir != 0) _blobPeekBase = SplitIsVertical ? h : w;

        // The window is a separate target: with a blob it must be big enough for the ball and
        // its whole drag disc, and it follows the NEW split state while the capsule target
        // above does not — that is what makes an attach a window-only morph.
        var (winToW, winToH) = WindowFor(snap.IsSplitClipboard, w, h);

        // If width is morphing, measure from the settled base so we don't spuriously morph.
        var fromW = Pill.Width > 0 ? Pill.Width : w;
        var fromH = Pill.Height > 0 ? Pill.Height : h;
        if (fromW <= 0) fromW = w;
        if (fromH <= 0) fromH = h;
        var winFromW = Width > 0 ? Width : winToW;
        var winFromH = Height > 0 ? Height : winToH;

        var same = Math.Abs(fromW - w) < 0.5 && Math.Abs(fromH - h) < 0.5
                   && Math.Abs(winFromW - winToW) < 0.5 && Math.Abs(winFromH - winToH) < 0.5;
        // Inflate is decided on whichever of the two grew: with a blob attach the capsule does
        // not move at all and only the window does, and that has to count as an inflate.
        var inflate = (w * h) >= (fromW * fromH) || (winToW * winToH) >= (winFromW * winFromH);
        var morphSpeed = AnimationTiming.Effective(
            _settings.AnimationSpeed,
            inflate ? _settings.AnimMorphInflate : _settings.AnimMorphCollapse);
        if (same || !AnimationTiming.IsEnabled(morphSpeed) || !IsVisible)
        {
            // No morph will run, so nothing will ever consume _blobDir. Settle the ball to its
            // defined resting state here instead of leaving it mid-animation.
            StopMorph(snapToTarget: false);
            ResetMorphVisuals();
            SettleBlob();
            SetSizeImmediate(w, h);
            return;
        }

        var enteringNotify = inflate && snap.Kind == OverlayKind.Notification;
        var leavingNotify = !inflate && _prevKind == OverlayKind.Notification;
        StartMorph(fromW, fromH, w, h, winFromW, winFromH, winToW, winToH,
            morphSpeed, inflate, enteringNotify, leavingNotify);
    }

    /// <summary>Window size for a capsule of <paramref name="w"/>×<paramref name="h"/>.</summary>
    private (double Width, double Height) WindowFor(bool withBlob, double w, double h) =>
        withBlob ? ClipboardBlob.WindowFor(SplitIsVertical, w, h) : (w, h);

    private void SetSizeImmediate(double w, double h)
    {
        // The capsule keeps its own size; only the window around it grows. PlaceIsland() then
        // re-seats the window so the capsule lands on the same screen pixels either way.
        Pill.Width = w;
        Pill.Height = h;
        Pill.CornerRadius = new CornerRadius(Math.Min(w, h) / 2);
        var (ww, wh) = WindowFor(_splitApplied || _blobDir != 0, w, h);
        Width = ww;
        Height = wh;
        if (_blobDir == 0) UpdateBlobVisual(travel: 1.0, opacity: _blobOpacity);
        PlaceIsland();
    }

    private void StartMorph(
        double fromW, double fromH, double toW, double toH,
        double winFromW, double winFromH, double winToW, double winToH,
        AnimationSpeed morphSpeed, bool inflate, bool enteringNotify, bool leavingNotify)
    {
        _morphFromW = fromW;
        _morphFromH = fromH;
        _morphToW = toW;
        _morphToH = toH;
        _winFromW = winFromW;
        _winFromH = winFromH;
        _winToW = winToW;
        _winToH = winToH;
        _morphInflate = inflate;
        _morphUsesNotifyStyle = enteringNotify || leavingNotify;
        _morphAppear = ResolveAppearStyle(enteringNotify);
        _morphDismiss = ResolveDismissStyle(leavingNotify);
        _morphDurationMs = AnimationTiming.ScaleMs(OverlayTokens.MorphMs, morphSpeed);
        // Bounce / Pop / Ragged / Glitch lean a bit longer for readability
        if (_morphUsesNotifyStyle)
        {
            if (inflate && _morphAppear is NotifyAppearStyle.Bounce or NotifyAppearStyle.Pop)
                _morphDurationMs = Math.Max(_morphDurationMs, AnimationTiming.ScaleMs(480, morphSpeed));
            if (!inflate && _morphDismiss is NotifyDismissStyle.Ragged or NotifyDismissStyle.Glitch)
                _morphDurationMs = Math.Max(_morphDurationMs, AnimationTiming.ScaleMs(460, morphSpeed));
        }

        AppLog.Info(
            $"Morph {_morphFromW:0}×{_morphFromH:0} → {_morphToW:0}×{_morphToH:0} " +
            $"({_morphDurationMs} ms, {morphSpeed}, " +
            $"{(inflate ? $"appear={_morphAppear}" : $"dismiss={_morphDismiss}")})");

        if (_morphActive)
        {
            _morphFromW = Pill.Width > 0 ? Pill.Width : fromW;
            _morphFromH = Pill.Height > 0 ? Pill.Height : fromH;
            _winFromW = Width > 0 ? Width : winFromW;
            _winFromH = Height > 0 ? Height : winFromH;
        }
        else
        {
            _morphTimer.Tick -= OnMorphTick;
            _morphTimer.Tick += OnMorphTick;
            _morphActive = true;
        }

        PrepareMorphVisualStart();
        _morphWatch.Restart();
        _morphTimer.Start();
    }

    private NotifyAppearStyle ResolveAppearStyle(bool enteringNotify)
    {
        if (!enteringNotify) return NotifyAppearStyle.Inflate;
        if (_demoOn)
            return DemoAppearCycle[_demoAppearStep++ % DemoAppearCycle.Length];
        return _settings.AppearStyle;
    }

    private NotifyDismissStyle ResolveDismissStyle(bool leavingNotify)
    {
        if (!leavingNotify) return NotifyDismissStyle.Collapse;
        if (_demoOn)
            return DemoDismissCycle[(_demoAppearStep + 2) % DemoDismissCycle.Length];
        return _settings.DismissStyle;
    }

    private void PrepareMorphVisualStart()
    {
        ResetMorphVisuals();
        if (!_morphUsesNotifyStyle) return;
        if (_morphInflate)
        {
            switch (_morphAppear)
            {
                case NotifyAppearStyle.SlideDown:
                    _pillTranslate.Y = -20;
                    Pill.Opacity = 0;
                    break;
                case NotifyAppearStyle.FadeScale:
                    Pill.Opacity = 0;
                    _pillScale.ScaleX = 0.85;
                    _pillScale.ScaleY = 0.85;
                    break;
                case NotifyAppearStyle.Bounce:
                    _pillScale.ScaleX = 0.92;
                    _pillScale.ScaleY = 0.92;
                    break;
                case NotifyAppearStyle.Pop:
                    _pillScale.ScaleX = 0.88;
                    _pillScale.ScaleY = 0.88;
                    Pill.Opacity = 0.85;
                    break;
            }
        }
        else
        {
            // dismiss starts from settled visuals
            Pill.Opacity = 1;
            _pillScale.ScaleX = 1;
            _pillScale.ScaleY = 1;
        }
    }

    private void ResetMorphVisuals()
    {
        Pill.Opacity = 1;
        _pillScale.ScaleX = 1;
        _pillScale.ScaleY = 1;
        if (!_pressing)
        {
            _pillTranslate.X = 0;
            _pillTranslate.Y = 0;
        }
    }

    private void StopMorph(bool snapToTarget)
    {
        if (_morphActive)
        {
            _morphTimer.Stop();
            _morphTimer.Tick -= OnMorphTick;
            _morphActive = false;
        }
        _morphWatch.Reset();
        if (snapToTarget && _morphToW > 0 && _morphToH > 0)
            SetSizeImmediate(_morphToW, _morphToH);
        ResetMorphVisuals();
    }

    private void OnMorphTick(object? sender, EventArgs e)
    {
        var dur = Math.Max(1, _morphDurationMs);
        var t = Math.Clamp(_morphWatch.ElapsedMilliseconds / (double)dur, 0.0, 1.0);

        double widthT;
        double auxT;
        if (_morphUsesNotifyStyle && _morphInflate)
        {
            (widthT, auxT) = AppearProgress(t, _morphAppear);
        }
        else if (_morphUsesNotifyStyle && !_morphInflate)
        {
            (widthT, auxT) = DismissProgress(t, _morphDismiss);
        }
        else
        {
            widthT = AnimationEasing.CubicOut(t);
            auxT = widthT;
        }

        var cw = Math.Max(20, _morphFromW + (_morphToW - _morphFromW) * widthT);
        var ch = Math.Max(8, _morphFromH + (_morphToH - _morphFromH) * widthT);
        // The window follows its own pair on the SAME eased progress, so a frame never shows a
        // window and a capsule that were computed from different points in the animation.
        var ww = Math.Max(20, _winFromW + (_winToW - _winFromW) * widthT);
        var wh = Math.Max(8, _winFromH + (_winToH - _winFromH) * widthT);
        Width = ww;
        Height = wh;
        Pill.Width = cw;
        Pill.Height = ch;
        Pill.CornerRadius = new CornerRadius(Math.Min(cw, ch) / 2);
        ApplyMorphAux(auxT, t);
        PlaceIsland();

        if (t >= 1.0)
        {
            StopMorph(snapToTarget: false);
            ResetMorphVisuals();
            // A blob morph that just finished must land on the ball's resting values, not on
            // the last frame's scale/opacity. Settling also clears the direction so the next
            // attach starts from the capsule's centre again.
            SettleBlob();
            SetSizeImmediate(_morphToW, _morphToH);
        }
    }

    // -- 1.12.3 goo blob animation and geometry -----------------------------------------

    /// <summary>
    /// Per-frame ball motion. <see cref="_blobDir"/> is 0 unless this morph is carrying a blob
    /// attach (+1) or retract (-1), so an unrelated morph (notify appear, hover peek) leaves
    /// the ball alone. No new timer: this rides the existing 16 ms morph tick.
    ///
    /// <paramref name="travel"/> is 0 = ball at the capsule's own centre, 1 = ball at its home
    /// spot; the caller derives it from the ClickPop curve so the ball overshoots its home and
    /// settles back, and the bridge is rebuilt from the capsule edge to wherever the ball
    /// currently is — that is what makes the pair look welded together at every frame instead
    /// of only at the ends.
    /// </summary>
    private void ApplyBlobMorph(double t)
    {
        if (_blobDir == 0) return;
        var entering = _blobDir > 0;
        // 1.12.4, two phases off ONE token (spec §Анимация). Phase A is the island running out
        // and showing the clipboard again — the 1.12.2 behaviour the user asked to get back.
        // Phase B is the capsule returning to 170 while the ball peels out of it. The user's
        // complaint was that there was "no animation at all": a ball sliding out of a capsule
        // that never changed reads as a repaint, not a detach, because nothing sets up the
        // second shape to leave from.
        var a = ClipboardBlob.PeekPhaseAt(t);
        var b = ClipboardBlob.DetachPhaseAt(t);
        ApplyBlobPeek(t);

        // The ball only exists in phase B. During phase A it sits at travel 0, which is INSIDE
        // the capsule, and the rope has no length — so it is hidden rather than drawn as a dot
        // under the island. It fades in over the first fifth of phase B, by which point the
        // capsule has already started pulling back and the ball is visibly emerging from under
        // it rather than appearing out of nothing.
        var p = b;
        // ClickPop peaks above 1; remap its 1..peak range onto 0..1 so the overshoot becomes a
        // nudge past the home spot on the travel axis instead of a change of size.
        var pop = AnimationEasing.ClickPop(p);
        var eased = (pop - 1.0) / (OverlayTokens.ClipboardHalfPopPeak - 1.0);
        var travel = entering ? eased : 1.0 - eased;
        var opacity = entering
            ? Math.Clamp(p / 0.2, 0.0, 1.0)
            : 1.0 - Math.Clamp(p, 0.0, 1.0);
        UpdateBlobVisual(travel, opacity);
        // A retracting ball must stay visible for the whole morph or the collapse reads as a
        // snap; SettleBlob hides it once the direction clears.
        Blob.IsVisible = b > 0;
        BlobBridge.IsVisible = opacity > 0.01;
        _blobScale.ScaleX = _blobScale.ScaleY = entering ? pop : 2.0 - pop;
    }

    /// <summary>
    /// Phase A: run the capsule out by <see cref="OverlayTokens.BlobPeekW"/> and fade the
    /// clipboard preview in, then bring both back over phase B.
    ///
    /// The growth goes on the capsule's LEADING end only and the preview is anchored to that
    /// same end, so the capsule's near edge — the one the eye uses to locate the island — never
    /// moves. That is what keeps "the island is static" (spec §Окно) true while it is briefly
    /// longer. The extra length is subtracted back off everywhere the island is MEASURED:
    /// <see cref="PlaceIsland"/> (else the window would creep sideways) and the click zones
    /// (else the ⅓/⅓/⅓ split would slide while the preview is up).
    /// </summary>
    private void ApplyBlobPeek(double t)
    {
        var peek = ClipboardBlob.PeekWidthAt(t);
        var vertical = SplitIsVertical;
        // The preview is opaque only while there is capsule to show it in; it fades in over
        // phase A and out over phase B so the text never sits half-outside the rounded cap.
        var a = AnimationEasing.CubicOut(ClipboardBlob.PeekPhaseAt(t));
        var b = AnimationEasing.CubicOut(ClipboardBlob.DetachPhaseAt(t));
        BlobPeek.IsVisible = peek > 0.5;
        BlobPeek.Opacity = a * (1.0 - b);
        // Clip the preview by the capsule's current length, on whichever axis is the long one.
        // The 15 DIP trailing inset is the corner radius, so the text stops before the curve
        // instead of running over it.
        if (vertical)
        {
            BlobPeek.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            BlobPeek.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            BlobPeek.Margin = new Thickness(0);
            BlobPeek.Width = double.NaN;
            BlobPeek.Height = Math.Max(0, peek - 15);
        }
        else
        {
            BlobPeek.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            BlobPeek.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            BlobPeek.Margin = new Thickness(0, 0, 15, 0);
            BlobPeek.Width = Math.Max(0, peek - 15);
            BlobPeek.Height = double.NaN;
        }

        if (Math.Abs(peek - _blobPeek) < 0.01) return;
        _blobPeek = peek;

        // Keep the island's OWN content where it is. CollapsedRow and SecondsStrip are
        // centre-aligned in the capsule, so a capsule 110 DIP longer would slide both 55 DIP
        // along the long axis — the capsule's edge would hold still while its contents crawled
        // out from under it, which is exactly the "the island moves" the spec rules out. A
        // trailing margin of the peek length takes that room back on the growing side, so the
        // content keeps centring in the ORIGINAL 170 DIP box for the whole phase.
        var hold = new Thickness(vertical ? 0 : 0, 0, vertical ? 0 : peek, vertical ? peek : 0);
        CollapsedRow.Margin = hold;
        SecondsStrip.Margin = vertical
            ? new Thickness(10, 0, 10, peek + 2)
            : new Thickness(10, 0, 10 + peek, 2);
        // Grow the capsule on its long axis FROM the morph's own base, not from the previous
        // frame: OnMorphTick has already written this frame's interpolated capsule length, and
        // for a blob attach that base is the settled 170. Accumulating onto the live value would
        // let any unrelated interpolation leak into the peek.
        // The pill is Left/Top anchored in the window, so the extra length appears on the
        // trailing side without moving the near edge.
        if (vertical) Pill.Height = _blobPeekBase + peek;
        else Pill.Width = _blobPeekBase + peek;
    }

    /// <summary>Capsule length WITHOUT the phase-A peek — what the island and its hit zones
    /// are measured against.</summary>
    private double IslandLongAxis() => PillLongAxis() - _blobPeek;

    /// <summary>
    /// The capsule's size as the island knows it: its live size minus the phase-A peek, with
    /// the window as the fallback when the pill has no size yet. One place, so the placement
    /// and the click zones cannot disagree about how long the island is.
    /// </summary>
    private (double Width, double Height) IslandCapsuleSize()
    {
        var w = (SplitIsVertical ? Pill.Width : Pill.Width - _blobPeek);
        var h = (SplitIsVertical ? Pill.Height - _blobPeek : Pill.Height);
        if (w <= 0) w = Width;
        if (h <= 0) h = Height;
        return (w, h);
    }

    /// <summary>
    /// Land the ball on its defined resting state and clear the transition. Attached → ball at
    /// its home spot, visible, bridge drawn; detached → hidden. This is the only place a blob
    /// morph is allowed to end, which is what guarantees no residual offset, scale or drag
    /// survives into the next attach.
    /// </summary>
    private void SettleBlob()
    {
        if (_blobDir == 0 && _splitApplied) return;
        ApplyBlobRest(_splitApplied);
        _blobDir = 0;
        _blobClock.Restart();
    }

    /// <summary>Write one well-defined resting frame of the ball and its bridge.</summary>
    private void ApplyBlobRest(bool attached)
    {
        _blobScale.ScaleX = 1;
        _blobScale.ScaleY = 1;
        // Phase A is over at rest, by definition: the capsule is exactly its own length and the
        // preview is gone. Left set, a later ApplySize would find a capsule 110 DIP too long.
        _blobPeek = 0;
        BlobPeek.IsVisible = false;
        BlobPeek.Opacity = 0;
        // Release the hold the phase-A margin put on the island's own rows — see ApplyBlobPeek.
        CollapsedRow.Margin = new Thickness(0);
        SecondsStrip.Margin = new Thickness(10, 0, 10, 2);
        // The home spot is the resting definition, so a drag offset never outlives the blob —
        // a re-attach always brings the ball back to exactly where the geometry says it goes.
        _blobDragAlong = 0;
        _blobDragCross = 0;
        _blobOpacity = attached ? 1.0 : 0.0;
        _blobBreathe = 0;
        Blob.IsVisible = attached;
        BlobBridge.IsVisible = attached;
        if (attached) UpdateBlobVisual(travel: 1.0, opacity: 1.0);
        else BlobBridge.Data = null;
    }

    /// <summary>
    /// Idle breathe, called from the existing 200 ms tick — the same tick that already drives
    /// the split's own expiry, so no timer is added. ±<see cref="OverlayTokens.ClipboardHalfBreathePx"/>
    /// DIP across the short axis, on the sine ClipboardSplit already owns. Suppressed while a
    /// morph runs: the ball is mid-travel then, and a 200 ms step would fight the morph.
    /// </summary>
    private void ApplyBlobBreathe()
    {
        if (!_splitApplied || _blobDir != 0 || _morphActive) return;
        _blobBreathe = ClipboardSplit.CrossBreatheOffset((int)_blobClock.ElapsedMilliseconds);
        UpdateBlobVisual(travel: 1.0, opacity: _blobOpacity);
    }

    /// <summary>
    /// Put the ball and the bridge where they belong for a given travel fraction. The single
    /// place that knows the ball's coordinates, shared by the morph, the drag and the breathe,
    /// so render and hit test cannot drift apart.
    ///
    /// The ball's centre is <see cref="ClipboardBlob.BlobHomeAlong"/> from the capsule's
    /// trailing edge plus the clamped drag offset; on the cross axis it sits on the capsule's
    /// centre line (the window grows evenly around that line, so the two agree), which is also
    /// why the drag clamp is a disc and not a box.
    /// </summary>
    private void UpdateBlobVisual(double travel, double opacity)
    {
        var vertical = SplitIsVertical;
        // The PEEK-FREE capsule length: the ball's home spot and the rope's start are defined
        // against the island's own 170 DIP. Measuring them against the temporarily grown
        // capsule would drag the ball outwards during phase A and leave the rope a frame behind
        // when the capsule snapped back.
        var capsuleLong = IslandLongAxis();
        var capsuleCross = vertical ? Pill.Width : Pill.Height;
        if (capsuleLong <= 0 || capsuleCross <= 0) return;

        _blobOpacity = opacity;
        var homeAlong = ClipboardBlob.BlobHomeAlong(capsuleLong);
        // The cross coordinate is measured from the WINDOW's edge, not the capsule's: the
        // window grew evenly around the capsule's cross centre (see IslandLayout.BlobWindowFor
        // and ApplyBlobAnchor), so the capsule's centre line sits at windowCross / 2 and not
        // at capsuleCross / 2. Measuring from the capsule instead put the ball a full
        // (windowCross - capsuleCross) / 2 too high — far enough to push it off the top of
        // the screen, which is exactly what the first live run showed.
        var windowCross = vertical ? Width : Height;
        var capsuleCentre = windowCross / 2;
        var cross = capsuleCentre + _blobDragCross + _blobBreathe;
        // travel 0 starts the ball at the capsule's own centre, so it grows out of the island
        // rather than sliding in from the side; travel 1 is the home spot plus the drag.
        var along = (capsuleLong / 2) * (1.0 - travel) + (homeAlong + _blobDragAlong) * travel;

        var x = vertical ? cross : along;
        var y = vertical ? along : cross;
        Blob.Margin = new Thickness(x - OverlayTokens.BlobD / 2, y - OverlayTokens.BlobD / 2, 0, 0);
        Blob.Opacity = opacity;
        UpdateBlobBridge(vertical, capsuleLong, capsuleCross, along, cross, opacity);
    }

    /// <summary>
    /// Rebuild the rope outline between the capsule's leading edge and the ball's centre.
    ///
    /// Continuity is structural, not animated: one end sits 2 DIP INSIDE the capsule and the
    /// other end is the ball's centre, and both are drawn under the capsule and the ball, so
    /// there is no frame on which either end can be seen. The half-widths come from
    /// <see cref="ClipboardBlob.BridgeHalfAt"/> (3→2.5 DIP: thin at both ends, since the user
    /// asked for a rope) and stay positive at both ends, which is what makes the two read as
    /// one structure rather than a gap with a bar in it. Rebuilt from code, not declared in
    /// XAML, because the endpoints are the same numbers the ball is placed from.
    /// </summary>
    private void UpdateBlobBridge(
        bool vertical, double capsuleLong, double capsuleCross, double along, double cross, double opacity)
    {
        const double inset = 2.0;
        // Both endpoints are window coordinates: the long axis starts at the capsule's own
        // leading edge (the window only grows towards the ball), the cross axis is the
        // window's centre line, where the centred capsule sits. Measuring the cross end
        // from the capsule height instead would start the neck above the ball and the two
        // would visibly come apart at the top of the screen.
        var sx = capsuleLong - inset;
        var sy = (vertical ? Width : Height) / 2;
        var dx = along - sx;
        var dy = cross - sy;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.5 || opacity <= 0.01)
        {
            // Degenerate: the ball is still inside the capsule, so there is no neck to draw.
            BlobBridge.Data = null;
            BlobBridge.IsVisible = false;
            return;
        }

        // 1.12.4: a ROPE, not a bar. The centreline sags by BridgeSagAt(t) along the CROSS
        // axis — downwards on a horizontal island, outwards on a vertical one — and the sag
        // itself comes from SagPxFor(len), so it is 0 while the ball is still inside the
        // capsule and maximal by the time the ball reaches home. A rope that snapped from
        // straight to drooping at the moment of arrival would be the "jerk" the spec forbids.
        var sagPx = ClipboardBlob.SagPxFor(len);
        // Cross axis points the same way as the window's cross axis, so adding to it is
        // "down" on a Top/Bottom island; the geometry is identical on Left/Right.
        var dySag = sagPx;

        // Sample the sagging centreline and build a strip around it: two rails, each point
        // offset by its own half-width along the normal of the LOCAL tangent. Using the local
        // tangent rather than one global perpendicular is what keeps the rope a constant
        // thickness where it curves — a global perpendicular would visibly thin it at the
        // bottom of the droop.
        const int segments = 12;

        // One sample of the rope: the sagging centreline point at t, plus the unit normal of
        // the local tangent there, offset by the rope's half-width at that t.
        Point Rail(double tt, double side)
        {
            var cx = sx + dx * tt;
            var cy = sy + dy * tt + dySag * ClipboardBlob.BridgeSagAt(tt, sagPx);
            // Local tangent by a forward difference, normalised. Using the local tangent rather
            // than one global perpendicular is what keeps the rope a constant thickness where
            // it curves; a global perpendicular visibly thins it at the bottom of the droop.
            var ahead = Math.Min(1.0, tt + 1.0 / segments);
            var ax = sx + dx * ahead;
            var ay = sy + dy * ahead + dySag * ClipboardBlob.BridgeSagAt(ahead, sagPx);
            var tx = ax - cx;
            var ty = ay - cy;
            var tl = Math.Sqrt(tx * tx + ty * ty);
            var hw = ClipboardBlob.BridgeHalfAt(tt);
            if (tl <= 0) return ToWindow(vertical, cx, cy);
            return ToWindow(vertical, cx - (ty / tl) * hw * side, cy + (tx / tl) * hw * side);
        }

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(Rail(0, 1), isFilled: true);
            for (var i = 1; i <= segments; i++) ctx.LineTo(Rail((double)i / segments, 1));
            for (var i = segments; i >= 0; i--) ctx.LineTo(Rail((double)i / segments, -1));
            ctx.EndFigure(isClosed: true);
        }
        BlobBridge.Data = geo;
        BlobBridge.Opacity = opacity;
        BlobBridge.IsVisible = true;
    }

    /// <summary>Along/cross coordinates to window X/Y, swapping on a vertical island.</summary>
    private static Point ToWindow(bool vertical, double along, double cross) =>
        vertical ? new Point(cross, along) : new Point(along, cross);

    private static (double widthT, double auxT) AppearProgress(double t, NotifyAppearStyle style) => style switch
    {
        NotifyAppearStyle.Bounce => (AnimationEasing.SpringOut(t), AnimationEasing.SpringOut(t)),
        NotifyAppearStyle.Pop => (AnimationEasing.CubicOut(t), AnimationEasing.PopScale(t)),
        NotifyAppearStyle.SlideDown => (AnimationEasing.CubicOut(t), AnimationEasing.CubicOut(t)),
        NotifyAppearStyle.FadeScale => (AnimationEasing.CubicOut(t), AnimationEasing.CubicOut(t)),
        _ => (AnimationEasing.CubicOut(t), AnimationEasing.CubicOut(t))
    };

    private static (double widthT, double auxT) DismissProgress(double t, NotifyDismissStyle style) => style switch
    {
        NotifyDismissStyle.Glitch => (AnimationEasing.GlitchStep(t), AnimationEasing.GlitchStep(t)),
        NotifyDismissStyle.Ragged => (AnimationEasing.CubicOut(t), t),
        NotifyDismissStyle.SlideUp => (AnimationEasing.CubicOut(t), AnimationEasing.CubicOut(t)),
        NotifyDismissStyle.FadeScaleOut => (AnimationEasing.CubicOut(t), AnimationEasing.CubicOut(t)),
        _ => (AnimationEasing.CubicOut(t), AnimationEasing.CubicOut(t))
    };

    /// <summary>
    /// 1.12.3: the ball's own motion, driven from the same morph <c>t</c> as the capsule and
    /// the window so the island growing and the ball leaving it are one movement. Runs before
    /// the notify-style branch because a blob morph is not a notify morph — the ball must
    /// animate for both, and the early return below would otherwise skip it.
    /// </summary>
    private void ApplyMorphAux(double auxT, double rawT)
    {
        ApplyBlobMorph(rawT);
        if (!_morphUsesNotifyStyle)
        {
            Pill.Opacity = 1;
            return;
        }

        if (_morphInflate)
        {
            switch (_morphAppear)
            {
                case NotifyAppearStyle.SlideDown:
                    _pillTranslate.Y = -20 * (1.0 - auxT);
                    Pill.Opacity = auxT;
                    break;
                case NotifyAppearStyle.FadeScale:
                    Pill.Opacity = auxT;
                    var s = 0.85 + 0.15 * auxT;
                    _pillScale.ScaleX = s;
                    _pillScale.ScaleY = s;
                    break;
                case NotifyAppearStyle.Bounce:
                    // Spring width already overshoots; gentle scale breathe with spring
                    var springScale = 0.92 + 0.08 * AnimationEasing.SpringOut(rawT);
                    _pillScale.ScaleX = springScale;
                    _pillScale.ScaleY = springScale;
                    Pill.Opacity = Math.Min(1.0, 0.7 + 0.3 * auxT);
                    break;
                case NotifyAppearStyle.Pop:
                    _pillScale.ScaleX = auxT;
                    _pillScale.ScaleY = auxT;
                    Pill.Opacity = Math.Min(1.0, 0.85 + 0.15 * AnimationEasing.CubicOut(rawT));
                    break;
                default:
                    Pill.Opacity = 1;
                    break;
            }
        }
        else
        {
            switch (_morphDismiss)
            {
                case NotifyDismissStyle.SlideUp:
                    _pillTranslate.Y = -18 * auxT;
                    Pill.Opacity = 1.0 - auxT;
                    break;
                case NotifyDismissStyle.FadeScaleOut:
                    Pill.Opacity = 1.0 - auxT;
                    var so = 1.0 - 0.15 * auxT;
                    _pillScale.ScaleX = so;
                    _pillScale.ScaleY = so;
                    break;
                case NotifyDismissStyle.Ragged:
                    {
                        var amp = 3.5 * (1.0 - rawT);
                        _pillTranslate.X = (_morphRng.NextDouble() * 2 - 1) * amp;
                        _pillTranslate.Y = (_morphRng.NextDouble() * 2 - 1) * amp * 0.35;
                        Pill.Opacity = 1.0 - AnimationEasing.CubicOut(rawT) * 0.85;
                        break;
                    }
                case NotifyDismissStyle.Glitch:
                    {
                        var stutter = (int)(rawT * 12) % 2 == 0;
                        _pillTranslate.X = stutter ? 2.5 : -1.5;
                        _pillTranslate.Y = stutter ? -1.0 : 0.5;
                        Pill.Opacity = stutter ? Math.Max(0.15, 1.0 - auxT) : Math.Max(0.05, 0.7 - auxT);
                        break;
                    }
                default:
                    Pill.Opacity = 1;
                    break;
            }
        }
    }

    private void PlaceIsland()
    {
        var screen = Screens.Primary ?? Screens.ScreenFromWindow(this);
        if (screen is null) return;
        var wa = screen.WorkingArea;
        var scale = RenderScaling <= 0 ? 1 : RenderScaling;
        // 1.12.3: with a blob the WINDOW is much larger than the capsule, but everything the
        // island is — the edge anchor, the user's offsets, the click zones, the hit region — is
        // defined against the CAPSULE. So Place() is still given the capsule's pixel size, and
        // the window is then re-seated around that spot by BlobWindowFor. Doing it the other
        // way round (placing the window and letting the capsule ride along) is what would make
        // the island jump sideways every time the ball attached. This runs on every morph
        // frame, so the capsule holds its screen position during the morph too, not just at
        // rest.
        // The PEEK-FREE capsule size, as a (w, h) pair: phase A makes the capsule longer, and
        // feeding that longer length to IslandLayout.Place would move the window — and with it
        // the island — 55 DIP for the duration of the preview. The spec's "the island does not
        // move" has to be measured against the settled length, not the temporary one.
        var (homeW, homeH) = IslandCapsuleSize();
        var homePw = (int)Math.Round(homeW * scale);
        var homePh = (int)Math.Round(homeH * scale);
        var (x, y) = IslandLayout.Place(
            wa.X, wa.Y, wa.Width, wa.Height, homePw, homePh,
            _settings.Edge, _settings.OffsetX, _settings.OffsetY);
        if (_splitApplied || _blobDir != 0)
        {
            var winPw = (int)Math.Round(Width * scale);
            var winPh = (int)Math.Round(Height * scale);
            (x, y) = IslandLayout.BlobWindowFor(SplitIsVertical, x, y, homePw, homePh, winPw, winPh);
        }
        Position = new PixelPoint(x, y);
        Win32Overlay.ApplyZOrder(this, _settings.ZOrderMode);
    }

    private void Paint()
    {
        var snap = _machine.Snapshot();
        var kind = snap.Kind;
        var p = snap.Payload;
        var overlayOn = IsOverlayKind(kind);

        // SystemStats renders in its own dynamic-height slot (SystemStatsPanel), not the
        // single-row overlay.
        OverlayPanel.IsVisible = overlayOn && kind != OverlayKind.SystemStats;
        CollapsedRow.IsVisible = !overlayOn;

        // 1.12.3 goo blob. The ball's visibility is owned by the blob transition (SettleBlob /
        // ApplyBlobMorph), not by this paint: the window is already growing while the ball is
        // still held back, so flipping it here would pop it in one frame early. All Paint does
        // is refill the ball's content on the same UI turn as the capture that produced it.
        // The island itself needs no compensation any more: the capsule never grew, so
        // CollapsedRow and SecondsStrip just centre themselves in it.
        if (snap.IsSplitClipboard) ApplyBlobContent(snap.SplitClipboard);
        // 1.12.4: only claim these margins at rest. During phase A the morph owns them — it
        // holds the island's rows in the original 170 DIP box while the capsule is longer, and a
        // Paint landing mid-morph (Paint runs on every kind change) would otherwise yank the
        // rows 55 DIP outwards for a frame. At rest _blobPeek is 0, so this is the old margin.
        if (_blobPeek <= 0)
        {
            CollapsedRow.Margin = new Thickness(0);
            SecondsStrip.Margin = new Thickness(10, 0, 10, 2);
        }
        CollapsedRow.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        SecondsStrip.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;

        // 1.12.1: panel visibility follows the FSM kind immediately (Paint runs on the same
        // UI turn as the command that changed the kind). It must NOT wait for the next
        // SystemStatsRefreshMs sampling tick, otherwise the panel appears 0-2 s late on hover.
        // Preset Off resolves to no rows: show nothing at all rather than an empty pill.
        var statsVisible =
            kind == OverlayKind.SystemStats && _settings.SystemStatsEnabled && !StatsSurfaceEmpty;
        SystemStatsPanel.IsVisible = statsVisible;
        if (statsVisible && _lastStats is { } known)
            ApplyStatsValues(known);

        var showMinimalWx = !overlayOn && snap.WeatherEnabled;
        MinimalWeather.IsVisible = showMinimalWx;
        if (showMinimalWx)
        {
            var wx = snap.LastWeather;
            WeatherTempText.Text = WeatherCodes.FormatMinimalTemp(wx.TemperatureC ?? 18);
            SetWeatherIcons(WeatherCodes.IconKey(wx.WeatherCode ?? 0), animate: true);
            if (_settings.WeatherLocationMode == WeatherLocationMode.Manual
                && !string.IsNullOrWhiteSpace(_settings.WeatherLocationName))
            {
                ToolTip.SetTip(MinimalWeather,
                    $"{_settings.WeatherLocationName.Trim()} · температура — из Windows; название локации — выбранное");
            }
            else
                ToolTip.SetTip(MinimalWeather, "Погода Windows");
        }

        var showBat = !overlayOn && _settings.ShowBatteryInCollapsed
            && _lastPower is { HasBattery: true };
        MinimalBattery.IsVisible = showBat;
        if (showBat)
        {
            var pct = _lastPower!.Percent;
            BatteryPercentText.Text = $"{pct}%";
            var batKey = _lastPower.IsCharging || _lastPower.OnAc ? "bolt" : "battery";
            var batBrush = new SolidColorBrush(ParseColor(_settings.ColorTextSecondary, OverlayTokens.TextSecondaryHex));
            BatteryIconHost.Child = IconPackService.Create(_settings.IconPack, batKey, CurrentIconCollapsed(), batBrush);
            ToolTip.SetTip(MinimalBattery,
                _lastPower.IsCharging || _lastPower.OnAc ? $"Зарядка · {pct}%" : $"Батарея · {pct}%");
        }

        var title = string.IsNullOrWhiteSpace(p.Title) ? Fallback(kind) : p.Title;
        var sub = string.IsNullOrWhiteSpace(p.Subtitle) ? p.Body : p.Subtitle;
        if (kind == OverlayKind.Weather)
        {
            title = string.IsNullOrWhiteSpace(p.Body)
                ? WeatherCodes.FormatExpanded(p.TemperatureC ?? snap.LastWeather.TemperatureC ?? 18,
                    p.WeatherCode ?? snap.LastWeather.WeatherCode ?? 0, p.PrecipProb ?? snap.LastWeather.PrecipProb)
                : p.Body;
            if (_settings.WeatherLocationMode == WeatherLocationMode.Manual
                && !string.IsNullOrWhiteSpace(_settings.WeatherLocationName))
            {
                sub = _settings.WeatherLocationName.Trim();
            }
            else
                sub = "";
        }
        else if (kind == OverlayKind.Timer)
        {
            sub = IslandTimerLogic.FormatRemaining(p.RemainingSeconds);
            if (!p.Playing && !p.CountUp)
                title = string.IsNullOrWhiteSpace(p.Title) ? "Пауза" : p.Title;
        }
        else if (kind == OverlayKind.Progress && string.IsNullOrWhiteSpace(sub))
            sub = $"{(int)Math.Round(p.Progress * 100)}%";
        else if (kind == OverlayKind.Battery && string.IsNullOrWhiteSpace(sub))
            sub = $"{(int)Math.Round(p.Progress * 100)}%";

        OverlayTitle.Text = string.IsNullOrWhiteSpace(sub) ? title : $"{title} · {sub}";
        OverlaySubtitle.Text = "";

        OverlayProgress.Value = p.Progress * 100;
        OverlayProgress.IsVisible = kind is OverlayKind.Progress or OverlayKind.Media or OverlayKind.Battery;

        var textPrimary = ParseColor(_settings.ColorTextPrimary, OverlayTokens.TextHex);
        var accent = ParseColor(_settings.ColorAccent, OverlayTokens.AccentHex);
        OverlayTitle.Foreground = new SolidColorBrush(kind == OverlayKind.Error ? Color.Parse(OverlayTokens.ErrorHex) : textPrimary);
        AppIcon.Background = new SolidColorBrush(kind == OverlayKind.Error ? Color.Parse(OverlayTokens.ErrorHex) : accent);
        UnreadBadge.Background = new SolidColorBrush(accent);

        if (kind == OverlayKind.Weather)
            SetKindIconWeather(p.WeatherCode ?? snap.LastWeather.WeatherCode ?? 0);
        else
            SetKindIcon(kind);

        MediaControls.IsVisible = kind == OverlayKind.Media;
        MediaPlayGlyph.Text = p.Playing ? "||" : "▶";
        ApplyMediaArtwork(kind == OverlayKind.Media ? p.ArtworkBytes : null);

        TimerControls.IsVisible = kind == OverlayKind.Timer;
        if (kind == OverlayKind.Timer)
        {
            TimerPauseGlyph.Text = p.Playing ? "||" : "▶";
            TimerPlusBtn.IsVisible = !p.CountUp;
        }

        var unread = snap.UnreadCount;
        var showBadge = overlayOn && unread > 0 && kind is OverlayKind.Notification or OverlayKind.Expanded;
        UnreadBadge.IsVisible = showBadge;
        BadgeText.Text = unread > 99 ? "99+" : unread.ToString(CultureInfo.InvariantCulture);

        var showDot = !overlayOn && unread > 0;
        if (!showDot)
        {
            StopUnreadPulse(resetOpacity: true);
        }
        else if (!_pulseActive)
        {
            UnreadDot.Opacity = 1.0;
            SyncUnreadPulse(true);
        }
        else
        {
            SyncUnreadPulse(true);
        }

        var tip = kind switch
        {
            OverlayKind.Idle or OverlayKind.Collapsed when _hoverPin.IsPinned =>
                "Закреплено · клик — открепить · Esc · двойной клик — Центр уведомлений",
            OverlayKind.Idle or OverlayKind.Collapsed =>
                "Клик — закрепить · двойной клик — Центр уведомлений",
            _ => OverlayTitle.Text
        };
        ToolTip.SetTip(this, tip);

        // PinnedBadge only makes sense on Idle/Collapsed — overlays already have their own affordances.
        PinnedBadge.IsVisible = kind is OverlayKind.Idle or OverlayKind.Collapsed && _hoverPin.IsPinned;
    }

    /// <summary>
    /// 1.12.3: fill the ball from the payload BuildPayload already produced — format icon per
    /// <see cref="ClipboardItemKind"/> plus the preview text. Both the icon key and the
    /// wording/truncation/empty-fallback rules live in
    /// <see cref="ClipboardHalfPreview"/> and are unit-tested there, so this stays a pure
    /// "put it on screen" step. The plural («5 файлов») is BuildPayload's, not ours.
    /// </summary>
    private void ApplyBlobContent(OverlayPayload cp)
    {
        var brush = new SolidColorBrush(Colors.White);
        BlobIcon.Child = IconPackService.Create(
            _settings.IconPack, ClipboardHalfPreview.IconKeyFor(cp.ClipboardItemKind),
            CurrentIconCollapsed(), brush);

        BlobText.Text = ClipboardHalfPreview.TextFor(cp);
        // 1.12.4 phase A shows the SAME preview inside the capsule, so the text/icon rules stay
        // in one tested place. Refilled here rather than per frame: the morph only changes how
        // wide and how opaque the row is, never what it says. The icon is built TWICE on
        // purpose — a Viewbox has a single Child and handing the ball's to the preview would
        // reparent it, so the ball would lose its icon mid-peek.
        BlobPeekIcon.Child = IconPackService.Create(
            _settings.IconPack, ClipboardHalfPreview.IconKeyFor(cp.ClipboardItemKind),
            CurrentIconCollapsed(), brush);
        BlobPeekText.Text = ClipboardHalfPreview.TextFor(cp);
        ToolTip.SetTip(Blob, cp.ClipboardItemKind switch
        {
            ClipboardItemKind.Text => "Скопирован текст",
            ClipboardItemKind.File => "Скопирован файл",
            ClipboardItemKind.MultiFile => "Скопированы файлы",
            _ => "Буфер обмена — нажмите для истории",
        });
    }

    private void SetKindIcon(OverlayKind kind)
    {
        var key = IslandIcons.KindKey(kind);
        var brush = new SolidColorBrush(Colors.White);
        AppIconHost.Child = IconPackService.Create(_settings.IconPack, key, CurrentIconKind(), brush, 1.5);
    }

    private void SetKindIconWeather(int code)
    {
        var key = WeatherCodes.IconKey(code);
        var brush = new SolidColorBrush(Colors.White);
        AppIconHost.Child = IconPackService.Create(_settings.IconPack, key, CurrentIconKind(), brush, 1.5);
    }

    private void SetWeatherIcons(string key, bool animate)
    {
        if (string.Equals(key, _lastWeatherIconKey, StringComparison.OrdinalIgnoreCase) && WeatherIconA.Child is not null)
            return;

        var brush = new SolidColorBrush(ParseColor(_settings.ColorTextSecondary, OverlayTokens.TextSecondaryHex));
        var path = IconPackService.Create(_settings.IconPack, key, CurrentIconCollapsed(), brush);

        if (!animate || WeatherIconA.Child is null)
        {
            WeatherIconA.Child = path;
            WeatherIconA.Opacity = 1;
            WeatherIconB.Child = null;
            WeatherIconB.Opacity = 0;
            _weatherIconFlip = false;
            _lastWeatherIconKey = key;
            return;
        }

        if (!_weatherIconFlip)
        {
            WeatherIconB.Child = path;
            WeatherIconB.Opacity = 1;
            WeatherIconA.Opacity = 0;
            _weatherIconFlip = true;
        }
        else
        {
            WeatherIconA.Child = path;
            WeatherIconA.Opacity = 1;
            WeatherIconB.Opacity = 0;
            _weatherIconFlip = false;
        }
        _lastWeatherIconKey = key;
    }

    private static string Fallback(OverlayKind kind) => kind switch
    {
        OverlayKind.Notification => "Уведомление",
        OverlayKind.Progress => "Прогресс",
        OverlayKind.Media => "Без названия",
        OverlayKind.Timer => "Таймер",
        OverlayKind.Error => "Ошибка",
        OverlayKind.Expanded => "Обзор",
        OverlayKind.Weather => "Погода",
        OverlayKind.Battery => "Зарядка",
        OverlayKind.SystemStats => "Монитор",
        _ => ""
    };

    private static MenuItem Menu(string header, Action act)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => act();
        return item;
    }

    private void OnMediaPlay(object? sender, RoutedEventArgs e)
    {
        if (_mediaFromSmtc && _mediaSource is not null)
        {
            _ = _mediaSource.TryTogglePlayPauseAsync();
            return;
        }
        var next = OverlayMachine.Sanitize(_machine.Snapshot().Payload);
        next.Playing = !next.Playing;
        _machine.Dispatch(OverlayCommand.SetMedia, next);
        Paint();
    }

    private void OnMediaPrev(object? sender, RoutedEventArgs e)
    {
        PlayClickPop();
        if (_mediaSource is not null && _settings.ShowNowPlaying)
            _ = _mediaSource.TrySkipPreviousAsync();
    }

    private void OnMediaNext(object? sender, RoutedEventArgs e)
    {
        PlayClickPop();
        if (_mediaSource is not null && _settings.ShowNowPlaying)
            _ = _mediaSource.TrySkipNextAsync();
    }

    /// <summary>
    /// Click-acknowledgement pop: scale 1 → ClickPopPeak (peak holds up to ~33% of total) → 1.
    /// Uses <see cref="Avalonia.Animation.Animation"/> with two <see cref="KeyFrame"/>s because
    /// Avalonia 11 has no WPF-style <c>DoubleAnimation</c>.
    /// </summary>
    private void PlayClickPop()
    {
        var speed = AnimationTiming.Effective(_settings.AnimationSpeed, _settings.AnimClickPop);
        if (!AnimationTiming.IsEnabled(speed)) return;
        var halfMs = AnimationTiming.ScaleMs(OverlayTokens.ClickPopMs, speed);
        var anim = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromMilliseconds(halfMs * 2),
            Easing = new CubicEaseOut(),
            FillMode = Avalonia.Animation.FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0.5),
                    Setters =
                    {
                        new Setter(ScaleTransform.ScaleXProperty, OverlayTokens.ClickPopPeak),
                        new Setter(ScaleTransform.ScaleYProperty, OverlayTokens.ClickPopPeak),
                    }
                },
            }
        };
        _ = anim.RunAsync(_pillScale);
    }

    private void EnsureMediaSource()
    {
        if (!_settings.ShowNowPlaying) return;
        if (_mediaSource is not null) return;
        try
        {
            _mediaSource = new WindowsMediaSessionSource();
            _mediaSource.Changed += OnSmtcChanged;
            _ = _mediaSource.StartAsync();
        }
        catch (Exception ex)
        {
            AppLog.Warn("EnsureMediaSource failed", ex);
            _mediaSource = null;
        }
    }

    private void OnSmtcChanged(MediaSessionSnapshot? snap)
    {
        // Marshal to UI thread — SMTC events/poll may arrive off-thread.
        Dispatcher.UIThread.Post(() => ApplySmtcSnapshot(snap), DispatcherPriority.Background);
    }

    private void ApplySmtcSnapshot(MediaSessionSnapshot? snap)
    {
        if (!_settings.ShowNowPlaying)
            return;

        if (snap is null)
        {
            if (_mediaFromSmtc && _machine.Snapshot().Kind == OverlayKind.Media)
            {
                _mediaFromSmtc = false;
                var before = _machine.Snapshot().Kind;
                _machine.Dispatch(OverlayCommand.Clear);
                var after = _machine.Snapshot().Kind;
                if (before != after) OnKindChanged(before, after);
                ApplySize();
                Paint();
            }
            return;
        }

        // Do not interrupt an active notification toast.
        var kind = _machine.Snapshot().Kind;
        if (kind == OverlayKind.Notification)
            return;

        // Running/paused timer owns the island until cancel/complete (unless user opened Media).
        if (IslandTimerLogic.TimerOwnsIsland(kind, _userOpenedMedia))
            return;

        var before2 = kind;
        _mediaFromSmtc = true;
        _machine.Dispatch(OverlayCommand.SetMedia, snap.ToPayload());
        var after2 = _machine.Snapshot().Kind;
        if (before2 != after2) OnKindChanged(before2, after2);
        ApplySize();
        Paint();
    }

    private void ApplyMediaArtwork(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            MediaArtwork.IsVisible = false;
            MediaArtwork.Source = null;
            _lastArtworkBytes = null;
            AppIconHost.IsVisible = true;
            return;
        }
        if (_lastArtworkBytes is not null
            && _lastArtworkBytes.Length == bytes.Length
            && bytes.AsSpan().SequenceEqual(_lastArtworkBytes))
        {
            MediaArtwork.IsVisible = true;
            AppIconHost.IsVisible = false;
            return;
        }
        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new Avalonia.Media.Imaging.Bitmap(ms);
            MediaArtwork.Source = bmp;
            MediaArtwork.IsVisible = true;
            AppIconHost.IsVisible = false;
            _lastArtworkBytes = (byte[])bytes.Clone();
        }
        catch (Exception ex)
        {
            AppLog.Warn("ApplyMediaArtwork failed", ex);
            MediaArtwork.IsVisible = false;
            MediaArtwork.Source = null;
            AppIconHost.IsVisible = true;
            _lastArtworkBytes = null;
        }
    }

    private void EnsurePowerSource()
    {
        if (!_settings.ShowBatteryAlerts && !_settings.ShowBatteryInCollapsed) return;
        if (_powerSource is not null) return;
        try
        {
            _powerSource = new WindowsPowerSource();
            _powerSource.Changed += OnPowerChanged;
            _powerSource.Start();
        }
        catch (Exception ex)
        {
            AppLog.Warn("EnsurePowerSource failed", ex);
            _powerSource = null;
        }
    }

    private void OnPowerChanged(PowerStatusSnapshot snap) =>
        Dispatcher.UIThread.Post(() => ApplyPowerSnapshot(snap), DispatcherPriority.Background);

    private void OnClipboardCaptured(ClipboardEntry entry)
    {
        // Ignore when user disabled clipboard listener at runtime.
        if (!_settings.ClipboardEnabled) return;
        var payload = ClipboardHistory.BuildPayload(entry, DateTimeOffset.UtcNow);
        // 1.12.2: the clipboard rides in a split half next to the current kind instead of
        // taking the whole pill over, so the clock/pin surface survives the copy.
        var snap = _machine.Dispatch(OverlayCommand.SetClipboardSplit, payload);
        ApplySize();
        Paint();
        if (!snap.IsSplitClipboard) return;
        // Refresh the Idle-pill cycle previews so prev/next zones show the latest history.
        RefreshIdleClipboardCycle();
        // Beep-on-copy is opt-in via Notify volume slider; v1 stays silent for MultiFile.
        if (_settings.SoundEnabled
            && _settings.SoundVolNotify > 0
            && entry.Kind is ClipboardItemKind.Text or ClipboardItemKind.File)
        {
            IslandSounds.Play(IslandSoundKind.Notify, _settings);
        }
    }

    /// <summary>Push the current ring-buffer previews into FSM so the Idle pill can cycle.</summary>
    private void RefreshIdleClipboardCycle()
    {
        var items = _clipboardHistory.SnapshotNewestFirst();
        if (items.Count < 2)
        {
            // Single item (or empty): no cycling needed, leave the Idle pill in default clock state.
            AppLog.Info($"RefreshIdleClipboardCycle: only {items.Count} item(s) — cycle stays inactive");
            return;
        }
        var previews = items.Select(MakeCyclePreview).ToList();
        var snap = _machine.Dispatch(OverlayCommand.SetClipboardCycle, new OverlayPayload
        {
            ClipboardCyclePreviews = previews,
            ClipboardCycleIndex = 0
        });
        AppLog.Info($"RefreshIdleClipboardCycle: cycle ON, {snap.Payload.ClipboardCycleCount} items, first preview = \"{snap.Payload.ClipboardCyclePreview}\"");
    }

    private static string MakeCyclePreview(ClipboardEntry e) => e.Kind switch
    {
        ClipboardItemKind.Text => (e.Text ?? "").Replace("\r\n", " ").Replace('\n', ' ').Trim(),
        ClipboardItemKind.File => System.IO.Path.GetFileName(e.Paths is { Count: > 0 } ? e.Paths[0] : ""),
        ClipboardItemKind.MultiFile => $"{e.Paths?.Count ?? 0} файлов",
        _ => ""
    };

    /// <summary>Value colour: normal / attention / critical, high-is-bad metric (CPU, RAM %).</summary>
    private static IBrush StatsBrushHigh(double value, double warn, double crit) =>
        new SolidColorBrush(Color.Parse(
            value >= crit ? OverlayTokens.ErrorHex
            : value >= warn ? OverlayTokens.AccentHex
            : OverlayTokens.TextSecondaryHex));

    /// <summary>Value colour: normal / attention / critical, low-is-bad metric (battery %).</summary>
    private static IBrush StatsBrushLow(double value, double warn, double crit) =>
        new SolidColorBrush(Color.Parse(
            value <= crit ? OverlayTokens.ErrorHex
            : value <= warn ? OverlayTokens.AccentHex
            : OverlayTokens.TextSecondaryHex));

    /// <summary>Normal (never-accented) value colour — used for placeholders such as a missing battery.</summary>
    private static IBrush StatsBrushNormal() => new SolidColorBrush(Color.Parse(OverlayTokens.TextSecondaryHex));

    /// <summary>«↓ 21,3 МБ/с» / «↑ 812 КБ/с» — binary 1024 unit switch, current culture decimal separator.</summary>
    private static string FormatNetRate(long bytesPerSec, string arrow)
    {
        const double kb = 1024.0;
        const double mb = kb * 1024.0;
        var b = bytesPerSec < 0 ? 0 : bytesPerSec;
        if (b == 0)
            return $"{arrow} {0.0.ToString("F1", CultureInfo.CurrentCulture)} МБ/с";
        return b >= mb
            ? $"{arrow} {(b / mb).ToString("F1", CultureInfo.CurrentCulture)} МБ/с"
            : $"{arrow} {(b / kb).ToString("F1", CultureInfo.CurrentCulture)} КБ/с";
    }

    private void OnStatsSnapshot(SystemSnapshot s)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Cache always: a later hover opens the panel with the last known values
            // instead of waiting for the next sampling tick.
            _lastStats = s;

            var kind = _machine.Snapshot().Kind;
            var overlayOn = IsOverlayKind(kind) && kind != OverlayKind.SystemStats;
            if (!_settings.SystemStatsEnabled || overlayOn)
                return;
            // Visibility is owned by Paint() (kind-driven). Here we only refresh values.
            if (kind != OverlayKind.SystemStats) return;

            var screen = Screens.All.FirstOrDefault(sc => sc.WorkingArea.Contains(new PixelPoint(
                (int)Position.X, (int)Position.Y)));
            var available = (screen?.WorkingArea.Width ?? OverlayTokens.StatsShowAllMetricsW)
                            - OverlayTokens.StatsScreenMarginPx;
            _machine.StatsMetricCount = StatsLayout.VisibleMetricCount(available);

            ApplyStatsValues(s);
        });
    }

    /// <summary>The rows currently in the panel, in render order (empty until first sync).</summary>
    private readonly List<StatsRowView> _statsRows = new();

    /// <summary>Row kinds behind <see cref="_statsRows"/>, index-aligned with it.</summary>
    private readonly List<StatsRow> _statsRowKinds = new();

    /// <summary>
    /// The row set the settings currently ask for, empty only for preset <c>Off</c>.
    /// </summary>
    private IReadOnlyList<StatsRow> ResolvedStatsRows =>
        StatsLayout.ResolveRows(_settings.StatsRowsPreset, _settings.StatsRows);

    /// <summary>True when the panel has no rows at all (preset Off) — the surface stays hidden.</summary>
    private bool StatsSurfaceEmpty =>
        StatsLayout.ShouldCollapseStatsSurface(OverlayKind.SystemStats, enabled: true, _statsRowKinds.Count);

    /// <summary>
    /// Rebuild the panel from the settings list and push the row count into the machine so
    /// the pill height follows. Rebuilds only when the resolved set actually changed —
    /// per-tick work stays in <see cref="ApplyStatsValues"/>.
    /// </summary>
    private void SyncStatsRows()
    {
        var rows = ResolvedStatsRows;
        if (!SameRows(rows, _statsRowKinds))
        {
            SystemStatsPanel.Children.Clear();
            _statsRows.Clear();
            _statsRowKinds.Clear();
            foreach (var row in rows)
            {
                var view = new StatsRowView
                {
                    IsCaptionRow = StatsLayout.IsCaptionRow(row),
                    Label = StatsLayout.LabelFor(row),
                    Value = "—"
                };
                _statsRows.Add(view);
                _statsRowKinds.Add(row);
                SystemStatsPanel.Children.Add(view);
            }
        }
        // Height budget always follows the resolved count, so a preset change that keeps the
        // same rows but the same count also stays correct after a settings edit.
        _machine.StatsRowCount = rows.Count;
    }

    private static bool SameRows(IReadOnlyList<StatsRow> a, List<StatsRow> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>Render one SystemSnapshot into the current stats rows. Idempotent.</summary>
    private void ApplyStatsValues(SystemSnapshot s)
    {
        for (var i = 0; i < _statsRows.Count; i++)
        {
            var view = _statsRows[i];
            switch (_statsRowKinds[i])
            {
                case StatsRow.Cpu:
                    view.Value = $"{s.CpuPercent:F0} %";
                    view.ValueBrush = StatsBrushHigh(
                        s.CpuPercent, OverlayTokens.StatsCpuWarn, OverlayTokens.StatsCpuCrit);
                    break;

                case StatsRow.Memory:
                    if (s.RamTotalBytes > 0)
                    {
                        var usedGb = s.RamUsedBytes / 1_000_000_000.0;
                        var totalGb = s.RamTotalBytes / 1_000_000_000.0;
                        view.Value = $"{usedGb:F1} / {totalGb:F0} ГБ";
                        view.ValueBrush = StatsBrushHigh(
                            s.RamPercent, OverlayTokens.StatsRamWarn, OverlayTokens.StatsRamCrit);
                    }
                    else
                    {
                        view.Value = "—";
                        view.ValueBrush = StatsBrushNormal();
                    }
                    break;

                case StatsRow.Battery:
                    if (s.BatteryPercent is { } bp)
                    {
                        view.Value = $"{bp:F0} %{(s.OnAcPower ? " ⚡" : "")}";
                        view.ValueBrush = StatsBrushLow(
                            bp, OverlayTokens.StatsBatteryWarn, OverlayTokens.StatsBatteryCrit);
                    }
                    else
                    {
                        // Desktop / no battery — placeholder stays in the normal colour.
                        view.Value = "—";
                        view.ValueBrush = StatsBrushNormal();
                    }
                    break;

                case StatsRow.Network:
                    // Network never goes critical — one normal-coloured value, down + up.
                    view.Value =
                        $"{FormatNetRate(s.NetDownBytesPerSec, "↓")}  {FormatNetRate(s.NetUpBytesPerSec, "↑")}";
                    view.ValueBrush = StatsBrushNormal();
                    break;

                case StatsRow.Date:
                    view.Caption = DateFormatHelper.Format(DateTime.Now, DateFormat.FullLong);
                    break;
            }
        }
    }

    private void ApplyPowerSnapshot(PowerStatusSnapshot snap)
    {
        var prev = _lastPower;
        var prevPct = _prevPowerPercent;
        _lastPower = snap;
        _prevPowerPercent = snap.Percent;

        if (_settings.ShowBatteryInCollapsed)
            ApplySize();

        if (!_settings.ShowBatteryAlerts)
        {
            Paint();
            return;
        }

        var kind = _machine.Snapshot().Kind;
        // Don't interrupt an active notification/battery toast.
        if (kind is OverlayKind.Notification or OverlayKind.Battery)
        {
            Paint();
            return;
        }

        var showCharge = _powerSource is not null && (
            _powerSource.ConsumeChargeConnect(snap, prev) ||
            _powerSource.ConsumeChargeBump(snap, prevPct));

        if (showCharge && snap.HasBattery)
        {
            ShowChargePill(snap.Percent);
            return;
        }

        var threshold = BatteryAlertLogic.ClampLowPercent(_settings.LowBatteryPercent);
        if (_powerSource is not null && _powerSource.TakeLowAlert(threshold))
        {
            ShowLowBattery(snap.Percent);
            return;
        }

        Paint();
    }

    private void ShowChargePill(int percent)
    {
        var before = _machine.Snapshot().Kind;
        _machine.Dispatch(OverlayCommand.SetBattery, BatteryAlertLogic.ChargePayload(percent));
        var after = _machine.Snapshot().Kind;
        if (before != after) OnKindChanged(before, after);
        ApplySize();
        Paint();
    }

    private void ShowLowBattery(int percent)
    {
        var before = _machine.Snapshot().Kind;
        _machine.Dispatch(OverlayCommand.Notify, BatteryAlertLogic.LowBatteryPayload(percent));
        var after = _machine.Snapshot().Kind;
        if (before != after) OnKindChanged(before, after);
        ApplySize();
        Paint();
    }

    public void DemoChargePill() => ShowChargePill(_lastPower?.Percent ?? 67);

    public void DemoLowBattery()
    {
        _powerSource?.ResetLowLatch();
        var pct = Math.Min(_settings.LowBatteryPercent, 15);
        ShowLowBattery(pct);
    }

    // ── Island timer / stopwatch ──────────────────────────────────────────

    public void StartCountdownMinutes(int minutes)
    {
        if (!_settings.TimerEnabled) return;
        _userOpenedMedia = false;
        var secs = IslandTimerLogic.PresetToSeconds(minutes);
        var before = _machine.Snapshot().Kind;
        _machine.Dispatch(OverlayCommand.SetTimer, IslandTimerLogic.CountdownPayload(secs));
        var after = _machine.Snapshot().Kind;
        if (before != after) OnKindChanged(before, after);
        ApplySize();
        Paint();
        _tray?.RefreshLabels();
        _winTray?.RefreshLabels();
    }

    public void StartStopwatchFromSettings()
    {
        if (!_settings.TimerEnabled) return;
        _userOpenedMedia = false;
        var before = _machine.Snapshot().Kind;
        _machine.Dispatch(OverlayCommand.SetTimer, IslandTimerLogic.StopwatchPayload());
        var after = _machine.Snapshot().Kind;
        if (before != after) OnKindChanged(before, after);
        ApplySize();
        Paint();
    }

    public void CancelTimer()
    {
        if (_machine.Snapshot().Kind != OverlayKind.Timer) return;
        var before = _machine.Snapshot().Kind;
        _machine.Dispatch(OverlayCommand.Clear);
        var after = _machine.Snapshot().Kind;
        if (before != after) OnKindChanged(before, after);
        ApplySize();
        Paint();
        _tray?.RefreshLabels();
        _winTray?.RefreshLabels();
    }

    public bool IsTimerActive => _machine.Snapshot().Kind == OverlayKind.Timer;

    private void OnTimerPauseResume(object? sender, RoutedEventArgs e)
    {
        var snap = _machine.Snapshot();
        if (snap.Kind != OverlayKind.Timer) return;
        var next = OverlayMachine.Sanitize(snap.Payload);
        next.Playing = !next.Playing;
        _machine.Dispatch(OverlayCommand.SetTimer, next);
        Paint();
    }

    private void OnTimerPlusOne(object? sender, RoutedEventArgs e)
    {
        var snap = _machine.Snapshot();
        if (snap.Kind != OverlayKind.Timer || snap.Payload.CountUp) return;
        var next = OverlayMachine.Sanitize(snap.Payload);
        next.RemainingSeconds = Math.Min(359999, next.RemainingSeconds + 60);
        _machine.Dispatch(OverlayCommand.SetTimer, next);
        Paint();
    }

    private void OnTimerCancel(object? sender, RoutedEventArgs e) => CancelTimer();

    /// <summary>
    /// FontAudio digital-dot seconds strip along bottom of Idle/Collapsed (incl. hover/pin peek).
    /// Hidden while expanded overlay kinds show OverlayProgress / panel.
    /// </summary>
    private void UpdateSecondsStrip()
    {
        try
        {
            var kind = _machine.Snapshot().Kind;
            var idle = kind is OverlayKind.Idle or OverlayKind.Collapsed;
            var show = idle && _settings.ShowSecondsStrip && _settings.IslandVisible && !_hiddenByFullscreen;
            if (!show)
            {
                SecondsStrip.IsVisible = false;
                return;
            }

            // 1.12.3: the strip belongs to the capsule, which is the island again, so its dots
            // are counted from the capsule's own width. No split compensation is needed — the
            // ball lives outside the capsule and nothing clips the strip any more.
            var stripW = Pill.Width > 0 ? Pill.Width : Width;
            if (stripW <= 0) stripW = OverlayTokens.CollapsedW;
            var slots = SecondsStripLogic.SlotCountForWidth(stripW, SecondsStripView.DotWidth + 1.5);
            var lit = SecondsStripLogic.LitCount(DateTime.Now, slots);
            // Prefer accent; fall back to primary text
            var brush = new SolidColorBrush(ParseColor(_settings.ColorAccent, OverlayTokens.AccentHex));
            var ok = SecondsStripView.Apply(SecondsStrip, slots, lit, brush);
            SecondsStrip.IsVisible = ok;
        }
        catch (Exception ex)
        {
            AppLog.Warn("UpdateSecondsStrip failed", ex);
            try { SecondsStrip.IsVisible = false; } catch { /* ignore */ }
        }
    }

    private void ConfigureHoverPinFromSettings()

    {
        _hoverPin.Configure(
            _settings.HoverExpandEnabled && _settings.SystemStatsHoverPeek,
            _settings.ClickPinEnabled,
            _settings.HoverExpandDelayMs,
            _settings.HoverCollapseGraceMs);
        ApplyPinnedBorderVisual();
    }

    /// <summary>
    /// Advance the hover-pin machine. <see cref="HoverPinMachine.IsContentExpanded"/> is the single
    /// source of truth for the SystemStats peek: entering the expanded phase dispatches
    /// <see cref="OverlayCommand.SetSystemStats"/>, leaving it dispatches <see cref="OverlayCommand.Collapse"/>.
    /// No wall-clock auto-collapse — the machine's own grace timer drives the exit.
    /// </summary>
    private bool TickHoverPin(int deltaMs)
    {
        var kind = _machine.Snapshot().Kind;
        // SystemStats is itself a hover-driven state, so it must not force-reset the machine.
        if (kind is not (OverlayKind.Idle or OverlayKind.Collapsed or OverlayKind.SystemStats))
        {
            if (_hoverPin.Phase != HoverPinPhase.Collapsed)
            {
                _hoverPin.ResetToCollapsed();
                ApplySize();
                Paint();
                TickClock();
                return true;
            }
            return false;
        }
        var wasExpanded = _hoverPin.IsContentExpanded;
        var changed = _hoverPin.Tick(deltaMs);
        var isExpanded = _hoverPin.IsContentExpanded;
        if (isExpanded != wasExpanded)
            ApplyHoverExpandedState(isExpanded);
        if (changed)
            ApplyPinnedBorderVisual();
        return changed;
    }

    /// <summary>Show (open SystemStats) or hide (collapse back) the hover-peek surface.</summary>
    private void ApplyHoverExpandedState(bool expanded)
    {
        if (expanded)
        {
            // Preset Off resolves to zero rows: there is nothing to peek at, so don't
            // dispatch the surface at all (the pill stays collapsed).
            if (_settings.SystemStatsHoverPeek
                && !StatsLayout.ShouldCollapseStatsSurface(OverlayKind.SystemStats, _settings.SystemStatsEnabled, _statsRowKinds.Count))
            {
                _machine.Dispatch(OverlayCommand.SetSystemStats, new OverlayPayload
                {
                    SystemStats = _lastStats ?? SystemSnapshot.Empty
                });
                IslandSounds.Play(IslandSoundKind.Expand, _settings);
            }
            ApplyPinnedBorderVisual();
            ApplySize();
            Paint();
            TickClock();
        }
        else
        {
            // Pinned keeps the surface; only an unpinned collapse returns to Idle.
            if (_machine.Snapshot().Kind == OverlayKind.SystemStats)
            {
                _machine.Dispatch(OverlayCommand.Collapse);
                IslandSounds.Play(IslandSoundKind.Collapse, _settings);
            }
            ApplyPinnedBorderVisual();
            ApplySize();
            Paint();
            TickClock();
        }
    }

    private void OnPillHoverEnter()
    {
        var kind = _machine.Snapshot().Kind;
        if (kind is not (OverlayKind.Idle or OverlayKind.Collapsed or OverlayKind.SystemStats)) return;
        _hoverPin.PointerEnter();
        ApplyPinnedBorderVisual();
    }

    private void OnPillHoverLeave()
    {
        _hoverPin.PointerLeave();
        if (!_hoverPin.IsContentExpanded)
            ApplyHoverExpandedState(false);
    }

    private void ApplyPinnedBorderVisual()
    {
        // Pinned state gets a brighter outline. Run after any hover-pin transition
        // (Expand, Collapse, Escape, etc.). Idempotent.
        if (_hoverPin.IsPinned)
            Pill.BorderBrush = new SolidColorBrush(Color.Parse("#88FFFFFF"));
            SyncBlobFill();
    }

    /// <summary>
    /// Idle/Collapsed click. Three zones when clipboard cycle is active:
    ///   - left third  → cycle to previous history item
    ///   - middle third → Action Center (single click on the clock area)
    ///   - right third  → cycle to next history item
    /// Without clipboard cycle: existing behavior (single → pin/unpin; double → Action Center).
    /// Note: explicit chevron Buttons (`‹` / `›`) handle cycle clicks directly via
    /// OnCyclePrevClick / OnCycleNextClick — this X-position path is the fallback for
    /// the empty gap between chevron and clock area.
    /// </summary>
    /// <summary>
    /// Left half of the pill (1.12.2). <paramref name="releaseX"/> must be X relative to the
    /// left half and <paramref name="zoneW"/> that half's width, so the ⅓/⅓/⅓ zones keep their
    /// positions whether the pill is collapsed (170) or split (370 total, 170 island half).
    /// </summary>
    private void HandleIdlePillClick(double releaseX, double zoneW = 0)
    {
        var snap = _machine.Snapshot();
        // Clipboard-cycle zones take priority over pin/unpin when there's a history to browse.
        if (snap.Payload.ClipboardCycleCount > 1)
        {
            var pillW = zoneW > 0 ? zoneW : Pill.Bounds.Width;
            if (pillW <= 0) pillW = OverlayTokens.CollapsedWeatherW + 80;
            var third = pillW / 3.0;
            if (releaseX < third)
            {
                _machine.Dispatch(OverlayCommand.CycleClipboardPrev);
                ApplySize(); Paint();
                IslandSounds.Play(IslandSoundKind.Hover, _settings);
            }
            else if (releaseX > 2 * third)
            {
                _machine.Dispatch(OverlayCommand.CycleClipboardNext);
                ApplySize(); Paint();
                IslandSounds.Play(IslandSoundKind.Hover, _settings);
            }
            else
            {
                // Middle = clock zone → Action Center directly (per user request).
                TrayService.OpenActionCenter();
                IslandSounds.Play(IslandSoundKind.Notify, _settings);
            }
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - _lastPillClickUtc).TotalMilliseconds < 400)
        {
            _lastPillClickUtc = DateTime.MinValue;
            TrayService.OpenActionCenter();
            return;
        }
        _lastPillClickUtc = now;

        if (!_settings.ClickPinEnabled)
        {
            TrayService.OpenActionCenter();
            return;
        }

        var wasPinned = _hoverPin.IsPinned;
        _hoverPin.ClickTogglePin();
        if (!wasPinned && _hoverPin.IsPinned)
            IslandSounds.Play(IslandSoundKind.Expand, _settings);
        else if (wasPinned && !_hoverPin.IsPinned)
        {
            IslandSounds.Play(IslandSoundKind.Collapse, _settings);
            // Unpinned while the SystemStats surface is up → close it too.
            if (_machine.Snapshot().Kind == OverlayKind.SystemStats)
            {
                _machine.Dispatch(OverlayCommand.Collapse);
                IslandSounds.Play(IslandSoundKind.Collapse, _settings);
            }
        }
        PlayClickPop();

        // If we unpinned but pointer is still over, restart hover peek promptly.
        if (!_hoverPin.IsPinned && _pointerOverPill && _settings.HoverExpandEnabled)
            _hoverPin.PointerEnter();

        ApplySize();
        Paint();
    }

    /// <summary>
    /// Visible chevron buttons on the pill edges. Always available on every overlay kind.
    /// Left chevron: cycle to previous IslandSlot (Idle → Notification → Media → Weather).
    /// Right chevron: cycle to next IslandSlot (Idle → Weather → Media → Notification).
    /// The clipboard-cycle behavior stays accessible via the existing X-position logic
    /// in HandleIdlePillClick when clipboard history is active.
    /// </summary>
    private void OnCyclePrevClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        e.Handled = true;
        _machine.Dispatch(OverlayCommand.CyclePrev);
        ApplySize();
        Paint();
        IslandSounds.Play(IslandSoundKind.Hover, _settings);
    }

    private void OnCycleNextClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        e.Handled = true;
        _machine.Dispatch(OverlayCommand.CycleNext);
        ApplySize();
        Paint();
        IslandSounds.Play(IslandSoundKind.Hover, _settings);
    }

    /// <summary>Alternative click handlers wired when clipboard cycle is active.</summary>
    private void OnClipboardCyclePrevClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        e.Handled = true;
        _machine.Dispatch(OverlayCommand.CycleClipboardPrev);
        ApplySize();
        Paint();
        IslandSounds.Play(IslandSoundKind.Hover, _settings);
    }

    private void OnClipboardCycleNextClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        e.Handled = true;
        _machine.Dispatch(OverlayCommand.CycleClipboardNext);
        ApplySize();
        Paint();
        IslandSounds.Play(IslandSoundKind.Hover, _settings);
    }

    /// <summary>
    /// Hide (preferred) or click-through while exclusive/fullscreen. Fail-soft.
    /// Does not mutate IslandVisible setting — restores when leaving fullscreen.
    /// </summary>
    private void PollFullscreen()
    {
        try
        {
            if (!_settings.IslandVisible)
            {
                // User hid island — do not fight; clear transient fullscreen flags.
                if (_clickThroughActive)
                {
                    _clickThroughActive = false;
                    Win32Overlay.ApplyClickThrough(this, false);
                }
                _hiddenByFullscreen = false;
                return;
            }

            var fs = Win32Overlay.IsFullscreenOrBusy();
            if (fs)
            {
                if (_settings.HideOnFullscreen)
                {
                    if (!_hiddenByFullscreen)
                    {
                        _hiddenByFullscreen = true;
                        if (_clickThroughActive)
                        {
                            _clickThroughActive = false;
                            Win32Overlay.ApplyClickThrough(this, false);
                        }
                        Opacity = 0;
                        IsHitTestVisible = false;
                        Hide();
                        AppLog.Info("Fullscreen detected — island hidden");
                    }
                }
                else if (_settings.ClickThroughOnFullscreen)
                {
                    if (!_clickThroughActive)
                    {
                        _clickThroughActive = true;
                        Win32Overlay.ApplyClickThrough(this, true);
                        AppLog.Info("Fullscreen detected — click-through");
                    }
                    if (_hiddenByFullscreen)
                    {
                        _hiddenByFullscreen = false;
                        Show();
                        Opacity = 1;
                        IsHitTestVisible = true;
                        Win32Overlay.ApplyNoActivate(this);
                        Win32Overlay.ApplyZOrder(this, _settings.ZOrderMode);
                        PlaceIsland();
                    }
                }
            }
            else
            {
                if (_hiddenByFullscreen)
                {
                    _hiddenByFullscreen = false;
                    Show();
                    Opacity = 1;
                    IsHitTestVisible = true;
                    Win32Overlay.ApplyNoActivate(this);
                    Win32Overlay.ApplyZOrder(this, _settings.ZOrderMode);
                    PlaceIsland();
                    AppLog.Info("Left fullscreen — island restored");
                }
                if (_clickThroughActive)
                {
                    _clickThroughActive = false;
                    Win32Overlay.ApplyClickThrough(this, false);
                    Win32Overlay.ApplyNoActivate(this);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("PollFullscreen failed", ex);
        }
    }
}

