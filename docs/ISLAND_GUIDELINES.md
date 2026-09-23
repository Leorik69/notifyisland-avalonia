# NotifyIsland — правила островка (единый источник правды)

Документ обязателен при любой доработке UI/анимаций/функционала.
Конкретные числа важнее общих формулировок. При конфликте с README — этот файл побеждает.

Связанные файлы: [`CONTEXT.md`](../CONTEXT.md) (как запускать проект), [`OverlayTokens.cs`](../NotifyIsland.Core/OverlayTokens.cs) (числовые токены в коде).

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
| Жесты | tap, long-press | tap, swipe, **pull-down → мини-окно**, drag-to-share |
| Автосворот expanded | системный | по умолчанию **~5 с** (`islandFirstFloat` / `enableFloat`) |
| Архитектура | ActivityKit | поверх Focus Notification + payload `param_v2.*` |

### 1.3 Samsung / Nothing / прочее
- **Samsung One UI**: нет полноценного DI; близки edge-панели, Now Bar / Live Notifications (зависят от версии) — брать идею persistent status, не копировать форму.
- **Nothing OS**: Glyph / точечные индикаторы — референс для **минимального unread-dot**, не для широкой капсулы.
- Open-source: https://github.com/d4viddf/hyperisland-toolkit (DSL под Xiaomi payloads) — полезен как каталог шаблонов (media/timer/taxi), не как UI для Win11.

---

## 2. Правила анимаций NotifyIsland (actionable)

Код: `OverlayTokens.MorphMs`, Avalonia `Transitions` на `Width` / `Pill.Width` / opacity точки / hover brushes / swipe rubber-band / icon crossfade.

| Переход | Длительность | Easing | Примечание |
|---|---:|---|---|
| Width morph (idle ↔ notify/media/weather/…) | **280 мс** | `CubicEaseOut` | только ширина; высота **всегда** `CollapsedH` |
| Hover border/background | **160 мс** | `CubicEaseInOut` | без scale-прыжков |
| Unread-dot opacity | **280 мс** | `CubicEaseOut` | 0 ↔ 1 |
| Notify auto-dismiss | **4000 мс** | — | затем возврат в previous/idle |
| Demo scene cycle (после первого notify) | **1800 мс** | — | только `--demo` / F9 |
| Контент-апдейты внутри expanded | ≤ **450 мс** | soft-out | не дольше Apple max 2 с |
| Swipe rubber-band snap-back | **180 мс** | `CubicEaseOut` | если жест &lt; fire threshold |
| Weather / icon glyph crossfade | **240 мс** | `CubicEaseOut` | `IconCrossfadeMs` |

Запрещено:
- менять высоту капсулы при notify (не «расти вверх»);
- резкий snap без transition на Width;
- анимации > 450 мс на обычные UI-переходы (кроме morph 280 и notify hold 4 с);
- сторонние HTTP weather API (Open-Meteo / MSN / etc.) — см. §10.

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
1. Высота всегда **`CollapsedH = 30`**.
2. CornerRadius = `Height / 2` (пиксель-капсула).
3. Notify инкрементит `UnreadCount`; `Clear` сбрасывает; `Collapse` **сохраняет** unread.
4. Клик (движение ≤ **12 px**) по Idle/Collapsed → `ms-actioncenter:`.
5. Right-click → контекстное меню (Demo / **Погода вкл↔выкл** / Свернуть / Выход). Полные настройки — **не** в этом меню (см. §7).
6. Swipe cycle **не** инкрементит unread (Notification slot = demo seed).

---

## 3b. Свайп-жесты (Xiaomi-like)

Пороги (`OverlayTokens`):

| Константа | Значение | Смысл |
|---|---:|---|
| `SwipeClickMaxPx` | **12** | ≤12 px → click (Action Center), не свайп |
| `SwipeFirePx` | **48** | ≥48 px → жест срабатывает |
| `SwipeRubberMs` | **180** | rubber-band назад, если 12 &lt; dist &lt; 48 |

Направления (доминирующая ось):
- **Left / Right** → `CycleNext` / `CyclePrev` среди `IslandSlot`: Idle ↔ Notification ↔ Weather (если enabled) ↔ Media.
- **Down** → `Collapse` (minimal / Idle-Collapsed).
- **Up** → `ExpandWidget` (из Idle: Weather если enabled, иначе Notification seed).

Во время drag: `TranslateTransform` с damping; при release под fire — snap-back 180 мс.

---

## 4. Индикаторы, цвета, типографика

| Токен | Значение |
|---|---|
| Fill | `#080808` |
| Text | `#FFFFFF` |
| Text secondary | `#C8C8CC` |
| Accent / badge / unread | `#3D9CF0` (dot glow ярче: `#5CB6FF`) |
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
- Idle clock + unread glow + optional minimal weather
- Notification (width morph, badge)
- Progress / Media / Timer / Error / **Weather** (FSM + demo)
- Xiaomi-like swipe (L/R cycle, up expand, down collapse)
- Weather toggle (ПКМ «Погода вкл/выкл», persist `%LOCALAPPDATA%/NotifyIsland/settings.json`)
- Windows-primary weather source + local stub fallback (см. §10)
- Unified outline icon pack + weather crossfade
- Demo cycle (`--demo` / F9) включает `SetWeather`
- Click → Action Center (short press)
- Unit-тесты FSM + python FSM script

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
| Swipe cycle | Xiaomi multi-island |

### Добавить (приоритет, ещё не в этом workstream)
| Что | Обоснование | Референс |
|---|---|---|
| **Tray icon** + quick menu + unread indicator | Win11-паттерн; DI не нужен, нам нужен entry-point | Windows UX |
| **Окно настроек** (не раздувать tray) | позиция, z-order, ориентация, weather toggle UI | Xiaomi: настройки островов отдельно |
| Позиция top/bottom/left/right + X/Y px + drag | ПК ≠ iPhone notch | — |
| Z-order: Topmost / Desktop / BehindApps | три явных режима | — |
| Реальный **SMTC** media (опционально) | заменить мок `Night Drive` | аналог DI Now Playing |
| Очередь уведомлений / счётчик >1 | DI minimal + Xiaomi multi-island | — |
| Полный CsWinRT Geolocator + стабильный Bing cache parser | углубить §10 primary path | Win11 |

---

## 6. Демо и источники данных

**Медиа в демо:** захардкоженный мок (`Night Drive` / `Local Radio`), не SMTC.

**Погода в демо:** `SetWeather` в `RunDemoStep()`; данные из `WindowsWeatherSource` (или stub `WeatherCodes.MockMoscow()` = ясно 18°).

- Play/pause в UI только переключает флаг `Playing` в FSM (`OnMediaPlay`), звук не играет.

---

## 7. Трей и настройки (спека)

### Tray (ещё не реализован — спека)
- Иконка в notification area (тот же outline pack).
- Отражает unread (overlay icon / badge-dot).
- **Одиночный клик / ПКМ:** меню быстрых действий только:
  - Открыть центр уведомлений
  - Показать/скрыть островок
  - Погода вкл/выкл
  - Настройки…
  - Выход
- **Двойной клик:** центр уведомлений.

### Сейчас (без tray)
- ПКМ по островку: Demo / Погода вкл↔выкл / Свернуть / Выход.
- Persist: `%LOCALAPPDATA%/NotifyIsland/settings.json` (`WeatherEnabled`, lat/lon defaults Moscow 55.75,37.62).

### Окно настроек (ещё не реализовано — спека)
1. Расположение: сверху / снизу / слева / справа.
2. Ручной drag островка + поля X/Y (пиксели).
3. Ориентация: Auto | Horizontal | Vertical.
4. Z-order (три режима): Topmost / Desktop / BehindApps.
5. Тогглы: weather, demo, start with Windows (позже).

---

## 8. Доступность и Win11

- `AutomationProperties.Name` на интерактивных контролах.
- Контраст текста к `#080808` ≥ обычного UI (белый/серый).
- Не перехватывать фокус (`ShowActivated=False`, Win32 no-activate).
- Лог: `%TEMP%\notifyisland.log`.

---

## 9. Чеклист перед PR

- [ ] Высота капсулы не изменилась (осталась 30).
- [ ] Morph только Width, 280 мс CubicEaseOut.
- [ ] Unread: Notify++, Clear=0, Collapse сохраняет; cycle seed не ++.
- [ ] Нет Open-Meteo / third-party weather HTTP.
- [ ] Swipe thresholds 12 / 48 / 180 задокументированы и в токенах.
- [ ] Новые kinds описаны здесь и покрыты тестом в `NotifyIsland.Tests`.
- [ ] CONTEXT.md не дублирует числа — ссылается сюда.

---

## 10. Погода — Windows-only (без Open-Meteo)

**Почему не виджет Windows Weather API напрямую:** у сторонних приложений **нет** стабильного публичного API виджета «Погода» Win11. Поэтому primary path — Windows-поверхности с честным fallback.

Конвейер `WindowsWeatherSource` / `IWeatherSource` (без сети к open-meteo/msn/…):

1. **Primary (Win11):** WinRT `Windows.Devices.Geolocation` (тип через reflection в portable-сборке; полный CsWinRT — follow-up) + best-effort чтение локального кэша Bing Weather / Widgets (`TryReadBingWeatherCache` под `%LOCALAPPDATA%\Packages\Microsoft.BingWeather*` / WebExperience / WidgetsRuntime).
2. **On-disk cache:** `%LOCALAPPDATA%/NotifyIsland/weather-cache.json` после успешного Windows-чтения.
3. **Sandbox / non-Windows / нет кэша:** `LocalStubWeather` → `WeatherCodes.MockMoscow()` (ясно · 18° · 0%). **Никакого HTTP.**

UI:
- **Minimal** (Idle/Collapsed + `WeatherEnabled`): outline weather icon + `18°` рядом с часами; ширина `CollapsedWeatherW`.
- **Expanded** (`OverlayKind.Weather`): icon + `Ясно · 18° · 0%` (precip если есть).
- Refresh: startup + каждые **15 мин** (`WeatherRefreshMs`).
- Payload: `TemperatureC`, `WeatherCode`, `PrecipProb` (+ Title/Subtitle/Body согласованы через `WeatherCodes.ToPayload`).

**Open-Meteo НЕ используется и не должен добавляться.**
