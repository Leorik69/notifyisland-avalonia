# NotifyIsland (Avalonia)

Win11 top-center overlay. Fluent/Segoe, palettes, tray + settings, weather, icon packs, sounds. Not Apple Dynamic Island.

## Download (win-x64)

- Installer: https://github.com/Leorik69/notifyisland-avalonia/releases/download/portable-win11-1.0.0/NotifyIsland-Setup-win-x64.exe
- Portable zip: https://github.com/Leorik69/notifyisland-avalonia/releases/download/portable-win11-1.0.0/NotifyIsland-portable-win-x64.zip
- Release page: https://github.com/Leorik69/notifyisland-avalonia/releases/tag/portable-win11-1.0.0

```
dotnet run --project NotifyIsland.Av.csproj
dotnet run --project NotifyIsland.Av.csproj -- --demo
pwsh setup/pack-release.ps1
```

Tray: show/hide, settings, demo hub, exit. Click the capsule to open Notification Center (`ms-actioncenter:`); toggle off in settings. Shift+click expands. Optional autostart and Windows toast listener.

Weather: Open-Meteo (no key). Windows location if allowed, else city in settings. Idle pill: temp + condition icon.

Icons: Fluent outline, Fluent filled, Segoe MDL2, Weather soft.

Sounds: quiet notify / error / complete; on/off + volume.
