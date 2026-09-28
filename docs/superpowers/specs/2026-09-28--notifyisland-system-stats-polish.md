# NotifyIsland 1.12.0 — System Monitor + Polish Suite

**Date:** 2026-09-28
**Status:** Draft for review (iteration 3 — addresses spec-review findings from iterations 1 and 2)
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
- Adaptive collapsed-pill layout driven by the current monitor's work-area width
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
- All numeric animation / layout / timing values live in `NotifyIsland.Core/OverlayTokens.cs` and are mirrored in `docs/ISLAND_GUIDELINES.md`. No bare numeric literals in code or in this document.
- **New animation timings (rule 4):** 1.12.0 introduces exactly two new durations, both derived from an existing token rather than invented:
  - `PopScaleMs = MorphMs / 2` (420 / 2 = 210) — a click-acknowledgement is exactly half the morph it interrupts
  - `FirstAppearWobbleMs = MorphMs / 2` — same rhythm, reused so the two feel related
  Both land in `docs/ISLAND_GUIDELINES.md` §2 in commit 6.
- Git commits use `git -c user.name='Leorik69' -c user.email='leorik69@users.noreply.github.com'`, never `git config`
- Sandbox deploy publishes to a fresh `Desktop\notifyisland-fresh-<sha>` and kills any old `NotifyIsland.exe` first

## 3. Architecture

### 3.0 Project split

`NotifyIsland.Core` has **no** `UseWindowsForms` and stays WinRT / WinForms-free. Every existing `Windows*Source.cs` lives at the repo root (the `NotifyIsland.Av` project): `WindowsWeatherSource.cs`, `WindowsPowerSource.cs`, `WindowsClipboardSource.cs`, `WindowsMediaSessionSource.cs`. The new source follows that convention.

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
    public long   RamTotalBytes { get; init; }          // physical RAM installed; 0 = unknown
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
    /// <summary>Re-configures the sampling period in place, without restarting the timer.</summary>
    void SetInterval(TimeSpan period);
    /// <summary>Re-configures the network-interface filter in place.</summary>
    void SetIncludeAllInterfaces(bool includeAll);
}
```

**`NotifyIsland.Core/StatsDebounce.cs`**

Extracted so the tests can reach it — `NotifyIsland.Core.csproj` already has `InternalsVisibleTo("NotifyIsland.Tests")`, but a `public static` helper is cleaner and reusable by `DigitalClockView` later.

```csharp
public static class StatsDebounce
{
    /// <summary>Reuse <paramref name="previous"/> when |next - previous| is below the threshold.</summary>
    public static double Percent(double previous, double next, double thresholdPct)
        => Math.Abs(next - previous) < thresholdPct ? previous : next;

    /// <summary>Rate variant: reuses the previous byte-rate when below the absolute floor.</summary>
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

    public void Start()      => _source.Start();
    public void Stop()       => _source.Stop();
    public void SetInterval(TimeSpan period) => _source.SetInterval(period);
    public void SetIncludeAllInterfaces(bool all) => _source.SetIncludeAllInterfaces(all);

    public void Dispose()
    {
        _source.SnapshotChanged -= OnSourceSnapshot;
        _source.Dispose();
        OnSnapshot = null;
    }
}
```

**First-tick contract:** the first snapshot after `Start()` reports `CpuPercent = 0` and `NetUp/Down = 0`, because those two are *deltas* and need two samples. `RamUsedBytes` / `RamTotalBytes` / `BatteryPercent` / `CapturedAt` are real on the first tick. This is asserted by a test, not incidental.

### 3.2 New Av file

**`WindowsSystemMonitorSource.cs`** (repo root, next to the other `Windows*Source.cs`)

```csharp
public sealed class WindowsSystemMonitorSource : ISystemMonitorSource
{
    private readonly System.Threading.Timer _timer;
    private TimeSpan _period;
    private volatile bool _running;
    private volatile bool _includeAllInterfaces = true;

    private Process[] _cachedProcesses = Array.Empty<Process>();
    private DateTime _lastProcessRefreshUtc;
    private DateTime _lastSampleUtc;
    private long   _lastTotalCpuTicks;
    private long   _lastNetDown, _lastNetUp;

    private volatile SystemSnapshot _current = SystemSnapshot.Empty;
    public SystemSnapshot Current => _current;
    public event Action<SystemSnapshot>? SnapshotChanged;

    public WindowsSystemMonitorSource(TimeSpan? interval = null)
    {
        _period = interval ?? TimeSpan.FromMilliseconds(OverlayTokens.StatsRefreshMs);
        _timer  = new System.Threading.Timer(_ => Sample(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
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

    public void SetIncludeAllInterfaces(bool all) => _includeAllInterfaces = all;

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
        DisposeProcessCache();
    }

    private void Sample()
    {
        // Each sampler has its own try/catch so one failing metric cannot drop
        // the other three. See §7.
        var now = DateTime.UtcNow;
        var elapsedMs = (now - _lastSampleUtc).TotalMilliseconds;
        if (_lastSampleUtc != default && elapsedMs < OverlayTokens.StatsMinSampleIntervalMs)
            return;

        RefreshProcessCacheIfStale(now);

        var totalCpuTicks = TrySample(() => SampleTotalCpuTicks(), 0L);
        var netDown = TrySample(() => SampleNetBytesReceived(), 0L);
        var netUp   = TrySample(() => SampleNetBytesSent(),   0L);
        var (ramUsed, ramTotal) = TrySampleRam();

        double cpuPercent = 0.0;
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

        _current = new SystemSnapshot
        {
            CpuPercent = StatsDebounce.Percent(
                _current.CpuPercent, Math.Clamp(cpuPercent, 0, 100), OverlayTokens.StatsDebouncePercent),
            RamUsedBytes   = ramUsed,
            RamTotalBytes  = ramTotal,
            BatteryPercent = TrySampleBatteryPercent(),
            OnAcPower      = TrySampleOnAc(),
            NetUpBytesPerSec = StatsDebounce.Rate(
                _current.NetUpBytesPerSec, netUpBps, OverlayTokens.StatsNetRateFloorBps),
            NetDownBytesPerSec = StatsDebounce.Rate(
                _current.NetDownBytesPerSec, netDownBps, OverlayTokens.StatsNetRateFloorBps),
            CapturedAt = now,
        };
        SnapshotChanged?.Invoke(_current);
    }

    private static T TrySample<T>(Func<T> sampler, T fallback)
    {
        try { return sampler(); }
        catch { return fallback; }
    }
}
```

**Physical RAM via `GlobalMemoryStatusEx`** (P/Invoke declared in this Av file, no new package):

```csharp
[StructLayout(LayoutKind.Sequential)]
private struct MEMORYSTATUSEX
{
    public uint   dwLength;
    public uint   dwMemoryLoad;
    public ulong  ullTotalPhys;
    public ulong  ullAvailPhys;
    public ulong  ullTotalPageFile;
    public ulong  ullAvailPageFile;
    public ulong  ullTotalVirtual;
    public ulong  ullAvailVirtual;
    public ulong  ullAvailExtendedVirtual;
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
```

`GC.GetGCMemoryInfo()` is **not** used for RAM — it reports the GC heap, not physical memory, and would produce a meaningless ratio.

`RamTotalBytes == 0` means `GlobalMemoryStatusEx` failed; the RAM slot hides itself rather than showing `0/0 GB`.

### 3.3 Metric sources (all pure-managed, no new packages)

| Metric | Source | Notes |
|---|---|---|
| CPU% | `Σ Process.TotalProcessorTime.Ticks` over cached `Process[]` ÷ (elapsed × `Environment.ProcessorCount`) | **Approximate** in two ways. (a) Processes started after the last cache rebuild contribute 0 for up to `OverlayTokens.StatsProcessCacheMs`. (b) A process that *exits* at a cache-rebuild boundary has its accumulated ticks removed from the numerator without changing the elapsed denominator, producing at most one tick of error. Clamped to 0..100 by `Sanitize`. The cache is fully **rebuilt** on each refresh, so it cannot grow unbounded. |
| RAM used / total | `GlobalMemoryStatusEx` → `ullTotalPhys − ullAvailPhys` / `ullTotalPhys` | True physical RAM. P/Invoke in the Av file. |
| Battery% | `SystemInformation.PowerStatus` (WinForms) — **Av only** | `null` when `PowerStatus.BatteryLifePercent == 255` (desktop / no battery). See §7 for the STA note. |
| Net down/up | `Σ` over `NetworkInterface.GetAllNetworkInterfaces()` where `OperationalStatus == Up` and the interface passes the filter below; each contributes `GetIPv4Statistics().BytesReceived` / `.BytesSent` deltas | IPv4 only — `GetIPv4Statistics()` is the API surface that ships in-box with .NET 8; IPv6 is not counted. |

**Network interface filter** (`SystemStatsAllInterfaces = false` → "NonVirtual"):
- **All** (default, `true`): every interface that is `Up`. Includes Wi-Fi, Ethernet, VPN, Hyper-V vNIC, WSL, Tailscale — everything currently carrying traffic.
- **NonVirtual** (`false`): excludes interfaces whose `NetworkInterfaceType` is `Loopback`, `Tunnel`, or `Unknown`, and excludes any whose `Name` or `Description` matches a virtual-adapter regex (`vEthernet|Hyper-V|VirtualBox|VMware|WSL|Tailscale|Loopback|Tunnel|VPN`, case-insensitive).

### 3.4 FSM integration

**`NotifyIsland.Core/OverlayMachine.cs`**

```csharp
public enum OverlayKind
{
    // ... existing 11 kinds ...
    SystemStats              // 12th, appended
}

public enum OverlayCommand
{
    // ... existing 17 commands ...
    SetSystemStats,          // 18th, appended
}
```

`OverlayPayload` gains:
```csharp
public SystemSnapshot? SystemStats { get; set; }
public bool AutoCollapse { get; set; } = true;
```

**Producer of `AutoCollapse`:** the `OnSnapshot` subscription callback in `MainWindow` reads `AppSettings.SystemStatsAutoCollapse` on every fire and always assigns it onto the outgoing payload, so the setting is never silently `false`. A dedicated FSM test pins this (see §8).

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
    _statsIdleMs = 0.0;
    break;
```

`Tick(deltaMs)` — the auto-collapse is gated on the setting:
```csharp
if (_kind == OverlayKind.SystemStats)
{
    _statsIdleMs += deltaMs;                    // _statsIdleMs is double; comparison promotes
    if (_payload.AutoCollapse && _statsIdleMs >= OverlayTokens.StatsAutoCollapseMs)
    {
        _kind = OverlayKind.Idle;
        _statsIdleMs = 0.0;
    }
}
```

`WidthFor(SystemStats, …)` — reuses existing tokens, invents no `ClockW` / `DateW`:
```csharp
var w = OverlayTokens.CollapsedW + visibleMetricCount * OverlayTokens.StatsMetricSlotW;
return Math.Clamp(w, OverlayTokens.StatsMinPillW, OverlayTokens.ExpandedMaxW);
```

`visibleMetricCount` comes from the threshold mapping in §5.2, which reads the **monitor work area**, not the computed pill width — so there is no feedback loop.

### 3.5 New tokens (`NotifyIsland.Core/OverlayTokens.cs`)

Type convention follows the existing file: widths and ratios are `double`, durations and counters are `int`, byte-rates are `long`.

```csharp
// System monitor — sampling
public const int    StatsRefreshMs            = 1000;    // default sampling period
public const int    StatsRefreshMinMs        = 500;     // Settings combo floor; Normalize() clamps to it
public const int    StatsRefreshMaxMs        = 2000;    // Settings combo ceiling
public const int    StatsMinSampleIntervalMs  = 100;     // guard against burst timer ticks
public const int    StatsProcessCacheMs       = 30_000;  // rebuild Process[] cache
public const double StatsDebouncePercent      = 0.5;     // percent-change floor
public const long   StatsNetRateFloorBps      = 4_096;   // byte/sec-change floor

// System monitor — layout (double, matching CollapsedW / ExpandedMinW / ExpandedMaxW)
public const double StatsMinPillW             = 280.0;
public const double StatsMetricSlotW          = 56.0;    // px per visible metric
public const double StatsScreenMarginPx       = 48.0;    // gap kept between pill and screen edge
public const double StatsShowTwoMetricsW      = 380.0;   // >= this: CPU + RAM
public const double StatsShowThreeMetricsW    = 480.0;   // >= this: + Battery
public const double StatsShowAllMetricsW      = 620.0;   // >= this: + Net
public const int    StatsAutoCollapseMs       = 30_000;

// Hover-peek (polish C)
public const int    PeekAutoHideMs            = 1_200;
public const int    PeekMorphMs               = 200;
public const double PeekExtraFullDateW        = 120.0;

// Animations (polish A) — both derived from MorphMs, see §2
public const int    PopScaleMs                = MorphMs / 2;
public const double PopScalePeak              = 1.08;
public const int    FirstAppearWobbleMs       = MorphMs / 2;
public const double FirstAppearWobblePx       = 1.0;

// Tray (polish D)
public const int    TrayClipboardSubmenuItems = 5;
```

All values mirrored into `docs/ISLAND_GUIDELINES.md` §2 and §9 in commit 6.

### 3.6 AnimationAction additions (polish A wiring)

`PopScale` and `FirstAppearWobble` are **appended** to the enum, never inserted mid-sequence:

```csharp
public enum AnimationAction
{
    // ... existing 6 members, unchanged and in place ...
    PopScale,            // appended
    FirstAppearWobble    // appended
}
```

Appending preserves every existing ordinal, so any legacy numeric encoding in an older `settings.json` keeps resolving. (`IdleBreath` is **not** re-added to the enum — it was removed in PR #10 and stays removed; `AnimIdleBreath` is a legacy `AppSettings` key that no longer has a UI path.)

New `AppSettings` keys `AnimPopScale` and `AnimFirstAppearWobble` (both default `AnimationSpeed.Normal`) are added to `CopyTo` and to the Settings → Анимации per-action list. Both new animations obey `AnimationSpeed = Off` (1 ms snap, no visible motion, consistent with GUIDELINES §2).

## 4. Data flow

```
[System.Threading.Timer, OverlayTokens.StatsRefreshMs]
  └─ WindowsSystemMonitorSource.Sample()                       [timer thread]
       ├─ Process cache rebuild (every StatsProcessCacheMs)
       ├─ per-metric TrySample(...) — one failure cannot drop the rest
       ├─ StatsDebounce.Percent / StatsDebounce.Rate
       └─ SnapshotChanged?.Invoke(snapshot)                    [timer thread]
            └─ SystemMonitorMachine.OnSourceSnapshot           [still timer thread]
                 └─ Dispatcher.UIThread.Post(...)
                      ├─ read AppSettings.SystemStatsAutoCollapse
                      ├─ OverlayMachine.Dispatch(SetSystemStats, payload)
                      └─ ApplySize(); Paint()
```

`SystemStatsRefreshMs` and `SystemStatsAllInterfaces` are **live settings**: `SettingsWindow.Apply` calls `SystemMonitorMachine.SetInterval(...)` / `.SetIncludeAllInterfaces(...)`, which forward to the source, which calls `_timer.Change(period, period)` in place. No source re-creation, no restart.

## 5. UI

### 5.1 Collapsed pill

```
┌──────────────────────────────────────────────────────┐
│ 18:32  24 сен   ▓▓▓░░ 42%  ▓▓▓▓▓ 8.2/15.9 GB  ↓1.4  │
└──────────────────────────────────────────────────────┘
   clock    date   CPU slot    RAM slot      net slot
```

Drop priority when width-constrained, first dropped to last: **Net → Battery → RAM → CPU**. CPU is never dropped.

### 5.2 Where "available width" comes from

The pill is a fixed-width capsule — it is never width-constrained by another element. "Available width" therefore means:

```
available = Screens.All[ScreenFor(this)].WorkingArea.Width   // already DIP-normalised by Avalonia
          - OverlayTokens.StatsScreenMarginPx;
```

```csharp
var count = available < OverlayTokens.StatsMinPillW          ? 0   // metrics row hidden entirely
          : available < OverlayTokens.StatsShowTwoMetricsW   ? 1   // CPU only
          : available < OverlayTokens.StatsShowThreeMetricsW ? 2   // CPU + RAM
          : available < OverlayTokens.StatsShowAllMetricsW   ? 3   // + Battery
          : 4;                                                     // + Net
```

This makes the layout a pure function of `available` — no feedback loop between pill width and metric count, and no guessing. The pill's own width then follows from `count` via the §3.4 formula.

Worked example moved to `docs/ISLAND_PREVIEW.md`; the arithmetic is intentionally **not** duplicated here.

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

The sidebar currently has **10** sections (verified against `SettingsWindow.axaml`: island, weather, placement, theme, media, look, anim, sound, icons, clipboard). `docs/ISLAND_GUIDELINES.md` §7 still lists 9 — that doc is corrected in this PR.

Two sections are added:

**«Система» (11th)**

| Control | Type | Default |
|---|---|---|
| Показывать метрики в островке | CheckBox | `true` |
| Интервал обновления | ComboBox `StatsRefreshMinMs` / `StatsRefreshMs` / `StatsRefreshMaxMs` | `StatsRefreshMs` |
| Автоскрытие | CheckBox | `true` |
| Считать виртуальные интерфейсы | CheckBox | `true` |

**«О программе» (12th)** — version, runtime, build timestamp, GitHub URL, `Проверить обновления` button (stubbed and disabled; no outbound network).

Plus a search box at the top of the nav rail (filters sections by name, case-insensitive substring; hidden when fewer than 4 sections), live validation borders on out-of-range numeric inputs, and Export / Import JSON buttons in the bottom bar.

`AppSettings` gains:
```csharp
public bool SystemStatsEnabled { get; set; } = true;
public int  SystemStatsRefreshMs { get; set; } = OverlayTokens.StatsRefreshMs;
public bool SystemStatsAutoCollapse { get; set; } = true;
public bool SystemStatsAllInterfaces { get; set; } = true;
public bool SettingsSearchEnabled { get; set; } = true;
```

All five keys are clamped / defaulted inside `Normalize()` in the same PR — this is new code, not "no additional validation". `Normalize()` clamps `SystemStatsRefreshMs` to `[StatsRefreshMinMs, StatsRefreshMaxMs]`. Existing `settings.json` files that lack these keys deserialize with the property initializers, so all four stats keys default to `true` on upgrade with no migration step. A test pins this.

## 6. Polish items

### 6.1 Animations
- `PopScale` on `CycleNext` / `CyclePrev` / chevron clicks: scale 1.0 → `PopScalePeak` → 1.0 over `PopScaleMs`, scaled per-action through `AnimationTiming.ScaleMs(PopScaleMs, settings.AnimPopScale)`.
- `FirstAppearWobble`: when the pill first appears after being hidden (fullscreen exit, tray toggle), translate X oscillates within `FirstAppearWobblePx` over `FirstAppearWobbleMs`, scaled through `AnimFirstAppearWobble`.
- New `AnimationEasing.PopScale(t)` in Core, unit-tested at the endpoints and midpoint.
- Both respect `AnimationSpeed = Off` (1 ms snap, no visible motion).

### 6.2 Settings UI
- Sidebar sections 11 («Система») and 12 («О программе»), Lucide nav icons.
- Search box on the nav rail.
- Live validation: red `BorderBrush` on numeric fields whose raw input is out of range before clamping.
- Export / Import `settings.json` via `SaveFileDialog` / `OpenFileDialog`.
- `ISLAND_GUIDELINES.md` §7 sidebar list corrected to 12.

### 6.3 Hover-peek
- Peek body adds full date (`DateFormat.FullShort`), current weather temp when `WeatherEnabled`, and the unread badge when > 0.
- Auto-hide after `PeekAutoHideMs` of no pointer movement.
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

**Per-metric isolation.** Every platform call is wrapped individually via the `TrySample` helper, so a single failing metric cannot drop the others for that tick. There is no single outer `try/catch` around the whole snapshot initializer.

| Metric | Failure mode | Behaviour |
|---|---|---|
| CPU | `Process` access denied on a protected process | that process contributes 0 ticks; total still valid |
| RAM | `GlobalMemoryStatusEx` returns false | `(0, 0)` → `RamTotalBytes == 0` → RAM slot hides |
| Battery | WinForms `PowerStatus` throws, or returns a zeroed struct | `BatteryPercent = null` → battery slot hides; other metrics unaffected |
| Network | adapter disabled mid-enumeration | that adapter contributes 0 for the tick; others still counted |

**Battery / STA note.** `SystemInformation.PowerStatus` is not documented as thread-safe, and WinForms interop expects an initialised application context. `WindowsSystemMonitorSource` warms it on `Start()` via a one-shot `Dispatcher.UIThread.Post(() => _ = SystemInformation.PowerStatus)` before the first timer tick, so the timer callback always runs against a warm context. If `PlatformNotSupportedException` still escapes, `TrySampleBatteryPercent()` swallows it and returns `null`.

**Lifetime.** `SystemMonitorMachine.Dispose()` unsubscribes from `SnapshotChanged` and nulls `OnSnapshot`, so a disposed machine is not reachable from the source. `OverlayWindow` disposes it in its own `Closed` handler.

**Memory visibility.** `_current` is `volatile` — written on the timer thread, read on the UI thread via `Current` / `SystemMonitorMachine.Snapshot`.

**Settings.** `AppSettings.Normalize()` (edited in this PR) clamps `SystemStatsRefreshMs` to `[StatsRefreshMinMs, StatsRefreshMaxMs]`. Covered by a dedicated test.

**Process cache.** `RefreshProcessCacheIfStale` disposes exited processes and swallows `InvalidOperationException` (race with process exit). The cache is fully rebuilt on each refresh, so it cannot grow unbounded.

## 8. Testing

**Baseline (verified, not assumed):** `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release` on `feature/clipboard-cycle-pill` reports **174 passed, 0 failed** (114 `[Fact]` + 11 `[Theory]` with `InlineData` expansions).

**`NotifyIsland.Tests/SystemMonitorMachineTests.cs`** (11 tests, fake `ISystemMonitorSource`)

| Test | Asserts |
|---|---|
| `Machine_ForwardsSourceSnapshots` | machine's `Snapshot` matches the fake's last fire |
| `Machine_OnSnapshotRaisesOncePerFire` | event count == fire count |
| `Machine_StartDelegatesToSource` | fake records `Start()` |
| `Machine_StopDelegatesToSource` | fake records `Stop()` |
| `Machine_SetIntervalDelegatesToSource` | fake records the period |
| `Machine_SetIncludeAllInterfacesDelegates` | fake records the flag |
| `Machine_FirstSnapshot_HasZeroCpuButRealRam` | first fire → `CpuPercent == 0`, `RamTotalBytes > 0`, `CapturedAt` set |
| `Machine_DisposeUnsubscribesFromSource` | after `Dispose()`, a source fire does not raise `OnSnapshot` |
| `Debounce_Percent_ReusesPreviousBelowThreshold` | sub-threshold delta keeps old |
| `Debounce_Percent_UpdatesAboveThreshold` | above-threshold delta replaces |
| `Debounce_Rate_ReusesBelowFloor` | small byte-rate delta keeps old |
| `Debounce_Rate_NeverNegative` | negative input clamps to 0 |

**`NotifyIsland.Tests/AdaptiveStatsLayoutTests.cs`** (7 tests)

| Test | Asserts |
|---|---|
| `Layout_UnderMin_HidesRow` | `available < StatsMinPillW` → `count == 0` |
| `Layout_AtMin_ShowsCpuOnly` | `count == 1` |
| `Layout_380_ShowsCpuRam` | `count == 2` |
| `Layout_480_ShowsCpuRamBattery` | `count == 3` |
| `Layout_620_ShowsAll` | `count == 4` |
| `Layout_NeverDropsCpu` | CPU slot present for every `count >= 1` |
| `SystemStats_AutoCollapseFlagRespected` | `AutoCollapse = false` → `Tick` past `StatsAutoCollapseMs` stays in `SystemStats`; `AutoCollapse = true` → collapses |

**`NotifyIsland.Tests/AnimationEasingTests.cs`** (4 tests, added by polish-A)
- `PopScale_StartsAtOne`, `PopScale_PeaksNearMidpoint` (≈ `PopScalePeak`), `PopScale_EndsAtOne`
- `FirstAppearWobble_StaysWithinPx` — |x| ≤ `FirstAppearWobblePx` for all t

**`NotifyIsland.Tests/AppSettingsTests.cs`** (3 tests appended)
- `Normalize_ClampsSystemStatsRefreshMs` — below `StatsRefreshMinMs` clamps up, above `StatsRefreshMaxMs` clamps down
- `Default_StatsKeys_AreTrue` — fresh `AppSettings` has all four bools `true`
- `Defaults_StatsKeys_AreTrueForExistingInstalls` — deserialize a hand-written `settings.json` that omits the stats keys into a fresh instance, then `Normalize()`; all four stay `true`

**Totals: 174 existing + 11 + 7 + 4 + 3 = 199 tests, 0 failing.**

**Build:** 0 warnings, 0 errors.

## 9. Commit structure (one PR, six commits)

```
1. feat(core): SystemSnapshot + ISystemMonitorSource + StatsDebounce + SystemMonitorMachine
2. feat(av):   WindowsSystemMonitorSource (Process / GlobalMemoryStatusEx / NetworkInterface)
3. feat(core): OverlayMachine SystemStats kind + SetSystemStats + auto-collapse gating
4. feat(ui):   SystemStatsPanel in collapsed pill + expanded kind + adaptive layout
5. polish(ui): animations (PopScale, FirstAppearWobble) + settings sidebar + hover-peek + tray
6. docs:       GUIDELINES tokens + §7 sidebar list + ISLAND_PREVIEW example + CONTEXT + CHANGELOG
```

## 10. Verification

1. `dotnet build NotifyIsland.Av.csproj -c Release` → 0 warnings, 0 errors
2. `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release` → **199 passed**
3. `python3 tools/test_overlay_states.py` → FSM states valid
4. Manual: run the published build; confirm metrics appear in the collapsed pill; click into `SystemStats`; cycle back to `Idle`; toggle `SystemStatsEnabled` and confirm the row hides; change `SystemStatsRefreshMs` and confirm the sampling rate changes without a restart; uncheck «Считать виртуальные интерфейсы» and confirm the net figure drops.
5. Sandbox: kill old `NotifyIsland.exe`, publish to `Desktop\notifyisland-fresh-<sha>`, launch.
