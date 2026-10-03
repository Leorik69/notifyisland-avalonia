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

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(uint dataflow, uint state, out object devices);
        int GetDefaultAudioEndpoint(uint dataflow, uint role, [MarshalAs(UnmanagedType.Interface)] out object endpoint);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out object device);
        int RegisterEndpointNotificationCallback(IntPtr client);
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

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
            }
            finally
            {
                _readingInFlight = false;
            }
        });
    }

    private bool _readingInFlight;

    private async Task PrimeAsync()
    {
        await ReadPeakAsync().ConfigureAwait(false);
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
            if (_meter is null)
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                try
                {
                    enumerator.GetDefaultAudioEndpoint(EDataflowCapture, ERoleConsole, out var endpoint);
                    _meter = (IAudioMeterInformation)endpoint;
                }
                finally
                {
                    Marshal.ReleaseComObject(enumerator);
                }
            }
            var hr = _meter.GetPeakValue(out var peak);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
            _lastPeak = peak;
            return peak;
        });
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
