# NotifyIsland (Avalonia)

Win11 top-center overlay. Fluent/Segoe, palettes, tray + settings (RU/EN), weather, icon packs, sounds. Not Apple Dynamic Island.

## Download (win-x64)

- Installer: https://github.com/Leorik69/notifyisland-avalonia/releases/download/portable-win11-1.2.0/NotifyIsland-Setup-win-x64.exe
- Portable zip: https://github.com/Leorik69/notifyisland-avalonia/releases/download/portable-win11-1.2.0/NotifyIsland-portable-win-x64.zip
- Release page: https://github.com/Leorik69/notifyisland-avalonia/releases/tag/portable-win11-1.2.0

```
dotnet run --project NotifyIsland.Av.csproj
dotnet run --project NotifyIsland.Av.csproj -- --demo --motion-debug
pwsh setup/pack-release.ps1
```

Click cycle: Notification Center, then SMTC player + system volume. Collapse is slower than expand (fade then shrink). F9 demo plays toast motion even if Windows notification access is Denied.
