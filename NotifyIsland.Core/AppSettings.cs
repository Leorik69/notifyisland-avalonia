using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotifyIsland;

public enum WeatherSide
{
    Left,
    Right
}

public enum ZOrderMode
{
    /// <summary>Above all windows (HWND_TOPMOST).</summary>
    Topmost,
    /// <summary>Above desktop icons, below normal app windows (after Progman/WorkerW).</summary>
    Desktop,
    /// <summary>Below app windows, above desktop/widgets (HWND_BOTTOM / non-topmost).</summary>
    BehindApps
}

public enum IslandEdge
{
    Top,
    Bottom,
    Left,
    Right
}

public enum IslandOrientation
{
    /// <summary>Top/Bottom → Horizontal; Left/Right → Vertical.</summary>
    Auto,
    Horizontal,
    Vertical
}

public enum SoundPack
{
    /// <summary>Original soft Glyph-like click pack under Assets/Sounds/nothing/.</summary>
    Nothing,
    /// <summary>Original soft iOS-like tap pack under Assets/Sounds/ios/.</summary>
    Ios,
    /// <summary>SystemSounds / MessageBeep (optional system/ WAV fallback).</summary>
    System,
    /// <summary>No UI sounds.</summary>
    Off
}

/// <summary>What happens when the user clicks the clipboard pill.</summary>
public enum ClipboardClickAction
{
    /// <summary>Collapse the pill. The data is already in the system clipboard; user pastes via Ctrl+V.</summary>
    Dismiss,
    /// <summary>Collapse the pill AND clear the system clipboard (paranoid mode).</summary>
    DismissAndClear,
    /// <summary>Reserved for v1.1 — auto-paste to the previous foreground window. Disabled in v1.</summary>
    PasteToLastFocus
}


/// <summary>JSON settings under %LOCALAPPDATA%/NotifyIsland/settings.json.</summary>
public sealed class AppSettings
{
    public bool WeatherEnabled { get; set; } = true;

    /// <summary>Show live Now Playing from Windows SMTC when a session is active. Default ON.</summary>
    public bool ShowNowPlaying { get; set; } = true;

    /// <summary>Charge-connect pill + low-battery alert. Default ON.</summary>
    public bool ShowBatteryAlerts { get; set; } = true;

    /// <summary>Show compact battery % in collapsed idle row. Default OFF.</summary>
    public bool ShowBatteryInCollapsed { get; set; } = false;

    /// <summary>Island countdown timer from tray / Settings. Default ON.</summary>
    public bool TimerEnabled { get; set; } = true;

    /// <summary>Default preset minutes for tray/Settings start (1–180). Default 5.</summary>
    public int TimerDefaultMinutes { get; set; } = IslandTimerLogic.DefaultPresetMinutes;

    /// <summary>When true, timer panel starts in stopwatch (count-up) mode. Default OFF.</summary>
    public bool TimerStopwatchMode { get; set; } = false;

    /// <summary>Low-battery threshold percent (5–50). Default 20.</summary>
    public int LowBatteryPercent { get; set; } = BatteryAlertLogic.DefaultLowPercent;
    public double Latitude { get; set; } = 55.75;
    public double Longitude { get; set; } = 37.62;

    /// <summary>Windows system weather vs manual city label/coords.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WeatherLocationMode WeatherLocationMode { get; set; } = WeatherLocationMode.Windows;

    /// <summary>Display name when Manual (e.g. «Москва»). Shown next to temp / expanded weather.</summary>
    public string WeatherLocationName { get; set; } = "Москва";

    /// <summary>Collapsed-row date chip format. Default DayMonth («24 сен»).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DateFormat DateFormat { get; set; } = DateFormat.DayMonth;

    /// <summary>FontAudio 7-segment digital clock glyphs instead of text HH:mm. Default ON.</summary>
    public bool DigitalClockEnabled { get; set; } = true;

    /// <summary>Show seconds (HH:mm:ss) on digital/text clock in collapsed Idle. Default OFF (narrow pill).</summary>
    public bool ShowClockSeconds { get; set; } = false;

    /// <summary>FontAudio digital-dot seconds progress strip at capsule bottom. Default ON.</summary>
    public bool ShowSecondsStrip { get; set; } = true;

    /// <summary>Hover over Idle/Collapsed expands to richer peek after delay. Default ON.</summary>
    public bool HoverExpandEnabled { get; set; } = true;

    /// <summary>Hover delay before peek (ms). Default 250.</summary>
    public int HoverExpandDelayMs { get; set; } = OverlayTokens.HoverExpandDelayMs;

    /// <summary>
    /// Pointer-leave grace before collapsing the hover peek (ms). Default
    /// <see cref="OverlayTokens.HoverCollapseGraceMs"/> (5000 as of 1.13.0; it was 500 before).
    /// </summary>
    public int HoverCollapseGraceMs { get; set; } = OverlayTokens.HoverCollapseGraceMs;

    /// <summary>
    /// The grace the product shipped with before 1.13.0. A settings.json written by an older
    /// install carries this value, and it is indistinguishable from one the user typed: the
    /// property has always been persisted. So the stored 500 is almost certainly nobody's
    /// choice — it is just the old default that survived every save. It is therefore migrated up
    /// to the new default instead of stranding the user on a 0.5 s panel.
    /// </summary>
    public const int LegacyHoverCollapseGraceMs = 500;

    /// <summary>
    /// User control over the collapsed island's long axis (1.14), as a multiplier of the token
    /// width rather than an absolute DIP count — see <see cref="IslandWidth"/> for why, and for
    /// the argument behind MinScale/MaxScale. 1.0 = exactly the token widths.
    /// </summary>
    public double IslandWidthScale { get; set; } = IslandWidth.DefaultScale;

    /// <summary>Single click toggles pinned expanded Idle. Default ON.</summary>
    public bool ClickPinEnabled { get; set; } = true;

    /// <summary>Hide island while exclusive/fullscreen foreground. Default ON.</summary>
    public bool HideOnFullscreen { get; set; } = true;

    /// <summary>
    /// When HideOnFullscreen is off: make overlay click-through during fullscreen.
    /// Secondary; default OFF. Prefer hide.
    /// </summary>
    public bool ClickThroughOnFullscreen { get; set; } = false;

    /// <summary>
    /// Raise the island above the app windows while a notification is on the capsule, and put it
    /// back where <see cref="ZOrderMode"/> says afterwards. Default ON.
    /// <para>
    /// 2026-10-02, added with the notification queue. A notification that arrives while the user
    /// is looking at something else was previously drawn behind that window whenever the island sat
    /// at Desktop or BehindApps — which is what those modes are FOR, but it means a toast the user
    /// asked to see is silently unseen. The jump is temporary and only for the notification's
    /// lifetime, so the configured z-order still governs the island the rest of the time.
    /// </para>
    /// <para>
    /// The fullscreen rules still win: if the island is hidden or click-through because something
    /// is fullscreen, this does not pull it back.
    /// </para>
    /// </summary>
    public bool NotifyJumpToTop { get; set; } = true;

    /// <summary>
    /// Scroll a notification's body text when it is wider than its column, instead of cutting it
    /// at the ellipsis. Default ON.
    /// <para>
    /// 2026-10-03, added with the bell badge. The title is short by construction — an app name —
    /// so the body is what a notification actually loses, and on a real toast that is usually the
    /// half that says what happened. Cutting it leaves the user with «Windows PowerShell» and no
    /// way to learn anything; scrolling shows all of it, at the cost of not being able to read it
    /// all at once.
    /// </para>
    /// <para>
    /// The motion is the monitor's own running caption (<see cref="MarqueeTrack"/>), not a second
    /// implementation, and reduced motion stops it: the body then stays trimmed exactly as it was
    /// before this setting existed.
    /// </para>
    /// </summary>
    public bool NotifyBodyMarquee { get; set; } = true;

    /// <summary>Stock theme or Custom. Stock Apply overwrites palette/font/anim/icons/date.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ThemePreset ThemePreset { get; set; } = ThemePreset.Custom;

    /// <summary>Weather chip relative to clock in collapsed layout.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WeatherSide WeatherSide { get; set; } = WeatherSide.Right;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ZOrderMode ZOrderMode { get; set; } = ZOrderMode.Topmost;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IslandEdge Edge { get; set; } = IslandEdge.Top;

    /// <summary>Pixel offset from the chosen edge anchor (along edge / inward).</summary>
    public int OffsetX { get; set; }

    public int OffsetY { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IslandOrientation Orientation { get; set; } = IslandOrientation.Auto;

    /// <summary>
    /// Legacy: mouse drag-reposition removed. Always false; position only via Edge + OffsetX/Y.
    /// Kept for JSON compat; Normalize() forces false so old settings never re-enable drag.
    /// </summary>
    public bool AllowDrag { get; set; } = false;

    public bool IslandVisible { get; set; } = true;

    /// <summary>Capsule fill opacity 0.35–1.0 (text/border stay readable).</summary>
    public double Opacity { get; set; } = 1.0;

    public bool SoundEnabled { get; set; } = true;

    /// <summary>Enable Windows clipboard history listener and the overlay pill.</summary>
    public bool ClipboardEnabled { get; set; } = true;

    /// <summary>Max items kept in clipboard history (1–100). Default 25.</summary>
    public int ClipboardMaxItems { get; set; } = ClipboardHistory.DefaultMaxItems;

    /// <summary>What happens when the user clicks the clipboard pill.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClipboardClickAction ClipboardClickAction { get; set; } = ClipboardClickAction.Dismiss;

    /// <summary>WAV pack or System/Off. Default Nothing (original inspired tones).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SoundPack SoundPack { get; set; } = SoundPack.Nothing;

    /// <summary>0.0–1.0 master volume for island UI sounds.</summary>
    public double SoundVolume { get; set; } = 0.35;

    /// <summary>Per-event multipliers 0–1 (applied on top of SoundVolume).</summary>
    public double SoundVolNotify { get; set; } = 1.0;
    public double SoundVolExpand { get; set; } = 0.85;
    public double SoundVolCollapse { get; set; } = 0.75;
    public double SoundVolSwipe { get; set; } = 0.7;
    public double SoundVolError { get; set; } = 1.0;
    public double SoundVolHover { get; set; } = 0.35;

    /// <summary>Island morph / pulse speed. Default Normal.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AnimationSpeed AnimationSpeed { get; set; } = AnimationSpeed.Slow;

    /// <summary>Capsule fill (#RRGGBB). Default dark island.</summary>
    public string ColorCapsuleFill { get; set; } = "#080808";

    /// <summary>Accent for badges / unread dot (#RRGGBB).</summary>
    public string ColorAccent { get; set; } = "#3D9CF0";

    /// <summary>Primary text (#RRGGBB).</summary>
    public string ColorTextPrimary { get; set; } = "#FFFFFF";

    /// <summary>Secondary text (#RRGGBB).</summary>
    public string ColorTextSecondary { get; set; } = "#C8C8CC";

    /// <summary>Icon pack id: IslandIcons (built-in), Tabler, or Lucide.</summary>
    public string IconPack { get; set; } = "IslandIcons";

    /// <summary>Island text size in px (clock, titles, weather temp). Range 10–18.</summary>
    public double FontSize { get; set; } = 12;

    /// <summary>Font family id: System | SpaceGrotesk | JetBrainsMono.</summary>
    public string FontFamily { get; set; } = "System";

    /// <summary>Per-action speeds (global AnimationSpeed=Off disables all).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AnimationSpeed AnimMorphInflate { get; set; } = AnimationSpeed.Slow;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AnimationSpeed AnimMorphCollapse { get; set; } = AnimationSpeed.Slow;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AnimationSpeed AnimUnreadPulse { get; set; } = AnimationSpeed.Normal;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AnimationSpeed AnimHover { get; set; } = AnimationSpeed.Normal;

    /// <summary>
    /// Speed of the old swipe rubber-band. **Dead since 1.13.0** — the <c>DoubleTransition</c> it
    /// fed was removed from <c>_pillTranslate</c> because it fought the per-frame morph writes, and
    /// the gesture it served was deleted back in 1.8.1. Kept in the schema on purpose: a user may
    /// have the key in settings.json, and dropping it would silently discard their file's value.
    /// Nothing reads it.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AnimationSpeed AnimSwipeRubber { get; set; } = AnimationSpeed.Normal;

    /// <summary>Speed of the click-acknowledgement pop.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AnimationSpeed AnimClickPop { get; set; } = AnimationSpeed.Normal;

    /// <summary>Speed of the first-appear wobble.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AnimationSpeed AnimFirstAppearWobble { get; set; } = AnimationSpeed.Normal;

    /// <summary>Enable unread-dot opacity pulse when unread &gt; 0.</summary>
    public bool AnimPulseEnabled { get; set; } = true;

    /// <summary>
    /// User's own "reduce motion" request (Settings → Анимации), combined with the OS toggle by
    /// <see cref="AnimReduced.Resolve"/>. Default <c>false</c> = "no extra request": a settings.json
    /// written by an older build simply has no such key and deserializes to the initialiser value, so
    /// untouched installs keep animating exactly as before (1.12.4).
    /// </summary>
    public bool ReducedMotion { get; set; } = false;

    /// <summary>Notification appear style (Settings → Анимации).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NotifyAppearStyle AppearStyle { get; set; } = NotifyAppearStyle.Bounce;

    /// <summary>Notification dismiss / collapse style.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NotifyDismissStyle DismissStyle { get; set; } = NotifyDismissStyle.Ragged;

    /// <summary>Show live CPU / RAM / battery / network in the collapsed pill.</summary>
    public bool SystemStatsEnabled { get; set; } = true;

    /// <summary>Sampling period in ms. Clamped to [StatsRefreshMinMs, StatsRefreshMaxMs] by Normalize().</summary>
    public int SystemStatsRefreshMs { get; set; } = OverlayTokens.StatsRefreshMs;

    // REMOVED 1.12.1: SystemStatsAutoCollapse (and its Settings checkbox "Сворачивать через 30 с").
    // The wall-clock 30 s self-collapse it gated no longer exists — the SystemStats surface is
    // entered on hover and closed on pointer-leave (HoverExpandDelayMs / HoverCollapseGraceMs).
    // Old settings.json files that still contain the key simply ignore the unknown member.

    /// <summary>Hover the Idle pill for <see cref="HoverExpandDelayMs"/> to peek the SystemStats surface. Default ON.</summary>
    public bool SystemStatsHoverPeek { get; set; } = true;

    /// <summary>Count virtual / tunnel / loopback network interfaces in the net metric.</summary>
    public bool SystemStatsAllInterfaces { get; set; } = true;

    /// <summary>
    /// Which row set the System Stats surface renders. Independent of <see cref="SystemStatsEnabled"/>,
    /// which is the on/off switch for the surface as a whole; this picks the rows.
    /// Not named <c>StatsPreset</c> to avoid colliding with the enum type in code that references both.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StatsPreset StatsRowsPreset { get; set; } = StatsPreset.Full;

    /// <summary>
    /// Ordered set of visible System Stats rows. Authoritative only when
    /// <see cref="StatsRowsPreset"/> is <see cref="StatsPreset.Custom"/>; for the other presets
    /// Normalize() rewrites it to the preset's canonical set. Defaults to the Full set.
    /// </summary>
    public List<StatsRow> StatsRows { get; set; } =
        new List<StatsRow>(StatsLayout.FullRows);

    /// <summary>Show the sidebar search box when there are enough sections to filter.</summary>
    public bool SettingsSearchEnabled { get; set; } = true;

    /// <summary>Persisted Settings window geometry (separate from island OffsetX/Y).</summary>
    public int? SettingsWindowX { get; set; }
    public int? SettingsWindowY { get; set; }
    public double SettingsWindowWidth { get; set; } = 720;
    public double SettingsWindowHeight { get; set; } = 560;

    /// <summary>
    /// When set, the clipboard listener ignores captures until this UTC instant (spec §«Не
    /// реагировать 30 мин»). Null = listening normally. The tray tooltip surfaces the
    /// remaining time so the user can see why new copies don't reach the clipboard section.
    /// <para>
    /// 1.14: the pause SURVIVED the ball. Everything around the clipboard's on-screen position
    /// (the ball pin, its drag offsets) went with the ball, but this is a listener-level switch
    /// and nothing about it depended on the ball existing.
    /// </para>
    /// </summary>
    public DateTime? ClipboardPrivacyPauseUntilUtc { get; set; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        // Reading is case-insensitive so a hand-edited file or an export from a build that wrote
        // PascalCase names still loads instead of silently falling back to defaults.
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotifyIsland");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static string WeatherCachePath => Path.Combine(SettingsDirectory, "weather-cache.json");

    public static AppSettings Load()
    {
        if (TryLoadFrom(SettingsPath, out var loaded, out var error))
        {
            LastLoadError = null;
            return loaded!;
        }

        // 2026-10-02: Load used to swallow this and return defaults, which was fine until the
        // next Save — that overwrote the user's file with defaults and the original was gone
        // for good, with nothing on screen or in the log to say why their settings had reset.
        // TryLoadFrom has already moved the unreadable file aside. The reason is kept rather than
        // printed because Core is a library: it cannot reach the app's own AppLog, and a
        // Console.Error from a WinExe goes nowhere. The host reads LastLoadError at startup.
        LastLoadError = error.Length == 0 ? null : error;
        return new AppSettings();
    }

    /// <summary>
    /// Why the last <see cref="Load"/> fell back to defaults, or null if it did not. Set only by
    /// Load, and cleared on a successful read — a stale reason from a later good load must not be
    /// reported as a current problem.
    /// </summary>
    public static string? LastLoadError { get; private set; }

    /// <summary>
    /// Read settings from an explicit path.
    /// <para>
    /// Split out of <see cref="Load"/> so the failure path is testable — Load's own path lives
    /// under LOCALAPPDATA and must not be touched by a test.
    /// </para>
    /// <para>
    /// Returns true only when a file existed and parsed. A missing file is not a failure: the
    /// error stays empty so first run is indistinguishable from a clean start. A file that exists
    /// but cannot be parsed IS a failure, it reports why, and it is moved to
    /// <c>settings.corrupt-&lt;timestamp&gt;.json</c> before this returns — because the next Save
    /// writes over the original, and a quarantined file is the difference between the user
    /// recovering their settings and not.
    /// </para>
    /// </summary>
    public static bool TryLoadFrom(string path, out AppSettings? settings, out string error)
    {
        settings = null;
        error = string.Empty;

        bool exists;
        try { exists = File.Exists(path); }
        catch { return false; }
        if (!exists) return false;

        try
        {
            var json = File.ReadAllText(path);
            var parsed = FromJson(json);
            if (parsed is not null)
            {
                settings = parsed;
                return true;
            }
            error = "файл не содержит настроек";
        }
        catch (JsonException) { error = "файл повреждён: не является корректным JSON"; }
        catch (IOException) { error = "файл не удалось прочитать"; }
        catch (UnauthorizedAccessException) { error = "нет доступа к файлу"; }
        catch
        {
            // Anything else is still a read failure worth reporting; the file stays where it is.
            error = "файл не удалось разобрать";
        }

        QuarantineCorrupt(path);
        return false;
    }

    /// <summary>
    /// Move an unreadable settings file aside so the next Save cannot destroy it. Best effort:
    /// a failure here must not stop the app from starting on defaults.
    /// </summary>
    private static void QuarantineCorrupt(string path)
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var target = Path.Combine(
                Path.GetDirectoryName(path) ?? string.Empty,
                $"settings.corrupt-{stamp}.json");
            File.Move(path, target, overwrite: true);
        }
        catch
        {
            // Cannot move it (locked, read-only volume). The original stays put and the message
            // from TryLoadFrom still told the user their file was the problem.
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            WriteAtomically(SettingsPath, ToJson());
        }
        catch
        {
            // ignore write failures (Sandbox / locked)
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    /// <summary>
    /// Write through a temp file and swap it in, so a crash or a full disk mid-write leaves the
    /// previous settings intact instead of a truncated file that Load would discard as corrupt.
    /// </summary>
    public static void WriteAtomically(string path, string contents)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }

    /// <summary>
    /// Parse a settings file chosen by the user. Unlike <see cref="Load"/> this reports why it
    /// failed, because the user is waiting on the answer: null result plus a short reason.
    /// </summary>
    public static AppSettings? TryImport(string json, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(json)) { error = "файл пуст"; return null; }
        try
        {
            var s = FromJson(json);
            if (s is null) error = "в файле нет настроек";
            return s;
        }
        catch (JsonException) { error = "файл не является настройками NotifyIsland"; return null; }
        catch (NotSupportedException) { error = "неподдерживаемый формат файла"; return null; }
    }

    public static AppSettings? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
        s?.Normalize();
        return s;
    }

    /// <summary>Copy mutable fields onto another instance (e.g. live overlay settings).</summary>
    public void CopyTo(AppSettings target)
    {
        target.WeatherEnabled = WeatherEnabled;
        target.ShowNowPlaying = ShowNowPlaying;
        target.ShowBatteryAlerts = ShowBatteryAlerts;
        target.ShowBatteryInCollapsed = ShowBatteryInCollapsed;
        target.TimerEnabled = TimerEnabled;
        target.TimerDefaultMinutes = IslandTimerLogic.ClampPresetMinutes(TimerDefaultMinutes);
        target.TimerStopwatchMode = TimerStopwatchMode;
        target.LowBatteryPercent = BatteryAlertLogic.ClampLowPercent(LowBatteryPercent);
        target.Latitude = Latitude;
        target.Longitude = Longitude;
        target.WeatherLocationMode = WeatherLocationMode;
        target.WeatherLocationName = string.IsNullOrWhiteSpace(WeatherLocationName) ? "Москва" : WeatherLocationName.Trim();
        target.DateFormat = DateFormat;
        target.DigitalClockEnabled = DigitalClockEnabled;
        target.ShowClockSeconds = ShowClockSeconds;
        target.ShowSecondsStrip = ShowSecondsStrip;
        target.HoverExpandEnabled = HoverExpandEnabled;
        target.HoverExpandDelayMs = Math.Clamp(HoverExpandDelayMs, 0, 2000);
        // 1.13.1: the ceiling was 3000 ms, which silently cut the 5000 ms default down to 3 s on
        // every load. The token and the machine both allowed 10 s, so the layer between them was
        // the only thing deciding the grace the user actually got — and it decided 3 s. The
        // ceiling now matches HoverPinMachine.Configure.
        target.HoverCollapseGraceMs = Math.Clamp(HoverCollapseGraceMs, 0, 10000);
        target.IslandWidthScale = IslandWidth.ClampScale(IslandWidthScale);
        target.ClickPinEnabled = ClickPinEnabled;
        target.HideOnFullscreen = HideOnFullscreen;
        target.ClickThroughOnFullscreen = ClickThroughOnFullscreen;
        target.NotifyJumpToTop = NotifyJumpToTop;
        target.NotifyBodyMarquee = NotifyBodyMarquee;
        target.ThemePreset = ThemePreset;
        target.WeatherSide = WeatherSide;
        target.ZOrderMode = ZOrderMode;
        target.Edge = Edge;
        target.OffsetX = OffsetX;
        target.OffsetY = OffsetY;
        target.Orientation = Orientation;
        target.AllowDrag = false; // drag removed
        target.IslandVisible = IslandVisible;
        target.Opacity = Math.Clamp(Opacity, 0.35, 1.0);
        target.SoundEnabled = SoundEnabled;
        target.ClipboardEnabled = ClipboardEnabled;
        target.ClipboardMaxItems = Math.Clamp(ClipboardMaxItems, 1, ClipboardHistory.HardCap);
        target.ClipboardClickAction = Enum.IsDefined(typeof(ClipboardClickAction), ClipboardClickAction)
            ? ClipboardClickAction : ClipboardClickAction.Dismiss;
        target.SoundPack = SoundPack;
        target.SoundVolume = Math.Clamp(SoundVolume, 0.0, 1.0);
        target.SoundVolNotify = Math.Clamp(SoundVolNotify, 0.0, 1.0);
        target.SoundVolExpand = Math.Clamp(SoundVolExpand, 0.0, 1.0);
        target.SoundVolCollapse = Math.Clamp(SoundVolCollapse, 0.0, 1.0);
        target.SoundVolSwipe = Math.Clamp(SoundVolSwipe, 0.0, 1.0);
        target.SoundVolError = Math.Clamp(SoundVolError, 0.0, 1.0);
        target.SoundVolHover = Math.Clamp(SoundVolHover, 0.0, 1.0);
        target.AnimationSpeed = AnimationSpeed;
        target.ColorCapsuleFill = NormalizeHex(ColorCapsuleFill, "#080808");
        target.ColorAccent = NormalizeHex(ColorAccent, "#3D9CF0");
        target.ColorTextPrimary = NormalizeHex(ColorTextPrimary, "#FFFFFF");
        target.ColorTextSecondary = NormalizeHex(ColorTextSecondary, "#C8C8CC");
        target.IconPack = string.IsNullOrWhiteSpace(IconPack) ? "IslandIcons" : IconPack.Trim();
        target.FontSize = Math.Clamp(FontSize <= 0 ? 12 : FontSize, 10, 18);
        target.FontFamily = NormalizeFontFamily(FontFamily);
        target.AnimMorphInflate = AnimMorphInflate;
        target.AnimMorphCollapse = AnimMorphCollapse;
        target.AnimUnreadPulse = AnimUnreadPulse;
        target.AnimHover = AnimHover;
        target.AnimSwipeRubber = AnimSwipeRubber;
        target.AnimClickPop = AnimClickPop;
        target.AnimFirstAppearWobble = AnimFirstAppearWobble;
        target.AnimPulseEnabled = AnimPulseEnabled;
        target.ReducedMotion = ReducedMotion;
        target.AppearStyle = AppearStyle;
        target.DismissStyle = DismissStyle;
        target.SystemStatsEnabled = SystemStatsEnabled;
        target.SystemStatsRefreshMs = SystemStatsRefreshMs;
        target.SystemStatsHoverPeek = SystemStatsHoverPeek;
        target.SystemStatsAllInterfaces = SystemStatsAllInterfaces;
        target.StatsRowsPreset = StatsRowsPreset;
        target.StatsRows = new List<StatsRow>(StatsRows ?? new List<StatsRow>());
        target.SettingsSearchEnabled = SettingsSearchEnabled;
        target.SettingsWindowX = SettingsWindowX;
        target.SettingsWindowY = SettingsWindowY;
        target.SettingsWindowWidth = SettingsWindowWidth;
        target.SettingsWindowHeight = SettingsWindowHeight;
        target.ClipboardPrivacyPauseUntilUtc = ClipboardPrivacyPauseUntilUtc;
    }

    /// <summary>Clamp opacity / volume into valid ranges after deserialize.</summary>
    public void Normalize()
    {
        // Drag-to-reposition removed: never engage regardless of persisted JSON.
        AllowDrag = false;
        if (string.IsNullOrWhiteSpace(WeatherLocationName)) WeatherLocationName = "Москва";
        else WeatherLocationName = WeatherLocationName.Trim();
        ColorCapsuleFill = NormalizeHex(ColorCapsuleFill, "#080808");
        ColorAccent = NormalizeHex(ColorAccent, "#3D9CF0");
        ColorTextPrimary = NormalizeHex(ColorTextPrimary, "#FFFFFF");
        ColorTextSecondary = NormalizeHex(ColorTextSecondary, "#C8C8CC");
        if (string.IsNullOrWhiteSpace(IconPack)) IconPack = "IslandIcons";
        else IconPack = IconPack.Trim();
        FontSize = Math.Clamp(FontSize <= 0 ? 12 : FontSize, 10, 18);
        FontFamily = NormalizeFontFamily(FontFamily);
        Opacity = Math.Clamp(Opacity <= 0 ? 1.0 : Opacity, 0.35, 1.0);
        SoundVolume = Math.Clamp(SoundVolume < 0 ? 0.35 : SoundVolume, 0.0, 1.0);
        SoundVolNotify = Math.Clamp(SoundVolNotify < 0 ? 1.0 : SoundVolNotify, 0.0, 1.0);
        SoundVolExpand = Math.Clamp(SoundVolExpand < 0 ? 0.85 : SoundVolExpand, 0.0, 1.0);
        SoundVolCollapse = Math.Clamp(SoundVolCollapse < 0 ? 0.75 : SoundVolCollapse, 0.0, 1.0);
        SoundVolSwipe = Math.Clamp(SoundVolSwipe < 0 ? 0.7 : SoundVolSwipe, 0.0, 1.0);
        SoundVolError = Math.Clamp(SoundVolError < 0 ? 1.0 : SoundVolError, 0.0, 1.0);
        SoundVolHover = Math.Clamp(SoundVolHover < 0 ? 0.35 : SoundVolHover, 0.0, 1.0);
        if (SettingsWindowWidth < 360) SettingsWindowWidth = 720;
        if (SettingsWindowHeight < 400) SettingsWindowHeight = 560;
        LowBatteryPercent = BatteryAlertLogic.ClampLowPercent(
            LowBatteryPercent <= 0 ? BatteryAlertLogic.DefaultLowPercent : LowBatteryPercent);
        TimerDefaultMinutes = IslandTimerLogic.ClampPresetMinutes(TimerDefaultMinutes);
        HoverExpandDelayMs = Math.Clamp(HoverExpandDelayMs, 0, 2000);
        // Same ceiling as CopyTo and HoverPinMachine.Configure — see the note there: a 3000 cap
        // here is what actually truncated the 5 s default the user was promised.
        HoverCollapseGraceMs = Math.Clamp(HoverCollapseGraceMs, 0, 10000);
        // 1.13.1: migrate the old shipped default. Without this, a settings.json from an earlier
        // install keeps a literal 500 forever and the new 5 s default is dead on arrival — the
        // clamp change alone could never help, because 500 was already inside the range.
        if (HoverCollapseGraceMs == LegacyHoverCollapseGraceMs
            && OverlayTokens.HoverCollapseGraceMs != LegacyHoverCollapseGraceMs)
            HoverCollapseGraceMs = OverlayTokens.HoverCollapseGraceMs;
        // A settings.json written before 1.14 has no islandWidthScale key at all, and a few
        // intermediate builds could persist 0. Neither is a choice — a scale of 0 would clamp
        // to MinScale and silently shrink the island the first time such a file is loaded.
        if (double.IsNaN(IslandWidthScale) || IslandWidthScale <= 0)
            IslandWidthScale = IslandWidth.DefaultScale;
        else
            IslandWidthScale = IslandWidth.ClampScale(IslandWidthScale);
        SystemStatsRefreshMs = Math.Clamp(SystemStatsRefreshMs,
            OverlayTokens.StatsRefreshMinMs, OverlayTokens.StatsRefreshMaxMs);

        // System Stats rows. Independent of SystemStatsEnabled: turning the surface off must not
        // destroy the user's row order, so this runs either way.
        if (!Enum.IsDefined(typeof(StatsPreset), StatsRowsPreset))
            StatsRowsPreset = StatsPreset.Full;
        // A non-custom preset wins outright, so a stale list can never contradict it.
        // Custom keeps the user's order, filtered to known/unique entries, falling back to Full
        // rather than leaving an empty (zero-height) panel.
        StatsRows = new List<StatsRow>(StatsLayout.ResolveRows(StatsRowsPreset, StatsRows));
    }

    /// <summary>Accept #RGB / #RRGGBB / #AARRGGBB; fallback on parse failure.</summary>
    public static string NormalizeHex(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var v = value.Trim();
        if (v[0] != '#') v = "#" + v;
        if (v.Length is not (4 or 7 or 9)) return fallback;
        foreach (var c in v.AsSpan(1))
        {
            if (!char.IsAsciiHexDigit(c)) return fallback;
        }
        return v.ToUpperInvariant();
    }

    /// <summary>Accept System / SpaceGrotesk / JetBrainsMono (case-insensitive).</summary>
    public static string NormalizeFontFamily(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "System";
        var v = value.Trim();
        if (v.Equals("SpaceGrotesk", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("Space Grotesk", StringComparison.OrdinalIgnoreCase))
            return "SpaceGrotesk";
        if (v.Equals("JetBrainsMono", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("JetBrains Mono", StringComparison.OrdinalIgnoreCase))
            return "JetBrainsMono";
        return "System";
    }
}
