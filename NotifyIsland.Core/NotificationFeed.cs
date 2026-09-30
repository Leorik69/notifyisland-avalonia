namespace NotifyIsland;

/// <summary>One toast as the feed understands it, free of any Windows type.</summary>
/// <param name="Id">Stable id of the notification in the notification centre.</param>
/// <param name="App">Display name of the sending app.</param>
/// <param name="Title">Headline text, may be empty.</param>
/// <param name="Body">Body text, may be empty.</param>
/// <param name="IsSilent">Silent toasts (reminder style) carry no text worth showing.</param>
public readonly record struct IncomingToast(
    string Id,
    string App,
    string Title,
    string Body,
    bool IsSilent = false);

/// <summary>
/// What the feed decided about one polled toast. <see cref="Accepted"/> is the only case the island
/// acts on; the other three exist so the caller can say WHY a toast was dropped instead of losing
/// it without a trace.
/// </summary>
public enum FeedVerdict
{
    /// <summary>New and showable — hand it to the island.</summary>
    Accepted,
    /// <summary>Already seen: the notification centre re-reports live toasts on every poll.</summary>
    Duplicate,
    /// <summary>Came from NotifyIsland itself.</summary>
    OwnApp,
    /// <summary>Silent, empty, or both — nothing to put on a 30 DIP capsule.</summary>
    Empty,
}

/// <summary>
/// Accepts toasts from a polling source, and decides nothing else.
/// <para>
/// The Windows notification listener is not an event stream: every poll returns the whole set of
/// toasts still in the notification centre, so the same notification comes back again and again
/// until it is dismissed. Without a seen-set the island would re-announce one toast every second
/// — that bookkeeping is the entire reason this class exists. It is deliberately free of any
/// Windows type, so all of it is testable on a machine with no package identity.
/// </para>
/// </summary>
public sealed class NotificationFeed
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly HashSet<string> _ownApps;
    private readonly int _maxRemembered;

    /// <param name="ownApps">App names that are us. Toasts from them are dropped so the island
    /// cannot announce its own state back to itself.</param>
    /// <param name="maxRemembered">How many ids to keep. A toast that scrolls out of the
    /// notification centre must stop being remembered, or a long session would grow this set
    /// forever. The cap is the bound, and it is why the queue is ordered.</param>
    public NotificationFeed(IEnumerable<string>? ownApps = null, int maxRemembered = 512)
    {
        _ownApps = new HashSet<string>(ownApps ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        _maxRemembered = Math.Max(1, maxRemembered);
    }

    /// <summary>Ids currently remembered. Exposed for tests and diagnostics.</summary>
    public int RememberedCount => _seen.Count;

    /// <summary>
    /// Decide what to do with one polled toast. The id is remembered for EVERY verdict, including
    /// the rejected ones: a silent or self-sent toast must not be re-examined on the next poll
    /// either, and re-evaluating it is how a filter turns into a per-poll cost.
    /// </summary>
    public FeedVerdict Accept(IncomingToast toast)
    {
        var id = (toast.Id ?? "").Trim();
        if (!_seen.Add(id)) return FeedVerdict.Duplicate;
        _order.Enqueue(id);
        Trim();

        if (_ownApps.Contains(toast.App ?? "")) return FeedVerdict.OwnApp;
        if (toast.IsSilent) return FeedVerdict.Empty;
        if (string.IsNullOrWhiteSpace(toast.Title) && string.IsNullOrWhiteSpace(toast.Body))
            return FeedVerdict.Empty;
        return FeedVerdict.Accepted;
    }

    /// <summary>
    /// The payload the island shows: the app name is the headline and the toast text is the body,
    /// which is the order Stage 8's row reads best in. A toast with no app name falls back to the
    /// headline, so the row is never nameless.
    /// </summary>
    public static OverlayPayload ToPayload(IncomingToast toast)
    {
        var app = (toast.App ?? "").Trim();
        var headline = (toast.Title ?? "").Trim();
        var body = (toast.Body ?? "").Trim();
        return new OverlayPayload
        {
            Title = string.IsNullOrEmpty(app) ? headline : app,
            Subtitle = headline,
            Body = body,
        };
    }

    private void Trim()
    {
        while (_order.Count > _maxRemembered)
        {
            var oldest = _order.Dequeue();
            _seen.Remove(oldest);
        }
    }
}
