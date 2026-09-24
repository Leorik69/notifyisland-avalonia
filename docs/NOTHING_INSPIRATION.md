# Nothing OS — вдохновение (легально)

NotifyIsland **не** копирует Nothing OS и **не** извлекает ассеты из APK/ROM/прошивок.

## Можно брать как *идею* (inspiration)

| Идея | Как у нас |
|---|---|
| Мягкие «glyph-like» клики | Оригинальные WAV в `Assets/Sounds/nothing/` (синтез, не системные звуки Nothing) |
| Геометричный / техно-шрифт | **Space Grotesk** (OFL) и **JetBrains Mono** (OFL) в `Assets/Fonts/` — похожий vibe, не NType82 |
| Плотность «точек» / glyph matrix | Сдержанный unread-indicator (чуть более «квадратная» точка), без матрицы Glyph |
| Минимальный статус-индикатор | Unread pulse + breath; не широкая копия Glyph Interface |

## Нельзя копировать

| Запрещено | Почему |
|---|---|
| **NType82** (бинарник шрифта) | Проприетарный шрифт Nothing — **не** извлекать из APK/ROM |
| Glyph Matrix firmware assets | Проприетарные растровые/прошивочные ассеты |
| Товарные знаки / логотипы Nothing | Branding |
| Официальные системные звуки Nothing OS | IP; используем собственные inspired WAV |

## Шрифты в продукте

| Id в Settings | Файл | Лицензия |
|---|---|---|
| `System` | Segoe UI Variable / Inter (NuGet) | Система / OFL (Inter) |
| `SpaceGrotesk` | `Assets/Fonts/SpaceGrotesk-*.ttf` | SIL OFL 1.1 |
| `JetBrainsMono` | `Assets/Fonts/JetBrainsMono-*.ttf` | SIL OFL 1.1 |

См. `Assets/Fonts/NOTICE`, `OFL-SpaceGrotesk.txt`, `OFL-JetBrainsMono.txt`.
