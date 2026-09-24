# NotifyIsland sound packs

**Original** short UI tones synthesized for NotifyIsland. They are **inspired by** the *feel* of soft Glyph-like clicks (Nothing) and soft rounded taps (iOS-style), but are **not** official Nothing OS or Apple iOS system sounds and do **not** contain any proprietary audio assets.

## Packs

| Folder | Role |
|--------|------|
| `nothing/` | Soft bright clicky cues (Glyph-like *inspiration*) |
| `ios/` | Soft warmer rounded taps (iOS-like *inspiration*) |
| `system/` | Optional simple beeps; runtime **System** pack prefers `SystemSounds` / `MessageBeep` and only falls back to these files if needed |

## Files (each pack)

`notify.wav`, `expand.wav`, `collapse.wav`, `swipe.wav`, `error.wav`, `hover.wav`

- Duration: &lt; 300 ms, quiet peak (~−10 dBFS after normalize)
- Format: mono PCM WAV 44.1 kHz 16-bit
- Generated with an in-repo Python sine+envelope synthesizer (no third-party sample libraries)

## License / legal

Original work for NotifyIsland. Do **not** replace these with ripped Nothing Glyph / Apple UIKit system sound files.

## Switching packs

Settings → **Звуки** → pack combo (`Nothing` / `iOS` / `System` / `Off`), master volume, and per-event volume sliders. Persisted in `%LOCALAPPDATA%/NotifyIsland/settings.json` as `soundPack`, `soundVolume`, `soundVol*`.
