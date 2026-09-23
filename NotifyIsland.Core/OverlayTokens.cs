namespace NotifyIsland;

/// <summary>Design tokens for the top-center overlay. No Apple assets.</summary>
public static class OverlayTokens
{
    public const string FillHex = "#080808";
    public const string TextHex = "#FFFFFF";
    public const string TextSecondaryHex = "#C8C8CC";
    public const string AccentHex = "#3D9CF0";
    public const string ErrorHex = "#E8A0A0";
    /// <summary>Collapsed capsule width (clock + optional unread dot).</summary>
    public const double CollapsedW = 140;
    /// <summary>Collapsed width when WeatherEnabled shows compact temp next to clock.</summary>
    public const double CollapsedWeatherW = 210;
    /// <summary>Fixed capsule height for every kind — width-only morph.</summary>
    public const double CollapsedH = 30;
    public const double ExpandedMinW = 280;
    public const double ExpandedMaxW = 460;
    /// <summary>Soft-Out morph duration (ms).</summary>
    public const int MorphMs = 280;
    public const int DefaultNotifyMs = 4000;
    /// <summary>Swipe must exceed this many DIPs to fire a gesture.</summary>
    public const int SwipeFirePx = 48;
    /// <summary>Movement at or below this is treated as a click (Action Center).</summary>
    public const int SwipeClickMaxPx = 12;
    /// <summary>Rubber-band snap-back when swipe under fire threshold (ms).</summary>
    public const int SwipeRubberMs = 180;
    /// <summary>How often to refresh Windows weather source (ms).</summary>
    public const int WeatherRefreshMs = 15 * 60 * 1000;
    /// <summary>Weather glyph crossfade between conditions (ms).</summary>
    public const int IconCrossfadeMs = 240;
    /// <summary>Outline icon stroke width (shared language).</summary>
    public const double IconStroke = 1.75;
    public const double IconSizeCollapsed = 12;
    public const double IconSizeKind = 11;
}
