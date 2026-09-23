# Changelog

## 1.5.6 — no-drag, morph fix, palette, icon packs

### Fixed
- **Animations visible again:** Avalonia `Transitions` on `Window.Width` were unreliable; morph now uses an explicit 16 ms timer (`StartMorph` / `OnMorphTick`) with CubicEaseOut on both Window and Pill size. Demo alternates expand ↔ collapse so inflate/collapse is obvious.
- Unread pulse and idle breath were killed by Opacity/Scale `Transitions` fighting 33 ms timers — those transitions removed; pulse 0.40↔1.0, breath ±2.5%.
- Temporary `AppLog.Info` line when morph starts (`%TEMP%/notifyisland.log`).

### Removed
- Mouse drag-reposition (`AllowDrag` hold >200 мс). Position only via Settings **Расположение** (Edge + Offset X/Y). Pointer kept for swipes / click Action Center. Checkbox removed; `Normalize()` forces `AllowDrag=false`.

### Added
- Color palette in Settings **Вид**: capsule fill, accent, primary/secondary text (`ColorCapsuleFill` / `ColorAccent` / `ColorTextPrimary` / `ColorTextSecondary`); live Apply.
- Icon pack stub in **Иконки** + research doc [`docs/ICON_PACKS.md`](docs/ICON_PACKS.md) (Tabler MIT, Lucide ISC, Phosphor MIT, Fluent MIT, Heroicons MIT).
- Target stack section in `ISLAND_GUIDELINES.md` §0 / `ISLAND_PREVIEW.md`.

### Docs
- Drag removed; animation table; palette; icon packs; product pick (must vs backlog differentiators).


## 1.5.5 — situation-aware island animations

### Added
- `AnimationSpeed` setting: **Off** | **Slow** (~1.6×) | **Normal** (1×) | **Fast** (~0.55×); default Normal; persisted in `settings.json`.
- Settings **Вид** tab: ComboBox «Скорость анимаций» (`AnimSpeedBox`).
- Unread-dot gentle opacity pulse when unread > 0 (Idle/Collapsed).
- Idle/Collapsed subtle “breathing” scale (±1.2%) on the pill.
- `AnimationTiming` helper in Core (multipliers + `ScaleMs`); unit tests for Off / Slow / Normal / Fast.

### Changed
- Morph / icon crossfade / swipe rubber / hover brush durations scale with `AnimationSpeed` via `ApplyAnimationSettings()` (ctor + settings Apply).
- Off → ~1 ms transitions; pulse and breath disabled.
- Hover response kept subtle (+0.06 fill alpha).

### Docs
- `ISLAND_GUIDELINES.md` animation table + settings tab; `ISLAND_PREVIEW.md` animation items.


## 1.5.4 — tabbed Settings window

### Changed
- Settings UI redesigned: Avalonia `TabControl` with categories instead of one long scroll pile.
- Tabs (RU): **Островок**, **Погода**, **Расположение**, **Вид**, **Звуки**, **Иконки**.
- Default Settings window size ~520×640; geometry still persisted.
- Bottom bar unchanged: Отмена / Применить / OK.

### Docs
- `ISLAND_GUIDELINES.md` §7 settings tabs; `ISLAND_PREVIEW.md` settings item.

## 1.5.3 — WinForms tray (visible on Sandbox)

### Fixed
- Tray icon now uses WinForms `NotifyIcon` as primary so it actually appears in Win11 / Windows Sandbox (Avalonia `TrayIcon` often stayed hidden).
- Avalonia `TrayService` kept as fallback only.
- TargetFramework `net8.0-windows` + `UseWindowsForms`; `EnableWindowsTargeting` for Linux CI cross-compile.
- Tray icon refresh only when unread count changes (no per-tick icon churn).

### Docs
- `ISLAND_GUIDELINES.md` §7 tray; `ISLAND_PREVIEW.md` items 25–28.

## 1.5.2 — tray icon UX

### Added / Fixed
- System tray icon with unread badge (`tray.png` / `tray-unread.png`).
- Left-click toggles island visibility; right-click menu (settings / island / demo / weather / exit); double-click opens Action Center.


## 1.5.1 — Settings crash fix

### Fixed
- Opening **Настройки** from the island context menu no longer crashes.
- Cause: hand-written `InitializeComponent()` called only `AvaloniaXamlLoader.Load` and never wired `x:Name` fields → `NullReferenceException` in `LoadUi()`.
- Fix: use Avalonia-generated `InitializeComponent()`.


## 1.5.0 — sound packs

### Added
- Original WAV sound packs under `Assets/Sounds/nothing/` and `Assets/Sounds/ios/` (`notify`, `expand`, `collapse`, `swipe`, `error`, `hover`) — synthesized tones *inspired by* soft Glyph-like clicks and soft iOS-like taps; **not** proprietary Nothing OS / Apple iOS system audio (see `Assets/Sounds/README.md`).
- Optional `Assets/Sounds/system/` simple beeps; **System** pack prefers Windows `SystemSounds` / `MessageBeep`.
- `SoundPack` enum: `Nothing` | `Ios` | `System` | `Off`.
- Master volume + per-event volume multipliers in Settings and `settings.json`.
- Hover cue with debounce (optional; volume default low).

### Changed
- `IslandSounds` plays local WAVs from app base `Assets/Sounds/{pack}/` (SoundPlayer / winmm), with SystemSounds fallback for the System pack.
- Morph cues split into expand vs collapse.
- Settings UI: pack combo + per-sound volume sliders.
- Docs: `ISLAND_GUIDELINES.md`, `ISLAND_PREVIEW.md` sound pack section.

### Legal
Do not replace pack files with ripped Nothing Glyph or Apple UIKit system sounds.