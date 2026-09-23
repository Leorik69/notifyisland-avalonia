# Пакеты иконок для NotifyIsland

Короткий список **открытых outline**-наборов, подходящих для Dynamic Island–подобного UI (погода, медиа, часы, уведомления).  
Текущий рантайм: встроенный **IslandIcons** (`IslandIcons.cs`). Плановые пакеты выбираются в Настройки → Иконки (пока stub; ассеты не вендорятся в этом релизе).

| Пакет | Лицензия | URL | Заметки |
|---|---|---|---|
| **IslandIcons** (текущий) | Original / inspired by Fluent+Tabler outline | `IslandIcons.cs`, `Assets/Icons/README.md` | Stroke 1.75, 24×24, weather+kind keys |
| **[Tabler Icons](https://tabler.io/icons)** | **MIT** | https://github.com/tabler/tabler-icons | 6000+ SVG, 24×24, 2px stroke; отличные weather / player / bell / clock |
| **[Lucide](https://lucide.dev)** | **ISC** (MIT-совместима) | https://github.com/lucide-icons/lucide | Форк Feather; чистый outline; clock, bell, cloud-sun, play |
| **[Phosphor Icons](https://phosphoricons.com)** | **MIT** | https://github.com/phosphor-icons/core | Вес **Light/Thin** ≈ outline; media + weather набор |
| **[Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons)** | **MIT** | https://github.com/microsoft/fluentui-system-icons | Regular (outline) от Microsoft; хорошо ложится на Win11 |
| **[Heroicons](https://heroicons.com)** (outline) | **MIT** | https://github.com/tailwindlabs/heroicons | Компактный набор; меньше weather-специфики |

## Рекомендация near-term

1. Оставить **IslandIcons** как default (уже подогнан под капсулу).
2. Следующий кандидат к вендорингу: **Tabler** (MIT, полный weather/media) или **Lucide** (ISC, легче по объёму).
3. Не тянуть проприетарные / Apple SF Symbols / платные паки.

## Как вендорить позже

- Скопировать нужные SVG (только используемые ключи) в `Assets/Icons/{pack}/` с файлом `LICENSE`.
- Маппинг ключей → файл в коде; Settings `IconPack` уже персистится.
- Не коммитить иконки с лицензией, запрещающей redistribution.
