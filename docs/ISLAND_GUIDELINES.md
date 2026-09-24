# NotifyIsland — правила островка (единый источник правды)

Документ обязателен при любой доработке UI/анимаций/функционала.
Конкретные числа важнее общих формулировок. При конфликте с README — этот файл побеждает.

Связанные файлы: [`CONTEXT.md`](../CONTEXT.md) (как запускать проект), [`OverlayTokens.cs`](../NotifyIsland.Core/OverlayTokens.cs) (числовые токены в коде).

---

## 0. Целевой стек (near-term product pick)

**NotifyIsland** — свой островок для Win11 (не копия Apple TM Dynamic Island / Live Activities и не Xiaomi Super Island «один в один»).  
Имена Apple / «Live Activities» в продукте не используем; глубину LA без Win API не имитируем.

### Must (near-term — уже есть или в работе)
| Фича | Статус |
|---|---|
| Now Playing (media kind; позже SMTC) | FSM + UI; SMTC — backlog |
| Timer / charging-style alerts (progress/timer/error) | есть |
| Compact ↔ expanded morph (width-only) | есть (явный timer morph) |
| Top-center capsule | Edge=Top + Offset |
| Fullscreen-safe / no-activate overlay | Win32 no-activate + z-order + **hide on fullscreen (1.10.0)** |
| Hover expand + click pin | **есть (1.10.0)** |
| Seconds strip (FontAudio digital-dot) | **есть (1.10.0)** |
| Weather (Windows-only) | есть |
| Tray | WinForms NotifyIcon |
| Settings tabs | есть |
| Gesture swipes | **убраны** (1.8.1) — только клики |
| Animation speed | есть |
| Color palette | есть (Вид) |
| Icon packs | IslandIcons + Tabler/Lucide vendored |
| Font size / family | есть (Вид) |
| Per-action animations | есть (вкладка Анимации) |
| Appear / Dismiss styles | Inflate/SlideDown/FadeScale/Bounce/Pop + Collapse/SlideUp/FadeScaleOut/Ragged/Glitch |
| Icon size ↔ FontSize | IconDip = FontSize × k |
| Nothing-inspired fonts | Space Grotesk / JB Mono (OFL), не NType82 |
| Date chip (no clock icon) | `DateFormat` рядом с HH:mm |
| Weather location mode | Windows / Manual + label |
| Theme presets | NothingDark / AppleQuiet / Ocean / Custom |

### Desktop differentiators (backlog — не реализовывать сейчас)
- File shelf (полка файлов у островка)
- Clipboard history chip
- Quick launcher / app strip

### Не делаем
- Копирование naming Apple TM / «Live Activities» как бренд
- Fake multi-island detached без Win cutout API
- Open-Meteo / third-party weather HTTP
- Mouse drag-reposition островка (позиция только Settings: Edge + Offset X/Y)


---

## 1. Референсы (выжимка)

### 1.1 iOS Dynamic Island + Live Activities
Источники:
- https://developer.apple.com/design/human-interface-guidelines/live-activities
- https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities

Состояния:
| Состояние | Когда | Что видно |
|---|---|---|
| **compact** | одна Live Activity | leading + trailing вокруг «дырки» |
| **minimal** | несколько Live Activities | одна прикреплена, вторая detached (круг/овал) |
| **expanded** | long-press / краткий апдейт | широкий блок с деталями и действиями |

Анимации (официально):
- Переход появления/морфа — **системный тайминг** (Apple не публикует мс).
- Кастомные анимации **контента** внутри LA: максимум **2000 мс**.
- Модификаторы `withAnimation` / `.animation` система **игнорирует** для презентации.
- Разрешены content transitions: `opacity`, `move`, `slide`, `push`.
- Размеры compact (пример 393×852): ~52×37 pt leading/trailing; corner radius островка ~44 pt.

Жесты: tap → приложение; touch-and-hold → expanded.

### 1.2 Xiaomi HyperOS «Super Island» / Focus Notification
Источники:
- https://dev.mi.com/xiaomihyperos/documentation/detail?pId=2140
- https://help.aliyun.com/en/document_detail/3037956.html
- https://eu.36kr.com/en/p/3444790738196866

Отличия от Apple:
| | Apple DI | Xiaomi Super Island |
|---|---|---|
| Состояния | compact / minimal / expanded | **summary** ↔ **expanded** (+ AOD / lock / shade) |
| Несколько | minimal detached | свайп между островами; свайп «за край» — скрыть все |
| Жесты | tap, long-press | ~~swipe~~ **clicks only** (1.8.1); no pull-down |
| Автосворот expanded | системный | по умолчанию **~5 с** (`islandFirstFloat` / `enableFloat`) |
| Архитектура | ActivityKit | поверх Focus Notification + payload `param_v2.*` |

### 1.3 Samsung / Nothing / прочее
- **Samsung One UI**: нет полноценного DI; близки edge-панели, Now Bar / Live Notifications (зависят от версии) — брать идею persistent status, не копировать форму.
- **Nothing OS**: Glyph / точечные индикаторы — референс для **минимального unread-dot**, не для широкой капсулы.
- Open-source: https://github.com/d4viddf/hyperisland-toolkit (DSL под Xiaomi payloads) — полезен как каталог шаблонов (media/timer/taxi), не как UI для Win11.

---

## 2. Правила анимаций NotifyIsland (actionable)

Код: `OverlayTokens.MorphMs` (**420**), `AnimationTiming` + `AppSettings.AnimationSpeed` (default **Slow**) + per-action + **`AppearStyle`/`DismissStyle`**; **явный timer-morph** `StartMorph`/`OnMorphTick` на Width/Height + aux (opacity/scale/translate/jitter); `AnimationEasing` (CubicOut / SpringOut / Pop / Glitch — без linear); pulse/breath — timer без Opacity/Scale Transition; `ApplyAnimationSettings()` в ctor + Settings Apply.

Базовые длительности (**Normal**). Множители: **Slow≈1.6×**, **Normal=1×**, **Fast≈0.55×**, **Off→1 мс**. Defaults lean Slow.

| Переход | Длительность (Normal) | Easing | Примечание |
|---|---:|---|---|
| Width morph (idle ↔ notify/media/…) | **420 мс** | Soft CubicOut / Spring | высота **всегда** `CollapsedH`; Bounce/Pop/Ragged/Glitch могут ≥460–480 base |
| Appear styles | × morph | см. enum | Inflate, SlideDown (−20→0 Y + fade), FadeScale (0.85→1), Bounce (spring), Pop (punch) |
| Dismiss styles | × morph | см. enum | Collapse, SlideUp, FadeScaleOut, Ragged (X jitter), Glitch (stutter) |
| Hover border/background | **160 мс** | `CubicEaseInOut` | лёгкий +0.06 fill alpha |
| Unread-dot pulse (unread &gt; 0) | **1600 мс**/цикл | sine | opacity 0.40↔1.0; Idle/Collapsed |
| Idle breathing | **3200 мс**/цикл | sine | scale ±2.5%; пауза во время morph |
| Notify auto-dismiss | **4000 мс** | — | затем dismiss-style → idle |
| Demo scene cycle | **1800 мс** | — | циклирует Appear/Dismiss styles |
| Swipe rubber-band | **180 мс** | CubicOut | |
| Icon crossfade | **240 мс** | CubicOut | |
| Icon DIP | — | — | `FontSize × 1.0` (clock/weather), `FontSize × 0.92` (kind) |

Запрещено:
- менять высоту капсулы при notify (не «расти вверх»);
- резкий snap без transition на Width (кроме AnimationSpeed=Off);
- linear easing на morph;
- сторонние HTTP weather API — см. §10.

---

## 3. Состояния и переходы

FSM: `OverlayMachine` / `OverlayKind`.

| Kind | UI | Ширина (токен) |
|---|---|---|
| `Idle` / `Collapsed` | часы + outline clock + unread-dot; при `WeatherEnabled` — compact weather (icon+temp) | `CollapsedW`=140 / `CollapsedWeatherW`=210 |
| `Notification` | иконка · title · body · badge | > CollapsedW, ≤ ExpandedMaxW |
| `Progress` | title · % · тонкий progress 2px | expanded |
| `Media` | media icon · title/subtitle · play · progress | ~400 |
| `Timer` | таймер | expanded |
| `Error` | error colors | expanded |
| `Expanded` | обзор | expanded |
| `Weather` | dynamic weather icon · «Ясно · 18° · 0%» | ~360 |

Правила:
1. Горизонтальный режим: высота всегда **`CollapsedH = 30`**, морф только по ширине.
2. Вертикальный режим (Orientation=Vertical или Auto на Left/Right): ширина = `CollapsedH`, морф по **высоте** (длинная ось).
3. CornerRadius = `min(Width,Height) / 2` (пиксель-капсула).
4. Notify инкрементит `UnreadCount`; `Clear` сбрасывает; `Collapse` **сохраняет** unread.
5. Idle/Collapsed: **одинарный клик** → pin/unpin (`ClickPinEnabled`); **двойной клик** → `ms-actioncenter:`; Esc → unpin. (1.10.0; раньше single = Action Center.)
6. Right-click → быстрое меню (Центр уведомлений / Demo / Погода / Свернуть / **Настройки…** / Выход). Полные настройки — только в окне Settings (§7).
7. CycleNext/Prev (API) **не** инкрементит unread (Notification slot = demo seed). UI-свайпов нет.

---

## 3b. Ввод: только клики (жесты убраны в 1.8.1)

| Константа | Значение | Смысл |
|---|---:|---|
| `ClickMaxPx` | **12** | ≤12 px → click (pin на Idle/Collapsed; double → Action Center) |
| `HoverExpandDelayMs` | **250** | hover → peek |
| `HoverCollapseGraceMs` | **500** | leave grace |
| `IdlePeekExtraW` | **20** | extra width while peek/pin |
| (legacy) `SwipeFirePx` / `SwipeRubberMs` | 48 / 180 | **не используются** UI; оставлены для совместимости |

- Свайп L/R/U/D **не** циклит слоты и **не** expand/collapse.
- `CycleNext` / `CyclePrev` остаются в `OverlayMachine` для API / unit-тестов / demo.
- ПКМ → контекстное меню; Media Prev/Play/Next — отдельные кнопки; F9–F12 demos — клавиатура.
- Hover-peek / click-pin: Core `HoverPinMachine` (1.10.0). Без свайпов.
- Fullscreen: `HideOnFullscreen` → hide via `Win32Overlay.IsFullscreenOrBusy`; optional click-through.

### Idle breath (усилен 1.8.1; soften 1.10.0)
| Токен | Значение |
|---|---:|
| `BreathPeriodMs` | **2600** |
| `BreathScaleAmp` | **0.04** (~1.0↔1.04) |
| `BreathWidthAmpPx` | **±7** |
| `BreathGlowAmp` | **0.14** |

Только Idle/Collapsed при `AnimBreathEnabled` (default ON). Стоп на notification/media/battery/expanded. Soften (~25%) на hover-peek; пауза при pin.

---

## 4. Индикаторы, цвета, типографика

| Токен | Значение |
|---|---|
| Fill | `#080808` (настраивается: `ColorCapsuleFill`) |
| Text | `#FFFFFF` (`ColorTextPrimary`) |
| Text secondary | `#C8C8CC` (`ColorTextSecondary`) |
| Accent / badge / unread | `#3D9CF0` (`ColorAccent`; glow чуть ярче) |
| Error | `#E8A0A0` |
| Font | Segoe UI Variable / Segoe UI, title SemiBold 12, clock 12, badge 10 |
| Unread dot | 7×7, BoxShadow glow, opacity transition |

### 4b. Иконки (единый pack)

- Файл: `IslandIcons.cs` + заметка `Assets/Icons/README.md`.
- Язык: **outline**, stroke **`IconStroke = 1.75`**, round caps/joins, design space 24×24.
- Размеры: collapsed **12**, kind chip **11** (`IconSizeCollapsed` / `IconSizeKind`).
- Палитра: secondary `#C8C8CC` на тёмной капсуле; белый на accent chip — читается и на light, и на dark Win11 chrome.
- Weather keys динамические по WMO-like code (`WeatherCodes.IconKey`): clear / partly / cloud / fog / drizzle / rain / snow / storm; смена с **crossfade 240 мс**.
- Kind keys: `clock`, `notify`, `media`, `timer`, `progress`, `error`.
- Без платных/проприетарных пакетов; геометрии в стиле MIT Fluent / Tabler outline.

---

## 5. Функционал: есть / убрать / добавить

### Сейчас есть
- Idle clock + unread glow + optional minimal weather (+ WeatherSide L/R)
- Notification morph (H: width / V: height), badge
- Progress / Media / Timer / Error / **Weather** (FSM + demo)
- **Clicks only** (no swipe); idle breath scale/width/glow
- Weather toggle (tray / ПКМ / Settings), Windows-primary source (§10)
- Unified outline icon pack + weather crossfade + tray icons
- **Tray** quick menu + unread icon/tooltip
- **Settings window** (отдельный Window, single-instance, **TabControl** по категориям): placement, z-order, opacity, sounds, orientation, drag/XY
- Z-order Topmost / Desktop / BehindApps (Win32 SetWindowPos)
- Edge + OffsetX/Y (без mouse drag)
- Opacity 0.35–1.0 на fill; AnimationSpeed (Slow|Normal|Fast|Off) с pulse/breath; SoundPack (Nothing|Ios|System|Off) + SoundEnabled + master/per-event volumes
- Demo cycle (`--demo` / F9); Click → Action Center
- Unit-тесты FSM + AppSettings + IslandLayout + python FSM script

### Убрать / не раздувать (обоснование)
| Что | Почему | Референс |
|---|---|---|
| Автоцикл demo как «продуктовая фича» | только QA; не держать в релизе по умолчанию | Apple: LA только реальные события |
| Open-Meteo / любой third-party weather HTTP | пользовательский запрет; Windows-only путь | — |
| Pull-down мини-окно Xiaomi | другой UX, сложно на Win11 overlay | Xiaomi-only gesture |
| Detached second island | нет cutout-камеры на ПК | Apple minimal — hardware-specific |

### Оставить
| Что | Почему |
|---|---|
| Width-only morph + fixed H | DI «растягивание», без прыжка вверх |
| Unread dot + badge | Nothing Glyph minimalism + DI trailing badge |
| Action Center click | Windows-native аналог «открыть уведомления» |
| Media/Progress/Timer/Weather kinds в FSM | Live Activities / Super Island templates |
| Click → Action Center | Windows-native |

### Добавлено в этом workstream
Tray, Settings window, WeatherSide, Edge+Offset (no drag), Orientation, Z-order×3, Opacity, AnimationSpeed (+ pulse/breath), color palette, Sound packs, icon pack stub.

### Добавить позже
| Что | Обоснование |
|---|---|
| Реальный **SMTC** media | заменить мок `Night Drive` |
| Очередь уведомлений / счётчик &gt;1 | multi-island |
| Полный CsWinRT Geolocator + стабильный Bing cache parser | углубить §10 |
| Start with Windows | удобство |

---

## 6. Демо и источники данных

**Медиа в демо:** захардкоженный мок (`Night Drive` / `Local Radio`), не SMTC.

**Погода в демо:** `SetWeather` в `RunDemoStep()`; данные из `WindowsWeatherSource` (или stub `WeatherCodes.MockMoscow()` = ясно 18°).

- Play/pause в UI только переключает флаг `Playing` в FSM (`OnMediaPlay`), звук не играет.

---

## 7. Трей и настройки

### Tray (реализовано)
- Primary: `WinFormsTray` (`System.Windows.Forms.NotifyIcon`) — надёжно видно в Win11 / Sandbox.
- Fallback: Avalonia `TrayService` (`TrayIcon`), если WinForms недоступен.
- Иконки: `Assets/tray.png` / `tray-unread.png` (outline IslandIcons).
- Левый клик → показать/скрыть островок.
- Правый клик → меню: «Открыть настройки», «Показать/скрыть островок», «Демо вкл/выкл», «Погода вкл/выкл», «Выход».
- Двойной клик → центр уведомлений Windows (`ms-actioncenter:`).
- Unread > 0 → `tray-unread.png` (точка-индикатор) + tooltip с числом.
- TargetFramework: `net8.0-windows` + `UseWindowsForms` (см. `Directory.Build.props` / `EnableWindowsTargeting`).

### Окно настроек (отдельный Avalonia `Window`)
- Открытие: трей «Настройки…» **и** ПКМ по островку «Настройки…».
- **Не** popup / не flyout / не dump в ContextMenu.
- Single-instance: повторное открытие → `Activate()` существующего.
- Геометрия окна Settings persist: `SettingsWindowX/Y/Width/Height` (отдельно от OffsetX/Y островка); default ~**520×640**.
- UI: Avalonia **`TabControl`** по категориям (тёмная тема `#1C1C1E`), не один длинный scroll-pile. Низ окна — DockPanel: Отмена / Применить / OK.
- Вкладки (RU):
  1. **Островок** — `IslandVisible` + **`DateFormat`** + digital clock + **hover/pin/fullscreen** (1.10.0). Иконка часов убрана.
  2. **Погода** — `WeatherEnabled`, `WeatherSide`, **`WeatherLocationMode`** (Windows|Manual), `WeatherLocationName`, lat/lon + пресеты городов. Заметка: температура из Windows; при Manual — выбранное имя на expanded/tooltip. Без third-party HTTP.
  3. **Расположение** — `Edge` (Top/Bottom/Left/Right), `OffsetX`/`OffsetY`, `Orientation` (Auto|Horizontal|Vertical)
  4. **Тема** — `ThemePreset`: NothingDark | AppleQuiet | Ocean | Custom. Сток Apply перезаписывает палитру/шрифт/анимации/иконки/дату/звук; расхождение → Custom; кнопка «Перейти в кастом».
  5. **Вид** — `ZOrderMode` + `Opacity` + **палитра** + FontSize/FontFamily (редактируемо при Custom)
  6. **Анимации** — master + per-action + Appear/Dismiss
  7. **Звуки** — `SoundEnabled`, pack `Nothing`|`Ios`|`System`|`Off`, master `SoundVolume`, per-event `SoundVol*`
  8. **Иконки** — `IconPack` (IslandIcons / Tabler / Lucide / Meteocons*); см. `docs/ICON_PACKS.md`
- Все `x:Name` контролов сохранены — `LoadUi` / `ReadUi` / `WireVolumeLabels` без ломки.

### Theme presets (1.6.0)

| Preset | Fill | Accent | Font | Icons | Anim lean | Date | Sound |
|---|---|---|---|---|---|---|---|
| **NothingDark** | `#080808` | `#3D9CF0` | SpaceGrotesk | MeteoconsFill | Slow + Bounce/Ragged | DayMonth | Nothing |
| **AppleQuiet** | `#1C1C1E` | `#0A84FF` | System | MeteoconsLine | Slow + FadeScale | WeekdayShort | Ios |
| **Ocean** | `#0A1628` | `#00C2A8` | JetBrainsMono | MeteoconsFlat | Normal/Fast + Slide | Numeric | Nothing |
| **Custom** | — | — | — | — | user | user | user |

Код: `ThemePresets.Apply` / `Matches` / `AutodetectCustom`.

### Позиция (без drag)
- Мышиное перетаскивание островка **удалено**. `AllowDrag` всегда `false` (Normalize мигрирует старые settings).
- Позиция только через **Расположение**: Edge + OffsetX/Y (+ Orientation).
- Указатель на островке: свайпы L/R (виджеты), up expand, down collapse, click → Action Center.

### Persist
`%LOCALAPPDATA%/NotifyIsland/settings.json` — все поля `AppSettings` (Weather*, Edge, Offsets, Orientation, ZOrder, Opacity, AnimationSpeed, Sound*, IslandVisible, SettingsWindow*).

### Sound packs
- Folders: `Assets/Sounds/nothing/`, `ios/`, optional `system/` — each has `notify|expand|collapse|swipe|error|hover.wav` (&lt;300 ms).
- **Legal:** original synthesized tones *inspired by* soft Glyph-like clicks / soft iOS-like taps — **not** official Nothing OS or Apple iOS system sounds. See `Assets/Sounds/README.md`.
- Runtime: `IslandSounds` loads from `AppContext.BaseDirectory/Assets/Sounds/{pack}/` via SoundPlayer / winmm; `System` uses SystemSounds; `Off` silent.
- Settings: pack combo + master volume + per-event volumes. Triggers: notify appear, morph expand/collapse, swipe commit, error, optional hover (debounced).



---

## 8. Доступность и Win11

- `AutomationProperties.Name` на интерактивных контролах.
- Контраст текста к `#080808` ≥ обычного UI (белый/серый).
- Не перехватывать фокус (`ShowActivated=False`, Win32 no-activate).
- Лог: `%TEMP%\notifyisland.log`.

---

## 9. Чеклист перед PR

- [ ] Высота капсулы не изменилась (осталась 30).
- [ ] Morph Width base 420 мс Soft easing (× AnimationSpeed); Appear/Dismiss styles на Notification.
- [ ] Unread: Notify++, Clear=0, Collapse сохраняет; cycle seed не ++.
- [ ] Нет Open-Meteo / third-party weather HTTP.
- [x] Clicks only (`ClickMaxPx=12`); swipe UI removed (1.8.1). Idle breath tokens documented.
- [ ] Новые kinds описаны здесь и покрыты тестом в `NotifyIsland.Tests`.
- [ ] CONTEXT.md не дублирует числа — ссылается сюда.
- [ ] Settings — отдельный Window; tray меню только quick actions.
- [ ] Opacity только fill alpha; z-order не ломается.
- [ ] Звуки: pack WAV original (не proprietary Nothing/Apple); mute через Off / SoundEnabled / Volume; hover только debounce.

---

## 10. Погода — Windows-only (без Open-Meteo)

**Почему не виджет Windows Weather API напрямую:** у сторонних приложений **нет** стабильного публичного API виджета «Погода» Win11. Поэтому primary path — Windows-поверхности с честным fallback.

Конвейер `WindowsWeatherSource` / `IWeatherSource` (без сети к open-meteo/msn/…):

1. **Primary (Win11):** WinRT `Windows.Devices.Geolocation` (тип через reflection в portable-сборке; полный CsWinRT — follow-up) + best-effort чтение локального кэша Bing Weather / Widgets (`TryReadBingWeatherCache` под `%LOCALAPPDATA%\Packages\Microsoft.BingWeather*` / WebExperience / WidgetsRuntime).
2. **On-disk cache:** `%LOCALAPPDATA%/NotifyIsland/weather-cache.json` после успешного Windows-чтения.
3. **Sandbox / non-Windows / нет кэша:** `LocalStubWeather` → `WeatherCodes.MockMoscow()` (ясно · 18° · 0%). **Никакого HTTP.**

UI:
- **Minimal** (Idle/Collapsed + `WeatherEnabled`): outline weather icon + `18°` рядом с **временем и датой** (без clock icon); ширина `CollapsedWeatherW` (**240**). Date: `DateFormat`.
- **Expanded** (`OverlayKind.Weather`): icon + `Ясно · 18° · 0%` (precip если есть).
- Refresh: startup + каждые **15 мин** (`WeatherRefreshMs`).
- Payload: `TemperatureC`, `WeatherCode`, `PrecipProb` (+ Title/Subtitle/Body согласованы через `WeatherCodes.ToPayload`).

**Open-Meteo НЕ используется и не должен добавляться.**
