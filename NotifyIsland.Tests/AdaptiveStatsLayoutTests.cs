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
    public void SystemStats_DoesNotAutoCollapseOnWallClockTick()
    {
        // 1.12.1: the 30 s wall-clock auto-collapse is gone. Exit is pointer-leave driven
        // (HoverPinMachine grace) or an explicit Collapse — Tick must never collapse SystemStats.
        var m = new OverlayMachine();
        var snap = new SystemSnapshot { CpuPercent = 20, RamTotalBytes = 1024, CapturedAt = DateTimeOffset.UtcNow };

        m.Dispatch(OverlayCommand.SetSystemStats, new OverlayPayload { SystemStats = snap });
        Assert.Equal(OverlayKind.SystemStats, m.Snapshot().Kind);
        m.Tick(300_000);
        Assert.Equal(OverlayKind.SystemStats, m.Snapshot().Kind);

        // Only an explicit Collapse leaves SystemStats.
        m.Dispatch(OverlayCommand.Collapse);
        Assert.Equal(OverlayKind.Collapsed, m.Snapshot().Kind);
    }

    [Fact]
    public void HoverPin_HoverDelay_OpensAndPointerLeave_ClosesSystemStats()
    {
        // The hover-pin machine is the entry/exit gate for the SystemStats surface:
        // IsContentExpanded is the signal OverlayWindow maps to SetSystemStats / Collapse.
        var m = new HoverPinMachine();
        m.Configure(hoverEnabled: true, pinEnabled: true,
            OverlayTokens.HoverExpandDelayMs, OverlayTokens.HoverCollapseGraceMs);
        var machine = new OverlayMachine();

        m.PointerEnter();
        Assert.False(m.IsContentExpanded);
        m.Tick(OverlayTokens.HoverExpandDelayMs - 1);
        Assert.False(m.IsContentExpanded);
        m.Tick(2);
        Assert.True(m.IsContentExpanded);

        if (m.IsContentExpanded)
            machine.Dispatch(OverlayCommand.SetSystemStats, new OverlayPayload { SystemStats = SystemSnapshot.Empty });
        Assert.Equal(OverlayKind.SystemStats, machine.Snapshot().Kind);

        m.PointerLeave();
        Assert.True(m.IsContentExpanded);          // still inside the grace window
        m.Tick(OverlayTokens.HoverCollapseGraceMs);
        Assert.False(m.IsContentExpanded);
        if (!m.IsContentExpanded)
            machine.Dispatch(OverlayCommand.Collapse);
        Assert.NotEqual(OverlayKind.SystemStats, machine.Snapshot().Kind);
    }

    [Fact]
    public void HoverPin_PinnedSurvivesPointerLeave()
    {
        // A click while the hover peek is open must pin, not be swallowed: pinned keeps
        // IsContentExpanded true across pointer leave.
        var m = new HoverPinMachine();
        m.PointerEnter();
        m.Tick(OverlayTokens.HoverExpandDelayMs + 1);
        Assert.True(m.IsContentExpanded);

        m.ClickTogglePin();
        Assert.True(m.IsPinned);
        m.PointerLeave();
        m.Tick(OverlayTokens.HoverCollapseGraceMs * 4);
        Assert.True(m.IsContentExpanded);
    }

    [Fact]
    public void HoverPin_HoverExpandDisabled_NeverExpands()
    {
        var m = new HoverPinMachine();
        m.Configure(hoverEnabled: false, pinEnabled: true,
            OverlayTokens.HoverExpandDelayMs, OverlayTokens.HoverCollapseGraceMs);
        m.PointerEnter();
        m.Tick(OverlayTokens.HoverExpandDelayMs * 10);
        Assert.False(m.IsContentExpanded);
    }

    [Fact]
    public void SystemStats_SetCommand_CarriesSnapshotThroughToSnapshot()
    {
        var m = new OverlayMachine();
        var snap = new SystemSnapshot { CpuPercent = 42.5, RamTotalBytes = 4096, CapturedAt = DateTimeOffset.UtcNow };
        m.Dispatch(OverlayCommand.SetSystemStats, new OverlayPayload
        {
            SystemStats = snap
        });
        var outSnap = m.Snapshot();
        Assert.NotNull(outSnap.Payload.SystemStats);
        Assert.Equal(42.5, outSnap.Payload.SystemStats!.CpuPercent);
        Assert.Equal(4096L, outSnap.Payload.SystemStats.RamTotalBytes);
    }

    [Fact]
    public void StatsWidth_FixedAndIndependentOfMetricCount()
    {
        // 1.12.1: the expanded SystemStats pill is a fixed 300 DIP block; metric count no longer inflates it.
        foreach (var count in new[] { 0, 1, 2, 3, 4, 99 })
        {
            Assert.Equal(OverlayTokens.StatsExpandedW, OverlayMachine.WidthFor(OverlayKind.SystemStats, statsMetricCount: count));
        }

        var m = new OverlayMachine { StatsMetricCount = 4 };
        m.Dispatch(OverlayCommand.SetSystemStats, new OverlayPayload
        {
            SystemStats = new SystemSnapshot { CpuPercent = 10, RamTotalBytes = 1024 }
        });
        var snap = m.Snapshot();
        Assert.Equal(OverlayKind.SystemStats, snap.Kind);
        Assert.Equal(OverlayTokens.StatsExpandedW, snap.Width);
        Assert.Equal(OverlayTokens.StatsExpandedH, snap.Height);
    }

    [Fact]
    public void StatsHeight_FitsSystemStatsPanelContentBudget()
    {
        // SystemStatsPanel (OverlayWindow.axaml): 4 rows, Spacing 4, FontSize 12 value lines,
        // FontSize 10 full-date line with Margin 0,2,0,0, panel Margin 12,6,12,6.
        // Line box ≈ FontSize × 1.33. 72 DIP clipped the first and last rows; the token now
        // carries the full content budget.
        const double rowLineHeight = 12.0 * 1.33;      // value line dominates the 11 DIP label
        const double dateLineHeight = 10.0 * 1.33;
        const double required = 4 * rowLineHeight      // 4 metric rows
                             + 4 * 4                  // Spacing 4 between the 5 children
                             + dateLineHeight + 2     // StatsFullDate + its top Margin
                             + 6 + 6;                 // panel Margin top/bottom
        Assert.True(
            OverlayTokens.StatsExpandedH >= required,
            $"StatsExpandedH = {OverlayTokens.StatsExpandedH} but the panel needs ≈{required:F1} DIP.");
    }
}
