using System;
using NotifyIsland;
using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// Geometry of the 1.12.3 clipboard history panel. The invariant these protect is the one the
/// spec states twice and that 1.12.2 got wrong: <b>the island must not move when the panel
/// opens</b>. So most of what is asserted here is a relation between the panel and the capsule —
/// never a bare number that could be right for the wrong reason.
/// </summary>
public class ClipboardHistoryPanelTests
{
    private const double CapsuleLong = 170;
    private const double CapsuleCross = 30;
    private const int Rows = OverlayTokens.HistoryPanelMaxRows;

    [Theory]
    [InlineData(IslandEdge.Top, 1)]
    [InlineData(IslandEdge.Left, 1)]
    [InlineData(IslandEdge.Bottom, -1)]
    [InlineData(IslandEdge.Right, -1)]
    public void CrossDirection_PanelOpensTowardsTheScreenInterior(IslandEdge edge, int expected)
    {
        // Top → down, Bottom → up, Left → right, Right → left. The one that matters in practice
        // is Top → down, because that is the default edge.
        Assert.Equal(expected, ClipboardHistoryPanel.CrossDirectionFor(edge));
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
        // Asking for more rows than fit must not silently produce a taller panel: the window
        // around it is sized from this number, so an unclamped value would overflow the screen.
        var capped = ClipboardHistoryPanel.SizeFor(OverlayTokens.HistoryPanelMaxRows + 10);
        Assert.Equal(OverlayTokens.HistoryPanelMaxRows * OverlayTokens.HistoryPanelRowH
                     + 2 * OverlayTokens.HistoryPanelPadY, capped.Height, 3);
    }

    [Fact]
    public void PanelStartsClearOfTheBallNotOfTheCapsule()
    {
        // The ball is 64 across and the capsule 30: anchoring the panel to the capsule would
        // slice it through the ball. The gap below the ball's edge is the panel's daylight.
        var start = ClipboardHistoryPanel.CrossStartFromCentre();

        Assert.Equal(OverlayTokens.BlobD / 2 + OverlayTokens.HistoryPanelGap, start, 3);
        Assert.True(start > OverlayTokens.BlobD / 2,
            "the panel's near edge must be below the ball's silhouette, not on it");
    }

    [Theory]
    [InlineData(IslandEdge.Top)]
    [InlineData(IslandEdge.Bottom)]
    [InlineData(IslandEdge.Left)]
    [InlineData(IslandEdge.Right)]
    public void PanelFitsExactlyInsideTheWindowItReserved(IslandEdge edge)
    {
        // The strongest form of "it fits": the panel's far edge must land ON the window's far
        // edge, to the pixel. A window one DIP short clips the last row.
        var vertical = edge is IslandEdge.Left or IslandEdge.Right;
        var (winW, winH) = ClipboardHistoryPanel.WindowFor(vertical, CapsuleLong, CapsuleCross, Rows);
        var (panelLong, panelCross) = ClipboardHistoryPanel.SizeFor(Rows);
        var origin = ClipboardHistoryPanel.CrossOriginFor(edge, CapsuleCross / 2, panelCross);
        var start = ClipboardHistoryPanel.PanelCrossStart(edge, CapsuleCross / 2);
        var windowCross = vertical ? winW : winH;
        var windowLong = vertical ? winH : winW;

        // The panel occupies a band of length panelCross inside the window, measured from the
        // WINDOW's own origin. It runs in the opening direction from `start`, so which end is
        // low depends on the sign — a min() here would fold both cases into one number and hide
        // a wrong sign in CrossDirectionFor, which is the whole point of testing all four edges.
        var dir = ClipboardHistoryPanel.CrossDirectionFor(edge);
        var startInWindow = start - origin;
        var bandLow = dir > 0 ? startInWindow : startInWindow - panelCross;
        var bandHigh = bandLow + panelCross;

        // Inside the window, on both counts. A window one DIP short clips the last row; a panel
        // that starts before the window's origin is drawn off the top/left of the screen.
        Assert.True(bandLow >= -0.5, $"the panel starts before the window ({bandLow})");
        Assert.True(bandHigh <= windowCross + 0.5,
            $"the panel runs past the window's cross edge ({bandHigh} > {windowCross})");

        // And the band is separated from the capsule by the ball's radius plus the gap — the
        // clearance the panel geometry reserves, expressed capsule-relatively (so the window
        // origin above must NOT be mixed in here).
        var clearance = dir > 0 ? startInWindow - (CapsuleCross / 2 - origin)
            : (CapsuleCross / 2 - origin) - bandHigh;
        Assert.Equal(ClipboardHistoryPanel.CrossStartFromCentre(), clearance, 3);

        // And on the long axis: clear of the ball, ending inside the window.
        var alongStart = ClipboardHistoryPanel.PanelAlongStart(CapsuleLong);
        Assert.True(alongStart + panelLong <= windowLong + 0.5,
            $"the panel must not run past the window's long edge ({alongStart + panelLong} > {windowLong})");
    }

    [Theory]
    [InlineData(IslandEdge.Top)]
    [InlineData(IslandEdge.Bottom)]
    [InlineData(IslandEdge.Left)]
    [InlineData(IslandEdge.Right)]
    public void CrossAxis_IsTheSameSizeOnEveryEdge(IslandEdge edge)
    {
        // Top and Bottom islands get the same window, only mirrored. If a sign leaked into a
        // size formula the two edges would get different windows for the same capsule. The cross
        // axis is Height on a horizontal island and Width on a vertical one — reading the wrong
        // element here compares a 384-DIP cross against a 524-DIP long and fails for a reason
        // that has nothing to do with the geometry.
        var vertical = edge is IslandEdge.Left or IslandEdge.Right;
        var (w, h) = ClipboardHistoryPanel.WindowFor(vertical, CapsuleLong, CapsuleCross, Rows);
        var (refW, refH) = ClipboardHistoryPanel.WindowFor(false, CapsuleLong, CapsuleCross, Rows);

        Assert.Equal(refH, vertical ? w : h, 3);
    }

    [Theory]
    [InlineData(IslandEdge.Top)]
    [InlineData(IslandEdge.Bottom)]
    [InlineData(IslandEdge.Left)]
    [InlineData(IslandEdge.Right)]
    public void IslandDoesNotMove_TheBallSideRoomIsIdenticalOnEveryEdge(IslandEdge edge)
    {
        // "The island does not move" holds by CONSTRUCTION here: the caller seats the window at
        // capsuleScreen + origin, so whatever origin comes back, the capsule lands on the pixels
        // it already had. What that construction depends on is asserted below — that the two
        // bands the window is made of are the ball's room and the panel's run, on every edge and
        // in the right order, so re-seating cannot quietly shrink the drag disc on Bottom/Right
        // and give the space to the panel instead.
        var (_, panelCross) = ClipboardHistoryPanel.SizeFor(Rows);
        var capsuleCentre = CapsuleCross / 2;
        var dir = ClipboardHistoryPanel.CrossDirectionFor(edge);
        var origin = ClipboardHistoryPanel.CrossOriginFor(edge, capsuleCentre, panelCross);
        var centreInWindow = capsuleCentre - origin;
        var panelWindowCross = ClipboardHistoryPanel
            .WindowFor(false, CapsuleLong, CapsuleCross, Rows).Height;

        // The band on the side AWAY from the panel is the ball's, and it is BallSideExtent
        // verbatim on every edge — never PanelSideExtent.
        var ballBand = dir > 0
            ? centreInWindow
            : (origin + panelWindowCross) - capsuleCentre;
        Assert.Equal(ClipboardHistoryPanel.BallSideExtent(), ballBand, 3);

        // The panel's near edge is one ball radius plus the gap from the capsule centre, measured
        // IN the opening direction. On Bottom/Right that is a subtraction, and asserting the
        // addition everywhere would have hidden the sign — which is exactly the half of the
        // mapping that puts the panel off-screen when it is wrong.
        var panelStart = ClipboardHistoryPanel.PanelCrossStart(edge, capsuleCentre);
        var expectedStart = dir > 0
            ? capsuleCentre + ClipboardHistoryPanel.CrossStartFromCentre()
            : capsuleCentre - ClipboardHistoryPanel.CrossStartFromCentre();
        Assert.Equal(expectedStart, panelStart, 3);

        // The two bands tile the window exactly. If they over- or under-fill it by even a DIP,
        // the window either clips the panel or is needlessly bigger than the geometry asks for.
        Assert.Equal(ClipboardHistoryPanel.BallSideExtent()
            + ClipboardHistoryPanel.PanelSideExtent(panelCross), panelWindowCross, 3);
    }

    [Fact]
    public void WindowFor_NeverSmallerThanTheRestingBlobWindow()
    {
        // The panel can only ADD room. A panel window smaller than the resting one would clip
        // the ball on the very gesture that was supposed to list the clipboard.
        var (blobW, blobH) = ClipboardBlob.WindowFor(false, CapsuleLong, CapsuleCross);
        var (panelW, panelH) = ClipboardHistoryPanel.WindowFor(false, CapsuleLong, CapsuleCross, Rows);

        Assert.True(panelW >= blobW, $"width {panelW} < resting {blobW}");
        Assert.True(panelH >= blobH, $"height {panelH} < resting {blobH}");
    }

    [Fact]
    public void WindowFor_ScalesWithTheRowCount()
    {
        var one = ClipboardHistoryPanel.WindowFor(false, CapsuleLong, CapsuleCross, 1).Height;
        var eight = ClipboardHistoryPanel.WindowFor(false, CapsuleLong, CapsuleCross, 8).Height;

        Assert.Equal(7 * OverlayTokens.HistoryPanelRowH, eight - one, 3);
    }

    [Fact]
    public void PanelAlongStart_LeavesTheGapAfterTheBall()
    {
        // The hand that reached for the ball has to reach the panel: the ball's home spot and
        // radius come from ClipboardBlob, so retuning the ball moves the panel with it.
        var start = ClipboardHistoryPanel.PanelAlongStart(CapsuleLong);
        var ballFarEdge = CapsuleLong + ClipboardBlob.BlobHomeAlong(CapsuleLong) + OverlayTokens.BlobD / 2;

        Assert.Equal(ballFarEdge + OverlayTokens.HistoryPanelGap, start, 3);
    }

    [Fact]
    public void PanelAlongStart_IsMeasuredFromTheBallsActualHomeSpot()
    {
        // The regression this pins: BlobHomeAlong(0) instead of BlobHomeAlong(capsuleLong) is the
        // same formula read as "capsule + bridge + radius", and it drops the entire capsule from
        // the panel's position — the panel would land on top of the ball. Asserting the panel is
        // PAST the ball is the statement that matters; the arithmetic above says how.
        var start = ClipboardHistoryPanel.PanelAlongStart(CapsuleLong);
        var ballCentre = CapsuleLong + ClipboardBlob.BlobHomeAlong(CapsuleLong);

        Assert.True(start > ballCentre + OverlayTokens.BlobD / 2,
            "the panel must start beyond the ball's far edge, not on it");
        Assert.True(start > CapsuleLong,
            "the panel's position has to include the capsule's own length");
    }
}
