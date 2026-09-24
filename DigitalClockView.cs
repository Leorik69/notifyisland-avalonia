using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Svg.Skia;

namespace NotifyIsland;

/// <summary>
/// Builds / refreshes a horizontal row of FontAudio digital segment SVGs
/// for the collapsed Idle clock. Tints via <c>currentColor</c> → theme brush.
/// </summary>
public static class DigitalClockView
{
    private static readonly Dictionary<string, string> SvgCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Sync <paramref name="host"/> children to <paramref name="formatted"/> time.
    /// Reuses Image controls when length matches; rebuilds when size/brush/length change.
    /// </summary>
    public static void Apply(
        StackPanel host,
        string formatted,
        double digitSize,
        IBrush brush,
        bool blinkColonLit)
    {
        var chars = DigitalClockGlyphs.GlyphChars(formatted);
        var hex = BrushToHex(brush);
        var needRebuild = host.Children.Count != chars.Count
            || host.Tag as string != TagKey(digitSize, hex);

        if (needRebuild)
        {
            host.Children.Clear();
            host.Tag = TagKey(digitSize, hex);
            foreach (var c in chars)
            {
                var img = CreateGlyphImage(c, digitSize, hex);
                if (img is null) continue;
                if (DigitalClockGlyphs.IsColon(c))
                    img.Opacity = blinkColonLit ? 1.0 : 0.28;
                host.Children.Add(img);
            }
            return;
        }

        for (var i = 0; i < chars.Count; i++)
        {
            if (host.Children[i] is not Avalonia.Controls.Image img) continue;
            var c = chars[i];
            var key = DigitalClockGlyphs.AssetKey(c);
            var want = key + "|" + hex;
            if (!Equals(img.Tag as string, want))
            {
                var fresh = CreateGlyphImage(c, digitSize, hex);
                if (fresh is null) continue;
                host.Children[i] = fresh;
                img = fresh;
            }
            if (DigitalClockGlyphs.IsColon(c))
                img.Opacity = blinkColonLit ? 1.0 : 0.28;
            else
                img.Opacity = 1.0;
        }
    }

    private static string TagKey(double size, string hex) => $"{size:0.##}|{hex}";

    private static Avalonia.Controls.Image? CreateGlyphImage(char c, double digitSize, string hex)
    {
        var key = DigitalClockGlyphs.AssetKey(c);
        if (key is null) return null;
        try
        {
            var xml = LoadSvgXml(key);
            if (xml is null) return null;
            xml = xml.Replace("currentColor", hex, StringComparison.OrdinalIgnoreCase);
            var loaded = SvgSource.LoadFromSvg(xml);
            if (loaded is null) return null;

            var factor = DigitalClockGlyphs.WidthFactor(c);
            var w = Math.Max(4, Math.Round(digitSize * factor, 1));
            var h = Math.Max(8, Math.Round(digitSize, 1));
            return new Avalonia.Controls.Image
            {
                Source = new SvgImage { Source = loaded },
                Width = w,
                Height = h,
                Stretch = Stretch.Uniform,
                IsHitTestVisible = false,
                Tag = key + "|" + hex,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
        }
        catch (Exception ex)
        {
            AppLog.Warn($"DigitalClock SVG load failed ({key})", ex);
            return null;
        }
    }

    private static string? LoadSvgXml(string key)
    {
        if (SvgCache.TryGetValue(key, out var cached))
            return cached;

        var file = key + ".svg";
        var path = ResolvePath(file);
        if (path is null) return null;
        var xml = File.ReadAllText(path);
        SvgCache[key] = xml;
        return xml;
    }

    private static string? ResolvePath(string file)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", DigitalClockGlyphs.PackFolder, file),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
            {
                var p = Path.Combine(dir.FullName, "Assets", "Icons", DigitalClockGlyphs.PackFolder, file);
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
        return OverlayTokens.TextHex;
    }
}
