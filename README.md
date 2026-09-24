# NotifyIsland (Avalonia)

**RU:** Прозрачная капсула top-center для Windows 11 (не WinUI). Overlay поверх рабочего стола — стиль Dynamic Island: фиксированная высота, морфинг только по ширине.

**EN:** Transparent top-center overlay capsule for Windows 11. Avalonia desktop app. Fixed-height island; width-only morph.

## Requirements

- .NET 8 SDK
- Windows 11 for the overlay UI (Win32 no-activate). Linux CI builds/tests Core only.

## Behavior

- **Collapsed:** clock (`HH:mm`) + glowing unread dot when `UnreadCount > 0`
- **Notification:** stretches wider (height fixed), shows icon + title/subtitle + unread badge, then collapses
- **Left-click** (idle/collapsed): opens Windows Notification Center (`ms-actioncenter:`)
- **Right-click:** context menu · **F9** demo · **Esc** collapse

## Run

```bash
dotnet run --project NotifyIsland.Av.csproj
```

Demo of all states:

```bash
dotnet run --project NotifyIsland.Av.csproj -- --demo
```

## Tests

```bash
dotnet test NotifyIsland.Tests/NotifyIsland.Tests.csproj -c Release
python3 tools/test_overlay_states.py
```

## Publish (portable win-x64)

```bash
dotnet publish NotifyIsland.Av.csproj -c Release -r win-x64 --self-contained true -o publish
```

Unpack the artifact folder and run `NotifyIsland.exe`. Default `dotnet build` has **no** RID / SelfContained so Linux can restore/build Core+Tests.

## Log

Overlay Win32 failures append to `%TEMP%\notifyisland.log` (e.g. `C:\Users\<you>\AppData\Local\Temp\notifyisland.log`).

## Solution

`NotifyIsland.sln` — Core (FSM/tokens), Av (Avalonia UI), Tests (xUnit, no UI).
