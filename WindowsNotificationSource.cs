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
    /// <summary>How often the notification centre is read. The centre changes on user action,
    /// not on a schedule, and 1 s is what the clipboard poller already costs.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

    /// <summary>How long one poll may take before it is abandoned. The WinRT call is asynchronous
    /// and a hung shell service would otherwise keep a thread pool thread occupied forever, one
    /// per poll tick, because the timer fires again whether or not the last poll finished.</summary>
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
    private readonly System.Threading.Timer _timer;
    private int _polling;              // 0 = idle, 1 = a poll is in flight
    private bool _primed;              // the first successful poll was spent on priming
    private bool _disposed;
    private bool _firstPollReported;   // say the first successful read once, not every second
    private bool _senderNameReported;  // ditto: a nameless sender is re-read on every poll

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
        _timer = new System.Threading.Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
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
        _postToUi(RequestAccess);
        _timer.Change(PollInterval, PollInterval);
    }

    public void Stop()
    {
        IsRunning = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _timer.Dispose();
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
        if (IsListening)
            AppLog.Info($"WindowsNotificationSource: access granted, identity=" +
                        $"{(OwnPackageFamilyName.Length == 0 ? "<none>" : OwnPackageFamilyName)}");
        else
            AppLog.Warn($"WindowsNotificationSource: access {granted} — no toasts will be read");
    }

    // -- Polling -----------------------------------------------------------

    private void Poll()
    {
        if (!IsRunning || _disposed) return;
        // The timer fires on a fixed period; if a poll is still in flight the next tick is
        // dropped rather than queued, or a slow shell service turns into an unbounded pile of
        // concurrent polls each holding its own WinRT awaiter.
        if (Interlocked.CompareExchange(ref _polling, 1, 0) != 0) return;
        try
        {
            PollOnce();
        }
        catch (Exception ex)
        {
            AppLog.Warn("WindowsNotificationSource: poll failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private void PollOnce()
    {
        if (!IsListening) return;

        var listener = UserNotificationListener.Current;
        var notifications = Await(listener.GetNotificationsAsync(NotificationKinds.Toast), PollTimeout);
        if (notifications is null) return;

        var fresh = new List<IncomingToast>();
        foreach (var userNotification in notifications)
        {
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

    // -- Async bridge ------------------------------------------------------

    /// <summary>
    /// Run a WinRT async operation to completion from a timer thread, or give up.
    /// <para>
    /// A timer thread has no dispatcher, so there is nothing to deadlock on and a blocking wait
    /// is the honest way to read the result. The operation is moved to the thread pool first:
    /// the WinRT continuation may resume on a UI-agnostic pool thread, and blocking the timer
    /// thread on it would only work by luck.
    /// </para>
    /// </summary>
    private static T? Await<T>(Windows.Foundation.IAsyncOperation<T> operation, TimeSpan timeout)
        where T : class
    {
        try
        {
            var task = Task.Run(async () => await operation.AsTask().ConfigureAwait(false));
            return task.Wait(timeout) ? task.Result : null;
        }
        catch (AggregateException ex)
        {
            AppLog.Warn("WindowsNotificationSource: async operation failed", ex.GetBaseException());
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Warn("WindowsNotificationSource: async wait failed", ex);
            return null;
        }
    }

    /// <summary>Value-type twin of <see cref="Await{T}(IAsyncOperation{T}, TimeSpan)"/> —
    /// <c>RequestAccessAsync</c> returns an enum, and a null means "timed out", not "no access".
    /// A generic constraint is not part of the method signature, hence the different name.</summary>
    private static T? AwaitValue<T>(Windows.Foundation.IAsyncOperation<T> operation, TimeSpan timeout)
        where T : struct
    {
        try
        {
            var task = Task.Run(async () => await operation.AsTask().ConfigureAwait(false));
            return task.Wait(timeout) ? task.Result : null;
        }
        catch (AggregateException ex)
        {
            AppLog.Warn("WindowsNotificationSource: async operation failed", ex.GetBaseException());
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Warn("WindowsNotificationSource: async wait failed", ex);
            return null;
        }
    }
}
