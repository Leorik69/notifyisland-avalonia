using Xunit;

namespace NotifyIsland.Tests;

public class NotificationPollPlanTests
{
    [Fact]
    public void Without_signal_keeps_the_original_two_second_poll()
    {
        Assert.Equal(2_000, NotificationPollPlan.PollMs);
        Assert.Equal(NotificationPollPlan.PollMs, NotificationPollPlan.NextWaitMs(signalLive: false));
    }

    [Fact]
    public void With_signal_only_a_slow_safety_poll_remains()
    {
        var wait = NotificationPollPlan.NextWaitMs(signalLive: true);
        Assert.Equal(NotificationPollPlan.SignalFallbackPollMs, wait);
        // The whole point of the signal: the safety read must be far rarer than the plain poll.
        Assert.True(wait >= 10 * NotificationPollPlan.PollMs);
    }

    [Fact]
    public void Coalesce_window_is_short_enough_to_feel_instant()
    {
        Assert.InRange(NotificationPollPlan.SignalCoalesceMs, 1, 250);
        // A signal-driven read must still beat the old poll's worst case by a wide margin.
        Assert.True(NotificationPollPlan.SignalCoalesceMs * 4 < NotificationPollPlan.PollMs);
    }
}