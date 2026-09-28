using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace NotifyIsland;

/// <summary>
/// The only file in the app that samples machine metrics. Runs on a
/// System.Threading.Timer and raises SnapshotChanged on the timer thread;
/// consumers must marshal to the UI thread themselves.
///
/// Lives in the Av project (not Core) because it needs WinForms SystemInformation.
/// </summary>
public sealed class WindowsSystemMonitorSource : ISystemMonitorSource
{
    // -- Interface ----------------------------------------------------------
    public event Action<SystemSnapshot>? SnapshotChanged;
    public SystemSnapshot Current => _current;

    public WindowsSystemMonitorSource(TimeSpan? interval = null)
    {
        _period = interval ?? TimeSpan.FromMilliseconds(OverlayTokens.StatsRefreshMs);
        _timer = new System.Threading.Timer(_ => SafeSample(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (_disposed) return;
        _running = true;
        _timer.Change(_period, _period);
    }

    public void Stop()
    {
        _running = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void SetInterval(TimeSpan period)
    {
        if (_disposed) return;
        _period = period;
        if (_running) _timer.Change(period, period);
    }

    public void SetIncludeAllInterfaces(bool includeAll) => _includeAllInterfaces = includeAll;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _timer.Dispose();
        DisposeProcessCache();
    }

    // -- Sampling -----------------------------------------------------------
    private void Sample()
    {
        var now = DateTime.UtcNow;
        var elapsedMs = (now - _lastSampleUtc).TotalMilliseconds;
        if (_lastSampleUtc != default && elapsedMs < OverlayTokens.StatsMinSampleIntervalMs)
            return;

        var processSetRebuilt = RefreshProcessCacheIfStale(now);

        var totalCpuTicks = TrySample(SampleTotalCpuTicks, (long?)null);
        var netDown = TrySample(SampleNetBytesReceived, (long?)null);
        var netUp = TrySample(SampleNetBytesSent, (long?)null);
        var (ramUsed, ramTotal) = TrySample(SamplePhysicalRam, (0L, 0L));

        // Process.TotalProcessorTime.Ticks are 100 ns units
        // (TimeSpan.TicksPerMillisecond ticks per millisecond). Without dividing
        // by TicksPerMillisecond the reading is ~10,000x too large and pins at 100.
        double cpuPercent = 0;
        long netDownBps = 0, netUpBps = 0;
        if (_lastSampleUtc != default)
        {
            var cpuElapsedMs = (now - _lastCpuBaselineUtc).TotalMilliseconds;
            var netDownElapsedMs = (now - _lastNetDownUtc).TotalMilliseconds;
            var netUpElapsedMs = (now - _lastNetUpUtc).TotalMilliseconds;
            // A rebuilt process set makes the CPU delta meaningless: dead
            // processes' ticks vanish and new ones contribute lifetime ticks.
            // Carry the previous reading forward for this one tick instead.
            if (processSetRebuilt || totalCpuTicks is null)
            {
                cpuPercent = _current.CpuPercent;
            }
            else
            {
                var cores = Math.Max(1, Environment.ProcessorCount);
                var tickDelta = totalCpuTicks.Value - _lastTotalCpuTicks;
                var capacityTicks = cpuElapsedMs * cores * TimeSpan.TicksPerMillisecond;
                cpuPercent = tickDelta * 100.0 / capacityTicks;
            }
            netDownBps = netDown is null ? _current.NetDownBytesPerSec
                : (netDown.Value - _lastNetDown) * 1000 / (long)netDownElapsedMs;
            netUpBps = netUp is null ? _current.NetUpBytesPerSec
                : (netUp.Value - _lastNetUp) * 1000 / (long)netUpElapsedMs;
        }

        // Only advance a baseline on a successful sample, so a failure cannot
        // make the next delta measure against zero. Each counter advances its
        // own window too, so a delta is never divided by a shorter interval
        // than the one it actually spans.
        if (totalCpuTicks is not null)
        {
            _lastTotalCpuTicks = totalCpuTicks.Value;
            _lastCpuBaselineUtc = now;
        }
        if (netDown is not null)
        {
            _lastNetDown = netDown.Value;
            _lastNetDownUtc = now;
        }
        if (netUp is not null)
        {
            _lastNetUp = netUp.Value;
            _lastNetUpUtc = now;
        }
        if (_lastSampleUtc == default)
        {
            // Fresh source: seed the windows so the first delta has a
            // well-defined denominator instead of zero.
            _lastCpuBaselineUtc = now;
            _lastNetDownUtc = now;
            _lastNetUpUtc = now;
        }
        _lastSampleUtc = now;

        _current = new SystemSnapshot
        {
            CpuPercent = StatsDebounce.Percent(
                _current.CpuPercent, Math.Clamp(cpuPercent, 0, 100), OverlayTokens.StatsDebouncePercent),
            RamUsedBytes = ramUsed,
            RamTotalBytes = ramTotal,
            BatteryPercent = TrySampleBatteryPercent(),
            OnAcPower = TrySampleOnAc(),
            NetUpBytesPerSec = StatsDebounce.Rate(
                _current.NetUpBytesPerSec, netUpBps, OverlayTokens.StatsNetRateFloorBps),
            NetDownBytesPerSec = StatsDebounce.Rate(
                _current.NetDownBytesPerSec, netDownBps, OverlayTokens.StatsNetRateFloorBps),
            CapturedAt = now
        };
        try { SnapshotChanged?.Invoke(_current); }
        catch (Exception ex) { AppLog.Warn("WindowsSystemMonitorSource subscriber failed", ex); }
    }

    private void SafeSample()
    {
        try { Sample(); }
        catch (Exception ex) { AppLog.Warn("WindowsSystemMonitorSource sample failed", ex); }
    }

    // -- Per-metric samplers ------------------------------------------------
    private long? SampleTotalCpuTicks()
    {
        long sum = 0;
        foreach (var p in _cachedProcesses)
        {
            try { sum += p.TotalProcessorTime.Ticks; }
            catch (InvalidOperationException) { /* process exited mid-read */ }
            catch (Win32Exception) { /* access denied on a protected process */ }
        }
        return sum;
    }

    private long? SampleNetBytesReceived()
    {
        long sum = 0;
        foreach (var ni in EnumerateCountedInterfaces())
        {
            try { sum += ni.GetIPv4Statistics().BytesReceived; }
            catch (NetworkInformationException) { /* adapter went away */ }
            catch (PlatformNotSupportedException) { }
            catch (NotSupportedException) { }
        }
        return sum;
    }

    private long? SampleNetBytesSent()
    {
        long sum = 0;
        foreach (var ni in EnumerateCountedInterfaces())
        {
            try { sum += ni.GetIPv4Statistics().BytesSent; }
            catch (NetworkInformationException) { }
            catch (PlatformNotSupportedException) { }
            catch (NotSupportedException) { }
        }
        return sum;
    }

    private IEnumerable<NetworkInterface> EnumerateCountedInterfaces()
    {
        NetworkInterface[] all;
        try { all = NetworkInterface.GetAllNetworkInterfaces(); }
        catch { yield break; }

        foreach (var ni in all)
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (!_includeAllInterfaces && IsVirtual(ni)) continue;
            yield return ni;
        }
    }

    private static bool IsVirtual(NetworkInterface ni)
    {
        if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback
            or NetworkInterfaceType.Tunnel
            or NetworkInterfaceType.Unknown) return true;
        return VirtualAdapterName.IsMatch(ni.Name) || VirtualAdapterName.IsMatch(ni.Description);
    }

    private static readonly Regex VirtualAdapterName = new(
        @"vEthernet|Hyper-V|VirtualBox|VMware|WSL|Tailscale|Loopback|Tunnel|VPN",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // -- RAM via GlobalMemoryStatusEx ---------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static (long Used, long Total) SamplePhysicalRam()
    {
        var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref ms)) return (0, 0);
        return ((long)(ms.ullTotalPhys - ms.ullAvailPhys), (long)ms.ullTotalPhys);
    }

    // -- Battery via WinForms -----------------------------------------------
    private static double? TrySampleBatteryPercent()
    {
        try
        {
            var ps = SystemInformation.PowerStatus;
            if (ps.BatteryLifePercent == 255) return null;   // no battery / unknown
            return ps.BatteryLifePercent * 100.0;
        }
        catch { return null; }
    }

    private static bool TrySampleOnAc()
    {
        try { return SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online; }
        catch { return false; }
    }

    // -- Helpers ------------------------------------------------------------
    private static T TrySample<T>(Func<T> sampler, T fallback)
    {
        try { return sampler(); }
        catch { return fallback; }
    }

    private bool RefreshProcessCacheIfStale(DateTime now)
    {
        if ((now - _lastProcessRefreshUtc).TotalMilliseconds < OverlayTokens.StatsProcessCacheMs) return false;
        _lastProcessRefreshUtc = now;
        DisposeProcessCache();
        Process[] all;
        try { all = Process.GetProcesses(); }
        catch { _cachedProcesses = Array.Empty<Process>(); return true; }
        var keep = new List<Process>(all.Length);
        foreach (var p in all)
        {
            try
            {
                if (p.Id > 0) keep.Add(p);
                else p.Dispose();
            }
            catch { try { p.Dispose(); } catch { } }
        }
        _cachedProcesses = keep.ToArray();
        return true;
    }

    private void DisposeProcessCache()
    {
        foreach (var p in _cachedProcesses)
        {
            try { p.Dispose(); } catch { }
        }
        _cachedProcesses = Array.Empty<Process>();
    }

    // -- State --------------------------------------------------------------
    private readonly System.Threading.Timer _timer;
    private TimeSpan _period;
    private volatile bool _running;
    private bool _disposed;
    private volatile bool _includeAllInterfaces = true;
    private volatile SystemSnapshot _current = SystemSnapshot.Empty;

    // Per-instance: owned by this source, disposed in Dispose, refreshed on
    // OverlayTokens.StatsProcessCacheMs. Never static — a shared cache would let
    // one source's Dispose() break a second live source's CPU sampling.
    private Process[] _cachedProcesses = Array.Empty<Process>();
    private DateTime _lastProcessRefreshUtc;
    private DateTime _lastSampleUtc;
    // The window each baseline was captured over. Advanced only alongside its
    // own baseline, so a failed sample cannot pair a stale counter delta with a
    // shorter elapsed time and inflate the rate for one tick.
    private DateTime _lastCpuBaselineUtc;
    private DateTime _lastNetDownUtc;
    private DateTime _lastNetUpUtc;
    private long _lastTotalCpuTicks;
    private long _lastNetDown;
    private long _lastNetUp;
}
