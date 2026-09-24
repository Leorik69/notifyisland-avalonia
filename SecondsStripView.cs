using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Svg.Skia;

namespace NotifyIsland;

/// <summary>
/// FontAudio <c>digital-dot</c> seconds progress strip along the capsule bottom.
/// Lit leading dots follow minute progress; dim trailing dots stay visible.
/// </summary>
public static class SecondsStripView
{
    private static readonly Dictionary<string, string> SvgCache = new(StringComparer.OrdinalIgnoreCase);
    private static SvgSource? _dotSource;
    private static string? _dotHex;

    public const double DotWidth = 5;
    public const double DotHeight = 8;
    public const double LitOpacity = 1.0;
    public const double DimOpacity = 0.18;

    /// <summary>
    /// Sync host children to <paramref name="litCount"/> / <paramref name="slotCount"/>.
    /// Returns false if the digital-dot asset could not be loaded.
    /// </summary>
    public static bool Apply(
        StackPanel host,
        int slotCount,
        int litCount,
        IBrush brush)
    {
        if (slotCount <= 0)
        {
            host.Children.Clear();
            host.Tag = null;
            return false;
        }

        litCount = Math.Clamp(litCount, 0, slotCount);
        var hex = BrushToHex(brush);
        var tag = $"{slotCount}|{hex}|{DotWidth:0.#}";
        var needRebuild = host.Children.Count != slotCount
            || host.Tag as string != tag;

        if (needRebuild)
        {
            host.Children.Clear();
            host.Tag = tag;
            for (var i = 0; i < slotCount; i++)
            {
                var img = CreateDot(hex);
                if (img is null)
                {
                    host.Children.Clear();
                    host.Tag = null;
                    return false;
                }
                img.Opacity = i < litCount ? LitOpacity : DimOpacity;
                host.Children.Add(img);
            }
            return host.Children.Count > 0;
        }

        for (var i = 0; i < host.Children.Count; i++)
        {
            if (host.Children[i] is Avalonia.Controls.Image img)
                img.Opacity = i < litCount ? LitOpacity : DimOpacity;
        }
        return true;
    }

    private static Avalonia.Controls.Image? CreateDot(string hex)
    {
        try
        {
            var src = EnsureDotSource(hex);
            if (src is null) return null;
            return new Avalonia.Controls.Image
            {
                Source = new SvgImage { Source = src },
                Width = DotWidth,
                Height = DotHeight,
                Stretch = Stretch.Uniform,
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "digital-dot|" + hex
            };
        }
        catch (Exception ex)
        {
            AppLog.Warn("SecondsStrip CreateDot failed", ex);
            return null;
        }
    }

    private static SvgSource? EnsureDotSource(string hex)
    {
        if (_dotSource is not null && string.Equals(_dotHex, hex, StringComparison.OrdinalIgnoreCase))
            return _dotSource;

        var path = ResolvePath("digital-dot.svg");
        if (path is null)
        {
            AppLog.Warn("SecondsStrip: missing digital-dot.svg");
            return null;
        }

        var xml = LoadSvgXml("digital-dot", path);
        if (xml is null) return null;
        xml = xml.Replace("currentColor", hex, StringComparison.OrdinalIgnoreCase);

        try
        {
            _dotSource = SvgSource.LoadFromSvg(xml);
            _dotHex = hex;
            return _dotSource;
        }
        catch (Exception ex)
        {
            AppLog.Warn("SecondsStrip LoadFromSvg failed", ex);
            try
            {
                _dotSource = SvgSource.Load(path);
                _dotHex = hex;
                return _dotSource;
            }
            catch (Exception ex2)
            {
                AppLog.Warn("SecondsStrip Load failed", ex2);
                return null;
            }
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
            AppLog.Warn($"SecondsStrip read failed ({key})", ex);
            return null;
        }
    }

    private static string? ResolvePath(string file)
    {
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
            if (File.Exists(c)) return c;
        return null;
    }

    private static string BrushToHex(IBrush brush)
    {
        if (brush is SolidColorBrush sc)
            return $"#{sc.Color.R:X2}{sc.Color.G:X2}{sc.Color.B:X2}";
        return OverlayTokens.AccentHex;
    }
}
