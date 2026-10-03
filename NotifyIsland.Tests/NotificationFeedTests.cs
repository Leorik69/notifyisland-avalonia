using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// The Windows notification listener is a poll, not an event stream: every poll re-reports every
/// toast still in the notification centre. These pin the bookkeeping that keeps the island from
/// announcing the same toast every second, plus the two filters and the payload mapping.
/// </summary>
public class NotificationFeedTests
{
    private static IncomingToast Toast(string id, string app = "Slack", string title = "Message",
        string body = "text", bool isSilent = false)
        => new(id, app, title, body, isSilent);

    [Fact]
    public void Accept_FirstSightingIsAccepted()
    {
        var feed = new NotificationFeed();
        Assert.Equal(FeedVerdict.Accepted, feed.Accept(Toast("1")));
    }

    [Fact]
    public void Accept_SameToastOnEveryPollIsOnlyAcceptedOnce()
    {
        // This is the whole point of the class: the notification centre hands back live toasts
        // on every single poll, so without the seen-set one toast would re-announce forever.
        var feed = new NotificationFeed();
        Assert.Equal(FeedVerdict.Accepted, feed.Accept(Toast("1")));
        for (var i = 0; i < 10; i++)
            Assert.Equal(FeedVerdict.Duplicate, feed.Accept(Toast("1")));
    }

    [Fact]
    public void Accept_DifferentIdsAreAllAccepted()
    {
        var feed = new NotificationFeed();
        Assert.Equal(FeedVerdict.Accepted, feed.Accept(Toast("1")));
        Assert.Equal(FeedVerdict.Accepted, feed.Accept(Toast("2")));
        Assert.Equal(FeedVerdict.Duplicate, feed.Accept(Toast("1")));
        Assert.Equal(FeedVerdict.Accepted, feed.Accept(Toast("3")));
    }

    [Fact]
    public void Accept_OwnAppIsDropped()
    {
        var feed = new NotificationFeed(new[] { "NotifyIsland" });
        Assert.Equal(FeedVerdict.OwnApp, feed.Accept(Toast("1", app: "NotifyIsland")));
    }

    [Fact]
    public void Accept_OwnAppMatchIgnoresCase()
    {
        var feed = new NotificationFeed(new[] { "notifyisland" });
        Assert.Equal(FeedVerdict.OwnApp, feed.Accept(Toast("1", app: "NotifyIsland")));
    }

    [Fact]
    public void Accept_OwnAppIsNotReconsideredOnTheNextPoll()
    {
        var feed = new NotificationFeed(new[] { "NotifyIsland" });
        feed.Accept(Toast("1", app: "NotifyIsland"));
        Assert.Equal(FeedVerdict.Duplicate, feed.Accept(Toast("1", app: "NotifyIsland")));
    }

    [Fact]
    public void Accept_SilentToastIsDropped()
    {
        var feed = new NotificationFeed();
        Assert.Equal(FeedVerdict.Empty, feed.Accept(Toast("1", isSilent: true)));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "\t")]
    public void Accept_ToastWithoutAnyTextIsDropped(string title, string body)
    {
        var feed = new NotificationFeed();
        Assert.Equal(FeedVerdict.Empty, feed.Accept(Toast("1", title: title, body: body)));
    }

    [Fact]
    public void Accept_BodyAloneIsEnoughToShow()
    {
        var feed = new NotificationFeed();
        Assert.Equal(FeedVerdict.Accepted, feed.Accept(Toast("1", title: "", body: "just body")));
    }

    [Fact]
    public void Accept_HeadlineAloneIsEnoughToShow()
    {
        var feed = new NotificationFeed();
        Assert.Equal(FeedVerdict.Accepted, feed.Accept(Toast("1", title: "just headline", body: "")));
    }

    [Fact]
    public void RememberedCount_IsBounded()
    {
        // A toast that scrolls out of the notification centre must stop being remembered, or a
        // session that runs for days grows this set without limit.
        var feed = new NotificationFeed(maxRemembered: 4);
        for (var i = 0; i < 50; i++) feed.Accept(Toast($"id{i}"));
        Assert.Equal(4, feed.RememberedCount);
    }

    [Fact]
    public void RememberedCount_EvictsTheOldestFirst()
    {
        var feed = new NotificationFeed(maxRemembered: 2);
        feed.Accept(Toast("a"));
        feed.Accept(Toast("b"));
        feed.Accept(Toast("c"));
        // "a" fell out of the window, so a late re-report of it is new again. That is the
        // intended trade: bounded memory over perfect suppression of a toast nobody can see.
        Assert.Equal(FeedVerdict.Accepted, feed.Accept(Toast("a")));
        Assert.Equal(FeedVerdict.Duplicate, feed.Accept(Toast("c")));
    }

    [Fact]
    public void Accept_ReReportedToastDoesNotGrowTheSet()
    {
        var feed = new NotificationFeed();
        feed.Accept(Toast("1"));
        for (var i = 0; i < 50; i++) feed.Accept(Toast("1"));
        Assert.Equal(1, feed.RememberedCount);
    }

    [Fact]
    public void Accept_EmptyIdIsStillRememberedSoItCannotLoop()
    {
        // A malformed notification with no id must not become a per-poll ghost.
        var feed = new NotificationFeed();
        feed.Accept(Toast("", body: "x"));
        Assert.Equal(FeedVerdict.Duplicate, feed.Accept(Toast("", body: "x")));
    }

    [Fact]
    public void ToPayload_AppNameIsTheHeadlineAndToastTextIsTheBody()
    {
        var payload = NotificationFeed.ToPayload(Toast("1", app: "Slack", title: "Ivan", body: "meeting at 10"));
        Assert.Equal("Slack", payload.Title);
        Assert.Equal("Ivan", payload.Subtitle);
        Assert.Equal("meeting at 10", payload.Body);
    }

    [Fact]
    public void ToPayload_WithoutAppNameTheHeadlineBecomesTheTitle()
    {
        var payload = NotificationFeed.ToPayload(Toast("1", app: "", title: "Backup finished", body: ""));
        Assert.Equal("Backup finished", payload.Title);
    }

    [Fact]
    public void ToPayload_WithoutAppNameTheHeadlineIsNotRepeated()
    {
        var payload = NotificationFeed.ToPayload(Toast("1", app: "", title: "Backup finished", body: "3 files"));
        Assert.Equal("Backup finished", payload.Title);
        Assert.Equal("", payload.Subtitle);
        Assert.Equal("3 files", payload.Body);
    }

    [Fact]
    public void ToPayload_TrimsSurroundingWhitespace()
    {
        var payload = NotificationFeed.ToPayload(Toast("1", app: "  Slack  ", title: " Ivan ", body: " hi "));
        Assert.Equal("Slack", payload.Title);
        Assert.Equal("Ivan", payload.Subtitle);
        Assert.Equal("hi", payload.Body);
    }

    [Fact]
    public void ToPayload_ProgressIsNotBorrowedForNotifications()
    {
        // A toast has no fraction. Reusing the battery's Progress field would show a bar that
        // means nothing, so the mapping leaves it at zero.
        var payload = NotificationFeed.ToPayload(Toast("1"));
        Assert.Equal(0, payload.Progress);
    }

    [Fact]
    public void Remember_MarksSeenWithoutAnnouncing()
    {
        // The priming path: the first poll returns the whole backlog, which is history, not news.
        // Priming spends those ids so the user's existing notifications do not all land on the
        // capsule seconds after launch.
        var feed = new NotificationFeed();
        feed.Remember(Toast("1"));
        feed.Remember(Toast("2"));
        Assert.Equal(2, feed.RememberedCount);
        Assert.Equal(FeedVerdict.Duplicate, feed.Accept(Toast("1")));
        Assert.Equal(FeedVerdict.Duplicate, feed.Accept(Toast("2")));
    }

    [Fact]
    public void Remember_ThenANewToastIsStillAccepted()
    {
        var feed = new NotificationFeed();
        feed.Remember(Toast("old"));
        Assert.Equal(FeedVerdict.Accepted, feed.Accept(Toast("new")));
    }

    [Fact]
    public void Remember_IsIdempotent()
    {
        var feed = new NotificationFeed();
        feed.Remember(Toast("1"));
        feed.Remember(Toast("1"));
        feed.Remember(Toast("1"));
        Assert.Equal(1, feed.RememberedCount);
    }

    [Fact]
    public void Remember_RespectsTheCap()
    {
        var feed = new NotificationFeed(maxRemembered: 2);
        feed.Remember(Toast("a"));
        feed.Remember(Toast("b"));
        feed.Remember(Toast("c"));
        Assert.Equal(2, feed.RememberedCount);
    }

    [Fact]
    public void IgnoreApps_WorksAfterConstruction()
    {
        // The caller learns its own package identity only after the platform has told it, so the
        // own-app set cannot be passed in the constructor alone.
        var feed = new NotificationFeed();
        Assert.Equal(FeedVerdict.Accepted, feed.Accept(Toast("1", app: "NotifyIsland")));
        feed.IgnoreApps("NotifyIsland");
        Assert.Equal(FeedVerdict.OwnApp, feed.Accept(Toast("2", app: "NotifyIsland")));
    }

    [Fact]
    public void IgnoreApps_IgnoresBlanks()
    {
        var feed = new NotificationFeed();
        feed.IgnoreApps("", "   ");
        // A blank key must not become a filter that drops every nameless toast.
        Assert.Equal(FeedVerdict.Accepted, feed.Accept(Toast("1", app: "")));
    }
}
