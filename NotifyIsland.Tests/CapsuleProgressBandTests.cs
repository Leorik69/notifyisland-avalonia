using NotifyIsland;
using Xunit;

namespace NotifyIsland.Tests;

public class CapsuleProgressBandTests
{
    private static ProgressBandState Nothing() => new();

    [Fact]
    public void NoActiveSourceLeavesTheBandFree()
    {
        Assert.Equal(ProgressBandOwner.None, CapsuleProgressBand.OwnerOf(Nothing()));
        Assert.False(CapsuleProgressBand.BandActive(Nothing()));
        Assert.Equal(0, CapsuleProgressBand.FractionFor(Nothing()));
        Assert.Equal("", CapsuleProgressBand.LabelFor(Nothing()));
    }

    [Fact]
    public void NullStateIsInert()
    {
        Assert.Equal(ProgressBandOwner.None, CapsuleProgressBand.OwnerOf(null));
        Assert.False(CapsuleProgressBand.BandActive(null));
    }

    [Fact]
    public void ClipboardBeatsMediaAndTimer()
    {
        var s = new ProgressBandState
        {
            ClipboardActive = true, ClipboardProgress = 0.4,
            MediaActive = true, MediaProgress = 0.9,
            TimerActive = true, TimerSeconds = 30, TimerTotalSeconds = 60
        };
        Assert.Equal(ProgressBandOwner.Clipboard, CapsuleProgressBand.OwnerOf(s));
        Assert.Equal(0.4, CapsuleProgressBand.FractionFor(s));
    }

    [Fact]
    public void MediaBeatsTimer()
    {
        var s = new ProgressBandState
        {
            MediaActive = true, MediaProgress = 0.33,
            TimerActive = true, TimerSeconds = 10, TimerTotalSeconds = 60
        };
        Assert.Equal(ProgressBandOwner.Media, CapsuleProgressBand.OwnerOf(s));
        Assert.Equal(0.33, CapsuleProgressBand.FractionFor(s));
    }

    [Fact]
    public void InactiveSourcesDoNotHoldTheBand()
    {
        // Media reported a session but is paused at the end: the flag the window sets is
        // "is there something to show", so an inactive source must never win arbitration.
        var s = new ProgressBandState { MediaActive = false, MediaProgress = 0.5 };
        Assert.Equal(ProgressBandOwner.None, CapsuleProgressBand.OwnerOf(s));
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(2.0, 1.0)]
    public void FractionsAreClamped(double input, double expected)
    {
        var s = new ProgressBandState { ClipboardActive = true, ClipboardProgress = input };
        Assert.Equal(expected, CapsuleProgressBand.FractionFor(s), 6);
    }

    [Fact]
    public void NaNFractionIsTreatedAsEmpty()
    {
        var s = new ProgressBandState { ClipboardActive = true, ClipboardProgress = double.NaN };
        Assert.Equal(0.0, CapsuleProgressBand.FractionFor(s));
    }

    [Fact]
    public void CountdownFractionCountsDown()
    {
        // A countdown empties as it runs, so the bar must shrink — the fraction is remaining.
        var s = new ProgressBandState
        {
            TimerActive = true, TimerCountUp = false,
            TimerSeconds = 20, TimerTotalSeconds = 60
        };
        Assert.Equal(1.0 - 20.0 / 60.0, CapsuleProgressBand.FractionFor(s), 6);
    }

    [Fact]
    public void StopwatchHasNoFraction()
    {
        // A stopwatch has no total, so any fraction would be a lie. The bar stays empty and
        // the label carries the elapsed time instead.
        var s = new ProgressBandState { TimerActive = true, TimerCountUp = true, TimerSeconds = 42 };
        Assert.Equal(0.0, CapsuleProgressBand.FractionFor(s));
        Assert.Equal("00:42", CapsuleProgressBand.LabelFor(s));
    }

    [Fact]
    public void CountdownWithUnknownTotalHasNoFractionEither()
    {
        var s = new ProgressBandState { TimerActive = true, TimerSeconds = 30, TimerTotalSeconds = 0 };
        Assert.Equal(0.0, CapsuleProgressBand.FractionFor(s));
    }

    [Fact]
    public void BandLabelsAreRussianAndShort()
    {
        Assert.Equal("Копирование",
            CapsuleProgressBand.LabelFor(new ProgressBandState { ClipboardActive = true }));
        Assert.Equal("♫",
            CapsuleProgressBand.LabelFor(new ProgressBandState { MediaActive = true }));
        Assert.Equal("05:00",
            CapsuleProgressBand.LabelFor(new ProgressBandState
            {
                TimerActive = true, TimerCountUp = false,
                TimerSeconds = 300, TimerTotalSeconds = 300
            }));
    }

    [Fact]
    public void LongCountdownUsesHours()
    {
        var s = new ProgressBandState { TimerActive = true, TimerSeconds = 3725, TimerTotalSeconds = 7200 };
        Assert.Equal("1:02:05", CapsuleProgressBand.LabelFor(s));
    }

    [Fact]
    public void BandIsEightDipTallWithATwoDipBar()
    {
        // The band must fit the strip the capsule already reserves: 8 DIP inside a fixed
        // 30 DIP capsule. If this ever changes, the band has outgrown its slot.
        Assert.Equal(8.0, CapsuleProgressBand.BandH);
        Assert.True(CapsuleProgressBand.BarH <= CapsuleProgressBand.BandH);
        Assert.True(CapsuleProgressBand.BandH < OverlayTokens.CollapsedH);
    }

    [Fact]
    public void EveryOwnerHasItsOwnAccent()
    {
        var accents = new[]
        {
            CapsuleProgressBand.AccentFor(new ProgressBandState { ClipboardActive = true }),
            CapsuleProgressBand.AccentFor(new ProgressBandState { MediaActive = true }),
            CapsuleProgressBand.AccentFor(new ProgressBandState { TimerActive = true })
        };
        Assert.Equal(3, accents.Distinct().Count());
        Assert.Equal("Idle", CapsuleProgressBand.AccentFor(Nothing()));
    }

    [Fact]
    public void StatusRowModelDefaultsAreInert()
    {
        var row = new StatusRowModel();
        Assert.False(row.Active);
        Assert.Null(row.Progress);
        Assert.Equal(StatusRowKind.Media, row.Kind);
    }
}

public class StatsRowStatusTests
{
    [Fact]
    public void MediaAndTimerHaveLabels()
    {
        Assert.Equal("Плеер", StatsLayout.LabelFor(StatsRow.Media));
        Assert.Equal("Таймер", StatsLayout.LabelFor(StatsRow.Timer));
    }

    [Fact]
    public void MediaAndTimerHaveNamesForTheSettingsEditor()
    {
        Assert.Equal("Плеер", StatsLayout.NameFor(StatsRow.Media));
        Assert.Equal("Таймер", StatsLayout.NameFor(StatsRow.Timer));
    }

    [Fact]
    public void MediaAndTimerCarryActions()
    {
        Assert.True(StatsLayout.HasActions(StatsRow.Media));
        Assert.True(StatsLayout.HasActions(StatsRow.Timer));
    }

    [Theory]
    [InlineData(StatsRow.Cpu)]
    [InlineData(StatsRow.Memory)]
    [InlineData(StatsRow.Battery)]
    [InlineData(StatsRow.Network)]
    [InlineData(StatsRow.Date)]
    public void MetricAndDateRowsCarryNoActions(StatsRow row) => Assert.False(StatsLayout.HasActions(row));

    [Fact]
    public void StatusRowsAreNotCaptionRows()
    {
        // A caption row is one centred line; a status row is label + value + buttons.
        Assert.False(StatsLayout.IsCaptionRow(StatsRow.Media));
        Assert.False(StatsLayout.IsCaptionRow(StatsRow.Timer));
    }

    [Fact]
    public void PresetsDoNotForceTheStatusRowsOn()
    {
        // Full/Brief are the metrics presets. Status rows are opt-in via Custom so adding one
        // never changes what an existing user's monitor looks like.
        Assert.DoesNotContain(StatsRow.Media, StatsLayout.FullRows);
        Assert.DoesNotContain(StatsRow.Timer, StatsLayout.FullRows);
        Assert.DoesNotContain(StatsRow.Media, StatsLayout.BriefRows);
    }

    [Fact]
    public void CustomRowSetMayIncludeStatusRows()
    {
        var rows = StatsLayout.ResolveRows(StatsPreset.Custom,
            new[] { StatsRow.Cpu, StatsRow.Media, StatsRow.Timer });
        Assert.Equal(3, rows.Count);
        Assert.Contains(StatsRow.Media, rows);
    }

    [Fact]
    public void AStatusRowStillFitsTheSixteenDipBudget()
    {
        // Rows with buttons are 16 DIP tall like every other row, so adding them must not
        // change the panel height formula.
        Assert.Equal(108.0, StatsLayout.StatsHeightFor(StatsLayout.DefaultRowCount), 3);
    }
}
