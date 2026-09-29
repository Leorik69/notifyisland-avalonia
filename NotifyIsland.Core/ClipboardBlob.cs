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
        var longAxis = capsuleLong + OverlayTokens.BlobBridgeMin
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
    /// Half-width of the bridge at a point <paramref name="t"/> along its length (0 at the
    /// capsule, 1 at the ball). The neck is widest where it leaves the capsule and narrows
    /// toward the ball, which is what makes the two shapes read as one structure instead
    /// of two shapes with a bar between them. Both ends stay positive, so the bridge always
    /// overlaps both shapes and can never read as detached.
    /// </summary>
    public static double BridgeHalfAt(double t)
    {
        var clamped = Math.Clamp(t, 0, 1);
        return OverlayTokens.BlobBridgeBaseHalf
               + (OverlayTokens.BlobBridgeTipHalf - OverlayTokens.BlobBridgeBaseHalf) * clamped;
    }
}
