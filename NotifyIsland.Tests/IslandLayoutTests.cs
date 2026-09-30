using Xunit;

namespace NotifyIsland.Tests;

public class IslandLayoutTests
{
    [Theory]
    [InlineData(IslandOrientation.Horizontal, IslandEdge.Left, false)]
    [InlineData(IslandOrientation.Vertical, IslandEdge.Top, true)]
    [InlineData(IslandOrientation.Auto, IslandEdge.Top, false)]
    [InlineData(IslandOrientation.Auto, IslandEdge.Bottom, false)]
    [InlineData(IslandOrientation.Auto, IslandEdge.Left, true)]
    [InlineData(IslandOrientation.Auto, IslandEdge.Right, true)]
    public void IsVertical_Resolves(IslandOrientation o, IslandEdge e, bool expect) =>
        Assert.Equal(expect, IslandLayout.IsVertical(o, e));

    [Fact]
    public void SizeFor_Horizontal_UsesWidthMorph()
    {
        var (w, h) = IslandLayout.SizeFor(OverlayKind.Idle, weatherEnabled: false,
            IslandOrientation.Horizontal, IslandEdge.Top);
        Assert.Equal(OverlayTokens.CollapsedW, w);
        Assert.Equal(OverlayTokens.CollapsedH, h);

        var (nw, nh) = IslandLayout.SizeFor(OverlayKind.Notification, false,
            IslandOrientation.Horizontal, IslandEdge.Top);
        Assert.True(nw > OverlayTokens.CollapsedW);
        Assert.Equal(OverlayTokens.CollapsedH, nh);
    }

    [Fact]
    public void SizeFor_Vertical_SwapsAxes()
    {
        var (w, h) = IslandLayout.SizeFor(OverlayKind.Idle, weatherEnabled: true,
            IslandOrientation.Vertical, IslandEdge.Left);
        Assert.Equal(OverlayTokens.CollapsedH, w);
        Assert.Equal(OverlayTokens.CollapsedWeatherW, h);

        var (nw, nh) = IslandLayout.SizeFor(OverlayKind.Media, false,
            IslandOrientation.Auto, IslandEdge.Right);
        Assert.Equal(OverlayTokens.CollapsedH, nw);
        Assert.True(nh >= OverlayTokens.ExpandedMinW);
    }

    [Fact]
    public void Place_TopCenter_DefaultInset()
    {
        var (x, y) = IslandLayout.Place(0, 0, 1920, 1080, 140, 30, IslandEdge.Top, 0, 0);
        Assert.Equal((1920 - 140) / 2, x);
        Assert.Equal(IslandLayout.DefaultEdgeInsetPx, y);
    }

    [Fact]
    public void Place_And_OffsetsFromPosition_RoundTrip()
    {
        const int waX = 0, waY = 0, waW = 1920, waH = 1080, pw = 210, ph = 30;
        var edge = IslandEdge.Bottom;
        var (x, y) = IslandLayout.Place(waX, waY, waW, waH, pw, ph, edge, 40, -10);
        var (ox, oy) = IslandLayout.OffsetsFromPosition(waX, waY, waW, waH, pw, ph, edge, x, y);
        Assert.Equal(40, ox);
        Assert.Equal(-10, oy);
    }

    [Fact]
    public void DragHoldMs_Is200() => Assert.Equal(200, IslandLayout.DragHoldMs);

    // -- 1.14 clipboard drawer: the window is capsule(+drawer), the capsule must not move --

    [Fact]
    public void DrawerWindowFor_Horizontal_KeepsXAndTakesTheDrawerOnY()
    {
        // Capsule 170x30 at (400, 100), drawer opening downwards (+1) so it needs no shift.
        var (x, y) = IslandLayout.DrawerWindowFor(
            isVertical: false, capsuleX: 400, capsuleY: 100, crossShiftDip: 0, scale: 1);
        Assert.Equal(400, x);
        Assert.Equal(100, y);
    }

    [Fact]
    public void DrawerWindowFor_Vertical_KeepsYAndTakesTheDrawerOnX()
    {
        var (x, y) = IslandLayout.DrawerWindowFor(
            isVertical: true, capsuleX: 400, capsuleY: 100, crossShiftDip: 0, scale: 1);
        Assert.Equal(400, x);
        Assert.Equal(100, y);
    }

    [Fact]
    public void DrawerWindowFor_ShiftsBackByTheDrawerWhenItOpensUpwards()
    {
        // A Bottom/Right island grows its drawer UPWARDS/LEFTWARDS, so the drawer occupies the
        // part of the window nearest the origin and the window must start that much ABOVE the
        // capsule. Getting this sign wrong is what draws the drawer off-screen.
        var (x, y) = IslandLayout.DrawerWindowFor(
            isVertical: false, capsuleX: 400, capsuleY: 100, crossShiftDip: -120, scale: 1);
        Assert.Equal(400, x);
        Assert.Equal(-20, y);
    }

    [Fact]
    public void DrawerWindowFor_LeavesCapsuleAtTheSameScreenSpot()
    {
        // The whole point of the function: whatever the drawer does, the capsule's screen spot is
        // unchanged. Reproduce the anchor math PlaceIsland uses. The two orientations use the
        // same capsule rotated: 170x30 on Top/Bottom, 30x170 on Left/Right.
        // The shifts below are ClipboardDrawer.CrossShiftDipFor's whole range: 0 (drawer grows
        // away from the capsule) and negative by the drawer's extent (grows the other way).
        const int capX = 400, capY = 100;
        foreach (var vertical in new[] { false, true })
        {
            foreach (var shiftDip in new[] { 0.0, -120.0, -384.0 })
            {
                var (x, y) = IslandLayout.DrawerWindowFor(vertical, capX, capY, shiftDip, 1);
                // The capsule is anchored at window offset -shiftDip on the cross axis -- flush
                // against the edge the drawer is NOT growing from.
                var capsuleX = vertical ? x - (int)shiftDip : x;
                var capsuleY = vertical ? y : y - (int)shiftDip;
                Assert.Equal(capX, capsuleX);
                Assert.Equal(capY, capsuleY);
            }
        }
    }

    [Fact]
    public void DrawerWindowFor_ScalesTheShiftToPixels()
    {
        // At 150% the same 120 DIP shift is 180 px. Measuring in DIP would leave a 60 px seam.
        var (_, y1) = IslandLayout.DrawerWindowFor(false, 0, 0, -120, scale: 1);
        var (_, y15) = IslandLayout.DrawerWindowFor(false, 0, 0, -120, scale: 1.5);
        Assert.Equal(-120, y1);
        Assert.Equal(-180, y15);
    }

    [Fact]
    public void DrawerWindowFor_ZeroShiftIsIdentity()
    {
        var (x, y) = IslandLayout.DrawerWindowFor(false, 10, 20, 0, 1);
        Assert.Equal((10, 20), (x, y));
    }
}
