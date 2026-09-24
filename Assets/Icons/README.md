# NotifyIsland icon packs

- **IslandIcons** — built-in Avalonia `StreamGeometry` (`IslandIcons.cs`), stroke ≈ 1.75, 24×24.
- **Tabler/** — vendored MIT outline SVGs (subset). See `LICENSE` + `../NOTICE`.
- **Lucide/** — vendored ISC outline SVGs (subset). See `LICENSE` + `../NOTICE`.

Runtime: `IconPackService` loads `Assets/Icons/{pack}/{key}.svg` (tinted), falls back to IslandIcons.

Keys: `clock`, `notify`, `media`, `pause`, `timer`, `progress`, `error`, `battery`,
`weather-clear|partly|cloud|fog|drizzle|rain|snow|storm`.
- **FontAudio/** — CC BY 4.0 digital segment digits (`digital0`…`9`, colon, dot) by @fefanto. See `ATTRIBUTION.md`.
