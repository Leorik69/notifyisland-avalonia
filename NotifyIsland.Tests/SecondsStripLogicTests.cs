using System;
using Xunit;

namespace NotifyIsland.Tests;

public class SecondsStripLogicTests
{
    [Fact]
    public void Progress01_ZeroAtSecond0()
    {
        var t = new DateTime(2026, 9, 24, 12, 0, 0, 0);
        Assert.Equal(0.0, SecondsStripLogic.Progress01(t), 3);
    }

    [Fact]
    public void Progress01_HalfAtSecond30()
    {
        var t = new DateTime(2026, 9, 24, 12, 0, 30, 0);
        Assert.Equal(0.5, SecondsStripLogic.Progress01(t), 3);
    }

    [Fact]
    public void LitCount_EmptyAtStart_FullNearEnd()
    {
        Assert.Equal(0, SecondsStripLogic.LitCount(0.0, 24));
        Assert.Equal(12, SecondsStripLogic.LitCount(0.5, 24));
        Assert.Equal(24, SecondsStripLogic.LitCount(0.999, 24));
        Assert.Equal(24, SecondsStripLogic.LitCount(1.0, 24));
    }

    [Fact]
    public void LitCount_ClampsBadInputs()
    {
        Assert.Equal(0, SecondsStripLogic.LitCount(double.NaN, 24));
        Assert.Equal(0, SecondsStripLogic.LitCount(0.5, 0));
        Assert.Equal(0, SecondsStripLogic.LitCount(-1, 10));
    }

    [Fact]
    public void SlotCountForWidth_Clamped()
    {
        Assert.Equal(SecondsStripLogic.MinSlots, SecondsStripLogic.SlotCountForWidth(20));
        Assert.InRange(SecondsStripLogic.SlotCountForWidth(170), SecondsStripLogic.MinSlots, SecondsStripLogic.MaxSlots);
        Assert.Equal(SecondsStripLogic.MaxSlots, SecondsStripLogic.SlotCountForWidth(9999));
    }
}
