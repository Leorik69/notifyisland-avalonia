namespace NotifyIsland;

/// <summary>
/// Platform-agnostic source of <see cref="SystemSnapshot"/> samples.
/// Implemented once for Windows; faked in tests.
/// </summary>
public interface ISystemMonitorSource : IDisposable
{
    /// <summary>Fires on the source's own timer thread. Consumers must marshal to the UI thread themselves.</summary>
    event Action<SystemSnapshot>? SnapshotChanged;

    SystemSnapshot Current { get; }

    void Start();
    void Stop();

    /// <summary>Re-configures the sampling period in place, without restarting the timer.</summary>
    void SetInterval(TimeSpan period);

    /// <summary>Re-configures the network-interface filter in place.</summary>
    void SetIncludeAllInterfaces(bool includeAll);
}
