using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Windows.Devices.Geolocation;

namespace NotifyIsland;

internal sealed record WeatherSnapshot
{
    public bool Ok { get; init; }
    public double TemperatureC { get; init; }
    public int WeatherCode { get; init; }
    public string City { get; init; } = "";
    public string Condition { get; init; } = "";
    public DateTimeOffset FetchedUtc { get; init; }
    public bool FromCache { get; init; }
}

internal static class WeatherHub
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static WeatherSnapshot? _live;
    private static DispatcherTimer? _timer;
    private static bool _started;

    public static WeatherSnapshot? Current => _live is { Ok: true } ? _live : null;
    public static string Status { get; private set; } = "Off";
    public static event Action? Changed;

    public static void Start()
    {
        if (_started) return;
        _started = true;
        PrefsStore.Changed += () => Dispatcher.UIThread.Post(RestartTimer);
        RestartTimer();
        _ = RefreshAsync(false);
    }

    public static async Task RefreshAsync(bool force)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var prefs = PrefsStore.Current;
            if (!prefs.ShowWeather)
            {
                _live = null;
                Status = "Hidden";
                Raise();
                return;
            }

            var cached = ReadCache();
            if (!force && cached is { Ok: true } &&
                DateTimeOffset.UtcNow - cached.FetchedUtc < TimeSpan.FromMinutes(Math.Max(5, prefs.WeatherIntervalMin)))
            {
                _live = cached with { FromCache = true };
                Status = "Cached";
                Raise();
                return;
            }

            try
            {
                var (lat, lon, city) = await ResolvePlaceAsync(prefs).ConfigureAwait(false);
                if (lat is null || lon is null)
                {
                    ApplyOffline(cached, "No location");
                    return;
                }

                var url =
                    $"https://api.open-meteo.com/v1/forecast?latitude={lat.Value.ToString(CultureInfo.InvariantCulture)}" +
                    $"&longitude={lon.Value.ToString(CultureInfo.InvariantCulture)}" +
                    "&current=temperature_2m,weather_code&timezone=auto";
                using var resp = await Http.GetAsync(url).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    ApplyOffline(cached, "HTTP " + (int)resp.StatusCode);
                    return;
                }

                await using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
                if (!doc.RootElement.TryGetProperty("current", out var cur) ||
                    !cur.TryGetProperty("temperature_2m", out var tEl) ||
                    !cur.TryGetProperty("weather_code", out var cEl))
                {
                    ApplyOffline(cached, "Bad payload");
                    return;
                }

                var temp = tEl.GetDouble();
                var code = cEl.GetInt32();
                var snap = new WeatherSnapshot
                {
                    Ok = true,
                    TemperatureC = temp,
                    WeatherCode = code,
                    City = string.IsNullOrWhiteSpace(city) ? (prefs.WeatherCity ?? "").Trim() : city,
                    Condition = ConditionName(code),
                    FetchedUtc = DateTimeOffset.UtcNow,
                    FromCache = false
                };
                _live = snap;
                WriteCache(snap);
                Status = "Live";
                Raise();
            }
            catch
            {
                ApplyOffline(cached, "Offline");
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    public static string TempLabel(WeatherSnapshot s)
    {
        var n = (int)Math.Round(s.TemperatureC);
        return n.ToString(CultureInfo.InvariantCulture) + "°";
    }

    public static string ExpandedLine(WeatherSnapshot s)
    {
        var city = string.IsNullOrWhiteSpace(s.City) ? s.Condition : s.City;
        return city + " · " + s.Condition;
    }

    public static WeatherGlyph GlyphFor(int code)
    {
        if (code == 0) return WeatherGlyph.Sun;
        if (code is >= 1 and <= 3) return WeatherGlyph.Partly;
        if (code is 45 or 48) return WeatherGlyph.Fog;
        if (code is >= 71 and <= 77 or 85 or 86) return WeatherGlyph.Snow;
        if (code is >= 95 and <= 99) return WeatherGlyph.Thunder;
        if (code is >= 51 and <= 67 or >= 80 and <= 82) return WeatherGlyph.Rain;
        return WeatherGlyph.Cloud;
    }

    public static string ConditionName(int code) => code switch
    {
        0 => "Clear",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        >= 51 and <= 55 => "Drizzle",
        >= 56 and <= 57 => "Freezing drizzle",
        >= 61 and <= 65 => "Rain",
        >= 66 and <= 67 => "Freezing rain",
        >= 71 and <= 77 => "Snow",
        >= 80 and <= 82 => "Showers",
        >= 85 and <= 86 => "Snow showers",
        >= 95 and <= 99 => "Thunder",
        _ => "Clouds"
    };

    private static void RestartTimer()
    {
        _timer ??= new DispatcherTimer();
        _timer.Stop();
        _timer.Tick -= OnTick;
        if (!PrefsStore.Current.ShowWeather) return;
        var min = Math.Clamp(PrefsStore.Current.WeatherIntervalMin, 5, 60);
        _timer.Interval = TimeSpan.FromMinutes(min);
        _timer.Tick += OnTick;
        _timer.Start();
        _ = RefreshAsync(false);
    }

    private static void OnTick(object? sender, EventArgs e) => _ = RefreshAsync(true);

    private static async Task<(double? lat, double? lon, string city)> ResolvePlaceAsync(UserPrefs prefs)
    {
        if (prefs.UseWindowsLocation)
        {
            try
            {
                var access = await Geolocator.RequestAccessAsync();
                if (access == GeolocationAccessStatus.Allowed)
                {
                    var geo = new Geolocator { DesiredAccuracyInMeters = 5000 };
                    var pos = await geo.GetGeopositionAsync();
                    var c = pos.Coordinate.Point.Position;
                    return (c.Latitude, c.Longitude, "");
                }
            }
            catch
            {
            }
        }

        var name = (prefs.WeatherCity ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return (null, null, "");
        var q = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&name=" + Uri.EscapeDataString(name);
        using var resp = await Http.GetAsync(q).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return (null, null, name);
        await using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("results", out var arr) || arr.GetArrayLength() == 0)
            return (null, null, name);
        var first = arr[0];
        var lat = first.GetProperty("latitude").GetDouble();
        var lon = first.GetProperty("longitude").GetDouble();
        var city = first.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? name : name;
        return (lat, lon, city);
    }

    private static void ApplyOffline(WeatherSnapshot? cached, string why)
    {
        if (cached is { Ok: true })
        {
            _live = cached with { FromCache = true };
            Status = "Cached (" + why + ")";
        }
        else
        {
            _live = null;
            Status = why;
        }
        Raise();
    }

    private static string CachePath()
    {
        var dir = Path.GetDirectoryName(PrefsStore.ActivePath);
        if (string.IsNullOrEmpty(dir)) dir = AppContext.BaseDirectory;
        return Path.Combine(dir, "notifyisland.weather.json");
    }

    private static WeatherSnapshot? ReadCache()
    {
        try
        {
            var path = CachePath();
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<WeatherSnapshot>(File.ReadAllText(path), JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteCache(WeatherSnapshot snap)
    {
        try
        {
            File.WriteAllText(CachePath(), JsonSerializer.Serialize(snap, JsonOpts));
        }
        catch
        {
        }
    }

    private static void Raise() => Dispatcher.UIThread.Post(() => Changed?.Invoke());
}
