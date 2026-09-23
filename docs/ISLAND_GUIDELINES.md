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

Код: `OverlayTokens.MorphMs`, Avalonia `Transitions` на `Width` / `Pill.Width` / opacity точки / hover brushes.

| Переход | Длительность | Easing | Примечание |
|---|---:|---|---|
| Width morph (idle ↔ notify/media/…) | **280 мс** | `CubicEaseOut` | только ширина; высота **всегда** `CollapsedH` |
| Hover border/background | **160 мс** | `CubicEaseInOut` | без scale-прыжков |
| Unread-dot opacity | **280 мс** | `CubicEaseOut` | 0 ↔ 1 |
| Notify auto-dismiss | **4000 мс** | — | затем возврат в previous/idle |
| Demo scene cycle (после первого notify) | **1800 мс** | — | только `--demo` / F9 |
| Контент-апдейты внутри expanded | ≤ **450 мс** | soft-out | не дольше Apple max 2 с |

Запрещено:
- менять высоту капсулы при notify (не «расти вверх»);
- резкий snap без transition на Width;
- анимации > 450 мс на обычные UI-переходы (кроме morph 280 и notify hold 4 с).

---

## 3. Состояния и переходы

FSM: `OverlayMachine` / `OverlayKind`.

| Kind | UI | Ширина (токен) |
|---|---|---|
| `Idle` / `Collapsed` | часы + глиф ⏱ + unread-dot (если UnreadCount>0) | `CollapsedW` = 140 |
| `Notification` | иконка · title · body · badge | > CollapsedW, ≤ ExpandedMaxW |
| `Progress` | title · % · тонкий progress 2px | expanded |
| `Media` | ♪ · title/subtitle · play · progress | ~400 |
| `Timer` | таймер | expanded |
| `Error` | error colors | expanded |
| `Expanded` | обзор (demo weather и т.п.) | expanded |

Правила:
1. Высота всегда **`CollapsedH = 30`**.
2. CornerRadius = `Height / 2` (пиксель-капсула).
3. Notify инкрементит `UnreadCount`; `Clear` сбрасывает; `Collapse` **сохраняет** unread.
4. Клик по Idle/Collapsed → `ms-actioncenter:` (центр уведомлений Windows).
5. Right-click → контекстное меню (Demo / Свернуть / Выход). Полные настройки — **не** в этом меню (см. §7).

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

---

## 5. Функционал: есть / убрать / добавить

### Сейчас есть
- Idle clock + unread glow
- Notification (width morph, badge)
- Progress / Media / Timer / Error (через FSM + demo)
- Demo cycle (`--demo` / F9) с **захардкоженными** payload’ами
- Click → Action Center
- Unit-тесты FSM

### Убрать / не раздувать (обоснование)
| Что | Почему | Референс |
|---|---|---|
| Автоцикл demo как «продуктовая фича» | только QA; не держать в релизе по умолчанию | Apple: LA только реальные события |
| Отдельный «Expanded weather» как ядро | демо-заглушка, не Windows API | DI: weather — отдельное приложение, не система |
| Pull-down мини-окно Xiaomi | другой UX, сложно на Win11 overlay | Xiaomi-only gesture |
| Detached second island | нет cutout-камеры на ПК | Apple minimal — hardware-specific |

### Оставить
| Что | Почему |
|---|---|
| Width-only morph + fixed H | DI «растягивание», без прыжка вверх |
| Unread dot + badge | Nothing Glyph minimalism + DI trailing badge |
| Action Center click | Windows-native аналог «открыть уведомления» |
| Media/Progress/Timer kinds в FSM | Live Activities / Super Island templates |

### Добавить (приоритет)
| Что | Обоснование | Референс |
|---|---|---|
| **Tray icon** + quick menu + unread indicator | Win11-паттерн; DI не нужен, нам нужен entry-point | Windows UX |
| **Окно настроек** (не раздувать tray) | позиция, z-order, ориентация, weather toggle | Xiaomi: настройки островов отдельно |
| Позиция top/bottom/left/right + X/Y px + drag | ПК ≠ iPhone notch | — |
| Z-order: Topmost / Desktop / BehindApps | три явных режима | — |
| Реальный **SMTC** media (опционально) | заменить мок `Night Drive` | аналог DI Now Playing |
| Очередь уведомлений / счётчик >1 | DI minimal + Xiaomi multi-island | — |

---

## 6. Демо и источники данных

**Ответ на вопрос «откуда музыка в демо»:**  
Это **захардкоженный мок**, не SMTC и не аудиосессия Windows.

- Файл: `NotifyIsland.Core/OverlayMachine.cs` → `RunDemoStep()`
- Payload: `Title = "Night Drive"`, `Subtitle = "Local Radio"`, `Progress = 0.33`, `Playing = true`
- Триггер: `OverlayCommand.DemoNext` из `OverlayWindow.StartDemo` / таймер `_demo`
- Play/pause в UI только переключает флаг `Playing` в FSM (`OnMediaPlay`), звук не играет.

Подключить реальное медиа: отдельный сервис чтения Windows SMTC / GlobalSystemMediaTransportControlsSessionManager → `Dispatch(SetMedia, payload)`.

---

## 7. Трей и настройки (спека)

### Tray
- Иконка в notification area.
- Отражает unread (overlay icon / badge-dot).
- **Одиночный клик / ПКМ:** меню быстрых действий только:
  - Открыть центр уведомлений
  - Показать/скрыть островок
  - Погода вкл/выкл (если реализовано)
  - Настройки…
  - Выход
- **Двойной клик:** центр уведомлений.
- Не класть сюда координаты/z-order/ориентацию.

### Окно настроек
1. Расположение: сверху / снизу / слева / справа.
2. Ручной drag островка + поля X/Y (пиксели).
3. Ориентация: Auto | Horizontal | Vertical.
4. Z-order (три режима):
   - **Topmost** — поверх всех окон;
   - **Desktop** — над иконками рабочего стола, под окнами приложений;
   - **BehindApps** — под окнами приложений, над рабочим столом/виджетами.
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
- [ ] Unread: Notify++, Clear=0, Collapse сохраняет.
- [ ] Нет новых пунктов в tray-меню сверх §7 (остальное → Settings).
- [ ] Новые kinds описаны здесь и покрыты тестом в `NotifyIsland.Tests`.
- [ ] CONTEXT.md не дублирует числа — ссылается сюда.

