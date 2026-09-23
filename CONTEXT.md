# CONTEXT.md — пакет контекста для любой LLM / агента

Читать **первым** при работе с репозиторием. Цель: за минуту понять продукт, стек, запуск и правила.

## Что это
**NotifyIsland** — плавающая капсула (Dynamic Island–style) для **Windows 11** на **Avalonia / .NET 8**.  
Показывает часы, непрочитанные, уведомления, прогресс, медиа, таймер, **погоду**; клик открывает Action Center; свайпы в стиле Xiaomi переключают виджеты.

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
| Outline icons | `IslandIcons.cs`, `Assets/Icons/README.md` |
| UI overlay | `OverlayWindow.axaml` + `.axaml.cs` |
| Settings JSON | `NotifyIsland.Core/AppSettings.cs` → `%LOCALAPPDATA%/NotifyIsland/settings.json` |
| Layout helpers | `NotifyIsland.Core/IslandLayout.cs` |
| Settings UI | `SettingsWindow.axaml(.cs)` — отдельный Window |
| Tray | `TrayService.cs` + `Assets/tray*.png` |
| Z-order | `Win32Overlay.ApplyZOrder` |
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
SettingsWindow.axaml(.cs)       # полное окно настроек
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
Горячие клавиши в overlay: **F9** demo on/off, **Esc** collapse.

## Функционал: есть / убрать / добавить
Кратко (детали — в GUIDELINES §5 / §10):

**Есть:** Idle clock + unread; weather (Windows-primary); morph FSM; **clicks only** (no swipe); idle breath; tray + Settings window; WeatherSide; Edge+Offset; Orientation H/V/Auto; Z-order×3; Opacity; Sounds; outline icons; battery pill; SMTC Now Playing; demo; tests+CI.

**Убрать/не раздувать:** demo как продукт; Open-Meteo; Xiaomi pull-down / swipe gestures; detached second island.

**Добавить позже:** timer polish / hover; deeper CsWinRT geolocation; file shelf / clipboard / launcher.

## Погода — откуда данные?
**Не Open-Meteo.** Конвейер Windows-only: WinRT geolocation (когда доступен) → Bing Weather / Widgets local cache → on-disk cache → `LocalStubWeather` (Sandbox). См. GUIDELINES §10.

## Батарея — откуда данные?
**Live:** `WindowsPowerSource` → WinForms `SystemInformation.PowerStatus` (2s poll). Charge connect / % bump → `SetBattery` pill; low battery → `Notify` once per cycle.
**Demo:** F10 charge pill, F11 low-battery, Settings «Демо зарядки».

## Медиа — откуда данные?
**Live:** `WindowsMediaSessionSource` → Windows SMTC (`GlobalSystemMediaTransportControlsSessionManager`). При активной сессии островок получает `SetMedia` (title/artist/progress/playing/artwork).  
**Demo:** F9 по-прежнему использует `"Night Drive"` / `"Local Radio"` в `OverlayMachine`. Если SMTC недоступен — demo/idle без принуждения Media.

## Ввод (клики)
Жесты свайпа **убраны** (1.8.1). `ClickMaxPx=12` — GUIDELINES §3b / OverlayTokens. `CycleNext`/`CyclePrev` остаются в FSM для тестов/API.

## Idle breath
`BreathScaleAmp=0.04`, `BreathWidthAmpPx=7`, `BreathGlowAmp=0.14`, `BreathPeriodMs=2600` — только Idle/Collapsed при `AnimBreathEnabled`.

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
