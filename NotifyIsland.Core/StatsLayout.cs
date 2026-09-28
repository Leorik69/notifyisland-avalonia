namespace NotifyIsland;

/// <summary>Which metric a collapsed-pill slot represents. Declaration order is the drop order (last drops first).</summary>
public enum StatsMetricSlot
{
    Cpu,
    Ram,
    Battery,
    Net
}

/// <summary>One row of the System Stats surface.</summary>
public enum StatsRow
{
    Cpu,
    Memory,
    Battery,
    Network,
    Date
}

/// <summary>Which rows the System Stats surface shows, and in what order.</summary>
public enum StatsPreset
{
    /// <summary>Кратко: только CPU и батарея.</summary>
    Brief,
    /// <summary>Полностью: все четыре метрики плюс дата.</summary>
    Full,
    /// <summary>Выключить поверхность монитора целиком.</summary>
    Off,
    /// <summary>Пользовательский набор — задаётся StatsRows вручную.</summary>
    Custom
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

    /// <summary>Rows shown by <see cref="StatsPreset.Full"/> — the default preset.</summary>
    public static IReadOnlyList<StatsRow> FullRows { get; } = new[]
    {
        StatsRow.Cpu, StatsRow.Memory, StatsRow.Battery, StatsRow.Network, StatsRow.Date
    };

    /// <summary>Rows shown by <see cref="StatsPreset.Brief"/>.</summary>
    public static IReadOnlyList<StatsRow> BriefRows { get; } = new[]
    {
        StatsRow.Cpu, StatsRow.Battery
    };

    /// <summary>Row count of the default (<see cref="StatsPreset.Full"/>) surface — the 108 DIP case.</summary>
    public const int DefaultRowCount = 5;

    /// <summary>
    /// Which rows to render, in which order, for a given preset.
    /// <see cref="StatsPreset.Custom"/> returns <paramref name="custom"/> filtered to known, unique
    /// entries in the user's order; an empty result falls back to <see cref="FullRows"/> so the
    /// panel can never end up zero-height. All other presets return their canonical set.
    /// </summary>
    public static IReadOnlyList<StatsRow> ResolveRows(StatsPreset preset, IReadOnlyList<StatsRow>? custom)
    {
        switch (preset)
        {
            case StatsPreset.Brief:  return BriefRows;
            case StatsPreset.Off:    return Array.Empty<StatsRow>();
            case StatsPreset.Custom: return FilterCustom(custom);
            default:                 return FullRows;
        }
    }

    /// <summary>Drop unknown and duplicate entries, keeping the user's order.</summary>
    public static IReadOnlyList<StatsRow> FilterCustom(IReadOnlyList<StatsRow>? custom)
    {
        if (custom is null || custom.Count == 0) return FullRows;
        var seen = new HashSet<StatsRow>();
        var kept = new List<StatsRow>(custom.Count);
        foreach (var row in custom)
        {
            if (Enum.IsDefined(typeof(StatsRow), row) && seen.Add(row))
                kept.Add(row);
        }
        return kept.Count == 0 ? FullRows : kept;
    }

    /// <summary>
    /// Fixed left-hand label of a label+value row, exactly as it was hard-coded in the panel
    /// before rows became dynamic. <see cref="StatsRow.Date"/> has no label — it renders as a
    /// single centred caption line (see <see cref="IsCaptionRow"/>) and returns "".
    /// </summary>
    public static string LabelFor(StatsRow row) => row switch
    {
        StatsRow.Cpu     => "CPU",
        StatsRow.Memory  => "Память",
        StatsRow.Battery => "Батарея",
        StatsRow.Network => "Сеть",
        _               => ""
    };

    /// <summary>
    /// Human name of a row for the Settings row editor, where there is no value column next to
    /// the label and <see cref="LabelFor"/> would render the дата row blank. The four metric rows
    /// keep their panel labels; <see cref="StatsRow.Date"/> gets the only name it ever has.
    /// </summary>
    public static string NameFor(StatsRow row) => row switch
    {
        StatsRow.Cpu     => "CPU",
        StatsRow.Memory  => "Память",
        StatsRow.Battery => "Батарея",
        StatsRow.Network => "Сеть",
        _               => "Дата и время"
    };

    /// <summary>
    /// True for rows styled as one centred caption line (FontSize 10, #8A8A92) instead of the
    /// label+value pair. Only <see cref="StatsRow.Date"/> qualifies.
    /// </summary>
    public static bool IsCaptionRow(StatsRow row) => row == StatsRow.Date;

    // -- Height budget (mirrors the comment on OverlayTokens.StatsExpandedH) --------------
    // Row line box ≈ FontSize × 1.33: the FontSize 12 value line (label 11) rounds up to 16 DIP;
    // the FontSize 10 full-date row is 15 (13 + Margin top 2). Spacing 4 between rows, panel
    // Margin 6+6. Budgeting every row at the taller 16 DIP keeps StatsHeightFor(5) at exactly
    // 108 and leaves 1 DIP of slack for the date row, so content is never clipped.
    private const double MetricRowH = 16.0;
    private const double RowSpacing  = 4.0;
    private const double PanelMargin = 12.0;

    /// <summary>
    /// Height of the System Stats panel for a given number of visible rows (DIP).
    /// For the default 5 rows this returns exactly <see cref="OverlayTokens.StatsExpandedH"/>
    /// (12 + 5×16 + 4×4 = 108), the value the constant was rounded to by hand. Fewer rows scale
    /// down by one row height plus one spacing each. A non-positive count falls back to the
    /// default 5-row panel rather than collapsing the surface to nothing.
    /// </summary>
    public static double StatsHeightFor(int rowCount)
    {
        if (rowCount <= 0) rowCount = DefaultRowCount;
        return PanelMargin + MetricRowH * rowCount + RowSpacing * (rowCount - 1);
    }

    // -- Empty-surface collapse decision --------------------------------------------------

    /// <summary>
    /// True when the System Stats surface is the current kind but has nothing to show, so the
    /// caller must dispatch <see cref="OverlayCommand.Collapse"/>. That happens when the master
    /// box is off (<paramref name="enabled"/> false) or the resolved row set is empty (preset
    /// Off, or an all-unknown custom set). Without this the machine stays at
    /// <see cref="OverlayKind.SystemStats"/> and the window keeps the expanded
    /// <see cref="StatsHeightFor"/>(0) height — an empty pill-sized island.
    /// The same predicate gates the hover peek, so an empty surface is never re-opened.
    /// </summary>
    public static bool ShouldCollapseStatsSurface(OverlayKind kind, bool enabled, int rowCount) =>
        kind == OverlayKind.SystemStats && (!enabled || rowCount <= 0);
}
