using NotifyIsland;
using Xunit;

namespace NotifyIsland.Tests;

public class RecentWindowsTests
{
    private static RecentWindow Win(nint h, string title = "Doc", string proc = "notepad") =>
        new() { Handle = h, Title = title, ProcessName = proc, LastSeen = DateTimeOffset.UnixEpoch };

    private static DateTimeOffset Now => new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    // -- Filter ------------------------------------------------------------------------------

    [Fact]
    public void A_plain_top_level_window_is_accepted()
    {
        Assert.True(RecentWindowFilter.Accept((nint)42, "NormalWin32", "Untitled",
            RecentWindowFilter.WsVisible | RecentWindowFilter.WsCaption, 0, 100, 1));
    }

    [Fact]
    public void The_islands_own_window_is_never_accepted()
    {
        // A row pointing at the island would make the island close itself when clicked.
        Assert.False(RecentWindowFilter.Accept((nint)42, "NormalWin32", "NotifyIsland",
            RecentWindowFilter.WsVisible | RecentWindowFilter.WsCaption, 0, 7, 7));
    }

    [Fact]
    public void An_invisible_window_is_not_a_window_the_user_can_return_to()
    {
        Assert.False(RecentWindowFilter.Accept((nint)42, "NormalWin32", "Hidden",
            RecentWindowFilter.WsCaption, 0, 100, 1));
    }

    [Fact]
    public void A_child_window_is_rejected()
    {
        Assert.False(RecentWindowFilter.Accept((nint)42, "NormalWin32", "Child",
            RecentWindowFilter.WsVisible | RecentWindowFilter.WsCaption | RecentWindowFilter.WsChild,
            0, 100, 1));
    }

    [Fact]
    public void A_tool_window_is_rejected()
    {
        Assert.False(RecentWindowFilter.Accept((nint)42, "NormalWin32", "Palette",
            RecentWindowFilter.WsVisible | RecentWindowFilter.WsCaption | RecentWindowFilter.WsTool,
            0, 100, 1));
    }

    [Fact]
    public void A_tool_window_exstyle_is_rejected()
    {
        // No taskbar button, so there is nothing the user could recognise either.
        Assert.False(RecentWindowFilter.Accept((nint)42, "NormalWin32", "Flyout",
            RecentWindowFilter.WsVisible | RecentWindowFilter.WsCaption,
            RecentWindowFilter.WsExToolWindow, 100, 1));
    }

    [Fact]
    public void A_window_without_a_caption_bar_is_rejected()
    {
        Assert.False(RecentWindowFilter.Accept((nint)42, "NormalWin32", "NoTitle",
            RecentWindowFilter.WsVisible, 0, 100, 1));
    }

    [Fact]
    public void An_empty_title_is_rejected()
    {
        Assert.False(RecentWindowFilter.Accept((nint)42, "NormalWin32", "   ",
            RecentWindowFilter.WsVisible | RecentWindowFilter.WsCaption, 0, 100, 1));
    }

    [Fact]
    public void A_null_class_name_does_not_throw()
    {
        Assert.True(RecentWindowFilter.Accept((nint)42, null!, "Ok",
            RecentWindowFilter.WsVisible | RecentWindowFilter.WsCaption, 0, 100, 1));
    }

    [Fact]
    public void A_zero_handle_is_rejected()
    {
        Assert.False(RecentWindowFilter.Accept(IntPtr.Zero, "NormalWin32", "Ok",
            RecentWindowFilter.WsVisible | RecentWindowFilter.WsCaption, 0, 100, 1));
    }

    [Fact]
    public void A_title_longer_than_the_cap_is_rejected_outright()
    {
        // A 200-character title is a document path; listing it helps nobody and pushes every
        // other row out of view.
        var long_ = new string('x', RecentWindowFilter.MaxTitleLength + 1);
        Assert.False(RecentWindowFilter.Accept((nint)42, "NormalWin32", long_,
            RecentWindowFilter.WsVisible | RecentWindowFilter.WsCaption, 0, 100, 1));
    }

    [Fact]
    public void The_taskbar_and_the_desktop_are_never_listed()
    {
        foreach (var cls in new[] { "Shell_TrayWnd", "Progman", "WorkerW" })
            Assert.False(RecentWindowFilter.Accept((nint)42, cls, "Windows",
                RecentWindowFilter.WsVisible | RecentWindowFilter.WsCaption, 0, 100, 1), cls);
    }

    // -- Titles ------------------------------------------------------------------------------

    [Fact]
    public void A_title_becomes_one_line()
    {
        Assert.Equal("a b", RecentWindowFilter.TitleFor("a\r\nb"));
    }

    [Fact]
    public void An_over_long_title_is_cut_with_an_ellipsis()
    {
        var t = RecentWindowFilter.TitleFor(new string('x', 500));
        Assert.Equal(RecentWindowFilter.MaxTitleLength, t.Length);
        Assert.EndsWith("…", t);
    }

    [Fact]
    public void A_null_title_is_empty_not_a_crash()
    {
        Assert.Equal("", RecentWindowFilter.TitleFor(null));
    }

    // -- List --------------------------------------------------------------------------------

    [Fact]
    public void The_newest_window_is_first()
    {
        var list = new RecentWindows();
        list.Touch(Win(1, "First"), Now);
        list.Touch(Win(2, "Second"), Now);
        Assert.Equal(2, list.Items[0].Handle);
    }

    [Fact]
    public void Touching_a_known_window_moves_it_rather_than_duplicating_it()
    {
        // This is what makes the list "recent" rather than "every window, in order".
        var list = new RecentWindows();
        list.Touch(Win(1, "A"), Now);
        list.Touch(Win(2, "B"), Now);
        Assert.Equal(2, list.Count);
        list.Touch(Win(1, "A"), Now);
        // Two distinct windows, so the count is unchanged — only the ORDER moves.
        Assert.Equal(2, list.Count);
        Assert.Equal(1, list.Items[0].Handle);
        Assert.Equal(2, list.Items[1].Handle);
    }

    [Fact]
    public void The_list_never_exceeds_its_capacity()
    {
        var list = new RecentWindows();
        for (var i = 1; i <= 30; i++) list.Touch(Win(i), Now);
        Assert.Equal(RecentWindows.Capacity, list.Count);
        Assert.Equal(30, list.Items[0].Handle);
    }

    [Fact]
    public void The_oldest_surviving_entry_is_the_one_dropped()
    {
        var list = new RecentWindows();
        for (var i = 1; i <= 12; i++) list.Touch(Win(i), Now);
        // 12 windows touched in order, capacity 8: 1..4 fell off the end.
        Assert.Equal(12, list.Items[0].Handle);
        Assert.Equal(5, list.Items[^1].Handle);
    }

    [Fact]
    public void A_window_older_than_the_retention_is_forgotten()
    {
        var list = new RecentWindows();
        list.Touch(Win(1, "Stale"), Now);
        list.Touch(Win(2, "Fresh"), Now.AddMinutes(RecentWindows.Retention.TotalMinutes + 1));
        Assert.Equal(1, list.Count);
        Assert.Equal(2, list.Items[0].Handle);
    }

    [Fact]
    public void A_dead_handle_is_dropped_on_the_next_update()
    {
        var list = new RecentWindows();
        list.Touch(Win(1, "Closed"), Now);
        list.Touch(Win(2, "Open"), Now);
        list.Touch(Win(3, "New"), Now, isAlive: h => h != 1);
        Assert.DoesNotContain(list.Items, w => w.Handle == 1);
    }

    [Fact]
    public void A_live_handle_survives_the_same_pass()
    {
        var list = new RecentWindows();
        list.Touch(Win(1, "Open"), Now);
        list.Touch(Win(2, "New"), Now, isAlive: _ => true);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void A_zero_handle_is_never_recorded()
    {
        var list = new RecentWindows();
        list.Touch(Win(0), Now);
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void An_empty_list_reports_zero_and_can_be_cleared()
    {
        var list = new RecentWindows();
        Assert.Equal(0, list.Count);
        list.Touch(Win(1), Now);
        list.Clear();
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void The_stored_title_is_the_one_from_the_latest_touch()
    {
        // A window renamed while open must show its current name, not the name it had when first
        // seen.
        var list = new RecentWindows();
        list.Touch(Win(1, "Untitled"), Now);
        list.Touch(Win(1, "Report.docx"), Now);
        Assert.Equal("Report.docx", list.Items[0].Title);
    }
}
