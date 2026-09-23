# Пакеты иконок для NotifyIsland

Короткий список **открытых outline**-наборов для Dynamic Island–подобного UI.  
Рантайм: `IconPackService` → SVG из `Assets/Icons/{pack}/` или fallback **IslandIcons** (`IslandIcons.cs`).

| Пакет | Лицензия | Статус | Путь |
|---|---|---|---|
| **IslandIcons** | Original / inspired by Fluent+Tabler | default, встроенный | `IslandIcons.cs` |
| **Tabler Icons** | **MIT** | **вендор** | `Assets/Icons/Tabler/` + `LICENSE` |
| **Lucide** | **ISC** | **вендор** | `Assets/Icons/Lucide/` + `LICENSE` |
| Phosphor / Fluent / Heroicons | MIT | research only | не вендорятся |

Сводка лицензий: [`Assets/Icons/NOTICE`](../Assets/Icons/NOTICE).

## Вендор (1.5.7+)

Ключи SVG (24×24 outline): `clock`, `notify`, `media`, `pause`, `timer`, `progress`, `error`, `battery`,  
`weather-clear`, `weather-partly`, `weather-cloud`, `weather-fog`, `weather-drizzle`, `weather-rain`, `weather-snow`, `weather-storm`.

Источники файлов:
- Tabler: https://github.com/tabler/tabler-icons (outline)
- Lucide: https://github.com/lucide-icons/lucide

Настройки → **Иконки** переключает пакет; при ошибке загрузки SVG — IslandIcons.

## Не делаем

- Проприетарные / Apple SF Symbols / платные паки
- Извлечение иконок из прошивок Nothing OS / iOS
