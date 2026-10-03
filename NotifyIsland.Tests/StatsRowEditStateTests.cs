using System.Collections.Generic;
using System.Linq;
using NotifyIsland;
using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// The Settings → Монитор → «Свой набор» row editor model: check / uncheck / reorder, seeding
/// when switching presets, and — most importantly — that what the editor persists and what
/// Normalize() + the overlay then resolve are the same rows in the same order.
/// </summary>
public class StatsRowEditStateTests
{
    private static StatsRow[] Order(StatsRowEditState s) => s.Items.Select(i => i.Row).ToArray();

    // -- Seeding -------------------------------------------------------------------------

    [Fact]
    public void Seed_FromFull_ShowsAllMetricsInCanonicalOrder()
    {
        // Full is the five metrics; the 1.13 status rows (Media/Timer) are parked below it,
        // unchecked, so adding them never changes an existing user's monitor.
        var s = new StatsRowEditState(StatsLayout.FullRows);
        Assert.Equal(
            new[]
            {
                StatsRow.Cpu, StatsRow.Memory, StatsRow.Battery, StatsRow.Network, StatsRow.Date,
                StatsRow.Media, StatsRow.Timer
            },
            Order(s));
        // The five metric rows come in checked; Media/Timer arrive parked and unchecked, so
        // the Full preset still means exactly what it meant in 1.12.
        Assert.Equal(StatsLayout.FullRows, s.Items.Where(i => i.IsVisible).Select(i => i.Row));
        Assert.False(s.IsVisible(StatsRow.Media));
        Assert.False(s.IsVisible(StatsRow.Timer));
    }

    [Fact]
    public void Seed_FromBrief_PutsTheOtherRowsBelowAsParkedUnchecked()
    {
        var s = new StatsRowEditState(StatsLayout.BriefRows);
        Assert.Equal(
            new[]
            {
                StatsRow.Cpu, StatsRow.Battery, StatsRow.Memory, StatsRow.Network, StatsRow.Date,
                StatsRow.Media, StatsRow.Timer
            },
            Order(s));
        Assert.Equal(new[] { StatsRow.Cpu, StatsRow.Battery },
            s.Items.Where(i => i.IsVisible).Select(i => i.Row));
    }

    [Fact]
    public void Seed_AlwaysCoversEveryRow_EvenFromNullOrEmpty()
    {
        foreach (var s in new[] { new StatsRowEditState(null), new StatsRowEditState(new List<StatsRow>()) })
        {
            Assert.Equal(Enum.GetValues<StatsRow>(), Order(s));
            Assert.Empty(s.ToCustomRows());   // nothing visible → ResolveRows falls back to Full
        }
    }

    [Fact]
    public void Seed_DropsDuplicatesAndUnknowns_WithoutLosingARow()
    {
        var s = new StatsRowEditState(new List<StatsRow>
        {
            StatsRow.Network, (StatsRow)99, StatsRow.Network, StatsRow.Cpu
        });
        Assert.Equal(
            new[]
            {
                StatsRow.Network, StatsRow.Cpu, StatsRow.Memory, StatsRow.Battery, StatsRow.Date,
                StatsRow.Media, StatsRow.Timer
            },
            Order(s));
        Assert.Equal(new[] { StatsRow.Network, StatsRow.Cpu }, s.ToCustomRows());
    }

    // -- Check / uncheck -----------------------------------------------------------------

    [Fact]
    public void WithVisibility_UnchecksInPlace_KeepingTheOrder()
    {
        var s = new StatsRowEditState(StatsLayout.FullRows)
            .WithVisibility(StatsRow.Memory, false);
        Assert.Equal(
            new[]
            {
                StatsRow.Cpu, StatsRow.Memory, StatsRow.Battery, StatsRow.Network, StatsRow.Date,
                StatsRow.Media, StatsRow.Timer
            },
            Order(s));
        Assert.Equal(
            new[] { StatsRow.Cpu, StatsRow.Battery, StatsRow.Network, StatsRow.Date },
            s.ToCustomRows());
    }

    [Fact]
    public void WithVisibility_IsIdempotent_AndDoesNotMutateTheOldState()
    {
        var before = new StatsRowEditState(StatsLayout.FullRows);
        var after = before.WithVisibility(StatsRow.Date, false);
        Assert.NotSame(before, after);
        Assert.Equal(StatsLayout.FullRows, before.ToCustomRows());   // still all five
        Assert.False(after.IsVisible(StatsRow.Date));
        Assert.Equal(after.ToCustomRows(), after.WithVisibility(StatsRow.Date, false).ToCustomRows());
    }

    [Fact]
    public void UncheckingEverything_FallsBackToFull_ExactlyLikeNormalize()
    {
        var s = new StatsRowEditState(StatsLayout.FullRows);
        foreach (var row in StatsRowEditState.AllRows) s = s.WithVisibility(row, false);
        Assert.Empty(s.ToCustomRows());
        Assert.Equal(StatsLayout.FullRows, s.ResolveRows(StatsPreset.Custom));
        Assert.Equal(StatsLayout.StatsHeightFor(5), s.HeightFor(StatsPreset.Custom));
    }

    // -- Reordering ----------------------------------------------------------------------

    [Fact]
    public void Move_Up_SwapsWithTheRowAbove()
    {
        var s = new StatsRowEditState(StatsLayout.FullRows).Move(StatsRow.Network, -1);
        Assert.Equal(
            new[]
            {
                StatsRow.Cpu, StatsRow.Memory, StatsRow.Network, StatsRow.Battery, StatsRow.Date,
                StatsRow.Media, StatsRow.Timer
            },
            Order(s));
    }

    [Fact]
    public void Move_Down_SwapsWithTheRowBelow()
    {
        var s = new StatsRowEditState(StatsLayout.FullRows).Move(StatsRow.Cpu, +1);
        Assert.Equal(
            new[]
            {
                StatsRow.Memory, StatsRow.Cpu, StatsRow.Battery, StatsRow.Network, StatsRow.Date,
                StatsRow.Media, StatsRow.Timer
            },
            Order(s));
    }

    [Fact]
    public void Move_AtEitherEnd_IsANoOp_NotAThrow()
    {
        var s = new StatsRowEditState(StatsLayout.FullRows);
        Assert.Equal(Order(s), Order(s.Move(StatsRow.Cpu, -1)));
        Assert.Equal(Order(s), Order(s.Move(StatsRow.Timer, +1)));   // last declared row
        Assert.Equal(Order(s), Order(s.Move(StatsRow.Memory, 0)));
        Assert.Equal(Order(s), Order(s.Move((StatsRow)123, -1)));   // unknown row
    }

    [Fact]
    public void Move_ReordersUncheckedRowsToo_SoAParkedRowCanBePromoted()
    {
        // Brief → Свой набор parks the rest; the user drags «Дата и время» up and ticks it.
        // Seed order is [Cpu, Battery, Memory, Network, Date, …], so one press of ↑ lands it
        // above «Сеть» — the parked rows keep canonical order below the visible ones.
        var s = new StatsRowEditState(StatsLayout.BriefRows)
            .Move(StatsRow.Date, -1)
            .WithVisibility(StatsRow.Date, true);
        Assert.Equal(
            new[]
            {
                StatsRow.Cpu, StatsRow.Battery, StatsRow.Memory, StatsRow.Date, StatsRow.Network,
                StatsRow.Media, StatsRow.Timer
            },
            Order(s));
        Assert.Equal(new[] { StatsRow.Cpu, StatsRow.Battery, StatsRow.Date }, s.ToCustomRows());
    }

    [Fact]
    public void AStatusRowCanBePromotedFromTheParkedTail()
    {
        // «Плеер» is parked below the metrics in 1.13; dragging it up must place it among the
        // visible rows so ticking it puts it in the user's custom set.
        var s = new StatsRowEditState(StatsLayout.FullRows)
            .Move(StatsRow.Media, -4)
            .WithVisibility(StatsRow.Media, true);
        Assert.Equal(StatsRow.Media, Order(s)[1]);
        Assert.Contains(StatsRow.Media, s.ToCustomRows());
    }

    [Fact]
    public void Move_Repeatedly_ReachesTheVeryTop()
    {
        var s = new StatsRowEditState(StatsLayout.FullRows);
        for (var i = 0; i < 10; i++) s = s.Move(StatsRow.Date, -1);
        Assert.Equal(StatsRow.Date, Order(s)[0]);
    }

    // -- Agreement with Normalize() / the overlay ----------------------------------------

    [Fact]
    public void EditorOutput_SurvivesNormalize_Unchanged()
    {
        var edit = new StatsRowEditState(StatsLayout.BriefRows)
            .Move(StatsRow.Battery, -1)
            .WithVisibility(StatsRow.Network, true);
        var s = new AppSettings
        {
            StatsRowsPreset = StatsPreset.Custom,
            StatsRows = new List<StatsRow>(edit.ToCustomRows()),
        };
        s.Normalize();
        // The settings preview, the persisted JSON and the live pill must all agree.
        Assert.Equal(edit.ToCustomRows(), s.StatsRows);
        Assert.Equal(edit.ResolveRows(StatsPreset.Custom),
            StatsLayout.ResolveRows(s.StatsRowsPreset, s.StatsRows));
    }

    [Fact]
    public void PreviewAndPill_AgreeForEveryPreset()
    {
        var edit = new StatsRowEditState(StatsLayout.FullRows)
            .Move(StatsRow.Date, -1)
            .WithVisibility(StatsRow.Memory, false);
        foreach (var preset in new[] { StatsPreset.Full, StatsPreset.Brief, StatsPreset.Custom })
        {
            var s = new AppSettings
            {
                StatsRowsPreset = preset,
                StatsRows = new List<StatsRow>(edit.ToCustomRows()),
            };
            s.Normalize();
            Assert.Equal(edit.ResolveRows(preset), StatsLayout.ResolveRows(s.StatsRowsPreset, s.StatsRows));
            Assert.Equal(StatsLayout.StatsHeightFor(edit.ResolveRows(preset).Count),
                edit.HeightFor(preset));
        }
    }

    [Fact]
    public void NonCustomPreset_IgnoresTheEditorOrder()
    {
        var edit = new StatsRowEditState(StatsLayout.FullRows).Move(StatsRow.Date, -4);
        Assert.Equal(StatsLayout.FullRows, edit.ResolveRows(StatsPreset.Full));
        Assert.Equal(StatsLayout.BriefRows, edit.ResolveRows(StatsPreset.Brief));
    }

    [Fact]
    public void PreviewHeight_ShrinksAsRowsAreUnchecked()
    {
        var full = new StatsRowEditState(StatsLayout.FullRows);
        var brief = full.WithVisibility(StatsRow.Memory, false)
            .WithVisibility(StatsRow.Network, false)
            .WithVisibility(StatsRow.Date, false);
        Assert.True(brief.HeightFor(StatsPreset.Custom) < full.HeightFor(StatsPreset.Custom));
        Assert.Equal(StatsLayout.StatsHeightFor(2), brief.HeightFor(StatsPreset.Custom));
    }

    [Fact]
    public void NameFor_LeavesTheDateRowNamed_WhereLabelForIsBlank()
    {
        Assert.Equal(StatsLayout.LabelFor(StatsRow.Cpu), StatsLayout.NameFor(StatsRow.Cpu));
        Assert.Equal(StatsLayout.LabelFor(StatsRow.Memory), StatsLayout.NameFor(StatsRow.Memory));
        Assert.Equal(StatsLayout.LabelFor(StatsRow.Battery), StatsLayout.NameFor(StatsRow.Battery));
        Assert.Equal(StatsLayout.LabelFor(StatsRow.Network), StatsLayout.NameFor(StatsRow.Network));
        Assert.Equal("", StatsLayout.LabelFor(StatsRow.Date));
        Assert.Equal("Дата и время", StatsLayout.NameFor(StatsRow.Date));
    }

    [Fact]
    public void AllRows_CoversTheWholeEnum_SoNoRowIsUnreachable()
    {
        Assert.Equal(Enum.GetValues<StatsRow>(), StatsRowEditState.AllRows);
    }
}
