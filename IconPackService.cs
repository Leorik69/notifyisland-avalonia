using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Svg.Skia;

namespace NotifyIsland;

/// <summary>
/// Loads icons from Assets/Icons/{pack}/ SVG files (Tabler MIT, Lucide ISC, Meteocons MIT)
/// with fallback to built-in <see cref="IslandIcons"/> geometries.
/// Meteocons packs are weather-only; other keys fall back to IslandIcons.
/// </summary>
public static class IconPackService
{
    private static readonly string[] OutlinePacks = ["IslandIcons", "Tabler", "Lucide"];

    /// <summary>
    /// Parsed <see cref="SvgSource"/> cache, keyed by pack + key + tint hex + size.
    ///
    /// Paint() runs on the 200 ms tick and calls <see cref="Create"/> for the battery and weather
    /// icons, so without a cache that is 5 file reads plus 5 SVG parses every second for icons
    /// that change a handful of times an hour. The SvgSource is safe to share: it is a parsed,
    /// read-only resource, and <see cref="MakeImage"/> wraps it in a fresh Image each time because
    /// a control can only ever have one parent. Same pattern as
    /// <see cref="SecondsStripView.EnsureDotSource"/>.
    /// </summary>
    private static readonly Dictionary<string, SvgSource> SvgSourceCache = new(StringComparer.Ordinal);

    public static IReadOnlyList<string> PackIds
    {
        get
        {
            var list = new List<string>(OutlinePacks.Length + MeteoconsMap.PackIds.Length);
            list.AddRange(OutlinePacks);
            list.AddRange(MeteoconsMap.PackIds);
            return list;
        }
    }

    public static bool IsSvgPack(string? pack) =>
        string.Equals(pack, "Tabler", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(pack, "Lucide", StringComparison.OrdinalIgnoreCase) ||
        MeteoconsMap.IsMeteoconsPack(pack);

    public static bool IsMeteoconsPack(string? pack) => MeteoconsMap.IsMeteoconsPack(pack);

    public static string NormalizePack(string? pack)
    {
        if (string.IsNullOrWhiteSpace(pack)) return "IslandIcons";
        var p = pack.Trim();
        foreach (var k in PackIds)
            if (k.Equals(p, StringComparison.OrdinalIgnoreCase)) return k;
        return "IslandIcons";
    }

    /// <summary>Create an icon control for the active pack (SVG Image or Path).</summary>
    public static Avalonia.Controls.Control Create(string packId, string key, double size, IBrush? stroke = null, double strokeThickness = -1)
    {
        var pack = NormalizePack(packId);
        var brush = stroke ?? new SolidColorBrush(Color.Parse(OverlayTokens.TextSecondaryHex));

        if (IsMeteoconsPack(pack))
        {
            if (!MeteoconsMap.IsWeatherKey(key))
                return IslandIcons.Create(key, size, brush, strokeThickness);
            if (TryCreateMeteoconSvg(pack, key, size, brush) is { } meteo)
                return MeteoconsMotion.Wrap(meteo, key, size);
            return IslandIcons.Create(key, size, brush, strokeThickness);
        }

        if (IsSvgPack(pack) && TryCreateOutlineSvg(pack, key, size, brush) is { } svg)
            return svg;
        return IslandIcons.Create(key, size, brush, strokeThickness);
    }

    private static Avalonia.Controls.Control? TryCreateOutlineSvg(string pack, string key, double size, IBrush brush)
    {
        try
        {
            var path = ResolveSvgPath(pack, key);
            if (path is null || !File.Exists(path)) return null;

            var hex = BrushToHex(brush);
            var cacheKey = IconCacheKey.For(pack, key, hex, size);
            if (!SvgSourceCache.TryGetValue(cacheKey, out var loaded))
            {
                var xml = File.ReadAllText(path);
                xml = xml.Replace("currentColor", hex, StringComparison.OrdinalIgnoreCase);
                xml = xml.Replace("stroke-width=\"2\"", "stroke-width=\"1.75\"", StringComparison.OrdinalIgnoreCase);

                loaded = SvgSource.LoadFromSvg(xml);
                if (loaded is null) return null;
                SvgSourceCache[cacheKey] = loaded;
            }

            return MakeImage(loaded, size);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"IconPack SVG load failed ({pack}/{key})", ex);
            return null;
        }
    }

    private static Avalonia.Controls.Control? TryCreateMeteoconSvg(string pack, string key, double size, IBrush brush)
    {
        try
        {
            var path = ResolveSvgPath(pack, key);
            if (path is null || !File.Exists(path)) return null;

            // The hex only participates in the cache key for the monochrome pack: for the other
            // three packs the colour is a paint-time tint that does not alter the parsed source,
            // so keying on it would grow the cache for nothing. Monochrome bakes black into the
            // XML, so it must be part of the key. IconCacheKey owns that decision.
            var hex = BrushToHex(brush);
            var isMono = pack.Equals(MeteoconsMap.Monochrome, StringComparison.OrdinalIgnoreCase);
            var cacheKey = IconCacheKey.For(pack, key, hex, size);
            if (!SvgSourceCache.TryGetValue(cacheKey, out var loaded))
            {
                var xml = File.ReadAllText(path);
                // Monochrome uses black/white fills in the npm package; tint black to theme brush.
                if (isMono)
                {
                    xml = xml.Replace("currentColor", hex, StringComparison.OrdinalIgnoreCase);
                    xml = xml.Replace("\"black\"", $"\"{hex}\"", StringComparison.OrdinalIgnoreCase);
                    xml = xml.Replace("'black'", $"'{hex}'", StringComparison.OrdinalIgnoreCase);
                }

                loaded = SvgSource.LoadFromSvg(xml);
                if (loaded is null) return null;
                SvgSourceCache[cacheKey] = loaded;
            }

            return MakeImage(loaded, size);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Meteocons SVG load failed ({pack}/{key})", ex);
            return null;
        }
    }

    /// <summary>
    /// Drop the parsed-source cache. Test-only seam: the cache is a pure memo of files on disk,
    /// so clearing it cannot change behaviour — it just lets a test assert cold-vs-warm behaviour
    /// without depending on another test having run first.
    /// </summary>
    internal static void ResetSvgCacheForTests() => SvgSourceCache.Clear();

    private static Avalonia.Controls.Image MakeImage(SvgSource loaded, double size) =>
        new()
        {
            Source = new SvgImage { Source = loaded },
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        };

    private static string? ResolveSvgPath(string pack, string key)
    {
        var file = key.Trim() + ".svg";
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", pack, file),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
            {
                var p = Path.Combine(dir.FullName, "Assets", "Icons", pack, file);
                if (File.Exists(p)) return p;
            }
        }
        catch { /* ignore */ }
        return null;
    }

    private static string BrushToHex(IBrush brush)
    {
        if (brush is SolidColorBrush sc)
            return $"#{sc.Color.R:X2}{sc.Color.G:X2}{sc.Color.B:X2}";
        return OverlayTokens.TextSecondaryHex;
    }
}
