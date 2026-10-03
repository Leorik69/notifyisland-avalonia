using System;
using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// Pins the reduced-motion policy for the two per-second flickers. Track B of
/// docs/superpowers/plans/2026-09-30--animation-audit.md asked for both to freeze; the colon does,
/// the seconds strip deliberately does not. These tests exist so that a future "unify the two"
/// refactor has to come here and argue it out rather than quietly changing the clock.
/// </summary>
public class FlickerGateTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(59, false)]
    public void Colon_WithoutReduced_BlinksOnEvenSeconds(int second, bool expected)
    {
        var t = new DateTime(2026, 9, 30, 12, 0, 0).AddSeconds(second);
        Assert.Equal(expected, FlickerGate.ColonLit(reducedMotion: false, t));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(59)]
    public void Colon_WithReduced_AlwaysLit(int second)
    {
        var t = new DateTime(2026, 9, 30, 12, 0, 0).AddSeconds(second);
        Assert.True(FlickerGate.ColonLit(reducedMotion: true, t));
    }

    [Fact]
    public void Colon_ReducedMotionRemovesEveryOffHalf()
    {
        // The point of the gate: a 1 Hz flicker in a 30 DIP capsule. Walk a whole minute and count
        // how many frames the colon would have been dim without the gate.
        var start = new DateTime(2026, 9, 30, 12, 0, 0);
        var dimWithout = 0;
        var dimWith = 0;
        for (var s = 0; s < 60; s++)
        {
            var t = start.AddSeconds(s);
            if (FlickerGate.ColonOpacity(FlickerGate.ColonLit(false, t)) == FlickerGate.ColonDimOpacity)
                dimWithout++;
            if (FlickerGate.ColonOpacity(FlickerGate.ColonLit(true, t)) == FlickerGate.ColonDimOpacity)
                dimWith++;
        }

        Assert.Equal(30, dimWithout);
        Assert.Equal(0, dimWith);
    }

    [Fact]
    public void ColonOpacity_UsesTheHistoricalDimValue()
    {
        // 0.28 is what shipped before the gate; a "tidied up" 0.3 here would silently restyle the
        // clock for every user who is NOT in reduced motion.
        Assert.Equal(0.28, FlickerGate.ColonDimOpacity);
        Assert.Equal(1.0, FlickerGate.ColonLitOpacity);
        Assert.Equal(1.0, FlickerGate.ColonOpacity(lit: true));
        Assert.Equal(0.28, FlickerGate.ColonOpacity(lit: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SecondsStrip_KeepsUpdating_UnderBothMotionModes(bool reducedMotion)
    {
        // The lit count IS the elapsed-seconds readout. Freezing it would show a stale count for
        // the rest of the minute — a worse outcome than the flicker, because the user loses data
        // rather than gaining comfort.
        Assert.True(FlickerGate.SecondsStripUpdates(reducedMotion));
    }

    [Theory]
    [InlineData(true, 30, 12)]
    [InlineData(false, 30, 12)]
    public void LitCountForDisplay_MatchesLitCount_RegardlessOfMotion(bool reducedMotion, int slots, int second)
    {
        var t = new DateTime(2026, 9, 30, 12, 0, 0).AddSeconds(second);
        Assert.Equal(
            SecondsStripLogic.LitCount(t, slots),
            SecondsStripLogic.LitCountForDisplay(reducedMotion, t, slots));
    }

    [Fact]
    public void LitCountForDisplay_StillTracksProgress_UnderReducedMotion()
    {
        var start = new DateTime(2026, 9, 30, 12, 0, 0);
        var early = SecondsStripLogic.LitCountForDisplay(true, start.AddSeconds(0), 24);
        var mid = SecondsStripLogic.LitCountForDisplay(true, start.AddSeconds(30), 24);
        var late = SecondsStripLogic.LitCountForDisplay(true, start.AddSeconds(59), 24);

        Assert.Equal(0, early);
        Assert.Equal(12, mid);
        Assert.Equal(23, late);
        Assert.True(early < mid && mid < late);
    }
}
