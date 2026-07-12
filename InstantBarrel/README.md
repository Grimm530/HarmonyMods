# InstantBarrel Harmony Mod

Makes barrels and road signs 1 HP and instantly spawns loot in the player's inventory when hit. Converted from the Oxide plugin by Tryware OÜ. **No Oxide dependency**: the mod patches game methods directly (see `.cursor/Harmony-Assembly/HARMONY_MODS_GUIDE.md`).

## How It Works

- **Patch**: `BaseCombatEntity.OnAttacked` (Prefix)
- **Targets**: Loot barrels, oil barrels, road signs (loot_barrel_1/2, loot-barrel-1/2, oil_barrel, roadsign1-9)
- **Permissions**: Config-only; no Oxide or external permission system. Use "Require permission" in config to reserve for future use; currently all players get instant barrels when the mod is enabled.
- **Config**: `HarmonyConfig/InstantBarrel.json`

## Requirements

- No Oxide/uMod or companion plugin required. Copy the DLL to `HarmonyMods/` and load with `harmony.load InstantBarrel`.

## Installation

1. Build: `.\build.ps1` from this directory
2. Copy `InstantBarrel.dll` to your server `HarmonyMods/` (build script may copy to a fixed path)
3. Load: `harmony.load InstantBarrel` (or restart server)

## Config Options

| Option | Default | Description |
|--------|---------|-------------|
| Enable farming with weapons | true | Allow projectile/weapon hits |
| Max farming distance | 3 | Max distance (meters) from barrel |
| Make barrels 1 hit to kill | true | One-shot barrels regardless of damage |
| Enable barrel gibs | true | Spawn gibs when barrel destroyed |
| Require permission | true | Reserved; no permission system. When false, all players get instant barrels. |

## Source Structure

```
InstantBarrel/
├── InstantBarrel.csproj
├── build.ps1
├── InstantBarrel/
│   ├── InstantBarrelMod.cs      # Main mod, IHarmonyModHooks, config-only
│   ├── InstantBarrelConfig.cs   # JSON config loading
│   └── Patches/
│       └── LootContainer_OnAttacked_Patch.cs  # Barrel intercept logic
└── README.md
```

## Compatibility

- **Other mods**: This mod runs as a Harmony prefix on `BaseCombatEntity.OnAttacked`. Other Harmony mods or Oxide plugins that patch the same or related methods may run before or after; order depends on load order.
- **Scrap tea bonus**: Preserves vanilla scrap yield modifier logic from `LootContainer_DropBonusItems`.
- **Leaderboard (Harmony)**: Loot is **injected** straight into your inventory when you break the barrel (so you can shoot from ~5m and it’s grabbed instantly). When the **Leaderboard** mod is loaded, InstantBarrel notifies it via reflection (scans loaded assemblies for `Leaderboard.LeaderboardMod`) so each item is recorded as **LootItems** (e.g. scrap). Load Leaderboard before or with InstantBarrel so the integration is active.
