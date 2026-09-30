using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// Pins the width-customisation limits (1.14).
///
/// The limits are the deliverable, not an afterthought. A width control with no bounds lets the
/// island grow until it no longer fits on screen, and the placement / window-seating code around
/// it is built for a 30 DIP-high capsule rather than an arbitrary one. These tests exist so that
/// widening the range later is a deliberate, visible act — and so a NaN or a negative from a
/// hand-edited settings.json cannot produce a nonsense width.
/// </summary>
public class IslandWidthTests
{
    [Fact]
    public void Default_IsExactlyTheTokenWidths()
    {
        // Scale 1 must be a no-op, or shipping the setting would silently resize every existing
        // install's island.
        Assert.Equal(OverlayTokens.CollapsedW, IslandWidth.CollapsedLongAxis(1.0, weatherEnabled: false));
        Assert.Equal(OverlayTokens.CollapsedWeatherW, IslandWidth.CollapsedLongAxis(1.0, weatherEnabled: true));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    [InlineData(0.74)]
    [InlineData(99.0)]
    [InlineData(double.NaN)]
    public void OutOfRangeAndNonsense_ClampToTheSupportedBand(double scale)
    {
        var clamped = IslandWidth.ClampScale(scale);
        Assert.InRange(clamped, IslandWidth.MinScale, IslandWidth.MaxScale);
    }

    [Fact]
    public void BoundariesThemselvesAreAllowed()
    {
        Assert.Equal(IslandWidth.MinScale, IslandWidth.ClampScale(IslandWidth.MinScale));
        Assert.Equal(IslandWidth.MaxScale, IslandWidth.ClampScale(IslandWidth.MaxScale));
    }

    [Fact]
    public void WeatherCapsuleStaysWiderThanThePlainOne_AtEveryScale()
    {
        // The weather chip has to keep its extra room; scaling must not be applied to the
        // difference between the two widths by accident.
        for (var s = IslandWidth.MinScale; s <= IslandWidth.MaxScale; s += 0.05)
            Assert.True(IslandWidth.CollapsedLongAxis(s, true) > IslandWidth.CollapsedLongAxis(s, false));
    }

    [Fact]
    public void LongerNeverShorter()
    {
        var prev = 0.0;
        for (var s = IslandWidth.MinScale; s <= IslandWidth.MaxScale + 0.001; s += 0.05)
        {
            var w = IslandWidth.CollapsedLongAxis(s, weatherEnabled: false);
            Assert.True(w >= prev, $"width went backwards at scale {s:0.00}");
            prev = w;
        }
    }

    [Fact]
    public void TheRangeIsTheOneTheDocsClaim()
    {
        // The upper bound is set by a 13" laptop: 170 x 1.6 = 272 DIP of mostly-empty capsule.
        Assert.Equal(272.0, IslandWidth.CollapsedLongAxis(IslandWidth.MaxScale, weatherEnabled: false), 6);
        // The lower bound is set by content fitting, not by taste.
        Assert.Equal(127.5, IslandWidth.CollapsedLongAxis(IslandWidth.MinScale, weatherEnabled: false), 6);
    }

    [Fact]
    public void SnapToStep_ProducesRoundNumbers()
    {
        Assert.Equal(1.25, IslandWidth.SnapToStep(1.24), 6);
        Assert.Equal(1.25, IslandWidth.SnapToStep(1.26), 6);
        Assert.Equal(1.00, IslandWidth.SnapToStep(1.01), 6);
    }

    [Fact]
    public void SnapToStep_StillClampsFirst()
    {
        // A step-snapped value must never escape the band, or a slider at the end could produce
        // a scale the clamp would then have to undo.
        Assert.Equal(IslandWidth.MinScale, IslandWidth.SnapToStep(-5.0), 6);
        Assert.Equal(IslandWidth.MaxScale, IslandWidth.SnapToStep(50.0), 6);
    }

    [Fact]
    public void Describe_ShowsAConcreteDipValue()
    {
        Assert.Equal("170 DIP", IslandWidth.Describe(1.0, weatherEnabled: false));
        Assert.Equal("240 DIP", IslandWidth.Describe(1.0, weatherEnabled: true));
        Assert.Equal("272 DIP", IslandWidth.Describe(IslandWidth.MaxScale, weatherEnabled: false));
    }

    // --- the scale has to actually reach the geometry, not just exist in Core ---

    [Fact]
    public void WidthFor_ScalesTheCollapsedIsland()
    {
        var plain = OverlayMachine.WidthFor(OverlayKind.Collapsed);
        var wide = OverlayMachine.WidthFor(OverlayKind.Collapsed,
            collapsedScale: IslandWidth.MaxScale);
        Assert.Equal(plain * IslandWidth.MaxScale, wide, 6);
    }

    [Fact]
    public void WidthFor_DefaultScaleIsExactlyTheOldBehaviour()
    {
        // Every pre-1.14 test in the suite calls WidthFor without the scale; if the default were
        // anything but 1.0 the whole existing suite would move, which is why this is pinned.
        Assert.Equal(OverlayTokens.CollapsedW,
            OverlayMachine.WidthFor(OverlayKind.Idle, collapsedScale: IslandWidth.DefaultScale));
        Assert.Equal(OverlayTokens.CollapsedBatteryExtraW,
            OverlayMachine.WidthFor(OverlayKind.Idle, batteryChip: true) - OverlayTokens.CollapsedW, 6);
    }

    [Fact]
    public void WidthFor_BatteryChipScalesWithTheCapsule()
    {
        // The chip is added before the scale on purpose. Scaling it too is what stops a widened
        // capsule from carrying a fixed-width stub; the delta must grow with the scale.
        var narrow = OverlayMachine.WidthFor(OverlayKind.Collapsed,
            batteryChip: true, collapsedScale: IslandWidth.MinScale)
            - OverlayMachine.WidthFor(OverlayKind.Collapsed, collapsedScale: IslandWidth.MinScale);
        var wide = OverlayMachine.WidthFor(OverlayKind.Collapsed,
            batteryChip: true, collapsedScale: IslandWidth.MaxScale)
            - OverlayMachine.WidthFor(OverlayKind.Collapsed, collapsedScale: IslandWidth.MaxScale);
        Assert.Equal(OverlayTokens.CollapsedBatteryExtraW * IslandWidth.MinScale, narrow, 6);
        Assert.Equal(OverlayTokens.CollapsedBatteryExtraW * IslandWidth.MaxScale, wide, 6);
    }

    [Fact]
    public void WidthFor_DoesNotTouchTheNotificationKinds()
    {
        // Notifications size themselves from their content; scaling them would decouple the
        // capsule from the rows inside it.
        foreach (var kind in new[]
                 {
                     OverlayKind.Notification, OverlayKind.Progress, OverlayKind.Media,
                     OverlayKind.Timer, OverlayKind.Error, OverlayKind.Weather,
                     OverlayKind.Battery, OverlayKind.Clipboard, OverlayKind.SystemStats
                 })
        {
            Assert.Equal(OverlayMachine.WidthFor(kind),
                OverlayMachine.WidthFor(kind, collapsedScale: IslandWidth.MaxScale));
        }
    }

    [Theory]
    [InlineData(IslandOrientation.Horizontal, IslandEdge.Top)]
    [InlineData(IslandOrientation.Vertical, IslandEdge.Left)]
    public void SizeFor_ScaleLandsOnTheLongAxisOnly(
        IslandOrientation orientation, IslandEdge edge)
    {
        var narrow = IslandLayout.SizeFor(OverlayKind.Collapsed, weatherEnabled: true,
            orientation, edge, collapsedScale: 1.0);
        var wide = IslandLayout.SizeFor(OverlayKind.Collapsed, weatherEnabled: true,
            orientation, edge, collapsedScale: 1.5);
        var isVertical = IslandLayout.IsVertical(orientation, edge);
        if (isVertical)
        {
            Assert.Equal(OverlayTokens.CollapsedH, wide.Width, 6);   // cross axis fixed
            Assert.Equal(narrow.Height * 1.5, wide.Height, 6);
        }
        else
        {
            Assert.Equal(narrow.Width * 1.5, wide.Width, 6);
            Assert.Equal(OverlayTokens.CollapsedH, wide.Height, 6);  // cross axis fixed
        }
    }

    [Fact]
    public void Machine_SnapshotCarriesTheScale()
    {
        var m = new OverlayMachine();
        m.CollapsedWidthScale = 1.5;
        // A fresh machine is Idle with the weather chip on, so the base is CollapsedWeatherW.
        Assert.Equal(OverlayTokens.CollapsedWeatherW * 1.5, m.Snapshot().Width, 6);
    }

    [Fact]
    public void Machine_ScaleIsClampedOnAssignment()
    {
        // The window sizes itself from the snapshot, so an out-of-range value reaching the
        // machine would push the capsule off screen. The machine refuses rather than trusting.
        var m = new OverlayMachine();
        m.CollapsedWidthScale = 99.0;
        Assert.Equal(IslandWidth.MaxScale, m.CollapsedWidthScale);
        m.CollapsedWidthScale = 0.1;
        Assert.Equal(IslandWidth.MinScale, m.CollapsedWidthScale);
        // NaN is not a width anyone chose, so it falls back to neutral rather than to a bound.
        m.CollapsedWidthScale = double.NaN;
        Assert.Equal(IslandWidth.DefaultScale, m.CollapsedWidthScale);
    }

    // --- the settings layer, which is where a stale or hand-edited value gets rescued ---

    [Fact]
    public void Settings_DefaultIsTheNeutralScale()
    {
        Assert.Equal(IslandWidth.DefaultScale, new AppSettings().IslandWidthScale);
    }

    [Fact]
    public void Settings_NormalizeRescuesMissingAndZeroValues()
    {
        // A settings.json from before 1.14 has no islandWidthScale at all, and a hand-edit can
        // write 0. Both would otherwise clamp to MinScale and silently shrink the island.
        foreach (var raw in new[] { 0.0, -2.0, double.NaN })
        {
            var s = new AppSettings { IslandWidthScale = raw };
            s.Normalize();
            Assert.Equal(IslandWidth.DefaultScale, s.IslandWidthScale);
        }
    }

    [Fact]
    public void Settings_NormalizeClampsRealChoices()
    {
        var s = new AppSettings { IslandWidthScale = 42.0 };
        s.Normalize();
        Assert.Equal(IslandWidth.MaxScale, s.IslandWidthScale);
    }

    [Fact]
    public void Settings_CopyToCarriesTheScaleThrough()
    {
        // The copy layer is where the 5 s grace got silently truncated to 3 s once; the scale
        // must survive it unchanged or the save would be a lie.
        var s = new AppSettings { IslandWidthScale = 1.35 };
        var t = new AppSettings();
        s.CopyTo(t);
        Assert.Equal(1.35, t.IslandWidthScale, 6);
    }
}
