using System;
using System.Windows.Forms;
using Avalonia.Threading;

namespace NotifyIsland;

/// <summary>
/// Battery / AC status via WinForms <see cref="SystemInformation.PowerStatus"/> (poll).
/// Fail-soft: if unavailable, raises nothing and leaves island alone.
/// </summary>
public sealed class WindowsPowerSource : IDisposable
{
    private readonly DispatcherTimer _poll;
    private bool _disposed;
    private bool _lowAlertFired;

    public event Action<PowerStatusSnapshot>? Changed;

    public PowerStatusSnapshot? Last { get; private set; }

    public WindowsPowerSource()
    {
        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _poll.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        try
        {
            Refresh(force: true);
            _poll.Start();
            AppLog.Warn("WindowsPowerSource started (PowerStatus poll)");
        }
        catch (Exception ex)
        {
            AppLog.Warn("WindowsPowerSource.Start failed — battery idle", ex);
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
        Stop();
    }

    public void ResetLowLatch() => _lowAlertFired = false;

    public void Refresh(bool force = false)
    {
        try
        {
            var ps = SystemInformation.PowerStatus;
            var line = ps.PowerLineStatus;
            var onAc = line == PowerLineStatus.Online;
            var onBattery = line == PowerLineStatus.Offline;
            var chargeFlags = ps.BatteryChargeStatus;
            var noBattery = chargeFlags.HasFlag(BatteryChargeStatus.NoSystemBattery);
            var charging = onAc && !noBattery;

            var life = ps.BatteryLifePercent;
            int percent;
            if (life is < 0 or > 1.0f)
                percent = Last?.Percent ?? 100;
            else
                percent = (int)Math.Round(Math.Clamp(life, 0f, 1f) * 100);

            var snap = new PowerStatusSnapshot
            {
                Percent = percent,
                OnAc = onAc,
                OnBattery = onBattery && !noBattery,
                IsCharging = charging && !noBattery,
                HasBattery = !noBattery,
                RawLine = line.ToString(),
                RawCharge = chargeFlags.ToString()
            };

            var changed = force
                || Last is null
                || Last.OnAc != snap.OnAc
                || Last.Percent != snap.Percent
                || Last.IsCharging != snap.IsCharging;

            Last = snap;
            if (changed)
                Changed?.Invoke(snap);
        }
        catch (Exception ex)
        {
            AppLog.Warn("WindowsPowerSource.Refresh failed", ex);
        }
    }

    public bool TakeLowAlert(int threshold)
    {
        var snap = Last;
        if (snap is null) return false;
        if (BatteryAlertLogic.ShouldResetLowFlag(snap.OnAc, snap.Percent, threshold))
        {
            _lowAlertFired = false;
            return false;
        }
        if (!BatteryAlertLogic.ShouldShowLowAlert(snap.OnBattery, snap.Percent, threshold, _lowAlertFired))
            return false;
        _lowAlertFired = true;
        return true;
    }

    public bool ConsumeChargeConnect(PowerStatusSnapshot snap, PowerStatusSnapshot? previous)
    {
        bool? wasOnAc = previous?.OnAc;
        return BatteryAlertLogic.ShouldShowChargeConnect(wasOnAc, snap.OnAc, snap.IsCharging);
    }

    public bool ConsumeChargeBump(PowerStatusSnapshot snap, int? prevPercent) =>
        BatteryAlertLogic.ShouldShowChargePercentBump(snap.OnAc, snap.IsCharging, prevPercent, snap.Percent);
}

/// <summary>UI-facing power snapshot (no WinForms types leaked to Core).</summary>
public sealed class PowerStatusSnapshot
{
    public int Percent { get; init; }
    public bool OnAc { get; init; }
    public bool OnBattery { get; init; }
    public bool IsCharging { get; init; }
    public bool HasBattery { get; init; }
    public string RawLine { get; init; } = "";
    public string RawCharge { get; init; } = "";
}
