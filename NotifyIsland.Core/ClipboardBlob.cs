using System;

namespace NotifyIsland;

/// <summary>
/// Pure-Core geometry for the clipboard blob (1.12.3, World of Goo): a round ball that
/// detaches from the capsule, is joined back to it by a bridge, and can be dragged a
/// short distance around its home spot. No UI types — the window drives these numbers.
///
/// Everything here is axis-agnostic: callers pass the long axis explicitly, so a
/// Left/Right island gets the same answers as a Top/Bottom one with the axes swapped.
/// </summary>
public static class ClipboardBlob
{
    /// <summary>
    /// Size of the window a blob needs, given the capsule's own size. The window is larger
    /// than the capsule because the blob lives outside it, and larger again because the
    /// blob may be dragged <see cref="OverlayTokens.BlobDragMaxPx"/> in every direction —
    /// including straight up and down, which is why the cross axis grows symmetrically
    /// instead of only along the long axis.
    ///
    /// The capsule keeps its own size; only the window grows. See BlobHomeAlong for how
    /// the island stays visually still while the window it lives in changes size.
    /// </summary>
    public static (double Width, double Height) WindowFor(
        bool isVertical, double capsuleLong, double capsuleCross)
    {
        // 1.12.4: BlobPeekW is reserved because phase A of the split morph makes the capsule
        // LONGER — for about 40 % of the morph, on the leading edge, without moving. The
        // window therefore has to hold capsule + peek + bridge + ball + drag, not just the
        // resting capsule, or the growing capsule would be clipped by the window edge while
        // it is showing the preview. It is a permanent reserve, not a phase-A-only one: the
        // window size is computed once per ApplySize and the reserve must never be smaller
        // than the widest frame the morph will produce.
        var longAxis = capsuleLong + OverlayTokens.BlobPeekW
                       + OverlayTokens.BlobBridgeMin
                       + OverlayTokens.BlobD + OverlayTokens.BlobDragMaxPx;
        // The cross axis must fit the ball at BOTH ends of its drag range, not just grow
        // by the range: the ball is centred on the capsule's cross centre, so it needs
        // BlobDragMaxPx of room above and below that centre, and the capsule's own height
        // may be smaller than the ball. Taking the max is what stops a drag upward from
        // clipping the ball against the window edge.
        var crossAxis = Math.Max(
            capsuleCross + 2 * OverlayTokens.BlobDragMaxPx,
            OverlayTokens.BlobD + 2 * OverlayTokens.BlobDragMaxPx);
        return isVertical ? (crossAxis, longAxis) : (longAxis, crossAxis);
    }

    /// <summary>
    /// Distance from the capsule's leading edge (its own 0) to the blob's centre, along
    /// the long axis, at its home spot. The bridge keeps
    /// <see cref="OverlayTokens.BlobBridgeMin"/> of clear length between the capsule edge
    /// and the ball's surface, so the two read as two shapes joined by a neck rather than
    /// as one shape that grew a bump.
    /// </summary>
    public static double BlobHomeAlong(double capsuleLong) =>
        capsuleLong + OverlayTokens.BlobBridgeMin + OverlayTokens.BlobD / 2;

    /// <summary>
    /// Clamp a drag offset from the blob's home spot to a disc of
    /// <paramref name="max"/>. A per-axis clamp would let a diagonal drag reach
    /// max·√2 ≈ 141 DIP, which is more than the window reserves and would clip the ball
    /// against the window edge; a disc caps every direction at exactly max.
    /// </summary>
    public static (double Along, double Cross) ClampOffset(
        double along, double cross, double max)
    {
        if (max <= 0) return (0, 0);
        var dist = Math.Sqrt(along * along + cross * cross);
        if (dist <= max || dist == 0) return (along, cross);
        var k = max / dist;
        return (along * k, cross * k);
    }

    /// <summary>
    /// Half-width of the rope at a point <paramref name="t"/> along its length (0 at the
    /// capsule, 1 at the ball). 1.12.4: both ends are ~3 DIP — a rope is thin, and the old
    /// 12→5 taper read as a stretched cone rather than a cord. The slight taper is kept
    /// because both ends stay positive, so the rope always overlaps both shapes and can
    /// never read as detached.
    /// </summary>
    public static double BridgeHalfAt(double t)
    {
        var clamped = Math.Clamp(t, 0, 1);
        return OverlayTokens.BlobBridgeBaseHalf
               + (OverlayTokens.BlobBridgeTipHalf - OverlayTokens.BlobBridgeBaseHalf) * clamped;
    }

    /// <summary>
    /// The rope's centreline, sagging under the ball's weight, at <paramref name="t"/> along
    /// its length: 0 at the capsule, 1 at the ball, <paramref name="sagPx"/> is the peak
    /// droop at the middle.
    ///
    /// The droop is a parabola — 4·sag·t·(1−t) — because a rope under a point load hangs in
    /// a curve that is flat where it is fixed and steepest in the middle; a straight line, or
    /// a single constant offset, would read as a bent stick. Both ends stay at zero, which
    /// is what keeps the rope welded to the capsule and the ball instead of pulled away from
    /// them at the anchors.
    ///
    /// The caller passes the sag it computed from the current rope LENGTH, not the constant:
    /// see <see cref="SagPxFor"/>, which is what makes the sag 0 when the ball is still inside
    /// the capsule and maximal at the home spot.
    /// </summary>
    public static double BridgeSagAt(double t, double sagPx)
    {
        var clamped = Math.Clamp(t, 0, 1);
        if (sagPx <= 0) return 0;
        return 4.0 * sagPx * clamped * (1.0 - clamped);
    }

    /// <summary>
    /// Peak droop for a rope of <paramref name="length"/> DIP.
    ///
    /// Proportional to length, capped at <see cref="OverlayTokens.BlobRopeSagMaxPx"/>: a rope
    /// with nothing hanging on it does not sag, so a ball still inside the capsule (length ≈ 0)
    /// gets 0 and never makes the rope twitch; and the cap means the sag is already at its
    /// maximum by the home spot, so the detach cannot snap the rope as the ball arrives.
    /// </summary>
    public static double SagPxFor(double length)
    {
        if (length <= 0) return 0;
        // The home length is the longest rope the resting layout produces, so it is also the
        // length at which the cap is reached. Deriving it keeps the cap and the geometry from
        // being two numbers that can disagree.
        var home = ClipboardBlobLengthAtHome();
        if (home <= 0) return 0;
        return Math.Min(OverlayTokens.BlobRopeSagMaxPx,
            OverlayTokens.BlobRopeSagMaxPx * (length / home));
    }

    /// <summary>
    /// Rope length at the home spot, measured the same way the view measures it: capsule
    /// edge to ball centre. Shared by the sag and by the tests, so "maximal sag at home"
    /// is one statement rather than two that can drift.
    /// </summary>
    public static double ClipboardBlobLengthAtHome() =>
        OverlayTokens.BlobBridgeMin + OverlayTokens.BlobD / 2;

    // -- 1.12.4: the split morph is TWO phases, not one ---------------------------
    // The user asked for the 1.12.2 sequence back on top of the ball: the island runs out and
    // shows a piece of the clipboard first, and only then does the ball peel off. Driving both
    // phases from ONE token is the whole point — two independent fractions in the morph would
    // drift apart the first time someone edited one of them.

    /// <summary>
    /// Phase A progress (0→1) at morph progress <paramref name="t"/>: the capsule running out
    /// to <see cref="OverlayTokens.BlobPeekW"/> and revealing the preview. Reaches 1 exactly
    /// at <see cref="OverlayTokens.BlobPeekShare"/>, so phase B always gets the remainder.
    /// </summary>
    public static double PeekPhaseAt(double t) =>
        Math.Clamp(t / OverlayTokens.BlobPeekShare, 0.0, 1.0);

    /// <summary>
    /// Phase B progress (0→1): the capsule returning to its own length while the ball
    /// detaches. Starts at 0 on the first frame — the ball is still inside the growing
    /// capsule, hidden, and the rope has no length to sag with.
    /// </summary>
    public static double DetachPhaseAt(double t) =>
        Math.Clamp((t - OverlayTokens.BlobPeekShare) / (1.0 - OverlayTokens.BlobPeekShare), 0.0, 1.0);

    /// <summary>
    /// Extra long-axis length the capsule holds at morph progress <paramref name="t"/>: it
    /// runs out over phase A and comes back over phase B, so the preview is on screen only
    /// while it is readable and the capsule is back to 170 by the time the morph ends.
    /// Pure, so "the capsule is exactly its own width at t = 0 and t = 1" is a test, not a
    /// comment.
    /// </summary>
    public static double PeekWidthAt(double t)
    {
        if (t <= 0 || t >= 1) return 0;
        // Out over phase A, back over phase B, both eased so the ends are soft: a linear
        // ramp would start and stop the capsule with a visible corner.
        var a = AnimationEasing.CubicOut(PeekPhaseAt(t));
        var b = AnimationEasing.CubicOut(DetachPhaseAt(t));
        return OverlayTokens.BlobPeekW * (1.0 - b) * a;
    }
}
