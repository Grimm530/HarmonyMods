# GrimmBoss (Harmony port)

Near-verbatim Harmony port of the **GrimmBoss** Oxide plugin (v2.4.9). The original plugin body
(`ControllerBoss`, spawn position system, AOE abilities, loot, economy/alerts) lives in
`GrimmBossPlugin.cs`; an Oxide compat shim + Harmony patches drive it instead of the Oxide runtime.

Bosses and helper NPCs spawn through **0GrimmNPC** — the Harmony port of Oxide **NpcSpawn**
(`SpawnNpc(Vector3, JObject|NpcConfig)`). Do not use the older RegisterPending-based
`.cursor/HarmonyMods/GrimmNPC` for this mod.

## Identity

- **Mod DLL:** `HarmonyMods/GrimmBoss.dll`
- **Harmony ID:** `com.facepunch.rust_dedicated.GrimmBoss`
- **Entry point:** `GrimmBoss.GrimmBossMod : IHarmonyModHooks`
- **Target framework:** `net48`

## Requirements

- **0GrimmNPC** (hard requirement). DLL name starts with `0` so it loads before consumers.
  Resolve type `GrimmNPC.GrimmNPC` / AppDomain `GrimmNPC.Instance`. Source:
  `.cursor/HarmonyMods/0GrimmNPC/`.
- **0PveMode** (optional) — `"Use the PVE mode of the plugin?"` in config.
- **Kits** (optional Harmony Kits mod) — kit loadouts via `GiveKit`.
- Soft-null optional Oxide plugins: Economics, ServerRewards, IQEconomic, XPerience,
  GUIAnnouncements, DiscordMessages, Notify, AnimalSpawn.

## Commands

Admin only (chat / F1 / server console where noted):

| Command | Description |
| --- | --- |
| `worldpos` | Print current world position (player). |
| `savepos <boss name>` | Save monument-relative spawn point for a boss. |
| `custompos <boss name>` | Save world-space CustomMap position (`Global.json`). |
| `spawnboss <boss name>` | Player: spawn at feet. Console: spawn via normal position logic. |
| `killboss <boss name>` | Server console only — kill all live instances of that boss. |

## Config & Data

- **Config:** `HarmonyConfig/GrimmBoss.json`
- **Boss profiles:** `HarmonyData/GrimmBoss/Bosses/*.json`
- **Custom map points:** `HarmonyData/GrimmBoss/CustomMap/` (e.g. `Global.json`)
- Legacy `HarmonyData/BossMonster/` folders are still read if present.

## Build

```powershell
# from .cursor/HarmonyMods/GrimmBoss
./build.ps1
```

Copies **only** `GrimmBoss.dll` into `HarmonyMods/`.

## Load order

```
harmony.load 0GrimmNPC
harmony.load GrimmBoss
```

Or rely on startup autoload (`0GrimmNPC.dll` sorts before `GrimmBoss.dll`).

## Harmony patches

| Patch | Oxide hook(s) |
| --- | --- |
| `BaseCombatEntity.Hurt(HitInfo)` prefix | `OnEntityTakeDamage` (player / ScientistNPC / BaseAnimalNPC) |
| `BasePlayer.Die(HitInfo)` postfix | `OnPlayerDeath` |
| `NPCPlayer.CreateCorpse` postfix | `OnCorpsePopulate` |

AlphaLoot-only hooks (`CanPopulateLoot`, `OnCustomLootNPC`) remain in the plugin body but are not
patched here — use TypeLootTable modes that GrimmBoss fills itself (Own / Prefab / Combined).
