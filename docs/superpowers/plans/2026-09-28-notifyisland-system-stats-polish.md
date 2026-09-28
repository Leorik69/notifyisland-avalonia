# NotifyIsland 1.12.0 Ч System Monitor + Polish Suite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a live System Monitor (CPU%, RAM, Battery%, Network throughput) to the NotifyIsland pill with an adaptive layout, plus four polish passes (animations, settings UI, hover-peek, tray menu).

**Architecture:** Five new pure-Core types (`SystemSnapshot`, `ISystemMonitorSource`, `StatsDebounce`, `StatsLayout`, `SystemMonitorMachine`) carry all logic and are unit-testable without any Windows API. One new Av-project file (`WindowsSystemMonitorSource.cs`) does the actual platform sampling via `System.Threading.Timer` + `Process` / `GlobalMemoryStatusEx` / `NetworkInterface` / WinForms `SystemInformation`, and marshals results to the UI thread. The FSM gains a 12th `OverlayKind` and a 19th `OverlayCommand`; the new payload fields are carried through all three payload transforms (`Apply`, `Clone`, `Sanitize`).

**Tech Stack:** .NET 8, Avalonia 11.3.22, WinForms interop (already referenced via `UseWindowsForms=true`), xUnit, Inno Setup 6.

**Spec:** `docs/superpowers/specs/2026-09-28--notifyisland-system-stats-polish.md`

## Global Constraints

- Target framework `net8.0-windows10.0.19041.0` (Av) and `net8.0-windows` (Core). No framework change.
- **No new NuGet dependency.** Every platform call must come from `System`, `System.Diagnostics`, `System.Net.NetworkInformation`, or WinForms `SystemInformation` / `System.Windows.Forms.Clipboard`.
- `NotifyIsland.Core` has **no** `UseWindowsForms` and must stay WinForms-free. All `Windows*Source.cs` live at the repo root (Av project).
- No Open-Meteo, no island mouse-drag, no swipe gestures Ч clicks only.
- **All numeric animation / layout / timing values live in `NotifyIsland.Core/OverlayTokens.cs`** and are mirrored in `docs/ISLAND_GUIDELINES.md` І2 and І9. No bare numeric literals in code.
- Width and ratio tokens are `double`. Durations and counters are `int`. Byte-rates are `long`.
- Git commits use `git -c user.name='Leorik69' -c user.email='leorik69@users.noreply.github.com'`, never `git config`.
- Baseline before this plan: **174 tests passing**, 0 warnings, 0 errors.
- Target after this plan: **203 tests passing**, 0 warnings, 0 errors.
  This is one more than the spec's 202: the plan adds `SystemStats_SetCommand_CarriesSnapshotThroughToSnapshot` on top of the spec's list, because the `Apply`/`Clone`/`Sanitize` extension was the spec review's highest-risk omission and deserves a direct assertion.
- New `AnimationAction` members are **appended**, never inserted mid-sequence (ordinal safety).
- Russian UI copy, English code identifiers.

## File Structure

**Created Ч Core (`NotifyIsland.Core/`)**

| File | Responsibility |
|---|---|
| `SystemSnapshot.cs` | Immutable record holding one sample of all four metrics. Pure data, no logic beyond `RamPercent`. |
| `ISystemMonitorSource.cs` | Platform-agnostic interface: `Current`, `SnapshotChanged`, `Start`, `Stop`, `SetInterval`, `SetIncludeAllInterfaces`, `IDisposable`. |
| `StatsDebounce.cs` | Two pure static helpers: `Percent`, `Rate`. |
| `StatsLayout.cs` | Pure layout math: `VisibleMetricCount`, `StatsPillWidth`, `SlotOrder`, `StatsMetricSlot` enum. |
| `SystemMonitorMachine.cs` | Facade over the source; marshals nothing, just holds the latest snapshot and re-raises the event. |

**Created Ч Av (repo root)**

| File | Responsibility |
|---|---|
| `WindowsSystemMonitorSource.cs` | The only file that touches platform APIs. Timer-driven sampler with per-item error isolation. |

**Created Ч Tests (`NotifyIsland.Tests/`)**

| File | Responsibility |
|---|---|
| `SystemMonitorMachineTests.cs` | 8 tests, fake source. |
| `StatsDebounceTests.cs` | 4 tests. |
| `AdaptiveStatsLayoutTests.cs` | 9 tests (layout math + FSM auto-collapse gate). |
| `AnimationEasingTests.cs` | 4 tests for `ClickPop`. |

**Modified Ч Core**

| File | Change |
|---|---|
| `OverlayTokens.cs` | Append ~20 new tokens. |
| `AnimationTiming.cs` | Append 2 `AnimationAction` members. |
| `OverlayMachine.cs` | New `OverlayKind`, new `OverlayCommand`, 2 payload fields, `Apply`/`Clone`/`Sanitize` carry them, `WidthFor` gains a parameter, `StatsMetricCount` property, `Tick` auto-collapse gate. |
| `AppSettings.cs` | 7 new keys, `Normalize()` clamps. |
| `NotifyAnimStyles.cs` | Add `ClickPop`. |

**Modified Ч Av**

| File | Change |
|---|---|
| `OverlayWindow.axaml` | `SystemStatsPanel` in `CollapsedRow`; `ClickPop`/wobble wiring. |
| `OverlayWindow.axaml.cs` | Compose machine, subscribe, dispatch, paint metrics, click-to-expand. |
| `SettingsWindow.axaml(.cs)` | Two new sidebar sections, search box, live validation, import/export. |
| `TrayService.cs` | New menu order + clipboard submenu. |
| `Program.cs` | Dispose the machine on shutdown. |

**Modified Ч Docs**

| File | Change |
|---|---|
| `docs/ISLAND_GUIDELINES.md` | New tokens in І2; І7 sidebar list 9 > 12. |
| `docs/ISLAND_PREVIEW.md` | І1.12.0 features; the DPI worked example. |
| `CONTEXT.md` | System monitor row in the source-of-truth table. |
| `CHANGELOG.md` | `## Unreleased Ч System monitor (1.12.0)`. |

---

### Task 1: Core data types and pure helpers

**Files:**
- Create: `NotifyIsland.Core/SystemSnapshot.cs`
- Create: `NotifyIsland.Core/ISystemMonitorSource.cs`
- Create: `NotifyIsland.Core/StatsDebounce.cs`
- Test: `NotifyIsland.Tests/StatsDebounceTests.cs`

**Interfaces:**
- Consumes: nothing (first task).
- Produces:
  - `public sealed record SystemSnapshot` with `double CpuPercent`, `long RamUsedBytes`, `long RamTotalBytes`, `double? BatteryPercent`, `bool OnAcPower`, `long NetUpBytesPerSec`, `long NetDownBytesPerSec`, `DateTimeOffset CapturedAt`, `static SystemSnapshot Empty`, `double RamPercent`
  - `public interface ISystemMonitorSource : IDisposable` with `event Action<SystemSnapshot>? SnapshotChanged`, `SystemSnapshot Current { get; }`, `void Start()`, `void Stop()`, `void SetInterval(TimeSpan period)`, `void SetIncludeAllInterfaces(bool includeAll)`
  - `public static class StatsDebounce` with `static double Percent(double previous, double next, double thresholdPct)` and `static long Rate(long previous, long next, long thresholdBytesPerSec)`

- [ ] **Step 1: Write the failing test**

Create `NotifyIsland.Tests/StatsDebounceTests.cs`:

```csharp
using Xunit;

namespace NotifyIsland.Tests;

public class StatsDebounceTests
{
    [Fact]
    public void Percent_ReusesPreviousWhenDeltaBelowThreshold()
    {
        Assert.Equal(42.0, StatsDebounce.Percent(previous: 42.0, next: 42.4, thresholdPct: 0.5));
    }

    [Fact]
    public void Percent_UpdatesWhenDeltaAboveThreshold()
    {
        Assert.Equal(43.0, StatsDebounce.Percent(previous: 42.0, next: 43.0, thresholdPct: 0.5));
    }

    [Fact]
    public void Rate_ReusesWhenBelowFloor()
    {
        Assert.Equal(1000L, StatsDebounce.Rate(previous: 1000L, next: 1200L, thresholdBytesPerSec: 4096L));
    }

    [Fact]
    public void Rate_NeverNegative()
    {
        Assert.Equal(0L, StatsDebounce.Rate(previous: 0L, next: -50L, thresholdBytesPerSec: 4096L));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release --filter "FullyQualifiedName~StatsDebounceTests"`
Expected: FAIL Ч `error CS0103: The name 'StatsDebounce' does not exist`

- [ ] **Step 3: Write minimal implementation**

Create `NotifyIsland.Core/StatsDebounce.cs`:

```csharp
namespace NotifyIsland;

/// <summary>
/// Hysteresis for sampled metrics so the pill does not flicker on sub-threshold jitter.
/// Pure static helpers; the Windows source is their only production caller.
/// </summary>
public static class StatsDebounce
{
    /// <summary>
    /// Returns <paramref name="previous"/> when |next - previous| is below
    /// <paramref name="thresholdPct"/> percentage points; otherwise returns <paramref name="next"/>.
    /// </summary>
    public static double Percent(double previous, double next, double thresholdPct)
        => Math.Abs(next - previous) < thresholdPct ? previous : next;

    /// <summary>
    /// Byte-rate variant: reuses <paramref name="previous"/> when the absolute delta is below
    /// <paramref name="thresholdBytesPerSec"/>. Never returns a negative rate.
    /// </summary>
    public static long Rate(long previous, long next, long thresholdBytesPerSec)
        => Math.Abs(next - previous) < thresholdBytesPerSec ? previous : Math.Max(0, next);
}
```

Create `NotifyIsland.Core/SystemSnapshot.cs`:

```csharp
namespace NotifyIsland;

/// <summary>One sample of the machine's live metrics. Immutable; produced by an ISystemMonitorSource.</summary>
public sealed record SystemSnapshot
{
    public double CpuPercent { get; init; }
    public long RamUsedBytes { get; init; }
    /// <summary>Physical RAM installed, or 0 when the sample could not read it. 0 means "hide the RAM slot".</summary>
    public long RamTotalBytes { get; init; }
    /// <summary>null on desktops or when the battery could not be read.</summary>
    public double? BatteryPercent { get; init; }
    public bool OnAcPower { get; init; }
    public long NetUpBytesPerSec { get; init; }
    public long NetDownBytesPerSec { get; init; }
    public DateTimeOffset CapturedAt { get; init; }

    public static SystemSnapshot Empty { get; } = new();

    public double RamPercent => RamTotalBytes > 0
        ? Math.Clamp(RamUsedBytes * 100.0 / RamTotalBytes, 0, 100)
        : 0;
}
```

Create `NotifyIsland.Core/ISystemMonitorSource.cs`:

```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release --filter "FullyQualifiedName~StatsDebounceTests"`
Expected: PASS Ч 4 passed

- [ ] **Step 5: Verify no regression**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release`
Expected: PASS Ч 178 passed (174 + 4)

- [ ] **Step 6: Commit**

```bash
git add NotifyIsland.Core/SystemSnapshot.cs NotifyIsland.Core/ISystemMonitorSource.cs NotifyIsland.Core/StatsDebounce.cs NotifyIsland.Tests/StatsDebounceTests.cs
git -c user.name='Leorik69' -c user.email='leorik69@users.noreply.github.com' commit -m "feat(core): SystemSnapshot + ISystemMonitorSource + StatsDebounce"
```

---

### Task 2: Adaptive layout math

**Files:**
- Create: `NotifyIsland.Core/StatsLayout.cs`
- Create: `NotifyIsland.Core/OverlayTokens.cs` (append only Ч check the file does not exist first; if it does, append to it)
- Test: `NotifyIsland.Tests/AdaptiveStatsLayoutTests.cs`

**Interfaces:**
- Consumes: `OverlayTokens.StatsMinPillW`, `StatsShowTwoMetricsW`, `StatsShowThreeMetricsW`, `StatsShowAllMetricsW`, `CollapsedW`, `StatsMetricSlotW`, `ExpandedMaxW` Ч all added in this task.
- Produces:
  - `public enum StatsMetricSlot { Cpu, Ram, Battery, Net }`
  - `public static class StatsLayout` with `static int VisibleMetricCount(double availableWidth)`, `static double StatsPillWidth(int visibleMetricCount)`, `static IReadOnlyList<StatsMetricSlot> SlotOrder`
  - New tokens on `OverlayTokens` (see Step 3)

- [ ] **Step 1: Write the failing test**

Create `NotifyIsland.Tests/AdaptiveStatsLayoutTests.cs`:

```csharp
using Xunit;

namespace NotifyIsland.Tests;

public class AdaptiveStatsLayoutTests
{
    [Fact]
    public void UnderMin_HidesRow()
    {
        Assert.Equal(0, StatsLayout.VisibleMetricCount(OverlayTokens.StatsMinPillW - 1));
    }

    [Fact]
    public void AtMin_ShowsCpuOnly()
    {
        Assert.Equal(1, StatsLayout.VisibleMetricCount(OverlayTokens.StatsMinPillW));
    }

    [Fact]
    public void AtTwoMetricThreshold_ShowsCpuRam()
    {
        Assert.Equal(2, StatsLayout.VisibleMetricCount(OverlayTokens.StatsShowTwoMetricsW));
    }

    [Fact]
    public void AtThreeMetricThreshold_ShowsCpuRamBattery()
    {
        Assert.Equal(3, StatsLayout.VisibleMetricCount(OverlayTokens.StatsShowThreeMetricsW));
    }

    [Fact]
    public void AtAllMetricThreshold_ShowsAll()
    {
        Assert.Equal(4, StatsLayout.VisibleMetricCount(OverlayTokens.StatsShowAllMetricsW));
    }

    [Fact]
    public void NeverDropsCpu()
    {
        // SlotOrder[0] is CPU and is never dropped; the rest drop from the right.
        Assert.Equal(StatsMetricSlot.Cpu, StatsLayout.SlotOrder[0]);
        Assert.Equal(4, StatsLayout.SlotOrder.Count);
    }

    [Fact]
    public void StatsPillWidth_ClampsToBounds()
    {
        Assert.Equal(OverlayTokens.StatsMinPillW, StatsLayout.StatsPillWidth(0));
        Assert.Equal(OverlayTokens.ExpandedMaxW, StatsLayout.StatsPillWidth(99));
    }

    [Fact]
    public void StatsPillWidth_GrowsWithCount()
    {
        var widths = Enumerable.Range(0, 5).Select(StatsLayout.StatsPillWidth).ToArray();
        for (var i = 1; i < widths.Length; i++)
        {
            Assert.True(widths[i] >= widths[i - 1], $"count {i} narrower than {i - 1}");
        }
    }
}
```

Add `using System.Linq;` at the top of the file.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release --filter "FullyQualifiedName~AdaptiveStatsLayoutTests"`
Expected: FAIL Ч `error CS0103: The name 'StatsLayout' does not exist`

- [ ] **Step 3: Add the tokens**

Append to `NotifyIsland.Core/OverlayTokens.cs`, immediately before the closing brace of the class:

```csharp

    // -- System monitor: sampling (1.12.0) -----------------------------------
    /// <summary>Default sampling period (ms).</summary>
    public const int    StatsRefreshMs            = 1000;
    /// <summary>Settings combo floor; AppSettings.Normalize() clamps to it.</summary>
    public const int    StatsRefreshMinMs        = 500;
    /// <summary>Settings combo ceiling; AppSettings.Normalize() clamps to it.</summary>
    public const int    StatsRefreshMaxMs        = 2000;
    /// <summary>Ignore timer ticks closer together than this (ms).</summary>
    public const int    StatsMinSampleIntervalMs  = 100;
    /// <summary>Rebuild the Process[] cache on this cadence (ms).</summary>
    public const int    StatsProcessCacheMs       = 30_000;
    /// <summary>Percent-change floor below which a CPU reading is reused.</summary>
    public const double StatsDebouncePercent      = 0.5;
    /// <summary>Byte/sec-change floor below which a network reading is reused.</summary>
    public const long   StatsNetRateFloorBps      = 4_096;

    // -- System monitor: layout ----------------------------------------------
    /// <summary>Narrowest pill that still shows one metric slot.</summary>
    public const double StatsMinPillW             = 280.0;
    /// <summary>Px added to the collapsed width per visible metric slot.</summary>
    public const double StatsMetricSlotW          = 56.0;
    /// <summary>Gap kept between the pill and the screen edge (DIP).</summary>
    public const double StatsScreenMarginPx       = 48.0;
    /// <summary>At or above this available width, show CPU + RAM.</summary>
    public const double StatsShowTwoMetricsW      = 380.0;
    /// <summary>At or above this available width, also show Battery.</summary>
    public const double StatsShowThreeMetricsW    = 480.0;
    /// <summary>At or above this available width, also show Net.</summary>
    public const double StatsShowAllMetricsW      = 620.0;
    /// <summary>Idle time before the SystemStats kind self-collapses (ms).</summary>
    public const int    StatsAutoCollapseMs       = 30_000;

    // -- Settings (1.12.0) --------------------------------------------------
    /// <summary>Below this many sidebar sections, hide the search box.</summary>
    public const int    SettingsSearchMinSections = 4;
```

- [ ] **Step 4: Write minimal implementation**

Create `NotifyIsland.Core/StatsLayout.cs`:

```csharp
namespace NotifyIsland;

/// <summary>Which metric a collapsed-pill slot represents. Declaration order is the drop order (last drops first).</summary>
public enum StatsMetricSlot
{
    Cpu,
    Ram,
    Battery,
    Net
}

/// <summary>
/// Pure layout math for the collapsed pill's metric row. Takes the available DIP width of
/// the monitor the pill sits on and answers two questions: how many slots fit, and how wide
/// should the pill be. No platform dependencies, so it is directly unit-testable.
/// </summary>
public static class StatsLayout
{
    /// <summary>Slot order, first-dropped last. CPU is index 0 and is never dropped.</summary>
    public static IReadOnlyList<StatsMetricSlot> SlotOrder { get; } =
        new[] { StatsMetricSlot.Cpu, StatsMetricSlot.Ram, StatsMetricSlot.Battery, StatsMetricSlot.Net };

    /// <summary>How many metric slots fit in the available DIP width. 0 means hide the row entirely.</summary>
    public static int VisibleMetricCount(double availableWidth)
    {
        if (availableWidth < OverlayTokens.StatsMinPillW)           return 0;
        if (availableWidth < OverlayTokens.StatsShowTwoMetricsW)    return 1;
        if (availableWidth < OverlayTokens.StatsShowThreeMetricsW)  return 2;
        if (availableWidth < OverlayTokens.StatsShowAllMetricsW)    return 3;
        return 4;
    }

    /// <summary>Pill width for a given visible metric count, clamped to the pill's width bounds.</summary>
    public static double StatsPillWidth(int visibleMetricCount)
    {
        var w = OverlayTokens.CollapsedW + visibleMetricCount * OverlayTokens.StatsMetricSlotW;
        return Math.Clamp(w, OverlayTokens.StatsMinPillW, OverlayTokens.ExpandedMaxW);
    }
}
```

Add `using System.Collections.Generic;` and `using System.Linq;` if not already present. `System.Linq` is needed for the `IReadOnlyList` array initializer if you prefer `Array.AsReadOnly`; with `ImplicitUsings` enabled in the Core csproj both are already available.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release --filter "FullyQualifiedName~AdaptiveStatsLayoutTests"`
Expected: PASS Ч 8 passed

- [ ] **Step 6: Verify no regression**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release`
Expected: PASS Ч 186 passed (174 + 4 + 8)

- [ ] **Step 7: Commit**

```bash
git add NotifyIsland.Core/StatsLayout.cs NotifyIsland.Core/OverlayTokens.cs NotifyIsland.Tests/AdaptiveStatsLayoutTests.cs
git -c user.name='Leorik69' -c user.email='leorik69@users.noreply.github.com' commit -m "feat(core): StatsLayout + system-monitor tokens"
```

---

### Task 3: SystemMonitorMachine facade

**Files:**
- Create: `NotifyIsland.Core/SystemMonitorMachine.cs`
- Create: `NotifyIsland.Tests/SystemMonitorMachineTests.cs`

**Interfaces:**
- Consumes: `ISystemMonitorSource`, `SystemSnapshot` (Task 1).
- Produces: `public sealed class SystemMonitorMachine : IDisposable` with `SystemSnapshot Snapshot { get; }`, `event Action<SystemSnapshot>? OnSnapshot`, ctor taking `ISystemMonitorSource`, plus `Start()`, `Stop()`, `SetInterval(TimeSpan)`, `SetIncludeAllInterfaces(bool)`, `Dispose()`.

- [ ] **Step 1: Write the failing test**

Create `NotifyIsland.Tests/SystemMonitorMachineTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using Xunit;

namespace NotifyIsland.Tests;

public class SystemMonitorMachineTests
{
    private sealed class FakeSource : ISystemMonitorSource
    {
        public event Action<SystemSnapshot>? SnapshotChanged;
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
            SnapshotChanged?.Invoke(s);
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
        var count = 0;
        machine.OnSnapshot += _ => count++;
        machine.Dispose();
        fake.Fire(SystemSnapshot.Empty);
        Assert.Equal(0, count);
        Assert.True(fake.Disposed);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release --filter "FullyQualifiedName~SystemMonitorMachineTests"`
Expected: FAIL Ч `error CS0103: The name 'SystemMonitorMachine' does not exist`

- [ ] **Step 3: Write minimal implementation**

Create `NotifyIsland.Core/SystemMonitorMachine.cs`:

```csharp
namespace NotifyIsland;

/// <summary>
/// Facade over an <see cref="ISystemMonitorSource"/>. Holds the latest snapshot and re-raises
/// the source's event. Deliberately does no marshalling Ч the Av layer owns the
/// Dispatcher.UIThread.Post hop.
/// </summary>
public sealed class SystemMonitorMachine : IDisposable
{
    private readonly ISystemMonitorSource _source;
    private volatile SystemSnapshot _snapshot = SystemSnapshot.Empty;

    public SystemSnapshot Snapshot => _snapshot;

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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release --filter "FullyQualifiedName~SystemMonitorMachineTests"`
Expected: PASS Ч 8 passed

- [ ] **Step 5: Verify no regression**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release`
Expected: PASS Ч 194 passed (174 + 4 + 8 + 8)

- [ ] **Step 6: Commit**

```bash
git add NotifyIsland.Core/SystemMonitorMachine.cs NotifyIsland.Tests/SystemMonitorMachineTests.cs
git -c user.name='Leorik69' -c user.email='leorik69@users.noreply.github.com' commit -m "feat(core): SystemMonitorMachine facade"
```

---

### Task 4: Windows sampler (the only file that touches platform APIs)

**Files:**
- Create: `WindowsSystemMonitorSource.cs` (repo root, next to the other `Windows*Source.cs`)
- Modify: `NotifyIsland.Av.csproj` only if a `System.Net.NetworkInformation` reference is missing (it is in-box; no change expected)

**Interfaces:**
- Consumes: `ISystemMonitorSource`, `SystemSnapshot`, `StatsDebounce`, `OverlayTokens.Stats*` (Tasks 1Ц2).
- Produces: `public sealed class WindowsSystemMonitorSource : ISystemMonitorSource`, ctor `WindowsSystemMonitorSource(TimeSpan? interval = null)`.

- [ ] **Step 1: Write the file**

Create `WindowsSystemMonitorSource.cs` at the repo root:

```csharp
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
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
        _timer = new System.Threading.Timer(_ => Sample(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        WarmWinForms();
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
        _period = period;
        if (_running) _timer.Change(period, period);
    }

    public void SetIncludeAllInterfaces(bool includeAll) => _includeAllInterfaces = includeAll;

    public void Dispose()
    {
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

        RefreshProcessCacheIfStale(now);

        var totalCpuTicks = TrySample(SampleTotalCpuTicks, 0L);
        var netDown = TrySample(SampleNetBytesReceived, 0L);
        var netUp = TrySample(SampleNetBytesSent, 0L);
        var (ramUsed, ramTotal) = TrySampleRam();

        // Process.TotalProcessorTime.Ticks are 100 ns units
        // (TimeSpan.TicksPerMillisecond ticks per millisecond). Without dividing
        // by TicksPerMillisecond the reading is ~10,000x too large and pins at 100.
        double cpuPercent = 0;
        long netDownBps = 0, netUpBps = 0;
        if (_lastSampleUtc != default)
        {
            var cores = Math.Max(1, Environment.ProcessorCount);
            var tickDelta = totalCpuTicks - _lastTotalCpuTicks;
            var capacityTicks = elapsedMs * cores * TimeSpan.TicksPerMillisecond;
            cpuPercent = tickDelta * 100.0 / capacityTicks;
            netDownBps = (netDown - _lastNetDown) * 1000 / (long)elapsedMs;
            netUpBps = (netUp - _lastNetUp) * 1000 / (long)elapsedMs;
        }

        _lastTotalCpuTicks = totalCpuTicks;
        _lastNetDown = netDown;
        _lastNetUp = netUp;
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
        SnapshotChanged?.Invoke(_current);
    }

    // -- Per-metric samplers ------------------------------------------------
    private static long SampleTotalCpuTicks()
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

    private static long SampleNetBytesReceived()
    {
        long sum = 0;
        foreach (var ni in EnumerateCountedInterfaces())
        {
            try { sum += ni.GetIPv4Statistics().BytesReceived; }
            catch (NetworkInformationException) { /* adapter went away */ }
            catch (PlatformNotSupportedException) { }
        }
        return sum;
    }

    private static long SampleNetBytesSent()
    {
        long sum = 0;
        foreach (var ni in EnumerateCountedInterfaces())
        {
            try { sum += ni.GetIPv4Statistics().BytesSent; }
            catch (NetworkInformationException) { }
            catch (PlatformNotSupportedException) { }
        }
        return sum;
    }

    private static IEnumerable<NetworkInterface> EnumerateCountedInterfaces()
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

    private static void WarmWinForms()
    {
        try
        {
            SystemInformation.PowerStatus.PowerLineStatus.ToString();
        }
        catch { /* platform without WinForms interop; battery stays null */ }
    }

    // -- Helpers ------------------------------------------------------------
    private static T TrySample<T>(Func<T> sampler, T fallback)
    {
        try { return sampler(); }
        catch { return fallback; }
    }

    private static (long Used, long Total) TrySampleRam()
    {
        try { return SamplePhysicalRam(); }
        catch { return (0, 0); }
    }

    private void RefreshProcessCacheIfStale(DateTime now)
    {
        if ((now - _lastProcessRefreshUtc).TotalMilliseconds < OverlayTokens.StatsProcessCacheMs) return;
        _lastProcessRefreshUtc = now;
        DisposeProcessCache();
        try { _cachedProcesses = Process.GetProcesses().Where(p => p.Id > 0).ToArray(); }
        catch { _cachedProcesses = Array.Empty<Process>(); }
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
    private volatile bool _includeAllInterfaces = true;
    private volatile SystemSnapshot _current = SystemSnapshot.Empty;

    private Process[] _cachedProcesses = Array.Empty<Process>();
    private DateTime _lastProcessRefreshUtc;
    private DateTime _lastSampleUtc;
    private long _lastTotalCpuTicks;
    private long _lastNetDown;
    private long _lastNetUp;
}
```

- [ ] **Step 2: Build and fix compile errors**

Run: `dotnet build NotifyIsland.Av.csproj -c Release`
Expected: 0 warnings, 0 errors

If the build reports that `SystemInformation` or `PowerLineStatus` is unreachable, add `using System.Windows.Forms;` (already present above) and confirm `UseWindowsForms=true` is in `NotifyIsland.Av.csproj` (it is).

- [ ] **Step 3: Verify the CPU formula is right by hand**

Read the `Sample()` method. Confirm:

```csharp
var capacityTicks = elapsedMs * cores * TimeSpan.TicksPerMillisecond;
cpuPercent = tickDelta * 100.0 / capacityTicks;
```

Sanity check: a machine at 100% of one core out of `cores` accumulates `elapsedMs * TimeSpan.TicksPerMillisecond` ticks in `elapsedMs` ms. Dividing by `elapsedMs * cores * TicksPerMillisecond` and multiplying by 100 gives `100 / cores` Ч correct per-core-normalized percentage. At full load across all cores the value is 100.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release`
Expected: PASS Ч 194 passed

- [ ] **Step 5: Commit**

```bash
git add WindowsSystemMonitorSource.cs
git -c user.name='Leorik69' -c user.email='leorik69@users.noreply.github.com' commit -m "feat(av): WindowsSystemMonitorSource Ч timer sampler with per-item isolation"
```

---

### Task 5: FSM integration Ч SystemStats kind, SetSystemStats, payload transforms

**Files:**
- Modify: `NotifyIsland.Core/OverlayMachine.cs`
- Test: `NotifyIsland.Tests/AdaptiveStatsLayoutTests.cs` (append the auto-collapse test)

**Interfaces:**
- Consumes: `SystemSnapshot`, `StatsLayout`, `OverlayTokens.StatsAutoCollapseMs` (Tasks 1Ц2).
- Produces:
  - `OverlayKind.SystemStats` (12th enum member, appended)
  - `OverlayCommand.SetSystemStats` (19th enum member, appended)
  - `OverlayPayload.SystemStats` (`SystemSnapshot?`) and `OverlayPayload.AutoCollapse` (`bool`, default `true`)
  - `OverlayMachine.StatsMetricCount { get; set; }` (`int`, default 0)
  - `OverlayMachine.WidthFor(OverlayKind, bool, bool, int cycleCount, int statsMetricCount)` Ч new trailing optional parameter

- [ ] **Step 1: Write the failing test**

Append to `NotifyIsland.Tests/AdaptiveStatsLayoutTests.cs`:

```csharp
    [Fact]
    public void SystemStats_AutoCollapseFlagRespected()
    {
        var m = new OverlayMachine();
        var snap = new SystemSnapshot { CpuPercent = 20, RamTotalBytes = 1024, CapturedAt = DateTimeOffset.UtcNow };

        // AutoCollapse on > collapses back to Idle after the token interval.
        m.Dispatch(OverlayCommand.SetSystemStats, new OverlayPayload
        {
            SystemStats = snap,
            AutoCollapse = true
        });
        Assert.Equal(OverlayKind.SystemStats, m.Snapshot().Kind);
        m.Tick(OverlayTokens.StatsAutoCollapseMs + 1);
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);

        // AutoCollapse off > stays expanded.
        m.Dispatch(OverlayCommand.SetSystemStats, new OverlayPayload
        {
            SystemStats = snap,
            AutoCollapse = false
        });
        m.Tick(OverlayTokens.StatsAutoCollapseMs + 1);
        Assert.Equal(OverlayKind.SystemStats, m.Snapshot().Kind);
    }

    [Fact]
    public void SystemStats_SetCommand_CarriesSnapshotThroughToSnapshot()
    {
        var m = new OverlayMachine();
        var snap = new SystemSnapshot { CpuPercent = 42.5, RamTotalBytes = 4096, CapturedAt = DateTimeOffset.UtcNow };
        m.Dispatch(OverlayCommand.SetSystemStats, new OverlayPayload
        {
            SystemStats = snap,
            AutoCollapse = false
        });
        var outSnap = m.Snapshot();
        Assert.NotNull(outSnap.Payload.SystemStats);
        Assert.Equal(42.5, outSnap.Payload.SystemStats!.CpuPercent);
        Assert.Equal(4096L, outSnap.Payload.SystemStats.RamTotalBytes);
    }
```

Add `using System;` to the top of the file if it is not already there.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release --filter "FullyQualifiedName~AdaptiveStatsLayoutTests"`
Expected: FAIL Ч `error CS0117: 'OverlayCommand' does not contain a definition for 'SetSystemStats'`

- [ ] **Step 3: Add the enum members**

In `NotifyIsland.Core/OverlayMachine.cs`, append `SystemStats` to `OverlayKind` and `SetSystemStats` to `OverlayCommand`, both at the end of their respective lists.

- [ ] **Step 4: Add the payload fields**

In the `OverlayPayload` class, add:

```csharp
    /// <summary>Live machine metrics; null when System Stats is disabled or sampling failed.</summary>
    public SystemSnapshot? SystemStats { get; set; }
    /// <summary>Mirrors AppSettings.SystemStatsAutoCollapse. Assigned after Apply so it is authoritative.</summary>
    public bool AutoCollapse { get; set; } = true;
```

- [ ] **Step 5: Extend all three payload transforms**

In `OverlayMachine.cs`, find each of the three and add the two new fields. This is the step most likely to silently drop the feature, so check all three.

`Apply(OverlayPayload data)` Ч add inside the method body:

```csharp
        SystemStats = data.SystemStats;
        AutoCollapse = data.AutoCollapse;
```

`Clone(OverlayPayload p)` Ч add inside the object initializer:

```csharp
        SystemStats = p.SystemStats,
        AutoCollapse = p.AutoCollapse,
```

`Sanitize(OverlayPayload raw)` Ч add inside the returned object initializer:

```csharp
            SystemStats = raw.SystemStats is { CpuPercent: var cpu } && double.IsFinite(cpu)
                ? raw.SystemStats with { CpuPercent = Math.Round(Math.Clamp(cpu, 0, 100), 1) }
                : null,
            AutoCollapse = raw.AutoCollapse,
```

- [ ] **Step 6: Add the dispatch case**

Add to the `Dispatch` switch, after the `SetBattery` case:

```csharp
            case OverlayCommand.SetSystemStats:
                if (data.SystemStats is null) break;
                if (_kind != OverlayKind.SystemStats)
                    _returnTo = _kind == OverlayKind.Collapsed ? OverlayKind.Idle : _kind;
                _kind = OverlayKind.SystemStats;
                Apply(data);
                _payload.AutoCollapse = data.AutoCollapse;   // authoritative; set after Apply
                _statsIdleMs = 0.0;
                break;
```

Add the field declaration next to the other private fields:

```csharp
    private double _statsIdleMs;
```

- [ ] **Step 7: Add the Tick gate**

In `Tick(int deltaMs)`, add before the existing notification/battery/clipboard block:

```csharp
        if (_kind == OverlayKind.SystemStats)
        {
            _statsIdleMs += deltaMs;
            if (_payload.AutoCollapse && _statsIdleMs >= OverlayTokens.StatsAutoCollapseMs)
            {
                _kind = OverlayKind.Idle;
                _statsIdleMs = 0.0;
            }
        }
```

- [ ] **Step 8: Add the metric-count property and extend WidthFor**

Next to the existing `WeatherEnabled` property, add:

```csharp
    /// <summary>How many metric slots the collapsed pill shows. 0 hides the row. Set by the Av layer.</summary>
    public int StatsMetricCount { get; set; }
```

In `WidthFor`, add the new trailing optional parameter and the SystemStats branch:

```csharp
    public static double WidthFor(OverlayKind kind, bool weatherEnabled = false,
                                  bool batteryChip = false, int cycleCount = 0,
                                  int statsMetricCount = 0)
    {
        if (kind == OverlayKind.SystemStats)
            return StatsLayout.StatsPillWidth(statsMetricCount);
        var w = kind switch
        {
            // ... existing arms unchanged ...
        };
        // ... existing tail unchanged ...
    }
```

- [ ] **Step 9: Run test to verify it passes**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release --filter "FullyQualifiedName~AdaptiveStatsLayoutTests"`
Expected: PASS Ч 10 passed

- [ ] **Step 10: Verify no regression**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release`
Expected: PASS Ч 196 passed

- [ ] **Step 11: Commit**

```bash
git add NotifyIsland.Core/OverlayMachine.cs NotifyIsland.Tests/AdaptiveStatsLayoutTests.cs
git -c user.name='Leorik69' -c user.email='leorik69@users.noreply.github.com' commit -m "feat(core): SystemStats kind, SetSystemStats, payload transforms"
```

---

### Task 6: AppSettings keys and Normalize clamps

**Files:**
- Modify: `NotifyIsland.Core/AppSettings.cs`
- Test: `NotifyIsland.Tests/AppSettingsTests.cs` (append)

**Interfaces:**
- Consumes: `OverlayTokens.StatsRefreshMinMs`, `StatsRefreshMaxMs`, `SettingsSearchMinSections` (Task 2).
- Produces: `bool SystemStatsEnabled`, `int SystemStatsRefreshMs`, `bool SystemStatsAutoCollapse`, `bool SystemStatsAllInterfaces`, `bool SettingsSearchEnabled` on `AppSettings`.

- [ ] **Step 1: Write the failing test**

Append to `NotifyIsland.Tests/AppSettingsTests.cs`:

```csharp
    [Fact]
    public void Normalize_ClampsSystemStatsRefreshMs()
    {
        var low = new AppSettings { SystemStatsRefreshMs = 10 };
        low.Normalize();
        Assert.Equal(OverlayTokens.StatsRefreshMinMs, low.SystemStatsRefreshMs);

        var high = new AppSettings { SystemStatsRefreshMs = 99_999 };
        high.Normalize();
        Assert.Equal(OverlayTokens.StatsRefreshMaxMs, high.SystemStatsRefreshMs);

        var mid = new AppSettings { SystemStatsRefreshMs = 750 };
        mid.Normalize();
        Assert.Equal(750, mid.SystemStatsRefreshMs);
    }

    [Fact]
    public void Default_StatsKeys_AreTrue()
    {
        var s = new AppSettings();
        s.Normalize();
        Assert.True(s.SystemStatsEnabled);
        Assert.True(s.SystemStatsAutoCollapse);
        Assert.True(s.SystemStatsAllInterfaces);
        Assert.True(s.SettingsSearchEnabled);
    }

    [Fact]
    public void Defaults_StatsKeys_AreTrueForExistingInstalls()
    {
        // A settings.json written before 1.12.0 has none of the new keys.
        var json = "{\"fontSize\":14,\"dateFormat\":\"DayMonth\"}";
        var s = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        s.Normalize();
        Assert.True(s.SystemStatsEnabled);
        Assert.True(s.SystemStatsAutoCollapse);
        Assert.True(s.SystemStatsAllInterfaces);
        Assert.True(s.SettingsSearchEnabled);
        Assert.Equal(14, s.FontSize);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release --filter "FullyQualifiedName~Normalize_ClampsSystemStatsRefreshMs"`
Expected: FAIL Ч `error CS0117: 'AppSettings' does not contain a definition for 'SystemStatsRefreshMs'`

- [ ] **Step 3: Add the properties**

In `AppSettings`, add near the other appearance settings:

```csharp
    /// <summary>Show live CPU / RAM / battery / network in the collapsed pill.</summary>
    public bool SystemStatsEnabled { get; set; } = true;
    /// <summary>Sampling period in ms. Clamped to [StatsRefreshMinMs, StatsRefreshMaxMs] by Normalize().</summary>
    public int SystemStatsRefreshMs { get; set; } = OverlayTokens.StatsRefreshMs;
    /// <summary>Self-collapse the SystemStats kind back to Idle after StatsAutoCollapseMs.</summary>
    public bool SystemStatsAutoCollapse { get; set; } = true;
    /// <summary>Count virtual / tunnel / loopback network interfaces in the net metric.</summary>
    public bool SystemStatsAllInterfaces { get; set; } = true;
    /// <summary>Show the sidebar search box when there are enough sections to filter.</summary>
    public bool SettingsSearchEnabled { get; set; } = true;
```

- [ ] **Step 4: Add the CopyTo lines**

In `CopyTo(AppSettings target)`, add:

```csharp
        target.SystemStatsEnabled = SystemStatsEnabled;
        target.SystemStatsRefreshMs = SystemStatsRefreshMs;
        target.SystemStatsAutoCollapse = SystemStatsAutoCollapse;
        target.SystemStatsAllInterfaces = SystemStatsAllInterfaces;
        target.SettingsSearchEnabled = SettingsSearchEnabled;
```

- [ ] **Step 5: Add the Normalize clamps**

In `Normalize()`, add:

```csharp
        SystemStatsRefreshMs = Math.Clamp(SystemStatsRefreshMs,
            OverlayTokens.StatsRefreshMinMs, OverlayTokens.StatsRefreshMaxMs);
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release --filter "FullyQualifiedName~AppSettingsTests"`
Expected: PASS Ч all AppSettings tests pass, including the 3 new ones

- [ ] **Step 7: Verify no regression**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release`
Expected: PASS Ч 199 passed

- [ ] **Step 8: Commit**

```bash
git add NotifyIsland.Core/AppSettings.cs NotifyIsland.Tests/AppSettingsTests.cs
git -c user.name='Leorik69' -c user.email='leorik69@users.noreply.github.com' commit -m "feat(core): System Stats settings keys + Normalize clamps"
```

---

### Task 7: Settings UI Ч two new sections, search, live validation, import/export

**Files:**
- Modify: `SettingsWindow.axaml`
- Modify: `SettingsWindow.axaml.cs`
- Test: none (UI-only; verified by build + manual checklist)

**Interfaces:**
- Consumes: the five `AppSettings` properties from Task 6, `OverlayTokens.SettingsSearchMinSections` (Task 2).
- Produces: `Panel_system` and `Panel_about` sidebar sections; `SystemStatsEnabledBox`, `SystemStatsRefreshBox`, `SystemStatsAutoCollapseBox`, `SystemStatsAllInterfacesBox`, `SettingsSearchBox`; `NavEntries` gains `("system", "activity")` and `("about", "info")`.

- [ ] **Step 1: Add the nav rail items**

In `SettingsWindow.axaml`, after the `Tag="clipboard"` `ListBoxItem`, add:

```xml
          <ListBoxItem Tag="system">
            <StackPanel Orientation="Horizontal" Spacing="10">
              <Image x:Name="NavIcon_system" Width="18" Height="18" Classes="navIcon"/>
              <TextBlock Text="—истема" VerticalAlignment="Center"/>
            </StackPanel>
          </ListBoxItem>
          <ListBoxItem Tag="about">
            <StackPanel Orientation="Horizontal" Spacing="10">
              <Image x:Name="NavIcon_about" Width="18" Height="18" Classes="navIcon"/>
              <TextBlock Text="ќ программе" VerticalAlignment="Center"/>
            </StackPanel>
          </ListBoxItem>
```

- [ ] **Step 2: Add the search box above the nav list**

Directly above the `<ListBox x:Name="NavList"`, add:

```xml
        <TextBox x:Name="SettingsSearchBox"
                 Classes="navSearch"
                 Watermark="ѕоиск раздела"
                 Margin="12,8,12,4" />
```

- [ ] **Step 3: Add the two content panels**

After the closing `</ScrollViewer>` of `Panel_clipboard`, add:

```xml
        <ScrollViewer x:Name="Panel_system" Classes="tabBody" IsVisible="False">
          <StackPanel Spacing="12">
            <Border Classes="settingsCard">
              <StackPanel Spacing="8">
                <TextBlock Classes="sectionTitle" Text="—истема"/>
                <TextBlock Classes="mutedNote"
                           Text="ћетрики снимаютс€ локально раз в интервал. Ќикаких сетевых запросов Ч только чтение счЄтчиков Windows."/>
                <CheckBox x:Name="SystemStatsEnabledBox" Content="ѕоказывать метрики в островке"/>
                <TextBlock Classes="fieldLabel" Text="»нтервал обновлени€"/>
                <ComboBox x:Name="SystemStatsRefreshBox" Width="200">
                  <ComboBoxItem Tag="500" Content="500 мс"/>
                  <ComboBoxItem Tag="1000" Content="1 с"/>
                  <ComboBoxItem Tag="2000" Content="2 с"/>
                </ComboBox>
                <CheckBox x:Name="SystemStatsAutoCollapseBox" Content="јвтоскрытие после 30 с"/>
                <CheckBox x:Name="SystemStatsAllInterfacesBox" Content="—читать виртуальные интерфейсы"/>
              </StackPanel>
            </Border>
          </StackPanel>
        </ScrollViewer>
        <ScrollViewer x:Name="Panel_about" Classes="tabBody" IsVisible="False">
          <StackPanel Spacing="12">
            <Border Classes="settingsCard">
              <StackPanel Spacing="8">
                <TextBlock Classes="sectionTitle" Text="ќ программе"/>
                <TextBlock x:Name="AboutVersionText" Classes="mutedNote" Text="NotifyIsland 1.12.0"/>
                <TextBlock x:Name="AboutRuntimeText" Classes="mutedNote" Text=".NET 8 / Avalonia 11"/>
                <TextBlock x:Name="AboutRepoText" Classes="mutedNote"
                           Text="https://github.com/Leorik69/notifyisland-avalonia"/>
                <StackPanel Orientation="Horizontal" Spacing="8">
                  <Button x:Name="AboutExportButton" Content="Ёкспорт настроек" Click="OnExportSettings"/>
                  <Button x:Name="AboutImportButton" Content="»мпорт настроек" Click="OnImportSettings"/>
                  <Button x:Name="AboutUpdateButton" Content="ѕроверить обновлени€" IsEnabled="False"/>
                </StackPanel>
                <TextBlock Classes="mutedNote" Text="ѕроверка обновлений отключена: сборка полностью офлайн."/>
              </StackPanel>
            </Border>
          </StackPanel>
        </ScrollViewer>
```

- [ ] **Step 4: Register the nav entries**

In `SettingsWindow.axaml.cs`, in the `NavEntries` array, after the `("clipboard", "clipboard")` entry, add:

```csharp
        ("system", "activity"),
        ("about", "info"),
```

If `activity.svg` and `info.svg` are not present under `Assets/Icons/Lucide/`, vendor them from the Lucide set (ISC) and add the attribution line to `Assets/Icons/NOTICE`, per AGENTS.md rule 6.

- [ ] **Step 5: Wire LoadUi**

In `LoadUi()`, after the clipboard bindings, add:

```csharp
        SystemStatsEnabledBox.IsChecked = _draft.SystemStatsEnabled;
        SelectByTag(SystemStatsRefreshBox, _draft.SystemStatsRefreshMs.ToString(CultureInfo.InvariantCulture));
        SystemStatsAutoCollapseBox.IsChecked = _draft.SystemStatsAutoCollapse;
        SystemStatsAllInterfacesBox.IsChecked = _draft.SystemStatsAllInterfaces;
        AboutVersionText.Text = $"NotifyIsland {typeof(AppSettings).Assembly.GetName().Version?.ToString(3) ?? "1.12.0"}";
        AboutRuntimeText.Text = $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} Ј Avalonia 11";
        AboutRepoText.Text = "https://github.com/Leorik69/notifyisland-avalonia";
        SettingsSearchBox.IsVisible = NavEntries.Length >= OverlayTokens.SettingsSearchMinSections;
```

- [ ] **Step 6: Wire ReadUi**

In `ReadUi()`, after the clipboard bindings, add:

```csharp
        _draft.SystemStatsEnabled = SystemStatsEnabledBox.IsChecked == true;
        if (int.TryParse(SelectedTag(SystemStatsRefreshBox), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var statsMs))
            _draft.SystemStatsRefreshMs = statsMs;
        _draft.SystemStatsAutoCollapse = SystemStatsAutoCollapseBox.IsChecked == true;
        _draft.SystemStatsAllInterfaces = SystemStatsAllInterfacesBox.IsChecked == true;
```

- [ ] **Step 7: Add the search filter**

In `SettingsWindow.axaml.cs`, add the handler and call it from the ctor's `WireNav()`:

```csharp
    private void OnSettingsSearchChanged(object? sender, TextChangedEventArgs e)
    {
        var q = SettingsSearchBox.Text?.Trim() ?? "";
        foreach (var item in NavList.Items.OfType<ListBoxItem>())
        {
            var label = item.Content is StackPanel sp && sp.Children.OfType<TextBlock>().FirstOrDefault()?.Text;
            item.IsVisible = q.Length == 0
                || (label?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }
```

In the ctor, immediately after `WireNav();`, add:

```csharp
        SettingsSearchBox.TextChanged += OnSettingsSearchChanged;
```

- [ ] **Step 8: Add import/export handlers**

In `SettingsWindow.axaml.cs`, add:

```csharp
    private void OnExportSettings(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var path = new Avalonia.Controls.StorageProvider.TopLevelFilePickerSaveOptions
        {
            SuggestedFileName = "notifyisland-settings.json",
            DefaultExtension = "json"
        };
        var window = GetTopLevel(this);
        if (window is null) return;
        window.SaveFilePicker(path).ContinueWith(t =>
        {
            if (t.Status != TaskStatus.RanToCompletion) return;
            using var stream = t.Result.OpenWriteAsync().GetAwaiter().GetResult();
            using var writer = new StreamWriter(stream);
            writer.Write(System.Text.Json.JsonSerializer.Serialize(_draft,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        });
    }

    private void OnImportSettings(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var window = GetTopLevel(this);
        if (window is null) return;
        window.OpenFilePicker(new Avalonia.Controls.StorageProvider.TopLevelFilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new[] { new Avalonia.Controls.StorageProvider.FilePickerFileType("JSON")
                { Patterns = new[] { "*.json" } } }
        }).ContinueWith(t =>
        {
            if (t.Status != TaskStatus.RanToCompletion) return;
            using var stream = t.Result[0].OpenReadAsync().GetAwaiter().GetResult();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            try
            {
                var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is null) return;
                loaded.Normalize();
                Dispatcher.UIThread.Post(() =>
                {
                    loaded.CopyTo(_draft);
                    LoadUi();
                });
            }
            catch (Exception ex)
            {
                AppLog.Warn("settings import failed", ex);
            }
        });
    }
```

- [ ] **Step 9: Build**

Run: `dotnet build NotifyIsland.Av.csproj -c Release`
Expected: 0 warnings, 0 errors. Fix any missing `using` (`System.Linq`, `System.Threading.Tasks`, `Avalonia.Controls`, `Avalonia.Input`, `System.IO`).

- [ ] **Step 10: Commit**

```bash
git add SettingsWindow.axaml SettingsWindow.axaml.cs Assets/Icons/
git -c user.name='Leorik69' -c user.email='leorik69@users.noreply.github.com' commit -m "polish(ui): Settings sections —истема + ќ программе, search, import/export"
```

---

### Task 8: Pill UI Ч metric row, expanded kind, click-to-expand

**Files:**
- Modify: `OverlayWindow.axaml`
- Modify: `OverlayWindow.axaml.cs`
- Test: none (UI-only; verified by build + manual checklist)

**Interfaces:**
- Consumes: `SystemMonitorMachine` (Task 3), `WindowsSystemMonitorSource` (Task 4), `StatsLayout` (Task 2), `OverlayKind.SystemStats` / `OverlayCommand.SetSystemStats` (Task 5).
- Produces: `SystemStatsPanel` and its five child controls in `CollapsedRow`; `SystemStatsExpanded` in the expanded panel.

- [ ] **Step 1: Add the metric row to the collapsed pill**

In `OverlayWindow.axaml`, at the **start** of the `CollapsedRow` `StackPanel`, before `ClockText`, add:

```xml
        <StackPanel x:Name="SystemStatsPanel"
                    Orientation="Horizontal"
                    Spacing="6"
                    VerticalAlignment="Center"
                    IsVisible="False">
          <TextBlock x:Name="StatsCpuText"  VerticalAlignment="Center" FontSize="11" FontWeight="Medium" Text="Ч"/>
          <TextBlock x:Name="StatsRamText"  VerticalAlignment="Center" FontSize="11" FontWeight="Medium" Text="Ч"/>
          <TextBlock x:Name="StatsBatteryText" VerticalAlignment="Center" FontSize="11" FontWeight="Medium" Text="Ч"/>
          <TextBlock x:Name="StatsNetText"  VerticalAlignment="Center" FontSize="11" FontWeight="Medium" Text="Ч"/>
        </StackPanel>
```

- [ ] **Step 2: Add the expanded metric block**

Inside the expanded content panel of `OverlayWindow.axaml` (the one holding `OverlayTitle` / `OverlaySubtitle`), add after `OverlaySubtitle`:

```xml
        <StackPanel x:Name="SystemStatsExpanded" IsVisible="False" Spacing="6" Margin="0,8,0,0">
          <TextBlock x:Name="StatsExCpuText"     FontSize="12" Text="CPU Ч"/>
          <TextBlock x:Name="StatsExRamText"     FontSize="12" Text="RAM Ч"/>
          <TextBlock x:Name="StatsExBatteryText" FontSize="12" Text="Ѕатаре€ Ч"/>
          <TextBlock x:Name="StatsExNetText"     FontSize="12" Text="—еть Ч"/>
        </StackPanel>
```

- [ ] **Step 3: Compose and subscribe in OverlayWindow**

In `OverlayWindow.axaml.cs`, add the field next to the other service fields:

```csharp
    private SystemMonitorMachine? _statsMachine;
```

In the ctor, after the clipboard source wiring, add:

```csharp
        _statsMachine = new SystemMonitorMachine(
            new WindowsSystemMonitorSource(
                TimeSpan.FromMilliseconds(_settings.SystemStatsRefreshMs)))
        {
        };
        _statsMachine.OnSnapshot += OnStatsSnapshot;
        _statsMachine.SetIncludeAllInterfaces(_settings.SystemStatsAllInterfaces);
```

In the `Opened` handler, after the clipboard source start, add:

```csharp
            if (_settings.SystemStatsEnabled)
                _statsMachine?.Start();
```

- [ ] **Step 4: Add the snapshot handler**

In `OverlayWindow.axaml.cs`, add:

```csharp
    private void OnStatsSnapshot(SystemSnapshot s)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var overlayOn = _machine.Snapshot().Kind is not (OverlayKind.Idle or OverlayKind.Collapsed);
            if (!_settings.SystemStatsEnabled || overlayOn)
            {
                SystemStatsPanel.IsVisible = false;
                SystemStatsExpanded.IsVisible = false;
                return;
            }

            var screen = Screens.All.FirstOrDefault(s => s.WorkingArea.Contains(new PixelPoint(
                (int)Position.X, (int)Position.Y)));
            var available = (screen?.WorkingArea.Width ?? OverlayTokens.StatsShowAllMetricsW)
                            - OverlayTokens.StatsScreenMarginPx;
            var count = StatsLayout.VisibleMetricCount(available);
            _machine.StatsMetricCount = count;
            SystemStatsPanel.IsVisible = count > 0;

            var slot = StatsLayout.SlotOrder;
            StatsCpuText.Text     = count > 0 ? $"{s.CpuPercent:F0}%" : "";
            StatsRamText.Text     = count > 1 && s.RamTotalBytes > 0
                ? $"{s.RamUsedBytes / 1_000_000_000.0:F1}/{s.RamTotalBytes / 1_000_000_000.0:F0} GB" : "";
            StatsBatteryText.Text = count > 2 && s.BatteryPercent is { } bp ? $"{bp:F0}%" : "";
            StatsNetText.Text     = count > 3 && s.NetDownBytesPerSec > 0
                ? $"v{s.NetDownBytesPerSec / 1_000_000.0:F1}" : "";

            if (_machine.Snapshot().Kind == OverlayKind.SystemStats)
            {
                SystemStatsExpanded.IsVisible = true;
                StatsExCpuText.Text     = $"CPU      {s.CpuPercent:F0}%";
                StatsExRamText.Text     = s.RamTotalBytes > 0
                    ? $"RAM      {s.RamUsedBytes / 1_000_000_000.0:F1} / {s.RamTotalBytes / 1_000_000_000.0:F0} GB  ({s.RamPercent:F0}%)"
                    : "RAM      Ч";
                StatsExBatteryText.Text = s.BatteryPercent is { } ebp
                    ? $"Ѕатаре€  {ebp:F0}%{(s.OnAcPower ? "  ?" : "")}" : "Ѕатаре€  Ч";
                StatsExNetText.Text     = $"—еть     v {s.NetDownBytesPerSec / 1_000_000.0:F1} MB/s   ^ {s.NetUpBytesPerSec / 1_000.0:F0} KB/s";
            }
            else
            {
                SystemStatsExpanded.IsVisible = false;
            }
        });
    }
```

- [ ] **Step 5: Dispatch from the click handler**

In `OnPillPointerReleased`, in the `kind is OverlayKind.Idle or OverlayKind.Collapsed` branch, before `HandleIdlePillClick(pos.X)`, add:

```csharp
                if (pos.X >= SystemStatsPanel.Bounds.X
                    && pos.X < SystemStatsPanel.Bounds.X + SystemStatsPanel.Bounds.Width
                    && SystemStatsPanel.IsVisible
                    && _settings.SystemStatsEnabled)
                {
                    _machine.Dispatch(OverlayCommand.SetSystemStats, new OverlayPayload
                    {
                        SystemStats = _statsMachine?.Snapshot ?? SystemSnapshot.Empty,
                        AutoCollapse = _settings.SystemStatsAutoCollapse
                    });
                    IslandSounds.Play(IslandSoundKind.Expand, _settings);
                    ApplySize();
                    Paint();
                    e.Handled = true;
                    return;
                }
```

- [ ] **Step 6: Return to Idle on click of the expanded pill**

In `OnPillPointerReleased`, add a branch after the Idle one:

```csharp
            else if (kind == OverlayKind.SystemStats)
            {
                _machine.Dispatch(OverlayCommand.Collapse);
                IslandSounds.Play(IslandSoundKind.Collapse, _settings);
                ApplySize();
                Paint();
                e.Handled = true;
                return;
            }
```

- [ ] **Step 7: Pass the metric count into WidthFor**

In `IslandLayout.SizeFor`, add an optional trailing parameter and forward it:

```csharp
    public static (double Width, double Height) SizeFor(
        OverlayKind kind, bool weatherEnabled, IslandOrientation orientation,
        IslandEdge edge, bool batteryChip = false, int cycleCount = 0,
        int statsMetricCount = 0)
    {
        var longAxis = OverlayMachine.WidthFor(kind, weatherEnabled, batteryChip, cycleCount, statsMetricCount);
        // ... existing body unchanged ...
    }
```

In `ApplySize` in `OverlayWindow.axaml.cs`, pass `_machine.StatsMetricCount` as the new argument.

- [ ] **Step 8: Apply settings changes live**

In `ApplySettingsFromUi(AppSettings draft)`, after the existing settings application, add:

```csharp
        if (_settings.SystemStatsEnabled) _statsMachine?.Start(); else _statsMachine?.Stop();
        _statsMachine?.SetInterval(TimeSpan.FromMilliseconds(_settings.SystemStatsRefreshMs));
        _statsMachine?.SetIncludeAllInterfaces(_settings.SystemStatsAllInterfaces);
```

- [ ] **Step 9: Dispose on close**

In the `Closed` handler of `OverlayWindow`, add:

```csharp
        _statsMachine?.Dispose();
        _statsMachine = null;
```

- [ ] **Step 10: Build and verify**

Run: `dotnet build NotifyIsland.Av.csproj -c Release`
Expected: 0 warnings, 0 errors

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release`
Expected: PASS Ч 199 passed

- [ ] **Step 11: Manual smoke test**

Per AGENTS.md rule 8, kill any running instance, publish fresh, launch:
1. Copy something so the clipboard pill path is exercised.
2. Confirm the metric row appears to the left of the clock on Idle.
3. Click the metric row > the pill expands to the full readout.
4. Click the expanded pill > returns to Idle.
5. Change Ђ»нтервал обновлени€ї in Settings > the rate changes without a restart.
6. Uncheck Ђ—читать виртуальные интерфейсыї > the net figure changes.
7. Uncheck Ђѕоказывать метрикиї > the row hides.

- [ ] **Step 12: Commit**

```bash
git add OverlayWindow.axaml OverlayWindow.axaml.cs NotifyIsland.Core/IslandLayout.cs
git -c user.name='Leorik69' -c user.email='leorik69@users.noreply.github.com' commit -m "feat(ui): System Stats metric row + expanded kind + click-to-expand"
```

---

### Task 9: ClickPop animation

**Files:**
- Modify: `NotifyIsland.Core/OverlayTokens.cs` (append)
- Modify: `NotifyIsland.Core/AnimationTiming.cs` (append enum members)
- Modify: `NotifyIsland.Core/NotifyAnimStyles.cs` (add `ClickPop`)
- Modify: `NotifyIsland.Core/AppSettings.cs` (two keys)
- Modify: `OverlayWindow.axaml.cs` (apply on chevron/cycle clicks)
- Test: `NotifyIsland.Tests/AnimationEasingTests.cs`

**Interfaces:**
- Consumes: `OverlayTokens.MorphMs` (existing).
- Produces: `int ClickPopMs`, `double ClickPopPeak` tokens; `AnimationAction.ClickPop`, `AnimationAction.FirstAppearWobble` members; `AnimationEasing.ClickPop(double t)`; `AppSettings.AnimClickPop`, `AppSettings.AnimFirstAppearWobble`.

- [ ] **Step 1: Write the failing test**

Create `NotifyIsland.Tests/AnimationEasingTests.cs`:

```csharp
using System;
using System.Linq;
using Xunit;

namespace NotifyIsland.Tests;

public class AnimationEasingTests
{
    [Fact]
    public void ClickPop_StartsAtOne()
    {
        Assert.Equal(1.0, AnimationEasing.ClickPop(0.0), 3);
    }

    [Fact]
    public void ClickPop_EndsAtOne()
    {
        Assert.Equal(1.0, AnimationEasing.ClickPop(1.0), 3);
    }

    [Fact]
    public void ClickPop_PeaksAtClickPopPeak()
    {
        var peak = Enumerable.Range(0, 101).Select(i => AnimationEasing.ClickPop(i / 100.0)).Max();
        Assert.Equal(OverlayTokens.ClickPopPeak, peak, 2);
    }

    [Fact]
    public void ClickPop_DoesNotDipBelowOne()
    {
        for (var i = 0; i <= 100; i++)
        {
            Assert.True(AnimationEasing.ClickPop(i / 100.0) >= 1.0, $"t={i / 100.0} dipped below 1.0");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release --filter "FullyQualifiedName~AnimationEasingTests"`
Expected: FAIL Ч `error CS0103: The name 'ClickPop' does not exist`

- [ ] **Step 3: Add the tokens**

Append to `OverlayTokens.cs`:

```csharp

    // -- Animations (1.12.0) Ч both derived from MorphMs ---------------------
    /// <summary>Click-acknowledgement pop duration (ms). Half the morph it interrupts.</summary>
    public const int    ClickPopMs           = MorphMs / 2;
    /// <summary>Peak scale for the click-acknowledgement pop.</summary>
    public const double ClickPopPeak         = 1.08;
    /// <summary>First-appear wobble duration (ms). Same rhythm as ClickPopMs.</summary>
    public const int    FirstAppearWobbleMs  = MorphMs / 2;
    /// <summary>Horizontal wobble amplitude (DIP) on first appear.</summary>
    public const double FirstAppearWobblePx  = 1.0;
```

- [ ] **Step 4: Append the AnimationAction members**

In `AnimationTiming.cs`, append to the end of `AnimationAction` (ordinals preserved):

```csharp
    ,
    /// <summary>Click-acknowledgement pop on CycleNext / CyclePrev / chevron clicks.</summary>
    ClickPop,
    /// <summary>First-appear horizontal wobble after the pill becomes visible again.</summary>
    FirstAppearWobble
```

Match the existing comma placement in the file: if the previous member is `IconCrossfade` with no trailing comma, add a comma after it and then the two new members.

- [ ] **Step 5: Add ClickPop to NotifyAnimStyles.cs**

Append inside the `AnimationEasing` class. Note: **do not touch the existing `PopScale`** Ч it is used by `NotifyAppearStyle.Pop` and asserted in `AnimationTimingTests.cs:110`.

```csharp
    /// <summary>
    /// Click-acknowledgement pop: starts at 1, peaks at ClickPopPeak in the first third,
    /// settles back to 1. Never dips below 1 (unlike PopScale, which starts at 0.88).
    /// </summary>
    public static double ClickPop(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        const double rise = 0.33;
        if (t < rise)
        {
            var u = t / rise;
            return 1.0 + (OverlayTokens.ClickPopPeak - 1.0) * CubicOut(u);
        }
        var v = (t - rise) / (1.0 - rise);
        return OverlayTokens.ClickPopPeak + (1.0 - OverlayTokens.ClickPopPeak) * CubicOut(v);
    }
```

If `CubicOut` is private in that class, use `1.0 - Math.Pow(1.0 - v, 3.0)` inline instead.

- [ ] **Step 6: Add the AppSettings keys**

In `AppSettings`, add:

```csharp
    /// <summary>Speed of the click-acknowledgement pop.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AnimationSpeed AnimClickPop { get; set; } = AnimationSpeed.Normal;
    /// <summary>Speed of the first-appear wobble.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AnimationSpeed AnimFirstAppearWobble { get; set; } = AnimationSpeed.Normal;
```

Add to `CopyTo`:

```csharp
        target.AnimClickPop = AnimClickPop;
        target.AnimFirstAppearWobble = AnimFirstAppearWobble;
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release --filter "FullyQualifiedName~AnimationEasingTests"`
Expected: PASS Ч 4 passed

- [ ] **Step 8: Verify no regression Ч especially the existing PopScale tests**

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release`
Expected: PASS Ч 203 passed (199 + 4). The pre-existing `PopScale` assertions in `AnimationTimingTests.cs:110` must still pass untouched.

- [ ] **Step 9: Apply the pop in OverlayWindow**

In `OverlayWindow.axaml.cs`, add a helper and call it from the cycle paths:

```csharp
    private void PlayClickPop()
    {
        var speed = AnimationTiming.Effective(_settings.AnimationSpeed, _settings.AnimClickPop);
        if (!AnimationTiming.IsEnabled(speed)) return;
        var ms = AnimationTiming.ScaleMs(OverlayTokens.ClickPopMs, speed);
        var anim = new Avalonia.Animation.DoubleAnimation(1.0, OverlayTokens.ClickPopPeak, new Avalonia.Animation.CubicEaseOut())
        { Duration = TimeSpan.FromMilliseconds(ms) };
        anim.Completed += (_, _) =>
        {
            var back = new Avalonia.Animation.DoubleAnimation(OverlayTokens.ClickPopPeak, 1.0, new Avalonia.Animation.CubicEaseOut())
            { Duration = TimeSpan.FromMilliseconds(ms) };
            _pillScale.BeginAnimation(Avalonia.Layout.ScaleTransform.ScaleXProperty, anim);
            _pillScale.BeginAnimation(Avalonia.Layout.ScaleTransform.ScaleYProperty, back);
        };
        _pillScale.BeginAnimation(Avalonia.Layout.ScaleTransform.ScaleXProperty, anim);
        _pillScale.BeginAnimation(Avalonia.Layout.ScaleTransform.ScaleYProperty, anim);
    }
```

Call `PlayClickPop();` at the top of `OnCyclePrevClick` and `OnCycleNextClick`.

- [ ] **Step 10: Commit**

```bash
git add NotifyIsland.Core/OverlayTokens.cs NotifyIsland.Core/AnimationTiming.cs NotifyIsland.Core/NotifyAnimStyles.cs NotifyIsland.Core/AppSettings.cs NotifyIsland.Tests/AnimationEasingTests.cs OverlayWindow.axaml.cs
git -c user.name='Leorik69' -c user.email='leorik69@users.noreply.github.com' commit -m "polish(ui): ClickPop acknowledgement on cycle clicks"
```

---

### Task 10: Hover-peek and tray menu

**Files:**
- Modify: `NotifyIsland.Core/OverlayTokens.cs` (append two peek tokens)
- Modify: `NotifyIsland.Core/HoverPinMachine.cs` (auto-hide timing)
- Modify: `OverlayWindow.axaml.cs` (peek body, pinned badge, auto-hide timer)
- Modify: `TrayService.cs` (menu order + clipboard submenu)
- Test: none (UI-only; manual checklist)

**Interfaces:**
- Consumes: `ClipboardHistory` (existing, from the clipboard PR) for the tray submenu.
- Produces: `int PeekAutoHideMs`, `int PeekMorphMs`, `double PeekExtraFullDateW` tokens.

- [ ] **Step 1: Add the peek tokens**

Append to `OverlayTokens.cs`:

```csharp

    // -- Hover-peek (1.12.0) -----------------------------------------------
    /// <summary>Idle time before an un-pinned hover-peek auto-hides (ms).</summary>
    public const int    PeekAutoHideMs     = 1_200;
    /// <summary>Duration of the peek width morph (ms).</summary>
    public const int    PeekMorphMs        = 200;
    /// <summary>Extra pill width for the full-date row during peek (DIP).</summary>
    public const double PeekExtraFullDateW = 120.0;
```

- [ ] **Step 2: Add the full-date row to the pill**

In `OverlayWindow.axaml`, inside the `CollapsedRow` `StackPanel`, after `DateText`, add:

```xml
        <TextBlock x:Name="PeekFullDateText"
                   VerticalAlignment="Center"
                   FontSize="11"
                   FontWeight="Medium"
                   Foreground="#888890"
                   IsVisible="False"
                   TextTrimming="CharacterEllipsis"
                   MaxWidth="200" />
```

- [ ] **Step 3: Populate the peek content**

In `OverlayWindow.axaml.cs`, in the hover-pin configuration or `TickHoverPin` path, when `_hoverPin.IsContentExpanded` is true, set:

```csharp
        PeekFullDateText.Text = DateFormatHelper.Format(DateTime.Now, DateFormat.FullShort);
        PeekFullDateText.IsVisible = true;
        PlaceIsland();
```

and hide it when peek ends:

```csharp
        PeekFullDateText.IsVisible = false;
        PlaceIsland();
```

- [ ] **Step 4: Add the auto-hide timer**

In `OverlayWindow.axaml.cs`, add the field and wire it in `TickHoverPin`:

```csharp
    private DispatcherTimer? _peekAutoHide;
```

Wherever peek enters the expanded phase, (re)start:

```csharp
        _peekAutoHide?.Stop();
        _peekAutoHide ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(OverlayTokens.PeekAutoHideMs)
        };
        _peekAutoHide.Tick -= OnPeekAutoHide;
        _peekAutoHide.Tick += OnPeekAutoHide;
        _peekAutoHide.Start();
```

```csharp
    private void OnPeekAutoHide(object? sender, EventArgs e)
    {
        if (_hoverPin.IsPinned) return;
        _peekAutoHide?.Stop();
        _hoverPin.PointerLeave();
        PeekFullDateText.IsVisible = false;
        ApplySize();
        Paint();
    }
```

- [ ] **Step 5: Add the pinned badge**

In `OverlayWindow.axaml`, at the end of `CollapsedRow`, add:

```xml
        <Border x:Name="PinnedBadge"
                IsVisible="False"
                CornerRadius="4"
                Background="#28FFFFFF"
                Padding="4,1" VerticalAlignment="Center">
          <TextBlock Text="«акреплено" FontSize="9" Foreground="#C8C8CC"/>
        </Border>
```

In `Paint()`, set `PinnedBadge.IsVisible = _hoverPin.IsPinned;` for the Idle/Collapsed kinds.

- [ ] **Step 6: Rebuild the tray menu**

In `TrayService.cs`, replace the menu construction with:

```csharp
        var menu = new NativeMenu();
        menu.Add(new NativeMenuItem
        {
            Header = _islandVisible ? "—крыть островок" : "ѕоказать островок",
            Command = new NativeMenuCommand { Callback = (_, _) => ToggleIsland() }
        });
        menu.Add(new NativeMenuItemSeparator());

        menu.Add(new NativeMenuItem
        {
            Header = "÷ентр уведомлений",
            Command = new NativeMenuCommand { Callback = (_, _) => OpenActionCenter() }
        });

        var clipItem = new NativeMenuItem { Header = "Ѕуфер обмена" };
        foreach (var entry in _clipboard.SnapshotNewestFirst()
                     .Take(OverlayTokens.TrayClipboardSubmenuItems))
        {
            var captured = entry;
            var label = captured.Kind switch
            {
                ClipboardItemKind.Text => (captured.Text ?? "").Replace('\n', ' ').Replace('\r', ' '),
                ClipboardItemKind.File => System.IO.Path.GetFileName(captured.Paths is { Count: > 0 } ? captured.Paths[0] : ""),
                ClipboardItemKind.MultiFile => $"{captured.Paths?.Count ?? 0} файлов",
                _ => ""
            };
            if (label.Length > 40) label = label[..39] + "Е";
            clipItem.Items.Add(new NativeMenuItem
            {
                Header = label,
                Command = new NativeMenuCommand
                {
                    Callback = (_, _) =>
                    {
                        var ok = captured.Kind == ClipboardItemKind.Text
                            ? WindowsClipboardWriter.WriteText(captured.Text ?? "")
                            : WindowsClipboardWriter.WriteFiles(captured.Paths ?? new List<string>());
                        if (ok) _tray?.RefreshIcon(0);
                    }
                }
            });
        }
        if (clipItem.Items.Count == 0)
            clipItem.Items.Add(new NativeMenuItem { Header = "(пусто)" });
        menu.Add(clipItem);
        menu.Add(new NativeMenuItemSeparator());

        menu.Add(new NativeMenuItem
        {
            Header = _demoOn ? "ƒемо выкл" : "ƒемо вкл",
            Command = new NativeMenuCommand { Callback = (_, _) => ToggleDemo() }
        });
        menu.Add(new NativeMenuItem
        {
            Header = _weatherOn ? "ѕогода выкл" : "ѕогода вкл",
            Command = new NativeMenuCommand { Callback = (_, _) => ToggleWeather() }
        });
        menu.Add(BuildTimerMenu());
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(new NativeMenuItem
        {
            Header = "ЌастройкиЕ",
            Command = new NativeMenuCommand { Callback = (_, _) => OpenSettings() }
        });
        menu.Add(new NativeMenuItem
        {
            Header = "¬ыход",
            Command = new NativeMenuCommand { Callback = (_, _) => Exit() }
        });
```

Adapt the field and method names to whatever `TrayService.cs` actually uses today Ч it already has equivalents of `_islandVisible`, `_demoOn`, `_weatherOn`, `BuildTimerMenu`, `OpenSettings`, `Exit`. `TrayService` needs a reference to the `ClipboardHistory` instance; pass it in from `OverlayWindow` where the tray is constructed.

- [ ] **Step 7: Build and verify**

Run: `dotnet build NotifyIsland.Av.csproj -c Release`
Expected: 0 warnings, 0 errors

Run: `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release`
Expected: PASS Ч 203 passed

- [ ] **Step 8: Commit**

```bash
git add NotifyIsland.Core/OverlayTokens.cs NotifyIsland.Core/HoverPinMachine.cs OverlayWindow.axaml OverlayWindow.axaml.cs TrayService.cs
git -c user.name='Leorik69' -c user.email='leorik69@users.noreply.github.com' commit -m "polish(ui): hover-peek full date + auto-hide, tray menu with clipboard submenu"
```

---

### Task 11: Documentation

**Files:**
- Modify: `docs/ISLAND_GUIDELINES.md`
- Modify: `docs/ISLAND_PREVIEW.md`
- Modify: `CONTEXT.md`
- Modify: `CHANGELOG.md`
- Test: none

- [ ] **Step 1: Update GUIDELINES І2 with the new tokens**

Add a row block to the animation timing table in `docs/ISLAND_GUIDELINES.md` І2, mirroring І3.5 of the spec:

```markdown
| ClickPop (chevron/cycle ack) | **210 мс** (= MorphMs/2) | CubicEaseOut | 1.0 > 1.08 > 1.0, never below 1 |
| First-appear wobble | **210 мс** (= MorphMs/2) | sine | ±1 DIP translate X |
| Peek auto-hide | **1200 мс** | Ч | un-pinned peek collapses |
| Peek width morph | **200 мс** | SoftOut | +120 DIP for the full-date row |
```

Add the system-monitor layout thresholds to the layout section:

```markdown
| Stats metric slot | **56 DIP** | px per visible metric |
| Stats row thresholds | **280 / 380 / 480 / 620 DIP** | 0 / CPU / +RAM / +Battery / +Net |
| Stats screen margin | **48 DIP** | gap kept between pill and screen edge |
```

- [ ] **Step 2: Correct the GUIDELINES І7 sidebar list**

The list currently names 9 sections. Replace it with the 12 that ship in 1.12.0:

```markdown
1. ќстровок Ј 2. ѕогода Ј 3. –асположение Ј 4. “ема Ј 5. ћедиа и питание Ј
6. ќформление Ј 7. јнимации Ј 8. «вуки Ј 9. »конки Ј 10. Ѕуфер обмена Ј
11. —истема Ј 12. ќ программе
```

- [ ] **Step 3: Add the ISLAND_PREVIEW feature entries and the DPI example**

Append to `docs/ISLAND_PREVIEW.md`:

```markdown
## 1.12.0 Ч System Monitor

48a. **ћетрики в островке**: CPU, RAM, батаре€, сеть Ч в р€д слева от часов. ќтсчЄт локальный, раз в настраиваемый интервал, без сети.
49. **јдаптивный р€д**: 1Ц4 слота по ширине монитора. ѕриоритет `CPU > RAM > батаре€ > сеть`; при нехватке места отпадают справа налево, CPU не отпадает никогда.
50. **–аскрытый вид**: клик по р€ду открывает полный отчЄт (4 строки) вместо часов; клик по нему возвращает в Idle.
51. **–аздел Ђ—истемаї**: тумблер, интервал, автоскрытие, учЄт виртуальных интерфейсов. ¬сЄ примен€етс€ без перезапуска.
```

And, for the layout worked example:

```markdown
ѕороги на 1920?1080 при 100 % (доступно 1872 DIP): 4 слота.
ѕри 150 % (доступно 1232 DIP): 4 слота.
Ќа 1280?1024 при 150 % (доступно 805 DIP): 4 слота.
Ќиже 620 DIP р€д сокращаетс€: минус сеть > минус батаре€ > минус RAM. Ќиже 380 DIP остаЄтс€ только CPU.
```

- [ ] **Step 4: Add the CONTEXT.md source-of-truth row**

In the table under Ђ≈диный источник правдыї, after the clipboard row, add:

```markdown
| System monitor | `NotifyIsland.Core/{SystemSnapshot,StatsDebounce,StatsLayout,SystemMonitorMachine}.cs` + `WindowsSystemMonitorSource.cs` |
```

- [ ] **Step 5: Add the CHANGELOG entry**

At the top of `CHANGELOG.md`, under the existing `## Unreleased` heading, add:

```markdown
### Added Ч System monitor (1.12.0)
- Live CPU%, RAM, battery% and network throughput in the collapsed pill, sampled locally every 500Ц2000 ms.
- Adaptive metric row: 1Ц4 slots by monitor width, drop order Net > Battery > RAM > CPU.
- Expanded `SystemStats` kind with a full readout; click the row to open, click again to return.
- Settings section Ђ—истемаї (toggle, interval, auto-collapse, virtual interfaces) and Ђќ программеї (version, repo, import/export).
- Tray menu gained a ЂЅуфер обменаї submenu with the last 5 items, click to re-copy.
- Hover-peek now shows the full date, weather and unread badge, and auto-hides.

### Changed
- `ClickPop` acknowledgement on cycle/chevron clicks. Distinct from the existing `PopScale`, which is unchanged.
- Hover-peek width morphs to fit the full-date row.
```

- [ ] **Step 6: Run the UTF-8 guard**

Run: `python tools/check_utf8.py . --exclude .git,bin,obj,dist,publish`
Expected: `OK: all text files are valid UTF-8.`

- [ ] **Step 7: Verify the full suite one last time**

Run: `dotnet build NotifyIsland.Av.csproj -c Release && dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release`
Expected: 0 warnings, 0 errors; 203 passed

- [ ] **Step 8: Commit**

```bash
git add docs/ISLAND_GUIDELINES.md docs/ISLAND_PREVIEW.md CONTEXT.md CHANGELOG.md
git -c user.name='Leorik69' -c user.email='leorik69@users.noreply.github.com' commit -m "docs: system monitor tokens, sidebar list, 1.12.0 preview, changelog"
```

---

### Task 12: Release build and installer

**Files:**
- Modify: `setup/notifyisland.iss` (version bump)
- Output: `dist/NotifyIsland-Setup-win-x64.exe`, `dist/NotifyIsland-portable-win-x64.zip`

**Interfaces:**
- Consumes: everything above.
- Produces: the two release artifacts.

- [ ] **Step 1: Bump the installer version**

In `setup/notifyisland.iss`, change:

```pascal
#define MyAppVersion "1.11.0"
```

to

```pascal
#define MyAppVersion "1.12.0"
```

- [ ] **Step 2: Bump the assembly version**

In `NotifyIsland.Av.csproj` and `NotifyIsland.Core.csproj`, change `<Version>1.11.0</Version>` (and `1.4.0` in Core) to the same release number so the About panel shows the right value. In `NotifyIsland.Core.csproj` the existing version is `1.4.0`; set both to `1.12.0` if the projects are meant to ship in lockstep, otherwise leave Core alone and only bump Av.

- [ ] **Step 3: Run the FSM python check**

Run: `python tools/test_overlay_states.py`
Expected: all states valid

- [ ] **Step 4: Build the release**

Run: `powershell -ExecutionPolicy Bypass -File setup/pack-release.ps1 -Configuration Release`
Expected: exit 0 and `Inno Setup: Setup.e32 generated successfully`

- [ ] **Step 5: Verify the artifacts**

Run: `Get-ChildItem dist -File | Select-Object Name, Length`
Expected: `NotifyIsland-Setup-win-x64.exe` and `NotifyIsland-portable-win-x64.zip`, both with a fresh timestamp

- [ ] **Step 6: Commit the version bump**

```bash
git add setup/notifyisland.iss NotifyIsland.Av.csproj
git -c user.name='Leorik69' -c user.email='leorik69@users.noreply.github.com' commit -m "build: 1.12.0"
```
