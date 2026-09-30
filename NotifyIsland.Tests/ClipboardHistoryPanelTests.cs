using System;
using NotifyIsland;
using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// The 1.12.3 clipboard history panel's row-count rule, the only part of it that survived the
/// 1.14 rewrite. The panel's PLACEMENT (its asymmetric seat beside the ball, the gap after the
/// ball, <c>BallSideExtent</c>/<c>PanelSideExtent</c>/<c>CrossOriginFor</c>) is gone with the ball;
/// the drawer that replaced it is tested in <see cref="ClipboardDrawerTests"/>.
/// </summary>
public class ClipboardHistoryPanelTests
{
    private const int Rows = OverlayTokens.HistoryPanelMaxRows;

    [Theory]
    [InlineData(IslandEdge.Top, 1)]
    [InlineData(IslandEdge.Left, 1)]
    [InlineData(IslandEdge.Bottom, -1)]
    [InlineData(IslandEdge.Right, -1)]
    public void CrossDirection_OpensTowardsTheScreenInterior(IslandEdge edge, int expected)
    {
        // Top → down, Bottom → up, Left → right, Right → left. The one that matters in practice
        // is Top → down, because that is the default edge.
        Assert.Equal(expected, ClipboardHistoryPanel.CrossDirectionFor(edge));
    }

    [Fact]
    public void CrossDirection_AgreesWithTheDrawer()
    {
        // 1.14: ClipboardHistoryPanel.CrossDirectionFor is now a forwarding member. If the two
        // ever disagreed, the legacy call sites would open the list the wrong way — so the
        // forwarding is asserted rather than assumed.
        foreach (var edge in new[] { IslandEdge.Top, IslandEdge.Bottom, IslandEdge.Left, IslandEdge.Right })
            Assert.Equal(ClipboardDrawer.CrossDirectionFor(edge), ClipboardHistoryPanel.CrossDirectionFor(edge));
    }

    [Fact]
    public void SizeFor_HasOneRowPerRowPlusPadding()
    {
        var (w, h) = ClipboardHistoryPanel.SizeFor(Rows);

        Assert.Equal(OverlayTokens.HistoryPanelW, w);
        Assert.Equal(Rows * OverlayTokens.HistoryPanelRowH + 2 * OverlayTokens.HistoryPanelPadY, h, 3);
    }

    [Fact]
    public void SizeFor_ClampsToTheTokenCap()
    {
        // Asking for more rows than fit must not silently produce a taller list: the window
        // around it is sized from this number, so an unclamped value would overflow the screen.
        var capped = ClipboardHistoryPanel.SizeFor(OverlayTokens.HistoryPanelMaxRows + 10);
        Assert.Equal(OverlayTokens.HistoryPanelMaxRows * OverlayTokens.HistoryPanelRowH
                     + 2 * OverlayTokens.HistoryPanelPadY, capped.Height, 3);
    }
}
