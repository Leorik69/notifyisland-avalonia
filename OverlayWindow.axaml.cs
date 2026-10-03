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
    private WindowsNotificationSource? _notifSource;
    private WindowsKeyboardLayoutSource? _keyboardLayout;
    private CancellationTokenSource? _keyboardLayoutHide;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _weatherTimer = new() { Interval = TimeSpan.FromMilliseconds(OverlayTokens.WeatherRefreshMs) };
    private CancellationTokenSource? _weatherCts;

    private Point _pressOrigin;
    private bool _pressing;
    private bool _weatherIconFlip;
    private string _lastWeatherIconKey = "";
    // 2026-10-02 perf pass: the collapsed-state readings that Paint re-derived on every 200 ms
    // tick. Each one only moves when its own source does, so each gets its own last-seen value
    // and the tick stops rebuilding identical controls, strings and tooltips.
    private string _lastWxTempText = "";
    private string _lastWxTip = "";
    private string _lastBatTip = "";
    private string _lastBatIconKey = "";
    private string _lastBatIconPack = "";
    private double _lastBatIconSize;
    private Color _lastBatIconInk;

    // A SolidColorBrush is immutable once constructed, so handing Avalonia the SAME instance for
    // the same colour is indistinguishable from a fresh one and costs nothing. Paint runs five
    // times a second and used to allocate five of them per call to express four colours, plus a
    // fresh Color.Parse of the same error hex three times. The cache is capped: a user who keeps
    // editing the accent in Settings should not be able to grow it without bound.
    private readonly List<(Color Color, SolidColorBrush Brush)> _brushCache = new();

    private SolidColorBrush CachedBrush(Color color)
    {
        for (var i = 0; i < _brushCache.Count; i++)
        {
            if (_brushCache[i].Color.Equals(color))
                return _brushCache[i].Brush;
        }

        if (_brushCache.Count >= 16)
            _brushCache.Clear();

        var brush = new SolidColorBrush(color);
        _brushCache.Add((color, brush));
        return brush;
    }
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
    // 1.13 (implementing the 1.12.0 token): first-appear wobble. A non-null start means a wobble
    // is in flight; it rides the shared 16 ms frame tick via TickFirstAppearWobble.
    private long? _firstAppearWobbleStartedAtMs;
    private int _firstAppearWobbleMs;
    private bool _hoverWired;
    private readonly HoverPinMachine _hoverPin = new();
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromMilliseconds(OverlayTokens.FullscreenPollMs) };
    private bool _hiddenByFullscreen;
    private bool _clickThroughActive;
    /// <summary>Staggered reveal in flight, if any — see <see cref="RevealRun"/>.</summary>
    private RevealRun? _reveal;
    /// <summary>One-shot accent flash, if in flight. Rides the shared frame clock.</summary>
    private long? _accentFlashStartedAtMs;
    private int _accentFlashMs;
    private Color _accentFlashColor = Colors.Transparent;
    /// <summary>The pill's own border brush, captured so the flash can put it back EXACTLY.</summary>
    private IBrush? _pillBorderBeforeFlash;
    /// <summary>
    /// The three hairlines of the theme, resolved once per palette: idle, hover, pinned.
    /// <para>
    /// They used to be the constants #16FFFFFF / #38FFFFFF / #60FFFFFF. A white hairline is only
    /// correct on a dark capsule: on a white Custom wall it disappeared completely, and on Ocean it
    /// competed with the accent it was supposed to sit under. Taking the alpha from the primary
    /// text instead makes one rule right on every surface — the border can never be brighter than
    /// the ink it frames, and it inverts with the theme instead of fighting it.
    /// </para>
    /// </summary>
    private IBrush _borderIdle = Avalonia.Media.Brushes.Transparent;
    private IBrush _borderHover = Avalonia.Media.Brushes.Transparent;
    private IBrush _borderPinned = Avalonia.Media.Brushes.Transparent;
    /// <summary>Hairline of the clipboard section's seam, the same ink at the idle weight.</summary>
    private IBrush _borderSeam = Avalonia.Media.Brushes.Transparent;
    /// <summary>Per-theme surface weight — see <see cref="ThemePresets.SurfaceMaterial"/>.</summary>
    private ThemePresets.SurfaceMaterial _material;
    /// <summary>
    /// The palette's ink AFTER the readability guard. Icons read this instead of
    /// <see cref="Colors.White"/>: a white glyph is invisible on a light Custom capsule, which is
    /// the same failure as a white hairline, and the spec asks for both to survive every theme
    /// (spec §10). One field, so the badge, the kind glyph and the weather glyph can never drift
    /// apart from the text they sit next to.
    /// </summary>
    private Color _inkPrimary = Colors.White;
    private Color _inkSecondary = Color.Parse(OverlayTokens.TextSecondaryHex);
    /// <summary>Hex form of <see cref="_inkSecondary"/>, for the Core-side guards that speak hex.</summary>
    private string _inkSecondaryHex = OverlayTokens.TextSecondaryHex;

    /// <summary>
    /// Tint for a clipboard format icon. The per-format colours stay as long as they are readable
    /// on the current surface; on a light Custom wall they would vanish (spec §10) and the icon
    /// drops to the capsule's own secondary ink. The format is still legible from the shape.
    /// </summary>
    private Color ClipboardIconTint(ClipboardItemKind kind) =>
        ParseColor(ThemePresets.GuardedIconInk(
            _settings.ColorCapsuleFill, ClipboardHalfPreview.IconTintHexFor(kind), _inkSecondaryHex),
            OverlayTokens.TextHex);
    /// <summary>
    /// The pill's transitions, captured for the same reason. The pill has a hover
    /// <c>BrushTransition</c> on the very property the flash animates, and a transition that is
    /// still live turns a per-frame flash into a smear.
    /// </summary>
    private Transitions? _pillTransitionsBeforeFlash;
    /// <summary>
    /// The frame clock's own elapsed milliseconds. One monotonic stopwatch for the whole window
    /// rather than one per animation: several short animations can overlap (a caption revealing
    /// while a flash runs), and a per-animation stopwatch restarts when its own animation starts,
    /// which would make an overlap silently rewind the older one.
    /// </summary>
    private readonly Stopwatch _frameClock = new();
    private long _frameClockMs;
    /// <summary>
    /// The kind the capsule was showing when the last arrival was staged. <see cref="_lastKind"/>
    /// cannot serve here: it is updated in the chrome pass BEFORE Paint runs, so by the time
    /// Paint asks "did something just arrive?" the answer has already been overwritten.
    /// </summary>
    private OverlayKind _lastArrivalKind = OverlayKind.Idle;
    /// <summary>Whether the SystemStats panel is currently on screen — gates its reveal.</summary>
    private bool _statsWasVisible;
    /// <summary>Whether an SMTC session was on screen at the last <c>ApplyMediaRow</c>.</summary>
    private bool _mediaWasActive;
    /// <summary>Whether a timer was running at the last <c>ApplyTimerRow</c>.</summary>
    private bool _timerWasActive;
    /// <summary>Whether the "время вышло" accent flash has already fired for this completion.</summary>
    private bool _timerSignalledDone;
    /// <summary>
    /// Intensity for the arrival currently being dispatched, when the kind alone does not say.
    /// Set by the site that KNOWS (the low-battery alert) and consumed by
    /// <see cref="StageArrival"/>; null means "use the kind's default".
    /// </summary>
    private ReactionLevel? _arrivalLevelOverride;
    private bool _pointerOverUi;                                  // aggregate over Pill, ClipboardSection, HistoryPanel
    private int _uiHoverCount;                                    // refcount so an enter/exit pair across two controls cancels cleanly
    private DateTime _lastPillClickUtc = DateTime.MinValue;
    private bool _peekSecondsActive;
    private SystemSnapshot? _lastStats;
    // 1.12.2: the split half's preview cap lives in ClipboardHalfPreview.TextMaxChars —
    // truncating text is a tested rule now, not a constant hiding in the view.

    // 1.14: the clipboard SECTION (spec
    // docs/superpowers/specs/2026-09-30--notifyisland-clipboard-section.md). The ball, its rope,
    // its drag, its peek, its count badge and its pin are all gone; the clipboard is a
    // compartment of the capsule now, so none of the per-frame state they needed survives.
    //
    // The section's growth rides the ordinary morph: _sectionDir is 0 unless THIS morph is
    // carrying an attach (+1) or a retract (-1), and the amount the capsule is running out by is
    // _sectionPeek — kept as a field, exactly like the old peek was, because the window must be
    // measured WITHOUT it (see PlaceIsland) and the island's click zones must not slide.
    private bool _splitApplied;
    /// <summary>0 = no section transition in flight, +1 = running out, -1 = retracting.</summary>
    private int _sectionDir;
    /// <summary>Extra long-axis length the capsule currently shows for the section (DIP).</summary>
    private double _sectionPeek;
    /// <summary>The capsule's own long-axis length captured when a section transition starts —
    /// the base the section grows FROM. Read, never written by the morph, so the section cannot
    /// feed back into the length the island and the window are sized from.</summary>
    private double _sectionBase;

    // -- 1.14 clipboard history drawer ---------------------------------------------
    // Opened by a click on the clipboard section, closed by a second such click, Escape, a
    // click on the drawer's own empty padding, or picking a row. The drawer rides the EXISTING
    // 16 ms morph tick (see ApplyDrawerAnim) — opening it changes the window size, so there is
    // already a morph running and the drawer has a progress value to read for free. A second
    // timer would be a second source of "when is this animation over".
    private bool _historyOpen;
    /// <summary>Row hosts currently in the drawer, kept so the hover highlight can be moved and
    /// a close can reset exactly what it showed. Typed as Border (not Control) because the
    /// hover IS a Background change, and going through Control would need a cast at every call
    /// site. Avalonia.Controls.Border spelled out: this file also sees System.Windows.Forms.</summary>
    private readonly List<Avalonia.Controls.Border> _historyRowControls = new();
    /// <summary>1.18: row hover / press fills, derived from the user's ColorAccent.</summary>
    private IBrush? _historyHoverBrush;
    private IBrush? _historyPressBrush;
    /// <summary>Row currently held down, so its fill survives a hover-leave mid-press.</summary>
    private int _historyPress = -1;
    /// <summary>Row count the drawer is currently laid out for — the window's size input.</summary>
    private int _historyRowCount;
    /// <summary>Drawer's slide offset in DIP, written by the morph tick.</summary>
    private readonly TranslateTransform _historySlide = new();
    /// <summary>+1 while the drawer is opening, -1 while it is closing, 0 at rest.</summary>
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

    // 1.13: clipboard preview cycle (wheel over the clipboard section). The wheel changes
    // WHICH entry the section previews; it never opens the drawer. Kept in Core
    // (BallPreviewCycle) so the step/resolve rules stay testable without a window.
    /// <summary>
    /// Index into the newest-first snapshot, the section preview is showing. <c>0</c> means
    /// "newest = the entry that just got captured"; non-zero means the user wheeled DOWN and is
    /// browsing older items. Reset to 0 on every new capture (handled in
    /// <see cref="OnClipboardCaptured"/>) so the freshly-copied value always pops up first.
    /// </summary>
    private int _ballPreviewIndex;
    /// <summary>Where a press on the clipboard section started, for the click-vs-drag test.</summary>
    private Point _sectionPressOrigin;
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
        // Same reason, same wiring: the notification body scrolls on a transform of its own, and
        // the bell ring's scale is declared in code for the identical reason (the source
        // generator does not register a field for an x:Name on a transform child).
        OverlaySubtitle.RenderTransform = _bodyMarqueeShift;
        NotifyBellRing.RenderTransform = _bellRingScale;
        OverlayTextColumn.SizeChanged += (_, _) => ClampTitleToColumn();
        _settings = AppSettings.Load();
        // If the settings file was unreadable, Load moved it aside and fell back to defaults. Say
        // so in the app's own log: without it, a user whose settings reset has no way to tell a
        // corrupt file from a preference they never made, and no way to find the kept copy.
        if (AppSettings.LastLoadError is { } loadError)
            AppLog.Warn($"Settings could not be read ({loadError}) — defaults are in use and the " +
                        "old file was kept next to settings.json as settings.corrupt-<timestamp>.json");
        _settings.Normalize();
        _machine.WeatherEnabled = _settings.WeatherEnabled;
        _machine.CollapsedWidthScale = _settings.IslandWidthScale;
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
        // 1.14: settle the clipboard section into its detached resting frame. This has to run
        // AFTER _settings exists — ApplySectionRest reaches ApplySeamRadii, which asks
        // SplitIsVertical, and that reads _settings.Orientation. Calling it above the settings
        // load crashed the app on every start with a NullReferenceException, so the section's
        // initial state has to be written here instead of at the top of the constructor.
        ApplySectionRest(attached: false);
        _statsMachine = new SystemMonitorMachine(
            new WindowsSystemMonitorSource(
                TimeSpan.FromMilliseconds(_settings.SystemStatsRefreshMs)))
        {
        };
        _statsMachine.OnSnapshot += OnStatsSnapshot;
        _statsMachine.SetIncludeAllInterfaces(_settings.SystemStatsAllInterfaces);

        EnableMorphTransitions();
        WirePointerClicks();
        WireClipboardSection();
        WireHistoryPanel();
        ConfigureHoverPinFromSettings();
        SeedIcons();
        ApplyWeatherSide();
        ApplyPalette();
        ApplyOpacity();
        ApplyIslandVisibility();
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
            _notifSource?.Dispose();
            _notifSource = null;
            _keyboardLayout?.Dispose();
            _keyboardLayout = null;
            _keyboardLayoutHide?.Cancel();
            _keyboardLayoutHide = null;
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
            SyncNotifyJumpToTop(after);
            if (_machine.TimerActive != timerBefore || SystemStatsPanel.IsVisible)
                ApplyTimerRow();
            TickMarquee(200);
            TickBodyMarquee(200);
            Paint();
            UpdateSecondsStrip();
            if (before != after || hoverChanged || splitBefore != splitAfter) ApplySize();
            var unread = _machine.UnreadCount;
            if (unread != _lastTrayUnread)
            {
                _lastTrayUnread = unread;
                _tray?.RefreshIcon(unread);
                _winTray?.RefreshIcon(unread);
                UpdateTrayTooltip();
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
        // The notification listener is the island's actual subject, so it has no setting to
        // gate on and no window handle to wait for. Started from here, like its neighbours.
        EnsureNotificationSource();
        // The language badge, same reason: it needs no window handle and no setting, and Opened
        // is not a reliable place to start anything (it does not run on first launch at all).
        EnsureKeyboardLayoutSource();



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
        // 1.15: the date sits one step below the clock in the type scale, not just in colour —
        // a 1 DIP step at 12 looked like a peer of the time rather than its caption, and a
        // long localised date ("ср, 30 сентября") had to fight the clock for horizontal room.
        DateText.FontSize = Math.Max(10, fs - 2);
        DateText.FontFamily = family;
        WeatherTempText.FontSize = fs;
        WeatherTempText.FontFamily = family;
        OverlayTitle.FontSize = fs;
        OverlayTitle.FontFamily = family;
        // The body follows the same family and sits one step below the title (11 at the default
        // 12, as before). It used to stay Segoe 11 whatever the user picked, so at 18 DIP the row
        // read as two unrelated fonts glued together rather than as a title and its detail.
        OverlaySubtitle.FontSize = Math.Max(10, fs - 1);
        OverlaySubtitle.FontFamily = family;
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
        // and the clipboard SECTION inside it), and the history drawer (which sits
        // OUTSIDE the pill, stacked on the cross axis). A refcount keeps a fast move
        // from one control to another — say, capsule → drawer — from briefly
        // registering as "leave all" and starting the 5-second grace prematurely.
        WireUiHover(Pill);
        WireUiHover(ClipboardSection);
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
            UpdateNotificationChrome();
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
            UpdateNotificationChrome();
        }
    }

    private void PillEnterVisuals()
    {
        // Hover raises the hairline by using the theme's hover weight, not by making a white
        // border whiter: the edge is already the theme's ink, and a hover must not be the moment
        // the border outshines the text (spec §7).
        Pill.BorderBrush = _hoverPin.IsPinned ? _borderPinned : _borderHover;
        Pill.Background = new SolidColorBrush(WithAlpha(_pillFill, Math.Min(1.0, _idleFillA + 0.06)));
        IslandSounds.Play(IslandSoundKind.Hover, _settings);
    }

    private void PillLeaveVisuals()
    {
        if (!_hoverPin.IsPinned)
        {
            Pill.BorderBrush = _borderIdle;
            ApplyOpacity();
        }
    }

    /// <summary>
    /// Stage 8: the notification's hover action lives in the row's layout, not in a separate
    /// window, so showing or hiding it is a paint concern — including the text width budget,
    /// which is computed together with the action's presence. Everything else ignores the call.
    /// </summary>
    private void UpdateNotificationChrome()
    {
        var kind = _machine.Snapshot().Kind;
        if (kind is not (OverlayKind.Notification or OverlayKind.Expanded or OverlayKind.Error)) return;
        Paint();
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
        if (!_frameClock.IsRunning) _frameClock.Start();
        _frameClockMs = _frameClock.ElapsedMilliseconds;
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
        _frameClockMs = _frameClock.ElapsedMilliseconds;
        var busy = TickUnreadPulse();
        if (TickClickPop()) busy = true;
        if (TickFirstAppearWobble()) busy = true;
        if (TickReveal()) busy = true;
        if (TickAccentFlash()) busy = true;
        if (TickBellPulse()) busy = true;
        if (busy) return;
        _frameTimer.Stop();
        _frameTimer.Tick -= OnFrameTick;
        _frameActive = false;
    }

    /// <summary>
    /// One frame of the staged reveal in flight, if any. Rides the shared clock like every other
    /// short animation, so a caption arriving costs no timer of its own and the window is back to
    /// zero timers the moment the last element lands.
    /// </summary>
    private bool TickReveal()
    {
        if (_reveal is not { } run) return false;
        if (run.Tick()) return true;
        _reveal = null;
        return false;
    }

    /// <summary>
    /// A single short accent tint along the capsule's own edge — the reaction to something the
    /// user was waiting for (spec Этап 5, §1, "сильные реакции").
    /// <para>
    /// It is ONE flash with a start and an end, not a lamp: the whole point of the tier is that
    /// an important event is briefly conspicuous and then the island goes quiet again, so a
    /// longer or repeating version of this is a different (and wrong) design.
    /// </para>
    /// <para>
    /// It is drawn on the pill's own <c>BorderBrush</c> rather than on an overlay shape. An
    /// overlay would have to re-derive the capsule's corner radius on every morph frame to avoid
    /// spilling past the rounded edge, and a hairline that sometimes overflows is worse than no
    /// flash at all. The border already has the exact geometry, the exact radius and the exact
    /// clipping, so raising its alpha is the accent that is guaranteed to stay inside the shape.
    /// The brush is captured first and restored verbatim, so a user's palette can never be
    /// repainted by a reaction.
    /// </para>
    /// </summary>
    private void PlayAccentFlash(string hex)
    {
        var speed = AnimationTiming.Effective(_settings.AnimationSpeed, _settings.AnimMorphInflate);
        var reduced = AnimReduced.Resolve(OsAnimationsEnabled(), _settings.ReducedMotion);
        if (reduced || !AnimationTiming.IsEnabled(speed))
        {
            // Reduced motion removes the effect, it does not shorten it: the arrival is still
            // announced by the size change and the content, which is the information the flash
            // was only emphasising.
            //
            // Nothing is cleared here on purpose. If animations are switched off MID-flash, a
            // flash is already in flight: it owns a captured brush and a lifted hover transition,
            // and the tick is the only thing that puts them back. Cancelling the marker here
            // without restoring them would strand the accent colour on the pill and leave hover
            // dead until the next restart — dropping the new request is the whole effect.
            return;
        }

        _pillBorderBeforeFlash ??= Pill.BorderBrush;
        // The pill carries a hover BrushTransition on exactly this property (see
        // ApplyAnimationSettings). Writing the flash 30 times a second against a 160 ms
        // transition means the brush never reaches any of the frames it is given: the result
        // is a slow smear that lags the flash by its whole duration and then unwinds after
        // it. The transition is lifted for the duration of the flash and put back verbatim
        // when it lands, so hover still eases the moment the island is quiet again.
        _pillTransitionsBeforeFlash ??= Pill.Transitions;
        Pill.Transitions = null;

        _accentFlashColor = ParseColor(hex, OverlayTokens.AccentHex);
        _accentFlashMs = Reaction.AccentMs(speed);
        // Start the clock BEFORE reading time, and read the stopwatch itself rather than the
        // per-frame cache. _frameClockMs is only refreshed inside OnFrameTick, while the capsule
        // morph runs on its own timer — so on an island that has been quiet for a second (the
        // common case: no pulse, no pop) the cached stamp is a second old, the first tick of the
        // flash already reads p = 1, and the flash is never drawn at all.
        EnsureFrameTick();
        _accentFlashStartedAtMs = _frameClock.ElapsedMilliseconds;
    }

    /// <summary>One frame of the accent flash; false when it has finished or none is in flight.</summary>
    private bool TickAccentFlash()
    {
        if (_accentFlashStartedAtMs is not { } started) return false;
        var p = Math.Clamp((_frameClockMs - started) / (double)Math.Max(1, _accentFlashMs), 0.0, 1.0);
        // Up fast, down slower: the eye catches the onset, and the tail is what makes it read as
        // a flash rather than a flicker.
        var a = p < 0.25
            ? Reaction.AccentPeak * (p / 0.25)
            : Reaction.AccentPeak * (1.0 - (p - 0.25) / 0.75);
        Pill.BorderBrush = new SolidColorBrush(WithAlpha(_accentFlashColor, a));
        if (p < 1.0) return true;
        Pill.BorderBrush = _pillBorderBeforeFlash ?? Pill.BorderBrush;
        Pill.Transitions = _pillTransitionsBeforeFlash;
        _pillTransitionsBeforeFlash = null;
        _pillBorderBeforeFlash = null;
        _accentFlashStartedAtMs = null;
        return false;
    }

    /// <summary>
    /// Stage a reveal for the given elements, in the given order, at the given intensity.
    /// <para>
    /// Replaces whatever was in flight rather than queuing behind it: a second notification
    /// arriving mid-reveal must be able to take the surface, and letting the old run finish would
    /// leave it writing opacity over content that has already changed.
    /// </para>
    /// </summary>
    private void PlayReveal(ReactionLevel level, params (Avalonia.Controls.Control Target, int Order)[] ordered)
    {
        if (ordered.Length == 0) return;

        // Land whatever was in flight BEFORE reading any opacity. The resting value of each
        // element is read from its current opacity, so a run that was cut off mid-fade would
        // otherwise have its half-faded value (0.3, 0.6) adopted as the NEW resting value — and
        // the element would stay permanently dimmed, growing dimmer on every interrupted
        // notification. Finishing first restores the previous run's resting values, which are
        // the design ones.
        _reveal?.Finish();
        _reveal = null;

        var speed = AnimationTiming.Effective(_settings.AnimationSpeed, _settings.AnimMorphInflate);
        var reduced = AnimReduced.Resolve(OsAnimationsEnabled(), _settings.ReducedMotion);
        var rise = Reaction.RiseDip(level);
        var items = ordered
            .Where(o => o.Target is not null)
            .Select((o, i) => new RevealItem(o.Target, o.Order, o.Target.Opacity) { RiseDip = rise })
            .ToList();

        if (reduced || !AnimationTiming.IsEnabled(speed)) return;

        // Same reasoning as the accent flash: the base stamp comes from the running stopwatch,
        // not from the per-frame cache, or a reveal started on a quiet island is already over on
        // its first tick. The clock is started first so the reading is real.
        EnsureFrameTick();
        _reveal = new RevealRun(
            items,
            Reaction.RevealMs(level, speed),
            Reaction.StaggerShare(level),
            () => _frameClock.ElapsedMilliseconds);
        _reveal.Start();
        // Paint the first frame synchronously: without it the freshly dimmed elements would show
        // their resting opacity for one 33 ms frame before the run takes them.
        if (!_reveal.Tick()) _reveal = null;
    }

    /// <summary>
    /// One pulse frame; false once the pulse is off or has been disabled in settings.
    /// <para>
    /// 1.12.4: the timer and the media surfaces have no animation of their own to migrate — they
    /// enter and leave through the same morph as every other kind, and the sine below is the only
    /// endless motion left in the capsule (the ball's breathe went with the ball in 1.14). So
    /// the reduced-motion promise for this file is carried by this one guard.
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
        // The theme scales the surface, not the slider: _settings.Opacity keeps its documented
        // 0.35–1.0 meaning and stays monotonic, AppleQuiet just renders its wall a little lighter
        // so the theme reads as a translucent object. Only the FILL is scaled — ink, hairline and
        // the unread dot are painted on top at full strength, which is what keeps text and the
        // capsule edge readable at the bottom of the opacity range (spec §8).
        var scaled = _settings.Opacity * _material.FillAlphaScale;
        _idleFillA = Math.Clamp(scaled <= 0 ? 1.0 : scaled, 0.30, 1.0);
        Pill.Background = new SolidColorBrush(WithAlpha(_pillFill, _idleFillA));
        // 1.18: the drawer is the SAME surface, not a second card next to the capsule. Its
        // fill is written from the same parsed ColorCapsuleFill and the same alpha as the
        // pill, so a user palette change reaches both at once and the seam can never show a
        // tone difference between the two shapes.
        HistoryPanel.Background = new SolidColorBrush(WithAlpha(_pillFill, _idleFillA));
    }

    /// <summary>Apply user palette (capsule / accent / text) live from settings.</summary>
    private void ApplyPalette()
    {
        _pillFill = ParseColor(_settings.ColorCapsuleFill, OverlayTokens.FillHex);
        // Custom can hand us ink the same colour as its own wall. One guard, applied to both
        // texts, keeps the capsule readable without touching the user's colours in any other way.
        var primaryHex = ThemePresets.GuardedPrimaryInk(_settings.ColorCapsuleFill, _settings.ColorTextPrimary);
        var secondaryHex = ThemePresets.GuardedSecondaryInk(
            _settings.ColorCapsuleFill, _settings.ColorTextSecondary, primaryHex);
        var text = ParseColor(primaryHex, OverlayTokens.TextHex);
        var textSec = ParseColor(secondaryHex, OverlayTokens.TextSecondaryHex);
        var accent = ParseColor(_settings.ColorAccent, OverlayTokens.AccentHex);
        _inkPrimary = text;
        _inkSecondary = textSec;
        _inkSecondaryHex = secondaryHex;

        _material = ThemePresets.MaterialFor(_settings.ThemePreset);
        _borderIdle = new SolidColorBrush(WithAlpha(text, _material.BorderAlpha));
        _borderHover = new SolidColorBrush(WithAlpha(text, _material.BorderHoverAlpha));
        _borderPinned = new SolidColorBrush(WithAlpha(text, _material.BorderPinnedAlpha));
        _borderSeam = new SolidColorBrush(WithAlpha(text, _material.BorderAlpha * 0.85));
        Pill.BorderBrush = _borderIdle;
        HistoryPanel.BorderBrush = _borderIdle;
        ClipboardSectionSeam.Background = _borderSeam;

        _clockBrush = new SolidColorBrush(text);
        ClockText.Foreground = _clockBrush;
        DateText.Foreground = new SolidColorBrush(textSec);
        WeatherTempText.Foreground = new SolidColorBrush(textSec);
        // B: the chevrons were a hard-coded light blue — a third accent next to the user's own
        // primary (clock) and secondary (date, weather) inks. They are navigation, not content,
        // so they take the secondary ink, a step quieter than the date.
        var chevronInk = new SolidColorBrush(WithAlpha(textSec, OverlayTokens.ChevronInkAlpha));
        CyclePrevButton.Foreground = chevronInk;
        CycleNextButton.Foreground = chevronInk;
        OverlayTitle.Foreground = new SolidColorBrush(text);
        // The badge sits ON the accent, so its ink is chosen against the accent rather than the
        // capsule: a pale custom accent would otherwise leave a white number on a white chip.
        BadgeText.Foreground = new SolidColorBrush(ParseColor(
            ThemePresets.GuardedPrimaryInk(_settings.ColorAccent, OverlayTokens.TextHex),
            OverlayTokens.TextHex));
        UnreadDot.Background = new SolidColorBrush(accent);
        // 1.15: no permanent BoxShadow. A steady halo around a 6 DIP dot read as an alert lamp
        // rather than an unread mark; the pulse already carries the attention. Keep the dot
        // itself the only ink.
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
        // FIX: the rebuild below used to re-add only these six controls, so `Children.Clear()`
        // silently dropped the four clipboard-cycle controls (CyclePrevButton, CyclePreviewText,
        // CycleNavHint, CycleNextButton) and PinnedBadge out of the visual tree. They kept their
        // x:Name fields, so every write to them still "worked" and every test still passed —
        // but an element with no Parent is never measured or arranged, which is why the cycle
        // preview reported Bounds.Width = 0 and DesiredSize.Width = 0 with IsVisible = true and
        // a 41-character Text, while a freshly constructed TextBlock with identical properties
        // measured 70 DIP. A control that is not in the tree cannot be laid out, and no amount
        // of property writing brings it back.
        //
        // B: the chevrons bracket the clock — "‹ 17:31 › date weather". The previous rebuild put
        // the clock first and then the whole cycle block, so with the text clock the row read
        // "17:31 ‹ › date", and with the digital clock (which lived in the tail) it read
        // "‹ › 17:31": two arrows glued together on one side, pointing at nothing. Whichever
        // clock is showing now sits between them, and the clipboard counter (1/3) joins it
        // inside the brackets because the chevrons are what step through it.
        var pinned = PinnedBadge;
        row.Children.Clear();
        void Add(params Avalonia.Controls.Control[] items)
        {
            foreach (var c in items) row.Children.Add(c);
        }
        var weatherLeft = _settings.WeatherSide == WeatherSide.Left;
        if (weatherLeft) Add(weather);
        Add(CyclePrevButton, clockText, digital, CyclePreviewText, CycleNavHint, CycleNextButton, dateText);
        if (!weatherLeft) Add(weather);
        Add(battery, dot, pinned, KeyboardLayoutBadge);
    }

    private void ApplyOrientationLayout()
    {
        var vertical = IslandLayout.IsVertical(_settings.Orientation, _settings.Edge);
        CollapsedRow.Orientation = vertical ? Avalonia.Layout.Orientation.Vertical : Avalonia.Layout.Orientation.Horizontal;
        MinimalWeather.Orientation = vertical ? Avalonia.Layout.Orientation.Vertical : Avalonia.Layout.Orientation.Horizontal;
        // B: the chevrons keep their 6 DIP side padding as hit area, but the padding is
        // invisible, so on a horizontal row it read as an extra 6 DIP of capsule margin on the
        // leading edge and a 12 DIP hole between the arrow and the clock (vs 6 everywhere else).
        // Negative margins give the visible glyphs the same rhythm as the rest of the row
        // without shrinking what the pointer can hit. Vertical rows stack, so the side padding
        // never shows there and the margins are reset.
        CyclePrevButton.Margin = vertical ? new Thickness(0) : new Thickness(-6, 0, -3, 0);
        CycleNextButton.Margin = vertical ? new Thickness(0) : new Thickness(-3, 0, -6, 0);
        ApplyDrawerAnchor(vertical);
    }

    // -- 1.14 clipboard section + drawer: anchor, hit test ---------------------------

    /// <summary>
    /// True when the capsule's long axis is its height (Left/Right edges). The section's anchor,
    /// the drawer's slide axis and the window growth all key off this one flag: the long axis is
    /// X on Top/Bottom and Y on Left/Right.
    /// </summary>
    private bool SplitIsVertical => IslandLayout.IsVertical(_settings.Orientation, _settings.Edge);

    /// <summary>Which way the drawer grows, as seen from the capsule (1 = down / right).</summary>
    private int DrawerCrossDirection => ClipboardDrawer.CrossDirectionFor(_settings.Edge);

    /// <summary>
    /// Pin the capsule inside the window: Leading on the long axis (the window is never longer
    /// than the capsule there, so this is just "flush with the near edge") and, on the cross
    /// axis, against whichever edge the drawer grows FROM. That second half is what keeps the
    /// island still — the window grows away from the capsule, and the capsule is arranged
    /// against the far side of it rather than floated in the middle.
    /// </summary>
    private void ApplyDrawerAnchor(bool vertical)
    {
        var away = DrawerCrossDirection > 0;
        Pill.HorizontalAlignment = vertical
            ? (away ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Right)
            : Avalonia.Layout.HorizontalAlignment.Left;
        Pill.VerticalAlignment = vertical
            ? Avalonia.Layout.VerticalAlignment.Top
            : (away ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Bottom);
    }

    /// <summary>Capsule's current long-axis extent: the height on a vertical edge, else the width.</summary>
    private double PillLongAxis()
    {
        var b = Pill.Bounds;
        return SplitIsVertical ? b.Height : b.Width;
    }

    /// <summary>
    /// Extent of the island along the long axis, which is what the ⅓/⅓/⅓ clipboard cycle
    /// zones and the section hit test are measured against. The capsule IS the island, so this
    /// is the capsule's own length MINUS the clipboard section — the section sits on the far end
    /// and must not push the island's own click zones outwards while it is showing.
    /// </summary>
    private double IslandHalfExtent()
    {
        var longAxis = IslandLongAxis();
        return longAxis > 0 ? longAxis : OverlayTokens.CollapsedW;
    }

    /// <summary>
    /// Wire the clipboard SECTION: click opens the drawer, wheel cycles the preview inside the
    /// section, double-click re-copies the newest entry, and file drops land here. Right-click
    /// opens the same clipboard block of the context menu the ball used to.
    /// </summary>
    private void WireClipboardSection()
    {
        ClipboardSection.PointerPressed += OnClipboardSectionPressed;
        ClipboardSection.PointerReleased += OnClipboardSectionReleased;
        ClipboardSection.PointerCaptureLost += (_, _) => _machine.SplitHold = false;
        // Avalonia 11: gestures on a control are added via the routed event directly (not
        // via Gestures.SetDoubleTapped — that API exists for elements that don't expose the
        // event, but InputElement.DoubleTapped is the canonical path here).
        ClipboardSection.AddHandler(PointerWheelChangedEvent, OnClipboardSectionWheel,
            handledEventsToo: false);
        ClipboardSection.DoubleTapped += OnClipboardSectionDoubleTapped;
        // Drag-drop on the section: file paths land in the history as File / MultiFile. The
        // window has WS_EX_NOACTIVATE, but Avalonia's DragDrop routed events do not depend
        // on activation — they fire as long as AllowDrop=true and a draggable source is
        // over us. The spec calls this out explicitly.
        DragDrop.SetAllowDrop(ClipboardSection, true);
        ClipboardSection.AddHandler(DragDrop.DragEnterEvent, OnClipboardDropOver);
        ClipboardSection.AddHandler(DragDrop.DragOverEvent, OnClipboardDropOver);
        ClipboardSection.AddHandler(DragDrop.DropEvent, OnClipboardDrop);
    }

    /// <summary>
    /// Press on the clipboard section. It is a CHILD of the capsule, so unlike the old ball it
    /// does have to mark the press handled or the capsule's own click path would also run — and
    /// the capsule's path is a long-axis hit test that would treat the section as the island.
    /// </summary>
    private void OnClipboardSectionPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            OpenContextMenu(isBallContext: true);
            e.Handled = true;
            return;
        }
        if (!props.IsLeftButtonPressed) return;
        // A press on the section freezes the clipboard section's own lifetime, so a user who
        // opened the drawer and then read it did not lose the section out from under them.
        _machine.SplitHold = true;
        _sectionPressOrigin = e.GetPosition(this);
        e.Pointer.Capture(ClipboardSection);
        e.Handled = true;
    }

    /// <summary>
    /// Release on the clipboard section. A press past the capsule's own
    /// <see cref="OverlayTokens.ClickMaxPx"/> threshold is a drag, and the section is not
    /// draggable any more, so it is simply ignored — the same threshold the capsule uses, so
    /// "dragged" and "clicked" cannot be confused.
    /// </summary>
    private void OnClipboardSectionReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        _machine.SplitHold = false;
        var pos = e.GetPosition(this);
        var dx = pos.X - _sectionPressOrigin.X;
        var dy = pos.Y - _sectionPressOrigin.Y;
        e.Handled = true;
        if (Math.Sqrt(dx * dx + dy * dy) > OverlayTokens.ClickMaxPx) return;
        HandleClipboardSectionClick();
    }

    /// <summary>
    /// Wheel over the clipboard section cycles the preview INSIDE it, newest-first. The first
    /// wheel down jumps to the oldest, each subsequent wheel-down walks one step toward newer,
    /// wheel-up is the mirror, and it wraps at both ends. Scrolling here does NOT open the
    /// drawer — it just changes what the section previews.
    /// </summary>
    private void OnClipboardSectionWheel(object? sender, PointerWheelEventArgs e)
    {
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
    /// Double-click on the clipboard section: re-copy the most-recent entry back to the system
    /// clipboard. The "I lost focus, get my copy back" gesture. Single-click still toggles the
    /// drawer.
    /// </summary>
    private void OnClipboardSectionDoubleTapped(object? sender, RoutedEventArgs e)
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
            ? $"Section double-click re-copied: {entry.Kind}"
            : $"Section double-click re-copy FAILED: {entry.Kind}");
        // Light a sound either way so the gesture has feedback even when the writer is a noop.
        IslandSounds.Play(IslandSoundKind.Hover, _settings);
        e.Handled = true;
    }

    /// <summary>Filter: only file drops are accepted; text drops go through the OS listener.</summary>
    private void OnClipboardDropOver(object? sender, Avalonia.Input.DragEventArgs e)
    {
        e.DragEffects = HasAnyStorageItem(e.DataTransfer) ? Avalonia.Input.DragDropEffects.Copy : Avalonia.Input.DragDropEffects.None;
    }

    /// <summary>
    /// File drop onto the clipboard section: write the file paths to the system clipboard via
    /// the same <see cref="WindowsClipboardWriter"/> the tray submenu and drawer use, then push
    /// the resulting entry into the history.
    /// </summary>
    private void OnClipboardDrop(object? sender, Avalonia.Input.DragEventArgs e)
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
                AppLog.Warn($"Section drop: WindowsClipboardWriter.WriteFiles failed for {paths.Count} paths");
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
            AppLog.Warn("OnClipboardDrop failed", ex);
        }
    }

    /// <summary>Clicks only (1.8.1). Swipe L/R/U/D cycle/collapse removed — CycleNext/Prev remain for API/tests.</summary>
    private void WirePointerClicks()
    {
        Pill.PointerPressed += OnPillPointerPressed;
        Pill.PointerReleased += OnPillPointerReleased;
        Pill.PointerCaptureLost += (_, _) => ResetPressState();

        // Stage 8 (§6): the notification's one action. It is a CHILD of the pill, so the press
        // must be marked handled here or the island's own click path also fires and the capsule
        // pins itself at the same time as it clears — exactly the ordering the clipboard section
        // already had to solve for the drawer.
        NotifActionClear.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            ClearNotifications();
        };
    }

    /// <summary>
    /// Stage 8: clear the visible notifications. Dispatches the EXISTING
    /// <see cref="OverlayCommand.Clear"/>, which is what zeroes the unread count, returns the
    /// machine to the kind it was in before the notification and drops the payload — no queue,
    /// no unread rule and no FSM transition is invented here.
    /// </summary>
    private void ClearNotifications()
    {
        if (_machine.Snapshot().Kind is not (OverlayKind.Notification or OverlayKind.Expanded or OverlayKind.Error))
            return;
        var before = _machine.Snapshot().Kind;
        _machine.Dispatch(OverlayCommand.Clear);
        var after = _machine.Snapshot().Kind;
        if (before != after) OnKindChanged(before, after);
        ApplySize();
        Paint();
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
    /// Write the clipboard section's preview from a single ClipboardEntry — used by the wheel
    /// cycle and the post-capture reset. Reuses the same IconPackService.Create +
    /// ClipboardHalfPreview rules the auto-update path uses, so a wheeled-back preview looks
    /// identical to the preview the user sees on a fresh capture.
    /// </summary>
    private void ApplyBallPreviewFromEntry(ClipboardEntry entry)
    {
        var payload = ClipboardHistory.BuildPayload(entry, entry.CapturedAt == default ? DateTimeOffset.UtcNow : entry.CapturedAt);
        var tint = new SolidColorBrush(ClipboardIconTint(entry.Kind));
        ClipboardSectionIcon.Child = IconPackService.Create(
            _settings.IconPack, ClipboardHalfPreview.IconKeyFor(entry.Kind),
            CurrentIconCollapsed(), tint);
        var baseText = ClipboardHalfPreview.TextFor(payload);
        ClipboardSectionText.Text = baseText + ClipboardHalfPreview.RunSuffix(entry.RunCount);
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
    private void RefreshTrayPauseLabel() => UpdateTrayTooltip();

    /// <summary>
    /// The ONE place the tray tooltip is written.
    /// <para>
    /// It had two: each tray class set the unread count, and the privacy-pause path overwrote the
    /// whole string. Whichever ran last won, so pausing the clipboard silently dropped the
    /// unread count from the tooltip and vice versa. One owner, one composition.
    /// </para>
    /// <para>
    /// The listener state lives here too. A user who declines the notification-access prompt gets
    /// an island that never shows a toast again, and until now there was nothing on screen and
    /// nothing in the tray to say why — only a WARN in a log file they do not know exists. The
    /// tooltip is where they already look. It is only added once the platform has actually
    /// answered: a null AccessStatus means "not asked yet", and saying "disabled" then would be
    /// a lie during the first second of startup.
    /// </para>
    /// </summary>
    private void UpdateTrayTooltip()
    {
        var pause = ClipboardPrivacyPause.RemainingMinutes(
            _settings.ClipboardPrivacyPauseUntilUtc, DateTime.UtcNow);

        // Null AccessStatus means the platform has not answered yet — not "denied". Passing that
        // as "known" would print "уведомления выкл" during the first second of every launch.
        var known = _notifSource?.AccessStatus is not null;
        var denied = _notifSource?.AccessStatus
            == Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Denied;

        var text = TrayTooltipText.For(_machine.UnreadCount, pause, known, denied);

        // 2026-10-02: a run being replayed is worth saying out loud. "3" in the tooltip is a count
        // the user cannot act on; "ещё 2" tells them the island is still working through it and
        // has not silently swallowed the rest.
        var pending = _machine.PendingNotifications;
        if (pending > 0)
            text += $" — ещё {pending}";

        try { _winTray?.SetTooltip(text); } catch { /* tray may be torn down on shutdown */ }
        try { _tray?.SetTooltip(text); } catch { /* tray may be torn down on shutdown */ }
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
        // 1.14: the clipboard section is a CHILD of the capsule and handles its own press, so
        // anything reaching this handler is a press on the island's own content. There is no
        // second hit region outside the capsule any more — the ball and its whole enlarged
        // window are gone.
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
            // ⅓/⅓/⅓ clipboard-cycle zones keep their collapsed-pill positions. The zone extent
            // is the island's OWN length — the clipboard section sits beyond it and is not part
            // of the cycle, so the zones cannot slide outboard while a copy is showing.
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
    /// 1.14: the drawer's own dismiss affordances: a press on its padding (the empty part)
    /// closes it. The rows handle their own press and mark it handled, so they never reach
    /// this — which is how "click a row" and "click the empty part" stay two different actions
    /// off one handler.
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
        // 1.14: leaving the drawer closes it. The section and the drawer are one surface now,
        // so a pointer that wanders off the pair dismisses rather than leaving a list hanging
        // in mid-air — the same rule the hover-refcount already applies to the island.
        HistoryPanel.PointerExited += (_, _) => CloseHistoryPanel();
    }

    /// <summary>
    /// 1.14: click on the clipboard section toggles the history drawer. The section is a child
    /// of the capsule, so this runs instead of — never in addition to — the island's own click
    /// path, and the island's pin/unpin does not fire.
    /// </summary>
    private void HandleClipboardSectionClick()
    {
        _machine.SplitHold = false;
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
            AppLog.Info("Section click: clipboard history is empty — drawer not opened");
            IslandSounds.Play(IslandSoundKind.Hover, _settings);
            return;
        }

        _historyRowCount = rows.Count;
        BuildHistoryRows(rows);
        _historyOpen = true;
        _historyDir = 1;
        // Visible and hit-testable from the first frame, at zero opacity. Making it hit-testable
        // only when the fade finished would mean a click during the 300 ms fade went through to
        // the section and TOGGLED THE DRAWER SHUT — the fastest possible way to make the drawer
        // feel broken. It is transparent, not absent: a transparent-but-present control takes the
        // click, which is what "open" means.
        HistoryPanel.IsVisible = true;
        HistoryPanel.IsHitTestVisible = true;
        HistoryPanel.Opacity = 0;
        // The drawer spans the CAPSULE's long axis exactly, so its size is a function of the
        // current capsule length and the row count — see ClipboardDrawer.SizeFor. That equality
        // is what makes the seam read as one object instead of two shapes that happen to touch.
        var (capsuleLong, capsuleCross) = IslandCapsuleSize();
        var drawer = ClipboardDrawer.SizeFor(rows.Count, capsuleLong);
        HistoryPanel.Width = drawer.Width;
        HistoryPanel.Height = drawer.Height;
        IslandSounds.Play(IslandSoundKind.Expand, _settings);
        // The window has to grow to hold the drawer, and ApplySize is what does that — the same
        // path every size change uses, so the capsule and the drawer share one morph.
        ApplySize();
        PlaceHistoryPanel();
    }

    /// <summary>
    /// Fold the drawer away. The rows stay alive until the dismiss finishes, so the drawer is
    /// one continuous surface rather than something rebuilt and re-faded.
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
        _historyPress = -1;

        var brush = new SolidColorBrush(_inkSecondary);
        // 1.18: row reactions ride the user's ColorAccent instead of a hardcoded white lift —
        // a neutral grey fill says "this is a row", an accent tint says "this is the action",
        // which is what the spec asks for. Both sit far below the accent's own opacity, so the
        // drawer never turns into an accent-coloured card.
        var accent = ParseColor(_settings.ColorAccent, OverlayTokens.AccentHex);
        _historyHoverBrush = new SolidColorBrush(WithAlpha(accent, 0.16));
        _historyPressBrush = new SolidColorBrush(WithAlpha(accent, 0.30));
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var index = i;

            // 1.13: each row's icon gets its own format-tinted brush (text #9CC4FF / file
            // #C8C8CC / multi-file #7AA8FF). The header colour still drives the title text so
            // the panel reads as one consistent typography, but the icon differentiates the
            // formats at a glance.
            var iconBrush = new SolidColorBrush(ClipboardIconTint(row.Entry.Kind));
            var icon = new Viewbox
            {
                // 1.18: 16 DIP inside a 24 DIP row — still a small anchor rather than a
                // thumbnail, but large enough to read a document/image/stack glyph at a
                // glance. IconPackService is asked for the same size, so the pack's own
                // stroke weight scales with the box instead of being stretched by the
                // Viewbox.
                Width = 16,
                Height = 16,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                IsHitTestVisible = false,
                Child = IconPackService.Create(_settings.IconPack, row.IconKey, 16, iconBrush),
            };
            var title = new TextBlock
            {
                // 1.13: the run-count suffix (— ×N) lives on the title so the panel reads
                // "first item — ×N" the way the spec asks. The suffix is empty when RunCount
                // is 1, so a fresh row stays exactly the same as it was before this feature.
                Text = row.Title + ClipboardHalfPreview.RunSuffix(row.RunCount),
                FontFamily = IslandFonts.Resolve(_settings.FontFamily),
                FontSize = 11,
                // 1.18: the user's ColorTextPrimary, not a hardcoded white — the drawer's
                // rows are the most text-heavy surface in the island and were the one place
                // that ignored the palette.
                Foreground = new SolidColorBrush(ParseColor(_settings.ColorTextPrimary, OverlayTokens.TextHex)),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                // One line, always: a wrapped row would make the panel taller than the window
                // that was sized for exactly N rows, and the last row would fall off the edge.
                TextTrimming = TextTrimming.CharacterEllipsis,
                // icon 16 + spacing 4 on the left, spacing 4 + the age column on the right.
                MaxWidth = OverlayTokens.HistoryPanelW - 2 * OverlayTokens.HistoryPanelPadX - 16 - 4 - 4 - 46,
            };
            var age = new TextBlock
            {
                Text = row.AgeText,
                FontFamily = IslandFonts.Resolve(_settings.FontFamily),
                FontSize = 10,
                // 1.18: the muted metadata colour is the same parsed secondary the rest of
                // the island uses, so a custom palette reaches the drawer too.
                Foreground = new SolidColorBrush(_inkSecondary),
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
            // 1.18: press is a short, flat reaction — the fill deepens for as long as the
            // button is down and nothing scales, so the row never looks like a pop-up card.
            // _historyPress is tracked separately from _historyHover because the pointer can
            // leave the row while it is still held.
            host.PointerPressed += (_, e) =>
            {
                _historyPress = index;
                PaintHistoryRow(index, pressed: true);
                // Handled: a press on a row must not also be the "clicked the empty part of the
                // panel" signal that closes it, and must not reach the ball underneath.
                var props = e.GetCurrentPoint(host).Properties;
                if (props.IsRightButtonPressed)
                {
                    // 1.13: row-level pin toggle. The most-recent row (index 0) is unpinnable
                    // by spec; the menu shows that as a disabled entry. Core owns the rule, so
                    // we ask it directly.
                    _historyPress = -1;
                    PaintHistoryRow(index, pressed: false);
                    OpenRowContextMenu(row, index, host);
                    e.Handled = true;
                    return;
                }
                e.Handled = true;
                ApplyHistoryRow(row);
            };
            host.PointerReleased += (_, _) =>
            {
                _historyPress = -1;
                PaintHistoryRow(index, pressed: false);
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
        var previous = _historyHover;
        _historyHover = index;
        // Repaint both the row that lost hover and the one that gained it. Repainting the
        // old row separately (rather than clearing it) is what makes a press survive the
        // pointer leaving the row while the button is still down.
        PaintHistoryRow(previous, pressed: previous == _historyPress);
        PaintHistoryRow(index, pressed: index == _historyPress);
    }

    /// <summary>
    /// 1.18: one row's fill, from the two states it can be in. Press wins over hover; a row
    /// in neither state is fully transparent so the drawer's own fill shows through.
    /// </summary>
    private void PaintHistoryRow(int index, bool pressed)
    {
        if (index < 0 || index >= _historyRowControls.Count) return;
        var row = _historyRowControls[index];
        if (pressed && _historyPressBrush is not null) row.Background = _historyPressBrush;
        else if (index == _historyHover && _historyHoverBrush is not null) row.Background = _historyHoverBrush;
        else row.Background = Avalonia.Media.Brushes.Transparent;
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

    /// <summary>
    /// Per-frame drawer motion, driven from the existing 16 ms morph tick. No new timer.
    /// <para>
    /// The window is already growing/shrinking around the drawer, so its own <c>t</c> IS the
    /// drawer's progress — the two are the same movement by construction. All of the phase
    /// maths lives in <see cref="ClipboardDrawer"/> (Core), so "the drawer is at the seam at
    /// t = 1" and "the drawer leaves from the direction it opens in" are tests, not comments.
    /// </para>
    /// </summary>
    private void ApplyDrawerAnim(double t)
    {
        if (_historyDir == 0) return;
        var opening = _historyDir > 0;
        var frame = ClipboardDrawer.FrameAt(t, opening, DrawerCrossDirection);
        var reduced = AnimReduced.Resolve(OsAnimationsEnabled(), _settings.ReducedMotion);
        var eased = AnimEase.WithReducedMotion(frame.Opacity, reduced);

        HistoryPanel.Opacity = eased;
        // The travel is on the cross axis, signed towards the seam. On a horizontal island
        // that is Y, on a vertical one X — the same axis the drawer itself grew along.
        if (SplitIsVertical) _historySlide.X = -frame.Slide;
        else _historySlide.Y = -frame.Slide;
        HistoryPanel.RenderTransform = _historySlide;
    }

    /// <summary>
    /// The drawer's resting state, called wherever a morph finishes. Mirrors
    /// <see cref="SettleClipboardSection"/>: the transition is allowed to end in exactly one
    /// place, so no residual opacity or offset survives into the next open.
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
        _historyPress = -1;
        _historyRowCount = 0;
    }

    /// <summary>
    /// One-phase schedule for the drawer, declared once so appear and dismiss cannot disagree
    /// about what "the drawer's own progress" means. Spec: the animation layer doc's
    /// AnimTimeline. The drawer is a single movement — it slides out and fades as one event —
    /// so a schedule with one phase is the honest description, not a simplification.
    /// </summary>
    private static readonly AnimTimeline HistoryPanelTimeline = new(new[] { ("drawer", 0.0, 1.0) });

    /// <summary>
    /// Put the drawer where <see cref="ClipboardDrawer"/> says it goes, in window coordinates.
    /// Called from the morph tick and after <see cref="ApplySize"/>, because the drawer's
    /// position is a function of the window size.
    /// <para>
    /// The long axis is 0 — the drawer starts at the capsule's leading edge and spans exactly
    /// the capsule's length, so it is flush along the whole seam. The cross axis is the
    /// capsule's cross origin plus the drawer's near edge, which is also flush. Nothing here
    /// has a gap in it; that is the whole point.
    /// </para>
    /// </summary>
    private void PlaceHistoryPanel()
    {
        if (!_historyOpen) return;
        var (capsuleLong, capsuleCross) = IslandCapsuleSize();
        var scale = RenderScaling <= 0 ? 1 : RenderScaling;

        // The window's cross origin sits from the capsule's by the drawer's own extent, on
        // whichever side the drawer grows into — the same number PlaceIsland seats the window
        // with, so the two cannot disagree about where the capsule is.
        var crossShift = ClipboardDrawer.CrossShiftDipFor(_settings.Edge, _historyRowCount) * scale;
        var drawerCrossStart = ClipboardDrawer.DrawerCrossStart(
            _settings.Edge, capsuleCross, _historyRowCount) + crossShift;

        // Margin is measured from the window's own top-left, so the capsule's position inside
        // the window is added to the capsule-relative geometry.
        HistoryPanel.Margin = SplitIsVertical
            ? new Thickness(drawerCrossStart, 0, 0, 0)
            : new Thickness(0, drawerCrossStart, 0, 0);
    }

    private void OpenContextMenu()
    {
        OpenContextMenu(isBallContext: false);
    }

    /// <summary>
    /// Shared builder for both context surfaces (capsule right-click and clipboard-section
    /// right-click). The section version adds the clipboard-only items: "История буфера",
    /// "Очистить историю", "Не реагировать 30 мин". Both menus share the common items
    /// (Action Center, weather, settings) because right-clicking either element should still
    /// let the user get at the global controls.
    /// <para>
    /// 1.14: the "Закрепить шарик" entry is gone. Pinning existed only because the ball could be
    /// dragged off its home spot, and there is nothing left to pin.
    /// </para>
    /// </summary>
    private void OpenContextMenu(bool isBallContext)
    {
        var menu = new Avalonia.Controls.ContextMenu();
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

        // 1.13 clipboard-only block — only when the user right-clicks the clipboard section
        // itself. The capsule's context menu gets just the history shortcut.
        if (isBallContext)
        {
            menu.Items.Add(Menu(_historyOpen ? "Скрыть историю буфера" : "История буфера",
                () => HandleClipboardSectionClick()));
            // "Очистить историю" — empties the ring buffer; the drawer closes if it was open.
            menu.Items.Add(Menu("Очистить историю", () =>
            {
                _clipboardHistory.Clear();
                if (_historyOpen) CloseHistoryPanel();
                AppLog.Info("История буфера очищена из меню секции");
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
            // Capsule right-click still gets the history shortcut (1.12.3) — same path the
            // section click uses. Only the section gets the richer clipboard submenu.
            menu.Items.Add(Menu(_historyOpen ? "Скрыть историю буфера" : "История буфера",
                () => HandleClipboardSectionClick()));
        }
        menu.Items.Add(Menu("Настроить монитор…", () => OpenSettings("system")));
        menu.Items.Add(new Avalonia.Controls.Separator());
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
        _machine.CollapsedWidthScale = _settings.IslandWidthScale;
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
            // Coming back from the tray toggle is the other "first appear" the spec names.
            PlayFirstAppearWobble();
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

    /// <summary>Whether the notification jump has currently taken the window to Topmost.</summary>
    private bool _notifyTopActive;

    /// <summary>
    /// Keep the island above the app windows while a notification is on the capsule, and put it
    /// back the moment the run is over.
    /// <para>
    /// A toast the user asked to see is not much use drawn behind the window they are reading. The
    /// jump is deliberately transient and deliberately does NOT change ZOrderMode: the configured
    /// mode still governs the island the rest of the time, so "Desktop" still means desktop.
    /// </para>
    /// <para>
    /// The fullscreen rules outrank this. If the island is hidden or click-through because
    /// something is fullscreen, this leaves it alone — pulling a click-through window back to
    /// Topmost would hand the pointer back to something the user is watching fullscreen.
    /// </para>
    /// </summary>
    private void SyncNotifyJumpToTop(OverlayKind kind)
    {
        var wanted = _settings.NotifyJumpToTop
            && kind == OverlayKind.Notification
            && !_hiddenByFullscreen
            && !_clickThroughActive;

        if (wanted == _notifyTopActive) return;
        _notifyTopActive = wanted;
        try
        {
            Win32Overlay.ApplyZOrder(this, wanted ? ZOrderMode.Topmost : _settings.ZOrderMode);

        }
        catch (Exception ex)
        {
            AppLog.Warn("SyncNotifyJumpToTop failed", ex);
        }
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
            // Subtle colon blink once per second (lit on even seconds). Reduced motion pins it
            // lit — the blink is decoration, so removing it costs no information and takes a
            // persistent 1 Hz flicker out of the middle of the field of view. See FlickerGate.
            var colonLit = FlickerGate.ColonLit(
                AnimReduced.Resolve(OsAnimationsEnabled(), _settings.ReducedMotion), now);
            digitalOk = DigitalClockView.Apply(DigitalClockRow, formatted, _clockDigitSize, _clockBrush, colonLit);
        }
        ClockText.IsVisible = !digitalOn || !digitalOk;
        DigitalClockRow.IsVisible = digitalOn && digitalOk;

        // Clipboard-cycle preview wins over the clock when the user has at least one item.
        var snap = _machine.Snapshot();
        var cycleCount = snap.Payload.ClipboardCycleCount;
        if (cycleCount > 1)
        {
            // 1.14: the clipboard preview is shown ONCE, in the section — that is the whole point
            // of the section ("справа появляется секция с иконкой формата и превью содержимого",
            // spec §Концепт). This row used to show the same text as well, and it cost the layout
            // twice over: the duplicated preview did not fit the island's own box once the section
            // had taken its 110 DIP, so it overflowed the centre-aligned row on BOTH sides and
            // the section's own text was drawn underneath the island's date and weather. The spec
            // also says the clock STAYS on the left, and this branch was hiding it to make room
            // for the duplicate. So: the row keeps the navigation (chevrons + the 1/2 counter)
            // and the clock, and the section keeps the text.
            CyclePreviewText.IsVisible = false;
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
        // 1.14: does this call carry a clipboard section attach/retract? Both routes into a
        // changed IsSplitClipboard — the machine's own expiry in OverlayMachine.Tick and the
        // capture command — land here, because both callers follow the dispatch with ApplySize.
        // The capsule size morphs through the normal StartMorph path below; what is recorded
        // here is the DIRECTION, which drives the section's own growth per frame in
        // ApplyClipboardSectionMorph.
        if (snap.IsSplitClipboard != _splitApplied)
        {
            _sectionDir = snap.IsSplitClipboard ? 1 : -1;
            _splitApplied = snap.IsSplitClipboard;
            // 1.14: the drawer cannot outlive the section. The split has its own 6 s lifetime
            // (ClipboardHistory.MaxPillMs), so a drawer left open across the expiry would keep
            // asking for the drawer-sized window while the section retracted into the capsule —
            // and the section that opens it would be gone. Closing here folds the drawer's
            // dismissal into the same morph that pulls the section back, so there is one movement
            // rather than two.
            if (!snap.IsSplitClipboard && _historyOpen)
            {
                _historyOpen = false;
                // -1 so the drawer fades out on THIS morph's t. Left at 0 it would sit at full
                // opacity until some later morph happened to call SettleHistoryPanel, which may
                // never come — a drawer stuck on screen over a shrinking window.
                _historyDir = -1;
            }
            // The capsule's own length is captured further down, once (w, h) are known —
            // see _sectionBase.
        }
        var batteryChip = _settings.ShowBatteryInCollapsed
            && _lastPower is { HasBattery: true }
            && snap.Kind is OverlayKind.Idle or OverlayKind.Collapsed;
        // 1.12.3: the capsule is the island again — SizeFor is asked for the plain island size
        // and NOT for the split long axis. The clipboard no longer takes a compartment of the
        // pill; it lives in the ball, in the window around it. This is what makes the whole
        // rest of the island (click zones, seconds strip, hit test) work off the capsule again.
        var (w, h) = IslandLayout.SizeFor(snap.Kind, snap.WeatherEnabled, _settings.Orientation,
            _settings.Edge, batteryChip, _machine.StatsMetricCount, _machine.StatsRowCount,
            collapsedScale: _settings.IslandWidthScale);
        if (snap.Kind is OverlayKind.Idle or OverlayKind.Collapsed)
        {
            var peek = _hoverPin.IsContentExpanded;
            var showSeconds = _settings.ShowClockSeconds || peek;
            // These two extras are appended AFTER SizeFor, so they are outside the width scale
            // applied there. They are parts of the collapsed capsule (the seconds pair and the
            // peek's extra content), so they scale with it — otherwise widening the island
            // leaves a fixed-size stub on the right and the clock sits off-centre.
            var extra = 0.0;
            if (showSeconds)
                extra += DigitalClockGlyphs.SecondsExtraCollapsedW;
            if (peek)
                extra += OverlayTokens.IdlePeekExtraW;
            w += extra * IslandWidth.ClampScale(_settings.IslandWidthScale);
        }

        // 1.14: the clipboard SECTION grows the capsule from the island's OWN length, so the
        // base is captured on every ApplySize while a section transition is in flight, not
        // only on the frame it starts. Without this a re-ApplySize mid-morph (the 200 ms tick
        // does it whenever the kind changes) would leave the section growing off a stale base.
        if (_sectionDir != 0) _sectionBase = SplitIsVertical ? h : w;

        // The window is a separate target: with a section (and, once the section is clicked,
        // a drawer hanging off it) the window has to be big enough to hold the drawer, and it
        // follows the NEW split state while the capsule target above does not — that is what
        // makes an attach a window-only morph.
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
        // Inflate is decided on whichever of the two grew: opening the clipboard DRAWER does not
        // move the capsule at all and only the window does, and that has to count as an inflate.
        var inflate = (w * h) >= (fromW * fromH) || (winToW * winToH) >= (winFromW * winFromH);
        var morphSpeed = AnimationTiming.Effective(
            _settings.AnimationSpeed,
            inflate ? _settings.AnimMorphInflate : _settings.AnimMorphCollapse);
        // Reduced motion takes the same path as "animations off" in the speed setting, and on
        // purpose the same path: the branch below does not merely jump the size, it also
        // settles the section and the drawer to their defined resting states, which is exactly
        // what a user who cannot tolerate motion needs. Shorter durations would leave the
        // section mid-growth and the drawer mid-slide.
        // 1.12.4: this is the ONLY reduced-motion branch for size changes, and it is deliberately
        // a settle and not a zero-duration run. Everything downstream of it — the hover peek's
        // width change, the monitor's expansion, the appear/dismiss styles — reaches the reduced
        // path through this one check, so none of them can grow its own "just make it fast" exit.
        var reduced = AnimReduced.Resolve(OsAnimationsEnabled(), _settings.ReducedMotion);
        if (same || reduced || !AnimationTiming.IsEnabled(morphSpeed) || !IsVisible)
        {
            // No morph will run, so nothing will ever consume _sectionDir or _historyDir. Settle
            // the section and the drawer to their defined resting states here instead of leaving
            // them mid-animation.
            StopMorph(snapToTarget: false);
            ResetMorphVisuals();
            // Size FIRST, then settle. The settle writes the section's resting length onto the
            // pill, and the size write is what puts the pill at the island's own length — so the
            // other order would leave a capsule 110 DIP shorter than the section it is drawing.
            SetSizeImmediate(w, h);
            SettleClipboardSection(w, h);
            SettleHistoryPanel();
            return;
        }

        var enteringNotify = inflate && snap.Kind == OverlayKind.Notification;
        var leavingNotify = !inflate && _prevKind == OverlayKind.Notification;

        // 1.13.1: a morph already flying to THIS exact target must not be restarted.
        //
        // The 200 ms tick reaches ApplySize twice for one hover change: once through
        // TickHoverPin → ApplyHoverExpandedState (which calls ApplySize itself), and again
        // through the tick's own `if (hoverChanged) ApplySize()`. The second call re-entered
        // StartMorph a millisecond later, which re-ran PrepareMorphVisualStart and
        // _morphWatch.Restart() over a morph that had not ticked yet — so the track restarted
        // from the pre-morph width, the section's own growth was reseeded, and the result read
        // as a stutter. The log showed it plainly: every hover morph logged twice, 1 ms apart.
        //
        // 1.14: this guard is also what keeps the DRAWER honest. Opening and closing the drawer
        // changes the window target and nothing else — the capsule size does not move — so
        // "a morph already flying to this exact target" is precisely the "a second open/close
        // a millisecond later" case, and it is caught by the same comparison. If this call were
        // ever reordered above the guard, every drawer toggle would read as a stutter again.
        //
        // Guarding inside StartMorph rather than at the call sites is deliberate: ApplySize has
        // 28 callers and any of them can be the second one. Comparing targets catches the whole
        // class, not just the pair that exists today.
        if (MorphRestart.WouldRestartSameTarget(
                _morphActive, _morphToW, _morphToH, _winToW, _winToH, w, h, winToW, winToH))
            return;

        StartMorph(fromW, fromH, w, h, winFromW, winFromH, winToW, winToH,
            morphSpeed, inflate, enteringNotify, leavingNotify);
    }

    /// <summary>
    /// Window size for a capsule of <paramref name="w"/>×<paramref name="h"/>.
    /// <para>
    /// 1.14: three states, not two — no section (the capsule alone), a section (still the
    /// capsule alone, because a section is PART of the capsule and needs no window), and a
    /// section with the drawer unfolded (capsule + drawer stacked on the cross axis). The
    /// section state being identical to the closed state is the point: the clipboard no longer
    /// needs any room of its own, which is what removed the drag-slack window.
    /// </para>
    /// <para>
    /// FIX: "still the capsule alone" has to mean the capsule's FULL length. The long axis
    /// used to come from <see cref="IslandCapsuleSizeFor"/>, which subtracts
    /// <c>_sectionPeek</c> — correct for its own question ("how long is the island ITSELF,
    /// for placement and hit zones"), wrong here. With a section up the pill is
    /// <c>_sectionBase + _sectionPeek</c> (404) while the window came out at
    /// <c>base - _sectionPeek</c> (184), so the window clipped the section AND 110 DIP of the
    /// capsule's own trailing end: the clock was pushed out of the window and the section
    /// rendered as a bare seam with its content off-screen.
    /// </para>
    /// <para>
    /// The target takes the larger of the growth the section has already shown and the growth
    /// it is about to show, so the window is already the right length on the first frame of an
    /// attach (the pill catches up over the morph) and stays the right length through a
    /// retract until the last frame of it. The cross axis is never touched — the section does
    /// not grow it, and the drawer is accounted for separately as window space.
    /// </para>
    /// </summary>
    private (double Width, double Height) WindowFor(bool withSection, double w, double h)
    {
        // FIX: the long axis here is the island's OWN length as the caller computed it (w, h
        // straight out of IslandLayout.SizeFor), NOT a live pill size. It used to be obtained
        // from IslandCapsuleSizeFor, which subtracts _sectionPeek because its other callers
        // pass Pill.Width — the length that already carries the section. Both were true at
        // once, so the section was subtracted twice and added back once: w 294, peek 110 →
        // baseLong 184 → capsuleLong 294 → a 294 DIP window for a 404 DIP capsule. The window
        // clipped the section and the capsule's trailing 110 DIP all over again, on the code
        // path that was written specifically to stop that.
        //
        // So: the island's own length is w/h, and the section is added on top exactly once.
        var ownLong = SplitIsVertical ? h : w;
        var ownCross = SplitIsVertical ? w : h;
        var sectionForWindow = Math.Max(
            _sectionPeek,
            _splitApplied || _sectionDir > 0 ? ClipboardSectionTrack.Width : 0);
        var capsuleLong = ownLong + sectionForWindow;
        return ClipboardDrawer.WindowFor(SplitIsVertical, capsuleLong, ownCross,
            withSection ? _historyRowCount : 0, withSection && _historyOpen);
    }

    private void SetSizeImmediate(double w, double h)
    {
        // The capsule keeps its own size; only the window around it grows when the drawer is
        // out. PlaceIsland() then re-seats the window so the capsule lands on the same screen
        // pixels either way.
        Pill.Width = w;
        Pill.Height = h;
        var (ww, wh) = WindowFor(_splitApplied || _sectionDir != 0, w, h);
        Width = ww;
        Height = wh;
        PlaceIsland();
        // The drawer's position and the seam's corner radii both depend on the window size it
        // just took, so they are written after the window is — otherwise the drawer would sit
        // at the previous frame's coordinates for one frame, which on a window that grew by
        // 250 DIP is a visible flash at the old spot.
        ApplySeamRadii();
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
        // The interpolated cw/ch are the island's OWN length, exactly as ApplySize asked for it.
        // A SETTLED clipboard section is a permanent 110 DIP addition to that length, and it is
        // re-written by the section only while _sectionDir != 0 — which is precisely the case
        // this is not. So an unrelated morph (a hover expand, a longer weather string, a kind
        // change) animated a capsule from base+110 down to base and snapped it back at t >= 1:
        // the section's own compartment visibly vanished for the length of the animation and
        // then reappeared. Adding the settled length here keeps the section a compartment of
        // the capsule at every frame, not just at rest.
        // During a section transition this is deliberately not applied — ApplyClipboardSection
        // is already writing _sectionBase + peek per frame, and the two would double-count.
        if (_sectionDir == 0 && _splitApplied)
        {
            if (SplitIsVertical) ch += ClipboardSectionTrack.Width;
            else cw += ClipboardSectionTrack.Width;
        }
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
            // A clipboard section morph that just finished must land on the section's resting
            // values, not on the last frame's width/opacity. Settling also clears the direction
            // so the next attach starts from the island's own length again.
            SetSizeImmediate(_morphToW, _morphToH);
            // Same for the drawer: opacity, offset and the row cleanup land in exactly one place.
            SettleClipboardSection(_morphToW, _morphToH);
            SettleHistoryPanel();
        }
    }

    // -- 1.14 clipboard section + drawer animation and geometry -------------------------

    /// <summary>
    /// The per-frame clipboard SECTION motion, driven from the same morph <c>t</c> as the
    /// capsule and the window so the island growing and the clipboard appearing are one
    /// movement. Runs before the notify-style branch in <see cref="ApplyMorphAux"/> because a
    /// clipboard morph is not a notify morph — the section has to animate for both.
    /// <para>
    /// This is the whole of the 1.12.4 two-phase morph, collapsed to one phase. There is no
    /// "run the capsule out, then bring it back and peel a ball out of it" any more: the capsule
    /// runs out once and STAYS out, because the clipboard section is the resting state while a
    /// copy is live. All the phase maths is in <see cref="ClipboardSection"/> (Core).
    /// </para>
    /// </summary>
    private void ApplyClipboardSectionMorph(double t)
    {
        if (_sectionDir == 0) return;
        ApplyClipboardSection(t);
    }

    /// <summary>
    /// Run the capsule out by <see cref="OverlayTokens.ClipboardSectionW"/> on its TRAILING end
    /// and fade the clipboard preview in.
    /// <para>
    /// The growth goes on the far end only and the section is anchored to that same end, so the
    /// capsule's near edge — the one the eye uses to locate the island — never moves. That is
    /// what keeps "the island is static" (spec §Окно) true while the capsule is longer. The
    /// extra length is subtracted back off everywhere the island is MEASURED:
    /// <see cref="PlaceIsland"/> and the click zones (else the ⅓/⅓/⅓ split would slide while the
    /// section is up).
    /// </para>
    /// </summary>
    private void ApplyClipboardSection(double t)
    {
        var peek = ClipboardSectionTrack.CapsuleLongAt(t, _sectionBase) - _sectionBase;
        var reduced = AnimReduced.Resolve(OsAnimationsEnabled(), _settings.ReducedMotion);
        var opacity = AnimEase.WithReducedMotion(ClipboardSectionTrack.OpacityAt(t), reduced);
        ApplySectionFrame(peek, opacity);
    }

    /// <summary>
    /// Write one complete frame of the clipboard section: the length it occupies, its opacity,
    /// the seam and content placement, the hold that keeps the island's own content centred in
    /// the ORIGINAL box, and the capsule length that carries all of it.
    /// <para>
    /// FIX: the morph and the resting state used to write this independently, and only the morph
    /// wrote the geometry. The rest wrote just <c>IsVisible</c>/<c>Opacity</c> and the pill
    /// length — so on the very first frame after a fresh attach (or after any ApplySize that
    /// settled), <c>ClipboardSection</c> still had the geometry of the PREVIOUS frame: a
    /// collapsed size of 0 or the trailing margin of an old peek, plus
    /// <c>CollapsedRow.Margin = 0</c> instead of the hold. The section was then laid out
    /// right-aligned inside a capsule that has no room for it, and the island's own content
    /// re-centred on the longer capsule. One writer, one frame, no second opinion.
    /// </para>
    /// </summary>
    private void ApplySectionFrame(double peek, double opacity)
    {
        var vertical = SplitIsVertical;

        ClipboardSection.IsVisible = peek > 0.5;
        ClipboardSection.Opacity = opacity;
        // Clip the preview by the capsule's current length, on whichever axis is the long one.
        // The trailing inset is the capsule's corner radius, so the text stops before the curve
        // instead of running over it.
        if (vertical)
        {
            ClipboardSection.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            ClipboardSection.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            ClipboardSection.Margin = new Thickness(0);
            ClipboardSection.Width = double.NaN;
            ClipboardSection.Height = Math.Max(0, peek);
            // 1.18: vertical capsule → the section grows up from the bottom edge, so the seam
            // is the BOTTOM edge and its hairline lies horizontally. Getting this backwards
            // would draw a vertical rule across a horizontal join.
            ClipboardSectionSeam.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            ClipboardSectionSeam.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            ClipboardSectionSeam.Width = double.NaN;
            ClipboardSectionSeam.Height = 1;
            ClipboardSectionSeam.Margin = new Thickness(0, 0, 0, 7);
            ClipboardSectionContent.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            ClipboardSectionContent.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            ClipboardSectionContent.Margin = new Thickness(0);
        }
        else
        {
            ClipboardSection.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            ClipboardSection.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            ClipboardSection.Margin = new Thickness(0, 0, ClipboardSectionTrack.CapInsetFor(OverlayTokens.CollapsedH), 0);
            ClipboardSection.Width = Math.Max(0, peek);
            ClipboardSection.Height = double.NaN;
            // 1.18: horizontal capsule → the section sits at the trailing end and the seam is
            // its LEFT edge. The hairline is inset 10 DIP from the join and the content starts
            // 18 DIP in, so the clock/date on the capsule's leading side are never covered.
            ClipboardSectionSeam.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            ClipboardSectionSeam.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            ClipboardSectionSeam.Width = 1;
            ClipboardSectionSeam.Height = double.NaN;
            ClipboardSectionSeam.Margin = new Thickness(10, 0, 0, 0);
            ClipboardSectionContent.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            ClipboardSectionContent.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            ClipboardSectionContent.Margin = new Thickness(14, 0, 0, 0);
        }

        // FIX: the `if (Math.Abs(peek - _sectionPeek) < 0.01) return;` short-circuit that used
        // to sit here is gone. It skipped the capsule-length write below whenever the section's
        // length had not CHANGED — but the pill's length is reset to the island's own by
        // SetSizeImmediate on every plain ApplySize, and nothing rewrote it when the section
        // itself had not moved. That is precisely the reported symptom: section out, peek = 110,
        // capsule 294 instead of 404. Re-writing an identical value every frame is cheap; the
        // "nothing changed, so skip the write" optimisation was the bug.
        _sectionPeek = peek;

        // Keep the island's OWN content where it is. CollapsedRow and SecondsStrip are
        // centre-aligned in the capsule, so a capsule 110 DIP longer would slide both 55 DIP
        // along the long axis — the capsule's edge would hold still while its contents crawled
        // out from under it, which is exactly the "the island moves" the spec rules out. A
        // trailing margin of the section length takes that room back on the growing side, so the
        // content keeps centring in the ORIGINAL 170 DIP box.
        //
        // FIX: the hold has to cover the section's own trailing inset, not just its length. The
        // section is right-aligned with a CapInsetFor(CollapsedH) margin on the trailing end, so
        // it actually begins at pill - inset - peek. A hold of plain peek therefore let the
        // island's date and weather run the last `inset` DIP under the section's leading edge —
        // the seam hairline and the section's text were drawn straight over them. The row's
        // right edge now stops where the section's left edge actually is.
        var inset = vertical
            ? 0
            : ClipboardSectionTrack.CapInsetFor(OverlayTokens.CollapsedH);
        var holdLength = peek + inset;
        var hold = new Thickness(0, 0, vertical ? 0 : holdLength, vertical ? holdLength : 0);
        CollapsedRow.Margin = hold;
        SecondsStrip.Margin = vertical
            ? new Thickness(10, 0, 10, holdLength + 2)
            : new Thickness(10, 0, 10 + holdLength, 2);
        // Grow the capsule on its long axis FROM the morph's own base, not from the previous
        // frame: OnMorphTick has already written this frame's interpolated capsule length, and
        // for an attach that base is the settled 170. Accumulating onto the live value would
        // let any unrelated interpolation leak into the section.
        // The pill is anchored to the window's near edge, so the extra length appears on the
        // trailing side without moving the near edge.
        if (vertical) Pill.Height = _sectionBase + peek;
        else Pill.Width = _sectionBase + peek;
    }

    /// <summary>Capsule length WITHOUT the clipboard section — what the island and its hit
    /// zones are measured against.</summary>
    private double IslandLongAxis() => PillLongAxis() - _sectionPeek;

    /// <summary>
    /// The capsule's size as the island knows it: its live size minus the clipboard section,
    /// with the window as the fallback when the pill has no size yet. One place, so the
    /// placement, the click zones and the drawer's long axis cannot disagree about how long the
    /// island is.
    /// <para>
    /// 1.14: the section is subtracted here exactly the way the old phase-A peek was. It is a
    /// compartment of the capsule, not a separate window that has to be accounted for, so what
    /// the island is measured against is unchanged by this workstream — only the thing being
    /// added to the far end of the capsule is different.
    /// </para>
    /// </summary>
    private (double Long, double Cross) IslandCapsuleSize() =>
        IslandCapsuleSizeFor(Pill.Width, Pill.Height);

    /// <summary>
    /// Long/cross extents of a capsule of the given size, with the clipboard section removed
    /// from the long axis. The cross axis is never touched — the section does not grow it, and
    /// the drawer is accounted for separately as window space, not as capsule space.
    /// </summary>
    private (double Long, double Cross) IslandCapsuleSizeFor(double pillW, double pillH)
    {
        var w = SplitIsVertical ? pillW : pillW - _sectionPeek;
        var h = SplitIsVertical ? pillH - _sectionPeek : pillH;
        if (w <= 0) w = Width;
        if (h <= 0) h = Height;
        return SplitIsVertical ? (h, w) : (w, h);
    }

    /// <summary>
    /// Land the clipboard section on its defined resting state and clear the transition.
    /// Attached → the section is fully out and the capsule is ClipboardSectionW longer;
    /// detached → the section is gone and the capsule is exactly its own length. This is the
    /// ONLY place a clipboard morph is allowed to end, which is what guarantees no residual
    /// length or margin survives into the next attach.
    /// </summary>
    private void SettleClipboardSection(double ownW, double ownH)
    {
        // FIX: this used to be guarded by `if (_sectionDir == 0 && _splitApplied) return;`,
        // which made the resting capsule length unreachable. Both callers land here immediately
        // after SetSizeImmediate put the pill at the island's OWN length, and the guard skipped
        // the one call that puts the section's resting length back — so any ordinary ApplySize
        // (the 200 ms tick, a kind change, a hover change) left a 294 DIP capsule carrying a
        // fully-out 110 DIP section: the section was visible, the window was sized for it, and
        // the capsule simply had no room for it. The resting state has to be re-appliable at
        // any time, not only on the frame a transition ends.
        //
        // The base is taken from the island's OWN length as the caller computed it (w, h) —
        // deliberately NOT from the live pill. The live pill is exactly the value this method
        // exists to correct, so reading it back would re-derive base = pill - peek and put the
        // capsule back at the wrong length instead of the right one.
        _sectionBase = SplitIsVertical ? ownH : ownW;
        ApplySectionRest(_splitApplied);
        _sectionDir = 0;
        // The window is sized from the same pair, and WindowFor reads _sectionPeek — which the
        // frame above has just changed. Re-deriving it here means a retract does not leave the
        // window 110 DIP wider than its contents until the next tick corrects it. The window
        // goes FIRST because PlaceIsland positions the window itself.
        var (ww, wh) = WindowFor(_splitApplied, ownW, ownH);
        Width = ww;
        Height = wh;
        // Both callers have just resized the pill through SetSizeImmediate, which placed the
        // island from a capsule that did not yet carry the section. The placement is derived
        // from pill - _sectionPeek (see IslandCapsuleSizeFor), so re-seat it against the settled
        // capsule — otherwise the island sits up to 110 DIP off until some later tick happens to
        // call PlaceIsland, which is what makes a settled section look like it nudged the island.
        PlaceIsland();
    }

    /// <summary>Write one well-defined resting frame of the clipboard section.</summary>
    private void ApplySectionRest(bool attached)
    {
        // At rest the section is either fully out or fully gone, by definition. Left at a
        // partial length, a later ApplySize would find a capsule that is not any of the sizes
        // the island actually asks for.
        //
        // FIX: the resting state is now produced by the SAME writer the morph uses, so the two
        // cannot disagree. It used to re-implement a third of the frame — IsVisible/Opacity plus
        // the pill length — and then explicitly CLEAR the hold margins
        // (`CollapsedRow.Margin = 0`) "because the section is no longer growing". That reasoning
        // was wrong: the hold is not about motion, it is about centring. A resting capsule that
        // carries a 110 DIP section is 404 DIP long, and its centre is 55 DIP further along the
        // long axis than the island's 294 DIP box. Clearing the hold at rest slid the clock and
        // date out from under the section by exactly that 55 DIP — the section was up, the
        // window was right, and the island's own content had quietly walked.
        ApplySectionFrame(attached ? ClipboardSectionTrack.Width : 0, attached ? 1 : 0);
        ApplySeamRadii();
    }

    /// <summary>
    /// The seam. While the drawer is out, the capsule's two corners on the seam go square and
    /// the drawer's two matching corners go square, so the pair reads as one object with a lid
    /// rather than two capsules that happen to be touching. The maths is in
    /// <see cref="ClipboardDrawer"/> (Core); this only composes it into the two shapes, and
    /// only when the drawer is actually open.
    /// </summary>
    private void ApplySeamRadii()
    {
        var capsuleRadius = Math.Min(Pill.Width, Pill.Height) / 2;
        if (Pill.Width <= 0 || Pill.Height <= 0) return;
        var joined = _historyOpen && _historyRowCount > 0;
        var dir = DrawerCrossDirection;
        var c = ClipboardDrawer.CapsuleRadiiFor(capsuleRadius, joined, dir, SplitIsVertical);
        Pill.CornerRadius = new CornerRadius(c.TopLeft, c.TopRight, c.BottomRight, c.BottomLeft);
        if (!joined) return;
        var d = ClipboardDrawer.DrawerRadiiFor(joined, dir, SplitIsVertical);
        HistoryPanel.CornerRadius = new CornerRadius(d.TopLeft, d.TopRight, d.BottomRight, d.BottomLeft);
    }

    /// <summary>Seam-aware corner radius for the capsule, for the paths that write it directly.</summary>
    private CornerRadius SeamCapsuleRadii(CornerRadius fallback, double capsuleRadius) =>
        _historyOpen && _historyRowCount > 0
            ? ApplySeamCapsuleRadius(capsuleRadius)
            : new CornerRadius(capsuleRadius);

    private CornerRadius ApplySeamCapsuleRadius(double capsuleRadius)
    {
        var c = ClipboardDrawer.CapsuleRadiiFor(
            capsuleRadius, joined: true, DrawerCrossDirection, SplitIsVertical);
        return new CornerRadius(c.TopLeft, c.TopRight, c.BottomRight, c.BottomLeft);
    }


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
        // 1.14: the clipboard SECTION's own motion, driven from the same morph <c>t</c> as the
        // capsule and the window so the island growing and the clipboard appearing are one
        // movement. Runs before the notify-style branch because a clipboard morph is not a
        // notify morph — the section has to animate for both, and the early return below would
        // otherwise skip it.
        ApplyClipboardSectionMorph(rawT);
        // 1.14 clipboard history drawer. Same tick, same progress: the window around the drawer
        // is already morphing, so the drawer's slide and fade ride that t rather than a timer
        // of their own. The seam's corner radii move with it — they are part of the same
        // "joined" state, not a separate effect.
        if (_historyDir != 0)
        {
            ApplyDrawerAnim(rawT);
            ApplySeamRadii();
        }
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
        // 1.14: the window is the capsule on the long axis and capsule+drawer on the cross
        // axis — nothing more. Everything the island is — the edge anchor, the user's offsets,
        // the click zones, the hit region — is defined against the CAPSULE, so Place() is still
        // given the capsule's pixel size and the window is then re-seated around that spot.
        // Doing it the other way round (placing the window and letting the capsule ride along)
        // is what would make the island jump sideways every time the drawer opened. This runs
        // on every morph frame, so the capsule holds its screen position during the morph too,
        // not just at rest.
        // The SECTION-FREE capsule size, as a (w, h) pair: the clipboard section makes the
        // capsule longer, and feeding that longer length to IslandLayout.Place would move the
        // window — and with it the island — 55 DIP for the duration of the preview. The spec's
        // "the island does not move" has to be measured against the settled length, not the
        // temporary one.
        //
        // IslandCapsuleSize() answers in LONG/CROSS, which is not the same as width/height once
        // the island is vertical: there the long axis is the height. ScreenSizeFor does that one
        // rotation, so Place() always gets a real (w, h) and the Right/Left edges seat the island
        // against the edge by its 30 DIP thickness instead of by its 384 DIP length.
        var (homeLong, homeCross) = IslandCapsuleSize();
        var (homeW, homeH) = IslandLayout.ScreenSizeFor(homeLong, homeCross, SplitIsVertical);
        var homePw = (int)Math.Round(homeW * scale);
        var homePh = (int)Math.Round(homeH * scale);
        var (x, y) = IslandLayout.Place(
            wa.X, wa.Y, wa.Width, wa.Height, homePw, homePh,
            _settings.Edge, _settings.OffsetX, _settings.OffsetY);
        // 1.14: NO CLAMP, and that is the fix rather than an omission.
        //
        // 1.13 had to clamp here, because the window was then inflated by BlobDragMaxPx of
        // invisible drag room on every side plus a whole detached ball — and because Place
        // clamps the CAPSULE while that oversized window was re-seated around it, the visible
        // content could walk past the screen edge. The user reported exactly that: the clipboard
        // panel and the ball running off-screen, and a clamp that "fixed" it by moving the
        // island, which the spec forbids.
        //
        // With the ball and its drag there is no slack to allow and nothing to clamp: the window
        // is exactly the capsule, or the capsule plus the drawer it is actually showing, and the
        // drawer only ever grows TOWARDS the screen interior (see ClipboardDrawer.CrossDirectionFor).
        // So Place() clamping the capsule already guarantees the whole window is on screen, and
        // the island keeps its position. Adding a clamp back here would reintroduce exactly the
        // jump this workstream is fixing.
        if (_splitApplied || _sectionDir != 0)
        {
            // The window's cross origin sits from the capsule's by the drawer's own extent; the
            // long axis is copied verbatim, so the capsule cannot move on the axis it is anchored to.
            var crossShift = _historyOpen && _historyRowCount > 0
                ? ClipboardDrawer.CrossShiftDipFor(_settings.Edge, _historyRowCount) * scale
                : 0.0;
            (x, y) = IslandLayout.DrawerWindowFor(SplitIsVertical, x, y, crossShift, scale);
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

        // 1.14: the clipboard SECTION. Its visibility is owned by the section transition
        // (SettleClipboardSection / ApplyClipboardSectionMorph), not by this paint: the capsule
        // is already growing while the section is still held back, so flipping it here would pop
        // it in one frame early. All Paint does is refill the section's content on the same UI
        // turn as the capture that produced it.
        // The island itself needs no compensation any more: the capsule only ever grows on its
        // trailing end, so CollapsedRow and SecondsStrip just centre themselves in it.
        if (snap.IsSplitClipboard) ApplyClipboardSectionContent(snap.SplitClipboard);
        // 1.12.4: only claim these margins at rest. During phase A the morph owns them — it
        // holds the island's rows in the original 170 DIP box while the capsule is longer, and a
        // Paint landing mid-morph (Paint runs on every kind change) would otherwise yank the
        // rows 55 DIP outwards for a frame. At rest _sectionPeek is 0, so this is the old margin.
        if (_sectionPeek <= 0)
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
        StageStatsReveal(statsVisible, kind);
        // The running caption lives inside the monitor, so its visibility follows the panel's —
        // decided here because this is the only place the panel's own visibility is set.
        SyncMarqueeVisibility();

        var showMinimalWx = !overlayOn && snap.WeatherEnabled;
        MinimalWeather.IsVisible = showMinimalWx;
        if (showMinimalWx)
        {
            var wx = snap.LastWeather;
            // Same story as the kind icon: the text and the tooltip only change when the reading
            // or the location does, so re-formatting and re-setting them five times a second
            // bought nothing. ToolTip.SetTip in particular re-arms Avalonia's tooltip timer.
            var tempText = WeatherCodes.FormatMinimalTemp(wx.TemperatureC ?? 18);
            if (!string.Equals(tempText, _lastWxTempText, StringComparison.Ordinal))
            {
                WeatherTempText.Text = tempText;
                _lastWxTempText = tempText;
            }

            SetWeatherIcons(WeatherCodes.IconKey(wx.WeatherCode ?? 0), animate: true);

            var wxTip = _settings.WeatherLocationMode == WeatherLocationMode.Manual
                && !string.IsNullOrWhiteSpace(_settings.WeatherLocationName)
                    ? $"{_settings.WeatherLocationName.Trim()} · температура — из Windows; название локации — выбранное"
                    : "Погода Windows";
            if (!string.Equals(wxTip, _lastWxTip, StringComparison.Ordinal))
            {
                ToolTip.SetTip(MinimalWeather, wxTip);
                _lastWxTip = wxTip;
            }
        }

        var showBat = !overlayOn && _settings.ShowBatteryInCollapsed
            && _lastPower is { HasBattery: true };
        MinimalBattery.IsVisible = showBat;
        if (showBat)
        {
            var pct = _lastPower!.Percent;
            if (BatteryPercentText.Text != $"{pct}%")
                BatteryPercentText.Text = $"{pct}%";

            var charging = _lastPower.IsCharging || _lastPower.OnAc;
            var batKey = charging ? "bolt" : "battery";
            var batSize = CurrentIconCollapsed();
            if (BatteryIconHost.Child is null
                || !string.Equals(batKey, _lastBatIconKey, StringComparison.Ordinal)
                || !string.Equals(_settings.IconPack, _lastBatIconPack, StringComparison.Ordinal)
                || !batSize.Equals(_lastBatIconSize)
                || !_inkSecondary.Equals(_lastBatIconInk))
            {
                var batBrush = new SolidColorBrush(_inkSecondary);
                BatteryIconHost.Child = IconPackService.Create(_settings.IconPack, batKey, batSize, batBrush);
                _lastBatIconKey = batKey;
                _lastBatIconPack = _settings.IconPack;
                _lastBatIconSize = batSize;
                _lastBatIconInk = _inkSecondary;
            }

            var batTip = charging ? $"Зарядка · {pct}%" : $"Батарея · {pct}%";
            if (!string.Equals(batTip, _lastBatTip, StringComparison.Ordinal))
            {
                ToolTip.SetTip(MinimalBattery, batTip);
                _lastBatTip = batTip;
            }
        }

        // Stage 8: title and body are two texts with two inks, not one "title · body" string.
        // Split() keeps the payload's own Subtitle fallback in one tested place; the kind-specific
        // wording below (weather expansion, battery percentage) then overrides it exactly as
        // before, so no existing text changed meaning — only how it is laid out.
        var (titleText, bodyText) = NotificationLayout.Split(p, Fallback(kind),
            joinHeadline: kind is OverlayKind.Notification or OverlayKind.Expanded);
        if (kind == OverlayKind.Weather)
        {
            titleText = string.IsNullOrWhiteSpace(p.Body)
                ? WeatherCodes.FormatExpanded(p.TemperatureC ?? snap.LastWeather.TemperatureC ?? 18,
                    p.WeatherCode ?? snap.LastWeather.WeatherCode ?? 0, p.PrecipProb ?? snap.LastWeather.PrecipProb)
                : NotificationLayout.Collapse(p.Body);
            bodyText = _settings.WeatherLocationMode == WeatherLocationMode.Manual
                && !string.IsNullOrWhiteSpace(_settings.WeatherLocationName)
                ? _settings.WeatherLocationName.Trim()
                : "";
        }
        else if (kind == OverlayKind.Battery && bodyText.Length == 0)
        {
            bodyText = $"{(int)Math.Round(p.Progress * 100)}%";
        }
        // 1.13: the Timer and Progress branches are gone — neither is a capsule kind any
        // more, so neither ever reaches this point. Their text is formatted by the monitor
        // rows (ApplyTimerRow / ApplyMediaRow) and their fraction by CapsuleProgressBand.

        var notifLike = kind is OverlayKind.Notification or OverlayKind.Expanded or OverlayKind.Error;
        // Stage 8: the action is an existing command (OverlayCommand.Clear), shown only while the
        // pointer is on the capsule and there is actually something to clear. Its width is taken
        // out of the text budget BEFORE measuring, so revealing it cannot reflow the message.
        var notifActions = notifLike && _pointerOverUi && snap.UnreadCount > 0;
        var (titleW, bodyW) = NotificationLayout.SplitWidths(
            Math.Max(0, Pill.Bounds.Width - 2 * OverlayPanel.Margin.Left - 26 /* icon + gap */ - 8),
            notifActions,
            bodyText.Length > 0);

        OverlayTitle.Text = titleText;
        _titleBudget = titleW;
        // The action cluster was visible or not in this same frame, and SplitWidths below has to
        // be told the same thing. See ClampTitleToColumn.
        _notifActionsShown = notifActions;
        OverlayTitle.MaxWidth = titleW;
        OverlaySubtitle.Text = bodyText;
        // The body sits in the text grid's star column, so the layout gives it exactly the room
        // that is left and its ellipsis engages there. The computed budget is only an upper
        // bound: Pill.Bounds is the PREVIOUS frame's width mid-morph and includes a settled
        // clipboard section, so trusting it alone let the body overrun the column and be cut by
        // ClipToBounds with no ellipsis at all.
        OverlaySubtitle.MaxWidth = bodyW > 0 ? bodyW : double.PositiveInfinity;
        OverlaySubtitle.IsVisible = bodyText.Length > 0;
        ClampTitleToColumn();
        // 1.15: the bell and the scrolling body are decided here, after the final texts are set,
        // because the badge answers to the KIND and the marquee to the final string — neither is
        // something SplitWidths above knows anything about.
        SyncNotifyBell(kind, notifLike);
        SyncBodyMarquee(bodyText, notifLike);

        // 1.13: the capsule's bottom 8 DIP are one shared band. Whoever owns it draws, and
        // the other one yields — seconds digits and a progress bar are two readings of the
        // same strip, and both at once reads as a glitch rather than as two things happening.
        ApplyProgressBand();

        var textPrimary = ParseColor(_settings.ColorTextPrimary, OverlayTokens.TextHex);
        var accent = ParseColor(_settings.ColorAccent, OverlayTokens.AccentHex);
        var errorInk = ParseColor(OverlayTokens.ErrorHex, OverlayTokens.ErrorHex);
        OverlayTitle.Foreground = CachedBrush(kind == OverlayKind.Error ? errorInk : textPrimary);
        // Stage 8: the body is the same information the title used to carry in the title's own
        // weight. Dropping it to the secondary ink is what creates the hierarchy inside a 30 DIP
        // row: the eye reads the name first and the detail second, without a second line and
        // without a second card.
        OverlaySubtitle.Foreground = CachedBrush(_inkSecondary);
        AppIcon.Background = CachedBrush(kind == OverlayKind.Error ? errorInk : accent);
        UnreadBadge.Background = CachedBrush(accent);
        // The action is destructive, so it wears the existing error colour — but on the LABEL,
        // not as a filled red chip, which at this size would read as the primary control.
        //
        // 2026-10-02: it wore the raw error hex, which bypasses the contrast guard Stage 7 put
        // on every other ink in the capsule. #E8A0A0 on the light theme's #F5F5F7 wall is about
        // 1.9:1 — the cancel glyph was technically present and practically unreadable, and the
        // Stage 7 palette held #23232A only for the PRIMARY, so the rule was satisfied on paper
        // while this label stayed outside it. GuardedIconInk keeps the destructive red wherever
        // it is actually readable and drops to the capsule's own ink where it is not.
        NotifActionClearText.Foreground = CachedBrush(ParseColor(
            ThemePresets.GuardedIconInk(
                _settings.ColorCapsuleFill, OverlayTokens.ErrorHex, _inkSecondaryHex),
            OverlayTokens.ErrorHex));
        NotifActionClear.IsVisible = notifActions;
        NotifActionClear.IsHitTestVisible = notifActions;

        if (kind == OverlayKind.Weather)
            SetKindIconWeather(p.WeatherCode ?? snap.LastWeather.WeatherCode ?? 0);
        else
            SetKindIcon(kind);

        // 1.13: the artwork and both control clusters left the capsule. They are drawn in the
        // Плеер and Таймер monitor rows now (StatsRowView.SetStatus), so the capsule only ever
        // carries the icon, the title and the progress band.
        ApplyMediaArtwork(null);

        var unread = snap.UnreadCount;
        // Stage 8: the badge and the action share the trailing edge and swap in place. A hovered
        // notification shows the action, because it is the only way to clear from the capsule
        // itself; the count is still on screen a moment later, and the count is also kept in the
        // collapsed dot, so nothing is ever "lost" by the swap.
        var showBadge = overlayOn && unread > 0 && kind is OverlayKind.Notification or OverlayKind.Expanded;
        UnreadBadge.IsVisible = showBadge && !notifActions;
        // While a run is being replayed, the badge says WHICH one is on screen rather than how many
        // are waiting: "1/3" says the capsule is working through a burst, where a plain "3" is just
        // a count the user has already seen and cannot act on. With nothing waiting the unread
        // count is the more useful thing, so it stays.
        var progress = _machine.NotificationProgress;
        BadgeText.Text = progress.Length > 0
            ? progress
            : unread > 99 ? "99+" : unread.ToString(CultureInfo.InvariantCulture);

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

        StageArrival(kind, overlayOn);
    }

    /// <summary>
    /// Stage the arrival reaction for whatever just took the surface (spec Этап 5, §8).
    /// <para>
    /// A notification arrives in the order the eye reads it — the icon identifies WHO, the title
    /// says WHAT, and only then is there anything left to say — so those two are staged. The
    /// badge is a count, not a caption: it arrives with the title rather than after it, because a
    /// badge that lands last reads as a separate event that the user did not cause.
    /// </para>
    /// <para>
    /// Keyed on the kind CHANGING, not on Paint running: Paint also runs on every system-monitor
    /// sample and every clipboard capture, and re-staging on those would make a notification
    /// restart its arrival animation while the user is reading it.
    /// </para>
    /// </summary>
    private void StageStatsReveal(bool statsVisible, OverlayKind kind)
    {
        if (!statsVisible)
        {
            if (_statsWasVisible)
            {
                _reveal?.Finish();
                _reveal = null;
            }
            _statsWasVisible = false;
            return;
        }

        if (kind != OverlayKind.SystemStats || _statsWasVisible) return;
        _statsWasVisible = true;
        if (_statsRows.Count == 0) return;

        // Quiet, not Medium: nothing in a list of readings is an event. With StaggerShare 0 at
        // the quiet level this is ONE soft fade over the whole panel, which is the honest read of
        // "the monitor opened" — a cascade here would imply the rows are arriving one after
        // another, and they are not, they were all already there.
        PlayReveal(ReactionLevel.Quiet, [.. _statsRows.Select((r, i) => ((Avalonia.Controls.Control)r, i))]);
    }

    private void StageArrival(OverlayKind kind, bool overlayOn)
    {
        if (!overlayOn)
        {
            if (_lastArrivalKind != kind)
            {
                // Leaving an overlay must not leave half a reveal holding the surface at 0.3.
                _reveal?.Finish();
                _reveal = null;
            }
            _lastArrivalKind = kind;
            return;
        }

        if (kind == _lastArrivalKind) return;
        var isNew = _lastArrivalKind is OverlayKind.Idle or OverlayKind.Collapsed;
        _lastArrivalKind = kind;
        if (!isNew) return;

        // 1.19 (spec Этап 5, §1, §10). Intensity is a property of WHAT ARRIVED, not of how it
        // was delivered, so the default is read from the kind: only a notification, a failure and
        // the low-battery alert are strong — the things the user must not miss, and the only
        // ones that earn a flash. The charge pill is MEDIUM: plugging in the charger is good news,
        // but it is not an event worth a flash, and the spec asks for a "soft accent near the
        // battery" there, not a strong reaction.
        //
        // Everything else is QUIET. An earlier version defaulted to Strong for every unlisted
        // kind, which meant that opening the stats panel, a click expanding the capsule, the
        // weather and the clipboard all flashed the accent edge — the exact twitch the tier
        // system exists to prevent, and the commit's own "a CPU reading must not make the
        // surface twitch" said the opposite. New kinds therefore default to the quiet end: a
        // missing entry degrades to silence, not to noise.
        //
        // Stage 8 (§5) splits the two events Этап 5 had joined. An ORDINARY notification is now
        // MEDIUM — soft appearance, no accent flash, one unread emphasis — while Error keeps
        // Strong, its local error colour and the flash. This is the tier the spec asks for: the
        // common case must not cost the same attention as a failure, otherwise every toast looks
        // like an alarm and the strong tier stops meaning anything.
        var level = _arrivalLevelOverride ?? kind switch
        {
            OverlayKind.Error => ReactionLevel.Strong,
            OverlayKind.Notification => ReactionLevel.Medium,
            OverlayKind.Battery => ReactionLevel.Medium,
            _ => ReactionLevel.Quiet,
        };
        _arrivalLevelOverride = null;

        // Stage 8 (§4, step 6): the unread indicator joins the reveal as the LAST element. It
        // used to be outside the sequence entirely, so the count popped in fully opaque while
        // the text was still arriving; arriving last is what makes it read as the final beat of
        // one gesture instead of a fourth, unrelated event.
        PlayReveal(level, (AppIcon, 0), (OverlayTitle, 1), (OverlaySubtitle, 2), (UnreadBadge, 3));
        // The flash is the strong tier's one flash. It stays the accent for a low-battery alert
        // (a warning about the machine, not a failure) and takes the error colour only for an
        // actual Error, so "red" keeps meaning one thing.
        if (level == ReactionLevel.Strong)
            PlayAccentFlash(kind == OverlayKind.Error ? OverlayTokens.ErrorHex : OverlayTokens.AccentHex);
    }

    /// <summary>
    /// 1.14: fill the clipboard SECTION from the payload BuildPayload already produced — format
    /// icon per <see cref="ClipboardItemKind"/> plus the preview text. Both the icon key and the
    /// wording/truncation/empty-fallback rules live in
    /// <see cref="ClipboardHalfPreview"/> and are unit-tested there, so this stays a pure
    /// "put it on screen" step. The plural («5 файлов») is BuildPayload's, not ours.
    /// <para>
    /// 1.14: this writes the section only. The ball and the in-capsule peek that used to be
    /// filled here alongside it are gone, and the wheel cycle's
    /// <see cref="ApplyBallPreviewFromEntry"/> writes the same two fields from a different
    /// source — which is why they are named apart rather than sharing one method.
    /// </para>
    /// </summary>
    private void ApplyClipboardSectionContent(OverlayPayload cp)
    {
        var brush = new SolidColorBrush(ClipboardIconTint(cp.ClipboardItemKind));
        ClipboardSectionIcon.Child = IconPackService.Create(
            _settings.IconPack, ClipboardHalfPreview.IconKeyFor(cp.ClipboardItemKind),
            CurrentIconCollapsed(), brush);
        ClipboardSectionText.Text = ClipboardHalfPreview.TextFor(cp);
        ToolTip.SetTip(ClipboardSection, cp.ClipboardItemKind switch
        {
            ClipboardItemKind.Text => "Скопирован текст",
            ClipboardItemKind.File => "Скопирован файл",
            ClipboardItemKind.MultiFile => "Скопированы файлы",
            _ => "Буфер обмена — нажмите для истории",
        });
    }

    // The capsule's kind icon used to be rebuilt from scratch on every Paint, and Paint runs on
    // the 200 ms tick — so five times a second the icon control was replaced (which re-measures
    // the capsule), a brush was allocated and the icon was re-resolved, all to draw the same
    // picture. These four fields are the "did anything that matters actually change" check for
    // that one call. The ink is part of the check on purpose: a theme change MUST rebuild the
    // icon, which the weather icon's key-only guard (SetWeatherIcons) does not handle.
    private string _lastKindIconKey = "";
    private string _lastKindIconPack = "";
    private double _lastKindIconSize;
    private Color _lastKindIconInk;

    private void SetKindIcon(OverlayKind kind) => ApplyKindIcon(IslandIcons.KindKey(kind));

    private void SetKindIconWeather(int code) => ApplyKindIcon(WeatherCodes.IconKey(code));

    private void ApplyKindIcon(string key)
    {
        var size = CurrentIconKind();
        if (AppIconHost.Child is not null
            && string.Equals(key, _lastKindIconKey, StringComparison.Ordinal)
            && string.Equals(_settings.IconPack, _lastKindIconPack, StringComparison.Ordinal)
            && size.Equals(_lastKindIconSize)
            && _inkPrimary.Equals(_lastKindIconInk))
            return;

        var brush = new SolidColorBrush(_inkPrimary);
        AppIconHost.Child = IconPackService.Create(_settings.IconPack, key, size, brush, 1.5);
        _lastKindIconKey = key;
        _lastKindIconPack = _settings.IconPack;
        _lastKindIconSize = size;
        _lastKindIconInk = _inkPrimary;
    }

    private void SetWeatherIcons(string key, bool animate)
    {
        if (string.Equals(key, _lastWeatherIconKey, StringComparison.OrdinalIgnoreCase) && WeatherIconA.Child is not null)
            return;

        var brush = new SolidColorBrush(_inkSecondary);
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

    private static Avalonia.Controls.MenuItem Menu(string header, Action act)
    {
        var item = new Avalonia.Controls.MenuItem { Header = header };
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
    /// <see cref="ApplyClipboardSectionMorph"/> already uses for the section, reached through the
    /// dictionary so the shape has exactly one implementation and cannot drift between call sites.
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

    /// <summary>
    /// Play the first-appear wobble: the small horizontal settle when the capsule comes back
    /// after being hidden. Implements the 1.12.0 tokens that shipped without an implementation.
    /// </summary>
    private void PlayFirstAppearWobble()
    {
        var speed = AnimationTiming.Effective(_settings.AnimationSpeed, _settings.AnimFirstAppearWobble);
        if (!AnimationTiming.IsEnabled(speed)) return;
        // Reduced motion: no settle. Same rule as the click pop — a quicker version of the same
        // movement is not what a user who cannot tolerate animation asked for.
        if (AnimReduced.Resolve(OsAnimationsEnabled(), _settings.ReducedMotion)) return;
        // The morph owns the translate for its whole run; a wobble on top would fight it.
        if (_morphActive) return;

        _firstAppearWobbleMs = AnimationTiming.ScaleMs(OverlayTokens.FirstAppearWobbleMs, speed);
        _firstAppearWobbleStartedAtMs = Environment.TickCount64;
        EnsureFrameTick();
    }

    /// <summary>One wobble frame; false once the wobble is over.</summary>
    private bool TickFirstAppearWobble()
    {
        if (_firstAppearWobbleStartedAtMs is not { } start) return false;
        // A morph took over the translate — drop the wobble rather than write over its frames.
        if (_morphActive)
        {
            _firstAppearWobbleStartedAtMs = null;
            return false;
        }

        var elapsed = Environment.TickCount64 - start;
        var offset = FirstAppearWobble.OffsetX(elapsed, _firstAppearWobbleMs, OverlayTokens.FirstAppearWobblePx);
        _pillTranslate.X = offset;
        if (elapsed < _firstAppearWobbleMs) return true;

        _firstAppearWobbleStartedAtMs = null;
        _pillTranslate.X = 0;
        return false;
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
        // 2026-10-02 perf pass: this used to memcmp the whole artwork on every call (five times
        // a second, from Paint) and then Clone it into _lastArtworkBytes. The media source
        // publishes one fresh array per thumbnail and reuses the same instance afterwards, so
        // reference equality answers "did the artwork change?" for free in the common case;
        // SequenceEqual stays as the fallback for a new-but-identical array. The reference is
        // only ever compared, never written through.
        if (_lastArtworkBytes is not null
            && (ReferenceEquals(_lastArtworkBytes, bytes)
                || bytes.AsSpan().SequenceEqual(_lastArtworkBytes)))
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
            _lastArtworkBytes = bytes;
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

    private void EnsureKeyboardLayoutSource()
    {
        if (_keyboardLayout is not null) return;
        try
        {
            _keyboardLayout = new WindowsKeyboardLayoutSource();
            _keyboardLayout.Changed += OnKeyboardLayoutChanged;
            _keyboardLayout.Start();
        }
        catch (Exception ex)
        {
            // Cosmetic. A missing language flag is not a reason for the island to be unhappy.
            AppLog.Warn("EnsureKeyboardLayoutSource failed", ex);
            _keyboardLayout = null;
        }
    }

    /// <summary>
    /// Flash the language badge for a couple of seconds. It is a caption, not a state: it takes
    /// no space in the FSM, disappears on its own, and never survives into the next layout.
    /// </summary>
    private void OnKeyboardLayoutChanged(KeyboardLayoutInfo info)
    {
        Dispatcher.UIThread.Post(() =>
        {
            KeyboardLayoutFlag.Text = info.Region;
            KeyboardLayoutName.Text = OneLine(info.Language);
            KeyboardLayoutBadge.IsVisible = info.Region.Length > 0 || info.Language.Length > 0;

            _keyboardLayoutHide?.Cancel();
            _keyboardLayoutHide = new CancellationTokenSource();
            var token = _keyboardLayoutHide.Token;
            _ = Task.Delay(TimeSpan.FromMilliseconds(KeyboardLayoutTag.TypingWindowMs), token)
                .ContinueWith(_ =>
                {
                    if (!token.IsCancellationRequested)
                        Dispatcher.UIThread.Post(() => KeyboardLayoutBadge.IsVisible = false);
                }, TaskScheduler.Default);
        });
    }

    private void EnsureNotificationSource()
    {
        if (_notifSource is not null) return;
        try
        {
            _notifSource = new WindowsNotificationSource(
                action => Dispatcher.UIThread.Post(action));
            _notifSource.Accepted += OnToastAccepted;
            // The platform's answer about our access is the one thing the user cannot see from
            // the island itself: nothing on the capsule changes when toasts stop arriving. The
            // tray tooltip is the only place that says so.
            _notifSource.AccessChanged += () => Dispatcher.UIThread.Post(UpdateTrayTooltip);
            _notifSource.Start();
        }
        catch (Exception ex)
        {
            // Never fatal: without a listener the island still shows the clock, the clipboard
            // and everything else. A notification source that cannot start is a missing feature,
            // not a broken app.
            AppLog.Warn("EnsureNotificationSource failed", ex);
            _notifSource = null;
        }
    }

    private void OnToastAccepted(IncomingToast toast)
    {
        // Already on the UI thread: WindowsNotificationSource posts through _postToUi before
        // raising Accepted, so there is nothing to marshal and no reason to defer — a toast
        // that waits for the next dispatcher turn is a toast the user already missed.
        var payload = NotificationFeed.ToPayload(toast);
        // One line per accepted toast: the listener has no other visible trace, and when a user
        // says "the island showed nothing", this is the difference between a guess and an answer.
        // A toast body is multi-line, and a multi-line message here would be written as several
        // log lines — which is exactly the format every other log reader here greps against.
        AppLog.Info($"Toast accepted: {OneLine(payload.Title)} | {OneLine(payload.Subtitle)} | " +
                    $"{OneLine(payload.Body)}");
        var before = _machine.Snapshot().Kind;
        _machine.Dispatch(OverlayCommand.Notify, payload);
        var after = _machine.Snapshot().Kind;
        if (before != after) OnKindChanged(before, after);
        ApplySize();
        Paint();
    }

    /// <summary>
    /// Re-derive the title's budget from the width the text column REALLY has. Runs from
    /// Paint and whenever the column is re-laid out (morph frames, section open/close), so the
    /// title never claims more than its share of the visible row.
    /// </summary>
    private void ClampTitleToColumn()
    {
        var w = OverlayTextColumn.Bounds.Width;
        if (w <= 0) return;
        // 2026-10-02: this passed a hardcoded `false` for the action cluster, while Paint passed
        // the real one. On a real toast the cluster takes 86 of 277 px, so Paint budgeted 191 px
        // for the title and this instantly clamped it to 191 against a column that only has 205 —
        // the gap varied with the body text, and on a wide window the title was measured 23 px
        // narrow and ellipsised early while space sat empty beside it. Both calls now use the same
        // value, recorded by Paint in the frame it was computed for.
        var (share, _) = NotificationLayout.SplitWidths(w, _notifActionsShown, OverlaySubtitle.IsVisible);
        var cap = Math.Min(share, _titleBudget);
        if (Math.Abs(OverlayTitle.MaxWidth - cap) > 0.5) OverlayTitle.MaxWidth = cap;
    }

    private double _titleBudget = double.PositiveInfinity;
    private bool _notifActionsShown;

    // -- 1.15: notification bell badge + scrolling body ---------------------------------
    // The body scrolls on a SEPARATE transform from the monitor caption's (_marqueeShift): the
    // two move at different times over different widths, and one shared transform would mean each
    // restarting the other's motion.
    private readonly TranslateTransform _bodyMarqueeShift = new();
    /// <summary>
    /// The bell ring's scale. Declared here rather than as an x:Name in the XAML because the
    /// Avalonia 11.3 source generator does not register fields for a name on a transform child of
    /// a RenderTransform property — the same trap the marquee shift documents.
    /// </summary>
    private readonly ScaleTransform _bellRingScale = new(1, 1);
    private double _bodyMarqueeElapsedMs;
    /// <summary>Text the body is currently scrolling, so a NEW notification resets the phase.</summary>
    private string _bodyMarqueeText = "";
    private bool _bodyMarqueeActive;
    private double _bellPulseMs;
    private string _lastBellGlyphKey = "";

    /// <summary>
    /// Show the bell badge for this kind and give its ring a fresh pulse.
    /// <para>
    /// The pulse restarts on every ARRIVAL rather than on every Paint — Paint runs five times a
    /// second, and resetting the phase there would hold the ring at its smallest size forever.
    /// The visibility guard is what tells an arrival from a repaint: a second toast of the same
    /// kind is still a second event, and the badge has to react to it.
    /// </para>
    /// </summary>
    private void SyncNotifyBell(OverlayKind kind, bool notifLike)
    {
        var want = notifLike && NotificationMarquee.BellVisible(kind);
        if (want && !NotifyBellBadge.IsVisible) _bellPulseMs = 0;
        NotifyBellBadge.IsVisible = want;
        if (!want)
        {
            _lastBellGlyphKey = "";
            return;
        }

        // The glyph is a fixed shape at a fixed size, so it is built once per (kind, ink) pair.
        // Rebuilding the geometry five times a second is what the kind-icon path above stopped
        // doing, for exactly this reason.
        var key = "bell:" + (kind == OverlayKind.Error ? "e" : "n") + _inkPrimary;
        if (string.Equals(key, _lastBellGlyphKey, StringComparison.Ordinal)) return;
        NotifyBellGlyph.Child = IslandIcons.Create("notify", 9, new SolidColorBrush(_inkPrimary), 2.0);
        _lastBellGlyphKey = key;
    }

    /// <summary>
    /// Decide whether the body scrolls, and in which of its two looks.
    /// <para>
    /// The measurement is the part worth writing down. A text block with
    /// <c>CharacterEllipsis</c> reports the width it was GIVEN, not the width of its string, so
    /// asking a trimming block how wide its text is always answers "the column" — the marquee
    /// could then never start. Clearing the trimming first makes the measurement honest; layout
    /// has not run again yet, so this reads the string's own width, and the visible edge still
    /// comes from the column's ClipToBounds.
    /// </para>
    /// </summary>
    private void SyncBodyMarquee(string bodyText, bool notifLike)
    {
        if (!notifLike || bodyText.Length == 0)
        {
            StopBodyMarquee();
            return;
        }

        OverlaySubtitle.TextTrimming = TextTrimming.None;
        OverlaySubtitle.MaxWidth = double.PositiveInfinity;
        var natural = OverlaySubtitle.Bounds.Width;
        var column = BodyColumnWidth();

        if (_settings.NotifyBodyMarquee
            && NotificationMarquee.ShouldScroll(natural, column, enabled: true))
        {
            // A different string is a different message: restart the travel, or the new text
            // would arrive mid-scroll and be unreadable at the very moment it appeared.
            if (!string.Equals(bodyText, _bodyMarqueeText, StringComparison.Ordinal))
            {
                _bodyMarqueeText = bodyText;
                _bodyMarqueeElapsedMs = 0;
                _bodyMarqueeShift.X = 0;
            }
            _bodyMarqueeActive = true;
            return;
        }

        StopBodyMarquee();
    }

    /// <summary>
    /// The room the body actually has: the text column minus the title and the gap between them.
    /// Both terms are read back from the laid-out controls rather than recomputed, because the
    /// star column is what actually gave the body its width and a second copy of that arithmetic
    /// is exactly the kind of thing that drifts.
    /// </summary>
    private double BodyColumnWidth()
    {
        var column = OverlayTextColumn.Bounds.Width;
        if (column <= 0) return 0;
        return Math.Max(0, column - OverlayTitle.Bounds.Width - NotificationLayout.TitleBodyGap);
    }

    private void StopBodyMarquee()
    {
        if (_bodyMarqueeActive) _bodyMarqueeShift.X = 0;
        _bodyMarqueeActive = false;
        _bodyMarqueeText = "";
    }

    /// <summary>
    /// One frame of the body's scroll, from the shared 200 ms tick.
    /// <para>
    /// It rides the 200 ms timer rather than the 33 ms frame clock on purpose. The text travels at
    /// 26 DIP/s, so 200 ms is 5.2 DIP — under a tenth of a glyph — while a 33 ms timer would keep
    /// the frame clock running for the whole notification window, which is precisely the idle CPU
    /// this project spent a week removing.
    /// </para>
    /// </summary>
    private void TickBodyMarquee(int deltaMs)
    {
        if (!_bodyMarqueeActive || !OverlaySubtitle.IsVisible) return;
        _bodyMarqueeElapsedMs += deltaMs;
        _bodyMarqueeShift.X = NotificationMarquee.OffsetFor(
            _bodyMarqueeElapsedMs, OverlaySubtitle.Bounds.Width, BodyColumnWidth());
    }

    /// <summary>
    /// One frame of the bell's ring, from the shared 33 ms clock.
    /// <para>
    /// Reports "not busy" under reduced motion, which is what lets the frame clock stop itself.
    /// The ring is left at its full size and simply does not move — reduced motion removes the
    /// movement rather than shortening it, so the badge still arrives, it just arrives quietly.
    /// </para>
    /// </summary>
    private bool TickBellPulse()
    {
        if (!NotifyBellBadge.IsVisible) return false;
        var speed = AnimationTiming.Effective(_settings.AnimationSpeed, _settings.AnimUnreadPulse);
        var reduced = AnimReduced.Resolve(OsAnimationsEnabled(), _settings.ReducedMotion);
        if (reduced || !AnimationTiming.IsEnabled(speed))
        {
            _bellRingScale.ScaleX = 1.0;
            _bellRingScale.ScaleY = 1.0;
            return false;
        }
        _bellPulseMs += OverlayTokens.CapsuleFrameTickMs;
        var period = AnimationTiming.ScaleMs(NotificationMarquee.BellPulsePeriodMs, speed);
        var s = NotificationMarquee.BellRingScale(_bellPulseMs, period, enabled: true);
        _bellRingScale.ScaleX = s;
        _bellRingScale.ScaleY = s;
        return true;
    }

    private static string OneLine(string? text) =>
        (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ⏎ ").Trim();

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
        // 1.14: clipboard-section preview cycle reset. A new capture always shows the
        // freshly-copied value first; the user can then wheel down to revisit older entries.
        // Spec §«Колесо на секции буфера».
        _ballPreviewIndex = 0;
        // 1.14: the rope pulse and the ball count badge are GONE with the ball — a pulse on a
        // rope that no longer exists, and a count on a ball that no longer exists. What a new
        // capture does instead is run the section's own growth, which ApplySize above already
        // started.
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
            : OverlayTokens.TextHex));

    /// <summary>Value colour: normal / attention / critical, low-is-bad metric (battery %).</summary>
    private static IBrush StatsBrushLow(double value, double warn, double crit) =>
        new SolidColorBrush(Color.Parse(
            value <= crit ? OverlayTokens.ErrorHex
            : value <= warn ? OverlayTokens.AccentHex
            : OverlayTokens.TextHex));

    /// <summary>Normal (never-accented) value colour — used for placeholders such as a missing battery.</summary>
    private static IBrush StatsBrushNormal() => new SolidColorBrush(Color.Parse(OverlayTokens.TextHex));

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
        if (!string.IsNullOrWhiteSpace(text) && MarqueeText.Text != text)
            MarqueeText.Text = text;
        else if (string.IsNullOrWhiteSpace(text))
        {
            MarqueeText.Text = "";
            _marqueeShift.X = 0;
        }
        SyncMarqueeVisibility();
    }

    /// <summary>
    /// Show the running caption only while the monitor is actually on screen (1.13.1).
    /// <para>
    /// It used to be gated on "there is text" alone. The Idle capsule is 30 DIP against a 14 DIP
    /// line with a 10 DIP bottom margin, so the caption drew straight across the clock — and
    /// inside the monitor it overlapped the last metric row. The panel height budget
    /// (<c>StatsMarquee</c>) already assumes the caption is PART of the monitor, so showing it
    /// outside contradicted the very budget that reserved room for it.
    /// </para>
    /// <para>
    /// Split out of <see cref="ApplyMarquee"/> because the panel's visibility is owned by
    /// <c>Paint</c>, which runs AFTER the media/timer row updates on the same tick. Deciding this
    /// inside the row update alone would read a one-frame-stale value and show the caption for a
    /// panel that just closed.
    /// </para>
    /// </summary>
    private void SyncMarqueeVisibility()
    {
        var shouldShow = SystemStatsPanel.IsVisible && MarqueeHasText();
        if (MarqueeHost.IsVisible != shouldShow)
        {
            MarqueeHost.IsVisible = shouldShow;
            if (!shouldShow) _marqueeShift.X = 0;
        }
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
        // 1.17: media only draws its own bar when it does NOT hold the capsule band.
        // When it does hold it, the bar at the capsule's bottom edge already says it.
        view.ShowRowProgress = CapsuleProgressBand.OwnerOf(_machine.BandState()) != ProgressBandOwner.Media;
        // Media changes the marquee source, so the panel's height grows and shrinks with it.
        SyncMarqueeState();

        // 1.19 (spec Этап 5, §5): a row that GAINS the player is a change of layer and gets a
        // medium reaction. A track change inside an already-playing session does not, and pause
        // does not: in both cases the row was already on screen, and re-revealing it would
        // restart the monitor for a change the user did not cause. The trigger is the
        // session's arrival, so it is the false→true edge and nothing else.
        if (active && !_mediaWasActive)
            PlayReveal(ReactionLevel.Medium, (view, 0));
        _mediaWasActive = active;
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
        // The low-battery alert arrives as a Notification, so the kind alone cannot tell it from
        // an ordinary one — it is declared here instead, at the one site that knows.
        _arrivalLevelOverride = ReactionLevel.Strong;
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
        // 1.17: same arbitration as the media row — one activity, one bar. The timer keeps
        // its digits either way; the bar is the part that was duplicated.
        if (_timerRowView is { } tv)
        {
            tv.ShowRowProgress = CapsuleProgressBand.OwnerOf(_machine.BandState()) != ProgressBandOwner.Timer;
            // 1.19 (spec Этап 5, §6): a row that GAINS the timer is a change of layer and gets a
            // medium reaction, same rule as the media row. Pause and resume keep the row on
            // screen and stay quiet — the digits changing is data, not an arrival, and a reveal
            // on every tick would make the row impossible to read.
            if (active && !_timerWasActive) PlayReveal(ReactionLevel.Medium, (tv, 0));

            // 1.19 (spec Этап 5, §6, "при завершении"): a finished countdown is the strongest
            // thing that can happen to a row the user was watching, so it earns the strong
            // tier's accent flash. Detected from the payload rather than from a new machine
            // signal, because the FSM is off limits: a completion is the one case where the
            // timer STOPS playing AND has no time left, which separates it from both a pause
            // (stopped, time left) and a cancel (stopped, and the row goes away entirely).
            var finished = t.Playing == false && t.RemainingSeconds <= 0 && t.Body.Length > 0;
            if (finished && !_timerSignalledDone)
                PlayAccentFlash(OverlayTokens.AccentHex);
            _timerSignalledDone = finished;
        }
        _timerWasActive = active;
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
            // Cached: this runs five times a second and the brush only changes with the accent.
            var brush = CachedBrush(ParseColor(_settings.ColorAccent, OverlayTokens.AccentHex));
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
        // Pinned is the one state with no pointer to explain it, so its hairline has to carry the
        // meaning on its own — as the theme's strongest ink, not as a hardcoded white that only
        // reads on a dark wall. Run after any hover-pin transition (Expand, Collapse, Escape,
        // etc.). Idempotent.
        if (_hoverPin.IsPinned)
        {
            Pill.BorderBrush = _borderPinned;
        }
        else if (!_hoverPin.IsContentExpanded)
        {
            // Unpinning while the pointer is still on the capsule used to leave the strong hairline
            // behind until the next hover cycle — nothing ever put the idle brush back. IsContentExpanded
            // is false only when the machine is at rest, so this cannot fight a live hover.
            Pill.BorderBrush = _borderIdle;
        }
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

            var fs = Win32Overlay.IsFullscreenOrBusy(out var fsReason);
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
                        AppLog.Info($"Fullscreen detected — island hidden ({fsReason})");
                    }
                }
                else if (_settings.ClickThroughOnFullscreen)
                {
                    if (!_clickThroughActive)
                    {
                        _clickThroughActive = true;
                        Win32Overlay.ApplyClickThrough(this, true);
                        AppLog.Info($"Fullscreen detected — click-through ({fsReason})");
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
                        PlayFirstAppearWobble();
                    }
                }
            }
            else
            {
                // The window's ACTUAL visibility is the truth here, not _hiddenByFullscreen.
                // That flag is cleared in places that do not call Show() — ApplySettingsFromUi
                // clears it and then calls PollFullscreen, whose restore branch is guarded by
                // the very flag it just cleared, so the island stayed hidden for the rest of the
                // session with nothing left to bring it back. Observed live: a window sitting at
                // visible=False with no "restored" line ever logged.
                //
                // Reconciling against IsVisible instead makes the whole class of flag/window
                // desync self-heal on the next poll, and costs one property read.
                if (_hiddenByFullscreen || !IsVisible)
                {
                    _hiddenByFullscreen = false;
                    Show();
                    Opacity = 1;
                    IsHitTestVisible = true;
                    Win32Overlay.ApplyNoActivate(this);
                    Win32Overlay.ApplyZOrder(this, _settings.ZOrderMode);
                    PlaceIsland();
                    AppLog.Info("Left fullscreen — island restored");
                    PlayFirstAppearWobble();
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

/// <summary>
/// How loudly the island answers an event (spec Этап 5, §1).
/// <para>
/// Three levels, and the level is chosen by what CHANGED — not by how the event arrived. A
/// CPU reading and a timer completing both arrive through the same clipboard-shaped path, but
/// one is a background fact and the other is something the user was waiting for.
/// </para>
/// <para>
/// This is a window-layer type on purpose. <see cref="AnimationTiming"/> and <see cref="AnimEase"/>
/// already own durations and curves, and a level here only says which of those existing values
/// to use — it introduces no new timing of its own, so nothing here can drift away from the
/// speed setting or from reduced motion.
/// </para>
/// </summary>
internal enum ReactionLevel
{
    /// <summary>Background facts: time, weather, CPU/RAM, network, progress, the passing second.</summary>
    Quiet,
    /// <summary>The island gaining or losing a layer: media, timer, clipboard, stats, drawer.</summary>
    Medium,
    /// <summary>Something the user was waiting for, or must not miss: a notification, a finished
    /// timer, a low battery, a failure.</summary>
    Strong,
}

/// <summary>
/// The reaction a given level is allowed to make, resolved against the user's speed setting.
/// <para>
/// The numbers below are expressed as FRACTIONS of an existing duration rather than as new
/// millisecond constants, so a level automatically follows <see cref="AnimationTiming"/>'s
/// Fast/Normal/Slow multipliers and the reduced-motion path instead of becoming a fourth,
/// parallel set of timings nobody scales.
/// </para>
/// </summary>
internal static class Reaction
{
    /// <summary>How long one element's fade takes, as a share of the morph it rides.</summary>
    public static int RevealMs(ReactionLevel level, AnimationSpeed speed) => level switch
    {
        // A quiet change is a crossfade, not a movement: it must be over before the next data
        // point arrives, or the numbers would never be readable.
        ReactionLevel.Quiet => AnimationTiming.ScaleMs(OverlayTokens.MorphMs / 3, speed),
        ReactionLevel.Medium => AnimationTiming.ScaleMs(OverlayTokens.MorphMs / 2, speed),
        _ => AnimationTiming.ScaleMs(OverlayTokens.MorphMs / 2, speed),
    };

    /// <summary>
    /// Delay between consecutive elements on the shared timeline, as a share of the level's
    /// own duration. <see cref="ReactionLevel.Quiet"/> is zero: a quiet change never staggers,
    /// because a stagger reads as a sequence and a CPU tick is not a sequence.
    /// </summary>
    public static double StaggerShare(ReactionLevel level) => level switch
    {
        ReactionLevel.Quiet => 0.0,
        // 1.12.4's spec asks for a stagger that is "barely noticeable" on the stats rows; 0.18
        // of a ~210 ms fade is ~38 ms, which reads as a soft cascade and not as a queue.
        _ => 0.18,
    };

    /// <summary>
    /// How far an element rises as it fades in (DIP). Zero for quiet — a background refresh
    /// must not move the surface, or the capsule appears to twitch once a second.
    /// </summary>
    public static double RiseDip(ReactionLevel level) => level switch
    {
        ReactionLevel.Quiet => 0.0,
        ReactionLevel.Medium => 2.0,
        _ => 3.0,
    };

    /// <summary>Accent flash length for a strong reaction (ms), as a share of the morph.</summary>
    public static int AccentMs(AnimationSpeed speed) =>
        AnimationTiming.ScaleMs(OverlayTokens.MorphMs / 2, speed);

    /// <summary>Peak extra opacity of the accent flash. Kept low: it is a tint, not a lamp.</summary>
    public const double AccentPeak = 0.28;
}

/// <summary>One element in a staged reveal.</summary>
internal readonly record struct RevealItem(
    Avalonia.Controls.Control Target,
    /// <summary>Position in the order, 0 = first. Ties keep the caller's order.</summary>
    int Order,
    /// <summary>Opacity the element rests at once the reveal is over.</summary>
    double RestOpacity)
{
    /// <summary>How far this element rises, from the reaction level that scheduled it.</summary>
    public double RiseDip { get; init; }
}

/// <summary>
/// A staged reveal: several elements fading in one after another on the shared frame clock.
/// <para>
/// The ordering lives in ONE list rather than in a timer per element, which is the point — a
/// per-element timer would let an icon arrive after its own title (they are written in the same
/// paint pass, and their timers start a frame apart), and the spec's "icon first, title second,
/// body third" is a property of the schedule, not of who happens to be scheduled first.
/// </para>
/// <para>
/// Reduced motion does not shorten this: it removes it. <see cref="Start"/> writes the end state
/// immediately and the run never ticks, so a user who asked for less motion gets a caption that
/// is simply already there, with the reveal's information — that something arrived — kept by the
/// size change and the content itself.
/// </para>
/// </summary>
internal sealed class RevealRun
{
    private readonly RevealItem[] _items;
    private readonly int _durationMs;
    private readonly Func<long> _now;
    private long _startedAtMs;
    private readonly double _staggerShare;

    /// <summary>Translate applied per element, parallel to <see cref="_items"/>.</summary>
    private readonly TranslateTransform?[] _moves;

    public RevealRun(
        IReadOnlyList<RevealItem> items,
        int durationMs,
        double staggerShare,
        Func<long> now)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(now);
        _items = items.OrderBy(i => i.Order).ToArray();
        _durationMs = Math.Max(1, durationMs);
        _staggerShare = Math.Max(0, staggerShare);
        _now = now;
        _moves = new TranslateTransform?[_items.Length];
        for (var i = 0; i < _items.Length; i++)
        {
            var t = _items[i].Target;
            // Only animate position where nothing else owns the transform. The chevrons, the
            // marquee text and the pill all carry transforms that are written every frame by
            // their own animation; putting a second transform on one of them would have the two
            // overwrite each other, and the reveal would show as a jitter instead of a rise.
            if (t.RenderTransform is null && _items[i].RiseDip > 0)
            {
                var tr = new TranslateTransform();
                t.RenderTransform = tr;
                _moves[i] = tr;
            }
        }
    }

    /// <summary>Total wall time including the stagger tail.</summary>
    private int TotalMs => _durationMs + (int)Math.Round(_staggerShare * _durationMs * (_items.Length - 1));

    /// <summary>Begin the run. Called once, immediately after construction.</summary>
    public void Start() => _startedAtMs = _now();

    /// <summary>Put every element straight to its resting state — the reduced-motion path.</summary>
    public void Finish()
    {
        for (var i = 0; i < _items.Length; i++)
        {
            _items[i].Target.Opacity = _items[i].RestOpacity;
            if (_moves[i] is not { } m) continue;
            m.Y = 0;
            // Hand the transform back. The ctor skips elements that already own one, so a
            // TranslateTransform left behind here would make the NEXT run's "only animate
            // position where nothing else owns the transform" guard false: the first reaction
            // would rise and every one after it would only crossfade.
            _items[i].Target.RenderTransform = null;
        }
    }

    /// <summary>One frame; false once every element has arrived.</summary>
    public bool Tick()
    {
        var elapsed = _now() - _startedAtMs;
        var done = true;
        for (var i = 0; i < _items.Length; i++)
        {
            var item = _items[i];
            if (item.Target is null || !item.Target.IsVisible) continue;

            var start = _staggerShare * _durationMs * i;
            var p = Math.Clamp((elapsed - start) / (double)_durationMs, 0.0, 1.0);
            if (p < 1.0) done = false;

            // power2.out: the same soft settle the island morph uses, so a caption arriving and
            // a capsule growing feel like one system rather than two.
            var eased = AnimEase.Ease("power2.out", p);
            item.Target.Opacity = item.RestOpacity * eased;
            if (_moves[i] is { } m) m.Y = item.RiseDip * (1.0 - eased);
        }
        if (done) Finish();
        return !done;
    }
}

