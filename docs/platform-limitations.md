# Windows platform limitations (NotifyIsland 1.3.0)

Product TFM: `net8.0-windows10.0.19041.0`, RID `win-x64` only. No macOS, Linux, D-Bus, Wayland, Flatpak, or notarization.

- **Toast listener:** `UserNotificationListener` returns **Denied** for unpackaged EXE. Sparse Appx identity can still fail on a VPS. The app never fakes `Allowed`.
- **Focus Assist / fullscreen:** `SHQueryUserNotificationState`. Quiet-hours registry CloudStore is not parsed.
- **Placement:** chosen monitor from `Screens.All` + that screen’s `Scaling` (per-monitor DPI). WorkerW wallpaper HWND is not used.
- **IPC:** named pipe `\\.\pipe\NotifyIsland`, JSON lines. Busy pipe fails open.
- **Contrast:** midnight palette is near-black `#080808` with `#FFFFFF` / `#C8C8CC` (WCAG AA for body). Custom palettes are user-owned.
- **Motion:** `--motion-debug` and settings Reduce motion skip HWND morph.
