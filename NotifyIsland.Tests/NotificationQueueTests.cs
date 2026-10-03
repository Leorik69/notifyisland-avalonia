using Xunit;

namespace NotifyIsland.Tests;

public class NotificationQueueTests
{
    private static OverlayPayload P(string title) => new() { Title = title };

    [Fact]
    public void NewQueue_IsEmpty()
    {
        var q = new NotificationQueue();
        Assert.True(q.IsEmpty);
        Assert.Equal(0, q.Count);
        Assert.Null(q.Peek());
        Assert.Null(q.Dequeue());
    }

    [Fact]
    public void Dequeue_ReturnsInArrivalOrderOldestFirst()
    {
        var q = new NotificationQueue();
        q.Push(P("first"));
        q.Push(P("second"));

        Assert.Equal("first", q.Dequeue()!.Title);
        Assert.Equal("second", q.Dequeue()!.Title);
        Assert.True(q.IsEmpty);
        Assert.Null(q.Dequeue());
    }

    [Fact]
    public void Peek_DoesNotConsume()
    {
        var q = new NotificationQueue();
        q.Push(P("first"));
        Assert.Equal("first", q.Peek()!.Title);
        Assert.Equal("first", q.Peek()!.Title);
        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void Push_NullIsIgnored()
    {
        var q = new NotificationQueue();
        q.Push(null!);
        Assert.True(q.IsEmpty);
    }

    [Fact]
    public void Push_PastCapacity_DropsTheOldestWaiting()
    {
        // The bug this prevents is a slow leak: a chatty app in a long session would grow this
        // without bound, and the oldest is the one the user is least likely to still want.
        var q = new NotificationQueue();
        for (var i = 0; i < NotificationQueue.Capacity + 5; i++)
            q.Push(P($"n{i}"));

        Assert.Equal(NotificationQueue.Capacity, q.Count);
        Assert.Equal("n5", q.Peek()!.Title);   // the five oldest went
    }

    [Fact]
    public void Clear_DropsEverythingWaiting()
    {
        var q = new NotificationQueue();
        q.Push(P("a"));
        q.Push(P("b"));
        q.Clear();
        Assert.True(q.IsEmpty);
    }

    [Fact]
    public void ProgressText_ShowsPositionWithinTheRun()
    {
        var q = new NotificationQueue();

        // A single notification: no "of" — it would be noise.
        Assert.Equal("1", q.ProgressText(1));

        q.Push(P("a"));
        q.Push(P("b"));
        // Showing the first of three.
        Assert.Equal("1/3", q.ProgressText(1));

        q.Dequeue();
        // Showing the second of three — dequeueing moves us forward, it does not shrink the run.
        Assert.Equal("2/3", q.ProgressText(2));

        q.Dequeue();
        q.Dequeue();
        // Last one on its own: back to a bare number.
        Assert.Equal("3", q.ProgressText(3));
    }
}
