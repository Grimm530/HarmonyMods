# Convoy (Harmony Mod)

Harmony port of the Oxide **Convoy** event. Spawns vehicles on roads, mounts GrimmNPC scientists, crates/turrets/samsites, map markers, and drives the convoy along a road route. No Oxide / NpcSpawn dependency.

## Identity

| Field | Value |
|-------|--------|
| **Name** | Convoy |
| **Type** | Harmony mod |
| **Config** | `HarmonyConfig/Convoy.json` (same schema as Oxide Convoy) |
| **Depends on** | **0GrimmNPC** (NPC AI / kits; DLL name; C# type still `GrimmNPC`). Optional: **0PveMode** (event ownership when `"PVE Mode Setting"` → Enable), **TruePVE** |
| **Load** | Prefer: `0Permissions` → `TruePVE` → `0PveMode` → `0GrimmNPC` → `Convoy`. `0GrimmNPC` sorts before Convoy. |

## Commands

| Command | Description |
|---------|-------------|
| **convoystart** `[preset]` | Start convoy (server console or admin). Optional preset: `easy`, `medium`, `hard`, `nightmare`. |
| **convoystop** | Stop convoy and clean up. |

## What it does

1. Builds a road route from `TerrainMeta.Path.Roads` (Route Settings).
2. Spawns vehicles from Convoy Presets (sedan, bike, bradley, modular, vendor, karuza).
3. Spawns NPCs via **GrimmNPC.SpawnNpc** (`0GrimmNPC.dll`), mounts them, equips wear/belt from NPC Configurations.
4. Spawns child crates / turrets / samsites from vehicle configs.
5. Moves the convoy **kinematically along the route** (on-rails). Attack → stop + aggressive turrets + event zone spheres. After the stop timer, the convoy only resumes if roaming NPCs are still alive; killed NPCs are **not** respawned on remount. All crates opened/destroyed → stay stopped and despawn after `Time to destroy the convoy after opening all the crates`.
6. Map marker follows the lead vehicle.
7. **PVE Mode Setting** (when Enable + **0PveMode** loaded): registers a PveMode ownership zone on stop (`OwnerIsStopper`, damage threshold, owner-only loot, cooldowns). When disabled, falls back to Convoy's simple team damage lock.

## Config

Prefer `HarmonyConfig/Convoy.json` (full Oxide-compatible file).

Important Route Settings:

- **Type of routes**: use **`0`** (standard) for reliable starts. `1` (complex) needs caching time and longer roads.
- **Minimum road length**: `200` is a good default on most maps.

`"Supported Plugins"` → `"PVE Mode Setting"` is wired when **0PveMode** is loaded. Set Enable to `false` to use Convoy-only team lock.

## Build / deploy

```powershell
.\build.ps1
```

Copies `Convoy.dll` to `c:\!2XRUST\HarmonyMods\Convoy.dll`.

Load: `harmony.load 0PveMode` then `harmony.load Convoy` (`0GrimmNPC` required for NPCs).

## Known simplifications vs Oxide Convoy

- Kinematic on-rails movement (not full wheel-physics drivers).
- No EventHeli, Economics, Discord, or CUI countdown GUI.
- Modular cars use the `_spawned` prefab variant (no per-module customization).
- Custom crate loot tables not applied (vanilla crate loot).

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| `RouteNotFound_Exeption` | Map has no roads, or PathType/MinRoadLength too strict. Set PathType `0`, MinRoadLength `200`. |
| No vehicles | Config missing Sedan/Bike/etc. presets matching Convoy Presets order. |
| Plain scientists | Load **0GrimmNPC** before Convoy (`harmony.load 0GrimmNPC`). |
| No owner / anyone can loot on PVE | Load **0PveMode**, set `"Use the PVE mode of the plugin?"` true. Remove legacy `HarmonyMods/PveMode.dll` if present. |
| Turrets shoot Bradley | Rebuild includes TurretOptimizer — `harmony.load Convoy`. |
| PhysX `illegal collision shapes` + `TravellingVendor.FindClosestNode` NRE spam | Hard/Nightmare presets use TravellingVendor vans. Rebuild includes `PrepareTravellingVendor` + `OnSplinePathTrigger` block — `harmony.load Convoy` (or restart). |
