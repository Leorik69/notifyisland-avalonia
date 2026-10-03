using System;

namespace NotifyIsland;

/// <summary>
/// Pure geometry for the clipboard SECTION (1.14, spec
/// docs/superpowers/specs/2026-09-30--notifyisland-clipboard-section.md): the right-hand part
/// of the capsule itself that shows the copied item's format icon and a preview, and that
/// opens the history drawer when clicked.
///
/// <para>
/// This replaces the goo blob outright. The clipboard is no longer a separate control that
/// detaches from the capsule on a rope — it is a compartment INSIDE the capsule, so the
/// window needs no slack for a drag disc and the island can never walk off the screen edge.
/// </para>
///
/// <para>
/// The section lives on the FAR end of the long axis (right on a Top/Bottom island, down on a
/// Left/Right one) and the clock keeps the near end. That anchoring is the whole point: the
/// capsule's leading edge — the one the eye uses to locate the island — never moves, so
/// "копирование раздвигает капсулу вправо, часы не смещаются" is a structural property here
/// rather than something the view has to compensate for with a margin.
/// </para>
/// </summary>
public static class ClipboardSectionTrack
{
    /// <summary>Long-axis length the section adds to the capsule when it is fully shown (DIP).</summary>
    public static double Width => OverlayTokens.ClipboardSectionW;

    /// <summary>
    /// How much of the section is revealed at morph progress <paramref name="t"/>, 0 → 1.
    /// A single ramp that STAYS at 1: the section does not run back out again when the morph
    /// ends, because the clipboard section is the resting state while a capture is live. (The
    /// old two-phase peek/retract is gone with the ball.)
    /// </summary>
    public static double RevealAt(double t) =>
        AnimationEasing.CubicOut(Math.Clamp(t, 0.0, 1.0));

    /// <summary>
    /// Opacity of the section's content at progress <paramref name="t"/>. The ink fades in
    /// slightly behind the width so the text is never half-outside the growing rounded cap:
    /// by the time it reaches full opacity the capsule already has room for it.
    /// </summary>
    public static double OpacityAt(double t) =>
        AnimationEasing.CubicOut(Math.Clamp((t - OverlayTokens.ClipboardSectionFadeDelay) /
                                            (1.0 - OverlayTokens.ClipboardSectionFadeDelay), 0.0, 1.0));

    /// <summary>
    /// The capsule's own long-axis length at morph progress <paramref name="t"/>: the island's
    /// length plus however much of the section is out. Pure, so "at t = 0 the capsule is exactly
    /// the island's own length" is a test rather than a comment.
    /// </summary>
    public static double CapsuleLongAt(double t, double islandLong) =>
        islandLong + Width * RevealAt(t);

    /// <summary>
    /// The ISLAND's own long-axis length at progress <paramref name="t"/> — everything except
    /// the section. This is what the clipboard cycle zones and the hit test are measured
    /// against, so a wider capsule cannot slide the island's own click zones outwards.
    /// </summary>
    public static double IslandLongAt(double t, double pillLongAxis) =>
        Math.Max(0.0, pillLongAxis - Width * RevealAt(t));

    /// <summary>
    /// Does a point on the long axis land in the clipboard section (as opposed to the island's
    /// own content)? Compares against the very same island-long number the markup is anchored
    /// to, so hit-testing can never disagree with rendering on either orientation.
    /// </summary>
    public static bool IsInSection(bool isVertical, double windowAlongPos,
        double pillLongAxis, double islandLong) =>
        windowAlongPos >= islandLong;

    /// <summary>
    /// Trailing inset the section's content keeps from the capsule's growing end (DIP), so the
    /// preview text stops before the rounded corner instead of running over the curve. Exposed
    /// as a function so the view and the tests agree on one number.
    /// </summary>
    public static double CapInsetFor(double capsuleCross) => capsuleCross / 2.0;

    /// <summary>The section's own resting frame: fully revealed, fully opaque, no inset margin.</summary>
    public static (double Reveal, double Opacity) Resting => (1.0, 1.0);
}
