namespace NotifyIsland;

/// <summary>Which metric a collapsed-pill slot represents. Declaration order is the drop order (last drops first).</summary>
public enum StatsMetricSlot
{
    Cpu,
    Ram,
    Battery,
    Net
}

/// <summary>
/// Pure layout math for the collapsed pill's metric row. Takes the available DIP width of
/// the monitor the pill sits on and answers two questions: how many slots fit, and how wide
/// should the pill be. No platform dependencies, so it is directly unit-testable.
/// </summary>
public static class StatsLayout
{
    /// <summary>Slot order, first-dropped last. CPU is index 0 and is never dropped.</summary>
    public static IReadOnlyList<StatsMetricSlot> SlotOrder { get; } =
        new[] { StatsMetricSlot.Cpu, StatsMetricSlot.Ram, StatsMetricSlot.Battery, StatsMetricSlot.Net };

    /// <summary>How many metric slots fit in the available DIP width. 0 means hide the row entirely.</summary>
    public static int VisibleMetricCount(double availableWidth)
    {
        if (availableWidth < OverlayTokens.StatsMinPillW)           return 0;
        if (availableWidth < OverlayTokens.StatsShowTwoMetricsW)    return 1;
        if (availableWidth < OverlayTokens.StatsShowThreeMetricsW)  return 2;
        if (availableWidth < OverlayTokens.StatsShowAllMetricsW)    return 3;
        return 4;
    }

    /// <summary>Pill width for a given visible metric count, clamped to the pill's width bounds.</summary>
    public static double StatsPillWidth(int visibleMetricCount)
    {
        var w = OverlayTokens.CollapsedW + visibleMetricCount * OverlayTokens.StatsMetricSlotW;
        return Math.Clamp(w, OverlayTokens.StatsMinPillW, OverlayTokens.ExpandedMaxW);
    }
}
