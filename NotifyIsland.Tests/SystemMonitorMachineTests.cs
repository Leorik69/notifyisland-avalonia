using System;
using Xunit;

namespace NotifyIsland.Tests;

public class SystemMonitorMachineTests
{
    private sealed class FakeSource : ISystemMonitorSource
    {
        private Action<SystemSnapshot>? _changed;

        public event Action<SystemSnapshot>? SnapshotChanged
        {
            add { _changed += value; }
            remove { _changed -= value; }
        }

        public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;
        public SystemSnapshot Current { get; private set; } = SystemSnapshot.Empty;
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }
        public TimeSpan? LastInterval { get; private set; }
        public bool? LastIncludeAll { get; private set; }

        public void Start() => Started = true;
        public void Stop() => Stopped = true;
        public void SetInterval(TimeSpan period) => LastInterval = period;
        public void SetIncludeAllInterfaces(bool includeAll) => LastIncludeAll = includeAll;
        public void Dispose() => Disposed = true;

        public void Fire(SystemSnapshot s)
        {
            Current = s;
            _changed?.Invoke(s);
        }
    }

    [Fact]
    public void Machine_ForwardsSourceSnapshots()
    {
        var fake = new FakeSource();
        using var machine = new SystemMonitorMachine(fake);
        var s = new SystemSnapshot { CpuPercent = 12.5, RamTotalBytes = 1024 };
        fake.Fire(s);
        Assert.Equal(12.5, machine.Snapshot.CpuPercent);
        Assert.Equal(1024L, machine.Snapshot.RamTotalBytes);
    }

    [Fact]
    public void Machine_OnSnapshotRaisesOncePerFire()
    {
        var fake = new FakeSource();
        using var machine = new SystemMonitorMachine(fake);
        var count = 0;
        machine.OnSnapshot += _ => count++;
        fake.Fire(SystemSnapshot.Empty);
        fake.Fire(SystemSnapshot.Empty);
        Assert.Equal(2, count);
    }

    [Fact]
    public void Machine_StartDelegatesToSource()
    {
        var fake = new FakeSource();
        using var machine = new SystemMonitorMachine(fake);
        machine.Start();
        Assert.True(fake.Started);
    }

    [Fact]
    public void Machine_StopDelegatesToSource()
    {
        var fake = new FakeSource();
        using var machine = new SystemMonitorMachine(fake);
        machine.Stop();
        Assert.True(fake.Stopped);
    }

    [Fact]
    public void Machine_SetIntervalDelegatesToSource()
    {
        var fake = new FakeSource();
        using var machine = new SystemMonitorMachine(fake);
        machine.SetInterval(TimeSpan.FromMilliseconds(500));
        Assert.Equal(TimeSpan.FromMilliseconds(500), fake.LastInterval);
    }

    [Fact]
    public void Machine_SetIncludeAllInterfacesDelegates()
    {
        var fake = new FakeSource();
        using var machine = new SystemMonitorMachine(fake);
        machine.SetIncludeAllInterfaces(false);
        Assert.False(fake.LastIncludeAll);
    }

    [Fact]
    public void Machine_FirstSnapshot_HasZeroCpuButRealRam()
    {
        var fake = new FakeSource();
        using var machine = new SystemMonitorMachine(fake);
        fake.Fire(new SystemSnapshot
        {
            CpuPercent = 0,
            RamUsedBytes = 4_000_000_000,
            RamTotalBytes = 16_000_000_000,
            CapturedAt = DateTimeOffset.UtcNow
        });
        Assert.Equal(0, machine.Snapshot.CpuPercent);
        Assert.Equal(16_000_000_000L, machine.Snapshot.RamTotalBytes);
        Assert.NotEqual(default, machine.Snapshot.CapturedAt);
    }

    [Fact]
    public void Machine_DisposeUnsubscribesFromSource()
    {
        var fake = new FakeSource();
        var machine = new SystemMonitorMachine(fake);
        Assert.Equal(1, fake.SubscriberCount);
        var count = 0;
        machine.OnSnapshot += _ => count++;
        machine.Dispose();
        Assert.Equal(0, fake.SubscriberCount);
        fake.Fire(SystemSnapshot.Empty);
        Assert.Equal(0, count);
        Assert.True(fake.Disposed);
    }
}
