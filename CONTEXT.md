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
| Outline icons | `IslandIcons.cs`, `Assets/Icons/README.md` |
| UI overlay | `OverlayWindow.axaml` + `.axaml.cs` |
| Settings JSON | `AppSettings.cs` → `%LOCALAPPDATA%/NotifyIsland/settings.json` |
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
OverlayWindow.axaml(.cs)        # капсула + swipe + weather UI
WindowsWeatherSource.cs         # WinRT/Bing cache/stub — NO Open-Meteo
AppSettings.cs                  # settings.json
IslandIcons.cs                  # outline icon pack
Assets/Icons/README.md
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

**Есть:** Idle clock + unread glow; minimal+expanded weather (Windows-primary / stub); Notification morph; Progress/Media/Timer/Error/Weather FSM; Xiaomi-like swipe; ПКМ weather toggle; outline icons; demo mocks; Action Center click; tests+CI.

**Убрать/не раздувать:** demo как продукт; Open-Meteo/third-party weather HTTP; Xiaomi pull-down window; второй detached island.

**Добавить позже:** tray icon + unread; Settings window (позиция, drag, X/Y, orientation, 3 z-order); optional real SMTC; deeper CsWinRT geolocation.

## Погода — откуда данные?
**Не Open-Meteo.** Конвейер Windows-only: WinRT geolocation (когда доступен) → Bing Weather / Widgets local cache → on-disk cache → `LocalStubWeather` (Sandbox). См. GUIDELINES §10.

## Демо-медиа — откуда данные?
Захардкожено в `OverlayMachine.RunDemoStep()` (`"Night Drive"` / `"Local Radio"` / progress 0.33).  
Это **не** Windows SMTC. Реальный источник — отдельная будущая интеграция.

## Свайпы
`SwipeClickMaxPx=12`, `SwipeFirePx=48`, `SwipeRubberMs=180` — GUIDELINES §3b / OverlayTokens.

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
