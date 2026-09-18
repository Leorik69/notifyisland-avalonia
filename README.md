# NotifyIsland (Avalonia)

Win11 top-center overlay. Fluent/Segoe, palettes, tray + settings. Not Apple Dynamic Island.

```
dotnet run --project NotifyIsland.Av.csproj
dotnet run --project NotifyIsland.Av.csproj -- --demo
```

Tray: show/hide, settings, demo, exit. Settings persist to `notifyisland.settings.json` beside the exe (or `%LOCALAPPDATA%\NotifyIsland\`). F9 still toggles demo.

Portable: Actions artifact `NotifyIsland-portable-win-x64`.
