using System;
using System.Linq;
using Xunit;

namespace NotifyIsland.Tests;

public class AdaptiveStatsLayoutTests
{
    [Fact]
    public void UnderMin_HidesRow()
    {
        Assert.Equal(0, StatsLayout.VisibleMetricCount(OverlayTokens.StatsMinPillW - 1));
    }

    [Fact]
    public void AtMin_ShowsCpuOnly()
    {
        Assert.Equal(1, StatsLayout.VisibleMetricCount(OverlayTokens.StatsMinPillW));
    }

    [Fact]
    public void AtTwoMetricThreshold_ShowsCpuRam()
    {
        Assert.Equal(2, StatsLayout.VisibleMetricCount(OverlayTokens.StatsShowTwoMetricsW));
    }

    [Fact]
    public void AtThreeMetricThreshold_ShowsCpuRamBattery()
    {
        Assert.Equal(3, StatsLayout.VisibleMetricCount(OverlayTokens.StatsShowThreeMetricsW));
    }

    [Fact]
    public void AtAllMetricThreshold_ShowsAll()
    {
        Assert.Equal(4, StatsLayout.VisibleMetricCount(OverlayTokens.StatsShowAllMetricsW));
    }

    [Fact]
    public void NeverDropsCpu()
    {
        // SlotOrder[0] is CPU and is never dropped; the rest drop from the right.
        Assert.Equal(StatsMetricSlot.Cpu, StatsLayout.SlotOrder[0]);
        Assert.Equal(4, StatsLayout.SlotOrder.Count);
    }

    [Fact]
    public void StatsPillWidth_ClampsToBounds()
    {
        Assert.Equal(OverlayTokens.StatsMinPillW, StatsLayout.StatsPillWidth(0));
        Assert.Equal(OverlayTokens.ExpandedMaxW, StatsLayout.StatsPillWidth(99));
    }

    [Fact]
    public void StatsPillWidth_GrowsWithCount()
    {
        var widths = Enumerable.Range(0, 5).Select(StatsLayout.StatsPillWidth).ToArray();
        for (var i = 1; i < widths.Length; i++)
        {
            Assert.True(widths[i] >= widths[i - 1], $"count {i} narrower than {i - 1}");
        }
    }

    [Fact]
    public void SystemStats_AutoCollapseFlagRespected()
    {
        var m = new OverlayMachine();
        var snap = new SystemSnapshot { CpuPercent = 20, RamTotalBytes = 1024, CapturedAt = DateTimeOffset.UtcNow };

        // AutoCollapse on > collapses back to Idle after the token interval.
        m.Dispatch(OverlayCommand.SetSystemStats, new OverlayPayload
        {
            SystemStats = snap,
            AutoCollapse = true
        });
        Assert.Equal(OverlayKind.SystemStats, m.Snapshot().Kind);
        m.Tick(OverlayTokens.StatsAutoCollapseMs + 1);
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);

        // AutoCollapse off > stays expanded.
        m.Dispatch(OverlayCommand.SetSystemStats, new OverlayPayload
        {
            SystemStats = snap,
            AutoCollapse = false
        });
        m.Tick(OverlayTokens.StatsAutoCollapseMs + 1);
        Assert.Equal(OverlayKind.SystemStats, m.Snapshot().Kind);
    }

    [Fact]
    public void SystemStats_SetCommand_CarriesSnapshotThroughToSnapshot()
    {
        var m = new OverlayMachine();
        var snap = new SystemSnapshot { CpuPercent = 42.5, RamTotalBytes = 4096, CapturedAt = DateTimeOffset.UtcNow };
        m.Dispatch(OverlayCommand.SetSystemStats, new OverlayPayload
        {
            SystemStats = snap,
            AutoCollapse = false
        });
        var outSnap = m.Snapshot();
        Assert.NotNull(outSnap.Payload.SystemStats);
        Assert.Equal(42.5, outSnap.Payload.SystemStats!.CpuPercent);
        Assert.Equal(4096L, outSnap.Payload.SystemStats.RamTotalBytes);
    }
}
