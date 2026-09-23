# Changelog

## 1.5.0 — sound packs

### Added
- Original WAV sound packs under `Assets/Sounds/nothing/` and `Assets/Sounds/ios/` (`notify`, `expand`, `collapse`, `swipe`, `error`, `hover`) — synthesized tones *inspired by* soft Glyph-like clicks and soft iOS-like taps; **not** proprietary Nothing OS / Apple iOS system audio (see `Assets/Sounds/README.md`).
- Optional `Assets/Sounds/system/` simple beeps; **System** pack prefers Windows `SystemSounds` / `MessageBeep`.
- `SoundPack` enum: `Nothing` | `Ios` | `System` | `Off`.
- Master volume + per-event volume multipliers in Settings and `settings.json`.
- Hover cue with debounce (optional; volume default low).

### Changed
- `IslandSounds` plays local WAVs from app base `Assets/Sounds/{pack}/` (SoundPlayer / winmm), with SystemSounds fallback for the System pack.
- Morph cues split into expand vs collapse.
- Settings UI: pack combo + per-sound volume sliders.
- Docs: `ISLAND_GUIDELINES.md`, `ISLAND_PREVIEW.md` sound pack section.

### Legal
Do not replace pack files with ripped Nothing Glyph or Apple UIKit system sounds.
