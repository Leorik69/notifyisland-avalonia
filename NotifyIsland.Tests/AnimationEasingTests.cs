using System;
using System.Linq;
using Xunit;

namespace NotifyIsland.Tests;

public class AnimationEasingTests
{
    [Fact]
    public void ClickPop_StartsAtOne()
    {
        Assert.Equal(1.0, AnimationEasing.ClickPop(0.0), 3);
    }

    [Fact]
    public void ClickPop_EndsAtOne()
    {
        Assert.Equal(1.0, AnimationEasing.ClickPop(1.0), 3);
    }

    [Fact]
    public void ClickPop_PeaksAtClickPopPeak()
    {
        var peak = Enumerable.Range(0, 101).Select(i => AnimationEasing.ClickPop(i / 100.0)).Max();
        Assert.Equal(OverlayTokens.ClickPopPeak, peak, 2);
    }

    [Fact]
    public void ClickPop_DoesNotDipBelowOne()
    {
        for (var i = 0; i <= 100; i++)
        {
            Assert.True(AnimationEasing.ClickPop(i / 100.0) >= 1.0, $"t={i / 100.0} dipped below 1.0");
        }
    }
}
