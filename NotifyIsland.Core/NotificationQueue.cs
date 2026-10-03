namespace NotifyIsland;

/// <summary>
/// Notifications waiting behind the one currently on the capsule.
/// <para>
/// The machine shows the NEWEST arrival and used to simply overwrite: a second toast arriving
/// during the first one's four seconds took the capsule, the first was never seen again, and the
/// unread badge was the only trace that anything had been lost. So the displaced notification is
/// kept here and replayed in order when the capsule frees up — newest first on screen, then the
/// rest, oldest last.
/// </para>
/// <para>
/// Bounded on purpose. A chatty app can emit a burst, and a queue that only grows is a slow leak
/// in an app that is meant to run all day. Past the cap the OLDEST waiting item is dropped,
/// because the newest is the one the user most likely still needs.
/// </para>
/// </summary>
public sealed class NotificationQueue
{
    /// <summary>How many notifications may wait behind the current one.</summary>
    public const int Capacity = 20;

    private readonly List<OverlayPayload> _pending = new(Capacity);

    /// <summary>How many notifications are waiting.</summary>
    public int Count => _pending.Count;

    /// <summary>True when nothing is waiting.</summary>
    public bool IsEmpty => _pending.Count == 0;

    /// <summary>
    /// Add a displaced notification. The payload is stored by reference, like every other place
    /// the machine keeps one — the caller must not keep mutating it.
    /// </summary>
    public void Push(OverlayPayload payload)
    {
        if (payload is null) return;
        if (_pending.Count >= Capacity)
            _pending.RemoveAt(0);   // oldest waiting goes first
        _pending.Add(payload);
    }

    /// <summary>The next notification to show, or null when the queue is empty.</summary>
    public OverlayPayload? Peek() => _pending.Count == 0 ? null : _pending[0];

    /// <summary>
    /// Take the next notification, or null when the queue is empty. This is what the machine
    /// calls when the current notification's time runs out.
    /// </summary>
    public OverlayPayload? Dequeue()
    {
        if (_pending.Count == 0) return null;
        var next = _pending[0];
        _pending.RemoveAt(0);
        return next;
    }

    /// <summary>Forget everything waiting. Used by Clear.</summary>
    public void Clear() => _pending.Clear();

    /// <summary>
    /// "3 of 7" style position for the badge: 1 when a single notification is on screen, and the
    /// position of the one being shown within the run of notifications still to come.
    /// </summary>
    public string ProgressText(int shown) =>
        _pending.Count == 0 ? $"{shown}" : $"{shown}/{shown + _pending.Count}";
}
