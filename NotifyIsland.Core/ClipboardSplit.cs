using System;

namespace NotifyIsland;

/// <summary>
/// Pure geometry for the split clipboard pill (1.12.2): the pill stays one capsule, the
/// clipboard half is attached on the morph (long) axis, and a thin divider centred on the
/// half boundary marks where the island ends and the clipboard half begins.
/// </summary>
public static class ClipboardSplit
{
    /// <summary>
    /// Total pill width when split, in window coordinates.
    /// Horizontal: the clipboard half grows the width — <paramref name="collapsedW"/> +
    /// <see cref="OverlayTokens.ClipboardHalfW"/>.
    /// Vertical (Left/Right): the long axis is the height, so the pill keeps its cross-axis
    /// thickness — <see cref="OverlayTokens.CollapsedCrossAxisVertical"/> — and the clipboard half
    /// grows the height instead. <see cref="IslandLayout.SizeFor"/> owns that axis swap.
    /// </summary>
    public static double SplitWidthFor(bool isVertical, double collapsedW) =>
        isVertical ? OverlayTokens.CollapsedCrossAxisVertical : collapsedW + OverlayTokens.ClipboardHalfW;

    /// <summary>Long-axis (morph length) of a split pill: the normal length plus the half.</summary>
    public static double SplitLongAxisFor(double longAxisW) => longAxisW + OverlayTokens.ClipboardHalfW;

    /// <summary>
    /// Where the clipboard half begins, measured along the pill's LONG axis in window
    /// coordinates. Horizontal (Top/Bottom) the long axis is X; vertical (Left/Right) it is Y
    /// — the half is always anchored at the far end of the long axis, which is the end the
    /// morph grows towards, so attach slides it in from exactly one half width out.
    /// A pill whose long axis is too short to hold the half clamps the boundary back to
    /// <paramref name="collapsedLongAxis"/> instead of inverting the layout.
    /// </summary>
    public static double ClipboardHalfStart(
        bool isVertical, double pillLongAxis, double collapsedLongAxis)
    {
        var start = pillLongAxis - OverlayTokens.ClipboardHalfW;
        return start < collapsedLongAxis ? collapsedLongAxis : start;
    }

    /// <summary>
    /// Extent of the island's own half along the long axis — the span the ⅓/⅓/⅓ clipboard
    /// cycle zones are measured against. Without a split that is the whole pill; with a split
    /// it stops at the half boundary, so the zones keep their collapsed-pill positions on
    /// both orientations. This is the single number both the render and the hit test derive
    /// from, which is why a split pill can never move its click zones.
    /// </summary>
    public static double IslandHalfExtent(
        bool isVertical, bool split, double pillLongAxis, double collapsedLongAxis) =>
        split ? ClipboardHalfStart(isVertical, pillLongAxis, collapsedLongAxis) : pillLongAxis;

    /// <summary>
    /// Does a point on the long axis fall inside the clipboard half? Compares against the very
    /// same <see cref="ClipboardHalfStart"/> the markup is anchored to, so hit-testing can never
    /// disagree with rendering — on either orientation.
    /// </summary>
    public static bool IsInHalf(
        bool isVertical, double windowAlongPos, double pillLongAxis, double collapsedLongAxis) =>
        windowAlongPos >= ClipboardHalfStart(isVertical, pillLongAxis, collapsedLongAxis);

    /// <summary>
    /// Divider geometry, in window coordinates of the long axis:
    /// <paramref name="Along"/> is the line's near edge, <paramref name="Cross"/> its length
    /// across the pill, <paramref name="Thickness"/> its width. The line is centred on
    /// <see cref="ClipboardHalfStart"/> (Along + Thickness/2 == boundary), and
    /// <see cref="OverlayTokens.ClipboardHalfGap"/> stays clearance reserved as inner padding
    /// of the two halves rather than extra length.
    /// </summary>
    public readonly record struct DividerFrame(double Along, double Cross, double Thickness)
    {
        /// <summary>
        /// The same centring expressed as a margin *inside* the half. The half is anchored at
        /// the far end of the long axis and its near edge is therefore the boundary itself, so
        /// the divider's element-level margin is a constant — negative, to centre the line on
        /// the boundary. This is the number the markup applies; it is derived here, not typed
        /// into the XAML, so production and this record cannot drift apart.
        /// </summary>
        public double NearMargin => -Thickness / 2.0;
    }

    /// <summary>Divider centred on the half boundary, for either orientation.</summary>
    public static DividerFrame DividerFor(
        bool isVertical, double pillLongAxis, double collapsedLongAxis) =>
        new(ClipboardHalfStart(isVertical, pillLongAxis, collapsedLongAxis) -
            OverlayTokens.ClipboardDividerW / 2.0,
            OverlayTokens.ClipboardDividerCross,
            OverlayTokens.ClipboardDividerW);

    /// <summary>
    /// Cross-axis breathing offset of the waiting clipboard half at a given clock (DIP). Sine,
    /// amplitude <see cref="OverlayTokens.ClipboardHalfBreathePx"/>, period
    /// <see cref="OverlayTokens.ClipboardHalfBreatheMs"/>. The amplitude is the same number on
    /// every edge; only the axis it is written to differs (Y on a horizontal pill, X on a
    /// vertical one), because the breathe drifts the half *across* the long axis while the
    /// morph slides it *along* it — the two must never fight over the same axis.
    /// </summary>
    public static double CrossBreatheOffset(int clockMs) =>
        OverlayTokens.ClipboardHalfBreathePx *
        Math.Sin(2.0 * Math.PI * (clockMs % OverlayTokens.ClipboardHalfBreatheMs) /
                 OverlayTokens.ClipboardHalfBreatheMs);

    /// <summary>
    /// One frame of the split half's own motion, driven from the same normalised
    /// morph progress as the pill width. Pure: no timers, no element state.
    /// <paramref name="Translate"/> is along the long axis — the caller writes it to X on a
    /// horizontal pill and to Y on a vertical one.
    /// </summary>
    /// <param name="t">Normalised morph progress, 0 → 1.</param>
    /// <param name="entering">True while the split is attaching, false while it detaches.</param>
    public readonly record struct HalfFrame(double Translate, double Opacity, double Scale);

    /// <summary>
    /// Translate of the half along the long axis (DIP, positive = outboard, i.e. toward the far
    /// end of that axis — right on Top/Bottom, bottom on Left/Right). Entering slides it in
    /// from exactly one half width out — that is the window edge, since the half is anchored at
    /// <see cref="OverlayTokens.ClipboardHalfW"/> on the far end. Detaching slides it back out
    /// along the identical path, so the reverse is a true reverse.
    /// </summary>
    public static double TranslateFor(double t, bool entering) => entering
        ? OverlayTokens.ClipboardHalfW * (1.0 - AnimationEasing.CubicOut(t))
        : OverlayTokens.ClipboardHalfW * AnimationEasing.CubicOut(t);

    /// <summary>
    /// Opacity of the half. The fade is <see cref="OverlayTokens.ClipboardHalfFadeDelay"/>
    /// of the morph behind the translate: the capsule has already started growing when
    /// the half begins to appear, so the two read as one movement instead of a label
    /// switching on in lockstep with the width. Detaching runs the same curve in reverse,
    /// so the half holds its opacity while it slides away and then vanishes.
    /// </summary>
    public static double OpacityFor(double t, bool entering)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        var delay = OverlayTokens.ClipboardHalfFadeDelay;
        var u = t <= delay ? 0.0 : Math.Clamp((t - delay) / (1.0 - delay), 0.0, 1.0);
        var fade = AnimationEasing.CubicOut(u);
        return entering ? fade : 1.0 - fade;
    }

    /// <summary>
    /// Scale of the half. Entering reuses the <see cref="AnimationEasing.ClickPop"/>
    /// curve (peak in the first third, never below 1) rescaled to
    /// <see cref="OverlayTokens.ClipboardHalfPopPeak"/>; no new easing is introduced.
    /// Detaching is the time-reverse of that curve about 1.0, so the half squeezes
    /// inward as it leaves rather than popping outward on the way out.
    /// </summary>
    public static double ScaleFor(double t, bool entering)
    {
        var span = OverlayTokens.ClickPopPeak - 1.0;
        if (span <= 0.0) return 1.0;
        var over = 1.0 + (OverlayTokens.ClipboardHalfPopPeak - 1.0) *
            (AnimationEasing.ClickPop(t) - 1.0) / span;
        return entering ? over : 2.0 - over;
    }

    /// <summary>All three channels for one morph frame.</summary>
    public static HalfFrame HalfFor(double t, bool entering) => new(
        TranslateFor(t, entering),
        OpacityFor(t, entering),
        ScaleFor(t, entering));

    /// <summary>
    /// The defined resting state of the attached half: fully opaque, at its slot, no
    /// overshoot. This is the well-defined target of the morph and the value the idle
    /// breathe offsets from — not "whatever the last morph frame left".
    /// </summary>
    public static HalfFrame Resting => new(0.0, 1.0, 1.0);

    /// <summary>
    /// The defined resting state of the detached half: invisible and at its slot, so the
    /// next split starts from exactly the frame <see cref="HalfFor"/>(0, entering: true)
    /// asks for — no residual offset survives a completed collapse.
    /// </summary>
    public static HalfFrame Detached => new(0.0, 0.0, 1.0);
}
