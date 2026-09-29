using System;
using NotifyIsland;
using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// Geometry of the 1.12.3 goo blob. These pin the numbers the window, the drag and the
/// bridge all derive from, so a token tweak cannot quietly shrink the window below what
/// the ball needs — which is what would clip it against the window edge.
/// </summary>
public class ClipboardBlobTests
{
    [Fact]
    public void Window_GrowsByBridgeBallAndFullDragRangeOnTheLongAxis()
    {
        var (w, h) = ClipboardBlob.WindowFor(false, 170, 30);

        Assert.Equal(170 + OverlayTokens.BlobBridgeMin + OverlayTokens.BlobD
                     + OverlayTokens.BlobDragMaxPx, w, 1);
        // The capsule is only 30 tall but the ball is 64, so the cross axis is driven by
        // the ball plus its drag range, not by the capsule's own height.
        Assert.Equal(Math.Max(30 + 2 * OverlayTokens.BlobDragMaxPx,
                              OverlayTokens.BlobD + 2 * OverlayTokens.BlobDragMaxPx), h, 1);
    }

    [Fact]
    public void Window_ReservesDragRangeOnBothSidesOfTheCrossAxis()
    {
        // The ball may be dragged up and down too, so the cross axis has to grow
        // symmetrically — otherwise the top half of every drag is cut off by the window.
        var (w, h) = ClipboardBlob.WindowFor(false, 170, 30);

        Assert.True(h >= OverlayTokens.BlobD + 2 * OverlayTokens.BlobDragMaxPx,
            "the window must fit the ball plus the full drag range above and below it");
        Assert.True(w >= OverlayTokens.BlobD + 2 * OverlayTokens.BlobDragMaxPx,
            "the window must fit the ball plus the full drag range on both sides");
    }

    [Fact]
    public void Window_VerticalIslandIsTheHorizontalAnswerWithAxesSwapped()
    {
        var horizontal = ClipboardBlob.WindowFor(false, 170, 30);
        var vertical = ClipboardBlob.WindowFor(true, 170, 30);

        Assert.Equal(horizontal.Width, vertical.Height, 3);
        Assert.Equal(horizontal.Height, vertical.Width, 3);
    }

    [Fact]
    public void HomeAlong_LeavesTheBridgeLengthClearBetweenCapsuleAndBall()
    {
        var home = ClipboardBlob.BlobHomeAlong(170);
        var capsuleEdgeToBallSurface = home - OverlayTokens.BlobD / 2 - 170;

        Assert.Equal(OverlayTokens.BlobBridgeMin, capsuleEdgeToBallSurface, 3);
    }

    [Fact]
    public void Drag_IsCappedToADiscNotASquare()
    {
        // A per-axis clamp would let a diagonal reach max·√2, and the window only
        // reserves max — the ball would be clipped by the window edge.
        var (along, cross) = ClipboardBlob.ClampOffset(200, 200, OverlayTokens.BlobDragMaxPx);
        var dist = Math.Sqrt(along * along + cross * cross);

        Assert.Equal(OverlayTokens.BlobDragMaxPx, dist, 3);
    }

    [Fact]
    public void Drag_StraightDownIsCappedTheSameAsStraightRight()
    {
        var (along, cross) = ClipboardBlob.ClampOffset(0, 500, OverlayTokens.BlobDragMaxPx);

        Assert.Equal(0, along, 3);
        Assert.Equal(OverlayTokens.BlobDragMaxPx, cross, 3);
    }

    [Fact]
    public void Drag_InsideTheRangeIsLeftAlone()
    {
        var (along, cross) = ClipboardBlob.ClampOffset(30, -20, OverlayTokens.BlobDragMaxPx);

        Assert.Equal(30, along, 3);
        Assert.Equal(-20, cross, 3);
    }

    [Fact]
    public void Bridge_IsWidestAtTheCapsuleAndNarrowerAtTheBall()
    {
        Assert.True(ClipboardBlob.BridgeHalfAt(0) > ClipboardBlob.BridgeHalfAt(1),
            "the neck must taper toward the ball");
    }

    [Fact]
    public void Bridge_NeverThinsToZeroSoItAlwaysOverlapsBothShapes()
    {
        foreach (var t in new[] { -5.0, 0.0, 0.5, 1.0, 5.0 })
            Assert.True(ClipboardBlob.BridgeHalfAt(t) > 0,
                $"the bridge pinched to zero at t={t} would read as a gap between the shapes");
    }

    [Fact]
    public void Bridge_IsAlwaysShorterThanTheCapsuleCrossAxisSoItDoesNotSwallowIt()
    {
        Assert.True(ClipboardBlob.BridgeHalfAt(0) * 2 < OverlayTokens.CollapsedH,
            "a bridge wider than the capsule would hide the island it hangs off");
    }
}
