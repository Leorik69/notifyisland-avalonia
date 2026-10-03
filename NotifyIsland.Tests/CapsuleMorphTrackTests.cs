using System;
using System.Linq;
using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// Pins the 1.12.4 morph migration. The brief for that stage is "visuals unchanged, curves named" —
/// so most of these tests are EQUALITY tests against the curves the morph used before
/// (<see cref="AnimationEasing"/>), and the ones that are not are the properties the scenario
/// actually bought: one declared schedule, and phases that cannot desync.
/// </summary>
public class CapsuleMorphTrackTests
{
    // -- The promise: same picture, named curves ----------------------------------

    [Fact]
    public void PlainTrack_SizeIsTheOldCubicOut()
    {
        for (var i = 0; i <= 200; i++)
        {
            var t = i / 200.0;
            Assert.Equal(AnimationEasing.CubicOut(t), CapsuleMorphTrack.Plain.SizeAt(t), 12);
        }
    }

    [Fact]
    public void PlainTrack_HasOnePhase()
    {
        // Hover peek / monitor expand / kind change: a soft size change is ONE event. Declaring
        // phases here would be two numbers to keep in sync for no gain — the same reasoning the
        // history panel already carries.
        Assert.Single(CapsuleMorphTrack.Plain.Steps);
        Assert.Equal("morph", CapsuleMorphTrack.Plain.Steps[0].Name);
    }

    [Theory]
    [InlineData(NotifyAppearStyle.SlideDown)]
    [InlineData(NotifyAppearStyle.FadeScale)]
    [InlineData(NotifyAppearStyle.Inflate)]
    public void SoftAppearStyles_AreTheSameSoftCurve(NotifyAppearStyle style)
    {
        var track = CapsuleMorphTrack.ForAppear(style);
        for (var i = 0; i <= 200; i++)
        {
            var t = i / 200.0;
            Assert.Equal(AnimationEasing.CubicOut(t), track.SizeAt(t), 12);
            Assert.Equal(AnimationEasing.CubicOut(t), track.AuxAt(t), 12);
        }
    }

    [Theory]
    [InlineData(NotifyDismissStyle.Collapse)]
    [InlineData(NotifyDismissStyle.SlideUp)]
    [InlineData(NotifyDismissStyle.FadeScaleOut)]
    public void SoftDismissStyles_AreTheSameSoftCurve(NotifyDismissStyle style)
    {
        var track = CapsuleMorphTrack.ForDismiss(style);
        for (var i = 0; i <= 200; i++)
        {
            var t = i / 200.0;
            Assert.Equal(AnimationEasing.CubicOut(t), track.SizeAt(t), 12);
            Assert.Equal(AnimationEasing.CubicOut(t), track.AuxAt(t), 12);
        }
    }

    [Fact]
    public void BounceTrack_IsTheOldSpring_BothChannels()
    {
        // The old AppearProgress returned SpringOut on BOTH channels; the track must too, or the
        // scale breathe and the width overshoot would no longer be one movement.
        for (var i = 0; i <= 200; i++)
        {
            var t = i / 200.0;
            var expected = AnimationEasing.SpringOut(t);
            Assert.Equal(expected, CapsuleMorphTrack.Bounce.SizeAt(t), 12);
            Assert.Equal(expected, CapsuleMorphTrack.Bounce.AuxAt(t), 12);
        }
    }

    [Fact]
    public void BounceTrack_HasNoBreakpoint()
    {
        // The spring's overshoot IS the effect. A second phase would restart the spring and read
        // as two bounces, so this is a one-phase scenario on purpose.
        Assert.Single(CapsuleMorphTrack.Bounce.Steps);
    }

    [Fact]
    public void PopTrack_AuxIsTheOldPopScale_AndSizeStaysCubicOut()
    {
        for (var i = 0; i <= 200; i++)
        {
            var t = i / 200.0;
            Assert.Equal(AnimationEasing.CubicOut(t), CapsuleMorphTrack.Pop.SizeAt(t), 12);
            // 8 digits, not 12: the two sides compose the punch from the phase boundary and so
            // carry a slightly different rounding of the same number. 1e-8 is far below a pixel.
            Assert.Equal(AnimationEasing.PopScale(t), CapsuleMorphTrack.Pop.AuxAt(t), 8);
        }
    }

    [Fact]
    public void PopTrack_PunchBoundaryMatchesPopScale()
    {
        // 0.28 exists in BOTH PopScale and the Pop track. Equal by construction here, pinned so a
        // later edit to one without the other fails the build's tests instead of the picture.
        var punch = CapsuleMorphTrack.Pop.Steps[0];
        Assert.Equal(0.28, punch.To, 12);
        Assert.Equal("punch", punch.Name);
        Assert.Equal(0.88, punch.AuxStart, 12);
        Assert.Equal(1.18, punch.AuxEnd, 12);
    }

    [Fact]
    public void GlitchTrack_BothChannelsAreTheOldGlitchStep()
    {
        for (var i = 0; i <= 400; i++)
        {
            var t = i / 400.0;
            var expected = AnimationEasing.GlitchStep(t);
            Assert.Equal(expected, CapsuleMorphTrack.Glitch.SizeAt(t), 9);
            Assert.Equal(expected, CapsuleMorphTrack.Glitch.AuxAt(t), 9);
        }
    }

    [Fact]
    public void GlitchTrack_IsTheOnlySteppedStyle_OnBothAxes()
    {
        // Every other style interpolates width with one global ease; only Glitch really does jump.
        // If a future style needs a stepped width, SizeEase is where that has to show up.
        Assert.Null(CapsuleMorphTrack.Glitch.SizeEase);
        Assert.NotNull(CapsuleMorphTrack.Pop.SizeEase);
        Assert.NotNull(CapsuleMorphTrack.Ragged.SizeEase);
        Assert.NotNull(CapsuleMorphTrack.Plain.SizeEase);
    }

    [Fact]
    public void GlitchTrack_KeepsTheHardCutBeforeTheLastPhase()
    {
        // 1.12.4: the last phase starts at 0, not at the previous phase's 0.48. That discontinuity
        // is the stutter; a track that "helpfully" continued from 0.48 would turn the glitch into
        // a slide, and GlitchStep equality above is what catches it.
        var cut = CapsuleMorphTrack.Glitch.Steps[^1];
        Assert.Equal("cut", cut.Name);
        Assert.Equal(0.0, cut.AuxStart, 12);
        Assert.Equal(0.48, CapsuleMorphTrack.Glitch.Steps[^2].AuxEnd, 12);
    }

    [Fact]
    public void RaggedTrack_AuxIsLinear_AndSizeStaysCubicOut()
    {
        for (var i = 0; i <= 200; i++)
        {
            var t = i / 200.0;
            // The old DismissProgress gave Ragged (CubicOut, t) — width eased, jitter linear.
            Assert.Equal(AnimationEasing.CubicOut(t), CapsuleMorphTrack.Ragged.SizeAt(t), 12);
            Assert.Equal(t, CapsuleMorphTrack.Ragged.AuxAt(t), 12);
        }
    }

    [Fact]
    public void RaggedEnvelope_IsNowANamedCurve()
    {
        // The window multiplies its per-tick jitter by this; it must stay the 1 → 0 decay line,
        // because "ragged" is the style losing its footing, not a wobble on a spring.
        for (var i = 0; i <= 100; i++)
        {
            var t = i / 100.0;
            Assert.Equal(1.0 - t, AnimEase.Ease("ragged", t), 12);
        }
        Assert.True(AnimEase.Has("ragged"));
        Assert.True(AnimEase.Has("spring.out"));
        Assert.Equal(AnimationEasing.SpringOut(0.37), AnimEase.Ease("spring.out", 0.37), 12);
    }

    // -- What the schedule actually buys ----------------------------------------

    [Fact]
    public void EveryTrack_DeclaresPhasesThatTileZeroToOne()
    {
        var tracks = new[]
        {
            CapsuleMorphTrack.Plain,
            CapsuleMorphTrack.SoftAppear,
            CapsuleMorphTrack.SoftDismiss,
            CapsuleMorphTrack.Bounce,
            CapsuleMorphTrack.Pop,
            CapsuleMorphTrack.Ragged,
            CapsuleMorphTrack.Glitch,
        };
        foreach (var track in tracks)
        {
            var phases = track.Timeline.Phases;
            Assert.NotEmpty(phases);
            Assert.Equal(0.0, phases[0].From, 12);
            for (var i = 1; i < phases.Count; i++)
                Assert.Equal(phases[i - 1].To, phases[i].From, 12);
            Assert.Equal(1.0, phases[^1].To, 12);
        }
    }

    [Fact]
    public void EveryTrack_PhaseNamesAreMeaningfulAndUnique()
    {
        // Names go in the log line, so they have to say what the phase IS. A duplicate name would
        // make the log lie about which phase is playing.
        var tracks = new[]
        {
            CapsuleMorphTrack.Plain, CapsuleMorphTrack.SoftAppear, CapsuleMorphTrack.SoftDismiss,
            CapsuleMorphTrack.Bounce, CapsuleMorphTrack.Pop, CapsuleMorphTrack.Ragged,
            CapsuleMorphTrack.Glitch,
        };
        foreach (var track in tracks)
        {
            var names = track.Steps.Select(s => s.Name).ToList();
            Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
            Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
            Assert.All(names, n => Assert.Equal(n.Trim(), n));
        }
    }

    [Fact]
    public void PhaseAt_MatchesTheTimeline()
    {
        Assert.Equal(("punch", 0.0), CapsuleMorphTrack.Pop.PhaseAt(0.0));
        Assert.Equal(("settle", 0.0), CapsuleMorphTrack.Pop.PhaseAt(0.28));
        Assert.Equal(("settle", 0.5), CapsuleMorphTrack.Pop.PhaseAt(0.64));
        // "hold" spans 0 → 0.15, so 0.05 is a third of the way through it — the phase's own
        // progress, not the morph's. Compared with a tolerance: it comes out of a division.
        var (holdName, holdProgress) = CapsuleMorphTrack.Glitch.PhaseAt(0.05);
        Assert.Equal("hold", holdName);
        Assert.Equal(1.0 / 3.0, holdProgress, 9);
        Assert.Equal(("cut", 1.0), CapsuleMorphTrack.Glitch.PhaseAt(1.0));
    }

    [Fact]
    public void GlitchStutterSteps_AlternatePerCell_AndWrap()
    {
        // The offsets alternate on (int)(t*12)%2, so the even/odd pattern per cell must be
        // unchanged, and t = 1 must land inside the last cell rather than throwing.
        var track = CapsuleMorphTrack.Glitch;
        for (var i = 0; i < CapsuleMorphTrack.GlitchStutterSteps; i++)
        {
            var t = (i + 0.5) / CapsuleMorphTrack.GlitchStutterSteps;
            Assert.Equal(i, track.StutterStep(t));
            Assert.Equal(i % 2 == 0, track.StutterStep(t) % 2 == 0);
        }
        Assert.InRange(track.StutterStep(1.0), 0, CapsuleMorphTrack.GlitchStutterSteps - 1);
        Assert.Equal(0, track.StutterStep(-5.0));
    }

    [Fact]
    public void Compose_ClampsOutsideZeroToOne()
    {
        foreach (var track in new[] { CapsuleMorphTrack.Plain, CapsuleMorphTrack.Glitch })
        {
            Assert.Equal(track.SizeAt(0.0), track.SizeAt(-3.0), 12);
            Assert.Equal(track.SizeAt(1.0), track.SizeAt(9.0), 12);
            Assert.Equal(track.AuxAt(0.0), track.AuxAt(-3.0), 12);
            Assert.Equal(track.AuxAt(1.0), track.AuxAt(9.0), 12);
        }
    }

    [Fact]
    public void EveryTrack_StyleSelectionCoversEveryEnumMember()
    {
        // An enum member with no track case would silently fall into the soft default, which is
        // exactly the "added a style, forgot the picture" bug the migration exists to prevent.
        foreach (var style in Enum.GetValues<NotifyAppearStyle>())
            Assert.NotNull(CapsuleMorphTrack.ForAppear(style));
        foreach (var style in Enum.GetValues<NotifyDismissStyle>())
            Assert.NotNull(CapsuleMorphTrack.ForDismiss(style));
    }

    [Fact]
    public void Describe_NamesThePhasesAndTheSizeEase()
    {
        // The log line is the only place a user can see which scenario ran; it must be enough.
        Assert.Contains("punch", CapsuleMorphTrack.Pop.Describe());
        Assert.Contains("settle", CapsuleMorphTrack.Pop.Describe());
        Assert.Contains("size=power2.out", CapsuleMorphTrack.Pop.Describe());
        Assert.Contains("cut", CapsuleMorphTrack.Glitch.Describe());
    }

    [Fact]
    public void SizeChannelEndsAtItsTarget_ForEveryTrack()
    {
        // Whatever the style, the capsule must reach its target width — a track whose size axis
        // stopped at 0.9 would leave the island permanently short with no error anywhere.
        foreach (var track in new[]
                 {
                     CapsuleMorphTrack.Plain, CapsuleMorphTrack.SoftAppear, CapsuleMorphTrack.SoftDismiss,
                     CapsuleMorphTrack.Pop, CapsuleMorphTrack.Ragged, CapsuleMorphTrack.Glitch,
                 })
        {
            Assert.Equal(1.0, track.SizeAt(1.0), 9);
        }

        // Bounce is the one style whose size curve is a spring, and a damped spring does not
        // mathematically reach 1.0 — it approaches it. The pre-migration code used the very same
        // SpringOut for the width, so the capsule has always landed ~0.08 % short and then been
        // settled exactly by SetSizeImmediate at t = 1. Pinned here so that if someone "fixes" the
        // spring later, this test says the landing changed.
        Assert.Equal(AnimationEasing.SpringOut(1.0), CapsuleMorphTrack.Bounce.SizeAt(1.0), 12);
        Assert.True(CapsuleMorphTrack.Bounce.SizeAt(1.0) > 0.999);
    }
}
