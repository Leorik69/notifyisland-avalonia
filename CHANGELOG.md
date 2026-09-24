# Changelog

## Unreleased — Idle breath removed
- Removed idle-breath animation entirely: `AppSettings.AnimBreathEnabled` / `AnimIdleBreath`, `OverlayTokens.BreathScaleAmp` / `BreathScaleXExtra` / `BreathWidthAmpPx` / `BreathGlowAmp`, `AnimationTiming.BreathPeriodMs`, `AnimationAction.IdleBreath`, `HoverPinMachine.SoftenBreath`. Pulse (unread-dot) kept as-is.
- UI: dropped «Дыхание в простое» checkbox and per-action «Дыхание (idle)» speed combo. Remaining per-action speeds: morph inflate / morph collapse / unread pulse / hover.
- GUIDELINES / CONTEXT / ISLAND_PREVIEW updated; no Idle-breath tokens documented anymore.

## 1.11.0 — Settings icon sidebar

### Added
- **SettingsWindow** redesigned: left **nav rail** (~216px) with **Lucide** SVG icons + Russian labels; right scrollable content in rounded cards (Nothing-ish near-black chrome).
- IA split: **Медиа и питание** (Now Playing / battery / timer) and **Оформление** (z-order, opacity, fonts, palette, preview) replace the old junk-drawer **Вид** tab.
- Vendored Lucide nav glyphs: `layout-dashboard`, `cloud-sun`, `move`, `palette`, `music`, `type`, `sparkles`, `volume-2`, `shapes` (+ `battery`) under `Assets/Icons/Lucide/` (ISC).
- Default Settings window size **720×560** (resizable); selected nav tint + accent highlight.

### Changed
- Version **1.11.0**. No TabControl tabs — `ListBox` nav switches content panels; all existing `x:Name` controls preserved so Apply/Load persistence is unchanged.
- Fixed `ShowSecondsStrip` write-back on Apply (was load-only).

### Note
- No swipe / Open-Meteo / island drag. FontAudio clock, hover/pin, fullscreen hide, SMTC, battery, timer preserved. Russian UI.


## 1.10.0 — Hover expand + click pin + hide on fullscreen

### Added
- **Hover expand**: hover Idle/Collapsed ~250 ms → peek richer idle (digital clock + seconds, date, weather/battery chips as enabled); leave → collapse after ~500 ms grace (re-enter cancels).
- **Click pin**: single click toggles pinned expanded Idle; survives pointer leave until click again or **Esc**.
- **Action Center**: pill **double-click** + tray/context menu «Центр уведомлений» (single click no longer opens AC).
- **Hide on fullscreen**: `SHQueryUserNotificationState` (D3D fullscreen / busy / presentation) + monitor-covering check; hide overlay (preferred). Optional `ClickThroughOnFullscreen` when hide is off. Fail-soft.
- Settings → **Островок**: `HoverExpandEnabled` (ON), hover delay ms, `ClickPinEnabled` (ON), `HideOnFullscreen` (ON), click-through secondary.
- **Seconds strip**: FontAudio `digital-dot` row along bottom inside edge of Idle/Collapsed (hover/pin too) — minute progress 0→59. Settings «Секундная полоска (digital-dot)» (`ShowSecondsStrip`, default ON). Core `SecondsStripLogic` + UI `SecondsStripView`.
- Core: `HoverPinMachine` + xUnit state-machine tests. Tokens: `HoverExpandDelayMs`, `HoverCollapseGraceMs`, `IdlePeekExtraW`, `FullscreenPollMs`.

### Changed
- Version **1.10.0**. Idle breath softens on hover-peek, pauses while pinned. FontAudio HH:mm clock + digital-dot strip / timer / SMTC / battery / clicks-only preserved — **no swipes**.

### Note
- No file shelf / clipboard / launcher.

## 1.9.1 — FontAudio digital segment clock

### Added
- **Digital clock** on Idle/Collapsed overlay: FontAudio (`fad`) 7-segment SVGs `HH:mm` (optional `HH:mm:ss`).
- Vendored icons: `digital0`…`digital9`, `digital-colon`, `digital-dot` under `Assets/Icons/FontAudio/` (CC BY 4.0, @fefanto). Offline — no Iconify at runtime.
- Settings → **Островок**: «Цифровые часы (FontAudio)» (`DigitalClockEnabled`, default **ON**); «Показывать секунды» (`ShowClockSeconds`, default OFF).
- Subtle colon opacity blink once per second; glyphs tint with theme text color (`currentColor`).
- Core helper `DigitalClockGlyphs` + UI `DigitalClockView`; xUnit mapping tests.

### Changed
- Version **1.9.1**. Text clock remains as fallback when digital toggle is OFF. Date chip unchanged. No clipped clock glyph.

### Note
- SMTC, battery, timer, clicks-only, idle breath preserved. No hover-expand. Attribution: `Assets/Icons/FontAudio/ATTRIBUTION.md`.

## 1.9.0 — Timer / stopwatch in the capsule

### Added
- **Countdown timer** in the island (`OverlayKind.Timer`): live `mm:ss` / `h:mm:ss`, Pause/Resume, Cancel, +1 мин.
- Start from **tray → Таймер** presets (1/5/10/25 мин + default), **Settings → Вид → Таймер**, or **F12**.
- On reach 0: notification «Таймер / Время вышло» + notify sound (if enabled), then Idle.
- **Stopwatch** (count-up) via Settings «Режим секундомера» + same Start/F12 path.
- Settings: `TimerEnabled` (default ON), `TimerDefaultMinutes` (default 5), `TimerStopwatchMode` (default OFF).
- Core: `IslandTimerLogic` + `OverlayPayload.CountUp`; `Tick` pauses when `Playing=false`, completes countdown → Notify.

### Priority
- **Running/paused timer owns the island** over SMTC Now Playing until cancel/complete. Brief battery/notify overlays still return to Timer via `_returnTo`. User click on Media allows SMTC to keep focus. Documented here.

### Changed
- Version **1.9.0**. Clicks-only (1.8.1) preserved — no swipe reintroduction.

### Note
- No hover-expand. No Open-Meteo. No island drag.


## 1.8.1 — Clicks only + stronger idle breath

### Changed
- **Gestures removed**: no swipe L/R/U/D to cycle slots or expand/collapse. Pointer is **clicks only** (≤`ClickMaxPx` → Action Center on Idle/Collapsed; right-click menu unchanged). Media Prev/Play/Next, tray, Settings, F9–F11 demos kept. `CycleNext`/`CyclePrev` remain in `OverlayMachine` for API/tests/demo.
- **Idle breath** more visible: scale ~1.0↔1.04 (+ slight X bias), width morph ±7 px, fill glow + border shimmer; period 2600 ms. Stops while expanded/notification/media/battery overlay; resumes on Idle/Collapsed. `AnimBreathEnabled` default ON.
- Settings: removed «Свайп rubber-band» and «Свайп» volume row; SMTC note no longer mentions свайп.

### Docs
- GUIDELINES / CONTEXT: gestures out, clicks only; breath tokens documented.

### Note
- Battery 1.8.0 + SMTC 1.7.0 preserved. Version **1.8.1**.

## 1.8.0 — Charging / battery pill + low-battery alert

### Added
- **Charge pill** (`OverlayKind.Battery`): transient morph on AC connect (and meaningful % bumps while charging) — title «Зарядка», subtitle percent, progress bar, auto-dismiss ~3.5s back to prior kind.
- **Low-battery alert** via `Notify` once per discharge cycle below threshold (default 20%, hysteresis +5% / AC reset).
- `WindowsPowerSource` — WinForms `SystemInformation.PowerStatus` poll (fail-soft); Core stays WinRT-free via `BatteryAlertLogic`.
- Settings → **Вид / Питание**: «Оповещения зарядки и низкого заряда» (`ShowBatteryAlerts`, default ON), «% в свёрнутом» (`ShowBatteryInCollapsed`, default OFF), порог (`LowBatteryPercent` 5–50), кнопка демо.
- Collapsed battery chip when enabled; icons `battery` / `bolt` in `IslandIcons`.
- Demo: **F10** charge pill, **F11** low-battery (Sandbox-friendly when AC state is fixed).

### Changed
- Version **1.8.0**. `OverlayMachine` WidthFor/Tick/DemoNext updated for Battery. CONTEXT + tests.

### Note
- Does not interrupt SMTC Now Playing except as brief overlay (returns to Media). No Open-Meteo. No island drag.

## 1.7.0 — Now Playing (Windows SMTC)

### Added
- **Live Now Playing** via Windows System Media Transport Controls (`GlobalSystemMediaTransportControlsSessionManager`): title, artist, play state, timeline progress, album art.
- `WindowsMediaSessionSource` in the Avalonia app — event + 1s poll; Play/Pause / Prev / Next try-invoke; fail-soft when WinRT/session missing (demo Media unchanged).
- Settings → **Вид**: «Показывать Now Playing» (`ShowNowPlaying`, default ON).
- Media row: artwork chip + Prev / Play-Pause / Next controls.
- `OverlayPayload.ArtworkBytes` (sanitized ≤2 MB) — Core stays WinRT-free.

### Changed
- App TFM **net8.0-windows10.0.19041.0** (WinRT projections). Version **1.7.0**.
- CONTEXT: SMTC is the real media source; demo remains for swipe/F9.

### Note
- Verify with Spotify / Edge media in Windows Sandbox. No Open-Meteo. No proprietary Nothing fonts.


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