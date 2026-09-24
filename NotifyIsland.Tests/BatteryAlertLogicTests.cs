using Xunit;

namespace NotifyIsland.Tests;

public class BatteryAlertLogicTests
{
    [Theory]
    [InlineData(0, 5)]
    [InlineData(4, 5)]
    [InlineData(20, 20)]
    [InlineData(50, 50)]
    [InlineData(99, 50)]
    public void ClampLowPercent_Clamps(int input, int expected) =>
        Assert.Equal(expected, BatteryAlertLogic.ClampLowPercent(input));

    [Fact]
    public void ChargeConnect_FiresOnAcTransition()
    {
        Assert.True(BatteryAlertLogic.ShouldShowChargeConnect(wasOnAc: false, nowOnAc: true, nowCharging: true));
        Assert.True(BatteryAlertLogic.ShouldShowChargeConnect(wasOnAc: null, nowOnAc: true, nowCharging: true));
        Assert.False(BatteryAlertLogic.ShouldShowChargeConnect(wasOnAc: true, nowOnAc: true, nowCharging: true));
        Assert.False(BatteryAlertLogic.ShouldShowChargeConnect(wasOnAc: false, nowOnAc: true, nowCharging: false));
    }

    [Fact]
    public void ChargePercentBump_BucketsByStep()
    {
        Assert.True(BatteryAlertLogic.ShouldShowChargePercentBump(true, true, 64, 65)); // 12→13 bucket
        Assert.False(BatteryAlertLogic.ShouldShowChargePercentBump(true, true, 66, 69)); // same bucket
        Assert.False(BatteryAlertLogic.ShouldShowChargePercentBump(false, true, 60, 70));
    }

    [Fact]
    public void LowAlert_OnceUntilReset()
    {
        Assert.True(BatteryAlertLogic.ShouldShowLowAlert(true, 15, 20, alreadyFired: false));
        Assert.False(BatteryAlertLogic.ShouldShowLowAlert(true, 15, 20, alreadyFired: true));
        Assert.False(BatteryAlertLogic.ShouldShowLowAlert(false, 15, 20, alreadyFired: false));
        Assert.True(BatteryAlertLogic.ShouldResetLowFlag(onAc: true, percent: 10, threshold: 20));
        Assert.True(BatteryAlertLogic.ShouldResetLowFlag(onAc: false, percent: 26, threshold: 20));
        Assert.False(BatteryAlertLogic.ShouldResetLowFlag(onAc: false, percent: 22, threshold: 20));
    }

    [Fact]
    public void Payloads_HaveRussianTitles()
    {
        var charge = BatteryAlertLogic.ChargePayload(42);
        Assert.Equal("Зарядка", charge.Title);
        Assert.Equal("42%", charge.Subtitle);
        Assert.Equal(0.42, charge.Progress, 3);
        Assert.True(charge.Playing);

        var low = BatteryAlertLogic.LowBatteryPayload(12);
        Assert.Equal("Низкий заряд", low.Title);
        Assert.Contains("12%", low.Body);
        Assert.False(low.Playing);
    }
}
