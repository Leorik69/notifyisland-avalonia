using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NotifyIsland.Core;

namespace NotifyIsland;

public sealed class UserPrefs
{
    public string PaletteId { get; set; } = "midnight";
    public string FontId { get; set; } = "segoe-variable";
    public string IconStyle { get; set; } = "fluent";
    public double Opacity { get; set; } = 0.92;
    public string Animation { get; set; } = "morph";
    public int ExpandMs { get; set; } = 167;
    public int CollapseMs { get; set; } = 333;
    public string UiLanguage { get; set; } = "ru";
    public string AnimSpeed { get; set; } = "normal";
    public bool OverlayVisible { get; set; } = true;
    public double BorderThickness { get; set; } = 1;
    public double GlowStrength { get; set; } = 0.55;
    public double CornerRadius { get; set; } = 0;
    public double IdleWidth { get; set; } = 180;
    public double IdleHeight { get; set; } = 40;
    public string ClockFormat { get; set; } = "HH:mm";
    public bool AutoStart { get; set; }
    public bool HoverPeek { get; set; } = true;
    public bool ListenToasts { get; set; }
    public int NotifyDurationMs { get; set; } = 4000;
    public string ExpandMode { get; set; } = "width";
    public bool ExpandHeight { get; set; }
    public double MinWidth { get; set; } = 160;
    public double MaxWidth { get; set; } = 480;
    public string AnchorH { get; set; } = "center";
    public string AnchorV { get; set; } = "top";
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public string Layer { get; set; } = "topmost";
    public bool ClickOpensActionCenter { get; set; } = true;
    public string ClickMode { get; set; } = "cycle-center-player";
    public string TextScale { get; set; } = "medium";
    public string IconScale { get; set; } = "medium";
    public bool ShowWeather { get; set; } = true;
    public bool UseWindowsLocation { get; set; } = true;
    public string WeatherCity { get; set; } = "";
    public int WeatherIntervalMin { get; set; } = 15;
    public string WeatherPosition { get; set; } = "right";
    public bool SoundsEnabled { get; set; } = true;
    public double SoundVolume { get; set; } = 0.28;
    public bool SoundNotify { get; set; } = true;
    public bool SoundChat { get; set; } = true;
    public bool SoundError { get; set; } = true;
    public bool SoundComplete { get; set; } = true;
    public bool ShowAppBadge { get; set; } = true;
    public string BadgeStyle { get; set; } = "icon-count";
    public string BadgeApps { get; set; } = "";
    public string Density { get; set; } = "comfort";
    public double GlyphSize { get; set; } = 14;
    public double Glass { get; set; }
    public string AccentHex { get; set; } = "";
    public int ChatDurationMs { get; set; } = 2500;
    public int CallDurationMs { get; set; } = 6000;
    public int CompleteDurationMs { get; set; } = 2200;
    public int WarnDurationMs { get; set; } = 4000;
    public int ScreenIndex { get; set; } = -1;
    public bool SuppressFullscreen { get; set; } = true;
    public bool SuppressFocusAssist { get; set; } = true;
    public bool ReduceMotion { get; set; }
    public bool Diagnostics { get; set; }
    public int SettingsSchema { get; set; } = 13;
    public string LastSeenVersion { get; set; } = "";
    /// <summary>Default architecture is fixedHost. resizeHost is an emergency fallback only.</summary>
    public string RenderMode { get; set; } = "fixedHost";
}

internal static class PrefsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static UserPrefs Current { get; private set; } = new();
    public static event Action? Changed;

    public static bool UseFixedHost
    {
        get
        {
            var o = Program.RenderModeOverride;
            if (!string.IsNullOrWhiteSpace(o))
                return !o.Contains("resize", StringComparison.OrdinalIgnoreCase);
            return Current.RenderMode != "resizeHost";
        }
    }

    public static string ActivePath { get; private set; } = PortablePath();

    public static void Load()
    {
        try
        {
            foreach (var path in new[] { PortablePath(), AppDataPath() })
            {
                if (!File.Exists(path)) continue;
                var raw = File.ReadAllText(path);
                var prefs = JsonSerializer.Deserialize<UserPrefs>(raw, JsonOpts);
                if (prefs is null) continue;
                Sanitize(prefs);
                Current = prefs;
                ActivePath = path;
                return;
            }
        }
        catch (Exception ex)
        {
            IslandLog.Write("prefs", ex.Message);
        }
        Current = new UserPrefs();
        ActivePath = CanWrite(PortablePath()) ? PortablePath() : AppDataPath();
    }

    public static void Save()
    {
        Sanitize(Current);
        try
        {
            var path = ActivePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(Current, JsonOpts));
        }
        catch
        {
            try
            {
                ActivePath = AppDataPath();
                var dir = Path.GetDirectoryName(ActivePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ActivePath, JsonSerializer.Serialize(Current, JsonOpts));
            }
            catch
            {
            }
        }
        Changed?.Invoke();
    }

    public static void Mutate(Action<UserPrefs> edit)
    {
        edit(Current);
        Save();
    }

    private static void Sanitize(UserPrefs p)
    {
        if (string.IsNullOrWhiteSpace(p.PaletteId)) p.PaletteId = "midnight";
        if (string.IsNullOrWhiteSpace(p.FontId)) p.FontId = "segoe-variable";
        if (string.IsNullOrWhiteSpace(p.IconStyle)) p.IconStyle = "fluent";
        if (string.IsNullOrWhiteSpace(p.Animation)) p.Animation = "morph";
        if (string.IsNullOrWhiteSpace(p.AnimSpeed)) p.AnimSpeed = "normal";
        if (p.ExpandMs < 120 || p.ExpandMs > 500) p.ExpandMs = 167;
        if (p.CollapseMs < 180 || p.CollapseMs > 600) p.CollapseMs = 333;
        p.UiLanguage = (p.UiLanguage ?? "ru").ToLowerInvariant() switch
        {
            "en" or "english" => "en",
            "system" or "auto" => "system",
            _ => "ru"
        };
        p.AnimSpeed = p.ExpandMs <= 180 ? "fast" : "normal";
        if (double.IsNaN(p.Opacity) || double.IsInfinity(p.Opacity)) p.Opacity = 0.92;
        p.Opacity = Math.Clamp(p.Opacity, 0.45, 1.0);
        if (double.IsNaN(p.BorderThickness) || double.IsInfinity(p.BorderThickness)) p.BorderThickness = 1;
        p.BorderThickness = Math.Clamp(p.BorderThickness, 0, 3);
        if (double.IsNaN(p.GlowStrength) || double.IsInfinity(p.GlowStrength)) p.GlowStrength = 0.55;
        p.GlowStrength = Math.Clamp(p.GlowStrength, 0, 1);
        if (double.IsNaN(p.CornerRadius) || double.IsInfinity(p.CornerRadius)) p.CornerRadius = 0;
        p.CornerRadius = Math.Clamp(p.CornerRadius, 0, 28);
        if (double.IsNaN(p.IdleWidth) || double.IsInfinity(p.IdleWidth)) p.IdleWidth = 180;
        p.IdleWidth = Math.Clamp(p.IdleWidth, 140, 280);
        if (double.IsNaN(p.IdleHeight) || double.IsInfinity(p.IdleHeight)) p.IdleHeight = 40;
        p.IdleHeight = Math.Clamp(p.IdleHeight, 32, 48);
        p.ExpandHeight = p.ExpandHeight || string.Equals(p.ExpandMode, "both", StringComparison.OrdinalIgnoreCase);
        p.ExpandMode = p.ExpandHeight ? "both" : "width";
        if (double.IsNaN(p.MinWidth) || double.IsInfinity(p.MinWidth)) p.MinWidth = 160;
        if (double.IsNaN(p.MaxWidth) || double.IsInfinity(p.MaxWidth)) p.MaxWidth = 480;
        p.MinWidth = Math.Clamp(p.MinWidth, 120, 400);
        p.MaxWidth = Math.Clamp(p.MaxWidth, p.MinWidth + 20, 640);
        p.AnchorH = p.AnchorH?.ToLowerInvariant() switch { "left" => "left", "right" => "right", _ => "center" };
        p.AnchorV = p.AnchorV?.ToLowerInvariant() switch { "center" => "center", "bottom" => "bottom", _ => "top" };
        if (double.IsNaN(p.OffsetX) || double.IsInfinity(p.OffsetX)) p.OffsetX = 0;
        if (double.IsNaN(p.OffsetY) || double.IsInfinity(p.OffsetY)) p.OffsetY = 0;
        p.OffsetX = Math.Clamp(p.OffsetX, -800, 800);
        p.OffsetY = Math.Clamp(p.OffsetY, -800, 800);
        p.Layer = p.Layer?.ToLowerInvariant() switch { "normal" => "normal", "desktop" => "desktop", _ => "topmost" };
        p.ClickMode = (p.ClickMode ?? "").ToLowerInvariant() switch
        {
            "off" or "expand" => "off",
            "player" => "player",
            "center" => "center",
            "cycle-player-center" => "cycle-player-center",
            _ => "cycle-center-player"
        };
        p.ClickOpensActionCenter = p.ClickMode is not "off" and not "player";
        p.TextScale = TypeScale.Normalize(p.TextScale);
        p.IconScale = TypeScale.Normalize(p.IconScale);
        p.GlyphSize = TypeScale.GlyphPx(p.IconScale);
        p.ClockFormat = p.ClockFormat switch
        {
            "HH:mm:ss" => "HH:mm:ss",
            "h:mm tt" => "h:mm tt",
            _ => "HH:mm"
        };
        if (!PaletteCatalog.TryGet(p.PaletteId, out _)) p.PaletteId = "midnight";
        if (!FontCatalog.TryGet(p.FontId, out _)) p.FontId = "segoe-variable";
        p.IconStyle = IslandIcons.Normalize(p.IconStyle);
        if (p.WeatherIntervalMin < 5 || p.WeatherIntervalMin > 60) p.WeatherIntervalMin = 15;
        p.WeatherCity = (p.WeatherCity ?? "").Trim();
        if (p.WeatherCity.Length > 80) p.WeatherCity = p.WeatherCity[..80];
        if (double.IsNaN(p.SoundVolume) || double.IsInfinity(p.SoundVolume)) p.SoundVolume = 0.28;
        p.SoundVolume = Math.Clamp(p.SoundVolume, 0, 1);
        p.WeatherPosition = p.WeatherPosition?.ToLowerInvariant() switch { "hide" => "hide", "expand" => "expand", _ => "right" };
        p.BadgeStyle = p.BadgeStyle?.ToLowerInvariant() switch { "count" => "count", "dot" => "dot", _ => "icon-count" };
        p.Density = p.Density?.ToLowerInvariant() switch { "compact" => "compact", _ => "comfort" };
        if (double.IsNaN(p.GlyphSize) || double.IsInfinity(p.GlyphSize)) p.GlyphSize = 14;
        p.GlyphSize = Math.Clamp(p.GlyphSize, 12, 18);
        if (double.IsNaN(p.Glass) || double.IsInfinity(p.Glass)) p.Glass = 0;
        p.Glass = Math.Clamp(p.Glass, 0, 1);
        p.AccentHex = (p.AccentHex ?? "").Trim();
        if (p.AccentHex.Length > 0 && !p.AccentHex.StartsWith('#')) p.AccentHex = "#" + p.AccentHex;
        if (p.ChatDurationMs < 500 || p.ChatDurationMs > 30000) p.ChatDurationMs = 2500;
        if (p.CallDurationMs < 500 || p.CallDurationMs > 30000) p.CallDurationMs = 6000;
        if (p.CompleteDurationMs < 500 || p.CompleteDurationMs > 30000) p.CompleteDurationMs = 2200;
        if (p.WarnDurationMs < 500 || p.WarnDurationMs > 30000) p.WarnDurationMs = 4000;
        p.BadgeApps = (p.BadgeApps ?? "").Trim();
        if (p.SettingsSchema < 13)
        {
            p.SettingsSchema = 13;
            if (p.ScreenIndex < -1) p.ScreenIndex = -1;
        }
        p.ScreenIndex = Math.Clamp(p.ScreenIndex, -1, 15);
        p.Animation = p.Animation.ToLowerInvariant() switch
        {
            "pulse" => "pulse",
            "breathe" => "breathe",
            "none" => "none",
            _ => "morph"
        };
        p.RenderMode = (p.RenderMode ?? "").ToLowerInvariant() switch
        {
            "resizehost" or "resize" or "hwnd" => "resizeHost",
            _ => "fixedHost"
        };
        if (p.NotifyDurationMs < 500 || p.NotifyDurationMs > 30000) p.NotifyDurationMs = 4000;
    }

    private static string PortablePath() => Path.Combine(AppContext.BaseDirectory, "notifyisland.settings.json");

    private static string AppDataPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotifyIsland", "settings.json");

    private static bool CanWrite(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return false;
            Directory.CreateDirectory(dir);
            var probe = path + ".probe";
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
