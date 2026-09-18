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
    public bool OverlayVisible { get; set; } = true;
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
        if (double.IsNaN(p.Opacity) || double.IsInfinity(p.Opacity)) p.Opacity = 0.92;
        p.Opacity = Math.Clamp(p.Opacity, 0.45, 1.0);
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
