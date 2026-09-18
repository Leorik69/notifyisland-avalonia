# Architecture roadmap (Windows only)

Source of the review: Core / Presentation / Platform / Integration split, notification API + IPC, checklists, P0–P3. Product 1.2.0 on `brobot-player-toast-motion-628c`. P0 on `brobot-arch-core-ipc-628c`. **Target: Windows 11 only** (`net8.0-windows10.0.19041.0`). No Windhawk. No merge to main.

**Scope decision:** this product is **Windows only**. Do not plan or stub macOS, Linux, signing/notarization, D-Bus, Wayland, Flatpak, or AppImage. Thin `IIpcEndpoint` / `IScreenPlacement` / `IWindowChrome` exist so Win32 IPC, placement, and chrome stay injectable and testable — not as a second OS host.

## Review item → status

| Review item | Status | Reality (files) |
|---|---|---|
| Tray icon + menu | **Done** | `App.axaml`, `App.axaml.cs`, `IslandHost.cs` |
| Autostart | **Done** | `AutoStart.cs`, settings checkbox |
| Morph animation (Fluent tokens, HWND morph, collapse fade+Soft-Out) | **Done** (1.1–1.2) | `Motion.cs`, `IslandAnimator.cs` `HwndMorph`, `OverlayWindow.axaml.cs` ApplySize |
| Settings window | **Done** | `SettingsWindow.axaml` / `.cs` |
| RU localization + EN table | **Partial** | `loc/ru.json`, `loc/en.json`, `Ui.cs` — titles/hints on appearance/motion; not every expander string wired yet |
| Weather Open-Meteo | **Done** | `WeatherHub.cs` |
| App badge | **Done** | `AppBadgeHub.cs`, idle row in `OverlayWindow.axaml` |
| Action Center click | **Done** | `ActionCenter.cs`; 1.2 click **cycle** Center → player/volume (`ClickMode`) |
| Installer + portable zip | **Done** | `setup/notifyisland.iss`, `setup/pack-release.ps1` |
| SMTC player + system volume | **Done** (1.2) | `SmtcHub.cs`, `SystemVolume.cs` |
| Toast listener (honest Denied) | **Partial** | `ToastHub.cs` `RequestAccessAsync`; sparse register `ToastIdentity.cs` — still unpackaged-Denied on VPS |
| Core / Presentation / Platform / Integration folders | **Partial (P0)** | New `Core/`, `Platform/` (thin Win32 seams), `Integration/Windows/`, `Demo/`; overlay UI still in WinExe root |
| OverlayMachine prefs-free | **Done (P0)** | `OverlayMachine.cs` Notify uses `DurationMs` / `NotifyDurationMs`; no `PrefsStore` |
| `NotificationRequest` / `NotificationId` / `NotificationAction` | **Done (P0)** | `Core/NotificationModel.cs` |
| Notification dispatcher | **Done (P0)** | `Core/OverlayDispatcher.cs`; toasts/IPC go through it |
| Queue replace-by-id + dismiss | **Done (P0)** | `Core/NotificationQueue.cs` |
| IPC named pipe (Windows) behind interface | **Done (P0)** | `IIpcEndpoint` + `NamedPipeIpc.cs` `\\.\pipe\NotifyIsland` |
| Demo separated from production | **Done (P0)** | `Demo/DemoScript.cs`; `DemoNext` removed from machine |
| `Screens.Primary` only | **Gap P1** | `OverlayPlacement.cs` Primary then `ScreenFromWindow` |
| DPI per-screen | **Partial** | Uses `window.RenderScaling` for the host window |
| Fragile `GetWindowLong` | **Partial** | `Win32Overlay.cs` uses `GetWindowLongPtrW`; `IWindowChrome` wraps ApplyNoActivate |
| Empty `catch { }` | **Gap P1** | `IslandLog` stub exists; many Win32 catches still silent |
| Timer completed-state | **Gap P1** | Timer ticks to 0; sound only `MaybeCompleteSound` |
| Fullscreen / Focus Assist | **Missing P1** | No query of quiet hours / fullscreen exclusive |
| Logging | **Partial (P0 stub)** | `%TEMP%\notifyisland.log` via `IslandLog`; morph file still separate |
| Screenshot tests | **Missing P2** | Python FSM + token grep (`tools/test_*.py`) |

Dropped from the review (will not be built): macOS/Linux backends, notarization, D-Bus, Wayland, Flatpak/AppImage.

## P0 execution plan (Windows)

1. Core types + queue — **landed**
2. Dispatcher — **landed**
3. Demo extraction — **landed**
4. Thin Win32 seams (`IIpcEndpoint`, placement, chrome) — **landed** (not multi-OS scaffolding)
5. Named pipe IPC — **landed** (fail-open)
6. IslandLog stub — **landed**
7. Keep 1.2.0 overlay behavior — **goal of this PR** (no morph rewrite)

**Not P0:** per-monitor DPI picker, Focus Assist, screenshot golden frames, emptying every catch.

## After P0

- **P1:** timer completed state, Focus Assist + fullscreen pause, screen picker, log empty catches, finish RU labels.
- **P2:** screenshot / frame tests, bounded badge cache.
- **P3:** Windows polish only (more toast identity, installer UX) — no extra OS.
