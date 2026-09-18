# NotifyIsland (Avalonia)

Win11 top-center overlay. Fluent/Segoe, palettes, tray + settings. Not Apple Dynamic Island.

```
dotnet run --project NotifyIsland.Av.csproj
dotnet run --project NotifyIsland.Av.csproj -- --demo
dotnet run --project NotifyIsland.Av.csproj -- --settings
```

Tray: show/hide, settings, demo hub, exit. Click the capsule to open Notification Center (`ms-actioncenter:`); toggle off in settings. Shift+click expands. Optional autostart and Windows toast listener.

Portable: Actions artifact `NotifyIsland-portable-win-x64`.
