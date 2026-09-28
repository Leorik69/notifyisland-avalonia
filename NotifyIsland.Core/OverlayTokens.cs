namespace NotifyIsland;

/// <summary>Design tokens for the top-center overlay. No Apple assets.</summary>
public static class OverlayTokens
{
    public const string FillHex = "#080808";
    public const string TextHex = "#FFFFFF";
    public const string TextSecondaryHex = "#C8C8CC";
    public const string AccentHex = "#3D9CF0";
    public const string ErrorHex = "#E8A0A0";
    /// <summary>Collapsed capsule width (time + optional date; clock icon removed in 1.6.0).</summary>
    public const double CollapsedW = 170;
    /// <summary>Collapsed width when WeatherEnabled shows compact temp next to clock/date.</summary>
    public const double CollapsedWeatherW = 240;
    /// <summary>Extra collapsed width for optional battery % chip.</summary>
    public const double CollapsedBatteryExtraW = 36;
    /// <summary>Fixed capsule height for every kind — width-only morph.</summary>
    public const double CollapsedH = 30;
    public const double ExpandedMinW = 280;
    public const double ExpandedMaxW = 460;
    /// <summary>Soft-Out morph base duration (ms) at Normal. Raised in 1.5.8 for smoother/slower feel.</summary>
    public const int MorphMs = 420;
    public const int DefaultNotifyMs = 4000;
    /// <summary>Movement at or below this DIP distance is a click (Action Center).</summary>
    public const int ClickMaxPx = 12;
    /// <summary>Legacy alias — gestures removed in 1.8.1; kept for older tests/docs.</summary>
    public const int SwipeClickMaxPx = ClickMaxPx;
    /// <summary>Obsolete (gestures removed). Kept so persisted AnimSwipeRubber scale still resolves.</summary>
    public const int SwipeFirePx = 48;
    /// <summary>Obsolete rubber-band ms (gestures removed).</summary>
    public const int SwipeRubberMs = 180;

    /// <summary>How often to refresh Windows weather source (ms).</summary>
    public const int WeatherRefreshMs = 15 * 60 * 1000;
    /// <summary>Weather glyph crossfade between conditions (ms).</summary>
    public const int IconCrossfadeMs = 240;
    /// <summary>Outline icon stroke width (shared language).</summary>
    public const double IconStroke = 1.75;
    /// <summary>Legacy fixed DIP sizes (fallback when FontSize unavailable).</summary>
    public const double IconSizeCollapsed = 12;
    public const double IconSizeKind = 11;

    /// <summary>Clock / weather icon DIP ≈ FontSize × this.</summary>
    public const double IconFontFactorCollapsed = 1.0;
    /// <summary>Kind badge glyph DIP ≈ FontSize × this.</summary>
    public const double IconFontFactorKind = 0.92;

    /// <summary>
    /// Icon DIP matched to island FontSize.
    /// Clock/weather: FontSize × 1.0; kind glyph: FontSize × 0.92 (clamped 9–20).
    /// </summary>
    /// <summary>Hover → peek delay (ms) before richer idle content.</summary>
    public const int HoverExpandDelayMs = 250;
    /// <summary>Pointer-leave grace before collapsing hover peek (ms).</summary>
    public const int HoverCollapseGraceMs = 500;
    /// <summary>Extra collapsed width while hover-peek / pinned (beyond seconds).</summary>
    public const double IdlePeekExtraW = 20;
    /// <summary>Fullscreen poll interval (ms).</summary>
    public const int FullscreenPollMs = 500;

    public static double IconDip(double fontSize, double factor = IconFontFactorCollapsed)
    {
        var fs = fontSize <= 0 ? 12 : fontSize;
        if (fs < 10) fs = 10;
        if (fs > 18) fs = 18;
        var dip = fs * factor;
        if (dip < 9) dip = 9;
        if (dip > 20) dip = 20;
        return Math.Round(dip, 1);
    }
}
