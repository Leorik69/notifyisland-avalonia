# NotifyIsland 1.12.0 — System Monitor + Polish Suite

**Date:** 2026-09-28
**Status:** Draft for review
**Branch base:** `feature/clipboard-cycle-pill` (which is ahead of `fix/win11-stability-build`)
**Delivery:** One PR, one branch, multiple commits

---

## 1. Motivation

NotifyIsland 1.11.0 already covers: pill overlay, weather, SMTC Now Playing, battery alerts, timer/stopwatch, clipboard history, click-pin/hover-peek, settings sidebar, tray. What it lacks:

- **No visibility into machine state.** Users on underpowered laptops have no idea why things stutter. The pill shows a clock but nothing about CPU/RAM/network pressure.
- **Polish debt.** Animations are functional but flat. Settings sidebar is dense with no search. Tray menu is minimal (4 items). Hover-peek shows only partial info.

**Goal:** add a System Monitor module (5 metrics, adaptive layout) plus four targeted polish passes, delivered as one atomic PR.

## 2. Scope

### In scope
- System Monitor: CPU%, RAM used/total, Battery%, Network up/down (bytes/sec)
- Adaptive pill layout that grows/shrinks based on available width
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
- No duplicate animation timings — all numbers go in `NotifyIsland.Core/OverlayTokens.cs` and are mirrored in `docs/ISLAND_GUIDELINES.md`
- Git commits use `git -c user.name='Leorik69' -c user.email='leorik69@users.noreply.github.com'`, never `git config`
- Sandbox deploy publishes to a fresh `Desktop\notifyisland-fresh-<sha>` and kills any old `NotifyIsland.exe` first

## 3. Architecture

### 3.1 New files (Core)

**`NotifyIsland.Core/SystemSnapshot.cs`**

```csharp
public sealed record SystemSnapshot
{
    public double CpuPercent { get; init; }              // 0..100
    public long   RamUsedBytes { get; init; }
    public long   RamTotalBytes { get; init; }
    public double? BatteryPercent { get; init; }         // null on desktops
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
    /// <summary>Fires on the caller's thread; handlers must marshal to UI themselves.</summary>
    event Action<SystemSnapshot>? SnapshotChanged;
    SystemSnapshot Current { get; }
    void Start();
    void Stop();
}
```

**`NotifyIsland.Core/WindowsSystemMonitorSource.cs`**

```csharp
public sealed class WindowsSystemMonitorSource : ISystemMonitorSource
{
    private readonly System.Threading.Timer _timer;
    private readonly int _processorCount = Environment.ProcessorCount;
    private Process[] _cachedProcesses = Array.Empty<Process>();
    private DateTime _lastSampleUtc;
    private long _lastTotalCpuTicks;
    private long _lastNetDown, _lastNetUp;
    private DateTime _lastProcessRefreshUtc;
    private SystemSnapshot _current = SystemSnapshot.Empty;

    public SystemSnapshot Current => _current;
    public event Action<SystemSnapshot>? SnapshotChanged;

    public WindowsSystemMonitorSource(TimeSpan? interval = null)
    {
        var period = interval ?? TimeSpan.FromSeconds(1);
        _timer = new System.Threading.Timer(_ => Sample(), null, Timeout.Infinite, Timeout.Infinite);
        _period = period;
    }

    public void Start()  => _timer.Change(_period, _period);
    public void Stop()   => _timer.Change(Timeout.Infinite, Timeout.Infinite);
    public void Dispose() { Stop(); _timer.Dispose(); DisposeProcessCache(); }

    private void Sample()
    {
        try
        {
            var now = DateTime.UtcNow;
            var elapsedMs = (now - _lastSampleUtc).TotalMilliseconds;
            if (elapsedMs < 100) return;              // ignore sub-100ms ticks

            RefreshProcessCacheIfStale(now);

            var totalCpuTicks = SampleTotalCpuTicks();
            var netDown = SampleNetBytesReceived();
            var netUp   = SampleNetBytesSent();

            double cpuPercent = 0;
            long   netDownBps = 0, netUpBps = 0;
            if (_lastSampleUtc != default)
            {
                cpuPercent = (totalCpuTicks - _lastTotalCpuTicks) * 1000.0
                             / (elapsedMs * _processorCount);
                netDownBps = (netDown - _lastNetDown) * 1000 / (long)elapsedMs;
                netUpBps   = (netUp   - _lastNetUp)   * 1000 / (long)elapsedMs;
            }
            _lastTotalCpuTicks = totalCpuTicks;
            _lastNetDown = netDown; _lastNetUp = netUp;
            _lastSampleUtc = now;

            cpuPercent = Math.Clamp(cpuPercent, 0, 100);
            netDownBps = Math.Max(0, netDownBps);
            netUpBps   = Math.Max(0, netUpBps);

            var snapshot = new SystemSnapshot
            {
                CpuPercent       = Debounced(_current.CpuPercent, cpuPercent),
                RamUsedBytes     = SampleRamUsedBytes(),
                RamTotalBytes    = _lastRamTotal,
                BatteryPercent   = SampleBatteryPercent(),
                OnAcPower        = SampleOnAcPower(),
                NetUpBytesPerSec = DebouncedLong(_current.NetUpBytesPerSec, netUpBps),
                NetDownBytesPerSec = DebouncedLong(_current.NetDownBytesPerSec, netDownBps),
                CapturedAt       = now,
            };
            _current = snapshot;
            SnapshotChanged?.Invoke(snapshot);
        }
        catch
        {
            // Fail-soft: keep _current, never throw to the timer callback.
        }
    }

    /// <summary>Reuse the previous value if the new one moved less than the threshold.</summary>
    private static double Debounced(double previous, double next) =>
        Math.Abs(next - previous) < 0.5 ? previous : next;
}
```

**`NotifyIsland.Core/SystemMonitorMachine.cs`**

```csharp
public sealed class SystemMonitorMachine
{
    private readonly ISystemMonitorSource _source;
    private SystemSnapshot _snapshot = SystemSnapshot.Empty;

    public SystemSnapshot Snapshot => _snapshot;
    public event Action<SystemSnapshot>? OnSnapshot;

    public SystemMonitorMachine(ISystemMonitorSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _source.SnapshotChanged += s =>
        {
            _snapshot = s;
            OnSnapshot?.Invoke(s);
        };
    }

    public void Start() => _source.Start();
    public void Stop()  => _source.Stop();
    public void Dispose() => _source.Dispose();
}
```

### 3.2 Metric sources (all pure-managed, no new packages)

| Metric | Source | Notes |
|---|---|---|
| CPU% | `Σ Process.TotalProcessorTime.Ticks` over cached `Process[]` ÷ (elapsed × `Environment.ProcessorCount`) | Process cache refreshed every 30 s to avoid re-enumerating |
| RAM used | `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`; total = `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes + GC.GetGCMemoryInfo().HeapSizeBytes` | Falls back to `GC.GetTotalMemory(false)` if `GCMemoryInfo` is unavailable |
| Battery% | `SystemInformation.PowerStatus` (WinForms, already referenced) | `null` when `PowerStatus.BatteryLifePercent` is `255` (desktop) |
| Net down/up | `Σ` over **all** `NetworkInterface.GetAllNetworkInterfaces()` where `OperationalStatus == Up` and `NetworkInterfaceType != Loopback`; each `GetIPv4Statistics().BytesReceived` / `BytesSent` | Includes Wi-Fi, Ethernet, VPN, Hyper-V vNIC, WSL — everything currently operational. IPv4 only (matches the pre-Win10-era API surface that ships with .NET 8); IPv6 is not counted. |

### 3.3 FSM integration

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

`OverlayPayload` gains `public SystemSnapshot? SystemStats { get; set; }`.

`Sanitize(...)` rounds `CpuPercent` to 1 decimal and clamps byte counters to non-negative.

`Dispatch` for `SetSystemStats`:
```csharp
case OverlayCommand.SetSystemStats:
    if (data.SystemStats is null) break;
    if (_kind != OverlayKind.SystemStats)
        _returnTo = _kind == OverlayKind.Collapsed ? OverlayKind.Idle : _kind;
    _kind = OverlayKind.SystemStats;
    Apply(data);
    _statsIdleMs = 0;
    break;
```

`Tick(deltaMs)`:
```csharp
if (_kind == OverlayKind.SystemStats)
{
    _statsIdleMs += dt;
    if (_statsIdleMs >= StatsAutoCollapseMs)   // 30_000
        _kind = OverlayKind.Idle;
}
```

`WidthFor(SystemStats)` returns `Math.Max(OverlayTokens.CollapsedStatsMinW, OverlayTokens.CollapsedW)`.

### 3.4 New tokens (`NotifyIsland.Core/OverlayTokens.cs`)

```csharp
// System monitor
public const int   CollapsedStatsMinW        = 280;
public const int   CollapsedStatsMetricsW    = 56;   // per-metric slot width
public const int   StatsRefreshMs            = 1000;
public const int   StatsAutoCollapseMs       = 30_000;
public const double StatsDebouncePercent     = 0.5;

// Adaptive layout thresholds (width in px → metrics shown)
public const int   StatsTwoMetricsW          = 280;  // CPU only
public const int   StatsThreeMetricsW        = 380;  // CPU + RAM
public const int   StatsFourMetricsW         = 480;  // + Battery

// Hover-peek (polish C)
public const int   PeekAutoHideMs            = 1_200;
public const int   PeekMorphMs               = 200;
public const int   PeekExtraFullDateW        = 120;

// Animations (polish A)
public const int   PopScaleMs                = 180;
public const double PopScalePeak             = 1.08;
public const int   FirstAppearWobbleMs       = 220;
public const double FirstAppearWobblePx      = 1.0;

// Tray (polish D)
public const int   TrayClipboardSubmenuItems = 5;
```

All mirrored into `docs/ISLAND_GUIDELINES.md` §2 and §9 in the same PR.

## 4. Data flow

```
[System.Threading.Timer, 1 Hz]
  └─ WindowsSystemMonitorSource.Sample()
       ├─ Process cache refresh (every 30 s)
       ├─ CPU / RAM / Battery / Net samples
       ├─ Debounce (< 0.5% delta reuses previous)
       └─ SnapshotChanged?.Invoke(snapshot)
            └─ SystemMonitorMachine.OnSnapshot (still on timer thread)
                 └─ Dispatcher.UIThread.Post(...)
                      ├─ OverlayMachine.Dispatch(SetSystemStats, payload)
                      └─ ApplySize(); Paint()
                           └─ SystemStatsPanel renders if kind == SystemStats
```

**FSM auto-collapse:** `SystemStats` self-collapses back to `Idle` after 30 s without a new snapshot, so it never becomes a stuck expanded pill.

**Interaction:** when `SystemStatsEnabled` is true and the pill is in `Idle`, the metrics row renders *inside* the collapsed pill rather than taking over the whole kind. Clicking any metric row switches to `SystemStats` (expanded) where the full readout is visible.

## 5. UI

### 5.1 Collapsed pill

```
┌──────────────────────────────────────────────────────┐
│ 18:32  24 сен   ▓▓▓░░ 42%  ▓▓▓▓▓ 8.2/15.9 GB  ↓1.4  │
└──────────────────────────────────────────────────────┘
   clock    date   CPU bar    RAM bar      network ↓
```

Priority order (dropped first when width-constrained): **Net → Battery → RAM → CPU**. The pill never shrinks below `CollapsedStatsMinW` (280 px); at that floor only CPU remains.

### 5.2 Expanded `SystemStats` kind

```
┌───────────────────────────────────────────────┐
│ [icon]  Система                             │
│ CPU    ▓▓▓▓▓▓░░░░  57%                     │
│ RAM    ▓▓▓▓░░░░░░  8.2 / 15.9 GB  (52%)      │
│ Батарея ▓▓▓▓▓▓░░░░  67%  ⚡                   │
│ Сеть   ↓ 1.4 MB/s   ↑ 220 KB/s               │
└───────────────────────────────────────────────┘
```

### 5.3 Settings — new section

Added to the existing 10-section sidebar as **«Система»** (11th):

| Control | Type | Default |
|---|---|---|
| Показывать метрики в островке | CheckBox | `true` |
| Интервал обновления | ComboBox 500/1000/2000 ms | `1000` |
| Автоскрытие | CheckBox | `true` |
| Интерфейсы сети | ComboBox `All` / `NonVirtual` | `All` |

`AppSettings` gains:
```csharp
public bool   SystemStatsEnabled { get; set; } = true;
public int    SystemStatsRefreshMs { get; set; } = 1000;   // clamp 500..2000
public bool   SystemStatsAutoCollapse { get; set; } = true;
public bool   SystemStatsAllInterfaces { get; set; } = true;
```

## 6. Polish items

### 6.1 Animations

- `PopScale` on `CycleNext` / `CyclePrev` / chevron clicks: scale 1.0 → 1.08 → 1.0 over `PopScaleMs` (scaled by `AnimationSpeed` through `AnimationTiming.ScaleMs`).
- `FirstAppearWobble`: when the pill first appears after being hidden (fullscreen, tray toggle), translate X oscillates ±1 px over `FirstAppearWobbleMs`.
- New `AnimationEasing.PopScale(t)` helper in Core, unit-tested for `PopScale(0) == 1.0`, `PopScale(0.5) == ~1.08`, `PopScale(1) == 1.0`.

### 6.2 Settings UI

- New sidebar section **«О программе»** (12th): version string, runtime info, GitHub URL, build timestamp, «Проверить обновления» button (stubbed, no network).
- Search box at the top of the nav rail: filters sections by name (case-insensitive substring). Hidden when fewer than 4 sections.
- Live validation: `NumericUpDown` / `TextBox` bound to clamped values get a red `BorderBrush` when the raw input is out of range before clamping.
- Export / Import buttons in the bottom bar: `SaveFileDialog` / `OpenFileDialog` for `settings.json`.

### 6.3 Hover-peek

- Peek body shows: full date (`DateFormat.FullShort`), current weather temp when `WeatherEnabled`, unread badge when > 0.
- Auto-hide after `PeekAutoHideMs` (1.2 s) of no pointer movement.
- Width morphs by `PeekExtraFullDateW` (120 px) over `PeekMorphMs` (200 ms).
- Persistent **«Закреплено»** badge in the right edge while pinned.

### 6.4 Tray menu

New order:
```
Показать/Скрыть островок        (primary, no icon)
─────────────
Центр уведомлений
Буфер обмена ▸                  (submenu, last 5 items, click = re-copy)
─────────────
Демо вкл/выкл
Погода вкл/выкл
Таймер ▸ (existing presets)
─────────────
Настройки…
Выход
```

## 7. Error handling

- Every Win32 / `Process` / `NetworkInterface` call inside `WindowsSystemMonitorSource.Sample()` is wrapped in a single outer `try/catch`. On exception the previous `_current` snapshot is kept and the timer keeps ticking. The source never throws to its caller.
- `SystemMonitorMachine` never throws; `OnSnapshot` handler failures are caught by the caller's `Dispatcher.Post` boundary.
- `Paint()` treats a null `SystemStats` as "hide the panel" — no NRE.
- `AppSettings` already runs `Normalize()` on load, which clamps `SystemStatsRefreshMs` to 500..2000. New keys need no additional validation.
- Process cache: `RefreshProcessCacheIfStale` disposes exited processes and swallows `InvalidOperationException` (race with process exit).
- NetworkInterface enumeration can throw on disabled adapters — caught, treated as 0 bytes for that adapter.

## 8. Testing

New test file `NotifyIsland.Tests/SystemMonitorMachineTests.cs`:

| Test | Asserts |
|---|---|
| `SystemMonitorMachine_ForwardsSourceSnapshots` | fake source fires → machine's `Snapshot` matches |
| `SystemMonitorMachine_OnSnapshotRaises` | event fired exactly once per source fire |
| `SystemMonitorMachine_StartDelegatesToSource` | fake records `Start()` call |
| `SystemMonitorMachine_StopDelegatesToSource` | fake records `Stop()` call |
| `Debounce_ReusesPreviousWhenDeltaBelowThreshold` | 0.4% delta keeps old value |
| `Debounce_UpdatesWhenDeltaAboveThreshold` | 1.0% delta replaces value |
| `SystemSnapshot_RamPercent_ClampsAndComputes` | 0 total → 0%, valid → correct |
| `SystemSnapshot_Equality_UsesAllFields` | record value equality |

New test file `NotifyIsland.Tests/AdaptiveStatsLayoutTests.cs`:

| Test | Asserts |
|---|---|
| `Layout_280px_ShowsCpuOnly` | width < 380 → `[Cpu]` |
| `Layout_380px_ShowsCpuRam` | 380..479 → `[Cpu, Ram]` |
| `Layout_480px_ShowsCpuRamBattery` | 480..999 → `[Cpu, Ram, Battery]` |
| `Layout_1000px_ShowsAll` | ≥ 1000 → all four |
| `Layout_NeverDropsCpu` | CPU present at every width |

New test file `NotifyIsland.Tests/AnimationEasingTests.cs` (populated by the polish-A commit):
- `PopScale_PeakAtMidpoint`, `PopScale_EndsAtOne`, `FirstAppearWobble_OscillatesWithinPx`

**Baseline:** 174 tests currently pass. Target after this PR: **189 tests, 0 failing.**
**Build:** 0 warnings, 0 errors.

## 9. Commit structure (one PR, five commits)

```
1. feat(core): system monitor snapshot + ISystemMonitorSource + WindowsSystemMonitorSource
2. feat(core): SystemMonitorMachine + OverlayMachine SystemStats kind
3. feat(ui): SystemStatsPanel in collapsed pill + expanded kind
4. polish(ui): animations (PopScale, FirstAppearWobble)
5. polish(ui): settings sidebar, hover-peek, tray menu
6. docs: GUIDELINES tokens + CONTEXT + CHANGELOG
```

## 10. Verification

1. `dotnet build NotifyIsland.Av.csproj -c Release` → 0 warnings, 0 errors
2. `dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release` → 189 passed
3. `python3 tools/test_overlay_states.py` → FSM states valid
4. Manual: run the published build, confirm metrics appear in the collapsed pill, click into `SystemStats`, cycle back to `Idle`, toggle `SystemStatsEnabled` in Settings, confirm the panel hides.
5. Sandbox: kill old, publish to `Desktop\notifyisland-fresh-<sha>`, launch.
