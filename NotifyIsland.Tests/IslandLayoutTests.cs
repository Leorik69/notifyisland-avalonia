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

    // -- 1.12.3 goo blob: the window is bigger than the capsule, the capsule must not move --

    [Fact]
    public void BlobWindowFor_Horizontal_KeepsXAndCentresCrossAxis()
    {
        // Capsule 170×30 at (400, 100); the blob window needs 264 of cross axis.
        var (x, y) = IslandLayout.BlobWindowFor(
            isVertical: false, homeX: 400, homeY: 100, homeW: 170, homeH: 30, windowW: 352, windowH: 264);
        Assert.Equal(400, x);
        Assert.Equal(100 - (264 - 30) / 2, y);
    }

    [Fact]
    public void BlobWindowFor_Vertical_KeepsYAndCentresCrossAxis()
    {
        var (x, y) = IslandLayout.BlobWindowFor(
            isVertical: true, homeX: 400, homeY: 100, homeW: 30, homeH: 170, windowW: 264, windowH: 352);
        Assert.Equal(400 - (264 - 30) / 2, x);
        Assert.Equal(100, y);
    }

    [Fact]
    public void BlobWindowFor_LeavesCapsuleAtTheSameScreenSpot()
    {
        // The whole point of the function: whatever the window size, the capsule's top-left
        // on screen is unchanged. Reproduce the offset math PlaceIsland does. The two
        // orientations use the same capsule, rotated: 170×30 on Top/Bottom, 30×170 on
        // Left/Right.
        const int homeX = 400, homeY = 100;
        foreach (var vertical in new[] { false, true })
        {
            var (homeW, homeH) = vertical ? (30, 170) : (170, 30);
            var (hw, hh) = vertical ? (homeH, homeW) : (homeW, homeH);
            // Both window shapes: the plain capsule-sized window (no blob) and the enlarged
            // one ClipboardBlob asks for.
            var w0 = ClipboardBlob.WindowFor(vertical, hw, hh);
            foreach (var (winW, winH) in new[] { ((double)homeW, (double)homeH), w0 })
            {
                var (x, y) = IslandLayout.BlobWindowFor(vertical, homeX, homeY, homeW, homeH,
                    (int)winW, (int)winH);
                // Capsule anchored Leading on the long axis, Center on the cross axis.
                var capX = vertical ? x + (winW - homeW) / 2 : x;
                var capY = vertical ? y : y + (winH - homeH) / 2;
                Assert.Equal(homeX, Math.Round(capX), 1);
                Assert.Equal(homeY, Math.Round(capY), 1);
            }
        }
    }

    [Fact]
    public void BlobWindowFor_WindowNoLargerThanCapsule_IsIdentity()
    {
        var (x, y) = IslandLayout.BlobWindowFor(false, 10, 20, 170, 30, 170, 30);
        Assert.Equal((10, 20), (x, y));
        // A stale/shrinking measurement must never push the window back over the capsule.
        var (x2, y2) = IslandLayout.BlobWindowFor(false, 10, 20, 170, 30, 100, 10);
        Assert.Equal((10, 20), (x2, y2));
    }
}
