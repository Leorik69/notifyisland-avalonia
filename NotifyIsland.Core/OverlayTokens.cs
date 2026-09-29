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
    // REMOVED 1.12.1: StatsAutoCollapseMs — the wall-clock 30 s self-collapse it fed is gone.

    // -- System Stats expanded (1.12.1 rework) -----------------------------
    /// <summary>Height of the expanded System Stats pill (DIP). Exempts SystemStats from CollapsedH.</summary>
    // Content budget (SystemStatsPanel, OverlayWindow.axaml:290-317), line box ≈ FontSize × 1.33:
    //   4 rows × 16 (FontSize 12 value line dominates the 11 label) = 64
    // + 4 gaps × Spacing 4                                        = 16
    // + StatsFullDate 13 (FontSize 10) + Margin top 2              = 15
    // + panel Margin 6 + 6                                        = 12
    // = 107 → 108 (rounded up to a clean value, no clipping).
    // Since 1.12.2 the height is computed per row count — use StatsLayout.StatsHeightFor(rowCount),
    // which reproduces this exact 108 for the default 5 rows. Kept for callers/tests that pin the default.
    public const double StatsExpandedH = 108.0;
    /// <summary>Fixed width of the expanded System Stats pill (DIP).</summary>
    public const double StatsExpandedW = 300.0;
    /// <summary>CPU % at or above which the value turns to the attention colour.</summary>
    public const double StatsCpuWarn = 70.0;
    /// <summary>CPU % at or above which the value turns to the critical colour.</summary>
    public const double StatsCpuCrit = 90.0;
    /// <summary>RAM usage % of total at or above which the value turns to the attention colour.</summary>
    public const double StatsRamWarn = 85.0;
    /// <summary>RAM usage % of total at or above which the value turns to the critical colour.</summary>
    public const double StatsRamCrit = 95.0;
    /// <summary>Battery % at or below which the value turns to the attention colour.</summary>
    public const double StatsBatteryWarn = 20.0;
    /// <summary>Battery % at or below which the value turns to the critical colour.</summary>
    public const double StatsBatteryCrit = 10.0;

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

    // -- Animation layer (1.12.4) -------------------------------------------
    /// <summary>
    /// Curve <see cref="AnimEase"/> falls back to for an unknown name. power2.out is the same soft
    /// settle the island morph uses today, so a typo degrades to "slightly different motion" instead
    /// of killing the morph tick. See AnimEase.Ease for why this is not an exception.
    /// </summary>
    public const string EaseFallbackName    = "power2.out";
    /// <summary>
    /// Every animation duration under reduced motion. 0 ms, not "fast": reduced motion removes the
    /// movement, it does not shorten it (spec invariant "сниженная анимация не «ускоряет»").
    /// </summary>
    public const int    ReducedMotionMs      = 0;

    // NOTE: the 1.12.0 hover-peek tokens (PeekAutoHideMs, PeekMorphMs, PeekExtraFullDateW)
    // were removed in 1.12.1 — the hover auto-hide timer and the date peek are gone; peek
    // expansion now reuses HoverExpandDelayMs / MorphMs.

    // -- Split clipboard pill (1.12.2) -------------------------------------
    /// <summary>Width of the clipboard half when the island is split (DIP).</summary>
    public const double ClipboardHalfW = 200.0;
    /// <summary>Divider thickness between the two halves (DIP).</summary>
    public const double ClipboardDividerW = 1.0;
    /// <summary>Gap between each half and the divider (DIP).</summary>
    public const double ClipboardHalfGap = 8.0;
    /// <summary>
    /// Length of the divider line *across* the long axis (DIP). On a horizontal pill this is
    /// the divider's height, on a vertical one its width — the same number either way, so the
    /// line reads identically in both orientations.
    /// </summary>
    public const double ClipboardDividerCross = 16.0;
    /// <summary>Breathe period of the waiting clipboard half (ms).</summary>
    public const int ClipboardHalfBreatheMs = 2400;
    /// <summary>Vertical amplitude of the waiting-half breathe (DIP).</summary>
    public const double ClipboardHalfBreathePx = 0.5;
    /// <summary>
    /// Fraction of the split morph spent holding the half invisible before its fade
    /// starts, as a fraction of the morph (0.25 = a quarter of MorphMs). The width is
    /// already moving here, so the half reads as catching up rather than appearing in
    /// lockstep with the capsule.
    /// </summary>
    public const double ClipboardHalfFadeDelay = 0.25;
    /// <summary>Peak scale of the split half: 1.0 → 1.06 → 1.0. Same shape as ClickPop.</summary>
    public const double ClipboardHalfPopPeak = 1.06;

    // -- Goo blob (1.12.3) ---------------------------------------------------
    /// <summary>Diameter of the clipboard blob (DIP).</summary>
    public const double BlobD = 64.0;
    /// <summary>Clear length of the bridge between the capsule edge and the ball.</summary>
    public const double BlobBridgeMin = 18.0;
    /// <summary>Half-width of the rope where it leaves the capsule (DIP). 1.12.4: 3, not 12 —
    /// a rope is thin at both ends. The old 12→5 taper read as a stretched cone, not a rope.</summary>
    public const double BlobBridgeBaseHalf = 3.0;
    /// <summary>Half-width of the rope where it meets the ball (DIP). Non-zero on purpose: the
    /// rope must always overlap the ball so it can never read as detached.</summary>
    public const double BlobBridgeTipHalf = 2.5;
    /// <summary>How far the blob may be dragged from its home spot, every direction.</summary>
    public const double BlobDragMaxPx = 100.0;
    /// <summary>How far the rope droops at its middle when the ball sits at home (DIP).
    /// The rope hangs under the ball's weight, so its middle sinks along the CROSS axis; the
    /// droop is 0 at zero length and capped here, which is what keeps the detach from
    /// snapping the rope sideways when the ball reaches home.</summary>
    public const double BlobRopeSagMaxPx = 7.0;
    /// <summary>Extra long-axis length the capsule takes for phase A of the split morph
    /// (DIP). The capsule grows to the RIGHT (down, on a vertical island) only — its leading
    /// edge and its screen position never move, so "the island is still" survives.</summary>
    public const double BlobPeekW = 110.0;
    /// <summary>Share of the split morph spent on phase A — the capsule growing and revealing
    /// the clipboard preview. The remaining 1 − this is the detach itself. A token, not a
    /// literal in the morph, so the code and its comments cannot drift apart.</summary>
    public const double BlobPeekShare = 0.4;

    // -- Tray menu (1.12.0) -------------------------------------------------
    /// <summary>Maximum entries surfaced in the tray clipboard submenu.</summary>
    public const int    TrayClipboardSubmenuItems = 5;
}
