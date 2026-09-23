# NotifyIsland icon pack

Unified **outline** icons (stroke ≈ 1.75) for clock, kinds, and dynamic weather.

- **Source of truth:** `IslandIcons.cs` (Avalonia `StreamGeometry` path data, 24×24 design space).
- **Style:** round caps/joins, secondary `#C8C8CC` on dark pill / white on accent chip; works on light+dark Win11 chrome.
- **Weather keys:** `weather-clear`, `weather-partly`, `weather-cloud`, `weather-fog`, `weather-drizzle`, `weather-rain`, `weather-snow`, `weather-storm`.
- **Kind keys:** `clock`, `notify`, `media`, `timer`, `progress`, `error`.
- **License:** original geometries inspired by MIT Fluent System Icons / Tabler outline style — no paid/proprietary packages.
- Crossfade between weather glyphs: `OverlayTokens.IconCrossfadeMs` (240 ms).
