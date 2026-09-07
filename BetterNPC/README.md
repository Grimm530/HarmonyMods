# BetterNPC (Harmony port)

A near-verbatim Harmony port of the **BetterNpc** Oxide plugin (KpucTaJl 2.2.7). Spawns custom NPCs at monuments and cargo via GrimmNPC presets.

## Identity

| Field | Value |
|-------|--------|
| **Mod DLL** | `HarmonyMods/BetterNPC.dll` |
| **Harmony ID** | `com.facepunch.rust_dedicated.BetterNpc` |
| **Entry point** | `BetterNpc.BetterNpcMod : IHarmonyModHooks` |
| **Target framework** | `net48` |
| **Config** | `HarmonyConfig/BetterNpc.json` |
| **Data** | `HarmonyData/BetterNpc/` (Monument/, Custom/, Event/, Road/, Biome/, etc.) |

## Requirements / Dependencies

- **0GrimmNPC** (required for NPC spawning). `NpcSpawn.Call(...)` is forwarded by reflection (`BetterNpcGrimmNpc` / `NpcSpawnBridge`): SpawnNpc, SpawnPreset, SetParent, IsStationaryPreset, GetAreaMask, GetSpawnPoint, GetRoadSpawnPoint, RegisterPresetUsage, UnregisterPresetUsage, GetJObject.
- **0Permissions** (optional, for admin commands).
- **0PveMode** (optional): `PveMode.Call("ScientistAddPveMode", npc)` when config `Pve` is enabled.
- **Economics** (optional Harmony mod): reward deposits via AppDomain `Economics_Plugin` / `Economics_ApiType`.
- Load order: `0Permissions` → `0GrimmNPC` → `BetterNPC`.
- **Soft-fail:** mod loads even if GrimmNPC is absent; `OnServerInitialized` logs errors and skips spawning when NpcSpawn is unavailable.

## Commands

Registered automatically from `[ChatCommand]` / `[ConsoleCommand]` attributes on the ported plugin:

| Chat | Console | Description |
|------|---------|-------------|
| `SpawnPointAdd` | `SpawnPointCreate` | Manage custom spawn points |
| `SpawnPointPos` | `SpawnPointDestroy` | Position / destroy spawn points |
| `SpawnPointAddPos` | `ShowAllNpc` | Add positions / debug NPCs |
| `SpawnPointRemovePos` | `ShowFailedNavMesh` | Remove positions / navmesh debug |
| `SpawnPointShowPos` | `ShowAllMonumentMarkers` | Visualize spawn data |
| `SpawnPointReload` | `ConvertNpcSpawn` | Reload configs / convert presets |
| `ShowAllZones` | | Zone visualization |
| `ShowFailedNavMesh` | | Navmesh failure debug |
| `TeleportToSpawnPoint` | | Teleport to a spawn point |

## Harmony patches

| Patch (game method) | Hook(s) |
|---------------------|---------|
| `NPCPlayer.CreateCorpse` postfix | `OnCorpsePopulate` |
| `BaseNetworkable.Spawn` postfix | `OnEntitySpawned(HumanNPC, ScientistNPC2, CargoShip, HackableLockedCrate)` |
| `BaseNetworkable.Kill` prefix | `OnEntityKill(CargoShip, HackableLockedCrate, SupplyDrop, LockedByEntCrate)` |
| `BaseCombatEntity.Die(HitInfo)` postfix | `OnEntityDeath(BradleyAPC, PatrolHelicopter)` |
| `CargoShip.SpawnCrate` postfix | `OnCargoShipSpawnCrate` |
| `CargoShip.OnArrivedAtHarbor` postfix | `OnCargoShipHarborArrived` |
| `BradleyAPC.CanDeployScientists` prefix | `CanDeployScientists` |
| `ConsoleSystem.Index.Server.Find(StringView)` postfix (manual) | command routing fallback |

GrimmNPC-originated hooks (`OnNpcSpawnInitialized`, `OnNpcSpawnPresetRename`) are delivered through AppDomain `Harmony_CallHookList`.

## Soft-disabled vs Oxide

- **ServerRewards / IQEconomic / XPerience** — skipped when absent.
- `HarmonyModInterface.CallHook` is a no-op.
- `Interface.Oxide.UnloadPlugin` is a soft no-op (mod keeps running).
- Remote version check should be disabled in the ported plugin.

## Build / deploy

```powershell
# from .cursor/HarmonyMods/BetterNPC
./build.ps1
```

Copies **only** `BetterNPC.dll` to `HarmonyMods/BetterNPC.dll`.

```
harmony.load 0GrimmNPC
harmony.load BetterNPC
```

## Data layout

| Path | Role |
|------|------|
| `HarmonyData/BetterNPC/` | **Live** spawn points only (currently Launch, Airfield, Harbors, Oil Rigs, CargoShip) |
| `HarmonyData/BetterNPC_Unloaded/` | Archived monument/event/road/biome/tunnel JSON kept for later |
| `HarmonyConfig/NpcSpawn/Preset/` | NpcSpawn preset definitions used by GrimmNPC |

To enable another monument later: move its JSON from `BetterNPC_Unloaded/...` back into the matching folder under `BetterNpc/`, set `"Enabled? [true/false]": true`, then `harmony.load BetterNPC`.

## AI sleep / dormancy

BetterNPC only **spawns** monument NPCs. Sleep/dormancy is owned by **0GrimmNPC** (CanSleep / SleepDistance on presets, plus ForceRespectAiDormant + DefaultSleepDistance in HarmonyConfig/GrimmNPC.json). Target: NPCs AI-dormant when no player is within **200 m**. Reload with harmony.unload 0GrimmNPC then harmony.load 0GrimmNPC (BetterNPC after).
