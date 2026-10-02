using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace NotifyIsland;

/// <summary>
/// Polls the Windows notification centre and turns new toasts into <see cref="IncomingToast"/>.
/// <para>
/// The listener is a poll, not an event stream: every poll returns the whole set of toasts still
/// in the centre, so the same notification comes back every second until it is dismissed. The
/// bookkeeping that turns that stream into "new toasts only" lives in <see cref="NotificationFeed"/>
/// (Core, no Windows types, fully tested); this class is only the Windows half.
/// </para>
/// <para>
/// <b>Package identity turned out not to be required.</b> The documented contract says reading
/// other apps' toasts needs a package identity plus the <c>userNotificationListener</c>
/// capability, and a sparse MSIX package was planned for exactly that. Measured on Windows 11
/// (build 26xxx, .NET 10) an <i>unpackaged</i> build reports <c>GetAccessStatus() == Allowed</c>
/// and <c>GetNotificationsAsync</c> returns the centre's real contents with readable text. So
/// the app listens as it ships, and <c>AppInfo</c> is probed only to learn our own display name
/// for the self-filter — a probe that throws COMException when unpackaged, which is expected and
/// not an error.
/// </para>
/// <para>
/// Text is read through <c>NotificationVisual.Bindings[i].GetTextElements()</c>, not through
/// <c>AppNotification</c> — that type is not projected on any TFM (checked on
/// <c>net10.0-windows10.0.26100.0</c>), while the visual/binding path has been there since the
/// first Windows 10 projection. It also happens to be the path that works, which is why the
/// earlier "no text available" conclusion was wrong.
/// </para>
/// </summary>
public sealed class WindowsNotificationSource : IDisposable
{
    /// <summary>How often the notification centre is read.
    /// <para>
    /// 2026-10-02, measured: this interval was the single most expensive thing in the app. The
    /// listener has no delta or "since" query — <c>GetNotificationsAsync</c> marshals the ENTIRE
    /// centre into managed COM wrappers on every call, and the centre only grows while the app
    /// runs (74 toasts after an hour here). A 40 s trace of the idle app put 88% of all CPU in
    /// WinRT marshalling: 47% releasing those wrappers (<c>MarshalInspectable.DisposeAbi</c>),
    /// 25% reading the collection (<c>IEnumerator.get_Current</c>), 16% creating them
    /// (<c>DefaultComWrappers.CreateObject</c>). The per-toast conversion filter from the earlier
    /// pass does not help here: the wrappers are built by the projection before this code ever
    /// sees them, so skipping <c>Convert</c> skips the cheap part.
    /// </para>
    /// <para>
    /// Two seconds halves that cost. The trade is honest and real: a toast can now appear up to
    /// two seconds later than before. That is imperceptible for a toast, and unlike the clipboard
    /// — where the user is waiting on their own Ctrl+C — a notification has no one waiting on the
    /// capsule to react in the same instant.
    /// </para></summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(NotificationPollPlan.PollMs);

    /// <summary>How long one poll may take before it is abandoned. The WinRT call is asynchronous
    /// and a hung shell service would otherwise stall the poll loop indefinitely.
    /// <para>
    /// 2026-10-02: the wait used to be <c>task.Wait(timeout)</c> on a thread pool thread, and a
    /// trace showed the timer callback parked in it for 5.4 s of every 40 s window. Rewriting the
    /// poll as a genuinely asynchronous loop removed that blocked thread — but measured over
    /// three 60 s runs per build, total CPU did not move (17.4% before, 17.7% after, against a
    /// run-to-run spread of about 1.3 points). The blocked thread was mostly idle wait, which is
    /// nearly free; the thread pool's <c>Monitor.PulseAll</c> showing up as 51% of CPU in the
    /// profile is where that idle wait is accounted, not work being done. The rewrite is kept for
    /// the resource behaviour, not for a speed-up it does not deliver.
    /// </para></summary>
    public TimeSpan PollTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Raised for each toast the feed accepted. Runs on the UI thread.</summary>
    public event Action<IncomingToast>? Accepted;

    /// <summary>Access status as the platform last reported it, or null before the first check.</summary>
    public UserNotificationListenerAccessStatus? AccessStatus { get; private set; }

    /// <summary>True once the listener can actually return toasts. False when the platform
    /// reports the access as denied — the one case a user has to act on.</summary>
    public bool IsListening { get; private set; }

    /// <summary>The acceptance policy. Exposed so the window can prime it and add our own app
    /// name to the ignore set once the platform has told us who we are.</summary>
    public NotificationFeed Feed { get; } = new();

    /// <summary>Our own package family name, when we have an identity. Empty otherwise.</summary>
    public string OwnPackageFamilyName { get; private set; } = string.Empty;

    public bool IsRunning { get; private set; }

    private readonly Action<Action> _postToUi;
    private CancellationTokenSource? _stop;
    // Wakes the poll loop early. Released by the toast-published signal and by an access grant;
    // drained after every wake so a burst of signals collapses into one read.
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private ToastPublishedSignal? _signal;
    private bool _primed;              // the first successful poll was spent on priming
    private bool _disposed;
    private bool _firstPollReported;   // say the first successful read once, not every second
    private bool _senderNameReported;  // ditto: a nameless sender is re-read on every poll

    /// <summary>
    /// Ids already converted on a previous poll. Replaced wholesale (not added to) on every poll
    /// from the notifications currently in the action centre, so a dismissed toast stops being
    /// remembered and this cannot grow without bound.
    /// </summary>
    private HashSet<uint> _seenIds = new();

    /// <summary>One-shot logger. A toast stays in the centre for minutes, so the same missing
    /// name would otherwise be reported once per second for as long as it sits there.</summary>
    private void LogSenderNameOnce(string message)
    {
        if (_senderNameReported) return;
        _senderNameReported = true;
        AppLog.Info($"WindowsNotificationSource: {message}");
    }

    public WindowsNotificationSource(Action<Action> postToUi, TimeSpan? pollInterval = null)
    {
        _postToUi = postToUi ?? throw new ArgumentNullException(nameof(postToUi));
        PollInterval = pollInterval ?? DefaultPollInterval;
    }

    public TimeSpan PollInterval { get; private set; }

    /// <summary>
    /// Start polling, and ask for access if we do not have it. Both are idempotent.
    /// <para>
    /// <c>RequestAccessAsync</c> shows a system dialog, so it is posted to the UI thread: a
    /// notification-listener prompt is a shell dialog, not something to raise from a timer thread.
    /// It is only called when the status is not already <c>Allowed</c> — a granted listener stays
    /// granted, and re-prompting every launch is how an app earns a reputation for nagging.
    /// </para>
    /// </summary>
    public void Start()
    {
        if (_disposed || IsRunning) return;
        IsRunning = true;
        TryGetIdentity();
        StartSignal();
        _postToUi(RequestAccess);

        // A sequential async loop, not a repeating timer. The old timer fired on a fixed period
        // and each tick blocked a pool thread for the duration of the WinRT call; the "_polling"
        // guard existed only to stop those blocked ticks from piling up. A loop that awaits its
        // own work cannot pile up by construction, and it holds no thread while it waits.
        _stop?.Dispose();
        _stop = new CancellationTokenSource();
        _ = Task.Run(() => PollLoopAsync(_stop.Token));
    }

    public void Stop()
    {
        IsRunning = false;
        _stop?.Cancel();
        if (_signal is not null)
        {
            _signal.Published -= Wake;
            _signal.Dispose();
            _signal = null;
        }
    }

    /// <summary>
    /// Subscribe to the shell's toast-published signal (see <see cref="ToastPublishedSignal"/>).
    /// Fail-soft: when it is refused the loop simply keeps the plain poll interval.
    /// </summary>
    private void StartSignal()
    {
        if (_signal is not null) return;
        var signal = new ToastPublishedSignal();
        if (!signal.IsLive)
        {
            signal.Dispose();
            return;
        }
        signal.Published += Wake;
        _signal = signal;
        AppLog.Info("WindowsNotificationSource: reading on toast signal, safety poll every " +
                    $"{NotificationPollPlan.SignalFallbackPollMs / 1000} s");
    }

    private void Wake()
    {
        try { _wake.Release(); }
        catch (ObjectDisposedException) { }
        catch (SemaphoreFullException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _stop?.Dispose();
        _wake.Dispose();
    }

    // -- Access ------------------------------------------------------------

    /// <summary>
    /// Read our own identity. With a package identity this is what lets the feed drop our own
    /// toasts; without one there is nothing to record, which is itself the diagnostic.
    /// </summary>
    private void TryGetIdentity()
    {
        try
        {
            var info = Windows.ApplicationModel.AppInfo.Current;
            var name = info?.DisplayInfo?.DisplayName;
            if (info is not null && !string.IsNullOrWhiteSpace(name))
            {
                OwnPackageFamilyName = info.PackageFamilyName ?? string.Empty;
                // Both spellings: the display name is what arrives in a toast, and the family
                // name is what a packaged identity is more likely to be reported under.
                Feed.IgnoreApps(name, "NotifyIsland");
            }
            else
            {
                // Unpackaged: there is no display name and no family name.
                Feed.IgnoreApps("NotifyIsland");
            }
        }
        catch (Exception ex)
        {
            // APPMODEL_ERROR_NO_PACKAGE. AppInfo has no DisplayInfo for an unpackaged process and
            // throws rather than returning null, and unpackaged is how this app ships — so this is
            // the expected path, not a failure. The self-filter falls back to our own name.
            AppLog.Info($"WindowsNotificationSource: no package identity ({ex.GetType().Name}) — " +
                        "self-filter by app name only");
            Feed.IgnoreApps("NotifyIsland");
        }
    }

    private void RequestAccess()
    {
        UserNotificationListener? listener;
        try
        {
            listener = UserNotificationListener.Current;
        }
        catch (Exception ex)
        {
            AppLog.Warn("WindowsNotificationSource: UserNotificationListener.Current failed", ex);
            return;
        }

        try
        {
            var status = listener.GetAccessStatus();
            if (status == UserNotificationListenerAccessStatus.Allowed)
            {
                AccessStatus = status;
                IsListening = true;
                // The loop's first read ran before this and found no access; with the signal
                // live its next read could be 30 s away, which would also turn the first real
                // toast into "backlog". Prime now.
                Wake();
                return;
            }

            // The user can still say no in the prompt, and can change their mind later in
            // Settings. Denied is recorded, not argued with.
            //
            // Deliberately NOT awaited here. This method already runs on the UI thread (it was
            // posted there by Start), and a WinRT async completion is delivered through the
            // dispatcher that owns the operation — blocking the UI thread waiting for it is a
            // self-inflicted deadlock that only ends at the timeout. Start the operation, walk
            // away, and let the continuation post the result back.
            var operation = listener.RequestAccessAsync();
            Task.Run(async () =>
            {
                try
                {
                    var granted = await operation.AsTask().ConfigureAwait(false);
                    _postToUi(() => OnAccessResult(granted));
                }
                catch (Exception ex)
                {
                    AppLog.Warn("WindowsNotificationSource: request access failed", ex.GetBaseException());
                }
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn("WindowsNotificationSource: request access failed", ex);
        }
    }

    private void OnAccessResult(UserNotificationListenerAccessStatus granted)
    {
        AccessStatus = granted;
        IsListening = granted == UserNotificationListenerAccessStatus.Allowed;
        // With the signal live the next scheduled read may be 30 s away; read now instead.
        if (IsListening) Wake();
        if (IsListening)
            AppLog.Info($"WindowsNotificationSource: access granted, identity=" +
                        $"{(OwnPackageFamilyName.Length == 0 ? "<none>" : OwnPackageFamilyName)}");
        else
            AppLog.Warn($"WindowsNotificationSource: access {granted} — no toasts will be read");
    }

    // -- Polling -----------------------------------------------------------

    /// <summary>
    /// One poll per interval, sequentially. Each iteration awaits the platform call and the
    /// interval, so at most one poll is ever in flight and no thread is held while waiting.
    /// </summary>
    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_disposed)
        {
            try
            {
                await PollOnceAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (TimeoutException)
            {
                // A poll that outran PollTimeout. Expected on a busy shell; the next one follows
                // immediately. Not logged, because a busy shell makes it a once-a-second event
                // and the log is a file.
            }
            catch (Exception ex)
            {
                AppLog.Warn("WindowsNotificationSource: poll failed", ex);
            }

            try
            {
                await WaitForNextReadAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Sleep until the next read is due: the plain poll interval without a signal, or until the
    /// shell says a toast was published (with a slow safety poll behind it) when the signal is live.
    /// </summary>
    private async Task WaitForNextReadAsync(CancellationToken ct)
    {
        var signalLive = _signal?.IsLive == true;
        var wait = signalLive
            ? TimeSpan.FromMilliseconds(NotificationPollPlan.NextWaitMs(signalLive: true))
            : PollInterval;
        var woken = await _wake.WaitAsync(wait, ct).ConfigureAwait(false);
        if (!woken) return;
        // Let a burst finish arriving, then swallow the wakes it produced: one read covers them all.
        await Task.Delay(NotificationPollPlan.SignalCoalesceMs, ct).ConfigureAwait(false);
        while (_wake.Wait(0)) { }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        if (!IsListening) return;

        var listener = UserNotificationListener.Current;
        // AsTask + WaitAsync replaces the old Task.Run(...).Wait(timeout): the WinRT operation
        // completes whenever it completes, and a timeout just abandons the await. No thread is
        // occupied while the shell service decides, so a slow shell service can no longer leave
        // a pool thread parked for the whole timeout on every tick.
        //
        // IAsyncOperation has no Dispose in this projection, so there is nothing to release by
        // hand; the WinRT async object is closed when the awaited task completes.
        var notifications = await listener
            .GetNotificationsAsync(NotificationKinds.Toast)
            .AsTask()
            .WaitAsync(PollTimeout, ct)
            .ConfigureAwait(false);

        // 2026-10-02 perf pass: only notifications we have never seen are converted. The listener
        // hands back the whole action centre, not a delta — measured at 71 toasts carrying text on
        // the very first poll — and every one of them used to be walked through GetTextElements()
        // and DisplayInfo() once per second, forever, only for the feed to drop it as a duplicate.
        // The guard is by Guid, so the common "nothing is new" poll allocates nothing at all here.
        // Seen is rebuilt from the current listing each poll, which is also the bound: when a
        // toast is dismissed it leaves the centre, its id is forgotten, and memory cannot grow.
        var seenNow = new HashSet<uint>();
        var fresh = new List<IncomingToast>();
        foreach (var userNotification in notifications)
        {
            var id = userNotification.Id;
            seenNow.Add(id);
            if (_seenIds.Contains(id))
                continue;

            IncomingToast toast;
            try
            {
                toast = Convert(userNotification);
            }
            catch (Exception ex)
            {
                // One malformed toast must not cost us the rest of the poll.
                AppLog.Warn("WindowsNotificationSource: could not read a toast", ex);
                continue;
            }

            if (!_primed)
            {
                // The first poll is the user's backlog, not news. Announcing it would put a
                // capsule per historical toast on screen seconds after launch.
                Feed.Remember(toast);
                if (!_firstPollReported)
                {
                    _firstPollReported = true;
                    var withText = notifications.Count(n => n.Notification?.Visual?.Bindings is { Count: > 0 });
                    AppLog.Info($"WindowsNotificationSource: first poll read {notifications.Count} " +
                                $"toast(s), {withText} carrying text");
                }
                continue;
            }

            if (Feed.Accept(toast) == FeedVerdict.Accepted) fresh.Add(toast);
        }

        _seenIds = seenNow;
        if (notifications.Count > 0) _primed = true;
        if (fresh.Count == 0) return;

        _postToUi(() =>
        {
            foreach (var toast in fresh) Accepted?.Invoke(toast);
        });
    }

    /// <summary>One platform notification as the feed understands it.</summary>
    private IncomingToast Convert(UserNotification userNotification)
    {
        var elements = new List<ToastTextElement>();
        var visual = userNotification?.Notification?.Visual;
        if (visual?.Bindings is not null)
        {
            foreach (var binding in visual.Bindings)
            {
                // One binding can hold several elements (headline + attribution), and one toast
                // can have several bindings, so the order of this walk is the order on screen.
                foreach (var element in binding.GetTextElements())
                {
                    if (element is null) continue;
                    elements.Add(new ToastTextElement(element.Kind.ToString(), element.Text));
                }
            }
        }

        var (headline, body) = ToastText.Fold(elements);
        return new IncomingToast(
            Id: userNotification?.Id.ToString() ?? string.Empty,
            App: DisplayName(userNotification, LogSenderNameOnce),
            Title: headline,
            Body: body,
            // A silent toast arrives as a notification with no text elements at all, and reads
            // back as an empty fold. There is no SuppressPopup flag on this projection, so the
            // empty fold IS the silence signal and the feed drops it on its own.
            IsSilent: false);
    }

    /// <summary>
    /// The sending app's display name, or empty when the platform has none to give.
    /// <para>
    /// This one line is the most fragile call in the whole source. <c>AppInfo.DisplayInfo</c>
    /// throws rather than returning null for a sender with no package identity — our own
    /// process, and toasts from non-packaged senders such as the classic PowerShell AUMID.
    /// Measured: one such toast threw <c>NotImplementedException</c> out of the converter on
    /// every poll, which cost the toast entirely, when all the text had already been read
    /// successfully. So the name is the one thing that is allowed to fail.
    /// </para>
    /// </summary>
    private static string DisplayName(UserNotification? userNotification, Action<string>? once = null)
    {
        try
        {
            return (userNotification?.AppInfo?.DisplayInfo?.DisplayName ?? string.Empty).Trim();
        }
        catch (Exception ex)
        {
            once?.Invoke($"sender has no display name ({ex.GetType().Name}) — " +
                         "showing the toast without an app name");
            return string.Empty;
        }
    }
}
