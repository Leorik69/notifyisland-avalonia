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
            var xml = File.ReadAllText(path);
            xml = xml.Replace("currentColor", hex, StringComparison.OrdinalIgnoreCase);
            xml = xml.Replace("stroke-width=\"2\"", "stroke-width=\"1.75\"", StringComparison.OrdinalIgnoreCase);

            var loaded = SvgSource.LoadFromSvg(xml);
            if (loaded is null) return null;

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

            var xml = File.ReadAllText(path);
            // Monochrome uses black/white fills in the npm package; tint black to theme brush.
            if (pack.Equals(MeteoconsMap.Monochrome, StringComparison.OrdinalIgnoreCase))
            {
                var hex = BrushToHex(brush);
                xml = xml.Replace("currentColor", hex, StringComparison.OrdinalIgnoreCase);
                xml = xml.Replace("\"black\"", $"\"{hex}\"", StringComparison.OrdinalIgnoreCase);
                xml = xml.Replace("'black'", $"'{hex}'", StringComparison.OrdinalIgnoreCase);
            }

            var loaded = SvgSource.LoadFromSvg(xml);
            if (loaded is null) return null;

            return MakeImage(loaded, size);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Meteocons SVG load failed ({pack}/{key})", ex);
            return null;
        }
    }

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
