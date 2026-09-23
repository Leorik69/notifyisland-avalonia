# Changelog

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