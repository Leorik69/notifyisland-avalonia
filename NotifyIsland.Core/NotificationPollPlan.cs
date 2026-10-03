namespace NotifyIsland;

/// <summary>
/// How long the notification listener waits before reading the notification centre again.
/// <para>
/// Measured 2026-10-02: one <c>GetNotificationsAsync</c> call costs 80-90 ms of CPU in this
/// process with 88 toasts in the centre, and that cost is paid by the call itself, not by
/// walking the result (Count alone measured the same). A 2 s poll therefore burned ~4.5% of a
/// core around the clock just to learn that nothing had changed.
/// </para>
/// <para>
/// Windows publishes a state-change signal the moment a toast is shown
/// (<c>WNF_SHEL_TOAST_PUBLISHED</c>; measured ~40 ms after <c>Show()</c>, and the new toast is
/// already in the listing by then). When that signal is live the listener reads on the signal
/// and only falls back to a slow safety poll. When it is not available — the signal is an
/// undocumented OS facility — the original 2 s poll is kept, so the feature never gets worse
/// than it was.
/// </para>
/// </summary>
public static class NotificationPollPlan
{
    /// <summary>The poll interval when no publish signal is available (the pre-signal behaviour).</summary>
    public const int PollMs = 2_000;

    /// <summary>Safety read while the publish signal is live: covers a missed signal and keeps the
    /// "already seen" set trimmed as toasts are dismissed. Nothing user-visible waits on it.</summary>
    public const int SignalFallbackPollMs = 30_000;

    /// <summary>After a signal, wait this long before reading, so a burst of toasts (an app that
    /// posts several at once) costs one read instead of one per toast.</summary>
    public const int SignalCoalesceMs = 120;

    /// <summary>The wait before the next read.</summary>
    public static int NextWaitMs(bool signalLive) => signalLive ? SignalFallbackPollMs : PollMs;
}