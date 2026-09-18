using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotifyIsland;

public sealed class UserPrefs
{
    public string PaletteId { get; set; } = "midnight";
    public string FontId { get; set; } = "segoe-variable";
    public string IconStyle { get; set; } = "fluent";
    public double Opacity { get; set; } = 0.92;
    public string Animation { get; set; } = "morph";
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
    public string AnchorH { get; set; } = "center";
    public string AnchorV { get; set; } = "top";
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public string Layer { get; set; } = "topmost";
    public bool ClickOpensActionCenter { get; set; } = true;
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
        catch
        {
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
        p.AnimSpeed = p.AnimSpeed.Equals("fast", StringComparison.OrdinalIgnoreCase) ? "fast" : "normal";
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
        p.ExpandMode = string.Equals(p.ExpandMode, "both", StringComparison.OrdinalIgnoreCase) ? "both" : "width";
        p.AnchorH = p.AnchorH?.ToLowerInvariant() switch { "left" => "left", "right" => "right", _ => "center" };
        p.AnchorV = p.AnchorV?.ToLowerInvariant() switch { "center" => "center", "bottom" => "bottom", _ => "top" };
        if (double.IsNaN(p.OffsetX) || double.IsInfinity(p.OffsetX)) p.OffsetX = 0;
        if (double.IsNaN(p.OffsetY) || double.IsInfinity(p.OffsetY)) p.OffsetY = 0;
        p.OffsetX = Math.Clamp(p.OffsetX, -800, 800);
        p.OffsetY = Math.Clamp(p.OffsetY, -800, 800);
        p.Layer = p.Layer?.ToLowerInvariant() switch { "normal" => "normal", "desktop" => "desktop", _ => "topmost" };
        p.ClockFormat = p.ClockFormat switch
        {
            "HH:mm:ss" => "HH:mm:ss",
            "h:mm tt" => "h:mm tt",
            _ => "HH:mm"
        };
        if (!PaletteCatalog.TryGet(p.PaletteId, out _)) p.PaletteId = "midnight";
        if (!FontCatalog.TryGet(p.FontId, out _)) p.FontId = "segoe-variable";
        p.IconStyle = p.IconStyle.ToLowerInvariant() switch
        {
            "mdl2" => "mdl2",
            "minimal" => "minimal",
            _ => "fluent"
        };
        p.Animation = p.Animation.ToLowerInvariant() switch
        {
            "pulse" => "pulse",
            "breathe" => "breathe",
            "none" => "none",
            _ => "morph"
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
