# Пакеты иконок для NotifyIsland

Рантайм: `IconPackService` → SVG из `Assets/Icons/{pack}/` или fallback **IslandIcons** (`IslandIcons.cs`).

| Пакет | Лицензия | Статус | Путь |
|---|---|---|---|
| **IslandIcons** | Original / inspired by Fluent+Tabler | default, встроенный | `IslandIcons.cs` |
| **Tabler Icons** | **MIT** | **вендор** | `Assets/Icons/Tabler/` + `LICENSE` |
| **Lucide** | **ISC** | **вендор** | `Assets/Icons/Lucide/` + `LICENSE` |
| **Meteocons Fill** | **MIT** (Bas Milius) | **вендор** | `Assets/Icons/MeteoconsFill/` |
| **Meteocons Flat** | **MIT** | **вендор** | `Assets/Icons/MeteoconsFlat/` |
| **Meteocons Line** | **MIT** | **вендор** | `Assets/Icons/MeteoconsLine/` |
| **Meteocons Monochrome** | **MIT** | **вендор** | `Assets/Icons/MeteoconsMonochrome/` |
| Phosphor / Fluent / Heroicons | MIT | research only | не вендорятся |

Сводка лицензий: [`Assets/Icons/NOTICE`](../Assets/Icons/NOTICE).

## Outline UI (1.5.7+)

Ключи SVG (24×24 outline): `clock`, `notify`, `media`, `pause`, `timer`, `progress`, `error`, `battery`,  
`weather-clear`, `weather-partly`, `weather-cloud`, `weather-fog`, `weather-drizzle`, `weather-rain`, `weather-snow`, `weather-storm`.

Источники: Tabler / Lucide GitHub.

## Meteocons weather (1.5.9+)

Четыре официальных стиля из [`@meteocons/svg`](https://www.npmjs.com/package/@meteocons/svg) / [basmilius/meteocons](https://github.com/basmilius/meteocons) (MIT):

1. **Fill** — заливка, градиенты  
2. **Flat** — плоский цвет без градиентов  
3. **Line** — контур  
4. **Monochrome** — чёрный/белый, тинтится под `ColorTextSecondary`

CDN (справочно): `https://cdn.meteocons.com/latest/svg/{fill|flat|line|monochrome}/{slug}.svg`

### Маппинг ключей

| NotifyIsland key | Upstream slug |
|---|---|
| weather-clear | clear-day |
| weather-partly | partly-cloudy-day |
| weather-cloud | cloudy |
| weather-fog | fog |
| weather-drizzle | drizzle |
| weather-rain | rain |
| weather-snow | snow |
| weather-storm | thunderstorms |
| weather-sleet | sleet |

Файлы в репо названы как ключи (`weather-rain.svg`), чтобы `IconPackService` резолвил по `key.svg`.

### Анимация

Исходные SVG содержат **SMIL** (`<animate>` / `<animateTransform>`).  
`Avalonia.Svg.Skia` **не исполняет SMIL** — рисует статичный кадр геометрии.  
`MeteoconsMotion` добавляет Avalonia-анимации, зеркалящие основной SMIL-паттерн:

- clear / partly → бесконечный rotate (6s / 10s)  
- cloud / drizzle / rain / snow / sleet → мягкий bob по Y (3s)  
- storm / fog → opacity pulse  

Полный SMIL через WebView2 или Lottie (`@meteocons/lottie` + Skottie) — возможный следующий шаг; SVG с SMIL сохранены для атрибуции и будущего хоста.

Meteocons применяется только к `weather-*`; остальные ключи (clock, notify, …) при выборе Meteocons → IslandIcons.

Настройки → **Иконки** переключает пакет.

## Не делаем

- Проприетарные / Apple SF Symbols / платные паки  
- Извлечение иконок из прошивок Nothing OS / iOS  
