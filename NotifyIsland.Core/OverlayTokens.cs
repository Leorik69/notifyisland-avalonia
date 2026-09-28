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

    // -- System monitor: sampling (1.12.0) -----------------------------------
    /// <summary>Default sampling period (ms).</summary>
    public const int    StatsRefreshMs            = 1000;
    /// <summary>Settings combo floor; AppSettings.Normalize() clamps to it.</summary>
    public const int    StatsRefreshMinMs        = 500;
    /// <summary>Settings combo ceiling; AppSettings.Normalize() clamps to it.</summary>
    public const int    StatsRefreshMaxMs        = 2000;
    /// <summary>Ignore timer ticks closer together than this (ms).</summary>
    public const int    StatsMinSampleIntervalMs  = 100;
    /// <summary>Rebuild the Process[] cache on this cadence (ms).</summary>
    public const int    StatsProcessCacheMs       = 30_000;
    /// <summary>Percent-change floor below which a CPU reading is reused.</summary>
    public const double StatsDebouncePercent      = 0.5;
    /// <summary>Byte/sec-change floor below which a network reading is reused.</summary>
    public const long   StatsNetRateFloorBps      = 4_096;

    // -- System monitor: layout ----------------------------------------------
    /// <summary>Narrowest pill that still shows one metric slot.</summary>
    public const double StatsMinPillW             = 280.0;
    /// <summary>Px added to the collapsed width per visible metric slot.</summary>
    public const double StatsMetricSlotW          = 56.0;
    /// <summary>Gap kept between the pill and the screen edge (DIP).</summary>
    public const double StatsScreenMarginPx       = 48.0;
    /// <summary>At or above this available width, show CPU + RAM.</summary>
    public const double StatsShowTwoMetricsW      = 380.0;
    /// <summary>At or above this available width, also show Battery.</summary>
    public const double StatsShowThreeMetricsW    = 480.0;
    /// <summary>At or above this available width, also show Net.</summary>
    public const double StatsShowAllMetricsW      = 620.0;
    /// <summary>Idle time before the SystemStats kind self-collapses (ms).</summary>
    public const int    StatsAutoCollapseMs       = 30_000;

    // -- Settings (1.12.0) --------------------------------------------------
    /// <summary>Below this many sidebar sections, hide the search box.</summary>
    public const int    SettingsSearchMinSections = 4;

    // -- Animations (1.12.0) -- both derived from MorphMs ---------------------
    /// <summary>Click-acknowledgement pop duration (ms). Half the morph it interrupts.</summary>
    public const int    ClickPopMs           = MorphMs / 2;
    /// <summary>Peak scale for the click-acknowledgement pop.</summary>
    public const double ClickPopPeak         = 1.08;
    /// <summary>First-appear wobble duration (ms). Same rhythm as ClickPopMs.</summary>
    public const int    FirstAppearWobbleMs  = MorphMs / 2;
    /// <summary>Horizontal wobble amplitude (DIP) on first appear.</summary>
    public const double FirstAppearWobblePx  = 1.0;

    // -- Hover-peek (1.12.0) -----------------------------------------------
    /// <summary>Idle time before an un-pinned hover-peek auto-hides (ms).</summary>
    public const int    PeekAutoHideMs          = 1_200;
    /// <summary>Duration of the peek width morph (ms).</summary>
    public const int    PeekMorphMs             = 200;
    /// <summary>Extra pill width for the full-date row during peek (DIP).</summary>
    public const double PeekExtraFullDateW      = 120.0;

    // -- Tray menu (1.12.0) -------------------------------------------------
    /// <summary>Maximum entries surfaced in the tray clipboard submenu.</summary>
    public const int    TrayClipboardSubmenuItems = 5;
}
