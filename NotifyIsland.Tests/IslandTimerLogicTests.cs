using Xunit;

namespace NotifyIsland.Tests;

public class IslandTimerLogicTests
{
    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(5, "00:05")]
    [InlineData(65, "01:05")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3661, "1:01:01")]
    public void FormatRemaining_MmSsOrHms(double seconds, string expected) =>
        Assert.Equal(expected, IslandTimerLogic.FormatRemaining(seconds));

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 1)]
    [InlineData(25, 25)]
    [InlineData(200, 180)]
    public void ClampPresetMinutes(int input, int expected) =>
        Assert.Equal(expected, IslandTimerLogic.ClampPresetMinutes(input));

    [Fact]
    public void PresetToSeconds_UsesClampedMinutes()
    {
        Assert.Equal(60, IslandTimerLogic.PresetToSeconds(1));
        Assert.Equal(300, IslandTimerLogic.PresetToSeconds(5));
        Assert.Equal(180 * 60, IslandTimerLogic.PresetToSeconds(999));
    }

    [Fact]
    public void CountdownPayload_Defaults()
    {
        var p = IslandTimerLogic.CountdownPayload(90);
        Assert.Equal("Таймер", p.Title);
        Assert.Equal(90, p.RemainingSeconds);
        Assert.True(p.Playing);
        Assert.False(p.CountUp);
    }

    [Fact]
    public void StopwatchPayload_Defaults()
    {
        var p = IslandTimerLogic.StopwatchPayload();
        Assert.Equal("Секундомер", p.Title);
        Assert.Equal(0, p.RemainingSeconds);
        Assert.True(p.Playing);
        Assert.True(p.CountUp);
    }

    [Fact]
    public void TimerOwnsIsland_OnlyTimerUnlessUserMedia()
    {
        Assert.True(IslandTimerLogic.TimerOwnsIsland(OverlayKind.Timer, userOpenedMedia: false));
        Assert.False(IslandTimerLogic.TimerOwnsIsland(OverlayKind.Timer, userOpenedMedia: true));
        Assert.False(IslandTimerLogic.TimerOwnsIsland(OverlayKind.Media, userOpenedMedia: false));
        Assert.False(IslandTimerLogic.TimerOwnsIsland(OverlayKind.Idle, userOpenedMedia: false));
    }
}
