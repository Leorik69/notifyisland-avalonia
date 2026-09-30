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
    private WindowsPowerSource? _powerSource;
    private PowerStatusSnapshot? _lastPower;
    private int? _prevPowerPercent;
    private byte[]? _lastArtworkBytes;
    private ClipboardHistory _clipboardHistory = new();
    private WindowsClipboardSource? _clipboardSource;
    private SystemMonitorMachine? _statsMachine;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _weatherTimer = new() { Interval = TimeSpan.FromMilliseconds(OverlayTokens.WeatherRefreshMs) };
    private CancellationTokenSource? _weatherCts;

    private Point _pressOrigin;
    private bool _pressing;
    private bool _weatherIconFlip;
    private string _lastWeatherIconKey = "";
    private readonly TranslateTransform _pillTranslate = new();
    private readonly Stopwatch _pressWatch = new();
    private OverlayKind _lastKind = OverlayKind.Idle;
    private double _marqueeElapsedMs;
    /// <summary>
    /// Marquee's <c>TranslateTransform</c>. Declared in code rather than via <c>x:Name</c> on the
    /// XAML child element: the Avalonia 11.3 source generator does not register fields for
    /// <c>x:Name</c> on transform children of a <c>RenderTransform</c> property, only on direct
    /// children of the root element. Assigned to <c>MarqueeText.RenderTransform</c> in the
    /// constructor (the same place the field initialiser that already exists wires up
    /// <c>Pill.RenderTransform</c>).
    /// </summary>
    private readonly TranslateTransform _marqueeShift = new();
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
    // One 33 ms frame clock shared by every short, self-contained capsule animation: the
    // unread-dot pulse and the click pop. It used to belong to the pulse alone; a click
    // starting a second loop beside a running one meant two timers stepping at two rates, so
    // the pop rides this one instead and the window is back to zero timers as soon as the
    // last of them finishes. No Avalonia.Animation anywhere — see PlayClickPop for why.
    private readonly DispatcherTimer _frameTimer = new() { Interval = TimeSpan.FromMilliseconds(OverlayTokens.CapsuleFrameTickMs) };
    private bool _frameActive;
    private double _pulsePhase;
    private bool _pulseActive;
    private bool _clickPopActive;
    /// <summary>Scaled ClickPopMs of the pop in flight; 0 when none is.</summary>
    private int _clickPopMs;
    /// <summary>Wall clock of the pop in flight — see Motion for why not an accumulator.</summary>
    private readonly Stopwatch _clickPopWatch = new();
    private bool _hoverWired;
    private readonly HoverPinMachine _hoverPin = new();
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromMilliseconds(OverlayTokens.FullscreenPollMs) };
    private bool _hiddenByFullscreen;
    private bool _clickThroughActive;
    private bool _pointerOverUi;                                  // aggregate over Pill, Blob, HistoryPanel
    private int _uiHoverCount;                                    // refcount so an enter/exit pair across two controls cancels cleanly
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

    // -- 1.12.3 clipboard history panel -------------------------------------------
    // Opened by a click on the ball, closed by a second ball click, Escape, a click on the
    // panel's own empty padding, or picking a row. The panel rides the EXISTING 16 ms morph
    // tick (see ApplyHistoryPanelAnim) — opening it changes the window size, so there is
    // already a morph running and the panel has a progress value to read for free. A second
    // timer would be a second source of "when is this animation over".
    private bool _historyOpen;
    /// <summary>Row hosts currently in the panel, kept so the hover highlight can be moved and a
    /// close can reset exactly what it showed. Typed as Border (not Control) because the hover
    /// IS a Background change, and going through Control would need a cast at every call site.
    /// Avalonia.Controls.Border spelled out: this file also sees System.Windows.Forms.</summary>
    private readonly List<Avalonia.Controls.Border> _historyRowControls = new();
    /// <summary>Row count the panel is currently laid out for — the window's size input.</summary>
    private int _historyRowCount;
    /// <summary>Panel's slide offset in DIP, written by the morph tick (negative = travelling in).</summary>
    private readonly TranslateTransform _historySlide = new();
    private readonly ScaleTransform _historyScale = new(1, 1);
    /// <summary>+1 while the panel is opening, -1 while it is closing, 0 at rest.</summary>
    private int _historyDir;
    /// <summary>Row whose background is currently lit by the pointer, -1 for none.</summary>
    private int _historyHover = -1;

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

    // -- 1.13: clipboard ball features (wheel cycle / context menu / drag-to-pin) ----
    /// <summary>
    /// Index into the newest-first snapshot, the ball preview is showing. <c>0</c> means
    /// "newest = the entry that just got captured"; non-zero means the user wheeled DOWN and is
    /// browsing older items. Reset to 0 on every new capture (handled in
    /// <see cref="OnClipboardCaptured"/>) so the freshly-copied value always pops up first.
    /// </summary>
    private int _ballPreviewIndex;
    /// <summary>Wall clock the most recent rope pulse was started at. Null = no pulse in flight.
    /// The 200 ms tick reads it to drive the stroke-width bump; a second capture within the
    /// 200 ms window replaces it instead of stacking (see <see cref="OnClipboardCaptured"/>).</summary>
    private long? _bridgePulseStartedAtMs;
    /// <summary>True iff <see cref="AppSettings.IsBlobPinned"/> is set. Cached here so the pin
    /// halo doesn't have to read settings every frame.</summary>
    private bool _blobIsPinned;
    /// <summary>
    /// The scenario this morph is playing, chosen once in <see cref="StartMorph"/> and read by
    /// every frame. Declared in Core (spec: animation layer, "Морфы капсулы") so the phase list and
    /// the breakpoints inside it can be pinned by tests — this file cannot be built by the test
    /// project. Kept as a field, not recomputed per tick, for the same reason the sizes are: a
    /// style must not be able to change mid-morph.
    /// </summary>
    private CapsuleMorphTrack _morphTrack = CapsuleMorphTrack.Plain;
    private OverlayKind _prevKind = OverlayKind.Idle;
    private readonly Random _morphRng = new();

    public AppSettings Settings => _settings;

    public OverlayWindow()
    {
        InitializeComponent();
        _pillTransforms.Children.Add(_pillScale);
        _pillTransforms.Children.Add(_pillTranslate);
        Pill.RenderTransform = _pillTransforms;
        // The marquee shift is declared in code (see the field docs): the Avalonia 11.3 source
        // generator does not register fields for x:Name on a transform child of RenderTransform,
        // so the XAML's <TranslateTransform x:Name="MarqueeShift"/> would compile without a
        // matching code-side field. Wire it onto the TextBlock here instead.
        MarqueeText.RenderTransform = _marqueeShift;
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
                action => Avalonia.Threading.Dispatcher.UIThread.Post(action))
            {
                // The privacy-pause lambda is re-evaluated by the listener each tick. Pointing
                // it at our own method keeps the listener dumb (no AppSettings reference) and
                // gives us a single place to add the "is the pause still active" logic.
                IsPaused = PrivacyPauseActive,
            };
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
        WireBlobGestures();
        WireHistoryPanel();
        ConfigureHoverPinFromSettings();
        SeedIcons();
        ApplyWeatherSide();
        ApplyPalette();
        ApplyOpacity();
        ApplyIslandVisibility();
        ApplyPinnedOffsetFromSettings();
        ApplyBallCountBadge();
        RefreshTrayPauseLabel();

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
            // 1.13: the timer's digits live in a monitor row now, so its countdown is
            // refreshed from here. The row is only rebuilt when the panel is on screen —
            // a timer ticking against a closed panel would be work nobody can see.
            var timerBefore = _machine.TimerActive;
            _machine.Tick(200);
            var after = _machine.Snapshot().Kind;
            var splitAfter = _machine.IsSplitClipboard;
            if (before != after) OnKindChanged(before, after);
            var hoverChanged = TickHoverPin(200);
            if (_machine.TimerActive != timerBefore || SystemStatsPanel.IsVisible)
                ApplyTimerRow();
            TickMarquee(200);
            Paint();
            UpdateSecondsStrip();
            if (before != after || hoverChanged || splitBefore != splitAfter) ApplySize();
            // 1.12.3 idle breathe of the ball. Reuses this 200 ms tick (the one that already
            // expires the split) — a 2.4 s sine at 200 ms is 12 samples per period, smooth
            // enough for a ±0.5 DIP drift, and it adds no timer.
            ApplyBlobBreathe();
            // 1.13: rope pulse on new capture. Reads the start timestamp, advances the bump,
            // and self-clears after 200 ms. The stroke-width bumps from 1 to 1.6 DIP and back.
            // Reuses the same 200 ms tick — no new timer, per the spec.
            TickBridgePulse();
            var unread = _machine.UnreadCount;
            if (unread != _lastTrayUnread)
            {
                _lastTrayUnread = unread;
                _tray?.RefreshIcon(unread);
                _winTray?.RefreshIcon(unread);
            }
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
        // 1.13: the media/timer glyphs moved into StatsRowView, so they are no longer scaled
        // from the capsule's font size here — a control that lives in the monitor panel should
        // follow the panel's type scale, not the capsule clock's.
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

        // The System Stats panel must stay open while the pointer is over ANY of the
        // surfaces the user is likely to read: the capsule (Pill, including the panel
        // inside it), the blob (which sits OUTSIDE the pill), and the history panel
        // (also outside). A refcount keeps a fast move from one control to another —
        // say, pill → ball — from briefly registering as "leave all" and starting the
        // 5-second grace prematurely.
        WireUiHover(Pill);
        WireUiHover(Blob);
        WireUiHover(HistoryPanel);
    }

    /// <summary>Wire PointerEnter/Leave that feed the shared hover-refcount.</summary>
    private void WireUiHover(Avalonia.Controls.Control control)
    {
        control.PointerEntered += (_, _) => OnUiHoverEntered();
        control.PointerExited += (_, _) => OnUiHoverExited();
    }

    private void OnUiHoverEntered()
    {
        // Stale events from before the window hid itself must not re-open the panel.
        if (!_settings.IslandVisible || _hiddenByFullscreen) return;
        _uiHoverCount++;
        if (_uiHoverCount == 1)
        {
            _pointerOverUi = true;
            PillEnterVisuals();
            OnPillHoverEnter();
        }
    }

    private void OnUiHoverExited()
    {
        if (_uiHoverCount > 0) _uiHoverCount--;
        if (_uiHoverCount == 0)
        {
            _pointerOverUi = false;
            PillLeaveVisuals();
            OnPillHoverLeave();
        }
    }

    private void PillEnterVisuals()
    {
        Pill.BorderBrush = new SolidColorBrush(Color.Parse(
            _hoverPin.IsPinned ? "#88FFFFFF" : "#55FFFFFF"));
        Pill.Background = new SolidColorBrush(WithAlpha(_pillFill, Math.Min(1.0, _idleFillA + 0.06)));
        SyncBlobFill();
        IslandSounds.Play(IslandSoundKind.Hover, _settings);
    }

    private void PillLeaveVisuals()
    {
        if (!_hoverPin.IsPinned)
        {
            Pill.BorderBrush = new SolidColorBrush(Color.Parse("#28FFFFFF"));
            ApplyOpacity();
        }
    }

    /// <summary>
    /// Re-apply morph / fade / hover durations from <see cref="AppSettings.AnimationSpeed"/>.
    /// Off → ~1 ms transitions and no pulse. Called from ctor and settings Apply.
    /// </summary>
    private void ApplyAnimationSettings()
    {
        var global = _settings.AnimationSpeed;
        var fadeSpeed = AnimationTiming.Effective(global, _settings.AnimMorphInflate);
        var hoverSpeed = AnimationTiming.Effective(global, _settings.AnimHover);
        var fade = TimeSpan.FromMilliseconds(AnimationTiming.ScaleMs(OverlayTokens.IconCrossfadeMs, fadeSpeed));
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
        // _pillTranslate is owned by the morph per frame (ApplyMorphAux writes X/Y up to ~35 ms
        // apart during Glitch). An Avalonia transition here would race the morph's own write:
        // every step lands 180 ms late and the capsule visibly lags the jitter. Translate
        // easing is owned by the morph layer (AnimEase) and the spring in ApplyMorphAux; we
        // only need to make sure no other transition fights it.
        _pillTranslate.Transitions = null;
        _pillScale.Transitions = null;

        // Reduced motion: stop the weather cycles (spin/bob/pulse). The icon hosts register
        // themselves with MeteoconsMotion on attach, so a single push reaches every live host
        // and lands it on the neutral pose without rebuilding the icon.
        MeteoconsMotion.SetReducedMotion(
            AnimReduced.Resolve(OsAnimationsEnabled(), _settings.ReducedMotion));

        if (!AnimationTiming.IsEnabled(global))
        {
            StopMorph(snapToTarget: true);
            StopUnreadPulse(resetOpacity: false);
            StopClickPop();
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
        // 1.12.4: reduced motion stops the endless cycles too, not just the transitions. The spec
        // lists the unread pulse and the ball's breathe by name: a loop with no end state has
        // nothing to land on, so "duration 0" cannot express it — the only reduced form of a loop
        // is not running it. The dot is left at its resting opacity by StopUnreadPulse.
        var reduced = AnimReduced.Resolve(OsAnimationsEnabled(), _settings.ReducedMotion);
        if (!shouldPulse || !_settings.AnimPulseEnabled || !AnimationTiming.IsEnabled(pulseSpeed) || reduced)
        {
            StopUnreadPulse(resetOpacity: false);
            return;
        }
        if (_pulseActive) return;
        _pulseActive = true;
        _pulsePhase = 0;
        EnsureFrameTick();
    }

    /// <summary>
    /// Turn the pulse off. Deliberately does NOT stop the timer: the click pop may still be
    /// running on the same clock, and the tick stops the timer itself as soon as nothing is
    /// animating any more (see <see cref="OnFrameTick"/>).
    /// </summary>
    private void StopUnreadPulse(bool resetOpacity)
    {
        _pulseActive = false;
        if (resetOpacity) UnreadDot.Opacity = 0;
    }

    /// <summary>Start the shared frame clock if it is not already running.</summary>
    private void EnsureFrameTick()
    {
        if (_frameActive) return;
        _frameActive = true;
        _frameTimer.Tick -= OnFrameTick;
        _frameTimer.Tick += OnFrameTick;
        _frameTimer.Start();
    }

    /// <summary>
    /// The shared 33 ms frame. Each sub-tick reports whether it is still animating; when the last
    /// one is done the clock stops itself, so an idle capsule costs no timer at all.
    /// </summary>
    private void OnFrameTick(object? sender, EventArgs e)
    {
        var busy = TickUnreadPulse();
        if (TickClickPop()) busy = true;
        if (busy) return;
        _frameTimer.Stop();
        _frameTimer.Tick -= OnFrameTick;
        _frameActive = false;
    }

    /// <summary>
    /// One pulse frame; false once the pulse is off or has been disabled in settings.
    /// <para>
    /// 1.12.4: the timer and the media surfaces have no animation of their own to migrate — they
    /// enter and leave through the same morph as every other kind, and the sine below is the only
    /// endless motion in the capsule besides the ball's breathe. So the reduced-motion promise for
    /// this file is carried by two guards: this pulse and <see cref="ApplyBlobBreathe"/>.
    /// </para>
    /// </summary>
    private bool TickUnreadPulse()
    {
        if (!_pulseActive) return false;
        var pulseSpeed = AnimationTiming.Effective(_settings.AnimationSpeed, _settings.AnimUnreadPulse);
        if (!_settings.AnimPulseEnabled || !AnimationTiming.IsEnabled(pulseSpeed))
        {
            StopUnreadPulse(resetOpacity: false);
            return false;
        }
        var period = AnimationTiming.ScaleMs(AnimationTiming.PulsePeriodMs, pulseSpeed);
        _pulsePhase += (Math.PI * 2.0) * (OverlayTokens.CapsuleFrameTickMs / Math.Max(1, period));
        if (_pulsePhase > Math.PI * 2.0) _pulsePhase -= Math.PI * 2.0;
        // Visible 0.40 ↔ 1.0 opacity pulse (no Opacity Transition fighting this timer)
        UnreadDot.Opacity = 0.70 + 0.30 * Math.Sin(_pulsePhase);
        return true;
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
        // 1.13: the media/timer control glyphs are gone from the capsule; their accent now
        // lives in the row styles (StatsRowView), which the theme does not reach into.
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
            OpenContextMenu(isBallContext: true);
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
        if (dist > OverlayTokens.ClickMaxPx)
        {
            // Real drag. Spec §«Закрепление шарика»: a release-without-modifier pins the ball at
            // the release position; Ctrl on release clears the pin (returns to home).
            ApplyBallPinOnRelease(e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control));
            return;
        }
        // Short tap (no drag): the click path. Single click still opens the panel; double-click
        // is wired separately via Gestures.DoubleTapped.
        HandleBlobClick();
    }

    /// <summary>Clicks only (1.8.1). Swipe L/R/U/D cycle/collapse removed — CycleNext/Prev remain for API/tests.</summary>
    private void WirePointerClicks()
    {
        Pill.PointerPressed += OnPillPointerPressed;
        Pill.PointerReleased += OnPillPointerReleased;
        Pill.PointerCaptureLost += (_, _) => ResetPressState();
    }

    /// <summary>
    /// Wire the ball's 1.13 gestures: PointerWheel (preview cycle inside the ball, panel does
    /// NOT open), DragOver/Drop (file drops into history), and DoubleTapped (re-copy the
    /// newest entry back to the system clipboard). The right-click ContextMenu is the same
    /// <see cref="OpenContextMenu"/> the capsule uses — see <see cref="OnBlobPointerPressed"/>.
    /// </summary>
    private void WireBlobGestures()
    {
        Blob.AddHandler(PointerWheelChangedEvent, OnBlobPointerWheel, handledEventsToo: false);
        // Avalonia 11: gestures on a control are added via the routed event directly (not
        // via Gestures.SetDoubleTapped — that API exists for elements that don't expose the
        // event, but InputElement.DoubleTapped is the canonical path here).
        Blob.DoubleTapped += OnBlobDoubleTapped;
        // Drag-drop on the ball: file paths land in the history as File / MultiFile. The
        // window has WS_EX_NOACTIVATE, but Avalonia's DragDrop routed events do not depend
        // on activation — they fire as long as AllowDrop=true and a draggable source is
        // over us. The spec calls this out explicitly.
        DragDrop.SetAllowDrop(Blob, true);
        Blob.AddHandler(DragDrop.DragEnterEvent, OnBlobDragOver);
        Blob.AddHandler(DragDrop.DragOverEvent, OnBlobDragOver);
        Blob.AddHandler(DragDrop.DropEvent, OnBlobDrop);
    }

    /// <summary>
    /// Wheel on the ball cycles the preview INSIDE the ball, in newest-first. The first wheel
    /// down jumps to the oldest (spec's literal "first wheel-down replaces with item N-1"),
    /// each subsequent wheel-down walks one step toward newer, wheel-up is the mirror. Wrap at
    /// both ends. Critically, scrolling on the ball does NOT open the panel — it just changes
    /// the in-ball preview. The panel's existing cycle is separate and still wired the same
    /// way it was before.
    /// </summary>
    private void OnBlobPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        // Only react when the ball is in its idle/detached state — the spec is about the ball
        // preview, not the morphing capsule. Mid-morph, swallowing the wheel is the right
        // thing because the panel-side cycle already exists for that case.
        if (_morphActive) return;
        var snap = _clipboardHistory.SnapshotNewestFirst();
        if (snap.Count == 0) return;
        // Each "notch" of the wheel is one step; touchpads can deliver fractional deltas, so
        // use the integer delta and ignore the fractional remainder. The spec is one-notch =
        // one-row, so this is the right shape.
        var delta = e.Delta.Y > 0 ? 1 : (e.Delta.Y < 0 ? -1 : 0);
        if (delta == 0) return;
        _ballPreviewIndex = BallPreviewCycle.Step(_ballPreviewIndex, delta, snap.Count);
        var entry = BallPreviewCycle.Resolve(snap, _ballPreviewIndex);
        if (entry is null) return;
        ApplyBallPreviewFromEntry(entry);
        e.Handled = true;
    }

    /// <summary>
    /// Double-click on the ball: re-copy the most-recent entry back to the system clipboard.
    /// The "I lost focus, get my copy back" gesture. Single-click still toggles the history
    /// panel; the two gestures do not collide because <see cref="HandleBlobClick"/> treats
    /// anything past <see cref="OverlayTokens.ClickMaxPx"/> as a drag.
    /// </summary>
    private void OnBlobDoubleTapped(object? sender, RoutedEventArgs e)
    {
        var entry = _clipboardHistory.Latest;
        if (entry is null) return;
        var ok = entry.Kind switch
        {
            ClipboardItemKind.Text => WindowsClipboardWriter.WriteText(entry.Text ?? ""),
            ClipboardItemKind.File or ClipboardItemKind.MultiFile =>
                WindowsClipboardWriter.WriteFiles(entry.Paths ?? new List<string>()),
            _ => false,
        };
        AppLog.Info(ok
            ? $"Ball double-click re-copied: {entry.Kind}"
            : $"Ball double-click re-copy FAILED: {entry.Kind}");
        // Light a sound either way so the gesture has feedback even when the writer is a noop.
        IslandSounds.Play(IslandSoundKind.Hover, _settings);
        e.Handled = true;
    }

    /// <summary>Filter: only file drops are accepted; text drops go through the OS listener.</summary>
    private void OnBlobDragOver(object? sender, Avalonia.Input.DragEventArgs e)
    {
        e.DragEffects = HasAnyStorageItem(e.DataTransfer) ? Avalonia.Input.DragDropEffects.Copy : Avalonia.Input.DragDropEffects.None;
    }

    /// <summary>
    /// File drop onto the ball: write the file paths to the system clipboard via the same
    /// <see cref="WindowsClipboardWriter"/> the tray submenu and history panel use, then push
    /// the resulting entry into the history. The OnClipboardCaptured handler picks it up from
    /// the listener side and fires the rope pulse.
    /// </summary>
    private void OnBlobDrop(object? sender, Avalonia.Input.DragEventArgs e)
    {
        try
        {
            if (!HasAnyStorageItem(e.DataTransfer)) return;
            var paths = new List<string>();
            var items = e.DataTransfer.TryGetFiles();
            if (items is null) return;
            foreach (var storage in items)
            {
                if (storage is null) continue;
                // StorageItem.Path on Windows is an Uri with a file:// scheme; .LocalPath gives
                // back the absolute filesystem path the writer needs. Empty paths (rare; e.g.
                // a virtual file dragged from a search result) are skipped, not pushed as "".
                var local = storage.Path?.LocalPath ?? "";
                if (!string.IsNullOrWhiteSpace(local)) paths.Add(local);
            }
            if (paths.Count == 0) return;
            // Round-trip through WindowsClipboardWriter so the system clipboard matches what
            // the history now holds; otherwise the next listener tick would re-detect the same
            // content and treat it as a fresh capture with a NEW timestamp.
            var ok = WindowsClipboardWriter.WriteFiles(paths);
            if (!ok)
            {
                AppLog.Warn($"Ball drop: WindowsClipboardWriter.WriteFiles failed for {paths.Count} paths");
                return;
            }
            // Synthesize the entry the listener would have produced and push it directly. Going
            // through the listener is a race (its timer reads the clipboard, which is now OUR
            // content); pushing manually keeps the drop's order-of-events deterministic.
            var entry = paths.Count == 1
                ? ClipboardEntry.FromFile(paths[0], DateTimeOffset.UtcNow)
                : ClipboardEntry.FromFiles(paths, DateTimeOffset.UtcNow);
            _clipboardHistory.Push(entry);
            OnClipboardCaptured(entry);
        }
        catch (Exception ex)
        {
            AppLog.Warn("OnBlobDrop failed", ex);
        }
    }

    private static bool HasAnyStorageItem(Avalonia.Input.IDataTransfer? data)
    {
        if (data is null) return false;
        try
        {
            // TryGetFiles returns null when the format is not offered. We treat that as "no
            // storage items" and let the drop go to None — text drops then continue to flow
            // through the OS clipboard listener, which is exactly the spec's "ignore text drops"
            // requirement.
            var items = data.TryGetFiles();
            return items is { Length: > 0 };
        }
        catch { return false; }
    }

    /// <summary>
    /// Write the in-ball preview from a single ClipboardEntry — used by the wheel cycle and
    /// the post-capture reset. Reuses the same IconPackService.Create + ClipboardHalfPreview
    /// rules the auto-update path uses, so a wheeled-back preview looks identical to the
    /// preview the user sees on a fresh capture.
    /// </summary>
    private void ApplyBallPreviewFromEntry(ClipboardEntry entry)
    {
        var payload = ClipboardHistory.BuildPayload(entry, entry.CapturedAt == default ? DateTimeOffset.UtcNow : entry.CapturedAt);
        var tint = new SolidColorBrush(Color.Parse(ClipboardHalfPreview.IconTintHexFor(entry.Kind)));
        BlobIcon.Child = IconPackService.Create(
            _settings.IconPack, ClipboardHalfPreview.IconKeyFor(entry.Kind),
            CurrentIconCollapsed(), tint);
        var baseText = ClipboardHalfPreview.TextFor(payload);
        BlobText.Text = baseText + ClipboardHalfPreview.RunSuffix(entry.RunCount);
    }

    /// <summary>
    /// Wire the privacy-pause hook into the clipboard listener. The listener asks this lambda
    /// each tick; while it returns true, captures are drained but never surfaced (see
    /// <see cref="WindowsClipboardSource.IsPaused"/>).
    /// </summary>
    private bool PrivacyPauseActive() =>
        ClipboardPrivacyPause.IsActive(_settings.ClipboardPrivacyPauseUntilUtc, DateTime.UtcNow);

    /// <summary>Enable the privacy pause for the default duration and refresh the tray.</summary>
    private void EnablePrivacyPause()
    {
        _settings.ClipboardPrivacyPauseUntilUtc = ClipboardPrivacyPause.Activate(DateTime.UtcNow);
        _settings.Save();
        AppLog.Info($"Privacy pause: enabled until {_settings.ClipboardPrivacyPauseUntilUtc:O}");
        RefreshTrayPauseLabel();
    }

    /// <summary>Disable the privacy pause early (when the user picks the toggle from the menu).</summary>
    private void DisablePrivacyPause()
    {
        _settings.ClipboardPrivacyPauseUntilUtc = null;
        _settings.Save();
        AppLog.Info("Privacy pause: cleared");
        RefreshTrayPauseLabel();
    }

    /// <summary>
    /// Push the current pause state into both tray implementations (WinForms and Avalonia).
    /// The pause only changes the TOOLTIP, never the icon — the icon stays on "no unread"
    /// because no capture is in flight. Calling this from the 200 ms tick would be overkill;
    /// we call it from the toggle path AND from the same hook that drives the rope pulse, so
    /// a pause that expires during a session simply stops blocking the next capture.
    /// </summary>
    private void RefreshTrayPauseLabel()
    {
        var remaining = ClipboardPrivacyPause.RemainingMinutes(_settings.ClipboardPrivacyPauseUntilUtc, DateTime.UtcNow);
        var label = ClipboardPrivacyPause.TooltipText(remaining);
        try { _winTray?.SetTooltip(label); } catch { /* tray may be torn down on shutdown */ }
        try { _tray?.SetTooltip(label); } catch { /* tray may be torn down on shutdown */ }
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
                // 1.13: the Timer and Media cases are gone. Both used to be capsule kinds whose
                // click had something to decide (keep expanded / claim ownership from the
                // timer); both are monitor rows now, so their controls are clicked where they
                // are drawn and the capsule's own click path no longer has to know about them.
                // That is also what retired _userOpenedMedia: there is no longer a capsule for
                // SMTC and a timer to fight over.
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
    /// A click toggles the history panel (spec §«Панель истории»).
    /// </summary>
    /// <summary>
    /// The panel's own dismiss affordances: a press on its padding (the empty part) closes it.
    /// The rows handle their own press and mark it handled, so they never reach this — which is
    /// how "click a row" and "click the empty part" stay two different actions off one handler.
    /// </summary>
    private void WireHistoryPanel()
    {
        HistoryPanel.PointerPressed += (_, e) =>
        {
            if (!_historyOpen) return;
            if (e.Handled) return;
            CloseHistoryPanel();
            e.Handled = true;
        };
    }

    private void HandleBlobClick()
    {
        if (_historyOpen)
        {
            CloseHistoryPanel();
            return;
        }
        OpenHistoryPanel();
    }

    // -- 1.12.3 history panel: open / close / rows ------------------------------

    /// <summary>
    /// Unfold the history panel, or do nothing at all when there is no history.
    /// <para>
    /// Empty history is the case that decides the whole shape of this method: an empty panel is
    /// a list with nothing in it, which is the one thing the user cannot act on, and an
    /// «История пуста» placeholder would be the app admitting it has nothing rather than the
    /// clipboard being empty. So the click is simply absorbed — a sound, no panel, no error.
    /// </para>
    /// </summary>
    private void OpenHistoryPanel()
    {
        var rows = ClipboardHistoryRows.Build(_clipboardHistory, DateTimeOffset.UtcNow);
        if (rows.Count == 0)
        {
            AppLog.Info("Blob click: clipboard history is empty — panel not opened");
            IslandSounds.Play(IslandSoundKind.Hover, _settings);
            return;
        }

        _historyRowCount = rows.Count;
        BuildHistoryRows(rows);
        _historyOpen = true;
        _historyDir = 1;
        // Visible and hit-testable from the first frame, at zero opacity. Making it hit-testable
        // only when the fade finished would mean a click during the 300 ms fade went through to
        // the ball and TOGGLED THE PANEL SHUT — the fastest possible way to make the panel feel
        // broken. It is transparent, not absent: a transparent-but-present control takes the
        // click, which is what "open" means.
        HistoryPanel.IsVisible = true;
        HistoryPanel.IsHitTestVisible = true;
        HistoryPanel.Opacity = 0;
        HistoryPanel.Width = ClipboardHistoryPanel.SizeFor(rows.Count).Width;
        HistoryPanel.Height = ClipboardHistoryPanel.SizeFor(rows.Count).Height;
        IslandSounds.Play(IslandSoundKind.Expand, _settings);
        // The window has to grow to hold the panel, and ApplySize is what does that — the same
        // path the blob attach uses, so the panel and the ball share one morph.
        ApplySize();
        PlaceHistoryPanel();
    }

    /// <summary>
    /// Fold the panel away. The rows stay alive until the dismiss finishes, so the panel is one
    /// continuous surface rather than something rebuilt and re-faded.
    /// </summary>
    private void CloseHistoryPanel()
    {
        if (!_historyOpen) return;
        _historyOpen = false;
        _historyDir = -1;
        // The window shrinks back; ApplySize reads _historyOpen, not the animation state.
        ApplySize();
    }

    /// <summary>
    /// Build one control per row. Built in code, like the System Stats rows, because the row
    /// count is data (however many things the user copied) and not a markup constant.
    /// <para>
    /// The row is a Border with a hover background rather than a Button: a Button brings its own
    /// chrome, its own focus ring and its own keyboard behaviour, none of which a mouse-only
    /// overlay list wants. The hover is the whole affordance.
    /// </para>
    /// </summary>
    private void BuildHistoryRows(IReadOnlyList<ClipboardHistoryRow> rows)
    {
        HistoryRows.Children.Clear();
        _historyRowControls.Clear();
        _historyHover = -1;

        var brush = new SolidColorBrush(ParseColor(_settings.ColorTextSecondary, OverlayTokens.TextSecondaryHex));
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var index = i;

            // 1.13: each row's icon gets its own format-tinted brush (text #9CC4FF / file
            // #C8C8CC / multi-file #7AA8FF). The header colour still drives the title text so
            // the panel reads as one consistent typography, but the icon differentiates the
            // formats at a glance.
            var iconBrush = new SolidColorBrush(
                Color.Parse(ClipboardHalfPreview.IconTintHexFor(row.Entry.Kind)));
            var icon = new Viewbox
            {
                Width = 12,
                Height = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                IsHitTestVisible = false,
                Child = IconPackService.Create(_settings.IconPack, row.IconKey, 12, iconBrush),
            };
            var title = new TextBlock
            {
                // 1.13: the run-count suffix (— ×N) lives on the title so the panel reads
                // "first item — ×N" the way the spec asks. The suffix is empty when RunCount
                // is 1, so a fresh row stays exactly the same as it was before this feature.
                Text = row.Title + ClipboardHalfPreview.RunSuffix(row.RunCount),
                FontFamily = IslandFonts.Resolve(_settings.FontFamily),
                FontSize = 11,
                Foreground = Avalonia.Media.Brushes.White,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                // One line, always: a wrapped row would make the panel taller than the window
                // that was sized for exactly N rows, and the last row would fall off the edge.
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = OverlayTokens.HistoryPanelW - 2 * OverlayTokens.HistoryPanelPadX - 12 - 4 - 46,
            };
            var age = new TextBlock
            {
                Text = row.AgeText,
                FontFamily = IslandFonts.Resolve(_settings.FontFamily),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.Parse("#888890")),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                IsHitTestVisible = false,
            };

            var line = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };
            line.Children.Add(icon);
            line.Children.Add(title);
            line.Children.Add(age);

            var host = new Border
            {
                Height = OverlayTokens.HistoryPanelRowH,
                CornerRadius = new CornerRadius(6),
                Background = Avalonia.Media.Brushes.Transparent,
                Padding = new Thickness(4, 0),
                Child = line,
            };
            host.PointerEntered += (_, _) => SetHistoryHover(index);
            host.PointerExited += (_, _) => SetHistoryHover(-1);
            host.PointerPressed += (_, e) =>
            {
                // Handled: a press on a row must not also be the "clicked the empty part of the
                // panel" signal that closes it, and must not reach the ball underneath.
                var props = e.GetCurrentPoint(host).Properties;
                if (props.IsRightButtonPressed)
                {
                    // 1.13: row-level pin toggle. The most-recent row (index 0) is unpinnable
                    // by spec; the menu shows that as a disabled entry. Core owns the rule, so
                    // we ask it directly.
                    OpenRowContextMenu(row, index, host);
                    e.Handled = true;
                    return;
                }
                e.Handled = true;
                ApplyHistoryRow(row);
            };

            HistoryRows.Children.Add(host);
            _historyRowControls.Add(host);
        }
    }

    /// <summary>
    /// 1.13 row-level context menu (spec §«Закреплённые записи»). The pin toggle is the
    /// primary action; the menu also lets the user re-copy the row (ApplyHistoryRow) without
    /// closing the panel, which the existing left-click already does. The most-recent row
    /// (chronological index 0) cannot be pinned: it would bounce between the pinned-first
    /// block and the chronological block on every new capture. Core enforces this — see
    /// <see cref="ClipboardHistory.Pin"/>.
    /// </summary>
    private void OpenRowContextMenu(ClipboardHistoryRow row, int index, Avalonia.Controls.Control anchor)
    {
        var menu = new Avalonia.Controls.ContextMenu();
        var entry = row.Entry;
        // Pin/unpin: only enabled when Core will accept the change. The newest row's
        // TogglePin always returns false, so the menu item surfaces that as disabled.
        var canPin = !entry.IsPinned && index > 0;
        var pinLabel = entry.IsPinned ? "Открепить" : "Закрепить";
        var pinItem = Menu(pinLabel, () =>
        {
            if (_clipboardHistory.TogglePin(index))
            {
                _settings.Save();
                var rows = ClipboardHistoryRows.Build(_clipboardHistory, DateTimeOffset.UtcNow);
                BuildHistoryRows(rows);
                AppLog.Info($"Row {index} ({entry.Kind} \"{row.Title}\") toggled pin");
            }
        });
        // IsEnabled needs to be set AFTER Menu(...) because the helper doesn't take it.
        pinItem.IsEnabled = canPin || entry.IsPinned;
        menu.Items.Add(pinItem);
        menu.Items.Add(Menu("Скопировать в буфер", () => ApplyHistoryRow(row)));
        menu.Items.Add(Menu("Удалить", () =>
        {
            // Pop the entry by timestamp+text match — Delete on a row that has just been
            // pinned clears the pin and then drops the row, since pinned rows still pop out
            // of the chronological block when removed from the ring. We re-use the
            // PopLatest path only when the user picked the head; otherwise we look up by
            // value.
            _clipboardHistory.PopLatest();
            ApplyBallCountBadge();
            if (_historyOpen)
            {
                var rows = ClipboardHistoryRows.Build(_clipboardHistory, DateTimeOffset.UtcNow);
                BuildHistoryRows(rows);
            }
        }));
        menu.Open(anchor);
    }

    /// <summary>Light one row's background. Re-asserting the same index is a no-op.</summary>
    private void SetHistoryHover(int index)
    {
        if (_historyHover == index) return;
        if (_historyHover >= 0 && _historyHover < _historyRowControls.Count)
            _historyRowControls[_historyHover].Background = Avalonia.Media.Brushes.Transparent;
        _historyHover = index;
        if (index >= 0 && index < _historyRowControls.Count)
            _historyRowControls[index].Background = new SolidColorBrush(Color.Parse("#1AFFFFFF"));
    }

    /// <summary>
    /// Put a history entry back on the system clipboard and close the panel — the same
    /// <see cref="WindowsClipboardWriter"/> path the tray submenu's re-copy uses. Reusing it
    /// rather than writing a second one matters: the clipboard is a global resource, and two
    /// writers disagreeing about, say, whether an empty string clears it is a bug nobody can
    /// reproduce from one of them.
    /// </summary>
    private void ApplyHistoryRow(ClipboardHistoryRow row)
    {
        var entry = row.Entry;
        var ok = entry.Kind switch
        {
            ClipboardItemKind.Text => WindowsClipboardWriter.WriteText(entry.Text ?? ""),
            ClipboardItemKind.File or ClipboardItemKind.MultiFile =>
                WindowsClipboardWriter.WriteFiles(entry.Paths ?? new List<string>()),
            _ => false,
        };
        AppLog.Info(ok
            ? $"History row applied: {entry.Kind} \"{row.Title}\""
            : $"History row FAILED to apply: {entry.Kind} \"{row.Title}\"");
        // The panel closes either way: a failed write is not something the user can fix by
        // staring at the list, and leaving it open over a success would hide the result.
        CloseHistoryPanel();
    }

    /// <summary>
    /// Per-frame panel motion, driven from the existing 16 ms morph tick. No new timer.
    /// <para>
    /// The window is already growing/shrinking around the panel, so its own <c>t</c> is the
    /// panel's progress — the two are the same movement by construction. The panel slides in
    /// from the ball's side and fades, on the same <see cref="AnimTimeline"/> the rest of the
    /// 1.12.4 layer uses, gated through <see cref="AnimReduced"/> so a user with animations off
    /// gets the end state rather than a faster version of the same travel.
    /// </para>
    /// </summary>
    /// <summary>
    /// Whether Windows animations are on (SPI_GETCLIENTAREAINIMATION, «Показывать анимации в
    /// Windows»), read once and cached.
    /// <para>
    /// A direct SPI call, not an Avalonia property: 11.3.22 exposes no public
    /// <c>SystemParameters.ClientAreaAnimation</c>, and Core cannot reference Avalonia types
    /// anyway. This is the app-layer half of <see cref="AnimReduced.Resolve"/> — Core owns the
    /// policy, the app owns the OS read, which is the split that file's docs describe.
    /// </para>
    /// <para>
    /// Cached because the answer can only change while the app is running if the user leaves the
    /// Settings app and comes back, and this is read from a per-frame animation tick: a syscall
    /// per frame to save a frame nobody will ever see.
    /// </para>
    /// </summary>
    private static bool? _osAnimationsEnabled;

    private static bool OsAnimationsEnabled()
    {
        if (_osAnimationsEnabled is { } cached) return cached;
        var enabled = true;
        try
        {
            if (NativeMethods.SystemParametersInfo(
                    NativeMethods.SPI_GETCLIENTAREAINIMATION, 0, out var v, 0))
                enabled = v != 0;
        }
        catch (Exception ex)
        {
            // A failure here must not break the animation: the safe default is "animations on",
            // which is what every user without the accessibility setting expects.
            AppLog.Warn("SPI_GETCLIENTAREAINIMATION failed — assuming animations enabled", ex);
            enabled = true;
        }
        _osAnimationsEnabled = enabled;
        return enabled;
    }

    /// <summary>
    /// The two P/Invokes the reduced-motion read needs, kept private and nested so the rest of
    /// the file cannot accidentally grow a general-purpose Win32 surface.
    /// </summary>
    private static class NativeMethods
    {
        /// <summary>SPI_GETCLIENTAREAINIMATION — «Показывать анимации в Windows».</summary>
        public const uint SPI_GETCLIENTAREAINIMATION = 0x1042;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out int pvParam, uint fWinIni);
    }

    private void ApplyHistoryPanelAnim(double t)
    {
        if (_historyDir == 0) return;
        var reversed = _historyDir < 0;
        // One phase, 0→1: the slide and the fade are the same event. Two phases here would be
        // two numbers to keep in sync for no gain — see AnimTimeline for what a schedule is for.
        var (_, local) = HistoryPanelTimeline.PhaseAt(reversed ? 1.0 - t : t);
        var reduced = AnimReduced.Resolve(OsAnimationsEnabled(), _settings.ReducedMotion);
        var eased = AnimEase.WithReducedMotion(AnimEase.Ease("power2.out", local), reduced);

        HistoryPanel.Opacity = eased;
        // 10 DIP of travel, from the direction the panel opens in: it comes FROM the ball, so
        // the gesture that reached for the ball is continued rather than interrupted.
        var dir = ClipboardHistoryPanel.CrossDirectionFor(_settings.Edge) > 0 ? 1 : -1;
        _historySlide.Y = SplitIsVertical ? 0 : (1.0 - eased) * -10 * dir;
        _historySlide.X = SplitIsVertical ? (1.0 - eased) * -10 * dir : 0;
        HistoryPanel.RenderTransform = new TransformGroup { Children = { _historySlide, _historyScale } };
    }

    /// <summary>
    /// Panel's resting state, called wherever a morph finishes. Mirrors <see cref="SettleBlob"/>:
    /// the transition is allowed to end in exactly one place, so no residual opacity or offset
    /// survives into the next open.
    /// </summary>
    private void SettleHistoryPanel()
    {
        if (_historyDir == 0) return;
        var open = _historyOpen;
        _historyDir = 0;
        _historySlide.X = 0;
        _historySlide.Y = 0;
        HistoryPanel.Opacity = open ? 1 : 0;
        HistoryPanel.IsVisible = open;
        HistoryPanel.IsHitTestVisible = open;
        if (open) return;
        // Closed for good: drop the controls so a later open rebuilds from the current history
        // rather than showing a stale list from ten copies ago.
        HistoryRows.Children.Clear();
        _historyRowControls.Clear();
        _historyHover = -1;
        _historyRowCount = 0;
    }

    /// <summary>
    /// One-phase schedule for the panel, declared once so appear and dismiss cannot disagree
    /// about what "the panel's own progress" means. Spec: the animation layer doc's AnimTimeline.
    /// </summary>
    private static readonly AnimTimeline HistoryPanelTimeline = new(new[] { ("panel", 0.0, 1.0) });

    /// <summary>
    /// Put the panel where <see cref="ClipboardHistoryPanel"/> says it goes, in window
    /// coordinates. Called from the morph tick and after <see cref="ApplySize"/>, because the
    /// panel's position is a function of the window size — the same reason
    /// <see cref="UpdateBlobVisual"/> re-derives the ball every frame.
    /// <para>
    /// The panel's coordinates are taken from the CAPSULE, and the window origin is added on top.
    /// The capsule sits at the window's leading edge on the long axis and at
    /// <see cref="HistoryWindowSeatFor"/>'s cross origin on the short one, so those two offsets
    /// ARE the window origin — read back rather than re-derived, so the panel cannot drift away
    /// from the ball by a rounding error.
    /// </para>
    /// </summary>
    private void PlaceHistoryPanel()
    {
        if (!_historyOpen) return;
        var (capsuleLong, capsuleCross) = IslandCapsuleSize();
        var (_, panelCross) = ClipboardHistoryPanel.SizeFor(Math.Max(1, _historyRowCount));
        var capsuleCentre = capsuleCross / 2;

        // Window-space origin of the capsule itself, from the same rule that seats the window.
        // Identical arithmetic to HistoryWindowSeatFor on purpose: the panel and the window have
        // to agree on where the capsule is, and two copies of that expression would drift.
        var scale = RenderScaling <= 0 ? 1 : RenderScaling;
        var crossOrigin = ClipboardHistoryPanel.CrossOriginFor(
            _settings.Edge, capsuleCentre, panelCross) * scale;

        var along = ClipboardHistoryPanel.PanelAlongStart(capsuleLong);
        var cross = ClipboardHistoryPanel.PanelCrossStart(_settings.Edge, capsuleCentre);
        // Margin is measured from the window's own top-left, so the capsule's position inside
        // the window is added to the capsule-relative geometry.
        HistoryPanel.Margin = SplitIsVertical
            ? new Thickness(crossOrigin + cross, along, 0, 0)
            : new Thickness(along, crossOrigin + cross, 0, 0);
    }

    private void OpenContextMenu()
    {
        OpenContextMenu(isBallContext: false);
    }

    /// <summary>
    /// Shared builder for both context surfaces (capsule right-click and ball right-click). The
    /// ball version adds the clipboard-only items the spec calls out: "История буфера",
    /// "Очистить историю", "Закрепить шарик на месте", "Не реагировать 30 мин". Both menus share
    /// the common items (Action Center, weather, settings) because right-clicking either element
    /// should still let the user get at the global controls.
    /// </summary>
    private void OpenContextMenu(bool isBallContext)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Menu("Центр уведомлений", TrayService.OpenActionCenter));
        if (_settings.TimerEnabled)
        {
            menu.Items.Add(Menu("Таймер 1 мин", () => StartCountdownMinutes(1)));
            menu.Items.Add(Menu("Таймер 5 мин", () => StartCountdownMinutes(5)));
            menu.Items.Add(Menu($"Таймер {_settings.TimerDefaultMinutes} мин (F12)", () => StartCountdownMinutes(_settings.TimerDefaultMinutes)));
            if (_machine.TimerActive)
                menu.Items.Add(Menu("Отменить таймер", CancelTimer));
        }
        var weatherLabel = _settings.WeatherEnabled ? "Погода выкл" : "Погода вкл";
        menu.Items.Add(Menu(weatherLabel, ToggleWeather));

        // 1.13 clipboard-only block — only when the user right-clicks the ball itself. The
        // capsule's context menu already routes to the panel via "История буфера" when the
        // ball is on screen; we don't duplicate it here.
        if (isBallContext)
        {
            menu.Items.Add(Menu(_historyOpen ? "Скрыть историю буфера" : "История буфера",
                () => HandleBlobClick()));
            // "Очистить историю" — empties the ring buffer; the panel closes if it was open.
            menu.Items.Add(Menu("Очистить историю", () =>
            {
                _clipboardHistory.Clear();
                if (_historyOpen) CloseHistoryPanel();
                ApplyBallCountBadge();
                AppLog.Info("Clipboard history cleared from ball context menu");
            }));
            // "Закрепить шарик на месте" / "Открепить шарик" — toggle the pin. The label flips
            // so the user can see the current state; Ctrl-release on drag is the other path to
            // the same outcome (see OnBlobPointerReleased).
            menu.Items.Add(Menu(_blobIsPinned ? "Открепить шарик" : "Закрепить шарик на месте",
                () =>
                {
                    if (_blobIsPinned)
                    {
                        _settings.ClearBlobPin();
                        _settings.Save();
                        _blobIsPinned = false;
                        _blobDragAlong = 0;
                        _blobDragCross = 0;
                        UpdateBlobVisual(travel: 1.0, opacity: _blobOpacity);
                    }
                    else
                    {
                        _settings.ClipboardBlobPinnedOffsetX = _blobDragAlong;
                        _settings.ClipboardBlobPinnedOffsetY = _blobDragCross;
                        _settings.Save();
                        _blobIsPinned = true;
                    }
                    ApplyPinHalo();
                }));
            // "Не реагировать 30 мин" — toggle the privacy pause. The label shows the current
            // remaining minutes when active so the user knows when it lifts.
            var pauseActive = PrivacyPauseActive();
            string pauseLabel;
            if (pauseActive)
            {
                var mins = ClipboardPrivacyPause.RemainingMinutes(
                    _settings.ClipboardPrivacyPauseUntilUtc, DateTime.UtcNow);
                pauseLabel = $"Не реагировать ({mins} мин осталось) — выключить";
            }
            else
            {
                pauseLabel = "Не реагировать 30 мин";
            }
            menu.Items.Add(Menu(pauseLabel, () =>
            {
                if (pauseActive) DisablePrivacyPause();
                else EnablePrivacyPause();
            }));
        }
        else if (_splitApplied)
        {
            // Capsule right-click still gets the history shortcut (1.12.3) — same path the ball
            // click uses. Only the ball gets the richer clipboard submenu.
            menu.Items.Add(Menu(_historyOpen ? "Скрыть историю буфера" : "История буфера",
                () => HandleBlobClick()));
        }
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

            _settingsWindow = new SettingsWindow(_settings, ApplySettingsFromUi,
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
        // The OS "animations in Windows" answer is cached because it is read from a per-frame
        // tick, but caching it for the whole process lifetime means a user who turns animations
        // off in Windows sees no change until they restart the app. Applying settings is a
        // deliberate act and happens at human speed, so this is the right moment to re-read it.
        _osAnimationsEnabled = null;
        ThemePresets.AutodetectCustom(_settings);
        _settings.Save();
        _machine.WeatherEnabled = _settings.WeatherEnabled;
        _weather = new WindowsWeatherSource(_settings.Latitude, _settings.Longitude);
        if (!_settings.WeatherEnabled && _machine.Snapshot().Kind == OverlayKind.Weather)
            _machine.Dispatch(OverlayCommand.Collapse);
        if (!_settings.TimerEnabled && _machine.TimerActive)
        {
            // 1.13: cancelling a timer is ClearTimer, not a full Clear — a disabled timer must
            // not wipe whatever the capsule was showing.
            _machine.ClearTimer();
            ApplyTimerRow();
        }
        if (!_settings.ShowNowPlaying)
        {
            _mediaSource?.Stop();
            _mediaSource = null;
            if (_mediaFromSmtc)
            {
                _mediaFromSmtc = false;
                _machine.ClearMedia();
                ApplyMediaRow();
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
        if (e.Key == Key.F12)
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
            // 1.12.3: the history panel is the topmost thing Escape can dismiss. It is checked
            // first because Escape must close ONE layer per press, and the panel is a layer above
            // the island — closing both at once is the shortcut that makes Escape feel broken.
            if (_historyOpen)
            {
                CloseHistoryPanel();
                e.Handled = true;
                return;
            }
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
            // 1.12.3: the panel cannot outlive the ball. The split has its own 6 s lifetime
            // (ClipboardHistory.MaxPillMs), so a panel left open across the expiry would keep
            // asking for the panel-sized window while the ball retracted into the capsule — the
            // panel would be drawn outside a window that had just shrunk around it, and the
            // "click the ball to open history" entry point would be gone. Closing here folds the
            // panel's dismissal into the same morph that pulls the ball back, so there is one
            // movement rather than two.
            if (!snap.IsSplitClipboard && _historyOpen)
            {
                _historyOpen = false;
                // -1 so the panel fades out on THIS morph's t. Left at 0 it would sit at full
                // opacity until some later morph happened to call SettleHistoryPanel, which may
                // never come — a panel stuck on screen over a shrinking window.
                _historyDir = -1;
            }
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
        // Reduced motion takes the same path as "animations off" in the speed setting, and on
        // purpose the same path: the branch below does not merely jump the size, it also
        // settles the ball and the panel to their defined resting states, which is exactly
        // what a user who cannot tolerate motion needs. Shorter durations would leave the
        // blob mid-travel and the panel mid-fade.
        // 1.12.4: this is the ONLY reduced-motion branch for size changes, and it is deliberately
        // a settle and not a zero-duration run. Everything downstream of it — the hover peek's
        // width change, the monitor's expansion, the appear/dismiss styles — reaches the reduced
        // path through this one check, so none of them can grow its own "just make it fast" exit.
        var reduced = AnimReduced.Resolve(OsAnimationsEnabled(), _settings.ReducedMotion);
        if (same || reduced || !AnimationTiming.IsEnabled(morphSpeed) || !IsVisible)
        {
            // No morph will run, so nothing will ever consume _blobDir or _historyDir. Settle the
            // ball and the panel to their defined resting states here instead of leaving them
            // mid-animation.
            StopMorph(snapToTarget: false);
            ResetMorphVisuals();
            SettleBlob();
            SettleHistoryPanel();
            SetSizeImmediate(w, h);
            return;
        }

        var enteringNotify = inflate && snap.Kind == OverlayKind.Notification;
        var leavingNotify = !inflate && _prevKind == OverlayKind.Notification;
        StartMorph(fromW, fromH, w, h, winFromW, winFromH, winToW, winToH,
            morphSpeed, inflate, enteringNotify, leavingNotify);
    }

    /// <summary>
    /// Window size for a capsule of <paramref name="w"/>×<paramref name="h"/>.
    /// <para>
    /// Three states, not two: no blob (the capsule alone), a blob, and a blob with the history
    /// panel unfolded. The third is not derivable from the second — the panel's window is
    /// ASYMMETRIC across the short axis, so it is a separate size, not a bigger blob window.
    /// </para>
    /// </summary>
    private (double Width, double Height) WindowFor(bool withBlob, double w, double h)
    {
        if (!withBlob) return (w, h);
        return _historyOpen && _historyRowCount > 0
            ? ClipboardHistoryPanel.WindowFor(SplitIsVertical, w, h, _historyRowCount)
            : ClipboardBlob.WindowFor(SplitIsVertical, w, h);
    }

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
        // The panel's position depends on the window size it just took, so it is placed after
        // the window is written — otherwise it would sit at the previous frame's coordinates for
        // one frame, which on a window that grew by 250 DIP is a visible flash at the old spot.
        PlaceHistoryPanel();
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
        // 1.12.4: the scenario is picked HERE, once, from the direction the morph already knows.
        // The tick only reads the track — it never re-decides the style, so "which phases and
        // which curves" is a single fact per morph instead of a switch evaluated 60 times a second.
        _morphTrack = !_morphUsesNotifyStyle
            ? CapsuleMorphTrack.Plain
            : inflate
                ? CapsuleMorphTrack.ForAppear(_morphAppear)
                : CapsuleMorphTrack.ForDismiss(_morphDismiss);
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
            $"{(inflate ? $"appear={_morphAppear}" : $"dismiss={_morphDismiss}")}, " +
            $"phases={_morphTrack.Describe()})");

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
        return _settings.AppearStyle;
    }

    private NotifyDismissStyle ResolveDismissStyle(bool leavingNotify)
    {
        if (!leavingNotify) return NotifyDismissStyle.Collapse;
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

        // 1.12.4: both channels come off ONE declared scenario (CapsuleMorphTrack). The width and
        // the window still ride the SAME size value — that pairing is what keeps a frame from
        // showing a capsule and a window computed from different points of the animation — and the
        // aux channel is the track's own phased value, so a style's breakpoints cannot drift away
        // from the style.
        var widthT = _morphTrack.SizeAt(t);
        var auxT = _morphTrack.AuxAt(t);

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
        PlaceHistoryPanel();

        if (t >= 1.0)
        {
            StopMorph(snapToTarget: false);
            ResetMorphVisuals();
            // A blob morph that just finished must land on the ball's resting values, not on
            // the last frame's scale/opacity. Settling also clears the direction so the next
            // attach starts from the capsule's centre again.
            SettleBlob();
            // Same for the panel: opacity, offset and the row cleanup land in exactly one place.
            SettleHistoryPanel();
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
        // 1.12.4: through the dictionary, so the detach pop and the click pop are visibly the
        // same curve reached by name (see AnimEase's clickPop entry).
        var pop = AnimEase.Ease("clickPop", p);
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
        var a = AnimEase.Ease("power2.out", ClipboardBlob.PeekPhaseAt(t));
        var b = AnimEase.Ease("power2.out", ClipboardBlob.DetachPhaseAt(t));
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
        // 1.13: drag-to-pin exception — when the user has pinned the ball, the resting position
        // IS the pinned offset, not (0, 0). The pin lives in AppSettings and is reapplied on
        // every ApplyBlobRest so a re-attach doesn't undo it.
        if (attached && _blobIsPinned)
        {
            (_blobDragAlong, _blobDragCross) = ClipboardBlob.ClampOffset(
                _settings.ClipboardBlobPinnedOffsetX ?? 0,
                _settings.ClipboardBlobPinnedOffsetY ?? 0,
                OverlayTokens.BlobDragMaxPx);
        }
        else
        {
            _blobDragAlong = 0;
            _blobDragCross = 0;
        }
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
        // 1.12.4: the second endless loop, and the other half of the reduced-motion promise the
        // spec states ("бесконечные циклы … выключены"). A loop has no end state to land on, so
        // reduced motion can only mean not running it; _blobBreathe is left at 0, which is the
        // ball's defined resting offset.
        if (AnimReduced.Resolve(OsAnimationsEnabled(), _settings.ReducedMotion)) return;
        _blobBreathe = ClipboardSplit.CrossBreatheOffset((int)_blobClock.ElapsedMilliseconds);
        UpdateBlobVisual(travel: 1.0, opacity: _blobOpacity);
    }

    /// <summary>
    /// 1.13 rope pulse (spec §«Пульс верёвки»). One-shot bump from 1.0 to 1.6 DIP and back
    /// over 200 ms, driven by the existing 200 ms tick (no new timer). The pulse starts on
    /// <see cref="StartBridgePulse"/>; we read the start timestamp, compute a normalised t in
    /// [0, 1], and emit the stroke width to the existing
    /// <see cref="UpdateBlobBridge"/> pipeline via <see cref="ClipBlobBridgeStrokeWidth"/>.
    /// <para>
    /// Two captures within 200 ms REPLACE the start (no stacking): a single nullable field
    /// overwritten by <see cref="StartBridgePulse"/> is exactly the right shape for that. After
    /// 200 ms the field is null again and the bridge rests at its baseline width of 1 DIP.
    /// </para>
    /// </summary>
    private void TickBridgePulse()
    {
        if (_bridgePulseStartedAtMs is not { } start) return;
        var elapsed = Environment.TickCount64 - start;
        const int pulseMs = 200;
        if (elapsed >= pulseMs)
        {
            _bridgePulseStartedAtMs = null;
            ClipBlobBridgeStrokeWidth(1.0);
            return;
        }
        // Triangle wave: 0→1.6 DIP at the midpoint (100 ms), back to 1 DIP at 200 ms.
        // Half-sine gives a softer bump than a triangle but with the same peak; either works,
        // sine is just what the existing AnimEase vocabulary offers.
        var t = elapsed / (double)pulseMs;
        var phase = t < 0.5 ? (t * 2.0) : (1.0 - (t - 0.5) * 2.0);
        var width = 1.0 + 0.6 * AnimEase.Ease("sine.out", phase);
        ClipBlobBridgeStrokeWidth(width);
    }

    /// <summary>
    /// Push a stroke-width override into the bridge pipeline. The geometry is unchanged — only
    /// the half-width on each rail bumps. Implemented as a tiny field read by
    /// <see cref="UpdateBlobBridge"/> via <see cref="BridgeStrokeWidthFactor"/> below.
    /// </summary>
    private double _bridgeStrokeWidthDip = 1.0;
    private void ClipBlobBridgeStrokeWidth(double widthDip)
    {
        _bridgeStrokeWidthDip = widthDip;
        UpdateBlobVisual(travel: 1.0, opacity: _blobOpacity);
    }

    /// <summary>
    /// 1.13 drag-to-pin visual cue (spec §«Закрепление шарика»). A hairline accent ring
    /// around the ball when pinned; the default 1-DIP 28%-white border when unpinned. The
    /// ring uses the accent colour so it reads as a deliberate state change rather than a
    /// hairline cosmetic.
    /// </summary>
    private void ApplyPinHalo()
    {
        if (_blobIsPinned)
        {
            Blob.BorderBrush = new SolidColorBrush(Color.Parse(OverlayTokens.AccentHex));
            Blob.BorderThickness = new Thickness(1.5);
        }
        else
        {
            Blob.BorderBrush = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
            Blob.BorderThickness = new Thickness(1);
        }
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
        // 1.13: the count badge tracks the ball's TOP-RIGHT corner: 4 DIP inside, so it sits on
        // the ball's rim like the UnreadBadge on the capsule does. The badge is visible only
        // when ApplyBallCountBadge turned it on (>1 history rows). Vertical mirrors across axes.
        var badgeOffsetX = vertical
            ? x - OverlayTokens.BlobD / 2 + 4   // cross axis top-left in window coords
            : x + OverlayTokens.BlobD / 2 - 4 - 18; // 18 = badge approx width
        var badgeOffsetY = vertical
            ? y + OverlayTokens.BlobD / 2 - 4 - 18
            : y - OverlayTokens.BlobD / 2 + 4;
        BallCountBadge.Margin = new Thickness(badgeOffsetX, badgeOffsetY, 0, 0);
        // 1.13 drag-to-pin visual cue: a hairline accent ring around the ball when pinned.
        // The BorderBrush / BorderThickness swap is the cheapest way to make the pin visible;
        // a separate decorative ellipse would be cleaner but it would need its own hit-test
        // island and the ball's existing border already lives on the same element.
        ApplyPinHalo();
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
            // 1.13 rope pulse: multiply the baseline half-width by the active stroke factor so
            // a 200 ms capture pulse bumps the rope from 1 DIP to 1.6 DIP and back. The factor
            // rests at 1.0 between captures (see TickBridgePulse).
            var hw = ClipboardBlob.BridgeHalfAt(tt) * _bridgeStrokeWidthDip;
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

    /// <summary>
    /// 1.12.4: the per-frame aux channel of one morph. <paramref name="auxT"/> is the size
    /// scenario's own phased value (<see cref="CapsuleMorphTrack.AuxAt"/>); <paramref name="rawT"/>
    /// is the un-eased morph progress, kept for the two styles whose effect IS a per-frame
    /// quantity rather than a curve: the Ragged jitter and the Glitch stutter.
    /// <para>
    /// The two style switches below therefore read the style, not the shape — every curve they
    /// evaluate now comes from the named dictionary, and the style-specific numbers (offsets, the
    /// stutter cell count) are tokens or the track's own, not bare literals.
    /// </para>
    /// </summary>
    private void ApplyMorphAux(double auxT, double rawT)
    {
        // 1.12.3: the ball's own motion, driven from the same morph <c>t</c> as the capsule and
        // the window so the island growing and the ball leaving it are one movement. Runs before
        // the notify-style branch because a blob morph is not a notify morph — the ball must
        // animate for both, and the early return below would otherwise skip it.
        ApplyBlobMorph(rawT);
        // 1.12.3 history panel. Same tick, same progress: the window around the panel is already
        // morphing, so the panel's slide and fade ride that t rather than a timer of their own.
        if (_historyDir != 0) ApplyHistoryPanelAnim(rawT);
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
                    var springScale = 0.92 + 0.08 * AnimEase.Ease("spring.out", rawT);
                    _pillScale.ScaleX = springScale;
                    _pillScale.ScaleY = springScale;
                    Pill.Opacity = Math.Min(1.0, 0.7 + 0.3 * auxT);
                    break;
                case NotifyAppearStyle.Pop:
                    _pillScale.ScaleX = auxT;
                    _pillScale.ScaleY = auxT;
                    Pill.Opacity = Math.Min(1.0, 0.85 + 0.15 * AnimEase.Ease("power2.out", rawT));
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
                        // 1.12.4: the amplitude's decay is the named `ragged` curve, the jitter is
                        // still Random per tick (it cannot be a function of t) — see
                        // CapsuleMorphTrack.Ragged. The peak DIP and the cross-axis share of the
                        // jitter are tokens, not bare numbers, because they are the look of the
                        // style and nothing else.
                        var amp = OverlayTokens.RaggedJitterAmpDip * AnimEase.Ease("ragged", rawT);
                        _pillTranslate.X = (_morphRng.NextDouble() * 2 - 1) * amp;
                        _pillTranslate.Y = (_morphRng.NextDouble() * 2 - 1)
                                           * amp * OverlayTokens.RaggedJitterCrossShare;
                        Pill.Opacity = 1.0 - AnimEase.Ease("power2.out", rawT) * 0.85;
                        break;
                    }
                case NotifyDismissStyle.Glitch:
                    {
                        // The stutter cell count belongs to the scenario, not to this switch — the
                        // offsets and the phase plateaus are two views of the same rhythm.
                        var stutter = _morphTrack.StutterStep(rawT) % 2 == 0;
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
            (x, y) = HistoryWindowSeatFor(x, y, homePw, homePh, winPw, winPh);
        }
        Position = new PixelPoint(x, y);
        Win32Overlay.ApplyZOrder(this, _settings.ZOrderMode);
    }

    /// <summary>
    /// Seat the enlarged window around the capsule, honouring the panel's asymmetry.
    /// <para>
    /// With the panel folded this is exactly <see cref="IslandLayout.BlobWindowFor"/> — the
    /// resting blob window is symmetric, so the capsule stays centred in it. With the panel open
    /// it is NOT: the window grew to <see cref="ClipboardHistoryPanel.BallSideExtent"/> on the
    /// ball's side and <see cref="ClipboardHistoryPanel.PanelSideExtent"/> on the panel's, so
    /// splitting the slack evenly (what BlobWindowFor does) would shift the capsule by half the
    /// difference — the island visibly jumping on a click, which is precisely what the spec
    /// forbids. The panel's own seat is used instead.
    /// </para>
    /// </summary>
    private (int X, int Y) HistoryWindowSeatFor(int x, int y, int homePw, int homePh, int winPw, int winPh)
    {
        if (!_historyOpen || _historyRowCount <= 0)
            return IslandLayout.BlobWindowFor(SplitIsVertical, x, y, homePw, homePh, winPw, winPh);

        // The panel's geometry is expressed against the CAPSULE, in DIP; only the resulting
        // window offset is converted to pixels. Measuring in pixels here would put the panel
        // half a pixel out at 150 % scaling, which is a visible seam on the ball side.
        var scale = RenderScaling <= 0 ? 1 : RenderScaling;
        var capsuleCross = (SplitIsVertical ? homePw : homePh) / scale;
        var (_, panelCross) = ClipboardHistoryPanel.SizeFor(_historyRowCount);
        var crossOrigin = ClipboardHistoryPanel.CrossOriginFor(
            _settings.Edge, capsuleCross / 2, panelCross) * scale;

        // The LONG axis keeps BlobWindowFor's answer verbatim: the capsule is Leading-anchored
        // there, so however far the window reaches past the ball, the capsule does not move.
        return SplitIsVertical
            ? ((int)Math.Round(x + crossOrigin), y)
            : (x, (int)Math.Round(y + crossOrigin));
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
        else if (kind == OverlayKind.Battery && string.IsNullOrWhiteSpace(sub))
            sub = $"{(int)Math.Round(p.Progress * 100)}%";
        // 1.13: the Timer and Progress branches are gone — neither is a capsule kind any
        // more, so neither ever reaches this point. Their text is formatted by the monitor
        // rows (ApplyTimerRow / ApplyMediaRow) and their fraction by CapsuleProgressBand.

        OverlayTitle.Text = string.IsNullOrWhiteSpace(sub) ? title : $"{title} · {sub}";
        OverlaySubtitle.Text = "";

        // 1.13: the capsule's bottom 8 DIP are one shared band. Whoever owns it draws, and
        // the other one yields — seconds digits and a progress bar are two readings of the
        // same strip, and both at once reads as a glitch rather than as two things happening.
        ApplyProgressBand();

        var textPrimary = ParseColor(_settings.ColorTextPrimary, OverlayTokens.TextHex);
        var accent = ParseColor(_settings.ColorAccent, OverlayTokens.AccentHex);
        OverlayTitle.Foreground = new SolidColorBrush(kind == OverlayKind.Error ? Color.Parse(OverlayTokens.ErrorHex) : textPrimary);
        AppIcon.Background = new SolidColorBrush(kind == OverlayKind.Error ? Color.Parse(OverlayTokens.ErrorHex) : accent);
        UnreadBadge.Background = new SolidColorBrush(accent);

        if (kind == OverlayKind.Weather)
            SetKindIconWeather(p.WeatherCode ?? snap.LastWeather.WeatherCode ?? 0);
        else
            SetKindIcon(kind);

        // 1.13: the artwork and both control clusters left the capsule. They are drawn in the
        // Плеер and Таймер monitor rows now (StatsRowView.SetStatus), so the capsule only ever
        // carries the icon, the title and the progress band.
        ApplyMediaArtwork(null);

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
    /// Click-acknowledgement pop: scale 1 → <see cref="OverlayTokens.ClickPopPeak"/> → 1 over
    /// <see cref="OverlayTokens.ClickPopMs"/>, on the shared 33 ms frame clock.
    ///
    /// Manual tick instead of <c>Avalonia.Animation.Animation</c>, for two reasons that both cost
    /// the process its life. In Avalonia 11.3 the only public <c>RunAsync</c> is
    /// <c>RunAsync(Animatable, CancellationToken)</c>: the argument is the control to animate, NOT
    /// the transform to drive. So the obvious <c>anim.RunAsync(_pillScale)</c> compiled (a
    /// ScaleTransform is an Animatable) and then threw
    /// <c>InvalidCastException: ScaleTransform → Visual</c> from inside Avalonia, on every single
    /// click. And the animation it replaced also had a <c>FillMode.Forward</c> single keyframe at
    /// cue 0.5, so even when it did run it walked 1 → peak and then left the capsule sitting at
    /// 1.08 forever.
    ///
    /// The curve is <c>clickPop</c> from <see cref="AnimEase"/> — the same one
    /// <see cref="ApplyBlobMorph"/> already uses for the ball, reached through the dictionary so
    /// the shape has exactly one implementation and cannot drift between the two call sites.
    /// </summary>
    private void PlayClickPop()
    {
        var speed = AnimationTiming.Effective(_settings.AnimationSpeed, _settings.AnimClickPop);
        if (!AnimationTiming.IsEnabled(speed)) return;

        // The morph owns _pillScale for its whole run — ApplyMorphAux writes the same transform
        // every frame, and PrepareMorphVisualStart seeds it. Two writers on one transform is a
        // visible fight: one of them loses its first or last frame, and starting a pop while a
        // morph is already in flight pays the cost of an extra tick for nothing. The morph wins
        // outright, here and again in TickClickPop.
        if (_morphActive) return;

        // Reduced motion: land on the end state, do not show a faster pop. See
        // AnimEase.WithReducedMotion — a quicker version of the same movement is exactly what a
        // user who cannot tolerate animation did not ask for.
        if (AnimReduced.Resolve(OsAnimationsEnabled(), _settings.ReducedMotion))
        {
            _clickPopActive = false;
            _clickPopWatch.Reset();
            _pillScale.ScaleX = _pillScale.ScaleY = 1.0;
            return;
        }

        _clickPopMs = AnimationTiming.ScaleMs(OverlayTokens.ClickPopMs, speed);
        _clickPopWatch.Restart();
        _clickPopActive = true;
        EnsureFrameTick();
    }

    /// <summary>Drop the pop without touching the scale — for when another writer owns it.</summary>
    private void StopClickPop()
    {
        _clickPopActive = false;
        _clickPopWatch.Reset();
    }

    /// <summary>One pop frame; false once the pop is done or has been taken over.</summary>
    private bool TickClickPop()
    {
        if (!_clickPopActive) return false;

        // A morph owns the capsule's scale for its whole run — ApplyMorphAux writes this same
        // _pillScale every frame, and PrepareMorphVisualStart seeds it. Two writers on one
        // transform is a visible fight, so the morph wins outright and the pop is dropped rather
        // than half-applied over it.
        if (_morphActive)
        {
            StopClickPop();
            return false;
        }

        var t = AnimTimeline.ProgressOf(_clickPopWatch.Elapsed.TotalMilliseconds, _clickPopMs);
        _pillScale.ScaleX = _pillScale.ScaleY = AnimEase.Ease("clickPop", t);
        if (t < 1.0) return true;

        _clickPopActive = false;
        _clickPopWatch.Reset();
        _pillScale.ScaleX = _pillScale.ScaleY = 1.0;
        return false;
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

        // 1.13: the media session is a monitor row and a band claimant, not a capsule
        // takeover. So a snapshot arrives once per second and must NOT run a full morph —
        // the previous code called ApplySize()+Paint() every tick, which with the old design
        // was the only way to move the capsule. Now the kind never changes, the panel row is
        // refilled and the band is re-arbitrated, which is a repaint and nothing more.
        if (snap is null)
        {
            _mediaFromSmtc = false;
            _machine.ClearMedia();
            ApplyMediaRow();
            ApplyProgressBand();
            return;
        }

        _mediaFromSmtc = true;
        _machine.Dispatch(OverlayCommand.SetMedia, snap.ToPayload());
        ApplyMediaRow();
        ApplyProgressBand();
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
        // 1.13: ball-preview cycle reset. A new capture always shows the freshly-copied value
        // first; the user can then wheel down to revisit older entries. Spec §«Колесо на шарике».
        _ballPreviewIndex = 0;
        ApplyBallCountBadge();
        // 1.13: rope pulse on new capture. The pulse drives the bridge stroke width from 1 to
        // 1.6 DIP and back over 200 ms via the existing 200 ms tick — no new timer. A second
        // capture within 200 ms just REPLACES the start time, the spec explicitly forbids
        // stacking. Spec §«Пульс верёвки».
        StartBridgePulse();
        // Beep-on-copy is opt-in via Notify volume slider; v1 stays silent for MultiFile.
        if (_settings.SoundEnabled
            && _settings.SoundVolNotify > 0
            && entry.Kind is ClipboardItemKind.Text or ClipboardItemKind.File)
        {
            IslandSounds.Play(IslandSoundKind.Notify, _settings);
        }
    }

    /// <summary>
    /// Stamp the pulse start time. The 200 ms tick reads <see cref="_bridgePulseStartedAtMs"/>
    /// and computes the bridge stroke-width bump; the pulse self-clears after 200 ms. A
    /// second capture within the window replaces the timestamp — the spec calls this out as
    /// "the second replaces the first, no stacking" — and that is exactly what
    /// overwriting the nullable gives us.
    /// </summary>
    private void StartBridgePulse()
    {
        _bridgePulseStartedAtMs = Environment.TickCount64;
    }

    /// <summary>
    /// Ball count badge (spec §«Бейджик со счётом»). Visible only when the history holds
    /// &gt; 1 items, mirroring the spec's "hide when count ≤ 1". Position follows the ball
    /// via the same Margin-driven layout the ball uses (recomputed every Paint), so the
    /// badge stays in the top-right corner of the ball through all drag and pin states.
    /// </summary>
    private void ApplyBallCountBadge()
    {
        var count = _clipboardHistory.Count;
        if (count <= 1)
        {
            BallCountBadge.IsVisible = false;
            return;
        }
        BallCountText.Text = count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // The badge sits 4 DIP inside the ball's top-right corner. Position is recomputed in
        // Paint() alongside the ball margin; the initial placement here is a no-op until then.
        BallCountBadge.IsVisible = true;
    }

    /// <summary>
    /// Honour the persisted pinned offset on startup. Both halves of the pin must be set,
    /// otherwise we leave the ball unpinned and let ApplyBlobRest drive it to the home spot.
    /// </summary>
    private void ApplyPinnedOffsetFromSettings()
    {
        if (!_settings.IsBlobPinned)
        {
            _blobIsPinned = false;
            return;
        }
        var (x, y) = ClipboardBlob.ClampOffset(
            _settings.ClipboardBlobPinnedOffsetX!.Value,
            _settings.ClipboardBlobPinnedOffsetY!.Value,
            OverlayTokens.BlobDragMaxPx);
        _blobDragAlong = x;
        _blobDragCross = y;
        _blobIsPinned = true;
    }

    /// <summary>
    /// Toggle the pin on release. Spec §«Закрепление шарика»: drag-then-release without
    /// modifiers pins the ball at its release position; with Ctrl held, the pin is cleared
    /// and the ball returns home. The decision is made here so a short click that does not
    /// actually drag still calls HandleBlobClick (the existing branch).
    /// </summary>
    private void ApplyBallPinOnRelease(bool ctrlHeld)
    {
        if (ctrlHeld && _blobIsPinned)
        {
            _settings.ClearBlobPin();
            _settings.Save();
            _blobDragAlong = 0;
            _blobDragCross = 0;
            _blobIsPinned = false;
            AppLog.Info("Ball pin cleared (Ctrl on release)");
        }
        else if (!_blobIsPinned)
        {
            // Save the current drag offset as the pinned position. If the user barely moved the
            // ball (drag < a few DIP), pin it at (0,0) — the home spot — which is a no-op pin.
            _settings.ClipboardBlobPinnedOffsetX = _blobDragAlong;
            _settings.ClipboardBlobPinnedOffsetY = _blobDragCross;
            _settings.Save();
            _blobIsPinned = true;
            AppLog.Info($"Ball pinned at ({_blobDragAlong:F1}, {_blobDragCross:F1})");
        }
        // The pin state changed; the halo and the rest position are both downstream of this,
        // so refresh the visual in either branch (and the no-op branch where neither side
        // changed — harmless).
        ApplyPinHalo();
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

    // 1.13: the two status rows, resolved lazily because the user may not have them in the
    // preset. Null simply means "that row is not in the panel right now".
    private StatsRowView? _mediaRowView;
    private StatsRowView? _timerRowView;

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
                // 1.13: a status row's buttons are wired once, at build time. The handler only
                // needs the index — which row it belongs to is already known by then.
                if (StatsLayout.HasActions(row))
                {
                    var kind = row;
                    view.ActionClicked += index => OnStatsRowAction(kind, index);
                }
                _statsRows.Add(view);
                _statsRowKinds.Add(row);
                SystemStatsPanel.Children.Add(view);
            }
            // Rebuilding the panel loses whatever the live sources were showing, so refill
            // them right away instead of waiting for the next tick.
            _mediaRowView = null;
            _timerRowView = null;
            ApplyMediaRow();
            ApplyTimerRow();
        }
        // Height budget always follows the resolved count, so a preset change that keeps the
        // same rows but the same count also stays correct after a settings edit.
        _machine.StatsRowCount = rows.Count;
        _machine.StatsMarquee = MarqueeHasText();
        ApplyMarquee();
    }

    /// <summary>What the monitor's running caption is currently about, by descending priority.</summary>
    private string? ResolveMarqueeText()
    {
        var snap = _machine.Snapshot();
        // Track first — a song title is the most "ambient" name and the one that benefits most
        // from a steady line beneath the rows. Clipboard preview only wins when nothing else is
        // playing, otherwise a copy would replace the now-playing name.
        if (_machine.MediaActive)
        {
            var m = _machine.MediaRow;
            if (!string.IsNullOrWhiteSpace(m.Title))
                return string.IsNullOrWhiteSpace(m.Subtitle) ? m.Title : $"{m.Title} — {m.Subtitle}";
        }
        if (_machine.TimerActive)
        {
            var t = _machine.TimerRow;
            var label = t.CountUp ? "Секундомер" : "Таймер";
            return string.IsNullOrWhiteSpace(t.Title) ? label : t.Title;
        }
        if (snap.Payload.ClipboardCycleCount > 1
            && !string.IsNullOrWhiteSpace(snap.Payload.ClipboardCyclePreview))
        {
            return snap.Payload.ClipboardCyclePreview;
        }
        return null;
    }

    private bool MarqueeHasText() => !string.IsNullOrWhiteSpace(ResolveMarqueeText());

    /// <summary>
    /// Fill the monitor's running caption. The text lives in <see cref="MarqueeText"/>; the
    /// scrolling offset comes from <see cref="MarqueeTrack.OffsetFor"/> driven by the shared
    /// 200 ms tick. The line is shown only while there is something to name, so the panel's
    /// height budget tracks it (StatsMarquee) and the marquee never reserves space for itself
    /// when idle.
    /// </summary>
    private void ApplyMarquee()
    {
        var text = ResolveMarqueeText();
        var has = !string.IsNullOrWhiteSpace(text);
        if (MarqueeHost.IsVisible != has)
            MarqueeHost.IsVisible = has;
        if (!has)
        {
            MarqueeText.Text = "";
            _marqueeShift.X = 0;
            return;
        }
        if (MarqueeText.Text != text)
            MarqueeText.Text = text;
    }

    /// <summary>Step the marquee by one frame; called from the shared 200 ms tick.</summary>
    private void TickMarquee(int deltaMs)
    {
        if (!MarqueeHost.IsVisible || MarqueeText.Text is null) return;
        _marqueeElapsedMs += deltaMs;
        var slot = MarqueeHost.Bounds.Width;
        var textWidth = MarqueeText.Bounds.Width;
        if (slot <= 0 || textWidth <= 0) return;   // not laid out yet
        _marqueeShift.X = MarqueeTrack.OffsetFor(_marqueeElapsedMs, textWidth, slot);
    }

    private static bool SameRows(IReadOnlyList<StatsRow> a, List<StatsRow> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>
    /// The panel row for a given kind, or null when the user's preset does not include it.
    /// Resolved on demand rather than cached because <see cref="SyncStatsRows"/> rebuilds the
    /// whole panel on every settings change.
    /// </summary>
    private StatsRowView? RowFor(StatsRow kind, ref StatsRowView? cache)
    {
        if (cache is not null) return cache;
        for (var i = 0; i < _statsRowKinds.Count && i < _statsRows.Count; i++)
        {
            if (_statsRowKinds[i] != kind) continue;
            cache = _statsRows[i];
            return cache;
        }
        return null;
    }

    /// <summary>Push the live SMTC session into the Плеер row (1.13).</summary>
    private void ApplyMediaRow()
    {
        var view = RowFor(StatsRow.Media, ref _mediaRowView);
        if (view is null) return;
        var active = _machine.MediaActive;
        var m = _machine.MediaRow;
        view.SetStatus(new StatusRowModel
        {
            Kind = StatusRowKind.Media,
            Label = StatsLayout.LabelFor(StatsRow.Media),
            Value = m.Title,
            Detail = m.Subtitle,
            Progress = m.Progress > 0 || active ? Math.Clamp(m.Progress, 0, 1) : null,
            Active = active,
            Playing = m.Playing
        });
        // Media changes the marquee source, so the panel's height grows and shrinks with it.
        SyncMarqueeState();
    }

    /// <summary>
    /// Route a status row's button press. The action set is fixed per row kind
    /// (see <see cref="StatsRowView.SetStatus"/>), so the index is all that is needed.
    /// </summary>
    private void OnStatsRowAction(StatsRow kind, int index)
    {
        try
        {
            switch (kind)
            {
                // Routed through the old capsule handlers on purpose: they carry the click pop
                // and the ShowNowPlaying check, and the 1.13 row buttons are the same actions
                // with a different home. Rewriting them inline would have left two copies.
                case StatsRow.Media:
                    if (index == 0) OnMediaPrev(this, null!);
                    else if (index == 1) OnMediaPlay(this, null!);
                    else if (index == 2) OnMediaNext(this, null!);
                    break;

                case StatsRow.Timer:
                    if (index == 0) OnTimerPauseResume(this, null!);
                    else if (index == 1) OnTimerPlusOne(this, null!);
                    else CancelTimer();
                    break;
            }
            ApplyMediaRow();
            ApplyTimerRow();
            Paint();
        }
        catch (Exception ex)
        {
            // A media key that the focused app refuses throws through SMTC. The panel must
            // survive it: the row is an instrument readout, not a window.
            AppLog.Warn("stats row action failed", ex);
        }
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

    // ── Island timer / stopwatch ──────────────────────────────────────────

    public void StartCountdownMinutes(int minutes)
    {
        if (!_settings.TimerEnabled) return;
        _machine.Dispatch(OverlayCommand.SetTimer, IslandTimerLogic.CountdownPayload(minutes * 60));
        // 1.13: the timer is a monitor row, not a capsule takeover, so no kind change and no
        // morph — the capsule is exactly where it was, one row of the panel now has content.
        ApplyTimerRow();
        Paint();
        _tray?.RefreshLabels();
        _winTray?.RefreshLabels();
    }

    public void StartStopwatchFromSettings()
    {
        if (!_settings.TimerEnabled) return;
        _machine.Dispatch(OverlayCommand.SetTimer, IslandTimerLogic.StopwatchPayload());
        ApplyTimerRow();
        Paint();
        _tray?.RefreshLabels();
        _winTray?.RefreshLabels();
    }

    public void CancelTimer()
    {
        if (!_machine.TimerActive) return;
        _machine.ClearTimer();
        ApplyTimerRow();
        Paint();
        _tray?.RefreshLabels();
        _winTray?.RefreshLabels();
    }

    public bool IsTimerActive => _machine.TimerActive;

    private void OnTimerPauseResume(object? sender, RoutedEventArgs e)
    {
        if (!_machine.TimerActive) return;
        var next = _machine.TimerRow;
        next.Playing = !next.Playing;
        _machine.Dispatch(OverlayCommand.SetTimer, next);
        ApplyTimerRow();
        Paint();
    }

    private void OnTimerPlusOne(object? sender, RoutedEventArgs e)
    {
        if (!_machine.TimerActive) return;
        var next = _machine.TimerRow;
        if (next.CountUp) return;
        next.RemainingSeconds = Math.Min(359999, next.RemainingSeconds + 60);
        _machine.Dispatch(OverlayCommand.SetTimer, next);
        ApplyTimerRow();
        Paint();
    }

    private void OnTimerCancel(object? sender, RoutedEventArgs e) => CancelTimer();

    /// <summary>
    /// Push the live timer into its monitor row. The row keeps its own text so the capsule
    /// only has to arbitrate the band — the digits are the row's business (1.13).
    /// </summary>
    private void ApplyTimerRow()
    {
        var active = _machine.TimerActive;
        var t = _machine.TimerRow;
        _timerRowView?.SetStatus(new StatusRowModel
        {
            Kind = StatusRowKind.Timer,
            Label = StatsLayout.LabelFor(StatsRow.Timer),
            Value = IslandTimerLogic.FormatRemaining(t.RemainingSeconds),
            Detail = t.CountUp
                ? (t.Playing ? "Секундомер" : "Пауза")
                : (t.Playing ? t.Title : "Пауза"),
            Progress = !t.CountUp && _machine.TimerTotalSeconds > 0
                ? Math.Clamp(t.RemainingSeconds / _machine.TimerTotalSeconds, 0, 1)
                : null,
            Active = active,
            Playing = t.Playing
        });
        SyncMarqueeState();
    }

    /// <summary>
    /// Single point that decides whether the running caption has text and asks the machine to
    /// grow or shrink the panel accordingly. Called from every source change so the height
    /// budget never gets out of sync with what the panel is actually showing.
    /// </summary>
    private void SyncMarqueeState()
    {
        var has = MarqueeHasText();
        if (_machine.StatsMarquee != has)
        {
            _machine.StatsMarquee = has;
            // Height changed → ApplySize + Paint. We are already inside an ApplySize path on
            // most callers; calling it again is cheap and keeps the contract local.
            ApplySize();
        }
        ApplyMarquee();
    }

    /// <summary>
    /// Arbitrate the capsule's bottom 8 DIP between the progress band and the seconds strip,
    /// and give the band its owner-specific accent.
    /// <para>
    /// The charge pill is deliberately NOT a claimant: the user kept battery as a real
    /// takeover, so a charge bar is drawn by the pill itself, never by this strip.
    /// </para>
    /// <para>
    /// Called from Paint and from every source that can start or stop a claimant (SMTC tick,
    /// timer start/cancel), because the seconds strip has its own visibility rule that must
    /// yield immediately rather than on the next seconds refresh.
    /// </para>
    /// </summary>
    private void ApplyProgressBand()
    {
        var state = _machine.BandState();
        var owner = CapsuleProgressBand.OwnerOf(state);
        var bandOn = owner != ProgressBandOwner.None
            && _settings.IslandVisible && !_hiddenByFullscreen;

        OverlayProgress.IsVisible = bandOn;
        if (bandOn)
        {
            OverlayProgress.Value = CapsuleProgressBand.FractionFor(state) * 100;
            // One accent for all three claimants. They are not competing for the strip and
            // they never overlap, so a per-owner colour would only add a second thing to keep
            // in sync with the theme for no gain.
            OverlayProgress.Foreground =
                new SolidColorBrush(ParseColor(_settings.ColorAccent, OverlayTokens.AccentHex));
            ToolTip.SetTip(OverlayProgress, CapsuleProgressBand.LabelFor(state));
        }

        // The strip yields to the band, and only there — it is a pure repaint, no morph.
        if (bandOn)
            SecondsStrip.IsVisible = false;
    }

    /// <summary>
    /// FontAudio digital-dot seconds strip along bottom of Idle/Collapsed (incl. hover/pin peek).
    /// Yields to the progress band, which owns the same 8 DIP (1.13).
    /// </summary>
    private void UpdateSecondsStrip()
    {
        try
        {
            // The band wins the shared strip outright. Checked here as well as in
            // ApplyProgressBand because this runs on its own timer and would otherwise
            // bring the strip back a frame after the band took it.
            if (CapsuleProgressBand.BandActive(_machine.BandState()))
            {
                SecondsStrip.IsVisible = false;
                return;
            }

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
        if (!_hoverPin.IsPinned && _pointerOverUi && _settings.HoverExpandEnabled)
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

