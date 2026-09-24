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
}
