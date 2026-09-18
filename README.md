# NotifyIsland (Avalonia)

Win11 top-center overlay. Fluent/Segoe, palettes, tray + settings. Not Apple Dynamic Island.

```
dotnet run --project NotifyIsland.Av.csproj
dotnet run --project NotifyIsland.Av.csproj -- --demo
dotnet run --project NotifyIsland.Av.csproj -- --settings
```

Tray: show/hide, settings, demo hub, exit. Click the capsule to open Notification Center (`ms-actioncenter:`); toggle off in settings. Shift+click expands. Optional autostart and Windows toast listener.

Weather: Open-Meteo (no key). Windows location if allowed, else city in settings. Idle pill: temp + condition icon. Width-only expand: city · condition · temp. Cache on failure; hide if nothing cached — never fake a live reading.

Icons: Fluent outline, Fluent filled, Segoe MDL2, Weather soft. Not SF Symbols.

Sounds: quiet notify / error / complete from Windows Media when present, else generated PCM. On/off + volume in JSON.

Portable: Actions artifact `NotifyIsland-portable-win-x64`.
