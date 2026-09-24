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

    /// <summary>Pointer-leave grace before collapsing peek (ms). Default 500.</summary>
    public int HoverCollapseGraceMs { get; set; } = OverlayTokens.HoverCollapseGraceMs;

    /// <summary>Single click toggles pinned expanded Idle. Default ON.</summary>
    public bool ClickPinEnabled { get; set; } = true;

    /// <summary>Hide island while exclusive/fullscreen foreground. Default ON.</summary>
    public bool HideOnFullscreen { get; set; } = true;

    /// <summary>
    /// When HideOnFullscreen is off: make overlay click-through during fullscreen.
    /// Secondary; default OFF. Prefer hide.
    /// </summary>
    public bool ClickThroughOnFullscreen { get; set; } = false;

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

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AnimationSpeed AnimSwipeRubber { get; set; } = AnimationSpeed.Normal;

    /// <summary>Enable unread-dot opacity pulse when unread &gt; 0.</summary>
    public bool AnimPulseEnabled { get; set; } = true;

    /// <summary>Notification appear style (Settings → Анимации).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NotifyAppearStyle AppearStyle { get; set; } = NotifyAppearStyle.Bounce;

    /// <summary>Notification dismiss / collapse style.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NotifyDismissStyle DismissStyle { get; set; } = NotifyDismissStyle.Ragged;

    /// <summary>Persisted Settings window geometry (separate from island OffsetX/Y).</summary>
    public int? SettingsWindowX { get; set; }
    public int? SettingsWindowY { get; set; }
    public double SettingsWindowWidth { get; set; } = 720;
    public double SettingsWindowHeight { get; set; } = 560;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotifyIsland");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static string WeatherCachePath => Path.Combine(SettingsDirectory, "weather-cache.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var s = FromJson(json);
                if (s is not null) return s;
            }
        }
        catch
        {
            // ignore corrupt settings — fall back to defaults
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, ToJson());
        }
        catch
        {
            // ignore write failures (Sandbox / locked)
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

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
        target.HoverCollapseGraceMs = Math.Clamp(HoverCollapseGraceMs, 0, 3000);
        target.ClickPinEnabled = ClickPinEnabled;
        target.HideOnFullscreen = HideOnFullscreen;
        target.ClickThroughOnFullscreen = ClickThroughOnFullscreen;
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
        target.AnimPulseEnabled = AnimPulseEnabled;
        target.AppearStyle = AppearStyle;
        target.DismissStyle = DismissStyle;
        target.SettingsWindowX = SettingsWindowX;
        target.SettingsWindowY = SettingsWindowY;
        target.SettingsWindowWidth = SettingsWindowWidth;
        target.SettingsWindowHeight = SettingsWindowHeight;
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
        HoverCollapseGraceMs = Math.Clamp(HoverCollapseGraceMs, 0, 3000);
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
