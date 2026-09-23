namespace NotifyIsland;

/// <summary>
/// Meteocons (Bas Milius, MIT) pack ids and slug mapping for weather keys.
/// Four official styles: fill / flat / line / monochrome.
/// </summary>
public static class MeteoconsMap
{
    public const string Fill = "MeteoconsFill";
    public const string Flat = "MeteoconsFlat";
    public const string Line = "MeteoconsLine";
    public const string Monochrome = "MeteoconsMonochrome";

    public static readonly string[] PackIds = [Fill, Flat, Line, Monochrome];

    /// <summary>Upstream slug used when vendoring (filename without .svg stays as weather-* key).</summary>
    public static readonly IReadOnlyDictionary<string, string> UpstreamSlug = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["weather-clear"] = "clear-day",
        ["weather-partly"] = "partly-cloudy-day",
        ["weather-cloud"] = "cloudy",
        ["weather-fog"] = "fog",
        ["weather-drizzle"] = "drizzle",
        ["weather-rain"] = "rain",
        ["weather-snow"] = "snow",
        ["weather-storm"] = "thunderstorms",
        ["weather-sleet"] = "sleet",
    };

    public static bool IsMeteoconsPack(string? pack)
    {
        if (string.IsNullOrWhiteSpace(pack)) return false;
        foreach (var id in PackIds)
            if (id.Equals(pack.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static string? StyleFolder(string packId) => packId switch
    {
        _ when Fill.Equals(packId, StringComparison.OrdinalIgnoreCase) => "fill",
        _ when Flat.Equals(packId, StringComparison.OrdinalIgnoreCase) => "flat",
        _ when Line.Equals(packId, StringComparison.OrdinalIgnoreCase) => "line",
        _ when Monochrome.Equals(packId, StringComparison.OrdinalIgnoreCase) => "monochrome",
        _ => null
    };

    public static bool IsWeatherKey(string key) =>
        UpstreamSlug.ContainsKey(key) ||
        key.StartsWith("weather-", StringComparison.OrdinalIgnoreCase);
}
