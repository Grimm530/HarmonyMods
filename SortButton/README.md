# SortButton (Harmony) — superseded

**Do not load this DLL on SVR1.** Sort Button 2.8.0 now lives in **LootQoL** (`.cursor/HarmonyMods/LootQoL`). Loading both duplicates loot patches and CUI.

This folder is kept as a standalone reference only.

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
