# LootQoL (Harmony)

Combined Oxide **Fast Loot 1.1.0** + **Loot Bouncer 1.0.11** + **Sort Button 2.8.0** port (no Oxide runtime).

Do **not** also load `SortButton.dll`. Sort lives in this mod. If `HarmonyMods/SortButton.dll` is present, both mods create `UISortButton` on Overlay; `DestroyUI` only removes the second copy, so the sort button (especially on the tool cupboard) stays on screen after closing loot. Delete that DLL and `harmony.unload SortButton` or restart.

## Load order

1. **0Permissions** (`0Permissions.dll`)
2. **LootQoL** (`LootQoL.dll`)

## Deploy

```powershell
.\build.ps1
```

Copies **only** `LootQoL.dll` to `HarmonyMods\LootQoL.dll`.

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/LootQoL.json` (FastLoot + LootBouncer + SortButton sections) |
| Lang | `HarmonyLanguage/LootQoL.json` |
| Data | `HarmonyData/LootQoL/` (SortButton player prefs: `SortButton.json`) |
| Images | `HarmonyImages/LootQoL/` |

On first load without a `SortButton` config section, `HarmonyConfig/SortButton.json` is imported if present. Player prefs are imported from `HarmonyData/SortButton.json` or `HarmonyData/SortButton/SortButton.json` when `HarmonyData/LootQoL/SortButton.json` does not exist yet.

## Features

- **FastLoot:** Overlay "Take all" button on loot crates/corpses/dropped containers (`fastloot.use`). CUI command: `cui.endtest LOOTQOL take`.
- **LootBouncer:** After a partial loot, leftover items bounce/despawn after a timeout. Trade boxes skipped only when `Trade_ApiType` is registered. Slap plugin is a no-op.
- **SortButton:** Overlay sort button on supported storage (boxes, TC, fridge, horse storage, etc.). Name vs category toggle. CUI commands: `cui.endtest LOOTQOL sort` / `order`. Chat: `/sortbutton`, `/sortbutton sort` (or `type`).

## Permissions

| Permission | Effect |
|------------|--------|
| `fastloot.use` | Show and use the Take all button |
| `sortbutton.use` | Show and use the Sort button / chat commands |

Granted to the Permissions group **`admin`** on load. Grant `sortbutton.use` to `default` if every player should see Sort.

## SortButton notes

- Ownership check uses vanilla teams plus optional Clans / Friends Harmony APIs (`ClansHarmony.ClansMod` / `FriendsHarmony.FriendsMod`, or `Clans_ApiType` / `Friends_ApiType`). Missing plugins are a no-op.
- Powered industrial storage adaptors hide the sort button; unpowered adaptors shift X to avoid overlap.
- Prefabs are auto-discovered on server init (deployable boxes/TCs/fridges + apartment `BoxStorage`).
- Multi-container loot panels are not sorted.
