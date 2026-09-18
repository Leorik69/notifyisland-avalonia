# NotifyIsland feature expansion (Avalonia)

Fluent Win11 overlay — not Apple Dynamic Island chrome.

## Capsule (default)

**One short height for every state.** Idle / notify / progress / media / timer all use **Capsule height** (default 40px). Expand is **width only**; extra text truncates on one row. Radius = height/2.

## Placement / click cycle

Click cycle (setting): Notification Center → SMTC player + **system** master volume → back. Shift/Alt/Ctrl+click still expands. Order can be reversed or disabled.

## Motion (1.2.0)

Separate persistable **expand** and **collapse** durations (presets Fast/Normal/Slow + ms). Expand: Point-to-Point, HWND grows first. Collapse: 83 ms fade of the row, then Soft-Out shrink toward the badge slot; HWND shrinks only after the inner pill finishes. `--motion-debug` logs kind, dt, dropped frames, hwndStart/hwndEnd.

### Toast / kind motion — **implemented** (F9 demo even if listener is Denied)

- Slide-in from badge side (composition Offset + fade) on notify/stack/error.
- Chat: PopChat scale 0.86→1 on the kind icon.
- Call: Breathe on glow only.
- Download/progress: FastInvoke on the bar (Value still Point-to-Point 250).
- Warn/error: WarnFlash on row text.
- Badge count tick: 83 ms opacity tick when the count changes.
- Dismiss-to-badge: collapse transform origin at the left badge slot.

## Weather / icons / badge / kinds

Open-Meteo. Icon packs Fluent (+ volume/skip/music/person/image/video/folder/link/star/shield). Badge left of clock. F9 kinds include chat/mail/calendar/call/download/…

## Settings

Russian default (`loc/ru.json`), English table (`loc/en.json`), system culture option. Each control has a title + helper. Text size and icon size S/M/L separate.

## Sounds vs system volume

App cue volume stays separate. System master volume is Core Audio `IAudioEndpointVolume` in the player panel and in settings.

## Toasts

`RequestAccessAsync` status is shown verbatim. Never fake Allowed. Sparse package register button + ms-settings:privacy-notifications.

## Persist

`notifyisland.settings.json`. Version 1.2.0.
