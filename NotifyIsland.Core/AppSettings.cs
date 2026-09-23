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

    /// <summary>When true, left-button hold &gt;200ms without release starts drag-reposition.</summary>
    public bool AllowDrag { get; set; } = true;

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

    /// <summary>Persisted Settings window geometry (separate from island OffsetX/Y).</summary>
    public int? SettingsWindowX { get; set; }
    public int? SettingsWindowY { get; set; }
    public double SettingsWindowWidth { get; set; } = 460;
    public double SettingsWindowHeight { get; set; } = 720;

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
        target.AllowDrag = AllowDrag;
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
        target.SettingsWindowX = SettingsWindowX;
        target.SettingsWindowY = SettingsWindowY;
        target.SettingsWindowWidth = SettingsWindowWidth;
        target.SettingsWindowHeight = SettingsWindowHeight;
    }

    /// <summary>Clamp opacity / volume into valid ranges after deserialize.</summary>
    public void Normalize()
    {
        Opacity = Math.Clamp(Opacity <= 0 ? 1.0 : Opacity, 0.35, 1.0);
        SoundVolume = Math.Clamp(SoundVolume < 0 ? 0.35 : SoundVolume, 0.0, 1.0);
        SoundVolNotify = Math.Clamp(SoundVolNotify < 0 ? 1.0 : SoundVolNotify, 0.0, 1.0);
        SoundVolExpand = Math.Clamp(SoundVolExpand < 0 ? 0.85 : SoundVolExpand, 0.0, 1.0);
        SoundVolCollapse = Math.Clamp(SoundVolCollapse < 0 ? 0.75 : SoundVolCollapse, 0.0, 1.0);
        SoundVolSwipe = Math.Clamp(SoundVolSwipe < 0 ? 0.7 : SoundVolSwipe, 0.0, 1.0);
        SoundVolError = Math.Clamp(SoundVolError < 0 ? 1.0 : SoundVolError, 0.0, 1.0);
        SoundVolHover = Math.Clamp(SoundVolHover < 0 ? 0.35 : SoundVolHover, 0.0, 1.0);
        if (SettingsWindowWidth < 360) SettingsWindowWidth = 460;
        if (SettingsWindowHeight < 400) SettingsWindowHeight = 720;
    }
}
