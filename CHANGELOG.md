# Changelog

## 1.6.0 — date chip, weather location, stock themes

### Added
- **Date next to time** in collapsed row (`DateFormat`: Off / DayMonth / WeekdayShort / WeekdayDay / Numeric / FullShort). Default **DayMonth** («24 сен», ru-RU). Settings → **Островок**.
- **Weather location**: `WeatherLocationMode` Windows | Manual + `WeatherLocationName` + lat/lon; city presets (Москва, СПб, …). Settings → **Погода**. Manual shows chosen label on expanded weather / tooltip; temperature remains Windows-primary (honest note in UI). **No Open-Meteo.**
- **Theme presets** (`ThemePreset`): **NothingDark**, **AppleQuiet**, **Ocean**, **Custom**. Settings → **Тема**. Stock Apply overwrites palette, font, anim speeds/styles, icon pack, date format, sound pack. Divergent save → auto **Custom**; button «Перейти в кастом».

### Changed
- Removed clipped **clock icon** (`ClockIconHost`) from collapsed island — frees space for date.
- Collapsed widths: `CollapsedW` **170**, `CollapsedWeatherW` **240** (icon gone + date chip).
- Version **1.6.0**. Docs: `ISLAND_GUIDELINES` themes + date + location.

### Note
- Weather stays Windows-primary. No proprietary asset rips.


## 1.5.9 — Meteocons weather icon packs (4 styles)

### Added
- Four **Meteocons** (Bas Milius, MIT) weather packs selectable in Settings → **Иконки**:
  - **Meteocons Fill** — заливка
  - **Meteocons Flat** — плоский
  - **Meteocons Line** — контур
  - **Meteocons Monochrome** — монохром (tint to text color)
- Vendored animated SVGs from `@meteocons/svg` under `Assets/Icons/Meteocons{Fill,Flat,Line,Monochrome}/` for keys: clear, partly, cloud, fog, drizzle, rain, snow, storm, sleet.
- `MeteoconsMap` (Core) + `MeteoconsMotion` — Avalonia rotate/bob/pulse (Skia does not run SMIL; SMIL kept in files).
- Attribution: `Assets/Icons/NOTICE`, per-folder `LICENSE`, `docs/ICON_PACKS.md`.

### Changed
- Version **1.5.9**.
- `IconPackService` recognizes 7 packs; Meteocons is weather-only (other keys → IslandIcons).

### Note
- No proprietary Nothing fonts. No WebView2 dependency in this drop.



## 1.5.8 — animation styles, slower soft morph, icon↔FontSize

### Added
- **Appear styles** (`NotifyAppearStyle`): Inflate, SlideDown, FadeScale, Bounce, Pop — Settings → Анимации.
- **Dismiss styles** (`NotifyDismissStyle`): Collapse, SlideUp, FadeScaleOut, Ragged («рваный» jitter), Glitch (stutter).
- Styles wired into morph path on Notification enter/leave; Demo (F9) cycles appear/dismiss so all are visible.
- `AnimationEasing` (CubicOut / SpringOut / PopScale / GlitchStep) — no linear morph.
- Icon DIP scales with FontSize: `IconDip = FontSize × k` (collapsed k≈1.0, kind k≈0.92); Viewbox hosts update on FontSize/IconPack change.

### Changed
- Base `MorphMs` **280 → 420**; defaults lean Slow (`AnimationSpeed`, `AnimMorphInflate`, `AnimMorphCollapse`).
- Default Appear=**Bounce**, Dismiss=**Ragged**.
- Version **1.5.8**.

### Note
- Per-action speed multipliers retained. No proprietary font/sound rips.


## 1.5.7 — UX: font size, palette swatches, icon packs, per-action anim, Nothing-inspired fonts

### Added
- **Font size** (10–18 px) in Settings **Вид**; persisted `FontSize`; applied to clock, titles, weather temp, badge.
- **Font family** ComboBox: Системный / Space Grotesk / JetBrains Mono; OFL fonts in `Assets/Fonts/` (see `docs/NOTHING_INSPIRATION.md`).
- **Visual palette**: preset swatches + Avalonia `ColorPicker` with live preview pill (replaces hex-only text boxes).
- **Real icon packs**: vendored Tabler (MIT) and Lucide (ISC) SVGs under `Assets/Icons/{Tabler,Lucide}/`; ComboBox switches pack; `IconPackService` + IslandIcons fallback; `docs/ICON_PACKS.md` + NOTICE.
- **Per-action animations** tab **Анимации**: master speed + toggle pulse/breath + Slow/Normal/Fast/Off for morph inflate, morph collapse, unread pulse, idle breath, hover, swipe rubber.
- Docs: `docs/NOTHING_INSPIRATION.md` (legal inspiration vs NType82 / Glyph firmware).

### Changed
- Version **1.5.7**; Settings default size slightly larger for new controls.
- Unread dot slightly more “matrix” (square-ish corners).

### Note
- No Open-Meteo. No proprietary Nothing/Apple firmware assets.

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