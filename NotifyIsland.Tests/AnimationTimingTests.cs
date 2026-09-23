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
    public void AppSettings_DefaultAnimationSpeed_IsSlow()
    {
        Assert.Equal(AnimationSpeed.Slow, new AppSettings().AnimationSpeed);
    }

    [Fact]
    public void MorphMs_IsRaisedForSofterFeel()
    {
        Assert.Equal(420, OverlayTokens.MorphMs);
    }

    [Theory]
    [InlineData(NotifyAppearStyle.Inflate)]
    [InlineData(NotifyAppearStyle.SlideDown)]
    [InlineData(NotifyAppearStyle.FadeScale)]
    [InlineData(NotifyAppearStyle.Bounce)]
    [InlineData(NotifyAppearStyle.Pop)]
    public void AppearStyle_RoundTrip(NotifyAppearStyle style)
    {
        var s = new AppSettings { AppearStyle = style };
        var back = AppSettings.FromJson(s.ToJson());
        Assert.NotNull(back);
        Assert.Equal(style, back!.AppearStyle);
    }

    [Theory]
    [InlineData(NotifyDismissStyle.Collapse)]
    [InlineData(NotifyDismissStyle.SlideUp)]
    [InlineData(NotifyDismissStyle.FadeScaleOut)]
    [InlineData(NotifyDismissStyle.Ragged)]
    [InlineData(NotifyDismissStyle.Glitch)]
    public void DismissStyle_RoundTrip(NotifyDismissStyle style)
    {
        var s = new AppSettings { DismissStyle = style };
        var back = AppSettings.FromJson(s.ToJson());
        Assert.NotNull(back);
        Assert.Equal(style, back!.DismissStyle);
    }

    [Fact]
    public void IconDip_ScalesWithFontSize()
    {
        Assert.Equal(12.0, OverlayTokens.IconDip(12));
        Assert.Equal(14.0, OverlayTokens.IconDip(14));
        Assert.Equal(12.9, OverlayTokens.IconDip(14, OverlayTokens.IconFontFactorKind)); // 14*0.92
        Assert.True(OverlayTokens.IconDip(18) >= OverlayTokens.IconDip(10));
    }

    [Fact]
    public void Easing_SoftNotLinear()
    {
        Assert.True(AnimationEasing.CubicOut(0.5) > 0.5);
        Assert.True(AnimationEasing.SpringOut(0.5) > 0.8);
        Assert.InRange(AnimationEasing.PopScale(0.2), 0.9, 1.25);
        Assert.True(AnimationEasing.GlitchStep(0.2) >= 0);
    }

    [Fact]
    public void Effective_GlobalOff_Wins()
    {
        Assert.Equal(AnimationSpeed.Off,
            AnimationTiming.Effective(AnimationSpeed.Off, AnimationSpeed.Fast));
    }

    [Fact]
    public void Effective_UsesPerAction_WhenGlobalOn()
    {
        Assert.Equal(AnimationSpeed.Fast,
            AnimationTiming.Effective(AnimationSpeed.Normal, AnimationSpeed.Fast));
        Assert.Equal(AnimationSpeed.Slow,
            AnimationTiming.Effective(AnimationSpeed.Fast, AnimationSpeed.Slow));
    }

    [Fact]
    public void ScaleActionMs_RespectsPerAction()
    {
        Assert.Equal(1, AnimationTiming.ScaleActionMs(280, AnimationSpeed.Off, AnimationSpeed.Normal));
        Assert.Equal((int)System.Math.Round(280 * 0.55),
            AnimationTiming.ScaleActionMs(280, AnimationSpeed.Normal, AnimationSpeed.Fast));
    }
}
