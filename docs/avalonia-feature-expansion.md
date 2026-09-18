# NotifyIsland feature expansion (Avalonia)

Fluent Win11 overlay — not Apple Dynamic Island chrome.

## Capsule (default)

**One short height for every state.** Idle / notify / progress / media / timer all use **Capsule height** (default 40px). Expand is **width only**; extra text truncates on one row. Radius = height/2.

Settings:
- Capsule height (px)
- **Expand height** checkbox — **off by default**. When on, notify/progress may grow taller.
- Min / max width for expanded states

## Placement / layer / click

Left-center-right + offsets; top-center-bottom + offsets. Always-on-top | normal | desktop (`HWND_BOTTOM`). Click opens Notification Center (`ms-actioncenter:`); toggleable. Shift/Alt/Ctrl+click still expands. Media play control is not stolen.

## Weather

- **API:** Open-Meteo forecast + geocoding, no API key.
- **Place:** Windows location when allowed; otherwise **City fallback** in settings.
- **Idle:** compact chip (condition glyph + temperature) on the right of the clock. Clock stays centered.
- **Expanded (width-only one row):** city · condition · temp.
- **Refresh:** 5–60 minutes (default 15).
- **Offline:** last `notifyisland.weather.json` cache, or hide weather. Never fake a live reading.

## Icon packs

Settings dropdown (`iconStyle`): `fluent` (outline), `fluent-fill`, `mdl2` (Segoe MDL2 Assets), `weather-soft`. Not Apple SF Symbols.

## Sounds

`soundsEnabled` + `soundVolume` (quiet; playback capped). Cues: notify, error, complete. Windows Media WAVs when present, else generated PCM.

## Persist

`notifyisland.settings.json` next to the exe or `%LOCALAPPDATA%\NotifyIsland\settings.json`.

## Motion

Morph 180/260ms on width (and height only if Expand height is on).
