using System;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NotifyIsland;

/// <summary>
/// Windows-primary weather pipeline. No third-party HTTP (Open-Meteo / MSN / etc.).
/// Order: WinRT geolocation hint → Bing Weather widget local cache → on-disk cache → LocalStub.
/// </summary>
public interface IWeatherSource
{
    Task<OverlayPayload> GetWeatherAsync(CancellationToken ct = default);
}

public sealed class WindowsWeatherSource : IWeatherSource
{
    private readonly double _lat;
    private readonly double _lon;

    public WindowsWeatherSource(double latitude = 55.75, double longitude = 37.62)
    {
        _lat = latitude;
        _lon = longitude;
    }

    public async Task<OverlayPayload> GetWeatherAsync(CancellationToken ct = default)
    {
        // 2026-10-02 perf pass: every step below touches the disk, and step 2 recursively
        // enumerates whole package directories under %LOCALAPPDATA%\Packages. This method is
        // awaited from the UI thread (the weather timer), and the old `await Task.Yield()`
        // posted the continuation BACK to that same UI thread — so the walk ran on it, and a
        // machine with a Widgets board saw a visible stall every refresh. Task.Run moves the
        // whole pipeline to the pool and the island's dispatcher stays free.
        return await Task.Run(() => GetWeatherCore(ct), ct).ConfigureAwait(false);
    }

    private static OverlayPayload GetWeatherCore(CancellationToken ct)
    {
        // 1) WinRT geolocation — available on full Win11; often denied/missing in Sandbox.
        _ = TryWinRtGeolocation();

        // 2) Microsoft Bing Weather / Widgets local package state (best-effort, version-fragile).
        var fromBing = TryReadBingWeatherCache();
        if (fromBing is not null)
        {
            PersistCache(fromBing);
            return fromBing;
        }

        // 3) Prior on-disk cache from a successful Windows read.
        var fromDisk = TryReadLocalCache();
        if (fromDisk is not null)
            return fromDisk;

        // 4) Local stub — no network. Keeps the UI usable in Sandbox / non-Windows.
        AppLog.Warn("Windows weather unavailable; using LocalStubWeather");
        var stub = new WindowsWeatherSource().LocalStubWeather();
        PersistCache(stub);
        return stub;
    }

    /// <summary>
    /// Attempts WinRT Geolocator. Compiled without WinRT refs on Linux CI —
    /// reflection keeps the project portable; real Win11 can resolve the type.
    /// </summary>
    internal static (double Lat, double Lon)? TryWinRtGeolocation()
    {
        try
        {
            var geolocatorType = Type.GetType(
                "Windows.Devices.Geolocation.Geolocator, Windows, ContentType=WindowsRuntime")
                ?? Type.GetType("Windows.Devices.Geolocation.Geolocator");
            if (geolocatorType is null)
                return null;
            // Presence check only in this build; full async GetGeopositionAsync
            // requires CsWinRT packaging. Documented as primary path on Win11.
            AppLog.Warn("WinRT Geolocator type present (coords not resolved in portable build)");
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Warn("TryWinRtGeolocation skipped", ex);
            return null;
        }
    }

    /// <summary>
    /// The file that last produced a usable reading. Remembering it turns every later refresh
    /// into one File.ReadAllText instead of a recursive walk of three package trees, and the
    /// walk only runs again if that file stops parsing or disappears.
    /// </summary>
    private static string? _bingCachePath;

    /// <summary>
    /// Best-effort read of Bing Weather / Windows Widgets local state under LocalAppData.
    /// Paths vary by Windows build; returns null when not found or unreadable.
    /// </summary>
    internal static OverlayPayload? TryReadBingWeatherCache()
    {
        var known = Volatile.Read(ref _bingCachePath);
        if (known is not null)
        {
            var cached = TryReadWeatherFile(known, requireNameMatch: false);
            if (cached is not null)
                return cached;
            // The package was updated or removed — forget it and fall back to the full walk.
            Interlocked.CompareExchange(ref _bingCachePath, null, known);
        }

        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var packages = Path.Combine(local, "Packages");
            if (!Directory.Exists(packages))
                return null;

            // Common package family name prefixes on Win11.
            string[] prefixes =
            {
                "Microsoft.BingWeather",
                "MicrosoftWindows.Client.WebExperience",
                "Microsoft.WidgetsPlatformRuntime"
            };

            foreach (var dir in Directory.EnumerateDirectories(packages))
            {
                var name = Path.GetFileName(dir);
                if (prefixes.All(p => !name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    continue;

                foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    var parsed = TryReadWeatherFile(file, requireNameMatch: true);
                    if (parsed is not null)
                    {
                        Interlocked.Exchange(ref _bingCachePath, file);
                        return parsed;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("TryReadBingWeatherCache failed", ex);
        }
        return null;
    }

    private static OverlayPayload? TryReadWeatherFile(string file, bool requireNameMatch)
    {
        try
        {
            if (requireNameMatch)
            {
                var fn = Path.GetFileName(file).ToLowerInvariant();
                if (!(fn.Contains("weather") || fn.Contains("forecast") || fn.EndsWith(".json")))
                    return null;
            }

            var info = new FileInfo(file);
            if (!info.Exists || info.Length is < 8 or > 2_000_000)
                return null;

            return TryParseWeatherJson(File.ReadAllText(file));
        }
        catch (Exception ex)
        {
            // A single unreadable file must not abort the walk over the rest of the package.
            AppLog.Warn($"Bing weather candidate unreadable: {file}", ex);
            return null;
        }
    }

    internal static OverlayPayload? TryParseWeatherJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!TryFindNumber(root, out var temp, "temperature", "temp", "Temperature", "tempC", "temperatureC")
                || !TryFindInt(root, out var code, "weatherCode", "WeatherCode", "iconCode", "conditionCode", "wmo"))
            {
                // Some caches store condition as string only — map a few Russian/English labels.
                if (!TryFindNumber(root, out temp, "temperature", "temp", "Temperature", "tempC"))
                    return null;
                code = 0;
                if (TryFindString(root, out var cond, "condition", "skyText", "caption", "phrase"))
                    code = MapPhraseToCode(cond);
            }

            double? precip = null;
            if (TryFindNumber(root, out var pp, "precip", "precipitation", "precipProbability", "PrecipProb"))
                precip = pp <= 1 ? pp * 100 : pp;

            return WeatherCodes.ToPayload(temp, code, precip);
        }
        catch
        {
            return null;
        }
    }

    private static int MapPhraseToCode(string phrase)
    {
        var p = phrase.ToLowerInvariant();
        if (p.Contains("thunder") || p.Contains("гроз")) return 95;
        if (p.Contains("snow") || p.Contains("снег")) return 71;
        if (p.Contains("rain") || p.Contains("дожд") || p.Contains("ливень")) return 63;
        if (p.Contains("fog") || p.Contains("туман")) return 45;
        if (p.Contains("cloud") || p.Contains("облач") || p.Contains("пасмур")) return 3;
        if (p.Contains("clear") || p.Contains("sunny") || p.Contains("ясн")) return 0;
        return 2;
    }

    private static bool TryFindNumber(JsonElement el, out double value, params string[] names)
    {
        value = 0;
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                foreach (var n in names)
                {
                    if (prop.Name.Equals(n, StringComparison.OrdinalIgnoreCase)
                        && prop.Value.ValueKind == JsonValueKind.Number
                        && prop.Value.TryGetDouble(out value))
                        return true;
                }
                if (TryFindNumber(prop.Value, out value, names))
                    return true;
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (TryFindNumber(item, out value, names))
                    return true;
            }
        }
        return false;
    }

    private static bool TryFindInt(JsonElement el, out int value, params string[] names)
    {
        value = 0;
        if (!TryFindNumber(el, out var d, names)) return false;
        value = (int)Math.Round(d);
        return true;
    }

    private static bool TryFindString(JsonElement el, out string value, params string[] names)
    {
        value = "";
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                foreach (var n in names)
                {
                    if (prop.Name.Equals(n, StringComparison.OrdinalIgnoreCase)
                        && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        value = prop.Value.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(value)) return true;
                    }
                }
                if (TryFindString(prop.Value, out value, names))
                    return true;
            }
        }
        return false;
    }

    internal static OverlayPayload? TryReadLocalCache()
    {
        try
        {
            var path = AppSettings.WeatherCachePath;
            if (!File.Exists(path)) return null;
            var dto = JsonSerializer.Deserialize<WeatherCacheDto>(File.ReadAllText(path));
            if (dto is null) return null;
            return WeatherCodes.ToPayload(dto.TemperatureC, dto.WeatherCode, dto.PrecipProb);
        }
        catch (Exception ex)
        {
            AppLog.Warn("TryReadLocalCache failed", ex);
            return null;
        }
    }

    internal static void PersistCache(OverlayPayload p)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.SettingsDirectory);
            var dto = new WeatherCacheDto
            {
                TemperatureC = p.TemperatureC ?? 18,
                WeatherCode = p.WeatherCode ?? 0,
                PrecipProb = p.PrecipProb,
                SavedUtc = DateTime.UtcNow
            };
            File.WriteAllText(AppSettings.WeatherCachePath,
                JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AppLog.Warn("PersistCache failed", ex);
        }
    }

    /// <summary>Deterministic local stub — no network. Uses lat seed lightly for variety.</summary>
    public OverlayPayload LocalStubWeather()
    {
        // Stable values: clear 18° Moscow-like; slight variation from lon hash unused for predictability.
        _ = (_lat, _lon);
        return WeatherCodes.MockMoscow();
    }

    private sealed class WeatherCacheDto
    {
        public double TemperatureC { get; set; }
        public int WeatherCode { get; set; }
        public double? PrecipProb { get; set; }
        public DateTime SavedUtc { get; set; }
    }
}
