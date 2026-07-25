# CHT (Custom Helicopter Tiers)

Oxide-free Harmony port of **Custom Helicopter Tiers 2**. DLL / load name: **`CHT`**.

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/CHT.json` |
| Tier data | `HarmonyData/CHT/<TierName>.json` |
| Cooldowns | `HarmonyData/CHT/Cooldowns.json` |

## Load order

1. `harmony.load 0Permissions`
2. Optional: `Economics`, `AlphaLoot`, `SkillTree`
3. `harmony.load CHT`

## Build

```powershell
.\.cursor\HarmonyMods\CHT\build.ps1
```

Copies only `CHT.dll` to root `HarmonyMods/`.

## Commands

| Command | Role |
|---------|------|
| `heli.shop` (F1 / config name) or Shop `cht.openshop %steamid%` | Player shop UI (no chat patch — avoids breaking Shop `/s`) |
| `cht.heli` | Call / spawn / kill / list helis |
| `cht.tier` | Create / manage tier JSON |
| `cht.callprofile` | Manage call profiles |
| `cht.gib` / `cht.crate` | Cleanup debris / crates |
| `cht.shopcontroller` | Shop UI button handler |

Admin permission: `customhelicoptertiers2.admin` (plus per-profile permissions from tier JSON). Those permission strings are unchanged for existing grants.

## Kept integrations

- **0Permissions** — generation rebind + ready callbacks
- **Economics** — coin costs / rewards
- **AlphaLoot** — optional crate profiles
- **SkillTree** — optional XP on caller kill

## Config fields supported

**Global:** Version, Enable Debug, Global Population Limit, Rarity Weights, Heli Shop Chat Command, Disable Vanilla Patrol Helicopter.

**Tier:** SchemaVersion through Death Command Sets, including Alpha Loot Profile, Use Custom Loot Table, Skill Tree XP Rewarded, Cost To Call via `coin` (Economics) or item shortnames. Allies = Rust teammates only.

## Removed vs Oxide original

- Loottable / `Use Loot Table Preset`
- ServerRewards / `point` costs
- XPerience XP Rewarded
- Clans / Friends ally expansion
- Oxide hooks / PluginReference / Interface.CallHook bus
