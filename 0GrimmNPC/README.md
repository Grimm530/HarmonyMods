# 0GrimmNPC (NpcSpawn Harmony port)

Harmony mod that is an **NpcSpawn 3.3.04** port. Previous GrimmNPC sources were moved to `GrimmNPCold/`.

## Identity

| Field | Value |
|-------|--------|
| **DLL / load name** | **0GrimmNPC** (`0` prefix loads before ArmoredTrain / Convoy / ZombieHorde) |
| **Source folder** | `.cursor/HarmonyMods/0GrimmNPC/` |
| **C# type / namespace** | `GrimmNPC.GrimmNPC` (unchanged for consumers) |
| **AppDomain keys** | `GrimmNPC.Type`, `GrimmNPC.Instance` |
| **Config** | `HarmonyConfig/GrimmNPC.json` (migrates from `legacy/config/NpcSpawn.json` if present) |
| **Data** | `HarmonyConfig/NpcSpawn/` (Preset, NavMesh) |
| **Log tag** | `[GrimmNPC]` |

## What changed vs original NpcSpawn (only Harmony necessities)

- `RustPlugin` → `GrimmNPC : IHarmonyModHooks` (`OnLoaded` / `OnUnloaded`)
- Config/data under `HarmonyConfig/` instead of `legacy/config` + `oxide/data`
- Harmony hooks → Harmony patches in `HarmonyHooksPatches.cs`
- `timer.Once` / `Puts` / `PluginReference` / `HarmonyModInterface.CallHook` → `HarmonyCompat` shims
- Commands `npccount` / `npcdiag` registered via `ConsoleSystem`
- Swim patches no longer defer to a separate GrimmNPC assembly (this mod *is* GrimmNPC)

**Unchanged:** `CustomScientistNpc`, `CustomScientistBrain`, spawn pipeline, states, targeting, weapons, public API (`SpawnNpc`, `SpawnPreset`, `GetJObject`, …).

## Build / deploy

```powershell
.\build.ps1
```

Copies `0GrimmNPC.dll` only into server `HarmonyMods/` and removes legacy `GrimmNPC.dll` if present.

```
harmony.load 0GrimmNPC
```

## Consumers

- Resolve via AppDomain `GrimmNPC.Type` / `GrimmNPC.Instance` or type `GrimmNPC.GrimmNPC` — not by DLL filename.
- Call **`SpawnNpc(position, NpcConfig|JObject)`** (NpcSpawn API), not the old `RegisterPending` / `CustomNpcData` API from GrimmNPCold.
- Skin ID for custom NPCs remains `11162132011012`.
- Optional plugins: **Kits** binds the Harmony Kits mod via AppDomain `Kits_ApiType` (`GiveKit` / `IsKit`) — load `Kits.dll` for NPC `Config.Kit`. Friends / Clans still resolve via Oxide reflection when Oxide is present.

## Note on Convoy / BossMonster

Mods/plugins that still bind `GrimmNPC.RegisterPending` + `CustomNpcData` need updating to `SpawnNpc` (or keep using `GrimmNPCold` until migrated).
