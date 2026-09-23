using System;
using System.IO;
using System.Text.Json;

namespace NotifyIsland;

/// <summary>Simple JSON settings under %LOCALAPPDATA%/NotifyIsland/settings.json.</summary>
public sealed class AppSettings
{
    public bool WeatherEnabled { get; set; } = true;
    public double Latitude { get; set; } = 55.75;
    public double Longitude { get; set; } = 37.62;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
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
                var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
                if (s is not null) return s;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("AppSettings.Load failed", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch (Exception ex)
        {
            AppLog.Warn("AppSettings.Save failed", ex);
        }
    }
}
