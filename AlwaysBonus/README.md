# AlwaysBonus – Harmony Mod

Auto-hits X markers on trees and stars on nodes. Migrated from Oxide plugin `AlwaysBonus` by Tryhard.

## Features

- **Tree X farming**: Always counts as hitting the X marker when chopping trees (no need to aim at the moving marker)
- **Node star farming**: Effectively always hits the star hotspot when mining nodes (jackhammer or pickaxe)

## Installation

1. Build: `.\build.ps1`
2. Copy `AlwaysBonus.dll` to `HarmonyMods/` (build script does this automatically)
3. Load: `harmony.load AlwaysBonus`
4. Unload: `harmony.unload AlwaysBonus`

## Config

**Path**: `HarmonyConfig/AlwaysBonus.json` (created on first load)

```json
{
  "Enable auto tree X farming": true,
  "Enable auto node star farming": true
}
```

## Architecture

| File | Purpose |
|------|---------|
| `AlwaysBonusMod.cs` | Main mod, IHarmonyModHooks, loads config on load |
| `AlwaysBonusConfig.cs` | Loads `HarmonyConfig/AlwaysBonus.json` |
| `Patches/OreResourceEntity_OnAttacked_Patch.cs` | Transpiler: node star radius multiplier (1.5f → 25f when enabled) |
| `Patches/TreeEntity_DidHitMarker_Patch.cs` | Prefix: always return true when tree X enabled |

## References

- HARMONY_MODS_GUIDE: `.cursor/Harmony-Assembly/HARMONY_MODS_GUIDE.md`
- Original: `oxide/onhold/AlwaysBonus.cs`
