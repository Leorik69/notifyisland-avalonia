using System;
using NotifyIsland;
using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// Geometry and animation of the 1.14 clipboard history DRAWER. The invariant these protect is
/// the user's own words: it must read as a drawer hanging off the capsule, not as a second
/// capsule that happens to be nearby. That is three separate claims, and each gets its own test
/// because each fails on its own — a gap with the right width still reads as two shapes, and a
/// matched radius at the seam still reads as two capsules.
///
/// <para>
/// The fourth claim is the bug this whole workstream exists to kill: <b>the window is exactly
/// the content it shows</b>. No drag disc, no slack, no clamp that moves the island to compensate.
/// </para>
/// </summary>
public class ClipboardDrawerTests
{
    private const double CapsuleLong = 170;
    private const double CapsuleCross = 30;
    private const int Rows = OverlayTokens.HistoryPanelMaxRows;
    private static readonly double Cross = ClipboardDrawer.CrossFor(Rows);

    // -- the seam --------------------------------------------------------------------

    [Theory]
    [InlineData(IslandEdge.Top, 1)]
    [InlineData(IslandEdge.Left, 1)]
    [InlineData(IslandEdge.Bottom, -1)]
    [InlineData(IslandEdge.Right, -1)]
    public void CrossDirection_DrawerGrowsTowardsTheScreenInterior(IslandEdge edge, int expected)
    {
        // Top → down, Bottom → up, Left → right, Right → left. A drawer that opened the other way
        // would grow off the screen edge, which is the bug the whole drag-slack clamp existed to
        // paper over. The one that matters in practice is Top → down, the default edge.
        Assert.Equal(expected, ClipboardDrawer.CrossDirectionFor(edge));
    }

    [Fact]
    public void DrawerIsFlushWithTheCapsuleEdge_ThereIsNoGap()
    {
        // THE claim. Both opening directions, both orientations: the drawer's near edge sits
        // exactly on the capsule's far edge. A single DIP of daylight here is enough for the two
        // shapes to read as two objects, and daylight is exactly what the 1.12.3 panel had
        // (HistoryPanelGap = 12).
        foreach (var edge in new[] { IslandEdge.Top, IslandEdge.Bottom, IslandEdge.Left, IslandEdge.Right })
        {
            var vertical = edge is IslandEdge.Left or IslandEdge.Right;
            var dir = ClipboardDrawer.CrossDirectionFor(edge);
            var start = ClipboardDrawer.DrawerCrossStart(edge, CapsuleCross, Rows);

            // "On the capsule's far edge" means: the capsule occupies [0, capsuleCross] when the
            // drawer grows away from the origin, and [drawerCross, drawerCross + capsuleCross]
            // when it does not. Either way the two bands share exactly one edge and no daylight.
            var drawerLow = start;
            var drawerHigh = start + Cross;
            var capsuleLow = dir > 0 ? 0 : Cross;
            var capsuleHigh = capsuleLow + CapsuleCross;

            // Touching, not overlapping and not separated: the innermost edge of each band is
            // the same number. Asserted this way round so a drawer that grew INTO the capsule
            // (overlap) fails as loudly as one that left a gap.
            Assert.Equal(Math.Max(drawerLow, capsuleLow), Math.Min(drawerHigh, capsuleHigh), 3);
            Assert.True(drawerLow >= 0 && drawerHigh <= CapsuleCross + Cross + 0.5,
                $"{edge}: the drawer must sit inside the window ({drawerLow}..{drawerHigh})");
        }
    }

    [Fact]
    public void TheTwoShapesTileTheWindowExactly()
    {
        // Capsule band + drawer band must fill the window's cross axis with nothing left over.
        // Over-fill clips a row; under-fill makes the window needlessly bigger than its content,
        // which is how the off-screen drift started in the first place.
        foreach (var vertical in new[] { false, true })
        {
            var (w, h) = ClipboardDrawer.WindowFor(vertical, CapsuleLong, CapsuleCross, Rows, open: true);
            var windowCross = vertical ? w : h;

            Assert.Equal(CapsuleCross + Cross, windowCross, 3);
        }
    }

    [Fact]
    public void DrawerSpansTheCapsulesLongAxisExactly()
    {
        // THE second claim. A drawer narrower than the capsule leaves the capsule's rounded end
        // sticking out past it; wider overhangs the screen edge on that axis.
        var (w, h) = ClipboardDrawer.SizeFor(Rows, CapsuleLong);
        Assert.Equal(CapsuleLong, w, 3);

        foreach (var vertical in new[] { false, true })
        {
            var (ww, wh) = ClipboardDrawer.WindowFor(vertical, CapsuleLong, CapsuleCross, Rows, open: true);
            Assert.Equal(CapsuleLong, vertical ? wh : ww, 3);
        }
    }

    // -- the window: exactly the content, no slack -------------------------------------

    [Fact]
    public void WindowWhenClosed_IsExactlyTheCapsule()
    {
        // With no clipboard there is nothing to show, so the window is the capsule. Not the
        // capsule plus a drag disc: that reserve is what used to push the visible content off
        // the screen, and it is gone.
        foreach (var vertical in new[] { false, true })
        {
            var (w, h) = ClipboardDrawer.WindowFor(vertical, CapsuleLong, CapsuleCross, 0, open: false);
            Assert.Equal(CapsuleLong, vertical ? h : w, 3);
            Assert.Equal(CapsuleCross, vertical ? w : h, 3);
        }
    }

    [Fact]
    public void OpenWindow_GrowsOnlyOnTheCrossAxis()
    {
        // The island must not move when the drawer opens, and a window that also grew on the
        // long axis would move it. Asserted as a relation, not a bare number.
        var (closedW, closedH) = ClipboardDrawer.WindowFor(false, CapsuleLong, CapsuleCross, Rows, open: false);
        var (openW, openH) = ClipboardDrawer.WindowFor(false, CapsuleLong, CapsuleCross, Rows, open: true);

        Assert.Equal(closedW, openW, 3);
        Assert.True(openH > closedH, "the drawer has to be able to grow the window");
        Assert.Equal(closedH + Cross, openH, 3);
    }

    [Fact]
    public void OpenWindow_NeverSmallerThanTheClosedOne()
    {
        foreach (var vertical in new[] { false, true })
        {
            var (cw, ch) = ClipboardDrawer.WindowFor(vertical, CapsuleLong, CapsuleCross, Rows, open: false);
            var (ow, oh) = ClipboardDrawer.WindowFor(vertical, CapsuleLong, CapsuleCross, Rows, open: true);
            Assert.True(ow >= cw, $"width {ow} < closed {cw}");
            Assert.True(oh >= ch, $"height {oh} < closed {ch}");
        }
    }

    [Fact]
    public void WindowScalesWithTheRowCount()
    {
        var one = ClipboardDrawer.WindowFor(false, CapsuleLong, CapsuleCross, 1, open: true).Height;
        var eight = ClipboardDrawer.WindowFor(false, CapsuleLong, CapsuleCross, 8, open: true).Height;

        Assert.Equal(7 * OverlayTokens.HistoryPanelRowH, eight - one, 3);
    }

    [Fact]
    public void CrossFor_ClampsToTheTokenCap()
    {
        // An unclamped row count would size a window taller than the screen can hold.
        var capped = ClipboardDrawer.CrossFor(OverlayTokens.HistoryPanelMaxRows + 10);
        Assert.Equal(OverlayTokens.HistoryPanelMaxRows * OverlayTokens.HistoryPanelRowH
                     + 2 * OverlayTokens.HistoryPanelPadY, capped, 3);
    }

    [Fact]
    public void CrossShift_IsZeroWhenGrowingAwayAndNegativeWhenGrowingBack()
    {
        // The sign is the whole ballgame: a drawer that opens upwards needs the window to start
        // ABOVE the capsule by its own height, or half the list lands off-screen.
        Assert.Equal(0, ClipboardDrawer.CrossShiftDipFor(IslandEdge.Top, Rows), 3);
        Assert.Equal(0, ClipboardDrawer.CrossShiftDipFor(IslandEdge.Left, Rows), 3);
        Assert.Equal(-Cross, ClipboardDrawer.CrossShiftDipFor(IslandEdge.Bottom, Rows), 3);
        Assert.Equal(-Cross, ClipboardDrawer.CrossShiftDipFor(IslandEdge.Right, Rows), 3);
    }

    // -- the corner radii at the seam -------------------------------------------------

    [Fact]
    public void SeamCorners_GoSquare_AndOnlyTheSeamCorners()
    {
        foreach (var edge in new[] { IslandEdge.Top, IslandEdge.Bottom, IslandEdge.Left, IslandEdge.Right })
        {
            var vertical = edge is IslandEdge.Left or IslandEdge.Right;
            var dir = ClipboardDrawer.CrossDirectionFor(edge);
            var c = ClipboardDrawer.CapsuleRadiiFor(15, joined: true, dir, vertical);
            var d = ClipboardDrawer.DrawerRadiiFor(joined: true, dir, vertical);

            var capsule = new[] { c.TopLeft, c.TopRight, c.BottomRight, c.BottomLeft };
            var drawer = new[] { d.TopLeft, d.TopRight, d.BottomRight, d.BottomLeft };

            // Exactly two square corners each, and they are the two that meet at the seam.
            Assert.Equal(2, capsule.Count(v => v == 0));
            Assert.Equal(2, drawer.Count(v => v == 0));

            // Index order is TopLeft, TopRight, BottomRight, BottomLeft. A drawer that grows
            // DOWN (a horizontal island) or RIGHT (a vertical one) meets the capsule on that
            // far side, so the capsule's BOTTOM pair and the drawer's TOP pair are square — and
            // they are mirrored about the seam, not the same pair. Squaring the wrong end
            // leaves a visible pinch at the seam.
            var expectedCapsuleSquare = dir > 0
                ? (vertical ? new[] { 1, 2 } : new[] { 2, 3 })   // right / bottom
                : (vertical ? new[] { 0, 3 } : new[] { 0, 1 });  // left / top
            var expectedDrawerSquare = dir > 0
                ? (vertical ? new[] { 0, 3 } : new[] { 0, 1 })
                : (vertical ? new[] { 1, 2 } : new[] { 2, 3 });

            for (var i = 0; i < 4; i++)
            {
                Assert.Equal(expectedCapsuleSquare.Contains(i), capsule[i] == 0);
                Assert.Equal(expectedDrawerSquare.Contains(i), drawer[i] == 0);
                Assert.True(drawer[i] == 0 || drawer[i] == ClipboardDrawer.Radius,
                    $"the drawer's off-seam corners keep the full radius (index {i})");
            }
        }
    }

    [Fact]
    public void UnjoinedShapes_KeepTheFullRadius()
    {
        // Not joined = the capsule is a capsule and the drawer is a drawer, both fully rounded.
        // Without this the seam would stay square after the drawer closed.
        foreach (var vertical in new[] { false, true })
        {
            var c = ClipboardDrawer.CapsuleRadiiFor(15, joined: false, 1, vertical);
            var d = ClipboardDrawer.DrawerRadiiFor(joined: false, 1, vertical);
            Assert.Equal(15, c.TopLeft);
            Assert.Equal(15, c.BottomRight);
            Assert.Equal(ClipboardDrawer.Radius, d.TopLeft);
            Assert.Equal(ClipboardDrawer.Radius, d.BottomLeft);
        }
    }

    [Fact]
    public void TheDrawerKeepsAnOffSeamEnd()
    {
        // A drawer rounded at BOTH ends and square at the seam is a drawer with no bottom. It
        // needs at least one rounded off-seam corner, or the pair reads as a clipped card.
        var d = ClipboardDrawer.DrawerRadiiFor(joined: true, crossDirection: 1, isVertical: false);
        Assert.True(d.BottomLeft > 0 && d.BottomRight > 0,
            "the drawer's off-seam end must stay rounded");
    }

    // -- the track -------------------------------------------------------------------

    [Fact]
    public void DrawerIsAtTheSeamWhenTheOpenFinishes()
    {
        // t = 1 is the rest position. If the slide did not land on 0 the drawer would hang
        // permanently off the capsule by a visible amount.
        var frame = ClipboardDrawer.FrameAt(1.0, opening: true, crossDirection: 1);
        Assert.Equal(0, frame.Slide, 6);
        Assert.Equal(1, frame.Opacity, 6);
    }

    [Fact]
    public void DrawerStartsOutsideTheSeamAndTravelsIn()
    {
        // The opposite end: at t = 0 the drawer is exactly one travel's worth outside, so the
        // gesture reads as coming FROM the capsule rather than materialising at it.
        var frame = ClipboardDrawer.FrameAt(0.0, opening: true, crossDirection: 1);
        Assert.Equal(OverlayTokens.ClipboardDrawerTravel, frame.Slide, 6);
        Assert.Equal(0, frame.Opacity, 6);
    }

    [Fact]
    public void SlideIsSignedByTheOpenDirection()
    {
        // On a Top island the drawer comes from below; on a Bottom island from above. Same
        // magnitude, opposite sign -- getting this wrong slides the drawer the wrong way out
        // of the seam on half the edges.
        var down = ClipboardDrawer.SlideDipAt(0.5, opening: true, crossDirection: 1);
        var up = ClipboardDrawer.SlideDipAt(0.5, opening: true, crossDirection: -1);
        Assert.Equal(down, -up, 6);
    }

    [Fact]
    public void ClosingIsTheTimeReverseOfOpening()
    {
        // An interrupted open has to be able to reverse from exactly where it is. That is only
        // true if the closing ramp is the mirror of the opening one, which is what makes this
        // a property of the function rather than of the caller.
        foreach (var t in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            Assert.Equal(ClipboardDrawer.PhaseAt(t, opening: true),
                1.0 - ClipboardDrawer.PhaseAt(t, opening: false), 6);
        }
    }

    [Fact]
    public void TrackIsClampedOutsideZeroToOne()
    {
        // A morph can be handed a t slightly outside its range during a restart; the drawer
        // must not slide further than its travel or fade past full.
        Assert.Equal(0, ClipboardDrawer.PhaseAt(-0.5, opening: true), 6);
        Assert.Equal(1, ClipboardDrawer.PhaseAt(1.5, opening: true), 6);
        // The slide is clamped at both ends too: it may not travel further than its own budget,
        // and it may not end up on the far side of the seam.
        Assert.Equal(OverlayTokens.ClipboardDrawerTravel, ClipboardDrawer.SlideDipAt(-0.5, true, 1), 6);
        Assert.InRange(ClipboardDrawer.SlideDipAt(1.5, true, 1), -1e-9, 1e-9);
    }
}
