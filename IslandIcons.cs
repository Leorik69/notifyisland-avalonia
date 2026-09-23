using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace NotifyIsland;

/// <summary>
/// Unified outline icon pack (stroke ~1.75). MIT-style Path geometries inspired by
/// Fluent System Icons / Tabler — embedded, no paid deps. Keys match WeatherCodes.IconKey
/// plus kind glyphs: clock, notify, media, timer, progress, error.
/// </summary>
public static class IslandIcons
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        // 24x24 viewBox geometries (scaled by control size)
        ["clock"] =
            "M12 3.5 A8.5 8.5 0 1 1 11.99 3.5 M12 7.5 L12 12 L15.5 14.5",
        ["notify"] =
            "M6 9.5 C6 6.5 8.5 4.5 12 4.5 C15.5 4.5 18 6.5 18 9.5 L18 14 L20 16 L4 16 L6 14 Z M10.5 18 C10.8 19.2 11.3 20 12 20 C12.7 20 13.2 19.2 13.5 18",
        ["media"] =
            "M9 7 L9 17 L16 12 Z",
        ["timer"] =
            "M12 5 A7 7 0 1 1 11.99 5 M12 8 L12 12 L15 14 M9 3.5 L15 3.5",
        ["progress"] =
            "M12 4 L12 12 M8 8 L12 4 L16 8 M6 18 L18 18",
        ["error"] =
            "M12 4 L20 18 L4 18 Z M12 10 L12 13.5 M12 16 L12 16.5",
        ["weather-clear"] =
            "M12 7 A5 5 0 1 1 11.99 7 M12 2.5 L12 4 M12 20 L12 21.5 M3.5 12 L5 12 M19 12 L20.5 12 M5.5 5.5 L6.6 6.6 M17.4 17.4 L18.5 18.5 M18.5 5.5 L17.4 6.6 M6.6 17.4 L5.5 18.5",
        ["weather-partly"] =
            "M9 10 A4 4 0 1 1 8.99 10 M16.5 14.5 A3.5 3.5 0 1 0 12.2 11.2 A5 5 0 0 0 7 16.5 L16.5 16.5",
        ["weather-cloud"] =
            "M7.5 17 A4.5 4.5 0 0 1 8 8.1 A6 6 0 0 1 19 11.5 A3.5 3.5 0 0 1 18.5 17 Z",
        ["weather-fog"] =
            "M5 10 L19 10 M6 13 L18 13 M7 16 L17 16 M8 19 L16 19",
        ["weather-drizzle"] =
            "M7.5 13 A4.5 4.5 0 0 1 8 4.1 A6 6 0 0 1 19 7.5 A3.5 3.5 0 0 1 18.5 13 Z M9 16 L8 19 M12 16 L11 19 M15 16 L14 19",
        ["weather-rain"] =
            "M7.5 12 A4.5 4.5 0 0 1 8 3.1 A6 6 0 0 1 19 6.5 A3.5 3.5 0 0 1 18.5 12 Z M9 15 L7.5 19 M12.5 15 L11 19 M16 15 L14.5 19",
        ["weather-snow"] =
            "M7.5 12 A4.5 4.5 0 0 1 8 3.1 A6 6 0 0 1 19 6.5 A3.5 3.5 0 0 1 18.5 12 Z M9 15.5 L9 18.5 M8 16.5 L10 17.5 M8 17.5 L10 16.5 M13 15.5 L13 18.5 M12 16.5 L14 17.5 M12 17.5 L14 16.5",
        ["weather-storm"] =
            "M7.5 11 A4.5 4.5 0 0 1 8 2.1 A6 6 0 0 1 19 5.5 A3.5 3.5 0 0 1 18.5 11 Z M11 12 L9 16 L12 16 L10 20"
    };

    public static string? GetPathData(string key) =>
        Paths.TryGetValue(key, out var d) ? d : null;

    public static IEnumerable<string> AllKeys => Paths.Keys;

    public static Avalonia.Controls.Shapes.Path Create(string key, double size, IBrush? stroke = null, double strokeThickness = -1)
    {
        var data = GetPathData(key) ?? GetPathData("weather-cloud")!;
        var geometry = StreamGeometry.Parse(data);
        var brush = stroke ?? new SolidColorBrush(Color.Parse(OverlayTokens.TextSecondaryHex));
        var thick = strokeThickness > 0 ? strokeThickness : OverlayTokens.IconStroke;
        // Scale 24x24 design to requested size
        var scale = size / 24.0;
        return new Avalonia.Controls.Shapes.Path
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = thick,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            RenderTransform = new ScaleTransform(scale > 0 ? 1 : 1, 1),
            IsHitTestVisible = false
        };
    }

    public static string KindKey(OverlayKind kind) => kind switch
    {
        OverlayKind.Media => "media",
        OverlayKind.Timer => "timer",
        OverlayKind.Progress => "progress",
        OverlayKind.Error => "error",
        OverlayKind.Notification => "notify",
        OverlayKind.Weather => "weather-clear",
        _ => "notify"
    };

    public static string WeatherKeyFromPayload(OverlayPayload p)
    {
        var code = p.WeatherCode ?? 0;
        return WeatherCodes.IconKey(code);
    }
}
