using System.Collections.Generic;
using System.Text.Json;
using NotifyIsland;
using Xunit;

namespace NotifyIsland.Tests;

/// <summary>Row-set presets, Normalize() consistency, computed height, and JSON round-trip.</summary>
public class StatsRowsTests
{
    // -- ResolveRows ---------------------------------------------------------------------

    [Fact]
    public void ResolveRows_Full_IsAllFiveRowsInCanonicalOrder()
    {
        Assert.Equal(
            new[] { StatsRow.Cpu, StatsRow.Memory, StatsRow.Battery, StatsRow.Network, StatsRow.Date },
            StatsLayout.ResolveRows(StatsPreset.Full, null));
    }

    [Fact]
    public void ResolveRows_Brief_IsCpuAndBattery()
    {
        Assert.Equal(
            new[] { StatsRow.Cpu, StatsRow.Battery },
            StatsLayout.ResolveRows(StatsPreset.Brief, StatsLayout.FullRows));
    }

    [Fact]
    public void ResolveRows_Off_IsEmpty_RegardlessOfCustom()
    {
        Assert.Empty(StatsLayout.ResolveRows(StatsPreset.Off, StatsLayout.FullRows));
        Assert.Empty(StatsLayout.ResolveRows(StatsPreset.Off, null));
    }

    [Fact]
    public void ResolveRows_Custom_KeepsUserOrder()
    {
        var custom = new List<StatsRow> { StatsRow.Date, StatsRow.Network, StatsRow.Cpu };
        Assert.Equal(custom, StatsLayout.ResolveRows(StatsPreset.Custom, custom));
    }

    [Fact]
    public void ResolveRows_Custom_DropsDuplicates()
    {
        var custom = new List<StatsRow> { StatsRow.Network, StatsRow.Cpu, StatsRow.Cpu };
        var rows = StatsLayout.ResolveRows(StatsPreset.Custom, custom);
        Assert.Equal(new[] { StatsRow.Network, StatsRow.Cpu }, rows);
    }

    [Fact]
    public void ResolveRows_Custom_DropsUnknownValues()
    {
        var custom = new List<StatsRow> { (StatsRow)999, StatsRow.Cpu, (StatsRow)(-3) };
        Assert.Equal(new[] { StatsRow.Cpu }, StatsLayout.ResolveRows(StatsPreset.Custom, custom));
    }

    [Fact]
    public void ResolveRows_Custom_EmptyOrAllUnknown_FallsBackToFull()
    {
        var full = new[] { StatsRow.Cpu, StatsRow.Memory, StatsRow.Battery, StatsRow.Network, StatsRow.Date };
        Assert.Equal(full, StatsLayout.ResolveRows(StatsPreset.Custom, new List<StatsRow>()));
        Assert.Equal(full, StatsLayout.ResolveRows(StatsPreset.Custom, null));
        Assert.Equal(full, StatsLayout.ResolveRows(
            StatsPreset.Custom, new List<StatsRow> { (StatsRow)42 }));
    }

    // -- Row → label / shape mapping -----------------------------------------------------

    [Fact]
    public void LabelFor_MetricRows_AreTheLegacyPanelLabels()
    {
        Assert.Equal("CPU", StatsLayout.LabelFor(StatsRow.Cpu));
        Assert.Equal("Память", StatsLayout.LabelFor(StatsRow.Memory));
        Assert.Equal("Батарея", StatsLayout.LabelFor(StatsRow.Battery));
        Assert.Equal("Сеть", StatsLayout.LabelFor(StatsRow.Network));
    }

    [Fact]
    public void DateRow_IsTheOnlyCaptionRow_AndHasNoLabel()
    {
        Assert.True(StatsLayout.IsCaptionRow(StatsRow.Date));
        Assert.Equal("", StatsLayout.LabelFor(StatsRow.Date));
        foreach (var row in new[] { StatsRow.Cpu, StatsRow.Memory, StatsRow.Battery, StatsRow.Network })
            Assert.False(StatsLayout.IsCaptionRow(row));
    }

    [Fact]
    public void EveryResolvedRow_RendersSomething_AndHeightCoversIt()
    {
        // Each row contributes at least a label or a caption, so no row is a blank 16 DIP hole.
        foreach (var row in StatsLayout.FullRows)
            Assert.True(StatsLayout.IsCaptionRow(row) || StatsLayout.LabelFor(row).Length > 0);

        // The caption row is shorter in reality; StatsHeightFor budgets every row at the
        // taller 16 DIP, so the panel height is never below the content it must hold.
        Assert.True(StatsLayout.StatsHeightFor(1) >= OverlayTokens.StatsExpandedH / 5.0);
    }

    // -- Normalize -----------------------------------------------------------------------

    [Fact]
    public void Normalize_FullPreset_RewritesStaleRowList()
    {
        var s = new AppSettings
        {
            StatsRowsPreset = StatsPreset.Full,
            StatsRows = new List<StatsRow> { StatsRow.Date }
        };
        s.Normalize();
        Assert.Equal(StatsLayout.FullRows, s.StatsRows);
    }

    [Fact]
    public void Normalize_BriefPreset_RewritesRowListToBrief()
    {
        var s = new AppSettings
        {
            StatsRowsPreset = StatsPreset.Brief,
            StatsRows = new List<StatsRow>(StatsLayout.FullRows)
        };
        s.Normalize();
        Assert.Equal(StatsLayout.BriefRows, s.StatsRows);
    }

    [Fact]
    public void Normalize_OffPreset_EmptiesRowList()
    {
        var s = new AppSettings
        {
            StatsRowsPreset = StatsPreset.Off,
            StatsRows = new List<StatsRow>(StatsLayout.FullRows)
        };
        s.Normalize();
        Assert.Empty(s.StatsRows);
    }

    [Fact]
    public void Normalize_CustomPreset_KeepsFilteredUserOrder()
    {
        var s = new AppSettings
        {
            StatsRowsPreset = StatsPreset.Custom,
            StatsRows = new List<StatsRow> { StatsRow.Network, (StatsRow)77, StatsRow.Network, StatsRow.Cpu }
        };
        s.Normalize();
        Assert.Equal(new[] { StatsRow.Network, StatsRow.Cpu }, s.StatsRows);
    }

    [Fact]
    public void Normalize_CustomPreset_EmptyList_FallsBackToFullNotZeroHeight()
    {
        var s = new AppSettings
        {
            StatsRowsPreset = StatsPreset.Custom,
            StatsRows = new List<StatsRow>()
        };
        s.Normalize();
        Assert.Equal(StatsLayout.FullRows, s.StatsRows);
        Assert.Equal(OverlayTokens.StatsExpandedH, StatsLayout.StatsHeightFor(s.StatsRows.Count));
    }

    [Fact]
    public void Normalize_UnknownPresetValue_FallsBackToFull()
    {
        var s = new AppSettings { StatsRowsPreset = (StatsPreset)123 };
        s.Normalize();
        Assert.Equal(StatsPreset.Full, s.StatsRowsPreset);
        Assert.Equal(StatsLayout.FullRows, s.StatsRows);
    }

    [Fact]
    public void Normalize_DisabledSurface_StillNormalizesRows_AndEnableFlagIsIndependent()
    {
        var s = new AppSettings
        {
            SystemStatsEnabled = false,
            StatsRowsPreset = StatsPreset.Brief
        };
        s.Normalize();
        Assert.False(s.SystemStatsEnabled);
        Assert.Equal(StatsPreset.Brief, s.StatsRowsPreset);
        Assert.Equal(StatsLayout.BriefRows, s.StatsRows);
    }

    [Fact]
    public void DefaultSettings_AreFullPresetWithFullRowList()
    {
        var s = new AppSettings();
        Assert.Equal(StatsPreset.Full, s.StatsRowsPreset);
        Assert.Equal(StatsLayout.FullRows, s.StatsRows);
    }

    [Fact]
    public void CopyTo_CarriesPresetAndRowOrder()
    {
        var src = new AppSettings
        {
            StatsRowsPreset = StatsPreset.Custom,
            StatsRows = new List<StatsRow> { StatsRow.Date, StatsRow.Cpu }
        };
        var dst = new AppSettings();
        src.CopyTo(dst);
        Assert.Equal(StatsPreset.Custom, dst.StatsRowsPreset);
        Assert.Equal(new[] { StatsRow.Date, StatsRow.Cpu }, dst.StatsRows);
    }

    // -- Height --------------------------------------------------------------------------

    [Fact]
    public void StatsHeightFor_FiveRows_EqualsLegacyToken()
    {
        // Regression guard: the default Full preset must keep the 108 DIP panel of 1.12.1.
        Assert.Equal(OverlayTokens.StatsExpandedH, StatsLayout.StatsHeightFor(5));
        Assert.Equal(108.0, StatsLayout.StatsHeightFor(5));
    }

    [Fact]
    public void StatsHeightFor_ScalesWithRowCount()
    {
        Assert.True(StatsLayout.StatsHeightFor(2) < StatsLayout.StatsHeightFor(5));
        Assert.True(StatsLayout.StatsHeightFor(3) < StatsLayout.StatsHeightFor(5));
        Assert.True(StatsLayout.StatsHeightFor(1) < StatsLayout.StatsHeightFor(2));
        Assert.True(StatsLayout.StatsHeightFor(2) < StatsLayout.StatsHeightFor(3));
    }

    [Fact]
    public void StatsHeightFor_NonPositiveCount_UsesDefaultPanel()
    {
        Assert.Equal(StatsLayout.StatsHeightFor(5), StatsLayout.StatsHeightFor(0));
        Assert.Equal(StatsLayout.StatsHeightFor(5), StatsLayout.StatsHeightFor(-3));
    }

    [Fact]
    public void HeightFor_SystemStats_FollowsRowCount()
    {
        Assert.Equal(OverlayTokens.CollapsedH, OverlayMachine.HeightFor(OverlayKind.Idle, 2));
        Assert.Equal(OverlayTokens.StatsExpandedH, OverlayMachine.HeightFor(OverlayKind.SystemStats));
        Assert.Equal(StatsLayout.StatsHeightFor(2), OverlayMachine.HeightFor(OverlayKind.SystemStats, 2));
    }

    [Fact]
    public void SizeFor_SystemStats_HeightFollowsRows_WidthStaysFixed()
    {
        var (wFull, hFull) = IslandLayout.SizeFor(
            OverlayKind.SystemStats, false, IslandOrientation.Horizontal, IslandEdge.Top);
        var (wBrief, hBrief) = IslandLayout.SizeFor(
            OverlayKind.SystemStats, false, IslandOrientation.Horizontal, IslandEdge.Top, statsRowCount: 2);
        Assert.Equal(OverlayTokens.StatsExpandedW, wFull);
        Assert.Equal(OverlayTokens.StatsExpandedW, wBrief);
        Assert.Equal(OverlayTokens.StatsExpandedH, hFull);
        Assert.True(hBrief < hFull);
    }

    [Fact]
    public void Snapshot_SystemStatsHeight_FollowsStatsRowCount()
    {
        var m = new OverlayMachine { StatsRowCount = 2 };
        m.Dispatch(OverlayCommand.SetSystemStats, new OverlayPayload
        {
            SystemStats = new SystemSnapshot { CpuPercent = 10, RamTotalBytes = 1024 }
        });
        var snap = m.Snapshot();
        Assert.Equal(OverlayKind.SystemStats, snap.Kind);
        Assert.Equal(OverlayTokens.StatsExpandedW, snap.Width);
        Assert.Equal(StatsLayout.StatsHeightFor(2), snap.Height);
    }

    // -- JSON ----------------------------------------------------------------------------

    [Fact]
    public void Json_SerializesEnumsAsReadableStrings()
    {
        var s = new AppSettings
        {
            StatsRowsPreset = StatsPreset.Brief,
            StatsRows = new List<StatsRow> { StatsRow.Memory, StatsRow.Date }
        };
        var json = s.ToJson();
        // The property-level [JsonConverter(typeof(JsonStringEnumConverter))] (no naming policy)
        // wins over the options' camelCase converter, so the preset serialises PascalCase; the
        // row list, which has no property attribute, falls back to the camelCase options converter.
        // Either way it is a human-readable string, not an ordinal number.
        Assert.Contains("\"statsRowsPreset\": \"Brief\"", json);
        Assert.Contains("\"memory\"", json);
        Assert.Contains("\"date\"", json);
        Assert.DoesNotContain("\"statsRowsPreset\": 1", json);
    }

    [Fact]
    public void Json_RoundTripsPresetAndRowOrder()
    {
        var s = new AppSettings
        {
            StatsRowsPreset = StatsPreset.Custom,
            StatsRows = new List<StatsRow> { StatsRow.Network, StatsRow.Cpu, StatsRow.Date }
        };
        var back = AppSettings.FromJson(s.ToJson());
        Assert.NotNull(back);
        Assert.Equal(StatsPreset.Custom, back!.StatsRowsPreset);
        Assert.Equal(new[] { StatsRow.Network, StatsRow.Cpu, StatsRow.Date }, back.StatsRows);
    }

    [Fact]
    public void Json_HandEditedStringPreset_IsAccepted()
    {
        var json = "{\"systemStatsEnabled\":true,\"statsRowsPreset\":\"Custom\",\"statsRows\":[\"Battery\",\"Cpu\"]}";
        var back = AppSettings.FromJson(json);
        Assert.NotNull(back);
        Assert.Equal(StatsPreset.Custom, back!.StatsRowsPreset);
        Assert.Equal(new[] { StatsRow.Battery, StatsRow.Cpu }, back.StatsRows);
    }

    [Fact]
    public void Json_DeserializesRowListAsEnumStringsNotNumbers()
    {
        var json = new AppSettings { StatsRows = new List<StatsRow> { StatsRow.Cpu } }.ToJson();
        using var doc = JsonDocument.Parse(json);
        var rows = doc.RootElement.GetProperty("statsRows");
        Assert.Equal(JsonValueKind.String, rows[0].ValueKind);
    }

    // -- Empty-surface collapse decision --------------------------------------------------

    [Fact]
    public void ShouldCollapseStatsSurface_SystemStatsWithNoRows_Collapses()
    {
        Assert.True(StatsLayout.ShouldCollapseStatsSurface(OverlayKind.SystemStats, enabled: true, rowCount: 0));
    }

    [Fact]
    public void ShouldCollapseStatsSurface_SystemStatsWithMasterBoxOff_Collapses()
    {
        Assert.True(StatsLayout.ShouldCollapseStatsSurface(OverlayKind.SystemStats, enabled: false, rowCount: 5));
    }

    [Fact]
    public void ShouldCollapseStatsSurface_SystemStatsWithRows_StaysOpen()
    {
        Assert.False(StatsLayout.ShouldCollapseStatsSurface(OverlayKind.SystemStats, enabled: true, rowCount: 1));
        Assert.False(StatsLayout.ShouldCollapseStatsSurface(OverlayKind.SystemStats, enabled: true, rowCount: 5));
    }

    [Fact]
    public void ShouldCollapseStatsSurface_OtherKinds_NeverCollapse()
    {
        Assert.False(StatsLayout.ShouldCollapseStatsSurface(OverlayKind.Idle, enabled: false, rowCount: 0));
        Assert.False(StatsLayout.ShouldCollapseStatsSurface(OverlayKind.Notification, enabled: false, rowCount: 0));
        Assert.False(StatsLayout.ShouldCollapseStatsSurface(OverlayKind.Weather, enabled: true, rowCount: 0));
    }
}
