# SortButton (Harmony)

Oxide **SortButton 2.7.0** port. Adds a sort button on supported storage loot panels.

## Load order

1. **0Permissions**
2. **SortButton**

## Deploy

```powershell
.\build.ps1
```

CUI buttons use `cui.endtest SORTBUTTON …` (clients only forward ConsoleGen commands).

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/SortButton.json` |
| Data | `HarmonyData/SortButton/SortButton.json` |
| Lang | `HarmonyLanguage/SortButton.json` (EN primary) |

## Commands

- `/sortbutton` — toggle button
- `/sortbutton sort` / `type` — name vs category

Clans/Friends: optional `AppDomain` `Clans_ApiType` / `Friends_ApiType`. Missing plugins are a no-op.
