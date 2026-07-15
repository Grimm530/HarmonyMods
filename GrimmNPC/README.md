# GrimmNPC (NpcSpawn Harmony port)

Harmony mod that is an **NpcSpawn 3.3.04** port. Previous GrimmNPC sources were moved to `GrimmNPCold/`.

## Identity

| Field | Value |
|-------|--------|
| **Name** | GrimmNPC |
| **Source** | `.cursor/Oxide.Plugins.Cant-Use/NpcSpawn.cs` |
| **Type** | Harmony mod (`IHarmonyModHooks`) |
| **Config** | `HarmonyConfig/GrimmNPC.json` (migrates from `oxide/config/NpcSpawn.json` if present) |
| **Data** | `HarmonyConfig/NpcSpawn/` (Preset, NavMesh) |

## What changed vs Oxide NpcSpawn (only Harmony necessities)

- `RustPlugin` → `GrimmNPC : IHarmonyModHooks` (`OnLoaded` / `OnUnloaded`)
- Config/data under `HarmonyConfig/` instead of `oxide/config` + `oxide/data`
- Oxide hooks → Harmony patches in `OxideHooksPatches.cs`
- `timer.Once` / `Puts` / `PluginReference` / `Interface.CallHook` → `OxideCompat` shims
- Commands `npccount` / `npcdiag` registered via `ConsoleSystem`
- Swim patches no longer defer to a separate GrimmNPC assembly (this mod *is* GrimmNPC)

**Unchanged:** `CustomScientistNpc`, `CustomScientistBrain`, spawn pipeline, states, targeting, weapons, public API (`SpawnNpc`, `SpawnPreset`, `GetJObject`, …).

## Build / deploy

```powershell
.\build.ps1
```

Copies `GrimmNPC.dll` only into server `HarmonyMods/`.

## Consumers

- Call **`SpawnNpc(position, NpcConfig|JObject)`** (NpcSpawn API), not the old `RegisterPending` / `CustomNpcData` API from GrimmNPCold.
- Skin ID for custom NPCs remains `11162132011012`.
- Optional plugins: **Kits** binds the Harmony Kits mod via AppDomain `Kits_ApiType` (`GiveKit` / `IsKit`) — load `Kits.dll` for NPC `Config.Kit`. Friends / Clans still resolve via Oxide reflection when Oxide is present.

## Note on Convoy / BossMonster

Mods/plugins that still bind `GrimmNPC.RegisterPending` + `CustomNpcData` need updating to `SpawnNpc` (or keep using `GrimmNPCold` until migrated).
