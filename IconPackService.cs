using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Svg.Skia;

namespace NotifyIsland;

/// <summary>
/// Loads outline icons from Assets/Icons/{pack}/ SVG files (Tabler MIT, Lucide ISC)
/// with fallback to built-in <see cref="IslandIcons"/> geometries.
/// </summary>
public static class IconPackService
{
    private static readonly string[] KnownPacks = ["IslandIcons", "Tabler", "Lucide"];

    public static IReadOnlyList<string> PackIds => KnownPacks;

    public static bool IsSvgPack(string? pack) =>
        string.Equals(pack, "Tabler", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(pack, "Lucide", StringComparison.OrdinalIgnoreCase);

    public static string NormalizePack(string? pack)
    {
        if (string.IsNullOrWhiteSpace(pack)) return "IslandIcons";
        var p = pack.Trim();
        foreach (var k in KnownPacks)
            if (k.Equals(p, StringComparison.OrdinalIgnoreCase)) return k;
        return "IslandIcons";
    }

    /// <summary>Create an icon control for the active pack (SVG Image or Path).</summary>
    public static Avalonia.Controls.Control Create(string packId, string key, double size, IBrush? stroke = null, double strokeThickness = -1)
    {
        var pack = NormalizePack(packId);
        var brush = stroke ?? new SolidColorBrush(Color.Parse(OverlayTokens.TextSecondaryHex));
        if (IsSvgPack(pack) && TryCreateSvg(pack, key, size, brush) is { } svg)
            return svg;
        return IslandIcons.Create(key, size, brush, strokeThickness);
    }

    private static Avalonia.Controls.Control? TryCreateSvg(string pack, string key, double size, IBrush brush)
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

            return new Avalonia.Controls.Image
            {
                Source = new SvgImage { Source = loaded },
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                IsHitTestVisible = false
            };
        }
        catch (Exception ex)
        {
            AppLog.Warn($"IconPack SVG load failed ({pack}/{key})", ex);
            return null;
        }
    }

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
