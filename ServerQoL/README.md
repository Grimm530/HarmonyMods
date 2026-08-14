# ServerQoL (Harmony)

Combines four small Oxide QoL plugins into one Harmony mod. **No Oxide required.**

| Oxide plugin | Behaviour |
|--------------|-----------|
| UnlockInventory 0.1.0 | Unlocks player main/belt/wear inventories |
| InfiniteBurn 1.1.0 | Candles last forever; torches stay lit without fuel |
| Electric Generator Tweaker 1.0.1 | Sets `ElectricGenerator.electricAmount` |
| Infinite Vending Stock 1.0.3 | NPC vending machines restock to 10,000,000; optional buy caps from CastleVendingSetup |

## Load order

1. **0Permissions** (`0Permissions.dll`) — only required for generator owner permission
2. **ServerQoL** (`ServerQoL.dll`)

Unload the four Oxide plugins before loading this mod so they do not double-apply.

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/ServerQoL.json` |
| Castle vending (optional) | `HarmonyConfig/CastleVendingSetup.json` (copied from `oxide/config/CastleVendingSetup.json`) |

On first load, missing Harmony files are copied from the matching Oxide config when present.

## Permissions

| Permission | Effect |
|------------|--------|
| `electricgeneratortweaker.tweak` | Owner-only generator tweak when "Setting for all World" is false |

Granted to Permissions group **`admin`** on load.

## Build

```powershell
.\.cursor\HarmonyMods\ServerQoL\build.ps1
```

Copies only `ServerQoL.dll` to `HarmonyMods/`.
