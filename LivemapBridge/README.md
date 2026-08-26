# LivemapBridge

Small Harmony mod. It does **not** patch gameplay. It dumps live XYZ so the web map can stop using demo paths.

Source: `C:\svr1\.cursor\HarmonyMods\LivemapBridge`  
Runtime DLL: `C:\svr1\HarmonyMods\LivemapBridge.dll`  
Viewer: `C:\!WEB RCON PANEL\livemap\` — see that folder’s README for controls, mesh extractors, and UI.

## Why a new mod

`RustadminOnline.cs` (Oxide) only wraps `playerlist` with `Position` plus `rustadmin.rendermap`. That is players + a 2D JPEG. It does not know patrol heli, Bradley, trains, or Convoy.

None of the existing Harmony mods expose a livemap snapshot. Bolting this onto Leaderboard / Convoy would couple unrelated systems. This DLL is the API.

## What it writes

Every ~0.4s (config):

| File | Role |
|---|---|
| `C:\!WEB RCON PANEL\livemap\data\snapshot.json` | Players + vehicles (MAP tab polls this) |
| `C:\!WEB RCON PANEL\livemap\data\buildings.json` | Building blocks |
| `C:\svr1\HarmonyData\Livemap\*.json` | Server-local copies of the same dumps |

Once per load (when monument list is ready):

| File | Role |
|---|---|
| `C:\!WEB RCON PANEL\livemap\data\monuments.json` | Runtime `MonumentInfo` placements |
| `C:\svr1\HarmonyData\Livemap\monuments.json` | Server-local copy |

Once per load (when Minimap cache exists):

| File | Role |
|---|---|
| `C:\!WEB RCON PANEL\livemap\data\map.png` | Minimap overworld render (same image/coords as in-game map) |
| `C:\!WEB RCON PANEL\livemap\data\terrain.json` | Live `TerrainMeta` + `oceanMargin: 500` for UV math |
| `C:\svr1\HarmonyData\Livemap\map.png` | Server-local copy |

Requires the **Minimap** Harmony mod to finish its overworld render first (`HarmonyData/Minimap/cache/world.minimap.*.png`).

### Snapshot JSON

```json
{
  "live": true,
  "worldSize": 4000,
  "players": [{ "id": "7656…", "name": "…", "x": 0, "y": 0, "z": 0, "yaw": 90 }],
  "vehicles": [
    { "id": "heli-…", "type": "patrolheli", "x": 0, "y": 80, "z": 0, "yaw": 90 },
    { "id": "bradley-…", "type": "bradley", "x": 100, "y": 2, "z": 50, "yaw": 0 },
    { "id": "convoy-…", "type": "sedan", "x": -1055, "y": 7.7, "z": 463, "yaw": 159 }
  ]
}
```

### Vehicle sources

| Source | Types written |
|---|---|
| `PatrolHelicopter` | `patrolheli` |
| `BradleyAPC` (not part of Convoy) | `bradley` — yaw follows parent `TrainCar` when welded (ArmoredTrain) |
| ArmoredTrain event (`_trainEngine` + `_wagonDatas`) | `locomotive` / `wagon` / `wagon_fuel` / `wagon_loot` / `wagon_flat` |
| Convoy `EventController._vehicles` (when event active) | `sedan`, `vendor`, `modular_car`, `bradley` |

Convoy rows use ids `convoy-<netId>` so the viewer can toggle them with the convoy layer even when `type` is `bradley`. Convoy Bradleys are skipped in the normal Bradley scan to avoid duplicates.

Convoy positions are whatever the Convoy mod sets on the entity (road `Sample` path). This bridge does not invent or repath them.

### Buildings JSON

Block rows: `x, y, z, yaw, k` (kind), `g` (grade), optional `h` (height scale for half / low walls), optional `t` (`1` = triangle footprint). Viewer treats `transform.position` as bottom-center; walls are thin on local X; Unity yaw is applied as +Y in Three.js.

## RCON

`livemap.snapshot` — same JSON as the file dump, admin only. Useful to test without the panel path.

## Build / load

```
powershell -File C:\svr1\.cursor\HarmonyMods\LivemapBridge\build.ps1
harmony.load LivemapBridge
```

On boot the snapshot loop waits until `InvokeHandler` exists (same pattern as Minimap). If the map still shows a stale `players: []` after a restart, reload with `harmony.load LivemapBridge`.

Config: `HarmonyConfig/LivemapBridge.json`

Do **not** point Staging / 2X at the same panel snapshot path or they will overwrite SVR1.
