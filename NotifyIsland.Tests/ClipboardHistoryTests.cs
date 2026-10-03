using System;
using System.Collections.Generic;
using Xunit;

namespace NotifyIsland.Tests;

public class ClipboardHistoryTests
{
    private static DateTimeOffset T(int seconds) => new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero).AddSeconds(seconds);

    [Fact]
    public void Capacity_IsClampedToHardCap()
    {
        Assert.Equal(50, new ClipboardHistory(50).Capacity);
        Assert.Equal(ClipboardHistory.HardCap, new ClipboardHistory(10_000).Capacity);
        Assert.Equal(1, new ClipboardHistory(0).Capacity);
    }

    [Fact]
    public void Push_AppendsAndRespectsCapacity()
    {
        var h = new ClipboardHistory(3);
        for (var i = 0; i < 5; i++)
            h.Push(ClipboardEntry.FromText($"t{i}", T(i)));
        Assert.Equal(3, h.Count);
        var snap = h.Snapshot();
        Assert.Equal("t2", snap[0].Text);
        Assert.Equal("t3", snap[1].Text);
        Assert.Equal("t4", snap[2].Text);
    }

    [Fact]
    public void Push_DedupesAgainstLatestIdentical()
    {
        // 1.13: the dedupe is no longer silent. Two byte-equal copies in a row COLLAPSE into
        // one row whose RunCount=2; the history's Count stays at 1 because there is one row,
        // not because the second capture was thrown away.
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromText("hello", T(0)));
        h.Push(ClipboardEntry.FromText("hello", T(5)));
        Assert.Equal(1, h.Count);
        Assert.Equal(2, h.Latest!.RunCount);
    }

    [Fact]
    public void Push_DifferentCapturesResetRunCount()
    {
        // "alpha" twice → run of 2; "beta" opens a fresh row with RunCount=1.
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromText("alpha", T(0)));
        h.Push(ClipboardEntry.FromText("alpha", T(1)));
        h.Push(ClipboardEntry.FromText("beta", T(2)));
        Assert.Equal(2, h.Count);
        Assert.Equal("beta", h.Latest!.Text);
        Assert.Equal(1, h.Latest.RunCount);
    }

    [Fact]
    public void Push_FileIdenticalSequenceBumpsRunCount()
    {
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromFiles(new List<string> { "a", "b" }, T(0)));
        h.Push(ClipboardEntry.FromFiles(new List<string> { "a", "b" }, T(1)));
        Assert.Equal(1, h.Count);
        Assert.Equal(2, h.Latest!.RunCount);
    }

    [Fact]
    public void Push_RunCountCapsAtDefaultDupRunCap()
    {
        var h = new ClipboardHistory();
        for (var i = 0; i < ClipboardHistory.DefaultDupRunCap + 5; i++)
            h.Push(ClipboardEntry.FromText("same", T(i)));
        Assert.Equal(1, h.Count);
        Assert.Equal(ClipboardHistory.DefaultDupRunCap, h.Latest!.RunCount);
    }

    [Fact]
    public void Push_PreservesCapturedAtAcrossRun()
    {
        // The "first item — ×N" wording needs the timestamp of the FIRST capture of the run,
        // not the latest one. The push guard must rewrite RunCount in place without bumping
        // CapturedAt.
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromText("x", T(0)));
        h.Push(ClipboardEntry.FromText("x", T(5)));
        h.Push(ClipboardEntry.FromText("x", T(9)));
        Assert.Equal(T(0), h.Latest!.CapturedAt);
    }

    // --- 1.13: pinned-first sort + toggle ----------------------------------------

    [Fact]
    public void Pin_NewestRowIsRejected()
    {
        // The spec calls this out explicitly: the most-recent row is the one the user just
        // copied, and pinning it would bounce back and forth between the top of the pinned
        // block and the chronological block on every new capture.
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromText("first", T(0)));
        h.Push(ClipboardEntry.FromText("second", T(1)));
        h.Push(ClipboardEntry.FromText("third", T(2)));
        Assert.False(h.Pin(0));
        Assert.False(h.Latest!.IsPinned);
    }

    [Fact]
    public void Pin_MovesRowToTopOfSnapshot()
    {
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromText("a", T(0)));
        h.Push(ClipboardEntry.FromText("b", T(1)));
        h.Push(ClipboardEntry.FromText("c", T(2)));
        // Pin "a" (snapshot index 2 in newest-first order: c=0, b=1, a=2).
        Assert.True(h.Pin(2));
        var pinned = h.SnapshotPinnedFirst();
        Assert.Equal("a", pinned[0].Text);
        Assert.Equal("c", pinned[1].Text);
        Assert.Equal("b", pinned[2].Text);
        Assert.True(pinned[0].IsPinned);
        Assert.False(pinned[1].IsPinned);
    }

    [Fact]
    public void TogglePin_RoundTrip()
    {
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromText("a", T(0)));
        h.Push(ClipboardEntry.FromText("b", T(1)));
        h.Push(ClipboardEntry.FromText("c", T(2)));
        Assert.True(h.TogglePin(2));
        Assert.True(h.NewestAt(2)!.IsPinned);
        Assert.True(h.TogglePin(2));
        Assert.False(h.NewestAt(2)!.IsPinned);
    }

    [Fact]
    public void TogglePin_PreservesRunCountAcrossRun()
    {
        // A pinned row that is repeated should keep its pinned state AND keep its first-run
        // capture timestamp — pinning is a property of the row, not of the captures.
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromText("a", T(0)));
        h.Push(ClipboardEntry.FromText("b", T(1)));
        h.Push(ClipboardEntry.FromText("c", T(2)));
        h.Pin(2); // pin "a"
        // "a" is no longer the head (c is), so each new "a" pushes a fresh row at first;
        // the SECOND consecutive "a" then bumps that new row's RunCount. After these pushes
        // the pinned block still holds the ORIGINAL "a" (RunCount=1), and a separate
        // chronological row holds the second "a" run with RunCount=2.
        h.Push(ClipboardEntry.FromText("a", T(3)));
        h.Push(ClipboardEntry.FromText("a", T(4)));
        var pinned = h.SnapshotPinnedFirst();
        Assert.Equal("a", pinned[0].Text);
        Assert.True(pinned[0].IsPinned);
        Assert.Equal(1, pinned[0].RunCount);
        // The newer "a" run is the chronological-newest entry (RunCount=2: T(3) opened it,
        // T(4) bumped it).
        Assert.Equal("a", pinned[1].Text);
        Assert.False(pinned[1].IsPinned);
        Assert.Equal(2, pinned[1].RunCount);
        Assert.Equal("c", pinned[2].Text);
        Assert.Equal("b", pinned[3].Text);
    }

    [Fact]
    public void Pin_OutOfRangeIndex_ReturnsFalse()
    {
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromText("a", T(0)));
        Assert.False(h.Pin(99));
        Assert.False(h.Pin(-1));
        Assert.False(h.TogglePin(99));
    }

    [Fact]
    public void Pin_AlreadyPinned_IsIdempotent()
    {
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromText("a", T(0)));
        h.Push(ClipboardEntry.FromText("b", T(1)));
        Assert.True(h.Pin(1));
        Assert.False(h.Pin(1));
        Assert.True(h.NewestAt(1)!.IsPinned);
    }

    // --- 1.13: clipboard privacy-pause helper (ball context menu + tray tooltip) ---

    [Fact]
    public void ClipboardPrivacyPause_Activate_Adds30MinutesUtc()
    {
        // Activate uses ToUniversalTime so a local "now" gives a UTC 30 minutes later,
        // regardless of the test machine's timezone. Without that, the spec's "30 мин" would
        // drift by ±1 h around DST shifts.
        var local = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Local);
        var expectedUtc = local.ToUniversalTime() + ClipboardPrivacyPause.DefaultDuration;
        Assert.Equal(expectedUtc, ClipboardPrivacyPause.Activate(local));
    }

    [Fact]
    public void ClipboardPrivacyPause_IsActive_TogglesByClock()
    {
        var now = T(0).UtcDateTime;
        var until = now.AddMinutes(30);
        Assert.True(ClipboardPrivacyPause.IsActive(until, now));
        Assert.True(ClipboardPrivacyPause.IsActive(until, now.AddMinutes(29).AddSeconds(59)));
        Assert.False(ClipboardPrivacyPause.IsActive(until, until)); // exactly at expiry → inactive
        Assert.False(ClipboardPrivacyPause.IsActive(until, until.AddHours(1)));
        Assert.False(ClipboardPrivacyPause.IsActive(null, now));
    }

    [Fact]
    public void ClipboardPrivacyPause_RemainingMinutes_RoundsUpForPartialMinutes()
    {
        var now = T(0).UtcDateTime;
        var untilShort = now.AddSeconds(30); // 30 s
        Assert.Equal(1, ClipboardPrivacyPause.RemainingMinutes(untilShort, now));
        var untilLong = now.AddMinutes(7).AddSeconds(45); // 7 min 45 s → rounds up to 8
        Assert.Equal(8, ClipboardPrivacyPause.RemainingMinutes(untilLong, now));
        var untilExact = now.AddMinutes(7); // exactly 7 min
        Assert.Equal(7, ClipboardPrivacyPause.RemainingMinutes(untilExact, now));
    }

    [Fact]
    public void ClipboardPrivacyPause_TooltipText_PhrasesItInRussian()
    {
        Assert.Equal("NotifyIsland", ClipboardPrivacyPause.TooltipText(0));
        Assert.Equal("NotifyIsland — пауза 1 мин", ClipboardPrivacyPause.TooltipText(1));
        Assert.Equal("NotifyIsland — пауза 17 мин", ClipboardPrivacyPause.TooltipText(17));
    }

    // --- 1.13: ball-preview cycle helper (wheel on the ball) -----------------------

    [Fact]
    public void BallPreviewCycle_Step_WrapsAroundAtBothEnds()
    {
        // History of 5 items: indices 0..4. The spec is literal — from index 0 (newest) the
        // FIRST wheel-down (delta=+1) jumps to index 4 (the oldest, N-1); each subsequent
        // wheel-down walks one step toward newer; wheel-up is the mirror.
        Assert.Equal(4, BallPreviewCycle.Step(0, +1, 5)); // newest → oldest in one notch
        Assert.Equal(3, BallPreviewCycle.Step(4, +1, 5)); // oldest → N-2
        Assert.Equal(0, BallPreviewCycle.Step(1, +1, 5)); // wrap forward: 1 → 0 (newest)
        Assert.Equal(0, BallPreviewCycle.Step(4, -1, 5)); // oldest → newest (mirror jump)
        Assert.Equal(1, BallPreviewCycle.Step(0, -1, 5)); // newest → 1 (one older)
        // Multi-notch deltas: each notch is one step. 4 notches from newest = N-4; the 5th
        // notch wraps fully back to the head.
        Assert.Equal(1, BallPreviewCycle.Step(0, +4, 5)); // 4 notches back = N-4 = 1
        Assert.Equal(0, BallPreviewCycle.Step(0, +5, 5)); // 5 notches = one full circle = 0
        Assert.Equal(4, BallPreviewCycle.Step(0, +6, 5)); // 6 notches = one past = oldest
    }

    [Fact]
    public void BallPreviewCycle_Step_EmptyHistory_ReturnsZero()
    {
        Assert.Equal(0, BallPreviewCycle.Step(0, +1, 0));
        Assert.Equal(0, BallPreviewCycle.Step(5, -1, 0));
    }

    [Fact]
    public void BallPreviewCycle_Resolve_PicksByIndexInNewestFirst()
    {
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromText("a", T(0)));
        h.Push(ClipboardEntry.FromText("b", T(1)));
        h.Push(ClipboardEntry.FromText("c", T(2)));
        var snap = h.SnapshotNewestFirst();
        Assert.Equal("c", BallPreviewCycle.Resolve(snap, 0)!.Text);
        Assert.Equal("b", BallPreviewCycle.Resolve(snap, 1)!.Text);
        Assert.Equal("a", BallPreviewCycle.Resolve(snap, 2)!.Text);
        // Wrap-around safety: a stale index from a previous (longer) history.
        Assert.Equal("c", BallPreviewCycle.Resolve(snap, 3)!.Text);
        Assert.Equal("b", BallPreviewCycle.Resolve(snap, 4)!.Text);
    }

    [Fact]
    public void BallPreviewCycle_ResetToNewestOnCapture()
    {
        // When a new capture lands, the wheel-on-ball state goes back to the newest entry —
        // the freshly-copied value. This is a convention the Av window owns; the Core helper
        // is just the resolution rule.
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromText("a", T(0)));
        h.Push(ClipboardEntry.FromText("b", T(1)));
        var idx = BallPreviewCycle.Step(0, -1, 2); // user wheeled down → index 1 = "a"
        Assert.Equal(1, idx);
        h.Push(ClipboardEntry.FromText("c", T(2))); // new capture → index resets to 0
        idx = 0;
        Assert.Equal("c", BallPreviewCycle.Resolve(h.SnapshotNewestFirst(), idx)!.Text);
    }

    [Fact]
    public void Push_IgnoresNoneKind()
    {
        var h = new ClipboardHistory();
        h.Push(new ClipboardEntry { Kind = ClipboardItemKind.None });
        Assert.Equal(0, h.Count);
    }

    [Fact]
    public void PopLatest_DropsLast()
    {
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromText("a", T(0)));
        h.Push(ClipboardEntry.FromText("b", T(1)));
        h.PopLatest();
        Assert.Equal("a", h.Latest!.Text);
        h.PopLatest();
        Assert.Null(h.Latest);
    }

    [Fact]
    public void Clear_WipesAll()
    {
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromText("x", T(0)));
        h.Push(ClipboardEntry.FromText("y", T(1)));
        h.Clear();
        Assert.Equal(0, h.Count);
    }

    [Fact]
    public void SnapshotNewestFirst_ReversesOrder()
    {
        var h = new ClipboardHistory();
        for (var i = 0; i < 5; i++)
            h.Push(ClipboardEntry.FromText($"item{i}", T(i)));
        var snap = h.SnapshotNewestFirst();
        Assert.Equal(5, snap.Count);
        Assert.Equal("item4", snap[0].Text);
        Assert.Equal("item3", snap[1].Text);
        Assert.Equal("item0", snap[4].Text);
    }

    [Fact]
    public void SnapshotNewestFirst_Empty_ReturnsEmpty()
    {
        var h = new ClipboardHistory();
        Assert.Empty(h.SnapshotNewestFirst());
    }

    [Fact]
    public void BuildPayload_Text_TruncatesTitleAndBody()
    {
        var entry = ClipboardEntry.FromText(new string('x', 500), T(0));
        var p = ClipboardHistory.BuildPayload(entry, T(0));
        Assert.Equal(ClipboardItemKind.Text, p.ClipboardItemKind);
        Assert.Equal(ClipboardHistory.TitlePreviewChars, p.Title.Length);
        Assert.Equal(ClipboardHistory.BodyPreviewChars, p.Body.Length);
        Assert.Equal("Текст", p.Subtitle);
        Assert.Equal(T(0), p.ClipboardCapturedAt);
    }

    [Fact]
    public void BuildPayload_File_ShowsFileName()
    {
        var entry = ClipboardEntry.FromFile(@"C:\Users\serjo\report.pdf", T(0));
        var p = ClipboardHistory.BuildPayload(entry, T(0));
        Assert.Equal(ClipboardItemKind.File, p.ClipboardItemKind);
        Assert.Equal("report.pdf", p.Title);
        Assert.Equal("Файл", p.Subtitle);
        Assert.Equal(@"C:\Users\serjo\report.pdf", p.Body);
        Assert.Equal(new[] { @"C:\Users\serjo\report.pdf" }, p.ClipboardPaths);
    }

    [Fact]
    public void BuildPayload_MultiFile_CountsFilesInRussianPlural()
    {
        // Cases: 1 файл, 2 файла, 5 файлов, 21 файл, 22 файла, 25 файлов
        Assert.Equal("1 файл", ClipboardHistory.BuildPayload(ClipboardEntry.FromFiles(new List<string> { "a" }, T(0)), T(0)).Title);
        Assert.Equal("2 файла", ClipboardHistory.BuildPayload(ClipboardEntry.FromFiles(new List<string> { "a", "b" }, T(0)), T(0)).Title);
        Assert.Equal("5 файлов", ClipboardHistory.BuildPayload(ClipboardEntry.FromFiles(EnumerableRange(5), T(0)), T(0)).Title);
        Assert.Equal("11 файлов", ClipboardHistory.BuildPayload(ClipboardEntry.FromFiles(EnumerableRange(11), T(0)), T(0)).Title);
        Assert.Equal("21 файл", ClipboardHistory.BuildPayload(ClipboardEntry.FromFiles(EnumerableRange(21), T(0)), T(0)).Title);
        Assert.Equal("22 файла", ClipboardHistory.BuildPayload(ClipboardEntry.FromFiles(EnumerableRange(22), T(0)), T(0)).Title);
        Assert.Equal("25 файлов", ClipboardHistory.BuildPayload(ClipboardEntry.FromFiles(EnumerableRange(25), T(0)), T(0)).Title);
    }

    private static List<string> EnumerableRange(int n)
    {
        var list = new List<string>(n);
        for (var i = 0; i < n; i++) list.Add($"f{i}");
        return list;
    }

    [Fact]
    public void BuildPayload_SingleFile_InferredAsFileNotMulti()
    {
        var p = ClipboardHistory.BuildPayload(
            new ClipboardEntry { Kind = ClipboardItemKind.None, Paths = new List<string> { @"d:\x.txt" } },
            T(0));
        // Sanitize promotes None + single path → File.
        var sanitized = OverlayMachine.Sanitize(p);
        Assert.Equal(ClipboardItemKind.File, sanitized.ClipboardItemKind);
    }

    [Fact]
    public void BuildPayload_DefaultCapturedAt_UsesNow()
    {
        var entry = ClipboardEntry.FromText("x", default);
        var now = T(99);
        var p = ClipboardHistory.BuildPayload(entry, now);
        Assert.Equal(now, p.ClipboardCapturedAt);
    }

    [Fact]
    public void ClipboardEntry_Equality_DistinguishesText()
    {
        var a = ClipboardEntry.FromText("hi", T(0));
        var b = ClipboardEntry.FromText("hi", T(0));
        var c = ClipboardEntry.FromText("bye", T(0));
        Assert.True(a.Equals(b));
        Assert.False(a.Equals(c));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ClipboardEntry_Equality_DistinguishesFilePaths()
    {
        var a = ClipboardEntry.FromFile(@"C:\a.txt", T(0));
        var b = ClipboardEntry.FromFile(@"C:\b.txt", T(0));
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void ClipboardEntry_Equality_DistinguishesMultiFileOrder()
    {
        var a = ClipboardEntry.FromFiles(new List<string> { "x", "y" }, T(0));
        var b = ClipboardEntry.FromFiles(new List<string> { "y", "x" }, T(0));
        Assert.False(a.Equals(b));
    }
}
