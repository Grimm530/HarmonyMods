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
| **Requires** | `0GrimmNPC.dll` loaded first |

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
harmony.load 0GrimmNPC
harmony.load ZombieHorde
```

## Permissions

Requires **0Permissions** (`0Permissions.dll`). No need to reload 0Permissions after loading ZombieHorde.

**Prefer the space form** (0Permissions). Dotted `perm.grant` is Oxide’s alias and only works after Oxide registration succeeds:

```text
perm grant user 7656119XXXXXXXXXX zombiehorde.ignore
perm.grant user 7656119XXXXXXXXXX zombiehorde.ignore
```

| Permission | Effect |
|------------|--------|
| `zombiehorde.admin` | Chat/console `/horde` + `horde` commands (server admins also pass this check) |
| `zombiehorde.ignore` | Zombies will not target this player |
| `zombiehorde.ignoreuntilhurt` | Ignored until the player damages a zombie |

Verify: `perm show user <steamid>` or `oxide.show user <steamid>` should list `zombiehorde.ignore` after grant.

On load you should see both:
- `[ZombieHorde] Linked to 0Permissions ...`
- `[ZombieHorde] Linked to Oxide Permission library (perm.grant).`

## Commands

| Command | Who | Purpose |
|---------|-----|---------|
| `/horde` | admin (`zombiehorde.admin`) | Chat: info / tpto / destroy / create / createspawn / createloadout / hordecount / membercount |
| `/hordeinfo` | all | Show active hordes (cached summary) |
| `horde` | console / RCON / F1 | Console: info / destroy / create / **addloadout** / hordecount / membercount |
| `hordeinfo` | console | Broadcast horde summary |

**Console notes (Oxide parity):**
- `horde create [distance] [profile]` spawns at a **random** valid point (bypasses max horde limit)
- `horde destroy <number>` uses **1-based** indices
- `horde addloadout <kit> [kit…]` requires Harmony **Kits** (`Kits_Plugin` / `GetKitInfo`) or Oxide Kits
- Chat `/horde create` still spawns near the player; `/horde createloadout` copies inventory (console uses kits via `addloadout`)

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
