# RoadFix Harmony Mod

Makes procedural **roads** behave more like **rails**: road meshes follow the path height instead of snapping to terrain, and river crossings stay elevated instead of filling the valley.

## Why a separate mod?

CustomMapGen already owns ring-road / roadside / monument patches. Road mesh + terrain height are untouched there, so RoadFix stays a focused mod you can enable without CustomMapGen.

## What the game does (and does not)

| | Rails | Roads (vanilla) | RoadFix |
|---|---|---|---|
| Mesh `snapToTerrain` | `false` | `true` | `false` (default) |
| Terrain under rivers | fade = 0 (no fill) | full `AdjustTerrainHeight` | fade = 0 when enabled |
| Path over water | keep above water | often flattened into fill | keep above water |

Stock `PathList.SpawnBridge` exists but **nothing calls it** during procgen. Harbor “bridges” are monument content, not procedural road bridges. You have never seen stock road bridges because they are not generated.

## Custom bridge assets

| File | Use |
|------|-----|
| `maps/prefabs/bridge.map` | Road crossings |
| `maps/prefabs/bridgerail.map` | Rail crossings |

Placement uses **map origin (0,0,0)** and aligns your RustEdit path-center node onto the world path:

- Road center local: `(-6.346, 5.065, 0.262)`
- Rail center local: `(-6.316, 5.036, 0.127)`

Bridgeonly’s baked leg offset / X scale stay in the `.map`. Span length is applied as **Z scale** (`spanLength / BridgeTemplateLength`).

## Build / install

```powershell
cd .cursor\HarmonyMods\RoadFix
.\build.ps1
```

Copies `RoadFix.dll` to server `HarmonyMods\`. Config is created at `HarmonyConfig/RoadFix.json` on first load.

**Requires a new procedural map** (mesh/terrain patches only run during generation).

## Config (`HarmonyConfig/RoadFix.json`)

| Key | Default | Meaning |
|-----|---------|---------|
| `Enabled` | `true` | Master switch |
| `RoadsSnapToTerrain` | `false` | `false` = rail-style path mesh |
| `ElevateOverWater` | `true` | Path Y ≥ water + clearance on river/riverside |
| `SoftenTerrainUnderRivers` | `true` | Do not fill river valleys under the road |
| `WaterClearance` | `2` | Metres above water (rails use ~2) |
| `SpawnCustomBridges` | `true` | Place bridge maps on river crossings |
| `RoadBridgeMapPath` | `maps/prefabs/bridge.map` | Road bridge template |
| `RailBridgeMapPath` | `maps/prefabs/bridgerail.map` | Rail bridge template |
| `RoadPathCenterLocal` / `RailPathCenterLocal` | RustEdit centers | Align map node onto world path |
| `BridgeTemplateLength` | `12` | Native tile length (m); Z-scale = span / this |
| `DebugLogging` | `true` | Extra `[RoadFix]` log lines |

## Patch targets

- `GenerateRoadMeshes.Process` — rebuild with `snapToTerrain: false`
- `GenerateRoadTerrain.Process` — rail-style water elevation + terrain fade on river topology (`0x4000` / `0x8000`)

Does **not** patch CustomMapGen road targets (`GenerateRoadRing`, `MarkRoadside`, roadside monuments).

## Next steps (when you want custom bridge meshes)

1. Measure `bridge.map` segment length / width in RustEdit.
2. Detect road points over river topology (or path Y ≫ terrain).
3. Place N segments along the span with yaw from path tangent; scale X/Z to span length (or tile fixed-length segments).
4. Keep road mesh for driveable surface, or hide mesh under the custom deck once alignment is dialed in.
