# LootQoL (Harmony)

Combined Oxide **Fast Loot 1.1.0** + **Loot Bouncer 1.0.11** port (no Oxide runtime).

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
| Config | `HarmonyConfig/LootQoL.json` (FastLoot + LootBouncer sections) |
| Lang | `HarmonyLanguage/LootQoL.json` |
| Data | `HarmonyData/LootQoL/` |
| Images | `HarmonyImages/LootQoL/` |

## Features

- **FastLoot:** Overlay "Take all" button on loot crates/corpses/dropped containers (`fastloot.use`). CUI command: `cui.endtest LOOTQOL take`.
- **LootBouncer:** After a partial loot, leftover items bounce/despawn after a timeout. Trade boxes skipped only when `Trade_ApiType` is registered. Slap plugin is a no-op.

## Permissions

| Permission | Effect |
|------------|--------|
| `fastloot.use` | Show and use the Take all button |

Granted to the Permissions group **`admin`** on load.
