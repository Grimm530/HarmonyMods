# AnimalSpawn (Harmony port)

Harmony port of Oxide **AnimalSpawn 1.0.81** for custom `BaseAnimalNPC` helpers (GrimmBoss animal ability). This is **not** part of GrimmNPC — GrimmNPC is Gen1 humanoid (`ScientistNPC`); this mod replaces animal brains.

**Horse ownership limits are not in this mod.** Shop already owns that (`HorseLimiter`, `shop.horse`, `animalspawn.horse` alias). Do not re-add horse spawn/claim logic here.

## Identity

| Field | Value |
|-------|--------|
| **DLL / load name** | `AnimalSpawn` |
| **Source** | `.cursor/HarmonyMods/AnimalSpawn/` |
| **C# type** | `AnimalSpawn.AnimalSpawn` |
| **AppDomain keys** | `AnimalSpawn.Type`, `AnimalSpawn.Instance`, `AnimalSpawn_ApiType` |
| **Skin ID** | `11491311214163` |
| **Config** | `HarmonyConfig/AnimalSpawn.json` (migrates from `oxide/config/AnimalSpawn.json`) |
| **Data** | `HarmonyData/AnimalSpawn/` (Preset + NavMesh; migrates from `oxide/data/AnimalSpawn/`) |

## Public API

GrimmBoss (and other mods) call:

```text
SpawnAnimal(Vector3 position, JObject|AnimalConfig)
```

Same JSON shape as Oxide AnimalSpawn (`Prefab`, `Health`, `RoamRange`, `ChaseRange`, `States`, …). Missing `States` defaults to Roam/Chase/Combat. `AreaMask` 0 becomes 1 (walkable).

Resolve via AppDomain (`AnimalSpawn.Instance`) — HarmonyLoader renames the assembly. GrimmBoss uses `AnimalSpawnPluginBridge`.

## Load order

```
harmony.load AnimalSpawn
harmony.load GrimmBoss
```

Startup autoload is enough for boss fights (SpawnAnimal is called at runtime). Unload Oxide `oxide/plugins/AnimalSpawn.cs` so you do not run two spawners, and so Shop keeps sole ownership of `animalspawn.horse`.

## Horse purchases

Shop Command products should use:

```text
shop.horse "Horse" %steamid%
```

`animalspawn.horse` remains a Shop compatibility alias. This mod does **not** register that command.

## Build

```powershell
.\.cursor\HarmonyMods\AnimalSpawn\build.ps1
```

Copies only `AnimalSpawn.dll` into `HarmonyMods/`.

## GrimmBoss boss JSON

Animal summons stay off until a boss profile sets Animal Ability cooldown not equal to `-1`, a type (`Wolf`, `Bear`, …), and count > 0. Wiring this mod only makes `SpawnAnimal` succeed when that ability runs.
