# TruePVE (Harmony Mod)

Harmony mod that provides **game PvE** (using `server.pve`) plus **Prevent Looting** and **Loot Defender** behavior. Reads JSON config and patches game methods.

## Identity

- **Name:** TruePVE  
- **Type:** Harmony mod (load with `harmony.load TruePVE`)  
- **Config:** JSON file compatible with TruePVE.json structure (Prevent Looting, Loot Defender, PvE options)

## What the game already does (used by this mod)

When **PvE** is enabled in config, the mod sets:

- `ConVar.Server.pve = true` → blocks player-vs-player and player-vs-building damage (reflected to attacker), enables demolish, adds server tag/Steam key  
- `ConVar.Server.pveBulletDamageMultiplier` → scales player→NPC bullet damage  

The mod patches specific vanilla PvE edge cases where the stock checks are too broad for event gameplay.

## Patches

| Patch | Purpose |
|-------|--------|
| `PlayerLoot.StartLootingEntity` | Prevent Looting + Loot Defender: block looting when config denies (non-ally corpses/players/storage/backpacks) or when entity/position is locked by Loot Defender. |
| `BasePlayer.CanBeLooted` | Prevent Looting on player bodies. |
| `StorageContainer.CanBeLooted` | Prevent Looting on storage. |
| `GrowableEntity.TakeClones` | Protect planterboxes (clone pickup). |
| `GrowableEntity.PickFruit` | Protect planterboxes (fruit pick). |
| `BaseCombatEntity.OnAttacked` | Loot Defender: record damage (attacker, amount, weapon) per entity for Bradley/Heli/NPC. |
| `BaseCombatEntity.Die` | Loot Defender: on death of PatrolHelicopter, BradleyAPC, BaseNpc, BaseNPC2, apply position-based lock (radius + duration from config). |
| `BuildingBlock.Hurt` | PvE building damage handling per config, including player-owned projectiles/cannonballs while allowing ownerless event structures. |
| `BaseCombatEntity.Hurt` | PvE deployable fire/heat damage handling: blocks player-owned fire against another player's deployables while allowing ownerless event entities and self-owned deployables. |
| `BasePlayer.Hurt` | **Player vs player:** when **Enable game server.pve** is on in config *or* `server.pve` is true, damage from one player to another is applied to the **shooter** as generic damage (same as vanilla `server.pve`); the victim does not take that hit. **Sleeping:** optional full block (no reflect) when **Protect sleeping players** is on. **NPCs / Frankenstein pets:** still uses `_skipReflectTo` so reflected pet edge cases behave as before. |
| `AutoTurret.SetTarget` | **Turrets ignore players:** when **Player auto turrets ignore players** is on (Oxide `TurretsIgnorePlayers`), player-owned auto turrets cannot acquire real players as targets; NPCs, animals, and scientists remain valid. |
| `AutoTurret.TargetScan` | Clears an active player target when ignore-players rules apply. |
| `AutoTurret.ApplyDamage` | Blocks turret bullet damage to real players (including stray hits). |
| `BasePlayer.Hurt` (turret) | Blocks damage when `HitInfo.Initiator` is an auto turret that must ignore players. |

## Config

- **Paths tried (first found):** `HarmonyConfig/TruePVE.json`, `Config/TruePVE.json`, `TruePVE.json` (server root).  
- **When no config exists:** The mod creates a default config at `HarmonyConfig/TruePVE.json` (creating the folder if needed).  
- **Structure:** **Prevent Looting**, **Loot Defender**, and **PvE** sections (see TruePVEConfig.cs).  
- **Prevent Looting:** `Enabled`, `Allow Looting Players/Corpses/Storage Containers`, `Allow Looting Sleepers`, `Use Teams For Allies`, `Respect Cupboard Authorization`, `Protect Planterboxes`, `Can Loot Backpack`, `Admins Can Always Loot`, `Debug Logging`, `Excluded ShortPrefabNames`, `Exclude Entities`.  
- **PvE:** `Enable game server.pve`, `PvE bullet damage multiplier`, `Protect sleeping players` (when true, sleepers take no player damage and no reflection), `Player auto turrets ignore players`, `Static/monument auto turrets ignore players`, `Safe zone NPC auto turrets ignore players`.  
- **Loot Defender:** `Enabled`, `Lock Bradley/Heli/NPC`, `Lock Radius`, `Lock Duration`, thresholds and lock times per type, `Group By Team`, `Allow Allies`, `Block Looting Only`, Hackable crates options.

## Build / deploy

1. **Recommended:** run `build.ps1` in `.cursor/HarmonyMods/TruePVE/` (finds `RustDedicated_Data\Managed`, builds, copies to `HarmonyMods/TruePVE.dll`).  
2. Or `dotnet build` with an explicit managed path if your server lives elsewhere:  
   `dotnet build .cursor/HarmonyMods/TruePVE/TruePVE.csproj -c Release -p:RustManaged="D:\!RustServer\RustDedicated_Data\Managed"`  
   Requires `Assembly-CSharp.dll` under that folder (install/update the dedicated server first).  
3. Place `TruePVE.json` in one of the config paths above, or let the mod create a default at `HarmonyConfig/TruePVE.json` on first load.

## Source layout (this repo)

| Path | Role |
|------|------|
| `TruePVE.csproj` | Single net48 project |
| `GlobalUsings.cs` | `Object` → `UnityEngine.Object` (decompiler-safe) |
| `TruePVE/` | Config, mod entry, `LootDefenderState`, options |
| `TruePVE.Patches/` | Harmony patches (`namespace TruePVE.Patches`) |
| `Properties/AssemblyInfo.cs` | Assembly version |

## Config vs Oxide TruePVE

**HarmonyConfig/TruePVE.json** includes every option the mod reads: all keys under **Prevent Looting**, **Loot Defender**, and **PvE** (see `TruePVEConfig.cs`). No config keys are missing for current behavior.

**Intentionally not ported** (Oxide plugin has these; this mod does not):

- **PvE engine:** RuleSets, Mappings, Schedule, Entity Groups, Configuration Options (reflect multipliers, twig damage, height limits, etc.). The mod uses the **game’s built-in PvE** (`server.pve`) instead of a custom rule engine.
- **Sleeper / damage extras:** Allow Killing Sleepers (and variants), Ignore Firework/Campfire/Ladder damage, trap/turret topology, block scrap heli/wallpaper/decay, etc. Not implemented; could be added with more patches if desired.
- **Oxide-only features:** Supply Drop Settings, Kill Notifications, Loot Defender UI/Discord/XP/ShoppyStock/permissions/lockouts, Prevent Looting “Use Permissions” / Zone Manager. These depend on Oxide or heavy new code; the mod stays Oxide-free.

**Possible future mod options** (would require new patches): e.g. Remove Fire From Crates, Heli Unlock When X Distance From Owner, per-type lock-time overrides already in config.

## Reference

- **Game PvE vs TruePVE:** See `Harmony-Assembly/HARMONY_MODS_GUIDE.md` → “TruePVE as a Harmony Mod”.  
- **Config alignment:** Same section; config keys match TruePVE.json for Prevent Looting and Loot Defender.
