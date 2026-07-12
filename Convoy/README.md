# Convoy (Harmony Mod)

Harmony port of the Oxide **Convoy** event. Spawns vehicles on roads, mounts GrimmNPC scientists, crates/turrets/samsites, map markers, and drives the convoy along a road route. No Oxide / NpcSpawn dependency.

## Identity

| Field | Value |
|-------|--------|
| **Name** | Convoy |
| **Type** | Harmony mod |
| **Config** | `HarmonyConfig/Convoy.json` (same schema as Oxide Convoy) |
| **Depends on** | **GrimmNPC** (for NPC AI / kits). Load GrimmNPC first. |
| **Load** | `harmony.load GrimmNPC` then `harmony.load Convoy` |

## Commands

| Command | Description |
|---------|-------------|
| **convoystart** `[preset]` | Start convoy (server console or admin). Optional preset: `easy`, `medium`, `hard`, `nightmare`. |
| **convoystop** | Stop convoy and clean up. |

## What it does

1. Builds a road route from `TerrainMeta.Path.Roads` (Route Settings).
2. Spawns vehicles from Convoy Presets (sedan, bike, bradley, modular, vendor, karuza).
3. Spawns NPCs via **GrimmNPC.SpawnNpc** (`IdleState` + `CombatStationaryState` when mounted), mounts them, equips wear/belt from NPC Configurations.
4. Spawns child crates / turrets / samsites from vehicle configs.
5. Moves the convoy **kinematically along the route** (on-rails). Attack → stop + aggressive turrets.
6. Map marker follows the lead vehicle. Event lock / loot rules from existing Convoy patches.

## Config

Prefer `HarmonyConfig/Convoy.json` (full Oxide-compatible file is restored there).

Important Route Settings:

- **Type of routes**: use **`0`** (standard) for reliable starts. `1` (complex) needs caching time and longer roads.
- **Minimum road length**: `200` is a good default on most maps.

Do not leave vehicle / NPC / crate arrays empty — the event needs those presets.

## Build / deploy

```powershell
.\build.ps1
```

Copies `Convoy.dll` to `c:\!2XRUST\HarmonyMods\Convoy.dll`.

## Known simplifications vs Oxide Convoy

- Kinematic on-rails movement (not full wheel-physics drivers).
- No EventHeli, PVE mode, Economics, Discord, or CUI GUI.
- Modular cars use the `_spawned` prefab variant (no per-module customization).
- Custom crate loot tables not applied (vanilla crate loot).

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| `RouteNotFound_Exeption` | Map has no roads, or PathType/MinRoadLength too strict. Set PathType `0`, MinRoadLength `200`. Console logs road count. |
| No vehicles | Config missing Sedan/Bike/etc. presets matching Convoy Presets order. |
| Plain scientists | Load **GrimmNPC** before Convoy. |
| Marker only (old behavior) | Reload this build — old DLL only created markers. |
