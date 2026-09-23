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


/// <summary>JSON settings under %LOCALAPPDATA%/NotifyIsland/settings.json.</summary>
public sealed class AppSettings
{
    public bool WeatherEnabled { get; set; } = true;
    public double Latitude { get; set; } = 55.75;
    public double Longitude { get; set; } = 37.62;

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

    /// <summary>Island morph / pulse / breath speed. Default Normal.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AnimationSpeed AnimationSpeed { get; set; } = AnimationSpeed.Normal;

    /// <summary>Capsule fill (#RRGGBB). Default dark island.</summary>
    public string ColorCapsuleFill { get; set; } = "#080808";

    /// <summary>Accent for badges / unread dot (#RRGGBB).</summary>
    public string ColorAccent { get; set; } = "#3D9CF0";

    /// <summary>Primary text (#RRGGBB).</summary>
    public string ColorTextPrimary { get; set; } = "#FFFFFF";

    /// <summary>Secondary text (#RRGGBB).</summary>
    public string ColorTextSecondary { get; set; } = "#C8C8CC";

    /// <summary>Icon pack id: IslandIcons (built-in) or planned packs from docs/ICON_PACKS.md.</summary>
    public string IconPack { get; set; } = "IslandIcons";

    /// <summary>Persisted Settings window geometry (separate from island OffsetX/Y).</summary>
    public int? SettingsWindowX { get; set; }
    public int? SettingsWindowY { get; set; }
    public double SettingsWindowWidth { get; set; } = 520;
    public double SettingsWindowHeight { get; set; } = 640;

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
        target.Latitude = Latitude;
        target.Longitude = Longitude;
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
        ColorCapsuleFill = NormalizeHex(ColorCapsuleFill, "#080808");
        ColorAccent = NormalizeHex(ColorAccent, "#3D9CF0");
        ColorTextPrimary = NormalizeHex(ColorTextPrimary, "#FFFFFF");
        ColorTextSecondary = NormalizeHex(ColorTextSecondary, "#C8C8CC");
        if (string.IsNullOrWhiteSpace(IconPack)) IconPack = "IslandIcons";
        else IconPack = IconPack.Trim();
        Opacity = Math.Clamp(Opacity <= 0 ? 1.0 : Opacity, 0.35, 1.0);
        SoundVolume = Math.Clamp(SoundVolume < 0 ? 0.35 : SoundVolume, 0.0, 1.0);
        SoundVolNotify = Math.Clamp(SoundVolNotify < 0 ? 1.0 : SoundVolNotify, 0.0, 1.0);
        SoundVolExpand = Math.Clamp(SoundVolExpand < 0 ? 0.85 : SoundVolExpand, 0.0, 1.0);
        SoundVolCollapse = Math.Clamp(SoundVolCollapse < 0 ? 0.75 : SoundVolCollapse, 0.0, 1.0);
        SoundVolSwipe = Math.Clamp(SoundVolSwipe < 0 ? 0.7 : SoundVolSwipe, 0.0, 1.0);
        SoundVolError = Math.Clamp(SoundVolError < 0 ? 1.0 : SoundVolError, 0.0, 1.0);
        SoundVolHover = Math.Clamp(SoundVolHover < 0 ? 0.35 : SoundVolHover, 0.0, 1.0);
        if (SettingsWindowWidth < 360) SettingsWindowWidth = 520;
        if (SettingsWindowHeight < 400) SettingsWindowHeight = 640;
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
}
