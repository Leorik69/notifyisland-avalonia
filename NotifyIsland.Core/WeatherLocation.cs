using System;

namespace NotifyIsland;

/// <summary>Where the weather location label / coords come from.</summary>
public enum WeatherLocationMode
{
    /// <summary>System / Windows weather cache + WinRT geolocation hint (default).</summary>
    Windows,
    /// <summary>User-chosen city name (+ optional lat/lon). Temp may still be Windows cache.</summary>
    Manual
}

/// <summary>Built-in city presets for Manual weather location.</summary>
public static class WeatherLocationPresets
{
    public sealed record City(string Name, double Lat, double Lon);

    public static readonly City[] All =
    [
        new("Москва", 55.75, 37.62),
        new("Санкт-Петербург", 59.93, 30.33),
        new("Новосибирск", 55.03, 82.92),
        new("Екатеринбург", 56.84, 60.60),
        new("Казань", 55.80, 49.11),
        new("Нижний Новгород", 56.33, 44.00),
        new("Владивосток", 43.12, 131.89)
    ];

    public static City? FindByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var c in All)
        {
            if (string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                return c;
        }
        return null;
    }
}
