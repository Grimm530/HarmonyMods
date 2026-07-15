# RestoreItems (Harmony)

Oxide-free port of **RestoreItems 2.1.6** (Grimm530). Captures player inventory on death and restores via `/getstuff` (configurable), with Economics cost, RaidableBases zone detection, and dungeon API parity.

## Build

```powershell
.\build.ps1
```

Deploys `RestoreItems.dll` to root `HarmonyMods/`.

## Paths

| Oxide | Harmony |
|-------|---------|
| `oxide/config/RestoreItems.json` | `HarmonyConfig/RestoreItems.json` |
| `oxide/data/RestoreItems_playerData.json` | `HarmonyData/RestoreItems/RestoreItems_playerData.json` |
| `oxide/lang/RestoreItems` | `HarmonyLanguage/RestoreItems.json` (optional) |

## Load order

- **0Permissions** — `restoreitems.use` is granted to group **`default`** on load (all players)
- **Economics** (optional, for restore cost)
- **RaidableBases** (optional, skips capture/restore in raid zones via `EventTerritory`)
- **RestoreItems**

## Commands

- `/getstuff` (configurable in config) — restore death inventory (permission + Economics cost)
- `/restored.debug on|off|toggle` — admin debug logging
- `/restoretest` — admin test restore

## API (AppDomain)

- `RestoreItems_ApiType` → `RestoreItemsHarmony.RestoreItemsHarmonyMod`
- `RestoreItems_Plugin` → wrapper with `Call(method, args)`

Static methods: `RestorePlayerItems`, `HasItemsToRestore`, `AutoRestorePlayerItems`, `SaveDungeonInventory`, `RestoreDungeonInventory`, `ClearDungeonInventory`, `HasDungeonInventory`.

## Re-port from Oxide source

After editing `.cursor/Oxide.Plugins.Cant-Use/RestoreItems.cs`:

```powershell
.\convert-from-oxide.ps1
.\build.ps1
```
