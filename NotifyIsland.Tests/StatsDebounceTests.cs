using Xunit;

namespace NotifyIsland.Tests;

public class StatsDebounceTests
{
    [Fact]
    public void Percent_ReusesPreviousWhenDeltaBelowThreshold()
    {
        Assert.Equal(42.0, StatsDebounce.Percent(previous: 42.0, next: 42.4, thresholdPct: 0.5));
    }

    [Fact]
    public void Percent_UpdatesWhenDeltaAboveThreshold()
    {
        Assert.Equal(43.0, StatsDebounce.Percent(previous: 42.0, next: 43.0, thresholdPct: 0.5));
    }

    [Fact]
    public void Rate_ReusesWhenBelowFloor()
    {
        Assert.Equal(1000L, StatsDebounce.Rate(previous: 1000L, next: 1200L, thresholdBytesPerSec: 4096L));
    }

    [Fact]
    public void Rate_NeverNegative()
    {
        Assert.Equal(0L, StatsDebounce.Rate(previous: 0L, next: -50L, thresholdBytesPerSec: 4096L));
    }
}
