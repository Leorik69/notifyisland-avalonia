using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// A second notification used to overwrite the one on screen: the first was never seen again and
/// the unread badge was the only trace. These pin the replay behaviour that replaced it.
/// </summary>
public class OverlayMachineNotificationQueueTests
{
    private static OverlayPayload P(string title, string body = "") =>
        new() { Title = title, Body = body };

    private static void Expire(OverlayMachine m) => m.Tick(OverlayTokens.DefaultNotifyMs + 1);

    [Fact]
    public void OneNotification_ShowsAndThenReturnsToRest()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.Notify, P("first"));

        Assert.Equal(OverlayKind.Notification, m.Snapshot().Kind);
        Assert.Equal(0, m.PendingNotifications);
        Assert.Equal(string.Empty, m.NotificationProgress);

        Expire(m);
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);
    }

    [Fact]
    public void SecondNotification_TakesTheCapsuleAndTheFirstWaits()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.Notify, P("first"));
        m.Dispatch(OverlayCommand.Notify, P("second"));

        // The newest wins the capsule — that is what the user most likely still needs.
        Assert.Equal("second", m.Snapshot().Payload.Title);
        Assert.Equal(1, m.PendingNotifications);
        Assert.Equal("1/2", m.NotificationProgress);
    }

    [Fact]
    public void WhenTheFirstExpires_TheWaitingOneIsReplayed()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.Notify, P("first"));
        m.Dispatch(OverlayCommand.Notify, P("second"));

        Expire(m);

        var snap = m.Snapshot();
        Assert.Equal(OverlayKind.Notification, snap.Kind);
        Assert.Equal("first", snap.Payload.Title);   // the displaced one, in order
        Assert.Equal(0, m.PendingNotifications);
        Assert.Equal("2/2", m.NotificationProgress);
    }

    [Fact]
    public void ThreeReplayedInArrivalOrderOldestFirst()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.Notify, P("one"));
        m.Dispatch(OverlayCommand.Notify, P("two"));
        m.Dispatch(OverlayCommand.Notify, P("three"));

        // The newest took the capsule; the two it displaced wait behind it.
        Assert.Equal("three", m.Snapshot().Payload.Title);
        Assert.Equal(2, m.PendingNotifications);

        // Replay is oldest-first among the WAITING ones: one, then two.
        Expire(m);
        Assert.Equal("one", m.Snapshot().Payload.Title);
        Expire(m);
        Assert.Equal("two", m.Snapshot().Payload.Title);
        Expire(m);
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);
    }

    [Fact]
    public void Replay_DoesNotBumpUnreadAgain()
    {
        // Each arrival counted once when it arrived. Showing an old one again is not news.
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.Notify, P("one"));
        m.Dispatch(OverlayCommand.Notify, P("two"));
        Assert.Equal(2, m.UnreadCount);

        Expire(m);
        Assert.Equal(2, m.UnreadCount);
    }

    [Fact]
    public void Replay_GivesTheNextOneAFullLifetimeNotTheRemainder()
    {
        // If the queue entry inherited the expired timer it would flash past unread. It has to
        // start its own budget, or the whole point of replaying it is lost.
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.Notify, P("one"));
        m.Dispatch(OverlayCommand.Notify, P("two"));
        Expire(m);

        var replayed = m.Snapshot();
        Assert.Equal("one", replayed.Payload.Title);
        Assert.Equal(OverlayTokens.DefaultNotifyMs, replayed.NotifyMsLeft);

        // Half of it goes, and it is still on screen.
        m.Tick(OverlayTokens.DefaultNotifyMs / 2);
        Assert.Equal("one", m.Snapshot().Payload.Title);
    }

    [Fact]
    public void ANotificationArrivingAfterTheRunEndedStartsAFreshRun()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.Notify, P("one"));
        m.Dispatch(OverlayCommand.Notify, P("two"));
        Expire(m);
        Expire(m);
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);

        m.Dispatch(OverlayCommand.Notify, P("later"));
        // No "3/3": the run is over, this is a new one on its own.
        Assert.Equal(string.Empty, m.NotificationProgress);
        Assert.Equal(0, m.PendingNotifications);
    }

    [Fact]
    public void Clear_DropsTheWholeRunNotJustTheOneOnScreen()
    {
        // "Clear" clearing only what is visible would leave the queue replaying into an island
        // the user just emptied.
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.Notify, P("one"));
        m.Dispatch(OverlayCommand.Notify, P("two"));
        Assert.Equal(1, m.PendingNotifications);

        m.Dispatch(OverlayCommand.Clear);
        Assert.Equal(0, m.PendingNotifications);
        Assert.Equal(0, m.UnreadCount);
        Assert.Equal(string.Empty, m.NotificationProgress);
    }

    [Fact]
    public void AToastOverSomethingTheUserOpenedStillDoesNotTakeTheScreen()
    {
        // 1.13's real guarantee has to survive the queue: a toast arriving while the stats panel
        // is open updates the payload and the unread count without stealing the capsule.
        var m = new OverlayMachine { WeatherEnabled = true };
        m.Dispatch(OverlayCommand.SetWeather, WeatherCodes.ToPayload(0, 0, 0));
        Assert.Equal(OverlayKind.Weather, m.Snapshot().Kind);

        m.Dispatch(OverlayCommand.Notify, P("toast"));
        Assert.Equal(OverlayKind.Weather, m.Snapshot().Kind);
        Assert.Equal(1, m.UnreadCount);
    }

    [Fact]
    public void ABurstLargerThanTheQueueKeepsItBounded()
    {
        var m = new OverlayMachine();
        for (var i = 0; i < NotificationQueue.Capacity + 10; i++)
            m.Dispatch(OverlayCommand.Notify, P($"n{i}"));

        Assert.Equal(NotificationQueue.Capacity, m.PendingNotifications);
    }
}
