# NotifyIsland (Avalonia)

Win11 top-center overlay. Fluent/Segoe, palettes, tray + settings. Not Apple Dynamic Island.

```
dotnet run --project NotifyIsland.Av.csproj
dotnet run --project NotifyIsland.Av.csproj -- --demo
dotnet run --project NotifyIsland.Av.csproj -- --settings
```

Tray: show/hide, settings, demo hub, exit. Optional autostart and Windows toast listener (Denied/Unavailable is shown honestly). Settings: `notifyisland.settings.json` beside the exe or `%LOCALAPPDATA%\NotifyIsland\`. F9 demo.

Portable: Actions artifact `NotifyIsland-portable-win-x64`.
