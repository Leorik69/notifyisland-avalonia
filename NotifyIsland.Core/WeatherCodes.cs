namespace NotifyIsland;

/// <summary>WMO-like weather codes → Russian short labels + icon keys (IslandIcons).</summary>
public static class WeatherCodes
{
    public static string IconKey(int code) => code switch
    {
        0 => "weather-clear",
        1 or 2 => "weather-partly",
        3 => "weather-cloud",
        45 or 48 => "weather-fog",
        51 or 53 or 55 or 56 or 57 => "weather-drizzle",
        61 or 63 or 65 or 66 or 67 => "weather-rain",
        71 or 73 or 75 or 77 => "weather-snow",
        80 or 81 or 82 => "weather-rain",
        85 or 86 => "weather-snow",
        95 or 96 or 99 => "weather-storm",
        _ => "weather-cloud"
    };

    /// <summary>Legacy emoji glyph for tests / text fallbacks.</summary>
    public static string Glyph(int code) => code switch
    {
        0 => "☀",
        1 or 2 => "🌤",
        3 => "☁",
        45 or 48 => "🌫",
        51 or 53 or 55 or 56 or 57 => "🌦",
        61 or 63 or 65 or 66 or 67 => "🌧",
        71 or 73 or 75 or 77 => "❄",
        80 or 81 or 82 => "🌧",
        85 or 86 => "❄",
        95 or 96 or 99 => "⛈",
        _ => "☁"
    };

    public static string LabelRu(int code) => code switch
    {
        0 => "Ясно",
        1 => "Преим. ясно",
        2 => "Перем. облачность",
        3 => "Пасмурно",
        45 or 48 => "Туман",
        51 or 53 or 55 => "Морось",
        56 or 57 => "Ледяная морось",
        61 => "Небольшой дождь",
        63 => "Дождь",
        65 => "Сильный дождь",
        66 or 67 => "Ледяной дождь",
        71 => "Небольшой снег",
        73 => "Снег",
        75 => "Сильный снег",
        77 => "Снежные зёрна",
        80 or 81 or 82 => "Ливень",
        85 or 86 => "Снегопад",
        95 => "Гроза",
        96 or 99 => "Гроза с градом",
        _ => "Погода"
    };

    public static string FormatExpanded(double tempC, int code, double? precipProb)
    {
        var label = LabelRu(code);
        var temp = $"{(int)Math.Round(tempC)}°";
        if (precipProb is { } p && !double.IsNaN(p))
            return $"{label} · {temp} · {(int)Math.Round(Math.Clamp(p, 0, 100))}%";
        return $"{label} · {temp}";
    }

    public static string FormatMinimalTemp(double tempC) => $"{(int)Math.Round(tempC)}°";

    public static OverlayPayload ToPayload(double tempC, int code, double? precipProb = null)
    {
        var label = LabelRu(code);
        var precip = precipProb is { } p && !double.IsNaN(p) ? Math.Clamp(p, 0, 100) : (double?)null;
        return new OverlayPayload
        {
            Title = label,
            Subtitle = FormatMinimalTemp(tempC),
            Body = FormatExpanded(tempC, code, precip),
            TemperatureC = tempC,
            WeatherCode = code,
            PrecipProb = precip
        };
    }

    /// <summary>Local demo stub (Moscow-like) when Windows weather is unavailable.</summary>
    public static OverlayPayload MockMoscow() => ToPayload(18, 0, 0);
}
