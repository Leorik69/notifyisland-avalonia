using System;
using NotifyIsland;
using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// The «сколько времени назад» wording of the 1.12.3 history panel, and the row set itself.
///
/// The boundaries are the whole point: a panel that says «1 мин» for a 59-second-old copy, or
/// «вчера» for 47 hours, is wrong in a way the eye catches immediately and a reader of the code
/// would not. So each boundary gets its own test rather than one table with a loop.
/// </summary>
public class ClipboardHistoryRowsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Age_ZeroSeconds_IsJustNow() =>
        Assert.Equal(ClipboardHistoryRows.JustNow, ClipboardHistoryRows.AgeTextFor(Now.AddSeconds(0), Now));

    [Fact]
    public void Age_59Seconds_IsStillJustNow()
    {
        // The off-by-one that matters: rounding instead of flooring would show «1 мин» here.
        Assert.Equal(ClipboardHistoryRows.JustNow, ClipboardHistoryRows.AgeTextFor(Now.AddSeconds(-59), Now));
    }

    [Fact]
    public void Age_60Seconds_IsOneMinute() =>
        Assert.Equal("1 мин", ClipboardHistoryRows.AgeTextFor(Now.AddSeconds(-60), Now));

    [Fact]
    public void Age_SeveralMinutes_CountsThem() =>
        Assert.Equal("7 мин", ClipboardHistoryRows.AgeTextFor(Now.AddMinutes(-7), Now));

    [Fact]
    public void Age_59Minutes_StaysInMinutes() =>
        Assert.Equal("59 мин", ClipboardHistoryRows.AgeTextFor(Now.AddMinutes(-59), Now));

    [Fact]
    public void Age_OneHour_IsSpelledOutNotNumbered()
    {
        // «час», not «1 ч» — the numeral reads as a measurement, and this is the one hour
        // count where the singular has its own word.
        Assert.Equal(ClipboardHistoryRows.OneHour, ClipboardHistoryRows.AgeTextFor(Now.AddHours(-1), Now));
    }

    [Fact]
    public void Age_TwoHours_IsNumbered() =>
        Assert.Equal("2 ч", ClipboardHistoryRows.AgeTextFor(Now.AddHours(-2), Now));

    [Fact]
    public void Age_23Hours59Minutes_IsStillInHours()
    {
        // The last instant before «вчера». Flooring to 23 here is what makes 24:00 read as
        // yesterday instead of as a 24-hour count.
        Assert.Equal("23 ч", ClipboardHistoryRows.AgeTextFor(Now.AddHours(-23).AddMinutes(-59), Now));
    }

    [Fact]
    public void Age_FullDay_IsYesterday() =>
        Assert.Equal(ClipboardHistoryRows.Yesterday, ClipboardHistoryRows.AgeTextFor(Now.AddHours(-24), Now));

    [Fact]
    public void Age_47Hours_IsStillYesterday()
    {
        // Floor, not round: 47 h 59 min is 1 day, and rounding would print «2 дн».
        Assert.Equal(ClipboardHistoryRows.Yesterday, ClipboardHistoryRows.AgeTextFor(Now.AddHours(-47), Now));
    }

    [Fact]
    public void Age_TwoDays_IsCountedInDays() =>
        Assert.Equal("2 дн", ClipboardHistoryRows.AgeTextFor(Now.AddDays(-2), Now));

    [Fact]
    public void Age_FutureCapture_ClampsToJustNow()
    {
        // Clock skew, or an entry stamped by a machine running ahead. A negative age must never
        // reach the panel as «-3 мин».
        Assert.Equal(ClipboardHistoryRows.JustNow, ClipboardHistoryRows.AgeTextFor(Now.AddMinutes(3), Now));
    }

    [Fact]
    public void Build_TakesNewestFirst_AndCapsAtTheToken()
    {
        var h = new ClipboardHistory(20);
        // Newest LAST in the ring buffer, with item 11 the most recent. Getting this backwards is
        // the easy mistake: Push appends, and SnapshotNewestFirst walks backwards from the end.
        for (var i = 0; i < 12; i++)
            h.Push(ClipboardEntry.FromText($"item {i}", Now.AddMinutes(-(11 - i) * 5)));

        var rows = ClipboardHistoryRows.Build(h, Now);

        Assert.Equal(OverlayTokens.HistoryPanelMaxRows, rows.Count);
        Assert.Equal("item 11", rows[0].Title);
        Assert.Equal("item 4", rows[rows.Count - 1].Title);
        // The ages must descend with the list — a panel that says "just now" at the bottom is
        // upside down, and nothing else in the test would catch it.
        Assert.Equal(ClipboardHistoryRows.JustNow, rows[0].AgeText);
        Assert.Equal("35 мин", rows[rows.Count - 1].AgeText);
    }

    [Fact]
    public void Build_ReusesTheIconRuleOfTheExistingPreview()
    {
        // The panel must not invent a second icon mapping; it takes the same keys the ball and
        // the phase-A preview already use, so the three always agree on what a file looks like.
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromFile(@"C:\tmp\a.txt", Now));
        h.Push(ClipboardEntry.FromText("привет", Now.AddSeconds(-30)));
        h.Push(ClipboardEntry.FromFiles(new[] { @"C:\a", @"C:\b" }, Now.AddSeconds(-60)));

        var rows = ClipboardHistoryRows.Build(h, Now);

        // Push order is oldest-first, so newest-first gives: files (t-60 s), text (t-30 s),
        // file (now). Text deliberately shares the clipboard silhouette with the generic case.
        Assert.Equal("files", rows[0].IconKey);
        Assert.Equal("clipboard", rows[1].IconKey);
        Assert.Equal("file", rows[2].IconKey);
    }

    [Fact]
    public void Build_UsesBuildPayloadTitle_NotASecondTextRule()
    {
        // MultiFile's title is the Russian plural BuildPayload already owns. If the panel rolled
        // its own wording the two would drift on the first number that needs 5-20 files.
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromFiles(FivePaths(), Now));

        var row = Assert.Single(ClipboardHistoryRows.Build(h, Now));
        Assert.Equal("5 файлов", row.Title);
    }

    [Fact]
    public void Build_EmptyHistory_ProducesNoRows()
    {
        // Empty history must be an empty list, not a panel with a placeholder row: the panel
        // simply does not open (see OverlayWindow.HandleBlobClick).
        Assert.Empty(ClipboardHistoryRows.Build(new ClipboardHistory(), Now));
        Assert.Empty(ClipboardHistoryRows.Build(null, Now));
    }

    [Fact]
    public void Build_StampsAgeFromTheCaptureTimeNotThePushTime()
    {
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromText("давно", Now.AddHours(-5)));

        var row = Assert.Single(ClipboardHistoryRows.Build(h, Now));
        Assert.Equal("5 ч", row.AgeText);
    }

    // 5 paths on purpose: 5-20 is the Russian plural band that reads «файлов», so this pins the
    // band rather than the trivial «2 файла».
    private static string[] FivePaths() => new[] { @"C:\a", @"C:\b", @"C:\c", @"C:\d", @"C:\e" };
}
