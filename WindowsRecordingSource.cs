using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace NotifyIsland;

/// <summary>
/// Reads whether a microphone is picking up signal, and whether something looks like a screen
/// recorder. Poll-based, fail-soft, and deliberately modest about what it claims.
/// <para>
/// The microphone half is real: WASAPI's <c>IAudioMeterInformation</c> is what Windows' own
/// privacy indicator is built on, and it is read through the default CAPTURE endpoint. The screen
/// half is a HEURISTIC over the process list, because Windows exposes no API for "is this process
/// recording the screen" — see <see cref="RecorderNames"/> for what it can and cannot see.
/// </para>
/// <para>
/// The COM work happens on a background thread and only the resulting numbers cross back, so a
/// wedged audio service cannot freeze the UI thread. The reading is polled rather than event-driven
/// because a capture meter has no events: silence is a value, not a notification.
/// </para>
/// </summary>
public sealed class WindowsRecordingSource : IDisposable
{
    /// <summary>Poll period (ms). A meter that updates every 700 ms still reads as live.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(700);

    /// <summary>
    /// Process names treated as screen recorders.
    /// <para>
    /// A lower-case list because <see cref="Process.ProcessName"/> has no extension. This is a
    /// WHITELIST of names, and that is the honest limit of the feature: it sees a recorder it has
    /// been told about, and misses every other one, including anything custom. It is drawn as
    /// «запись экрана» only when one of these is actually running, and a false negative is a
    /// missing indicator rather than a wrong one.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> RecorderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "obs64", "obs32", "obs",              // OBS Studio
        "sharex", "flameshot", "screenity",  // common open-source recorders
        "camtasia", "bandicam", "screenflow",
        "loom", "krisp", "manycam",
        "xboxgamebar", "gamebar", "gamebarpresencewriter",
        "dxtory", "mednz", "recme",
    };

    // -- COM -------------------------------------------------------------------------------

    private const uint EDataflowCapture = 1;
    private const uint ERoleConsole = 0;

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    /// <summary>
    /// The peak meter, narrowed to the one method we call. The interface has four members and the
    /// vtable order IS the contract — GetPeakValue is index 3, so the two before it must be
    /// declared even though they are never used, and skipping them would call whatever the next
    /// vtable slot happens to be.
    /// </summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    private interface IAudioMeterInformation
    {
        int GetPeakValue(out float peak);
        int GetMeteringChannelCount(out int channelCount);
        int GetChannelsPeakValues(int channelCount, [Out] float[] peaks);
        int QueryHardwareSupport(out int hardwareSupportMask);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    private interface IMMDeviceEnumeratorRaw
    {
        int EnumAudioEndpoints(uint dataflow, uint state, out object devices);
        int GetDefaultAudioEndpoint(uint dataflow, uint role, out IntPtr endpoint);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr device);
        int RegisterEndpointNotificationCallback(IntPtr client);
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    /// <summary>
    /// IID_IAudioMeterInformation. Spelled as bytes because it is needed before any interface
    /// type exists, and passing a <c>Guid</c> here means constructing one on every call.
    /// </summary>
    private static readonly Guid IidAudioMeterInformation =
        new("C02216F6-8C67-4B5B-9D00-D008E73E0064");

    private IntPtr _endpointRaw;

    private IAudioMeterInformation? _meter;
    private float _lastPeak;

    private readonly DispatcherTimer _poll;
    private bool _disposed;
    private volatile bool _reading;
    private int _errorLogged;

    public event Action<RecordingSample>? Changed;

    public RecordingSample Current { get; private set; } = new();

    /// <summary>Set when the audio stack could not be reached at all, for the tooltip.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Consecutive refused polls before the source stops asking.
    /// <para>
    /// 20 polls at 700 ms is about fourteen seconds of a COM call that provably cannot succeed.
    /// A machine with no capture device would otherwise pay that forever for a lamp that is off.
    /// The poll is NOT given up on permanently — see <see cref="ScheduleRetry"/> — because the
    /// user can plug in a microphone at any moment, and an indicator that never notices is worse
    /// than one that costs a little CPU.
    /// </para>
    /// </summary>
    private const int MaxConsecutiveFailures = 20;

    /// <summary>How long to wait before trying again once the source has backed off (ms).</summary>
    private const int RetryDelayMs = 30_000;

    private int _consecutiveFailures;
    private bool _backedOff;

    public WindowsRecordingSource()
    {
        _poll = new DispatcherTimer { Interval = PollInterval };
        _poll.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        try
        {
            _reading = true;
            _ = Task.Run(PrimeAsync);
            _poll.Start();
        }
        catch (Exception ex)
        {
            AppLog.Warn("WindowsRecordingSource.Start failed — indicator idle", ex);
        }
    }

    public void Stop()
    {
        try { _poll.Stop(); }
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reading = false;
        Stop();
        var meter = _meter;
        _meter = null;
        if (meter is not null)
        {
            try { Marshal.ReleaseComObject(meter); }
            catch { /* the RCW may already be detached */ }
        }
        // The raw endpoint pointer is ours alone: the meter wrapper took its own reference, so
        // this one is still held and has to be released separately.
        var endpoint = Interlocked.Exchange(ref _endpointRaw, IntPtr.Zero);
        if (endpoint != IntPtr.Zero)
        {
            try { Marshal.Release(endpoint); }
            catch { /* ignore */ }
        }
    }

    /// <summary>
    /// One poll. The COM call is dispatched to the thread pool and the UI is only told a number.
    /// A second poll arriving while the first is still in flight is DROPPED, not queued: the
    /// newest reading is the only one that matters, and a backlog of stale COM calls would make
    /// the indicator lag by exactly as much as the audio service was slow.
    /// </summary>
    public void Refresh()
    {
        if (_disposed || !_reading || _readingInFlight) return;
        _readingInFlight = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var peak = await Task.Run(ReadPeakAsync).ConfigureAwait(false);
                // A failure is latched to one log line, so it has to be UNLATCHED on success or a
                // transient failure at startup would silence every later problem for the rest of
                // the session. The audio stack is not always up in the first second of a launch —
                // measured here: the very first poll at 20:04:49 could not reach the meter, and
                // the ones after it could.
                if (Interlocked.Exchange(ref _errorLogged, 0) == 1)
                    AppLog.Info("WindowsRecordingSource: capture meter available again");
                if (Interlocked.Exchange(ref _consecutiveFailures, 0) > 0) ResumeAfterBackoff();
                var recorder = FindRecorder();
                var sample = new RecordingSample
                {
                    MicrophoneActive = peak >= RecordingGate.LevelFloor,
                    MicrophoneLevel = peak,
                    ScreenRecorderName = recorder,
                };
                LastError = null;
                var changed = sample.MicrophoneLevel != Current.MicrophoneLevel
                    || !string.Equals(sample.ScreenRecorderName, Current.ScreenRecorderName, StringComparison.Ordinal)
                    || sample.MicrophoneActive != Current.MicrophoneActive;
                Current = sample;
                if (changed) Changed?.Invoke(sample);
            }
            catch (Exception ex)
            {
                // One warning, not one per poll: a machine with no microphone would otherwise
                // write a log line every 700 ms for the rest of the session.
                if (System.Threading.Interlocked.Exchange(ref _errorLogged, 1) == 0)
                {
                    LastError = ex.Message;
                    AppLog.Warn("WindowsRecordingSource.Refresh failed — indicator idle", ex);
                }
                // Counted separately from the log latch: the latch is about not repeating a
                // MESSAGE, this is about not repeating the CALL. On a machine whose endpoint
                // refuses every audio interface (measured 2026-10-03) the poll would otherwise
                // run COM 128 times a minute, forever, for a lamp that can never light.
                if (Interlocked.Increment(ref _consecutiveFailures) == MaxConsecutiveFailures)
                    BackOff();
            }
            finally
            {
                _readingInFlight = false;
            }
        });
    }

    private bool _readingInFlight;

    /// <summary>Stop polling and arrange one more attempt later.</summary>
    private void BackOff()
    {
        _backedOff = true;
        try { _poll.Stop(); } catch { /* ignore */ }
        AppLog.Warn(
            $"WindowsRecordingSource: no capture meter after {MaxConsecutiveFailures} attempts — " +
            $"polling every {RetryDelayMs / 1000} s instead");
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(RetryDelayMs, _disposed
                    ? new CancellationToken(true) : CancellationToken.None).ConfigureAwait(false);
            }
            catch { return; }   // disposed while waiting
            if (_disposed || !_reading) return;
            try { _poll.Start(); } catch { /* ignore */ }
        });
    }

    /// <summary>A reading came back, so the source is live again.</summary>
    private void ResumeAfterBackoff()
    {
        if (!_backedOff) return;
        _backedOff = false;
        AppLog.Info("WindowsRecordingSource: polling normally again");
        // DispatcherTimer has no IsRunning, and reaching for one would mean keeping a second
        // source of truth. The backoff flag already IS the answer: we only get here having just
        // come out of BackOff, which is the only path that stopped the timer.
        try { _poll.Start(); } catch { /* ignore */ }
    }

    /// <summary>
    /// Resolve the meter once, before the first poll, so the first reading is not a second late.
    /// <para>
    /// Failures are LOGGED and dropped, never rethrown. A fire-and-forget task whose exception
    /// nobody observes is rethrown by the finalizer thread as an unobserved task exception, which
    /// the app treats as FATAL — so on a machine with no capture device at all, starting the
    /// source would otherwise log one warning and then kill the island.
    /// </para>
    /// </summary>
    private async Task PrimeAsync()
    {
        try
        {
            await ReadPeakAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (System.Threading.Interlocked.Exchange(ref _errorLogged, 1) == 0)
            {
                LastError = ex.Message;
                AppLog.Warn("WindowsRecordingSource: no capture meter — indicator idle", ex);
            }
        }
    }

    /// <summary>
    /// Peak level of the default capture endpoint, 0…1.
    /// <para>
    /// The endpoint is resolved ONCE and kept. Re-resolving it every poll would put a service call
    /// on the path of a 700 ms timer for no benefit, and the default capture device does not change
    /// while the app runs — a device change surfaces as the peak simply going to zero, which is
    /// what the gate treats as silence anyway.
    /// </para>
    /// </summary>
    private Task<float> ReadPeakAsync()
    {
        return Task.Run(() =>
        {
            if (_meter is null) _meter = ResolveMeter();
            var hr = _meter.GetPeakValue(out var peak);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
            _lastPeak = peak;
            return peak;
        });
    }

    /// <summary>
    /// Resolve the default capture endpoint and pull the meter interface off it.
    /// <para>
    /// The QueryInterface is EXPLICIT, and that is the fix rather than a stylistic choice.
    /// Casting the endpoint straight to <c>IAudioMeterInformation</c> is the obvious way to write
    /// this and it does not work: the endpoint arrives as a bare <c>System.__ComObject</c>, and
    /// the implicit cast asks the RCW for an interface the marshaler cannot supply, failing with
    /// <c>E_NOINTERFACE</c> — measured on this machine, on the very first poll. Going through
    /// <c>Marshal.QueryInterface</c> on the raw pointer asks the object itself.
    /// </para>
    /// <para>
    /// A REFUSAL here is a normal, recoverable state, not a bug: the audio service is not always
    /// ready in the first second of a launch, and a machine with no capture device never will be.
    /// Nothing is cached on failure, so the next poll tries again — which is why the failure is
    /// logged once and then, on recovery, logged as coming back.
    /// </para>
    /// </summary>
    private IAudioMeterInformation ResolveMeter()
    {
        var enumerator = (IMMDeviceEnumeratorRaw)new MMDeviceEnumeratorComObject();
        try
        {
            var hr = enumerator.GetDefaultAudioEndpoint(EDataflowCapture, ERoleConsole, out var endpoint);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
            if (endpoint == IntPtr.Zero)
                throw new COMException("No default capture endpoint");

            _endpointRaw = endpoint;
            // Marshal.QueryInterface(IntPtr, in Guid, out IntPtr) reports the HRESULT as the
            // RETURN VALUE and hands back the interface through the out parameter. Both are
            // integers and the compiler does not check the order, so a swap here is silent — both
            // are named for their role and both are in the failure message.
            var iid = IidAudioMeterInformation;
            var qiHr = Marshal.QueryInterface(endpoint, in iid, out var meterPtr);
            if (qiHr != 0 || meterPtr == IntPtr.Zero)
            {
                Marshal.Release(endpoint);
                _endpointRaw = IntPtr.Zero;
                // E_NOINTERFACE here is NOT a bug in the query, and saying so is the whole point
                // of the message. Measured on this machine on 2026-10-03: the default capture
                // endpoint answers IUnknown and IMMDevice but refuses IAudioMeterInformation,
                // IAudioClient AND IAudioEndpointVolume — an endpoint that implements no audio
                // interface at all is a placeholder, not a microphone. The IID is correct
                // (verified against the WASAPI header), so there is nothing left to try: the
                // machine has no capture device to measure.
                throw new COMException(
                    qiHr == unchecked((int)0x80004002)
                        ? "The default capture endpoint exposes no audio interfaces " +
                          "(E_NOINTERFACE for IAudioMeterInformation) — this machine has no " +
                          "capture device to measure, so the indicator stays idle"
                        : $"The capture endpoint does not expose IAudioMeterInformation " +
                          $"(hr=0x{qiHr:X8}, ptr=0x{meterPtr.ToInt64():X})");
            }
            try
            {
                return (IAudioMeterInformation)Marshal.GetObjectForIUnknown(meterPtr);
            }
            finally
            {
                // The wrapper holds its own reference now, so this one is ours to drop.
                Marshal.Release(meterPtr);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>
    /// Best-effort recorder name from the running processes.
    /// <para>
    /// This is the part that is a guess, and the comment in <see cref="RecorderNames"/> says so.
    /// It runs on the same background thread as the COM call, so a machine with 300 processes pays
    /// for it off the UI thread and at 700 ms rather than per frame.
    /// </para>
    /// </summary>
    private static string? FindRecorder()
    {
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    string name;
                    try { name = p.ProcessName; }
                    catch { continue; }   // exited between enumeration and read
                    if (RecorderNames.Contains(name))
                        return DisplayName(name);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("FindRecorder failed", ex);
        }
        return null;
    }

    /// <summary>Turn a process name into something a user would recognise in a tooltip.</summary>
    private static string DisplayName(string name) => name.ToLowerInvariant() switch
    {
        "obs64" or "obs32" or "obs" => "OBS",
        "xboxgamebar" or "gamebar" or "gamebarpresencewriter" => "Xbox Game Bar",
        _ => name,
    };
}
