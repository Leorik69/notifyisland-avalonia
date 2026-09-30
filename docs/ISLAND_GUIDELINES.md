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
| Settings sidebar (Lucide nav) | **есть (1.11.0)** |
| Gesture swipes | **убраны** (1.8.1) — только клики |
| Animation speed | есть |
| Color palette | есть (Оформление) |
| Icon packs | IslandIcons + Tabler/Lucide vendored |
| Font size / family | есть (Оформление) |
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

Код: `OverlayTokens.MorphMs` (**420**), `AnimationTiming` + `AppSettings.AnimationSpeed` (default **Slow**) + per-action + **`AppearStyle`/`DismissStyle`**; **явный timer-morph** `StartMorph`/`OnMorphTick` на Width/Height + aux (opacity/scale/translate/jitter); `AnimationEasing` (CubicOut / SpringOut / Pop / Glitch — без linear); pulse — timer без Opacity/Scale Transition; `ApplyAnimationSettings()` в ctor + Settings Apply.

Базовые длительности (**Normal**). Множители: **Slow≈1.6×**, **Normal=1×**, **Fast≈0.55×**, **Off→1 мс**. Defaults lean Slow.

| Переход | Длительность (Normal) | Easing | Примечание |
|---|---:|---|---|
| Width morph (idle ↔ notify/media/…) | **420 мс** | Soft CubicOut / Spring | высота **всегда** `CollapsedH`, **кроме `SystemStats`** (см. строку ниже); Bounce/Pop/Ragged/Glitch могут ≥460–480 base |
| Appear styles | × morph | см. enum | Inflate, SlideDown (−20→0 Y + fade), FadeScale (0.85→1), Bounce (spring), Pop (punch) |
| Dismiss styles | × morph | см. enum | Collapse, SlideUp, FadeScaleOut, Ragged (X jitter), Glitch (stutter) |
| Hover border/background | **160 мс** | `CubicEaseInOut` | лёгкий +0.06 fill alpha |
| Unread-dot pulse (unread &gt; 0) | **1600 мс**/цикл | sine | opacity 0.40↔1.0; Idle/Collapsed |
| Notify auto-dismiss | **4000 мс** | — | затем dismiss-style → idle |
| Icon crossfade | **240 мс** | CubicOut | |
| Icon DIP | — | — | `FontSize × 1.0` (clock/weather), `FontSize × 0.92` (kind) |
| ClickPop (chevron/cycle ack) | **210 мс** (= MorphMs/2) | CubicEaseOut | 1.0 → 1.08 → 1.0, never below 1. Морф в полёте выигрывает `_pillScale` — поп не стартует поверх него |
| First-appear wobble | **210 мс** (= MorphMs/2) | damped sine | ±1 DIP translate X, огибающая `(1−t)²`; играется при выходе из фулскрина (оба варианта) и при возврате из трея |
| Peek expand (Idle → SystemStats) | **250 мс** задержка (`HoverExpandDelayMs`) + MorphMs | Soft | закрывается по уходу курсора + **grace 5000 мс** (`HoverCollapseGraceMs`) |
| SystemStats height morph | **MorphMs = 420 мс** | Soft | 30 → `StatsHeightFor(rowCount, marquee)`; единственное исключение из «высота всегда CollapsedH» |
| Погода Meteocons: вращение | **6000 мс** (`weather-clear`) / **10 000 мс** (`weather-partly`) | линейный угол | `MeteoconsSpinClearMs` / `MeteoconsSpinPartlyMs`; тик 33 мс ≈ 30 fps |
| Погода Meteocons: покачивание | **3000 мс** (cloud / drizzle / rain / snow / sleet) | `sine.inOut` | ±`MeteoconsBobDip` = **2.5 DIP** по Y |
| Погода Meteocons: пульс | **1200 мс** (storm) / **2400 мс** (fog) | треугольник | opacity до `MeteoconsPulseStormMin` 0.55 / `MeteoconsPulseFogMin` 0.72 |
| Двоеточие часов | **1000 мс** (чётное сек — горит) | — | декорация; под reduced motion закреплено горящим |
| Точки секунд (полоса внизу капсулы) | **1000 мс** на деление | — | индикатор прошедших секунд минуты; **не** гасится под reduced motion — это данные, а не украшение |
| Полоса прогресса капсулы | — | — | нижние 8 DIP; приоритет clipboard &gt; media &gt; timer (`CapsuleProgressBand.BandState`) |

Запрещено:
- менять высоту капсулы при notify (не «расти вверх»); исключение — `SystemStats`, высота которого считается по числу строк;
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
| `SystemStats` (1.12.1) | строки CPU / Память / Батарея / Сеть / дата — набор и порядок задаёт пользователь (`AppSettings.StatsRows`) | `StatsExpandedW` = 300 |
| `Clipboard` (1.12.0) | **больше не целый kind в нормальной работе** (см. правило 8) | — |
| **Split-половина буфера** (1.12.2) | не kind, а **флаг на машине**: `OverlaySnapshot.IsSplitClipboard` + payload `SplitClipboard`; добавляется к **любому** текущему kind'у и не заменяет его | `ClipboardSplit.SplitLongAxisFor(kind) = widthFor(kind) + ClipboardHalfW` |

Правила:
1. Горизонтальный режим: высота всегда **`CollapsedH = 30`**, морф только по ширине.
   **Исключение (1.12.1):** `OverlayKind.SystemStats` — высота считается от числа строк через `StatsLayout.StatsHeightFor(rowCount, marquee)` (108 DIP при дефолтных 5 строках, 48 DIP у пресета «Кратко»; при включённой бегущей строке добавляется `MarqueeTrack.LineH`), ширина фиксирована `StatsExpandedW = 300` DIP. Пилюля раскрывается по наведению (`HoverExpandDelayMs = 250 мс`) и сворачивается при уходе курсора (grace `HoverCollapseGraceMs = 5000 мс`, 1.13.0 — было 500, панель исчезала раньше, чем глаз успевал перевести взгляд). Автосворачивание по таймеру 30 с и клик-открытие удалены. Метрики в свёрнутом виде не показываются. Панель строится динамически в порядке `AppSettings.StatsRows`; пустой набор строк сворачивает поверхность (`StatsLayout.ShouldCollapseStatsSurface`). Пока курсор над **любой** из поверхностей — капсула, шар, панель истории — панель не схлопывается (рефкаунт `_uiHoverCount`). Подробности — [`docs/superpowers/specs/2026-09-29--notifyisland-system-stats-rework.md`](superpowers/specs/2026-09-29--notifyisland-system-stats-rework.md).
2. Вертикальный режим (Orientation=Vertical или Auto на Left/Right): ширина = `CollapsedH`, морф по **высоте** (длинная ось).
3. CornerRadius = `min(Width,Height) / 2` (пиксель-капсула).
4. Notify инкрементит `UnreadCount`; `Clear` сбрасывает; `Collapse` **сохраняет** unread.
5. Idle/Collapsed: **одинарный клик** → pin/unpin (`ClickPinEnabled`); **двойной клик** → `ms-actioncenter:`; Esc → unpin. (1.10.0; раньше single = Action Center.)
6. Right-click → быстрое меню (Центр уведомлений / Таймер (если включён) / Погода / **Настроить монитор…** (1.12.1 — открывает окно Settings сразу на разделе «Монитор», даже если окно уже видно) / разделитель / Свернуть / **Настройки…** / Выход). Полные настройки — только в окне Settings (§7). **1.13.0:** пункт «Демо» и весь demo-режим удалены из продукта; при правом клике **по шарику** вместо этого появляется блок буфера (История буфера / Очистить историю / Закрепить шарик / Не реагировать 30 мин) — тот же `OpenContextMenu(isBallContext: true)`.
7. CycleNext/Prev (API) **не** инкрементит unread. UI-свайпов нет.
8. **Split-буфер (1.12.2).** Копирование не забирает пилюлю: `OverlayCommand.SetClipboardSplit` ставит флаг, островок сохраняет свой kind и содержимое (часы, дата, погода, батарея, unread-dot), а буфер живёт в half-pill рядом. Реализовано **флагом на машине, а не новым `OverlayKind`**: половина ортогональна kind'у — у неё свой `OverlayPayload` (`SplitClipboard`), своё время жизни (`SplitMsLeft`) и своя граница.
9. Что осталось от `OverlayKind.Clipboard`: сам enum и `OverlayCommand.SetClipboard` в Core **сохранены** (их держат тесты `ClipboardSplitTests` и API-контракт), но приложение их **больше не диспатчит** — `OnClipboardCaptured` шлёт только `SetClipboardSplit`. Нормальный путь буфера — шар; целый kind остался как запасной путь и как документация прежнего поведения.
10. **Геометрия split (1.12.2).** Горизонтально: островная половина `CollapsedW` = **170 DIP** + буферная `ClipboardHalfW` = **200 DIP** = **370 × 30 DIP**. Высота по-прежнему **всегда `CollapsedH` = 30** — split правило не нарушает, он добавляет только длину. На Left/Right длинная ось вертикальна, поэтому та же арифметика даёт **30 × 370**: ширина — тонкая капсула `CollapsedH`, половина буфера встаёт **вниз** (у дальнего конца длинной оси), а её содержимое (иконка + текст) поворачивается на **−90°** и верстается как обычная строка 200 × 30 внутри повёрнутой коробки. Итоговое правило: буферная половина всегда живёт длинной стороной `ClipboardHalfW` и всегда кросс-стороной `CollapsedH` — она физически не может выйти за капсулу.
11. `SystemStats` от split не разделяется: фиксированный блок не делится (`IslandLayout.SizeFor` возвращает его размер раньше, чем смотрит на split). Прочие kind'ы — `widthFor(kind) + ClipboardHalfW`; для kind'ов с клампом в `ExpandedMinW/MaxW` половинка добавляется **после** клампа (split Notification = 380 + 200, а не 460).
12. **1.12.3: half-pill заменён goo-шаром.** Флаг `IsSplitClipboard`, его время жизни и резервирование `ClipboardHalfW` по длинной оси **живы** — это до сих пор держит окно раскрытым. Но сама половинка не рисуется как половина `Border`: буфер едет в **шаре** диаметром `BlobD = 64 DIP`, который живёт отдельным контролом **вне капсулы** и соединяется с ней верёвочной перемычкой (`BlobBridgeMin` 18 DIP, полуоснование 3.0 / 2.5 DIP). Правила 10–11 описывают резервирование места, а не форму: читать «170 + 200 = 370» как «370 × 30 DIP» уже нельзя, видимая форма — капсула плюс шар на верёвке. Спека: [`docs/superpowers/specs/2026-09-29--notifyisland-goo-blob.md`](superpowers/specs/2026-09-29--notifyisland-goo-blob.md).

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
- **Split-половина (1.12.2):** обе половины — один `Border`, hit-test общий; решение принимается один раз в момент нажатия по координате **длинной** оси (X на Top/Bottom, Y на Left/Right) против `ClipboardSplit.ClipboardHalfStart`.
  - Клик по **островной половине** — как раньше: одиночный = пин/анпин, двойной = Action Center.
  - Клик по **буферной половине** → `ClipboardClickAction`: `Dismiss` (только закрыть) или `DismissAndClear` (закрыть и очистить системный буфер через `WriteText("")` — открывает буфер, пишет пустую строку, ничего не вставляет). Правая кнопка в любом месте по-прежнему открывает контекстное меню.
  - Зоны ⅓/⅓/⅓ цикла буфера меряются по **островной половине** (`ClipboardSplit.IslandHalfExtent` = 170 DIP), а не по всей пилюле: иначе при делении обе границы зон уехали бы вправо. Ось — длинная, то есть та же, что у hit-test половины.
- `CycleNext` / `CyclePrev` остаются в `OverlayMachine` для API и unit-тестов.
- ПКМ → контекстное меню; Media Prev/Play/Next — кнопки в строке «Плеер» монитора; **F12** — таймер, **Esc** — открепить/свернуть. Демо-режима в продукте нет (удалён в 1.13.0).
- Hover-peek / click-pin: Core `HoverPinMachine` (1.10.0). Без свайпов.
- Fullscreen: `HideOnFullscreen` → hide via `Win32Overlay.IsFullscreenOrBusy`; optional click-through.

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

### 4b. Layout thresholds (system stats)

| Параметр | Значение | Пояснение |
|---|---:|---|
| Stats expanded height | **считается от числа строк** (`StatsLayout.StatsHeightFor(rowCount)`) | 108 DIP при дефолтных 5 строках, 48 DIP при 2 строках («Кратко»). Истина о высоте — `StatsHeightFor`, а не `StatsExpandedH` |
| Stats expanded width | **300 DIP** (`StatsExpandedW`) | 1.12.1: фиксировано, не инфлируется по числу строк |
| CPU warn / crit | **≥70 % / ≥90 %** | цвет значения: accent `#3D9CF0` / error `#E8A0A0` |
| RAM warn / crit | **≥85 % / ≥95 %** от total | та же раскраска |
| Battery warn / crit | **≤20 % / ≤10 %** | та же раскраска |
| (legacy) `OverlayTokens.StatsExpandedH` / `StatsLayout.StatsPillWidth` | 108 / 300 DIP | **не управляют layout** — оставлены как якоря, на них ссылаются только тесты |
| Stats metric slot | **56 DIP** | 1.12.0, только для расчёта видимых метрик в Idle |
| Stats row thresholds | **280 / 380 / 480 / 620 DIP** | 1.12.0, пороги `StatsMinPillW` / `StatsShowTwoMetricsW` / `StatsShowThreeMetricsW` / `StatsShowAllMetricsW` — с 1.12.1 не влияют на ширину |
| Stats screen margin | **48 DIP** (`StatsScreenMarginPx`) | отступ от края экрана |

Код: `NotifyIsland.Core/OverlayTokens.cs` + `NotifyIsland.Core/StatsLayout.cs`. В 1.12.1 метрики вынесены из свёрнутой пилюли в отдельную поверхность, поэтому `StatsMinPillW` / `StatsShow*MetricsW` / `StatsMetricSlotW` больше не влияют на ширину — `WidthFor(SystemStats)` всегда возвращает `StatsExpandedW`.

### 4b-1. Layout tokens: split clipboard half (1.12.2)

| Параметр | Значение | Пояснение |
|---|---:|---|
| Island half (long axis) | **170 DIP** | `OverlayTokens.CollapsedW`; столько же, сколько свёрнутая пилюля без батареи |
| `ClipboardHalfW` | **200 DIP** | длинная сторона буферной половины; итог 170 + 200 = **370** по длинной оси |
| `ClipboardDividerW` | **1 DIP** | толщина разделителя (`#FFFFFF` @ 10 %) |
| `ClipboardDividerCross` | **16 DIP** | длина разделителя **поперёк** длинной оси; на вертикальном краю это его ширина, на горизонтальном — высота, одно и то же число |
| `ClipboardHalfGap` | **8 DIP** (по 4 DIP с каждой стороны) | отступ половин **от** разделителя, остаётся внутренним padding'ом; длину не добавляет. Разделитель центрируется на границе половин отрицательным margin'ом `DividerFrame.NearMargin` |
| `ClipboardHalfFadeDelay` | **0.25** (¼ морфа) | доля прогресса морфа, которую половина ещё невидима перед началом fade |
| `ClipboardHalfPopPeak` | **1.06** | пик пере-увеличения; та же кривая `ClickPop`, что у ack клика, только с другим пиком |
| `ClipboardHalfBreatheMs` | **2400 мс** | период синуса покачивания в покое |
| `ClipboardHalfBreathePx` | **0.5 DIP** | амплитуда; всегда по поперечной оси, никогда по длинной |
| Half content margin | **4 / 10 DIP** по краям, spacing **4 DIP**, иконка **12 DIP**, текст `MaxWidth` **166 DIP** | 200 − 12 − 4 − 4 − 10 = 166; на вертикальном краю та же строка просто повёрнута |

Код: `NotifyIsland.Core/OverlayTokens.cs` (константы) + `NotifyIsland.Core/ClipboardSplit.cs` (чистая геометрия и кадр анимации) + `NotifyIsland.Core/ClipboardHalfPreview.cs` (иконка формата и правила превью). Граница половины — **единственная функция** `ClipboardSplit.ClipboardHalfStart`: её используют и отрисовка (margin разделителя, анкоринг), и hit-test (`IsInHalf`), и зоны ⅓/⅓/⅓ (`IslandHalfExtent`), поэтому разойтись они не могут. Все три transform'а половины живут на трёх **разных** элементах и на разных осях (ориентация → `ClipboardHalfContent`, морф по длинной → `ClipboardHalfMotion`, breathe по поперечной → `ClipboardHalf`), поэтому ни один кадр не затирает другой ни на одном краю.

### 4c. Иконки (единый pack)

- Файл: `IslandIcons.cs` + заметка `Assets/Icons/README.md`.
- Язык: **outline**, stroke **`IconStroke = 1.75`**, round caps/joins, design space 24×24.
- Размеры: collapsed **12**, kind chip **11** (`IconSizeCollapsed` / `IconSizeKind`).
- Палитра: secondary `#C8C8CC` на тёмной капсуле; белый на accent chip — читается и на light, и на dark Win11 chrome.
- Weather keys динамические по WMO-like code (`WeatherCodes.IconKey`): clear / partly / cloud / fog / drizzle / rain / snow / storm; смена с **crossfade 240 мс**.
- Kind keys: `clock`, `notify`, `media`, `timer`, `progress`, `error`.
- Clipboard format keys (1.12.2, `ClipboardHalfPreview.IconKeyFor`): `clipboard` (текст и всё неопознанное), `file`, `files`. Резолвятся через `IconPackService` как все остальные ключи; `file` / `files` нарисованы в `IslandIcons` в том же 24×24 outline-конструировании, ничего не вендорилось.
- Без платных/проприетарных пакетов; геометрии в стиле MIT Fluent / Tabler outline.

---

## 5. Функционал: есть / убрать / добавить

### Сейчас есть
- Idle clock + unread glow + optional minimal weather (+ WeatherSide L/R)
- **Split-буфер (1.12.2 → goo-шар 1.12.3):** копирование не забирает капсулу — она остаётся живой, а буфер уезжает в шар на верёвке; место под него по длинной оси резервируется как раньше; см. §3 правила 8–12
- Notification morph (H: width / V: height), badge
- Progress / Media / Timer / Error / **Weather** (только FSM — мок-источников нет)
- **Clicks only** (no swipe)
- Weather toggle (tray / ПКМ / Settings), Windows-primary source (§10)
- Unified outline icon pack + weather crossfade + tray icons
- **Tray** quick menu + unread icon/tooltip
- **Settings window** (отдельный Window, single-instance, **sidebar ListBox + Lucide icons**, 1.11.0): placement, z-order, opacity, sounds, orientation, Edge+Offset (no drag)
- Z-order Topmost / Desktop / BehindApps (Win32 SetWindowPos)
- Edge + OffsetX/Y (без mouse drag)
- Opacity 0.35–1.0 на fill; AnimationSpeed (Slow|Normal|Fast|Off) с pulse; SoundPack (Nothing|Ios|System|Off) + SoundEnabled + master/per-event volumes
- Click → Action Center. Демо-цикл удалён в 1.13.0: остаётся только реальный ввод.
- Unit-тесты FSM + AppSettings + IslandLayout + python FSM script

### Убрать / не раздувать (обоснование)
| Что | Почему | Референс |
|---|---|---|
| Автоцикл demo как «продуктовая фича» | только QA; **1.13.0 — удалён из продукта целиком**, не скрыт | Apple: LA только реальные события |
| Open-Meteo / любой third-party weather HTTP | пользовательский запрет; Windows-only путь | — |
| Pull-down мини-окно Xiaomi | другой UX, сложно на Win11 overlay | Xiaomi-only gesture |
| Detached second island | нет cutout-камеры на ПК | Apple minimal — hardware-specific |

### Оставить
| Что | Почему |
|---|---|
| Width-only morph + fixed H | DI «растягивание», без прыжка вверх |
| Unread dot + badge | Nothing Glyph minimalism + DI trailing badge |
| Action Center click | Windows-native аналог «открыть уведомления» |
| Media/Progress/Timer/Weather/Clipboard kinds в FSM | Live Activities / Super Island templates |
| Click → Action Center | Windows-native |
| **Clipboard history (text + file paths)** | локальный ring buffer, нет HTTP, нет «фейковой вставки»; пользователь сам жмёт Ctrl+V |

### Добавлено в этом workstream
Tray, Settings window, WeatherSide, Edge+Offset (no drag), Orientation, Z-order×3, Opacity, AnimationSpeed (+ pulse), color palette, Sound packs, icon pack stub, **Clipboard history 1.0** (text + file paths, 1s polling, local-only, no auto-paste).

### Clipboard history — правила
- Только локальный ring buffer (`ClipboardHistory`), максимум 100, default 25.
- Источник: `WindowsClipboardSource` — `GetClipboardSequenceNumber()` polling 1с, читает CF_HDROP → CF_UNICODETEXT.
- **Показ (1.12.2):** `OverlayCommand.SetClipboardSplit` — половинка, а не целый kind. `OverlayKind.Clipboard` (11-й) и `OverlayCommand.SetClipboard` (15-й) в Core остались, но приложение их не диспатчит (см. §3 правило 9).
- Split-time половины = `min(NotifyDurationMs, ClipboardHistory.MaxPillMs)`; `ClipboardHistory.MaxPillMs = 6000` — это **потолок**, а дефолтное время жизни = 4000 мс (`NotifyDurationMs`), как у уведомления. Счётчик свой (`SplitMsLeft`), общий `_notifyMs` ему не мешает и наоборот.
- Геометрия и анимация — §3 правила 8–11, §2 (строки `Split:`), §3b, §4b-1. Кратко: 170 + 200 = 370 по длинной оси, высота 30 всегда, выезд с дальнего конца на существующем морфе, fade с задержкой ¼, пик 1.06 по `ClickPop`, breathe ±0.5 DIP / 2.4 с по поперечной оси.
- Превью: `ClipboardHistory.BuildPayload` уже даёт текст и **правильные склонения**; `ClipboardHalfPreview` только нормализует и обрезает под 200 DIP — все пробелы/переводы строк схлопываются в один пробел, текст режется по 22 символа с многоточием, файл показывает имя, мультифайл — «5 файлов», пустой payload — «буфер обмена». Склонение **не дублируется** в UI.
- НЕ bump'ит unread (это не системное уведомление).
- Звук `Notify` только на Text/File, не на MultiFile (слишком часто при копировании в Проводнике).
- **Клик по половине** (1.12.2) → `ClipboardClickAction`: `Dismiss` — закрыть половину, пользователь жмёт Ctrl+V сам. `DismissAndClear` — закрыть и очистить системный буфер (реализовано как `WriteText("")`: открывает буфер, пишет пустую строку, ничего не вставляет). `PasteToLastFocus` — зарезервировано для v1.1. До 1.12.2 эта настройка читалась только окном настроек и в островке была мёртвой.
- Схлопывание анимируется: и клик, и истечение времени дают тот же collapse-морф, половина на время перехода остаётся видимой и уезжает той же дорогой, что приехала. Прятать её можно только после завершения перехода.
- v1 НЕ вставляет в чужое окно автоматически — это даёт focus-эффект, который мешает пользователю.

### Добавить позже
| Что | Обоснование |
|---|---|
| Реальный **SMTC** media | заменить мок `Night Drive` |
| Очередь уведомлений / счётчик &gt;1 | multi-island |
| Полный CsWinRT Geolocator + стабильный Bing cache parser | углубить §10 |
| Start with Windows | удобство |

---

## 6. Источники данных

**Медиа:** только SMTC (`WindowsMediaSessionSource`). Мок-трека больше нет — демо-режим удалён из продукта в 1.13.0.

**Погода:** `SetWeather` из `WindowsWeatherSource` (или stub `WeatherCodes.MockMoscow()` = ясно 18°, когда источник недоступен).

- Play/pause в UI только переключает флаг `Playing` в FSM (`OnMediaPlay`), звук не играет.

---

## 7. Трей и настройки

### Tray (реализовано)
- Primary: `WinFormsTray` (`System.Windows.Forms.NotifyIcon`) — надёжно видно в Win11 / Sandbox.
- Fallback: Avalonia `TrayService` (`TrayIcon`), если WinForms недоступен.
- Иконки: `Assets/tray.png` / `tray-unread.png` (outline IslandIcons).
- Левый клик → показать/скрыть островок.
- Правый клик → меню: «Открыть настройки», «Показать/скрыть островок», «Погода вкл/выкл», «Выход». Пункта «Демо» больше нет (удалён в 1.13.0).
- Двойной клик → центр уведомлений Windows (`ms-actioncenter:`).
- Unread > 0 → `tray-unread.png` (точка-индикатор) + tooltip с числом.
- TargetFramework: `net8.0-windows` + `UseWindowsForms` (см. `Directory.Build.props` / `EnableWindowsTargeting`).

### Окно настроек (отдельный Avalonia `Window`)
- Открытие: трей «Настройки…» **и** ПКМ по островку «Настройки…».
- **Не** popup / не flyout / не dump в ContextMenu.
- Single-instance: повторное открытие → `Activate()` существующего.
- Геометрия окна Settings persist: `SettingsWindowX/Y/Width/Height` (отдельно от OffsetX/Y островка); default ~**720×560** (1.11.0).
- UI: Avalonia **левый nav rail (ListBox) + правый content** (тёмная тема `#121214` / cards `#1C1C1E`), Lucide SVG icons; не TabControl. Низ окна — DockPanel: Отмена / Применить / OK.
- Разделы sidebar (RU):
  1. **Островок** — visibility, дата, FontAudio clock, seconds strip, hover/pin, fullscreen
  2. **Погода** — Windows-primary weather
  3. **Расположение** — Edge + Offset X/Y + Orientation (**без drag**)
  4. **Тема** — NothingDark / AppleQuiet / Ocean / Custom
  5. **Медиа и питание** — Now Playing (SMTC), battery alerts, timer/stopwatch
  6. **Оформление** — `ZOrderMode` + `Opacity` + **палитра** + FontSize/FontFamily + preview (Custom)
  7. **Анимации** — speed + appear/dismiss + pulse
  8. **Звуки** — packs + volumes
  9. **Иконки** — IslandIcons / Tabler / Lucide / Meteocons
  10. **Буфер обмена** — toggle, max items, click action (`ClipboardClickAction` с 1.12.2 читается островком: клик по буферной половине её исполняет)
  11. **Монитор** — toggle, refresh interval, раскрывать при наведении, virtual interfaces (настройка «Сворачивать через 30 с» удалена в 1.12.1); набор и порядок строк (1.12.1): пресет + редактор строк + статичное превью
- Ключи набора строк монитора (1.12.1): `AppSettings.StatsRowsPreset` (`Full` / `Brief` / `Custom`; `Off` в Core, но в UI не предлагается — off-switch это чекбокс «Показывать системный монитор») и `AppSettings.StatsRows` (упорядоченный список `StatsRow`). Пишутся читаемыми строками, не ординалами; оба приводятся к согласованному виду в `Normalize()` (ручное `Off` в `settings.json` мигрирует в `Full`). Состояние редактора строк — `NotifyIsland.Core/StatsRowEditState.cs`.
  12. **О приложении** — version, repo, import/export
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
- Указатель на островке: **clicks only** (1.8.1+): hover-peek / click-pin; double-click → Action Center; Esc unpin. Свайпов нет.

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

- [ ] Высота капсулы не изменилась (осталась 30). Исключение — `SystemStats` (высота от числа строк, `StatsHeightFor`, 1.12.1). Split-половина (1.12.2) исключением **не** является: она добавляет только длину (170 + 200 = 370 на Top/Bottom, 30 × 370 на Left/Right).
- [ ] Новая механика описана в §2 (тайминги) и покрыта тестом; граница половины считается **только** через `ClipboardSplit` — и разметка, и hit-test (1.12.2).
- [ ] Зоны клика ⅓/⅓/⅓ меряются по островной половине, а не по всей пилюле (1.12.2), и ось у них длинная.
- [ ] Morph Width base 420 мс Soft easing (× AnimationSpeed); Appear/Dismiss styles на Notification.
- [ ] Unread: Notify++, Clear=0, Collapse сохраняет; cycle seed не ++.
- [ ] Нет Open-Meteo / third-party weather HTTP.
- [x] Clicks only (`ClickMaxPx=12`); swipe UI removed (1.8.1). Idle breath removed (see CHANGELOG unreleased).
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
