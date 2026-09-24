# CONTEXT.md — пакет контекста для любой LLM / агента

Читать **первым** при работе с репозиторием. Цель: за минуту понять продукт, стек, запуск и правила.

## Что это
**NotifyIsland** — плавающая капсула (Dynamic Island–style) для **Windows 11** на **Avalonia / .NET 8**.  
Показывает часы, непрочитанные, уведомления, прогресс, медиа, таймер, **погоду**; hover-peek + click-pin; двойной клик / меню — Action Center; полноэкранное скрытие; Settings **sidebar + Lucide icons** (1.11.0); **без свайпов**.

Репозиторий: https://github.com/Leorik69/notifyisland-avalonia  
Ветка разработки: `fix/win11-stability-build` · PR: https://github.com/Leorik69/notifyisland-avalonia/pull/8

## Единый источник правды
| Тема | Где |
|---|---|
| Анимации, состояния, свайпы, погода, иконки, трей, настройки, референсы | [`docs/ISLAND_GUIDELINES.md`](docs/ISLAND_GUIDELINES.md) |
| Превью фич простым языком (для утверждения) | [`docs/ISLAND_PREVIEW.md`](docs/ISLAND_PREVIEW.md) |
| Числовые токены в коде | `NotifyIsland.Core/OverlayTokens.cs` |
| FSM | `NotifyIsland.Core/OverlayMachine.cs` |
| Weather labels / WMO-like map | `NotifyIsland.Core/WeatherCodes.cs` |
| Windows weather source (no HTTP) | `WindowsWeatherSource.cs` |
| Now Playing (SMTC) | `WindowsMediaSessionSource.cs` |
| Battery / charging | `WindowsPowerSource.cs` + `NotifyIsland.Core/BatteryAlertLogic.cs` |
| Timer / stopwatch | `NotifyIsland.Core/IslandTimerLogic.cs` + `OverlayMachine` SetTimer/Tick |
| Digital clock (FontAudio) | `DigitalClockGlyphs` + `DigitalClockView` + `Assets/Icons/FontAudio/` |
| Seconds strip (digital-dot) | `SecondsStripLogic` + `SecondsStripView` |
| Outline icons | `IslandIcons.cs`, `Assets/Icons/README.md` |
| UI overlay | `OverlayWindow.axaml` + `.axaml.cs` |
| Settings JSON | `NotifyIsland.Core/AppSettings.cs` → `%LOCALAPPDATA%/NotifyIsland/settings.json` |
| Layout helpers | `NotifyIsland.Core/IslandLayout.cs` |
| Settings UI | `SettingsWindow.axaml(.cs)` — отдельный Window, **sidebar ListBox + Lucide/Tabler nav icons** (не TabControl; 1.11.0) |
| Tray | `TrayService.cs` + `Assets/tray*.png` |
| Z-order / fullscreen | `Win32Overlay.ApplyZOrder` / `IsFullscreenOrBusy` |
| Hover + pin | `NotifyIsland.Core/HoverPinMachine.cs` |
| Sounds | `IslandSounds.cs` + `Assets/Sounds/{nothing,ios,system}/` (packs; SystemSounds fallback) |
| Как собирать | этот файл + `README.md` |

**Не дублировать** тайминги и правила в README/комментах — править только GUIDELINES + OverlayTokens.

## Стек
- .NET 8, Avalonia UI
- Решение: `NotifyIsland.sln`
- Проекты: `NotifyIsland.Av.csproj` (приложение), `NotifyIsland.Core`, `NotifyIsland.Tests` (xUnit)
- Платформа: win-x64, Win11 (в т.ч. Windows Sandbox)

## Структура (корневая)
```
NotifyIsland.sln
NotifyIsland.Av.csproj          # entry Avalonia app (имя exe: NotifyIsland)
OverlayWindow.axaml(.cs)        # капсула + clicks + weather UI
WindowsWeatherSource.cs         # WinRT/Bing cache/stub — NO Open-Meteo
WindowsMediaSessionSource.cs    # SMTC Now Playing (WinRT)
WindowsPowerSource.cs           # Battery / AC (WinForms PowerStatus)
NotifyIsland.Core/AppSettings.cs / IslandLayout.cs
SettingsWindow.axaml(.cs)       # Settings sidebar (Lucide nav) + panels
TrayService.cs / IslandSounds.cs
IslandIcons.cs + Assets/tray*.png
Assets/Icons/README.md + Assets/Sounds/**
NotifyIsland.Core/              # OverlayMachine, OverlayTokens, WeatherCodes
NotifyIsland.Tests/             # unit tests FSM
docs/ISLAND_GUIDELINES.md       # правила островка
CONTEXT.md                      # этот файл
tools/test_overlay_states.py    # FSM smoke (python)
.github/workflows/portable.yml  # CI: test, fsm, build, publish
```

## Сборка / запуск / тесты
```bash
dotnet test NotifyIsland.sln -c Release
python3 tools/test_overlay_states.py
dotnet build NotifyIsland.Av.csproj -c Release
dotnet publish NotifyIsland.Av.csproj -c Release -r win-x64 --self-contained false -o ./publish
./publish/NotifyIsland.exe          # обычный запуск
./publish/NotifyIsland.exe --demo   # демо-цикл (мок-данные + weather step)
```
Горячие клавиши в overlay: **F9** demo on/off, **F10** charge pill, **F11** low-battery, **F12** timer/stopwatch, **Esc** unpin (и collapse). Версия продукта: **1.11.0**.

## Функционал: есть / убрать / добавить
Кратко (детали — в GUIDELINES §5 / §10):

**Есть:** Idle clock + unread; weather (Windows-primary); morph FSM; **clicks only** (no swipe); **hover expand + click pin 1.10.0**; **hide on fullscreen 1.10.0**; **Settings icon-sidebar 1.11.0**; tray + Settings window; WeatherSide; Edge+Offset (**no island drag**); Orientation H/V/Auto; Z-order×3; Opacity; Sounds; outline icons; battery pill; SMTC Now Playing; timer/stopwatch; FontAudio digital clock + seconds strip; demo; tests+CI.

**Убрать/не раздувать:** demo как продукт; Open-Meteo; Xiaomi pull-down / swipe gestures; detached second island.

**Добавить позже (backlog):** deeper CsWinRT geolocation; **file shelf / clipboard / launcher** (не в scope сейчас).

## Погода — откуда данные?
**Не Open-Meteo.** Конвейер Windows-only: WinRT geolocation (когда доступен) → Bing Weather / Widgets local cache → on-disk cache → `LocalStubWeather` (Sandbox). См. GUIDELINES §10.

## Батарея — откуда данные?
**Live:** `WindowsPowerSource` → WinForms `SystemInformation.PowerStatus` (2s poll). Charge connect / % bump → `SetBattery` pill; low battery → `Notify` once per cycle.
**Demo:** F10 charge pill, F11 low-battery, Settings «Демо зарядки».

## Медиа — откуда данные?
**Live:** `WindowsMediaSessionSource` → Windows SMTC (`GlobalSystemMediaTransportControlsSessionManager`). При активной сессии островок получает `SetMedia` (title/artist/progress/playing/artwork).  
**Demo:** F9 по-прежнему использует `"Night Drive"` / `"Local Radio"` в `OverlayMachine`. Если SMTC недоступен — demo/idle без принуждения Media.

## Таймер
**Live:** tray «Таймер» presets / Settings → Таймер / F12 → `SetTimer`. `Tick` уменьшает `RemainingSeconds` пока `Playing`; на 0 → Notify «Таймер» → Idle.  
**Приоритет:** идущий таймер удерживает островок над SMTC до отмены/завершения (клик по Media снимает приоритет).  
**Секундомер:** `TimerStopwatchMode` + `CountUp` (счёт вверх).

## Ввод (клики)
Жесты свайпа **убраны** (1.8.1). `ClickMaxPx=12` — GUIDELINES §3b / OverlayTokens. `CycleNext`/`CyclePrev` остаются в FSM для тестов/API.
- Idle/Collapsed: **одинарный клик** = pin/unpin; **двойной клик** = Action Center; Esc = unpin.
- Hover (~250 ms) → peek (секунды + ширина); leave → grace ~500 мс.

## Fullscreen
`HideOnFullscreen` (default ON): `SHQueryUserNotificationState` + monitor-cover → Hide; restore on leave. Optional click-through if hide off.

## Иконки — источники
Каталог паков: [allsvgicons.com/pack](https://allsvgicons.com/pack/). Вендор + лицензии: `Assets/Icons/NOTICE`, `docs/ICON_PACKS.md`.

| Пакет | Лицензия | Путь | Назначение |
|---|---|---|---|
| Lucide | ISC | `Assets/Icons/Lucide/` | Island outline + **Settings nav** (1.11.0) |
| Tabler | MIT | `Assets/Icons/Tabler/` | Island outline |
| Meteocons ×4 | MIT (Bas Milius) | `Assets/Icons/Meteocons*/` | Weather SVG |
| FontAudio (fad) | CC BY 4.0 @fefanto | `Assets/Icons/FontAudio/` | Digital clock + digital-dot strip |
| IslandIcons | built-in | `IslandIcons.cs` | Fallback geometries |

## Settings IA (1.11.0)
Левый **nav rail** (ListBox) + правый scroll; не TabControl. Секции:

1. **Островок** — visibility, дата, FontAudio clock, seconds strip, hover/pin, fullscreen  
2. **Погода** — Windows-primary (не Open-Meteo)  
3. **Расположение** — Edge + Offset X/Y + Orientation (**drag островка нет**)  
4. **Тема** — NothingDark / AppleQuiet / Ocean / Custom  
5. **Медиа и питание** — SMTC Now Playing, battery alerts, timer/stopwatch  
6. **Оформление** — z-order, opacity, fonts, palette, preview pill  
7. **Анимации** — speed + appear/dismiss + pulse  
8. **Звуки** — packs + volumes  
9. **Иконки** — IslandIcons / Tabler / Lucide / Meteocons  

Открыть: трей → «Настройки…» / ПКМ остров → Настройки. Проверить иконки nav: слева 9 пунктов с Lucide SVG, акцент при выборе.

## Sandbox deploy (standing rule)
Всегда **свежий** publish в Windows Sandbox: `Desktop\notifyisland-fresh-<sha>` (короткий SHA). Перед запуском — **убить** старый `NotifyIsland.exe`. Не переиспользовать предыдущую папку publish.

## Исследование / конкуренты
Краткий legal inspiration: [`docs/NOTHING_INSPIRATION.md`](docs/NOTHING_INSPIRATION.md). Внешний research brief по чужим island-стекам — **не в репо** (если появится файл — добавить путь сюда). Не копировать Apple Live Activities 1:1; Nothing — только легальная inspiration (OFL fonts / synthesized sounds).

## Как подключать контекст в инструментах
1. **Cursor / Copilot / любой агент:** открыть репо → прочитать `CONTEXT.md`, затем `docs/ISLAND_GUIDELINES.md`, затем `OverlayTokens.cs`.
2. **Чат с LLM без репо:** вставить целиком `CONTEXT.md` + §2–5 и §10 из GUIDELINES.
3. **Новый агент на другом устройстве:** `git clone` → те же два файла первыми; не выдумывать тайминги; не добавлять Open-Meteo.
4. **Правило:** перед UI-PR сверить чеклист GUIDELINES §9.

## Соглашения кода
- Русский UI-copy ок; код/идентификаторы — English.
- Лог: `%TEMP%\notifyisland.log` через `AppLog`.
- Не активировать окно (no-activate).
- Новые фичи: Core FSM + тесты → UI → GUIDELINES update в том же PR.
