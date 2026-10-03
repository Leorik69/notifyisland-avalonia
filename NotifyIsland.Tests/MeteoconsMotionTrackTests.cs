using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// Pins the Meteocons mirror motion's frame maths. The app-layer tick (<c>MeteoconsMotion</c>)
/// needs an Avalonia window and cannot be built here; everything it draws comes from
/// <see cref="MeteoconsMotionTrack"/>, which is why the durations, amplitudes and the
/// eased-then-triangled shape are pinned here instead.
/// </summary>
public class MeteoconsMotionTrackTests
{
    [Theory]
    [InlineData("weather-clear", MeteoconsMotionKind.Spin)]
    [InlineData("weather-partly", MeteoconsMotionKind.Spin)]
    [InlineData("weather-cloud", MeteoconsMotionKind.Bob)]
    [InlineData("weather-drizzle", MeteoconsMotionKind.Bob)]
    [InlineData("weather-rain", MeteoconsMotionKind.Bob)]
    [InlineData("weather-snow", MeteoconsMotionKind.Bob)]
    [InlineData("weather-sleet", MeteoconsMotionKind.Bob)]
    [InlineData("weather-storm", MeteoconsMotionKind.Pulse)]
    [InlineData("weather-fog", MeteoconsMotionKind.Pulse)]
    public void KindFor_MapsEveryMeteoconsKey(string key, MeteoconsMotionKind expected)
    {
        Assert.Equal(expected, MeteoconsMotionTrack.KindFor(key));
    }

    [Theory]
    [InlineData("  WEATHER-CLEAR  ", MeteoconsMotionKind.Spin)]
    [InlineData("Weather-Cloud", MeteoconsMotionKind.Bob)]
    [InlineData("Weather-Storm", MeteoconsMotionKind.Pulse)]
    public void KindFor_IgnoresCaseAndSurroundingWhitespace(string key, MeteoconsMotionKind expected)
    {
        // The key arrives from a settings string and a slot id, so it is not a compile-time
        // literal; the old animation code trimmed and lowercased it before switching too.
        Assert.Equal(expected, MeteoconsMotionTrack.KindFor(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notify")]
    [InlineData("weather-moon")]
    public void KindFor_UnknownKey_HasNoMotion(string? key)
    {
        // A host with no motion still has to be returned, so None is a real answer, not a throw.
        Assert.Equal(MeteoconsMotionKind.None, MeteoconsMotionTrack.KindFor(key));
        Assert.Equal(0, MeteoconsMotionTrack.PeriodMsFor(key));
    }

    [Fact]
    public void Periods_AreTheDocumentedOnes()
    {
        // docs/ICON_PACKS.md lists these; the two spins differ because a clear sun can turn at a
        // constant rate and a partly-cloudy one has to stay readable behind a cloud.
        Assert.Equal(6000, MeteoconsMotionTrack.PeriodMsFor("weather-clear"));
        Assert.Equal(10000, MeteoconsMotionTrack.PeriodMsFor("weather-partly"));
        Assert.Equal(3000, MeteoconsMotionTrack.PeriodMsFor("weather-cloud"));
        Assert.Equal(3000, MeteoconsMotionTrack.PeriodMsFor("weather-sleet"));
        Assert.Equal(1200, MeteoconsMotionTrack.PeriodMsFor("weather-storm"));
        Assert.Equal(2400, MeteoconsMotionTrack.PeriodMsFor("weather-fog"));
    }

    [Fact]
    public void Spin_IsLinearZeroToThreeSixty()
    {
        var period = MeteoconsMotionTrack.PeriodMsFor("weather-clear");
        Assert.Equal(0.0, Angle("weather-clear", 0), 6);
        Assert.Equal(180.0, Angle("weather-clear", period / 2.0), 6);
        // Just before the end of the turn, so the wrap is not what is being measured here.
        Assert.Equal(360.0 - 360.0 / period, Angle("weather-clear", period - 1.0), 3);
    }

    [Fact]
    public void Spin_WrapsBackToZero_WhichIsTheSameAngleAsThreeSixty()
    {
        // The endless cycle is a modulo now (no Avalonia IterationCount.Infinite), so phase 1.0
        // IS phase 0.0. For a full turn that is free: 0° and 360° are the same picture, which is
        // why a spinning sun never shows a seam.
        var period = MeteoconsMotionTrack.PeriodMsFor("weather-clear");
        Assert.Equal(0.0, Angle("weather-clear", period), 6);
        Assert.Equal(0.0, Angle("weather-clear", period * 40.0), 6);
    }

    [Fact]
    public void Spin_IsLinear_NotEased()
    {
        // A spin that eased in and out stalls at the top and bottom of every turn; the upstream
        // SMIL turns at a constant rate and the quarter turn must land on the quarter.
        var quarter = Angle("weather-clear", 1500);
        Assert.Equal(90.0, quarter, 6);
    }

    [Fact]
    public void Spin_DoesNotTouchTheOtherChannels()
    {
        var f = MeteoconsMotionTrack.FrameAt(
            MeteoconsMotionKind.Spin, "weather-clear", 6000, 1234);
        Assert.Equal(0.0, f.OffsetY, 9);
        Assert.Equal(1.0, f.Opacity, 9);
    }

    [Fact]
    public void Bob_StartsAtRestAndPeaksUpAtTheMidpoint()
    {
        var period = MeteoconsMotionTrack.PeriodMsFor("weather-cloud");
        Assert.Equal(0.0, Bob("weather-cloud", 0), 9);
        Assert.Equal(0.0, Bob("weather-cloud", period), 9);
        // Negative: the clouds ride UP, which is the SMIL's read and the token's sign.
        Assert.Equal(-2.5, Bob("weather-cloud", period / 2.0), 9);
    }

    [Fact]
    public void Bob_NeverRidesBelowItsRestPosition()
    {
        // An asymmetric bob (down on one half, up on the other) is the classic tell of a
        // mistyped amplitude sign; the mirror is symmetric around the rest pose.
        for (var i = 0; i <= 300; i++)
            Assert.True(Bob("weather-cloud", i * 10.0) <= 1e-9,
                $"bob dipped positive at {i * 10} ms");
    }

    [Fact]
    public void Pulse_TroughsAtTheMidpoint_AtTheDocumentedDepth()
    {
        Assert.Equal(1.0, Opacity("weather-storm", 0), 9);
        Assert.Equal(1.0, Opacity("weather-storm", 1200), 9);
        Assert.Equal(0.55, Opacity("weather-storm", 600), 9);
        // Fog is a shallower, slower flicker than a storm flash.
        Assert.Equal(0.72, Opacity("weather-fog", 1200), 9);
    }

    [Fact]
    public void Pulse_NeverGoesTransparent()
    {
        for (var i = 0; i <= 240; i++)
        {
            var o = Opacity("weather-storm", i * 5.0);
            Assert.InRange(o, 0.55, 1.0);
        }
    }

    [Fact]
    public void EveryKind_IsEndless_AndWrapsIdentically()
    {
        // No Avalonia.Animation.Infinite any more, so "endless" is now the wrapping itself: any
        // number of cycles later the frame must be the one a single cycle would have given.
        foreach (var key in new[] { "weather-clear", "weather-cloud", "weather-storm" })
        {
            var kind = MeteoconsMotionTrack.KindFor(key);
            var period = MeteoconsMotionTrack.PeriodMsFor(key);
            for (var i = 0; i < 40; i++)
            {
                var at = 1000.0 + i * period * 3.5;
                var a = MeteoconsMotionTrack.FrameAt(kind, key, period, at);
                var b = MeteoconsMotionTrack.FrameAt(kind, key, period, at % period);
                Assert.Equal(a.Angle, b.Angle, 6);
                Assert.Equal(a.OffsetY, b.OffsetY, 6);
                Assert.Equal(a.Opacity, b.Opacity, 6);
            }
        }
    }

    [Fact]
    public void ElapsedIsModuloThePeriod_SoAFrozenClockCannotDrift()
    {
        var period = MeteoconsMotionTrack.PeriodMsFor("weather-clear");
        // Six hours of uptime: an accumulator would be far off its period here, the wrap is not.
        Assert.Equal(0.0, Angle("weather-clear", 6 * 60 * 60 * 1000.0), 6);
        Assert.Equal(180.0, Angle("weather-clear", 6 * 60 * 60 * 1000.0 + period / 2.0), 6);
    }

    [Fact]
    public void NegativeElapsed_ReadsAsJustBeforeZero()
    {
        // Stopwatch.Elapsed cannot go negative, but a host re-attached mid-cycle can be handed a
        // wrapped value; the old `elapsed % period` would return a negative angle and flip the sun.
        var f = MeteoconsMotionTrack.FrameAt(
            MeteoconsMotionKind.Spin, "weather-clear", 6000, -10);
        Assert.InRange(f.Angle, 0.0, 360.0);
    }

    [Fact]
    public void None_IsAlwaysTheRestPose()
    {
        var f = MeteoconsMotionTrack.FrameAt(
            MeteoconsMotionKind.None, "notify", 0, 4321);
        Assert.Equal(0.0, f.Angle, 9);
        Assert.Equal(0.0, f.OffsetY, 9);
        Assert.Equal(1.0, f.Opacity, 9);
    }

    [Fact]
    public void NonPositivePeriod_DoesNotDivideByZero()
    {
        // A key that mapped to a kind but had no period would turn every tick into NaN, and a
        // NaN angle silently blanks the icon instead of throwing.
        foreach (var period in new[] { 0, -1 })
        {
            var f = MeteoconsMotionTrack.FrameAt(
                MeteoconsMotionKind.Spin, "weather-clear", period, 500);
            Assert.Equal(0.0, f.Angle, 9);
            Assert.Equal(1.0, f.Opacity, 9);
        }
    }

    [Fact]
    public void Swing_IsEasedOverTheWholeCycle_NotPerHalf()
    {
        // The old animation put one SineEaseInOut on the animation plus keyframes at 0/0.5/1, so
        // the easing shaped the WHOLE cycle and the eased value then picked a point on the
        // triangle. Easing each half separately keeps the peak but softens the flanks and makes
        // the dip deeper — a visible change the brief forbids. At the quarter of the cycle the
        // two schemes differ; this pins the one that is actually shipped.
        var period = MeteoconsMotionTrack.PeriodMsFor("weather-cloud");
        var eased = AnimEase.Ease("sine.inOut", 0.25);
        var expected = OverlayTokens.MeteoconsBobDip * (2.0 * eased);
        Assert.Equal(expected, Bob("weather-cloud", period * 0.25), 9);
        // …and it is genuinely shallower than a per-half easing (the amplitude is negative, so
        // "shallower" is the larger value), which is what makes the pin have teeth.
        var perHalf = OverlayTokens.MeteoconsBobDip * AnimEase.Ease("sine.inOut", 0.5);
        Assert.True(Bob("weather-cloud", period * 0.25) > perHalf,
            "the eased-then-tripled shape should dip less deeply than a per-half easing");
    }

    private static MeteoconsFrame Frame(string key, double elapsedMs) =>
        MeteoconsMotionTrack.FrameAt(
            MeteoconsMotionTrack.KindFor(key), key, MeteoconsMotionTrack.PeriodMsFor(key), elapsedMs);

    private static double Angle(string key, double elapsedMs) => Frame(key, elapsedMs).Angle;
    private static double Bob(string key, double elapsedMs) => Frame(key, elapsedMs).OffsetY;
    private static double Opacity(string key, double elapsedMs) => Frame(key, elapsedMs).Opacity;
}
