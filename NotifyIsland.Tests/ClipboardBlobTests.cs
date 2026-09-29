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

        // 1.12.4: the reserve also covers BlobPeekW, the capsule's temporary phase-A length.
        Assert.Equal(170 + OverlayTokens.BlobPeekW + OverlayTokens.BlobBridgeMin + OverlayTokens.BlobD
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
    public void Window_FitsThePhaseACapsulePlusTheBall()
    {
        // Phase A makes the capsule BlobPeekW longer while the window stays at its resting
        // size. If the reserve did not cover that, the growing capsule — and the preview text
        // inside it — would be clipped by the window edge for the first 40 % of the morph.
        var (w, _) = ClipboardBlob.WindowFor(false, 170, 30);
        var widestCapsule = 170 + OverlayTokens.BlobPeekW;

        Assert.True(w >= widestCapsule,
            "the window must hold the capsule at its widest phase-A frame");
        // And still hold the ball and its whole drag disc from that same capsule edge.
        Assert.True(w >= widestCapsule + OverlayTokens.BlobBridgeMin + OverlayTokens.BlobD
                        + OverlayTokens.BlobDragMaxPx,
            "the ball's drag disc must still fit when the capsule is at its phase-A length");
    }

    [Fact]
    public void Bridge_IsThinAtBothEndsBecauseItIsARopeNotACone()
    {
        // The user read the 12→5 DIP taper as a stretched cone. A rope is thin throughout:
        // if either end grows back past ~4 DIP this test says so before the shape does.
        Assert.True(ClipboardBlob.BridgeHalfAt(0) <= 4.0,
            "the rope must be thin where it leaves the capsule");
        Assert.True(ClipboardBlob.BridgeHalfAt(1) <= 4.0,
            "the rope must be thin where it meets the ball");
        // The droop has to be deeper than the rope is thick, or the sag is a wobble too small
        // to read as a rope hanging under a weight.
        Assert.True(OverlayTokens.BlobRopeSagMaxPx > ClipboardBlob.BridgeHalfAt(1) * 2,
            "the droop must be deep enough to read as a hanging rope, not a straight cord");
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

    // -- 1.12.4: the rope sags, and the split morph runs in two phases ---------------

    [Fact]
    public void Sag_IsZeroAtBothEndsSoTheRopeStaysWeldedToTheCapsuleAndTheBall()
    {
        // A rope that sagged AT its anchors would visibly pull away from the two shapes it
        // joins. The droop belongs in the middle only.
        Assert.Equal(0, ClipboardBlob.BridgeSagAt(0, 5), 6);
        Assert.Equal(0, ClipboardBlob.BridgeSagAt(1, 5), 6);
    }

    [Fact]
    public void Sag_IsPeakedInTheMiddleAndPositiveEverywhereInside()
    {
        var peak = ClipboardBlob.BridgeSagAt(0.5, 5);

        Assert.Equal(5, peak, 6);
        for (var t = 0.001; t < 1.0; t += 0.001)
            Assert.True(ClipboardBlob.BridgeSagAt(t, 5) > 0,
                $"the rope was flat or inverted at t={t:0.000} — that reads as a bent stick");
    }

    [Fact]
    public void Sag_IsSymmetricAboutTheMiddleSoTheRopeHangsStraight()
    {
        for (var t = 0.05; t < 0.5; t += 0.05)
            Assert.Equal(ClipboardBlob.BridgeSagAt(t, 5), ClipboardBlob.BridgeSagAt(1 - t, 5), 6);
    }

    [Fact]
    public void Sag_IsZeroForADegenerateRopeSoTheDetachDoesNotJerk()
    {
        // A ball still inside the capsule leaves no rope at all; a rope with no length must
        // not have droop, or the line would twitch the moment the ball started to move.
        Assert.Equal(0, ClipboardBlob.SagPxFor(0), 6);
        Assert.Equal(0, ClipboardBlob.SagPxFor(-10), 6);
        Assert.Equal(0, ClipboardBlob.BridgeSagAt(0.5, ClipboardBlob.SagPxFor(0)), 6);
    }

    [Fact]
    public void Sag_GrowsWithRopeLengthAndIsAlreadyMaximalAtTheHomeSpot()
    {
        // This is the spec's "no jerk on detach": the sag reaches its cap by the time the ball
        // gets home, so arriving does not change the rope's shape. If the cap were only
        // reached past the home spot, the rope would visibly straighten on arrival.
        var home = ClipboardBlob.ClipboardBlobLengthAtHome();
        var half = ClipboardBlob.SagPxFor(home / 2);

        Assert.Equal(OverlayTokens.BlobRopeSagMaxPx / 2, half, 6);
        Assert.Equal(OverlayTokens.BlobRopeSagMaxPx, ClipboardBlob.SagPxFor(home), 6);
        // Beyond home (a drag) it stays capped: a longer rope must not droop further.
        Assert.Equal(OverlayTokens.BlobRopeSagMaxPx, ClipboardBlob.SagPxFor(home * 2), 6);
    }

    [Fact]
    public void Peek_GrowsOverPhaseAAndIsBackToZeroByTheEndOfTheMorph()
    {
        // The capsule is exactly its own length at both ends of the morph — that is what lets
        // the resting layout assume 170 with no compensation.
        Assert.Equal(0, ClipboardBlob.PeekWidthAt(0), 6);
        Assert.Equal(0, ClipboardBlob.PeekWidthAt(1), 6);
        // Fully out at the phase boundary, which is the whole point of phase A.
        Assert.Equal(OverlayTokens.BlobPeekW,
            ClipboardBlob.PeekWidthAt(OverlayTokens.BlobPeekShare), 1);
    }

    [Fact]
    public void Peek_NeverExceedsItsTokenAndNeverGoesNegative()
    {
        for (var t = 0.0; t <= 1.0; t += 0.01)
        {
            var w = ClipboardBlob.PeekWidthAt(t);
            Assert.True(w >= 0 && w <= OverlayTokens.BlobPeekW + 0.01,
                $"the capsule grew to {w:0.0} DIP at t={t:0.00}, outside 0..{OverlayTokens.BlobPeekW}");
        }
    }

    [Fact]
    public void PeekPhase_AndDetachPhase_DoNotOverlapOrLeaveAGap()
    {
        // The two phases are one motion split by one token. If they overlapped, the capsule
        // would be running out while the ball was already leaving; if they left a gap, the
        // morph would sit still for a frame or two with nothing moving.
        Assert.Equal(0, ClipboardBlob.DetachPhaseAt(OverlayTokens.BlobPeekShare), 6);
        Assert.Equal(1, ClipboardBlob.PeekPhaseAt(OverlayTokens.BlobPeekShare), 6);
        Assert.Equal(1, ClipboardBlob.DetachPhaseAt(1), 6);
        Assert.Equal(0, ClipboardBlob.PeekPhaseAt(0), 6);
    }

    [Fact]
    public void PeekPhase_SaturatesOutsideItsWindowSoExtraProgressCannotOvershootTheCapsule()
    {
        // Phase A is over long before the morph ends: past the boundary its progress must stay
        // pinned at 1, or the capsule would keep growing after the preview has gone.
        Assert.Equal(1, ClipboardBlob.PeekPhaseAt(0.9), 6);
        // And phase B must not start early — inside phase A the detach is still 0, which is
        // what holds the ball inside the (grown) capsule.
        Assert.Equal(0, ClipboardBlob.DetachPhaseAt(0.05), 6);
    }
}
