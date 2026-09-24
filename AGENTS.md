# AGENTS.md

1. Read [`CONTEXT.md`](CONTEXT.md) **first** — product map, constraints, Settings IA (1.11.0 sidebar), icon packs, Sandbox deploy rule, hotkeys F9–F12 / Esc unpin.
2. Follow [`docs/ISLAND_GUIDELINES.md`](docs/ISLAND_GUIDELINES.md) for all island UI/animation/feature work; preview copy in [`docs/ISLAND_PREVIEW.md`](docs/ISLAND_PREVIEW.md).
3. Numeric tokens live in `NotifyIsland.Core/OverlayTokens.cs` — keep in sync with GUIDELINES.
4. Do not invent animation timings; use the table in GUIDELINES §2.
5. Hard constraints: **no Open-Meteo**, **no island mouse-drag** (Edge+Offset only), **no swipe gestures** (clicks only). Backlog later: file shelf / clipboard / launcher.
6. Icons: prefer vendored Lucide (ISC) / Tabler (MIT) under `Assets/Icons/`; browse https://allsvgicons.com/pack/ — update `Assets/Icons/NOTICE` when vendoring.
7. Git commit identity: `git -c user.name='Leorik69' -c user.email='leorik69@users.noreply.github.com'` — never `git config`.
8. Sandbox: always kill old NotifyIsland, publish to fresh `Desktop\notifyisland-fresh-<sha>`, then launch.
