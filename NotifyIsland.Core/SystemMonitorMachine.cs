namespace NotifyIsland;

/// <summary>
/// Facade over an <see cref="ISystemMonitorSource"/>. Holds the latest snapshot and re-raises
/// the source's event. Deliberately does no marshalling — the AV layer owns the
/// Dispatcher.UIThread.Post hop.
/// </summary>
public sealed class SystemMonitorMachine : IDisposable
{
    private readonly ISystemMonitorSource _source;
    private volatile SystemSnapshot _snapshot = SystemSnapshot.Empty;

    /// <summary>The most recent snapshot pushed by the source; <see cref="SystemSnapshot.Empty"/> until the first one arrives.</summary>
    public SystemSnapshot Snapshot => _snapshot;

    /// <summary>Raised once per source snapshot, on the source's timer thread — not the UI thread.</summary>
    public event Action<SystemSnapshot>? OnSnapshot;

    public SystemMonitorMachine(ISystemMonitorSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _source.SnapshotChanged += OnSourceSnapshot;
    }

    private void OnSourceSnapshot(SystemSnapshot s)
    {
        _snapshot = s;
        OnSnapshot?.Invoke(s);
    }

    public void Start() => _source.Start();

    public void Stop() => _source.Stop();

    public void SetInterval(TimeSpan period) => _source.SetInterval(period);

    public void SetIncludeAllInterfaces(bool includeAll) => _source.SetIncludeAllInterfaces(includeAll);

    public void Dispose()
    {
        _source.SnapshotChanged -= OnSourceSnapshot;
        _source.Dispose();
        OnSnapshot = null;
    }
}
