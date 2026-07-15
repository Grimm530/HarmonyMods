# ZombieHorde (Harmony mod)

Harmony port of **ZombieHorde** 0.6.351. Spawns via **GrimmNPC** reflection (`SpawnNpc` + `NpcConfig`). No Oxide.Ext.ChaosNPC / Oxide.Core references.

## Identity

| Field | Value |
|-------|--------|
| **Name** | ZombieHorde |
| **Source** | `.cursor/Oxide.Plugins.Cant-Use/ZombieHorde.cs` |
| **Type** | Harmony mod (`IHarmonyModHooks`) |
| **Entry** | `ZombieHorde.ZombieHordeHarmonyEntry` |
| **Config** | `HarmonyConfig/ZombieHorde.json` (migrates from `oxide/config/ZombieHorde.json`) |
| **Requires** | `GrimmNPC.dll` loaded first |

## Architecture

- **Spawn:** `GrimmNpcBridge` → GrimmNPC `SpawnNpc` → attach `ZombieNPC` MonoBehaviour
- **AI:** GrimmNPC states (`RoamState` / `ChaseState` / `CombatState` / optional Raid)
- **Horde logic:** Ported `Horde` + leader tick on `ZombieNPC`
- **RaidingZombies:** Built-in (Oxide 3.2.1) — picks raid hordes + TC scan; **raid AI is GrimmNPC** (`RaidState` / `RaidStateMelee`). Does not `AddState(Cooldown)` (avoids duplicate-state spam)
- **Config:** Identical JsonProperty names to Oxide config
- **Hooks:** Harmony patches for damage, death, turrets/APC/heli, NPC/animal target, safe zone, corpse loot, dud explosives, gather sensations, chat

## Build / deploy

```powershell
.\build.ps1
```

Copies **only** `ZombieHorde.dll` into server `HarmonyMods/`.

```text
harmony.load GrimmNPC
harmony.load ZombieHorde
```

## Commands

| Command | Who | Purpose |
|---------|-----|---------|
| `/horde` | admin | Create/destroy/info/teleport/loadouts |
| `/hordeinfo` | all | Show active hordes |
| `horde` | console | Admin console variant |

## Project structure

| File | Role |
|------|------|
| `ZombieHordeHarmonyEntry.cs` | `IHarmonyModHooks` |
| `ZombieHordePlugin.cs` | Lifecycle, hooks, commands, config |
| `Horde.cs` | Horde + SpawnOrder |
| `ZombieMember.cs` | `ZombieNPC` MonoBehaviour wrapper |
| `GrimmNpcBridge.cs` | Reflection spawn (Convoy pattern) |
| `RaidingZombies.cs` | Oxide RaidingZombies 3.2.1 (C4/rockets / TC raid) |
| `ChaosStubs.cs` | NPCSettings / Sense / NavmeshSpawnPoint / SimpleGrid |
| `ConfigData.cs` | Config (JsonProperty-compatible) |
| `Compat.cs` | Timer / permission / lang / console |
| `Patches/*.cs` | Harmony hooks |

## Known gaps vs Oxide + ChaosNPC

- Custom `ZombieNPCBrain` states replaced by GrimmNPC roam/chase/combat/raid (encrypted ChaosNPC engine unavailable)
- `SimpleGrid` instead of Facepunch `Spatial.Grid` (same cell/world size semantics)
- Gen2 animal (`BaseNPC2`) targeting not separately patched (classic `BaseNpc` is)
