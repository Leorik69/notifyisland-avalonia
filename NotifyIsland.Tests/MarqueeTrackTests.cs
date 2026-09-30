using NotifyIsland;
using Xunit;

namespace NotifyIsland.Tests;

public class MarqueeTrackTests
{
    private const double Text = 200.0;   // wider than the slot
    private const double Slot = 100.0;
    private const double Travel = Text - Slot;

    [Fact]
    public void TextThatFitsNeverMoves()
    {
        Assert.Equal(0, MarqueeTrack.OffsetFor(0, 90, 100));
        Assert.Equal(0, MarqueeTrack.OffsetFor(5000, 90, 100));
        Assert.Equal(0, MarqueeTrack.OffsetFor(5000, 100, 100));   // exactly fits
    }

    [Fact]
    public void ShouldScrollOnlyWhenTheTextIsWider()
    {
        Assert.False(MarqueeTrack.ShouldScroll(90, 100));
        Assert.False(MarqueeTrack.ShouldScroll(100, 100));
        Assert.True(MarqueeTrack.ShouldScroll(101, 100));
    }

    [Fact]
    public void ZeroSlotWidthIsInert()
    {
        // A slot that has not been laid out yet must not produce NaN or an infinite travel.
        Assert.Equal(0, MarqueeTrack.OffsetFor(1000, 200, 0));
        Assert.False(MarqueeTrack.ShouldScroll(200, 0));
    }

    [Fact]
    public void HoldsStillAtTheStartForTheEdgePause()
    {
        Assert.Equal(0, MarqueeTrack.OffsetFor(0, Text, Slot));
        Assert.Equal(0, MarqueeTrack.OffsetFor(MarqueeTrack.EdgePauseMs - 1, Text, Slot));
    }

    [Fact]
    public void ScrollsLeftDuringTheMovePhase()
    {
        var mid = MarqueeTrack.EdgePauseMs + (Travel / MarqueeTrack.DipPerSecond * 1000.0) / 2;
        var offset = MarqueeTrack.OffsetFor(mid, Text, Slot);
        Assert.InRange(offset, -Travel, 0);
        Assert.True(offset < 0);
    }

    [Fact]
    public void ReachesTheFarEndAndHoldsThere()
    {
        var stepMs = Travel / MarqueeTrack.DipPerSecond * 1000.0;
        var atEnd = MarqueeTrack.EdgePauseMs + stepMs;
        Assert.Equal(-Travel, MarqueeTrack.OffsetFor(atEnd, Text, Slot), 6);
        // Held, not creeping, during the second pause.
        Assert.Equal(-Travel, MarqueeTrack.OffsetFor(atEnd + MarqueeTrack.EdgePauseMs / 2, Text, Slot), 6);
    }

    [Fact]
    public void ComesBackRightwardsAfterTheSecondPause()
    {
        var stepMs = Travel / MarqueeTrack.DipPerSecond * 1000.0;
        var backStart = 2 * MarqueeTrack.EdgePauseMs + stepMs;
        var mid = backStart + stepMs / 2;
        var offset = MarqueeTrack.OffsetFor(mid, Text, Slot);
        Assert.InRange(offset, -Travel, 0);
        Assert.True(offset > -Travel);
    }

    [Fact]
    public void LoopsBackToTheStartWithoutAJump()
    {
        var stepMs = Travel / MarqueeTrack.DipPerSecond * 1000.0;
        var period = 2 * (stepMs + MarqueeTrack.EdgePauseMs);
        // The return leg ends at 0, so the last frame before the loop and the first frame
        // after it are both at the start. A raw modulo would instead jump from -travel
        // straight across the panel to 0, which reads as a glitch on every loop.
        var before = MarqueeTrack.OffsetFor(period - 1, Text, Slot);
        var after = MarqueeTrack.OffsetFor(period, Text, Slot);
        Assert.Equal(0, after, 6);
        Assert.True(Math.Abs(before - after) < 1.0,
            $"the loop jumps from {before} to {after}");
    }

    [Fact]
    public void MotionIsAlwaysInsideTheSlot()
    {
        var stepMs = Travel / MarqueeTrack.DipPerSecond * 1000.0;
        var period = 2 * (stepMs + MarqueeTrack.EdgePauseMs);
        for (var ms = 0; ms <= period; ms += 37)
        {
            var o = MarqueeTrack.OffsetFor(ms, Text, Slot);
            Assert.InRange(o, -Travel - 0.001, 0.001);
        }
    }

    [Fact]
    public void NegativeElapsedTimeIsFoldedBackNotThrowing()
    {
        // A clock that jumps backwards (NTP correction) must not produce a mirrored motion.
        var a = MarqueeTrack.OffsetFor(-500, Text, Slot);
        var stepMs = Travel / MarqueeTrack.DipPerSecond * 1000.0;
        var period = 2 * (stepMs + MarqueeTrack.EdgePauseMs);
        var b = MarqueeTrack.OffsetFor(period - 500, Text, Slot);
        Assert.Equal(b, a, 6);
    }

    [Fact]
    public void SlowerWhenTheTextIsLonger()
    {
        // Longer travel must mean a longer period, so the reading speed stays constant rather
        // than a long title zipping past.
        var shortStep = (Text - Slot) / MarqueeTrack.DipPerSecond * 1000.0;
        var longStep = (600 - Slot) / MarqueeTrack.DipPerSecond * 1000.0;
        Assert.True(longStep > shortStep);
    }

    [Fact]
    public void EmptyTextHasNoWidth()
    {
        Assert.Equal(0, MarqueeTrack.EstimateWidth(null));
        Assert.Equal(0, MarqueeTrack.EstimateWidth(""));
    }

    [Fact]
    public void EstimatedWidthGrowsWithLength()
    {
        var a = MarqueeTrack.EstimateWidth("Привет");
        var b = MarqueeTrack.EstimateWidth("Привет, NotifyIsland");
        Assert.True(b > a);
    }

    [Fact]
    public void TheLineFitsInsideThePanelBudget()
    {
        // The caption line lives inside the monitor panel, so it must be short enough not to
        // blow the height formula. A status row is 16 DIP; 14 leaves the gap spacing intact.
        Assert.True(MarqueeTrack.LineH <= 16.0);
    }
}
