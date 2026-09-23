# CONTEXT.md — пакет контекста для любой LLM / агента

Читать **первым** при работе с репозиторием. Цель: за минуту понять продукт, стек, запуск и правила.

## Что это
**NotifyIsland** — плавающая капсула (Dynamic Island–style) для **Windows 11** на **Avalonia / .NET 8**.  
Показывает часы, непрочитанные, уведомления, прогресс, медиа, таймер; клик открывает Action Center.

Репозиторий: https://github.com/Leorik69/notifyisland-avalonia  
Ветка разработки: `fix/win11-stability-build` · PR: https://github.com/Leorik69/notifyisland-avalonia/pull/8

## Единый источник правды
| Тема | Где |
|---|---|
| Анимации, состояния, функционал, трей, настройки, референсы | [`docs/ISLAND_GUIDELINES.md`](docs/ISLAND_GUIDELINES.md) |
| Числовые токены в коде | `NotifyIsland.Core/OverlayTokens.cs` |
| FSM | `NotifyIsland.Core/OverlayMachine.cs` |
| UI overlay | `OverlayWindow.axaml` + `.axaml.cs` |
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
OverlayWindow.axaml(.cs)        # капсула
Win32Overlay.cs                 # no-activate / topmost helpers
NotifyIsland.Core/              # OverlayMachine, OverlayTokens
NotifyIsland.Tests/             # unit tests FSM
docs/ISLAND_GUIDELINES.md       # правила островка
CONTEXT.md                      # этот файл
.github/workflows/portable.yml  # CI: test, fsm, build, publish
```

## Сборка / запуск / тесты
```bash
dotnet test NotifyIsland.sln -c Release
dotnet build NotifyIsland.Av.csproj -c Release
dotnet publish NotifyIsland.Av.csproj -c Release -r win-x64 --self-contained false -o ./publish
./publish/NotifyIsland.exe          # обычный запуск
./publish/NotifyIsland.exe --demo   # демо-цикл (мок-данные)
```
Горячие клавиши в overlay: **F9** demo on/off, **Esc** collapse.

## Функционал: есть / убрать / добавить
Кратко (детали и обоснования — в GUIDELINES §5):

**Есть:** Idle clock + unread glow; Notification morph; Progress/Media/Timer/Error FSM; demo mocks; Action Center click; tests+CI.

**Убрать/не раздувать:** demo как продукт; Xiaomi pull-down window; второй detached island.

**Добавить:** tray icon + unread; Settings window (позиция, drag, X/Y, orientation, 3 z-order режима); optional real SMTC media.

## Демо-медиа — откуда данные?
Захардкожено в `OverlayMachine.RunDemoStep()` (`"Night Drive"` / `"Local Radio"` / progress 0.33).  
Это **не** Windows SMTC. Реальный источник — отдельная будущая интеграция.

## Как подключать контекст в инструментах
1. **Cursor / Copilot / любой агент:** открыть репо → прочитать `CONTEXT.md`, затем `docs/ISLAND_GUIDELINES.md`, затем `OverlayTokens.cs`.
2. **Чат с LLM без репо:** вставить целиком `CONTEXT.md` + §2–5 из GUIDELINES.
3. **Новый агент на другом устройстве:** `git clone` → те же два файла первыми; не выдумывать тайминги.
4. **Правило:** перед UI-PR сверить чеклист GUIDELINES §9.

## Соглашения кода
- Русский UI-copy ок; код/идентификаторы — English.
- Лог: `%TEMP%\notifyisland.log` через `AppLog`.
- Не активировать окно (no-activate).
- Новые фичи: Core FSM + тесты → UI → GUIDELINES update в том же PR.

