using System;
using System.Collections.Generic;

namespace NotifyIsland;

/// <summary>
/// FontAudio (Iconify <c>fad</c>) digital segment glyph mapping — pure, no UI.
/// Assets live under <c>Assets/Icons/FontAudio/{key}.svg</c>.
/// </summary>
public static class DigitalClockGlyphs
{
    public const string PackFolder = "FontAudio";

    /// <summary>Map a clock character to a vendored SVG key, or null for spacer/unknown.</summary>
    public static string? AssetKey(char c) => c switch
    {
        '0' => "digital0",
        '1' => "digital1",
        '2' => "digital2",
        '3' => "digital3",
        '4' => "digital4",
        '5' => "digital5",
        '6' => "digital6",
        '7' => "digital7",
        '8' => "digital8",
        '9' => "digital9",
        ':' => "digital-colon",
        '.' => "digital-dot",
        _ => null
    };

    /// <summary>True when the glyph is a blinking colon separator.</summary>
    public static bool IsColon(char c) => c == ':';

    /// <summary>
    /// Format local time as digit/colon string (24h).
    /// No 12h setting in AppSettings yet — always <c>HH:mm</c> or <c>HH:mm:ss</c>.
    /// </summary>
    public static string FormatTime(DateTime now, bool includeSeconds) =>
        includeSeconds
            ? now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
            : now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Expand a formatted time into ordered glyph characters (digits + colons).</summary>
    public static IReadOnlyList<char> GlyphChars(string formatted)
    {
        if (string.IsNullOrEmpty(formatted))
            return Array.Empty<char>();
        var list = new List<char>(formatted.Length);
        foreach (var c in formatted)
        {
            if (AssetKey(c) is not null)
                list.Add(c);
        }
        return list;
    }

    /// <summary>Relative width factor for layout (colon ≈ half digit).</summary>
    public static double WidthFactor(char c) => IsColon(c) || c == '.' ? 0.5 : 1.0;

    /// <summary>Approx extra collapsed width when seconds are shown (two digits + colon).</summary>
    public const double SecondsExtraCollapsedW = 40;
}
