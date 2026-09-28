# NotifyIsland 1.12.0 — System Monitor + Polish Suite

**Date:** 2026-09-28
**Status:** Draft for review (iteration 2 — addresses spec-review findings)
**Branch base:** `feature/clipboard-cycle-pill` (which is ahead of `fix/win11-stability-build`)
**Delivery:** One PR, one branch, six commits

---

## 1. Motivation

NotifyIsland 1.11.0 already covers: pill overlay, weather, SMTC Now Playing, battery alerts, timer/stopwatch, clipboard history, click-pin/hover-peek, settings sidebar, tray. What it lacks:

- **No visibility into machine state.** Users on underpowered laptops have no idea why things stutter. The pill shows a clock but nothing about CPU/RAM/network pressure.
- **Polish debt.** Animations are functional but flat. Settings sidebar is dense with no search. Tray menu is minimal. Hover-peek shows only partial info.

**Goal:** add a System Monitor module (4 metrics, adaptive layout) plus four targeted polish passes, delivered as one atomic PR.

## 2. Scope

### In scope
- System Monitor: CPU%, RAM used/total, Battery%, Network up/down (bytes/sec)
- Adaptive collapsed-pill layout that grows/shrinks based on monitor work-area width
- Four polish items (animations, settings UI, hover-peek, tray menu)

### Out of scope
- CPU temperature (requires WMI or a hardware-monitoring package — separate PR)
- Persisting clipboard history across sessions (separate PR)
- Auto-paste to previous foreground window (separate PR)
- Any new NuGet dependency

### Hard constraints (from AGENTS.md)
- No Open-Meteo
- No island mouse-drag (Edge + OffsetX/Y only)
- No swipe gestures (clicks only)
- No new NuGet dependency
- All numeric animation/layout/timing values live in `NotifyIsland.Core/OverlayTokens.cs` and are mirrored in `docs/ISLAND_GUIDELINES.md`. No bare numeric literals in code or prose.
- Git commits use `git -c user.name='Leorik69' -c user.email='leorik69@users.noreply.github.com'`, never `git config`
- Sandbox deploy publishes to a fresh `Desktop\notifyisland-fresh-<sha>` and kills any old `NotifyIsland.exe` first

## 3. Architecture

### 3.0 Project split (revised after review)

`NotifyIsland.Core` has **no** `UseWindowsForms` and must stay WinRT/WinForms-free. Every existing `Windows*Source.cs` lives at the repo root (the `NotifyIsland.Av` project): `WindowsWeatherSource.cs`, `WindowsPowerSource.cs`, `WindowsClipboardSource.cs`, `WindowsMediaSessionSource.cs`. The new source follows that convention.

| File | Project | Rationale |
|---|---|---|
| `NotifyIsland.Core/SystemSnapshot.cs` | Core | Pure immutable record, no platform deps |
| `NotifyIsland.Core/ISystemMonitorSource.cs` | Core | Platform-agnostic interface, testable |
| `NotifyIsland.Core/StatsDebounce.cs` | Core | Pure static helpers, testable |
| `NotifyIsland.Core/SystemMonitorMachine.cs` | Core | FSM wrapper, testable |
| `WindowsSystemMonitorSource.cs` (repo root) | **Av** | Uses `Process`, `NetworkInterface`, `GlobalMemoryStatusEx` P/Invoke, `SystemInformation` (WinForms) — all require Av |

### 3.1 New Core files

**`NotifyIsland.Core/SystemSnapshot.cs`**

```csharp
public sealed record SystemSnapshot
{
    public double CpuPercent { get; init; }              // 0..100
    public long   RamUsedBytes { get; init; }           // physical RAM in use
    public long   RamTotalBytes { get; init; }          // physical RAM installed
    public double? BatteryPercent { get; init; }        // null on desktops / unknown
    public bool   OnAcPower { get; init; }
    public long   NetUpBytesPerSec { get; init; }
    public long   NetDownBytesPerSec { get; init; }
    public DateTimeOffset CapturedAt { get; init; }

    public static SystemSnapshot Empty { get; } = new();

    public double RamPercent => RamTotalBytes > 0
        ? Math.Clamp(RamUsedBytes * 100.0 / RamTotalBytes, 0, 100)
        : 0;
}
```

**`NotifyIsland.Core/ISystemMonitorSource.cs`**

```csharp
public interface ISystemMonitorSource : IDisposable
{
    /// <summary>Fires on the source's own timer thread. Consumers must marshal to UI themselves.</summary>
    event Action<SystemSnapshot>? SnapshotChanged;
    SystemSnapshot Current { get; }
    void Start();
    void Stop();
    /// <summary>Re-configures the sampling period without restarting the timer.</summary>
    void SetInterval(TimeSpan period);
}
```

**`NotifyIsland.Core/StatsDebounce.cs`** (new — extracted so tests can reach it; `InternalsVisibleTo` would also work but a public static helper is cleaner and reusable by `DigitalClockView` later)

```csharp
public static class StatsDebounce
{
    /// <summary>Reuse <paramref name="previous"/> when |next - previous| is below the threshold.</summary>
    public static double Percent(double previous, double next, double thresholdPct)
        => Math.Abs(next - previous) < thresholdPct ? previous : next;

    /// <summary>Rate variant: reuses the previous byte-rate when below the absolute threshold.</summary>
    public static long Rate(long previous, long next, long thresholdBytesPerSec)
        => Math.Abs(next - previous) < thresholdBytesPerSec ? previous : Math.Max(0, next);
}
```

**`NotifyIsland.Core/SystemMonitorMachine.cs`**

```csharp
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
    public void Stop()  => _source.Stop();
    public void SetInterval(TimeSpan period) => _source.SetInterval(period);

    public void Dispose()
    {
        _source.SnapshotChanged -= OnSourceSnapshot;
        _source.Dispose();
        OnSnapshot = null;
    }
}
```

**First-tick contract:** the first snapshot after `Start()` reports `CpuPercent = 0`, `NetUp/Down = 0`, but carries a real `RamUsedBytes` / `RamTotalBytes` / `BatteryPercent` and a real `CapturedAt`. Rationale: CPU and network are *deltas* and need two samples. This is asserted by a test, not incidental.

### 3.2 New Av file

**`WindowsSystemMonitorSource.cs`** (repo root, next to the other `Windows*Source.cs`)

```csharp
public sealed class WindowsSystemMonitorSource : ISystemMonitorSource
{
    private readonly System.Threading.Timer _timer;
    private TimeSpan _period;

    private Process[] _cachedProcesses = Array.Empty<Process>();
    private DateTime _lastProcessRefreshUtc;
    private DateTime _lastSampleUtc;
    private long   _lastTotalCpuTicks;
    private long   _lastNetDown, _lastNetUp;
    private long   _lastRamTotal;

    private volatile SystemSnapshot _current = SystemSnapshot.Empty;
    public SystemSnapshot Current => _current;
    public event Action<SystemSnapshot>? SnapshotChanged;

    public WindowsSystemMonitorSource(TimeSpan? interval = null)
    {
        _period = interval ?? TimeSpan.FromMilliseconds(OverlayTokens.StatsRefreshMs);
        _timer  = new System.Threading.Timer(_ => Sample(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _timer.Change(_period, _period);
    public void Stop()  => _timer.Change(Timeout.Infinite, Timeout.Infinite);
    public void SetInterval(TimeSpan period) { _period = period; if (IsRunning) _timer.Change(period, period); }

    private void Sample()
    {
        try
        {
            var now = DateTime.UtcNow;
            var elapsedMs = (now - _lastSampleUtc).TotalMilliseconds;
            if (_lastSampleUtc != default && elapsedMs < OverlayTokens.StatsMinSampleIntervalMs)
                return;                                     // guard against burst ticks

            RefreshProcessCacheIfStale(now);

            var totalCpuTicks = SampleTotalCpuTicks();
            var netDown = SampleNetBytesReceived();
            var netUp   = SampleNetBytesSent();

            var cpuPercent = 0.0;
            long netDownBps = 0, netUpBps = 0;
            if (_lastSampleUtc != default)
            {
                var cores = Math.Max(1, Environment.ProcessorCount);
                cpuPercent = (totalCpuTicks - _lastTotalCpuTicks) * 1000.0 / (elapsedMs * cores);
                netDownBps = (netDown - _lastNetDown) * 1000 / (long)elapsedMs;
                netUpBps   = (netUp   - _lastNetUp)   * 1000 / (long)elapsedMs;
            }
            _lastTotalCpuTicks = totalCpuTicks;
            _lastNetDown = netDown; _lastNetUp = netUp;
            _lastSampleUtc = now;

            var snapshot = new SystemSnapshot
            {
                CpuPercent = StatsDebounce.Percent(
                    _current.CpuPercent, Math.Clamp(cpuPercent, 0, 100), OverlayTokens.StatsDebouncePercent),
                RamUsedBytes   = SampleRamUsedBytes(),
                RamTotalBytes  = _lastRamTotal,
                BatteryPercent = SampleBatteryPercent(),
                OnAcPower      = SampleOnAcPower(),
                NetUpBytesPerSec = StatsDebounce.Rate(
                    _current.NetUpBytesPerSec, netUpBps, OverlayTokens.StatsNetRateFloorBps),
                NetDownBytesPerSec = StatsDebounce.Rate(
                    _current.NetDownBytesPerSec, netDownBps, OverlayTokens.StatsNetRateFloorBps),
                CapturedAt = now,
            };
            _current = snapshot;
            SnapshotChanged?.Invoke(snapshot);
        }
        catch
        {
            // Fail-soft: keep _current, never throw to the System.Threading.Timer callback.
        }
    }
}
```

**Physical RAM via `GlobalMemoryStatusEx`** (P/Invoke declared in this Av file, no new package):

```csharp
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
private class MEMORYSTATUSEX
{
    public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
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
private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

private static (long Used, long Total) SamplePhysicalRam()
{
    var ms = new MEMORYSTATUSEX();
    if (!GlobalMemoryStatusEx(ms)) return (0, 0);
    return ((long)(ms.ullTotalPhys - ms.ullAvailPhys), (long)ms.ullTotalPhys);
}
```

`GC.GetGCMemoryInfo()` is **not** used for RAM — it reports the GC heap, not physical memory, and would produce a meaningless ratio.

### 3.3 Metric sources (all pure-managed, no new packages)

| Metric | Source | Notes |
|---|---|---|
| CPU% | `Σ Process.TotalProcessorTime.Ticks` over cached `Process[]` ÷ (elapsed × `Environment.ProcessorCount`) | **Approximate**: processes started after the last cache refresh contribute 0 for up to `OverlayTokens.StatsProcessCacheMs` (30 s). The cache is **rebuilt** (not only pruned) on each refresh, so it does not grow unbounded. |
| RAM used / total | `GlobalMemoryStatusEx` → `ullTotalPhys − ullAvailPhys` / `ullTotalPhys` | True physical RAM. P/Invoke in the Av file. |
| Battery% | `SystemInformation.PowerStatus` (WinForms) — `Av` only | `null` when `PowerStatus.BatteryLifePercent == 255` (desktop / no battery) |
| Net down/up | `Σ` over `NetworkInterface.GetAllNetworkInterfaces()` where `OperationalStatus == Up` and the interface passes the filter below; each contributes `GetIPv4Statistics().BytesReceived` / `.BytesSent` deltas | IPv4 only — `GetIPv4Statistics()` is the API surface that ships in-box with .NET 8; IPv6 is not counted. |

**Network interface filter** (`SystemStatsAllInterfaces = false` → "NonVirtual"):
- **All** (default, `true`): every interface that is `Up`. Includes Wi-Fi, Ethernet, VPN, Hyper-V vNIC, WSL, Tailscale — everything currently carrying traffic.
- **NonVirtual** (`false`): excludes interfaces whose `NetworkInterfaceType` is `Loopback`, `Tunnel`, or `Unknown`, and excludes any whose `Name` or `Description` matches a virtual-adapter regex (`vEthernet|Hyper-V|VirtualBox|VMware|WSL|Tailscale|Loopback|Tunnel|VPN`).

### 3.4 FSM integration

**`NotifyIsland.Core/OverlayMachine.cs`**

```csharp
public enum OverlayKind
{
    // ... existing 11 kinds ...
    SystemStats              // 12th
}

public enum OverlayCommand
{
    // ... existing 17 commands ...
    SetSystemStats,          // 18th
}
```

`OverlayPayload` gains:
```csharp
public SystemSnapshot? SystemStats { get; set; }
public bool AutoCollapse { get; set; } = true;   // mirrored from AppSettings.SystemStatsAutoCollapse
```

`Sanitize(...)` rounds `CpuPercent` to 1 decimal, clamps byte counters to non-negative, and nulls `SystemStats` when `CpuPercent` is NaN.

`Dispatch` for `SetSystemStats`:
```csharp
case OverlayCommand.SetSystemStats:
    if (data.SystemStats is null) break;
    if (_kind != OverlayKind.SystemStats)
        _returnTo = _kind == OverlayKind.Collapsed ? OverlayKind.Idle : _kind;
    _kind = OverlayKind.SystemStats;
    _payload.AutoCollapse = data.AutoCollapse;
    Apply(data);
    _statsIdleMs = 0;
    break;
```

`Tick(deltaMs)` — the auto-collapse is now **gated on the setting**:
```csharp
if (_kind == OverlayKind.SystemStats)
{
    _statsIdleMs += dt;
    if (_payload.AutoCollapse && _statsIdleMs >= OverlayTokens.StatsAutoCollapseMs)
    {
        _kind = OverlayKind.Idle;
        _statsIdleMs = 0;
    }
}
```

`WidthFor(SystemStats, …)`:
```csharp
var w = OverlayTokens.ClockW
      + OverlayTokens.DateW
      + visibleMetricCount * OverlayTokens.StatsMetricSlotW;
return Math.Clamp(w, OverlayTokens.StatsMinPillW, OverlayTokens.ExpandedMaxW);
```
where `visibleMetricCount` is derived from the current monitor's work-area width (see §5.2).

### 3.5 New tokens (`NotifyIsland.Core/OverlayTokens.cs`)

```csharp
// System monitor — sampling
public const int    StatsRefreshMs            = 1000;   // default sampling period
public const int    StatsMinSampleIntervalMs  = 100;    // guard against burst timer ticks
public const int    StatsProcessCacheMs       = 30_000; // rebuild Process[] cache
public const double StatsDebouncePercent      = 0.5;    // percent-change floor
public const long   StatsNetRateFloorBps      = 4_096;  // byte/sec-change floor

// System monitor — layout
public const int    StatsMinPillW             = 280;
public const int    StatsMetricSlotW          = 56;     // px per visible metric
public const int    StatsShowTwoMetricsW      = 380;    // >= this: CPU + RAM
public const int    StatsShowThreeMetricsW    = 480;    // >= this: + Battery
public const int    StatsShowAllMetricsW      = 620;    // >= this: + Net
public const int    StatsAutoCollapseMs       = 30_000;

// Hover-peek (polish C)
public const int    PeekAutoHideMs            = 1_200;
public const int    PeekMorphMs               = 200;
public const int    PeekExtraFullDateW        = 120;

// Animations (polish A)
public const int    PopScaleMs                = 180;
public const double PopScalePeak              = 1.08;
public const int    FirstAppearWobbleMs       = 220;
public const double FirstAppearWobblePx       = 1.0;

// Tray (polish D)
public const int    TrayClipboardSubmenuItems = 5;
```

Type harmonization: existing width tokens (`CollapsedW`, `ExpandedMinW`) are `int` in this file; the new width tokens above match that type. `StatsDebouncePercent` is `double` because it is a ratio, not a pixel.

All values mirrored into `docs/ISLAND_GUIDELINES.md` §2 and §9 in the same PR.

### 3.6 AnimationAction additions (polish A wiring)

`AnimationAction` gains two members so the new animations get per-action speed overrides in Settings → Анимации:

```csharp
public enum AnimationAction
{
    MorphInflate,
    MorphCollapse,
    UnreadPulse,
    IdleBreath,          // (enum value retained; no UI path sets it)
    Hover,
    SwipeRubber,
    IconCrossfade,
    PopScale,            // new
    FirstAppearWobble    // new
}
```

New AppSettings keys `AnimPopScale` and `AnimFirstAppearWobble` (both default `AnimationSpeed.Normal`) are added to `CopyTo` and to the Settings → Анимации per-action list. Both new animations obey `AnimationSpeed = Off` (they collapse to a 1 ms snap, consistent with GUIDELINES §2).

## 4. Data flow

```
[System.Threading.Timer, OverlayTokens.StatsRefreshMs]
  └─ WindowsSystemMonitorSource.Sample()
       ├─ Process cache rebuild (every StatsProcessCacheMs)
       ├─ CPU / RAM / Battery / Net samples
       ├─ StatsDebounce.Percent / StatsDebounce.Rate
       └─ SnapshotChanged?.Invoke(snapshot)          [timer thread]
            └─ SystemMonitorMachine.OnSourceSnapshot  [still timer thread]
                 └─ Dispatcher.UIThread.Post(...)
                      ├─ OverlayMachine.Dispatch(SetSystemStats, payload)
                      └─ ApplySize(); Paint()
                           └─ SystemStatsPanel renders if kind == SystemStats
```

`SystemStatsRefreshMs` is a **live setting**: `SettingsWindow.Apply` calls `SystemMonitorMachine.SetInterval(...)` which forwards to `WindowsSystemMonitorSource.SetInterval`, which calls `_timer.Change(period, period)` in place. No source re-creation, no restart.

## 5. UI

### 5.1 Collapsed pill

```
┌──────────────────────────────────────────────────────┐
│ 18:32  24 сен   ▓▓▓░░ 42%  ▓▓▓▓▓ 8.2/15.9 GB  ↓1.4  │
└──────────────────────────────────────────────────────┘
   clock    date   CPU slot    RAM slot      net slot
```

Priority order (dropped **first** when width-constrained): **Net → Battery → RAM → CPU**. CPU is never dropped.

### 5.2 Where "available width" comes from

The pill is a fixed-width capsule — it is never width-constrained by another element. "Available width" therefore means:

1. Query the working-area width of the monitor the pill currently sits on (`IScreen.WorkingArea.Width` via Avalonia `Screens`, in DIPs).
2. `available = workingAreaW − OverlayTokens.StatsScreenMarginPx` (default 48 px, so the pill never touches the screen edge).
3. The metrics row is shown only when `available >= StatsMinPillW`; otherwise the row hides entirely and the pill is plain clock + date.

The pill's own width is then computed from the **visible metric count**, which is derived deterministically from `available`:

```
count = available >= StatsShowAllMetricsW   ? 4
      : available >= StatsShowThreeMetricsW ? 3
      : available >= StatsShowTwoMetricsW   ? 2
      : 1                                  // CPU only
```

This makes the layout a pure function of `available` — no guessing, no feedback loop between pill width and metric count.

On a 1920×1080 monitor at 100% DPI: `available ≈ 1872` → all 4 metrics. At 150% DPI: `available ≈ 1248` → all 4. On a 1280×1024 laptop at 150%: `available ≈ 805` → all 4. Below 620 px (rare, e.g. a narrow side-by-side windowed setup) the row starts dropping Net, then Battery, then RAM.

### 5.3 Expanded `SystemStats` kind

```
┌───────────────────────────────────────────────┐
│ [icon]  Система                              │
│ CPU      ▓▓▓▓▓▓░░░░  57%                    │
│ RAM      ▓▓▓▓░░░░░░  8.2 / 15.9 GB  (52%)     │
│ Батарея  ▓▓▓▓▓▓░░░░  67%  ⚡                  │
│ Сеть     ↓ 1.4 MB/s   ↑ 220 KB/s             │
└───────────────────────────────────────────────┘
```

**Interaction:** when `SystemStatsEnabled` is true and the pill is in `Idle`, the metrics row renders **inside** the collapsed pill. Clicking any metric slot dispatches `SetSystemStats` and the pill expands to the full readout. Clicking the expanded pill returns to `Idle`.

### 5.4 Settings — new sections

The sidebar currently has **10** sections (verified against `SettingsWindow.axaml`: island, weather, placement, theme, media, look, anim, sound, icons, clipboard). `docs/ISLAND_GUIDELINES.md` §7 still lists 9 — that doc is corrected to 10 in this PR.

Two sections are added:

**«Система» (11th)**

| Control | Type | Default |
|---|---|---|
| Показывать метрики в островке | CheckBox | `true` |
| Интервал обновления | ComboBox 500 / 1000 / 2000 ms | `1000` |
| Автоскрытие через 30 с | CheckBox | `true` |
| Считать виртуальные интерфейсы | CheckBox | `true` |

**«О программе» (12th)** — version, runtime, build timestamp, GitHub URL, `Проверить обновления` button (local-only: compares against the latest tag already present in the repo's release feed, no outbound network — stubbed and disabled for now).

Plus a search box at the top of the nav rail (filters sections by name, case-insensitive substring; hidden when fewer than 4 sections), live validation borders on out-of-range numeric inputs, and Export/Import JSON buttons in the bottom bar.

`AppSettings` gains:
```csharp
public bool SystemStatsEnabled { get; set; } = true;
public int  SystemStatsRefreshMs { get; set; } = OverlayTokens.StatsRefreshMs;  // clamp 500..2000
public bool SystemStatsAutoCollapse { get; set; } = true;
public bool SystemStatsAllInterfaces { get; set; } = true;
public bool SettingsSearchEnabled { get; set; } = true;
```

All five keys are clamped / defaulted inside `Normalize()` in the same PR — this is new code, not "no additional validation". Existing `settings.json` files that lack these keys deserialize with the property initializers, so all four stats keys default to `true` on upgrade without a migration step. A test pins this.

## 6. Polish items

### 6.1 Animations
- `PopScale` on `CycleNext` / `CyclePrev` / chevron clicks: scale 1.0 → 1.08 → 1.0 over `PopScaleMs`, scaled per-action through `AnimationTiming.ScaleMs(PopScaleMs, settings.AnimPopScale)`.
- `FirstAppearWobble`: when the pill first appears after being hidden (fullscreen exit, tray toggle), translate X oscillates ±1 px over `FirstAppearWobbleMs`, scaled through `AnimFirstAppearWobble`.
- New `AnimationEasing.PopScale(t)` in Core, unit-tested for `PopScale(0) == 1.0`, `PopScale(0.5) ≈ 1.08`, `PopScale(1) == 1.0`.
- Both respect `AnimationSpeed = Off` (1 ms snap, no visible motion).

### 6.2 Settings UI
- Sidebar sections 11 («Система») and 12 («О программе»), Lucide nav icons.
- Search box on the nav rail.
- Live validation: red `BorderBrush` on numeric fields whose raw input is out of range before clamping.
- Export / Import `settings.json` via `SaveFileDialog` / `OpenFileDialog`.
- `ISLAND_GUIDELINES.md` §7 sidebar list updated from 9 → 12.

### 6.3 Hover-peek
- Peek body adds full date (`DateFormat.FullShort`), current weather temp when `WeatherEnabled`, and the unread badge when > 0.
- Auto-hide after `PeekAutoHideMs` (1.2 s) of no pointer movement.
- Width morphs by `PeekExtraFullDateW` over `PeekMorphMs`.
- Persistent «Закреплено» badge in the right edge while pinned.

### 6.4 Tray menu
```
Показать/Скрыть островок        (primary)
─────────────
Центр уведомлений
Буфер обмена ▸                  (submenu, last TrayClipboardSubmenuItems, click = re-copy)
─────────────
Демо вкл/выкл
Погода вкл/выкл
Таймер ▸ (existing presets)
─────────────
Настройки…
Выход
```

## 7. Error handling

- Every platform call inside `WindowsSystemMonitorSource.Sample()` is wrapped in a single outer `try/catch`. On exception the previous `_current` snapshot is kept and the timer keeps ticking. The source never throws to its caller.
- `SystemMonitorMachine.Dispose()` unsubscribes from `SnapshotChanged` and sets `OnSnapshot = null`, so a disposed machine is not reachable from the source.
- `_current` is declared `volatile` — written on the timer thread, read on the UI thread.
- `Paint()` treats a null `SystemStats` as "hide the row" — no NRE.
- `AppSettings.Normalize()` (edited in this PR) clamps `SystemStatsRefreshMs` to 500..2000 and coerces the four bools to their declared defaults when the incoming value is out of the enum/domain. Covered by a dedicated test.
- Process cache: `RefreshProcessCacheIfStale` disposes exited processes and swallows `InvalidOperationException` (race with process exit). The cache is fully rebuilt each refresh, so it cannot grow unbounded.
- `NetworkInterface` enumeration can throw on adapters being disabled mid-tick — caught, that adapter contributes 0 for the tick.
- `GlobalMemoryStatusEx` failure → `(Used, Total) = (0, 0)`, `RamPercent` returns 0 and the RAM slot hides itself rather than showing a misleading `0/0 GB`.

## 8. Testing

**`NotifyIsland.Tests/SystemMonitorMachineTests.cs`** (9 tests, fake `ISystemMonitorSource`)

| Test | Asserts |
|---|---|
| `Machine_ForwardsSourceSnapshots` | machine's `Snapshot` matches the fake's last fire |
| `Machine_OnSnapshotRaisesOncePerFire` | event count == fire count |
| `Machine_StartDelegatesToSource` | fake records `Start()` |
| `Machine_StopDelegatesToSource` | fake records `Stop()` |
| `Machine_SetIntervalDelegatesToSource` | fake records the period |
| `Machine_FirstSnapshot_IsZeroNotNull` | first fire yields `CpuPercent == 0` with non-null `Snapshot` and real `CapturedAt` |
| `Machine_DisposeUnsubscribesFromSource` | after `Dispose()`, a source fire does not raise `OnSnapshot` |
| `StatsDebounce_Percent_ReusesPreviousBelowThreshold` | 0.4% delta keeps old |
| `StatsDebounce_Percent_UpdatesAboveThreshold` | 1.0% delta replaces |
| `StatsDebounce_Rate_ReusesBelowFloor` | small byte-rate delta keeps old |
| `StatsDebounce_Rate_NeverNegative` | negative input clamps to 0 |

**`NotifyIsland.Tests/AdaptiveStatsLayoutTests.cs`** (5 tests)

| Test | Asserts |
|---|---|
| `Layout_UnderMin_HidesRow` | `available < StatsMinPillW` → metrics hidden |
| `Layout_380_ShowsCpuRam` | `[Cpu, Ram]` |
| `Layout_480_ShowsCpuRamBattery` | `[Cpu, Ram, Battery]` |
| `Layout_620_ShowsAll` | all four |
| `Layout_NeverDropsCpu` | CPU present at every width ≥ `StatsMinPillW` |

**`NotifyIsland.Tests/AnimationEasingTests.cs`** (populated by polish-A commit, 3 tests)
- `PopScale_StartsAtOne`, `PopScale_PeaksNearMidpoint`, `PopScale_EndsAtOne`
- `FirstAppearWobble_StaysWithinPx` — |x| ≤ `FirstAppearWobblePx` for all t

**`NotifyIsland.Tests/AppSettingsTests.cs`** (3 new tests appended)
- `Normalize_ClampsSystemStatsRefreshMs` — 100 → 500, 5000 → 2000
- `Default_StatsKeys_AreTrue` — fresh `AppSettings` has all four bools `true`
- `Defaults_StatsKeys_AreTrueForExistingInstalls` — deserialize a hand-written `settings.json` that omits the stats keys into a fresh instance, then `Normalize()`; all four stay `true`

**Totals: 174 existing + 11 + 5 + 3 + 3 = 196 tests, 0 failing.**

**Build:** 0 warnings, 0 errors.

## 9. Commit structure (one PR, six commits)

```
1. feat(core): SystemSnapshot + ISystemMonitorSource + StatsDebounce + SystemMonitorMachine
2. feat(av):   WindowsSystemMonitorSource (Process / GlobalMemoryStatusEx / NetworkInterface)
3. feat(core): OverlayMachine SystemStats kind + SetSystemStats + auto-collapse gating
4. feat(ui):   SystemStatsPanel in collapsed pill + expanded kind + adaptive layout
5. polish(ui): animations (PopScale, FirstAppearWobble) + settings sidebar + hover-peek + tray
6. docs:       GUIDELINES tokens + §7 sidebar list + CONTEXT + CHANGELOG
```

## 10. Verification

1. `dotnet build NotifyIsland.Av.csproj -c Release` → 0 warnings, 0 errors
2. `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release` → **196 passed**
3. `python3 tools/test_overlay_states.py` → FSM states valid
4. Manual: run the published build; confirm metrics appear in the collapsed pill; click into `SystemStats`; cycle back to `Idle`; toggle `SystemStatsEnabled` in Settings and confirm the row hides; change `SystemStatsRefreshMs` and confirm the sampling rate changes without a restart.
5. Sandbox: kill old `NotifyIsland.exe`, publish to `Desktop\notifyisland-fresh-<sha>`, launch.
