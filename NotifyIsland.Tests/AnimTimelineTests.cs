using System;
using System.Linq;
using Xunit;

namespace NotifyIsland.Tests;

public class AnimTimelineTests
{
    private static AnimTimeline Copy() => new(
        ("peek", 0.00, 0.40),
        ("detach", 0.40, 0.70),
        ("travel", 0.70, 1.00));

    [Fact]
    public void SpecExample_KeepsPhaseOrderAndBounds()
    {
        var tl = Copy();
        Assert.Equal(3, tl.Phases.Count);
        Assert.Equal(new[] { "peek", "detach", "travel" }, tl.Phases.Select(p => p.Name));
        Assert.Equal(0.0, tl.Phases[0].From, 12);
        Assert.Equal(1.0, tl.Phases[^1].To, 12);
        Assert.False(tl.IsReversed);
    }

    [Fact]
    public void Phases_TileZeroToOne_WithoutGapsOrOverlaps()
    {
        var tl = Copy();
        Assert.Equal(0.0, tl.Phases[0].From, 12);
        for (var i = 1; i < tl.Phases.Count; i++)
        {
            Assert.Equal(tl.Phases[i - 1].To, tl.Phases[i].From, 12);
        }

        Assert.Equal(1.0, tl.Phases[^1].To, 12);

        // The whole point of the spec: phases are one list, so the two halves of the copy scenario
        // can no longer be desynced by a hand-edited constant.
        var total = tl.Phases.Sum(p => p.Span);
        Assert.Equal(1.0, total, 12);
    }

    // Progress comes out of a division, so compare it with a tolerance; only the phase name is exact.
    private static void AssertPhase(AnimTimeline tl, double t, string expectedName, double expectedProgress)
    {
        var (name, progress) = tl.PhaseAt(t);
        Assert.Equal(expectedName, name);
        Assert.Equal(expectedProgress, progress, 9);
    }

    [Fact]
    public void PhaseAt_MapsLocalProgressInsideEachPhase()
    {
        var tl = Copy();
        AssertPhase(tl, 0.00, "peek", 0.0);
        AssertPhase(tl, 0.20, "peek", 0.5);
        AssertPhase(tl, 0.40, "detach", 0.0);
        AssertPhase(tl, 0.55, "detach", 0.5);
        AssertPhase(tl, 0.70, "travel", 0.0);
        AssertPhase(tl, 0.85, "travel", 0.5);
    }

    [Fact]
    public void PhaseAt_AtBoundary_StartsTheNextPhaseAtZero()
    {
        // No "click": the frame exactly on the boundary must not be the previous phase at 1.0.
        var tl = Copy();
        AssertPhase(tl, 0.40, "detach", 0.0);
        AssertPhase(tl, 0.70, "travel", 0.0);
    }

    [Fact]
    public void PhaseAt_ProgressIsContinuousAcrossTheWholeTimeline()
    {
        var tl = Copy();
        for (var i = 0; i <= 1000; i++)
        {
            var (_, progress) = tl.PhaseAt(i / 1000.0);
            Assert.InRange(progress, 0.0, 1.0);
        }
    }

    [Fact]
    public void PhaseAt_AtOne_IsTheLastPhaseFullyComplete()
    {
        var tl = Copy();
        AssertPhase(tl, 1.0, "travel", 1.0);
    }

    [Fact]
    public void PhaseAt_ClampsOutOfRangeTime()
    {
        var tl = Copy();
        Assert.Equal(tl.PhaseAt(0.0), tl.PhaseAt(-4.0));
        Assert.Equal(tl.PhaseAt(1.0), tl.PhaseAt(9.0));
    }

    [Fact]
    public void PhaseAt_EveryPosition_ResolvesToExactlyOnePhase()
    {
        // The lookup has to be exhaustive: a t that matched nothing would be a silent hole.
        var tl = Copy();
        for (var i = 0; i <= 1000; i++)
        {
            var t = i / 1000.0;
            var (name, progress) = tl.PhaseAt(t);
            var phase = tl.Phases.Single(p => p.Name == name);
            Assert.True(t >= phase.From && t <= phase.To, $"t={t} not inside '{name}'");
            var expected = Math.Clamp((t - phase.From) / phase.Span, 0.0, 1.0);
            Assert.Equal(expected, progress, 9);
        }
    }

    [Fact]
    public void Reverse_KeepsTheScheduleAndInvertsProgress()
    {
        var fwd = Copy();
        var rev = fwd.Reverse();
        Assert.True(rev.IsReversed);
        Assert.False(fwd.IsReversed);
        Assert.Equal(
            fwd.Phases.Select(p => p.Name),
            rev.Phases.Select(p => p.Name));

        // The interruption contract: position t of a reversed run is exactly position 1-t of the
        // forward run — same phase, same progress. That is what replaces the separate settle/apply
        // paths (spec: Reverse() instead of SettleBlob / ApplyBlobRest).
        for (var i = 0; i <= 100; i++)
        {
            var t = i / 100.0;
            var a = fwd.PhaseAt(1.0 - t);
            var b = rev.PhaseAt(t);
            Assert.Equal(a.Name, b.Name);
            Assert.Equal(a.Progress, b.Progress, 9);
        }
    }

    [Fact]
    public void Reverse_IsReversible()
    {
        var tl = Copy().Reverse().Reverse();
        Assert.False(tl.IsReversed);
        AssertPhase(tl, 0.55, "detach", 0.5);
    }

    [Fact]
    public void SinglePhaseTimeline_Works()
    {
        var tl = new AnimTimeline(new[] { ("only", 0.0, 1.0) });
        AssertPhase(tl, 0.25, "only", 0.25);
        AssertPhase(tl, 1.0, "only", 1.0);
    }

    [Fact]
    public void GapBetweenPhases_IsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => new AnimTimeline(new[]
        {
            ("peek", 0.0, 0.40),
            ("detach", 0.50, 1.0),
        }));
        Assert.Contains("gaps", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OverlapBetweenPhases_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new AnimTimeline(new[]
        {
            ("peek", 0.0, 0.50),
            ("detach", 0.40, 1.0),
        }));
    }

    [Fact]
    public void DoesNotStartAtZero_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new AnimTimeline(new[]
        {
            ("peek", 0.10, 0.50),
            ("detach", 0.50, 1.0),
        }));
    }

    [Fact]
    public void DoesNotEndAtOne_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new AnimTimeline(new[]
        {
            ("peek", 0.0, 0.40),
            ("detach", 0.40, 0.90),
        }));
    }

    [Fact]
    public void OutOfUnitRange_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new AnimTimeline(new[] { ("peek", 0.0, 1.2) }));
        Assert.Throws<ArgumentException>(() => new AnimTimeline(new[] { ("peek", -0.2, 1.0) }));
    }

    [Fact]
    public void ZeroLengthPhase_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new AnimTimeline(new[]
        {
            ("peek", 0.0, 0.0),
            ("detach", 0.0, 1.0),
        }));
    }

    [Fact]
    public void EmptySchedule_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new AnimTimeline(Array.Empty<(string, double, double)>()));
    }

    [Fact]
    public void BlankPhaseName_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new AnimTimeline(new[] { ("  ", 0.0, 1.0) }));
    }

    [Fact]
    public void NullSchedule_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new AnimTimeline(null!));
    }

    [Fact]
    public void ProgressOf_ConvertsElapsedMilliseconds()
    {
        // Real milliseconds still come from AnimationTiming; the timeline itself is unitless.
        Assert.Equal(0.0, AnimTimeline.ProgressOf(0, 420), 12);
        Assert.Equal(0.5, AnimTimeline.ProgressOf(210, 420), 12);
        Assert.Equal(1.0, AnimTimeline.ProgressOf(420, 420), 12);
    }

    [Fact]
    public void ProgressOf_ClampsAndTreatsZeroDurationAsEndState()
    {
        Assert.Equal(0.0, AnimTimeline.ProgressOf(-50, 420), 12);
        Assert.Equal(1.0, AnimTimeline.ProgressOf(9000, 420), 12);
        // Reduced motion / AnimationSpeed.Off scale to 0 — the end state, not a division by zero.
        Assert.Equal(1.0, AnimTimeline.ProgressOf(0, OverlayTokens.ReducedMotionMs), 12);
    }

    [Fact]
    public void ElapsedMsOf_IsTheInverseOfProgressOf()
    {
        foreach (var duration in new[] { 1, 210, 420, 1000 })
        {
            for (var i = 0; i <= 20; i++)
            {
                var p = i / 20.0;
                Assert.Equal(p, AnimTimeline.ProgressOf(AnimTimeline.ElapsedMsOf(p, duration), duration), 9);
            }
        }

        Assert.Equal(0.0, AnimTimeline.ElapsedMsOf(0.5, 0), 12);
    }
}
