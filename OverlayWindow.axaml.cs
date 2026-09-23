using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly AppSettings _settings;
    private WindowsWeatherSource _weather;
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
    private double _idleFillA = 1.0;
    private readonly ScaleTransform _pillScale = new(1, 1);
    private readonly TransformGroup _pillTransforms = new();
    private readonly DispatcherTimer _pulseTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _breathTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private double _pulsePhase;
    private double _breathPhase;
    private bool _pulseActive;
    private bool _breathActive;
    private bool _hoverWired;

    // Explicit width/height morph (Avalonia Window Width Transitions are unreliable).
    private readonly DispatcherTimer _morphTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _morphWatch = new();
    private bool _morphActive;
    private double _morphFromW, _morphFromH, _morphToW, _morphToH;
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
        _settings = AppSettings.Load();
        _settings.Normalize();
        _machine.WeatherEnabled = _settings.WeatherEnabled;
        _weather = new WindowsWeatherSource(_settings.Latitude, _settings.Longitude);
        _pillFill = ParseColor(_settings.ColorCapsuleFill, OverlayTokens.FillHex);
        _idleFillA = _settings.Opacity;

        EnableMorphTransitions();
        WirePointerGestures();
        SeedIcons();
        ApplyWeatherSide();
        ApplyPalette();
        ApplyOpacity();
        ApplyIslandVisibility();

        // Prefer WinForms NotifyIcon (visible on Win11 Sandbox); Avalonia TrayIcon as fallback.
        try
        {
            _winTray ??= new WinFormsTray(this);
            _winTray.RefreshIcon(_machine.UnreadCount);
            AppLog.Warn("Using WinFormsTray as primary tray");
        }
        catch (Exception ex)
        {
            AppLog.Warn("WinFormsTray ctor failed — falling back to Avalonia TrayService", ex);
            try
            {
                _tray ??= new TrayService(this);
                _tray.RefreshIcon(_machine.UnreadCount);
            }
            catch (Exception ex2)
            {
                AppLog.Warn("TrayService ctor failed", ex2);
            }
        }

        Opened += (_, _) =>
        {
            Win32Overlay.ApplyNoActivate(this);
            Win32Overlay.ApplyZOrder(this, _settings.ZOrderMode);
            PlaceIsland();
            _ = RefreshWeatherAsync();
            if (_winTray is null && _tray is null)
            {
                try
                {
                    _tray ??= new TrayService(this);
                    _tray.RefreshIcon(_machine.UnreadCount);
                }
                catch (Exception ex)
                {
                    AppLog.Warn("TrayService Opened init failed", ex);
                }
            }
        };
        KeyDown += OnKey;
        _clock.Tick += (_, _) => TickClock();
        _tick.Tick += (_, _) =>
        {
            var before = _machine.Snapshot().Kind;
            _machine.Tick(200);
            var after = _machine.Snapshot().Kind;
            if (before != after) OnKindChanged(before, after);
            Paint();
            if (before != after) ApplySize();
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

        _clock.Start();
        _tick.Start();
        _weatherTimer.Start();
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
        DateText.FontSize = Math.Max(10, fs - 1);
        DateText.FontFamily = family;
        WeatherTempText.FontSize = fs;
        WeatherTempText.FontFamily = family;
        OverlayTitle.FontSize = fs;
        OverlayTitle.FontFamily = family;
        BadgeText.FontSize = Math.Max(9, fs - 2);
        BadgeText.FontFamily = family;
        MediaPlayGlyph.FontSize = Math.Max(10, fs - 1);
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
            Pill.BorderBrush = new SolidColorBrush(Color.Parse("#55FFFFFF"));
            Pill.Background = new SolidColorBrush(WithAlpha(_pillFill, Math.Min(1.0, _idleFillA + 0.06)));
            IslandSounds.Play(IslandSoundKind.Hover, _settings);
        };
        Pill.PointerExited += (_, _) =>
        {
            Pill.BorderBrush = new SolidColorBrush(Color.Parse("#28FFFFFF"));
            ApplyOpacity();
        };
    }

    /// <summary>
    /// Re-apply morph / fade / rubber durations from <see cref="AppSettings.AnimationSpeed"/>.
    /// Off → ~1 ms transitions and no pulse/breath. Called from ctor and settings Apply.
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
        // Do NOT put Opacity/Scale transitions here — they fight pulse/breath timers.
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
            StopBreathing();
        }
        else
        {
            var snap = _machine.Snapshot();
            var overlayOn = IsOverlayKind(snap.Kind);
            SyncUnreadPulse(!overlayOn && snap.UnreadCount > 0);
            SyncBreathing(!overlayOn);
        }
    }

    private static bool IsOverlayKind(OverlayKind kind) =>
        kind is OverlayKind.Notification or OverlayKind.Progress or OverlayKind.Media
            or OverlayKind.Timer or OverlayKind.Error or OverlayKind.Expanded or OverlayKind.Weather;

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

    private void SyncBreathing(bool shouldBreath)
    {
        var breathSpeed = AnimationTiming.Effective(_settings.AnimationSpeed, _settings.AnimIdleBreath);
        if (!shouldBreath || !_settings.AnimBreathEnabled || !AnimationTiming.IsEnabled(breathSpeed))
        {
            StopBreathing();
            return;
        }
        if (_breathActive) return;
        _breathActive = true;
        _breathPhase = 0;
        _breathTimer.Tick -= OnBreathTick;
        _breathTimer.Tick += OnBreathTick;
        _breathTimer.Start();
    }

    private void StopBreathing()
    {
        if (_breathActive)
        {
            _breathTimer.Stop();
            _breathTimer.Tick -= OnBreathTick;
            _breathActive = false;
        }
        _pillScale.ScaleX = 1.0;
        _pillScale.ScaleY = 1.0;
    }

    private void OnBreathTick(object? sender, EventArgs e)
    {
        var breathSpeed = AnimationTiming.Effective(_settings.AnimationSpeed, _settings.AnimIdleBreath);
        if (!_settings.AnimBreathEnabled || !AnimationTiming.IsEnabled(breathSpeed))
        {
            StopBreathing();
            return;
        }
        var period = AnimationTiming.ScaleMs(AnimationTiming.BreathPeriodMs, breathSpeed);
        _breathPhase += (Math.PI * 2.0) * (33.0 / Math.Max(1, period));
        if (_breathPhase > Math.PI * 2.0) _breathPhase -= Math.PI * 2.0;
        // Subtle but perceptible ±2.5% scale breath (no Scale Transition fighting this timer)
        var s = 1.0 + 0.025 * Math.Sin(_breathPhase);
        _pillScale.ScaleX = s;
        _pillScale.ScaleY = s;
    }

    private static Color WithAlpha(Color c, double a) =>
        Color.FromArgb((byte)Math.Clamp((int)Math.Round(a * 255), 0, 255), c.R, c.G, c.B);

    private void ApplyOpacity()
    {
        _idleFillA = Math.Clamp(_settings.Opacity, 0.35, 1.0);
        Pill.Background = new SolidColorBrush(WithAlpha(_pillFill, _idleFillA));
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

        ClockText.Foreground = new SolidColorBrush(text);
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
        var dateText = DateText;
        var weather = MinimalWeather;
        var dot = UnreadDot;
        row.Children.Clear();
        if (_settings.WeatherSide == WeatherSide.Left)
        {
            row.Children.Add(weather);
            row.Children.Add(clockText);
            row.Children.Add(dateText);
            row.Children.Add(dot);
        }
        else
        {
            row.Children.Add(clockText);
            row.Children.Add(dateText);
            row.Children.Add(weather);
            row.Children.Add(dot);
        }
    }

    private void ApplyOrientationLayout()
    {
        var vertical = IslandLayout.IsVertical(_settings.Orientation, _settings.Edge);
        CollapsedRow.Orientation = vertical ? Avalonia.Layout.Orientation.Vertical : Avalonia.Layout.Orientation.Horizontal;
        MinimalWeather.Orientation = vertical ? Avalonia.Layout.Orientation.Vertical : Avalonia.Layout.Orientation.Horizontal;
    }

    private void WirePointerGestures()
    {
        Pill.PointerPressed += OnPillPointerPressed;
        Pill.PointerMoved += OnPillPointerMoved;
        Pill.PointerReleased += OnPillPointerReleased;
        Pill.PointerCaptureLost += (_, _) => ResetSwipeVisual();
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
        _pressing = true;
        _pressWatch.Restart();
        e.Pointer.Capture(Pill);
        e.Handled = true;
    }

    private void OnPillPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_pressing) return;
        // Drag-reposition removed: pointer is reserved for swipe gestures only.
        var pos = e.GetPosition(this);
        var dx = pos.X - _pressOrigin.X;
        var dy = pos.Y - _pressOrigin.Y;
        var damp = 0.45;
        _pillTranslate.X = Math.Clamp(dx * damp, -56, 56);
        _pillTranslate.Y = Math.Clamp(dy * damp, -40, 40);
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
        var adx = Math.Abs(dx);
        var ady = Math.Abs(dy);
        var dist = Math.Sqrt(dx * dx + dy * dy);

        ResetSwipeVisual();

        if (dist <= OverlayTokens.SwipeClickMaxPx)
        {
            var kind = _machine.Snapshot().Kind;
            if (kind is OverlayKind.Idle or OverlayKind.Collapsed)
                TrayService.OpenActionCenter();
            e.Handled = true;
            return;
        }

        if (dist < OverlayTokens.SwipeFirePx)
        {
            e.Handled = true;
            return;
        }

        var before = _machine.Snapshot().Kind;
        if (adx >= ady)
            _machine.Dispatch(dx < 0 ? OverlayCommand.CycleNext : OverlayCommand.CyclePrev);
        else if (dy > 0)
            _machine.Dispatch(OverlayCommand.Collapse);
        else
            _machine.Dispatch(OverlayCommand.ExpandWidget);

        var after = _machine.Snapshot().Kind;
        IslandSounds.Play(IslandSoundKind.Swipe, _settings);
        if (before != after) OnKindChanged(before, after);
        ApplySize();
        Paint();
        e.Handled = true;
    }


    private void ResetSwipeVisual()
    {
        _pillTranslate.X = 0;
        _pillTranslate.Y = 0;
        _pressing = false;
    }

    private void OpenContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Menu("Demo F9", () => { if (_demoOn) StopDemo(); else StartDemo(); }));
        var weatherLabel = _settings.WeatherEnabled ? "Погода выкл" : "Погода вкл";
        menu.Items.Add(Menu(weatherLabel, ToggleWeather));
        menu.Items.Add(Menu("Свернуть", () =>
        {
            var before = _machine.Snapshot().Kind;
            _machine.Dispatch(OverlayCommand.Collapse);
            OnKindChanged(before, _machine.Snapshot().Kind);
            ApplySize(); Paint();
        }));
        menu.Items.Add(Menu("Настройки…", OpenSettings));
        menu.Items.Add(Menu("Выход", () =>
            (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown()));
        menu.Open(Pill);
    }

    public void OpenSettings()
    {
        try
        {
            if (_settingsWindow is { IsVisible: true })
            {
                _settingsWindow.Activate();
                _settingsWindow.Topmost = true;
                _settingsWindow.Topmost = false;
                return;
            }

            _settingsWindow = new SettingsWindow(_settings, ApplySettingsFromUi);
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
        TickClock();
        ApplyWeatherSide();
        ApplyOrientationLayout();
        ApplyPalette();
        ApplyOpacity();
        ApplyTypography();
        ApplyAnimationSettings();
        _lastWeatherIconKey = ""; // force weather icon reload for new pack
        SeedIcons();
        ApplyIslandVisibility();
        Win32Overlay.ApplyZOrder(this, _settings.ZOrderMode);
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

    private void OnKindChanged(OverlayKind before, OverlayKind after)
    {
        _prevKind = before;
        var wasCollapsed = before is OverlayKind.Idle or OverlayKind.Collapsed;
        var nowCollapsed = after is OverlayKind.Idle or OverlayKind.Collapsed;
        if (after == OverlayKind.Notification)
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
        else if (e.Key == Key.Escape)
        {
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
        ClockText.Text = now.ToString("HH:mm", CultureInfo.InvariantCulture);
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
    }

    private void ApplySize()
    {
        var snap = _machine.Snapshot();
        ApplyOrientationLayout();
        var (w, h) = IslandLayout.SizeFor(snap.Kind, snap.WeatherEnabled, _settings.Orientation, _settings.Edge);

        var fromW = Pill.Width > 0 ? Pill.Width : Width;
        var fromH = Pill.Height > 0 ? Pill.Height : Height;
        if (fromW <= 0) fromW = w;
        if (fromH <= 0) fromH = h;

        var same = Math.Abs(fromW - w) < 0.5 && Math.Abs(fromH - h) < 0.5;
        var inflate = (w * h) >= (fromW * fromH);
        var morphSpeed = AnimationTiming.Effective(
            _settings.AnimationSpeed,
            inflate ? _settings.AnimMorphInflate : _settings.AnimMorphCollapse);
        if (same || !AnimationTiming.IsEnabled(morphSpeed) || !IsVisible)
        {
            StopMorph(snapToTarget: false);
            ResetMorphVisuals();
            SetSizeImmediate(w, h);
            return;
        }

        var enteringNotify = inflate && snap.Kind == OverlayKind.Notification;
        var leavingNotify = !inflate && _prevKind == OverlayKind.Notification;
        StartMorph(fromW, fromH, w, h, morphSpeed, inflate, enteringNotify, leavingNotify);
    }

    private void SetSizeImmediate(double w, double h)
    {
        Width = w;
        Height = h;
        Pill.Width = w;
        Pill.Height = h;
        Pill.CornerRadius = new CornerRadius(Math.Min(w, h) / 2);
        PlaceIsland();
    }

    private void StartMorph(
        double fromW, double fromH, double toW, double toH,
        AnimationSpeed morphSpeed, bool inflate, bool enteringNotify, bool leavingNotify)
    {
        StopBreathing();
        _morphFromW = fromW;
        _morphFromH = fromH;
        _morphToW = toW;
        _morphToH = toH;
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
        if (!_breathActive)
        {
            _pillScale.ScaleX = 1;
            _pillScale.ScaleY = 1;
        }
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
        Width = cw;
        Height = ch;
        Pill.Width = cw;
        Pill.Height = ch;
        Pill.CornerRadius = new CornerRadius(Math.Min(cw, ch) / 2);
        ApplyMorphAux(auxT, t);
        PlaceIsland();

        if (t >= 1.0)
        {
            StopMorph(snapToTarget: false);
            ResetMorphVisuals();
            SetSizeImmediate(_morphToW, _morphToH);
            var snap = _machine.Snapshot();
            SyncBreathing(!IsOverlayKind(snap.Kind));
        }
    }

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

    private void ApplyMorphAux(double auxT, double rawT)
    {
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
        var pw = (int)Math.Round(Width * scale);
        var ph = (int)Math.Round(Height * scale);
        var (x, y) = IslandLayout.Place(
            wa.X, wa.Y, wa.Width, wa.Height, pw, ph,
            _settings.Edge, _settings.OffsetX, _settings.OffsetY);
        Position = new PixelPoint(x, y);
        Win32Overlay.ApplyZOrder(this, _settings.ZOrderMode);
    }

    private void Paint()
    {
        var snap = _machine.Snapshot();
        var kind = snap.Kind;
        var p = snap.Payload;
        var overlayOn = kind is OverlayKind.Notification or OverlayKind.Progress or OverlayKind.Media
            or OverlayKind.Timer or OverlayKind.Error or OverlayKind.Expanded or OverlayKind.Weather;

        OverlayPanel.IsVisible = overlayOn;
        CollapsedRow.IsVisible = !overlayOn;

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
            var ts = TimeSpan.FromSeconds(Math.Ceiling(p.RemainingSeconds));
            sub = $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}";
        }
        else if (kind == OverlayKind.Progress && string.IsNullOrWhiteSpace(sub))
            sub = $"{(int)Math.Round(p.Progress * 100)}%";

        OverlayTitle.Text = string.IsNullOrWhiteSpace(sub) ? title : $"{title} · {sub}";
        OverlaySubtitle.Text = "";

        OverlayProgress.Value = p.Progress * 100;
        OverlayProgress.IsVisible = kind is OverlayKind.Progress or OverlayKind.Media;

        var textPrimary = ParseColor(_settings.ColorTextPrimary, OverlayTokens.TextHex);
        var accent = ParseColor(_settings.ColorAccent, OverlayTokens.AccentHex);
        OverlayTitle.Foreground = new SolidColorBrush(kind == OverlayKind.Error ? Color.Parse(OverlayTokens.ErrorHex) : textPrimary);
        AppIcon.Background = new SolidColorBrush(kind == OverlayKind.Error ? Color.Parse(OverlayTokens.ErrorHex) : accent);
        UnreadBadge.Background = new SolidColorBrush(accent);

        if (kind == OverlayKind.Weather)
            SetKindIconWeather(p.WeatherCode ?? snap.LastWeather.WeatherCode ?? 0);
        else
            SetKindIcon(kind);

        MediaPlay.IsVisible = kind == OverlayKind.Media;
        MediaPlayGlyph.Text = p.Playing ? "||" : "▶";

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

        SyncBreathing(!overlayOn);

        ToolTip.SetTip(this, kind == OverlayKind.Idle ? "NotifyIsland" : OverlayTitle.Text);
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
        var next = OverlayMachine.Sanitize(_machine.Snapshot().Payload);
        next.Playing = !next.Playing;
        _machine.Dispatch(OverlayCommand.SetMedia, next);
        Paint();
    }
}
