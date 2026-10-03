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
    private static bool _loggedBase;

    /// <summary>
    /// 2026-10-02 perf pass: the XML was cached but the parsed <see cref="SvgSource"/> was not,
    /// so every changed digit re-ran the SVG parser and the currentColor substitution — three
    /// times a second, forever, for artwork that changes only when the theme does. Keyed by glyph
    /// AND colour because the colour is baked into the markup. Bounded by construction: the
    /// glyph set is digits and a colon, times the handful of colours the user can pick.
    /// </summary>
    private static readonly Dictionary<string, SvgSource> ParsedCache = new(StringComparer.Ordinal);

    /// <summary>Directory the glyph SVGs were found in, so each glyph stops probing nine paths.</summary>
    private static string? _assetDir;

    /// <summary>
    /// Sync <paramref name="host"/> children to <paramref name="formatted"/> time.
    /// Returns false when no glyphs could be loaded (caller should fall back to text).
    /// </summary>
    public static bool Apply(
        StackPanel host,
        string formatted,
        double digitSize,
        IBrush brush,
        bool blinkColonLit)
    {
        if (!_loggedBase)
        {
            _loggedBase = true;
            AppLog.Info($"DigitalClock BaseDirectory={AppContext.BaseDirectory}");
        }

        var chars = DigitalClockGlyphs.GlyphChars(formatted);
        var hex = BrushToHex(brush);
        var needRebuild = host.Children.Count != chars.Count
            || host.Tag as string != TagKey(digitSize, hex);

        if (needRebuild)
        {
            host.Children.Clear();
            host.Tag = TagKey(digitSize, hex);
            var added = 0;
            foreach (var c in chars)
            {
                var img = CreateGlyphImage(c, digitSize, hex);
                if (img is null) continue;
                if (DigitalClockGlyphs.IsColon(c))
                    img.Opacity = blinkColonLit ? 1.0 : 0.28;
                host.Children.Add(img);
                added++;
            }
            if (added == 0)
            {
                AppLog.Warn($"DigitalClock: no glyphs loaded for '{formatted}' (size={digitSize:0.##} hex={hex})");
                return false;
            }
            return true;
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
        return host.Children.Count > 0;
    }

    private static string TagKey(double size, string hex) => $"{size:0.##}|{hex}";

    private static Avalonia.Controls.Image? CreateGlyphImage(char c, double digitSize, string hex)
    {
        var key = DigitalClockGlyphs.AssetKey(c);
        if (key is null) return null;
        try
        {
            var path = ResolvePath(key + ".svg");
            if (path is null)
            {
                AppLog.Warn($"DigitalClock: missing asset {key}.svg");
                return null;
            }

            var xml = LoadSvgXml(key, path);
            if (xml is null) return null;

            var parsedKey = key + "|" + hex;
            if (!ParsedCache.TryGetValue(parsedKey, out var loaded))
            {
                xml = xml.Replace("currentColor", hex, StringComparison.OrdinalIgnoreCase);
                try { loaded = SvgSource.LoadFromSvg(xml); }
                catch (Exception ex) { AppLog.Warn($"DigitalClock LoadFromSvg({key})", ex); }

                if (loaded is null)
                {
                    try { loaded = SvgSource.Load(path); }
                    catch (Exception ex) { AppLog.Warn($"DigitalClock Load({key})", ex); }
                }

                if (loaded is null)
                {
                    AppLog.Warn($"DigitalClock: SvgSource null for {key}");
                    return null;
                }
                ParsedCache[parsedKey] = loaded;
            }

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
                Tag = parsedKey,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
        }
        catch (Exception ex)
        {
            AppLog.Warn($"DigitalClock SVG load failed ({key})", ex);
            return null;
        }
    }

    private static string? LoadSvgXml(string key, string path)
    {
        if (SvgCache.TryGetValue(key, out var cached))
            return cached;
        try
        {
            var xml = File.ReadAllText(path);
            SvgCache[key] = xml;
            return xml;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"DigitalClock read failed ({key})", ex);
            return null;
        }
    }

    private static string? ResolvePath(string file)
    {
        // The assets never move while the process runs, so the directory that worked once is
        // reused. Probing nine candidate paths for every glyph, every time one was rebuilt, was
        // pure repeated directory probing.
        if (_assetDir is not null)
            return Path.Combine(_assetDir, file);

        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", DigitalClockGlyphs.PackFolder, file),
        };
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
                candidates.Add(Path.Combine(dir.FullName, "Assets", "Icons", DigitalClockGlyphs.PackFolder, file));
        }
        catch { /* ignore */ }

        foreach (var c in candidates)
        {
            if (!File.Exists(c)) continue;
            _assetDir = Path.GetDirectoryName(c);
            return c;
        }
        return null;
    }

    private static string BrushToHex(IBrush brush)
    {
        if (brush is SolidColorBrush sc)
            return $"#{sc.Color.R:X2}{sc.Color.G:X2}{sc.Color.B:X2}";
        return OverlayTokens.TextHex;
    }
}
