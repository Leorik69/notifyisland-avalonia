using Xunit;

namespace NotifyIsland.Tests;

public class AnimationTimingTests
{
    [Theory]
    [InlineData(AnimationSpeed.Off, 0.0)]
    [InlineData(AnimationSpeed.Slow, 1.6)]
    [InlineData(AnimationSpeed.Normal, 1.0)]
    [InlineData(AnimationSpeed.Fast, 0.55)]
    public void Multiplier_MatchesSpec(AnimationSpeed speed, double expected)
    {
        Assert.Equal(expected, AnimationTiming.Multiplier(speed));
    }

    [Fact]
    public void IsEnabled_FalseOnlyForOff()
    {
        Assert.False(AnimationTiming.IsEnabled(AnimationSpeed.Off));
        Assert.True(AnimationTiming.IsEnabled(AnimationSpeed.Slow));
        Assert.True(AnimationTiming.IsEnabled(AnimationSpeed.Normal));
        Assert.True(AnimationTiming.IsEnabled(AnimationSpeed.Fast));
    }

    [Fact]
    public void ScaleMs_Off_IsInstantOneMs()
    {
        Assert.Equal(1, AnimationTiming.ScaleMs(OverlayTokens.MorphMs, AnimationSpeed.Off));
        Assert.Equal(1, AnimationTiming.ScaleMs(OverlayTokens.IconCrossfadeMs, AnimationSpeed.Off));
        Assert.Equal(1, AnimationTiming.ScaleMs(AnimationTiming.PulsePeriodMs, AnimationSpeed.Off));
    }

    [Fact]
    public void ScaleMs_Normal_PreservesBase()
    {
        Assert.Equal(OverlayTokens.MorphMs, AnimationTiming.ScaleMs(OverlayTokens.MorphMs, AnimationSpeed.Normal));
        Assert.Equal(OverlayTokens.SwipeRubberMs, AnimationTiming.ScaleMs(OverlayTokens.SwipeRubberMs, AnimationSpeed.Normal));
        Assert.Equal(AnimationTiming.PulsePeriodMs, AnimationTiming.ScaleMs(AnimationTiming.PulsePeriodMs, AnimationSpeed.Normal));
    }

    [Fact]
    public void ScaleMs_Slow_IsAbout1_6x()
    {
        Assert.Equal((int)System.Math.Round(280 * 1.6), AnimationTiming.ScaleMs(280, AnimationSpeed.Slow));
        Assert.Equal((int)System.Math.Round(1600 * 1.6), AnimationTiming.ScaleMs(1600, AnimationSpeed.Slow));
    }

    [Fact]
    public void ScaleMs_Fast_IsAbout0_55x()
    {
        Assert.Equal((int)System.Math.Round(280 * 0.55), AnimationTiming.ScaleMs(280, AnimationSpeed.Fast));
        Assert.Equal((int)System.Math.Round(240 * 0.55), AnimationTiming.ScaleMs(240, AnimationSpeed.Fast));
    }

    [Fact]
    public void AppSettings_DefaultAnimationSpeed_IsNormal()
    {
        Assert.Equal(AnimationSpeed.Normal, new AppSettings().AnimationSpeed);
    }
}
