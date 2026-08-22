# DefendableHomes (Harmony port)

A near-verbatim Harmony port of the **DefendableHomes** Oxide plugin (KpucTaJl 1.2.2). Players throw a custom flare at their base to start a multi-wave NPC raid; GrimmNPC scientists attack the foundations. A CH47 drops a hackable crate on success.

The original plugin body is retained in `DefendableHomesPlugin.cs`; Oxide is replaced by a compatibility shim plus Harmony patches (same approach as **ArmoredTrain**).

## Identity

| Field | Value |
|-------|--------|
| **Mod DLL** | `HarmonyMods/DefendableHomes.dll` |
| **Harmony ID** | `com.facepunch.rust_dedicated.DefendableHomes` |
| **Entry point** | `DefendableHomes.DefendableHomesMod : IHarmonyModHooks` |
| **Target framework** | `net48` |
| **Config** | `HarmonyConfig/DefendableHomes.json` (same schema as the Oxide plugin) |
| **Data** | `HarmonyData/DefendableHomes.json` (player cooldowns) |
| **Images** | `HarmonyData/Images/` (copied from the original pack: `Tab_KpucTaJl.png`, `Clock_KpucTaJl.png`, `Npc_KpucTaJl.png`, `Foundation_KpucTaJl.png`). Also `HarmonyData/DefendableHomes/Images/` and `oxide/data/Images/`. Flare skin previews: `flare_easy.png` / `flare_medium.png` / `flare_hard.png` under `HarmonyData/DefendableHomes/Images/`. |

## Requirements / Dependencies

- **0GrimmNPC** (required for NPCs). `NpcSpawn.Call("SpawnNpc" / "AddTargetRaid" / "SetCurrentWeapon" / "SetParent")` is forwarded by reflection (`DefendableHomesGrimmNpc` / `NpcSpawnBridge`).
- **0Permissions** (optional, for `defendablehomes.defstop`).
- **TruePVE** (optional): publishes `DefendableHomes_CanEntityTakeDamage` / `DefendableHomes_CanEntityBeTargeted` so raid NPCs can damage the base (and players/turrets) and so auto turrets can shoot them. Rebuild TruePVE after this port so the CallHook chain includes DefendableHomes.
- **Economics** (optional Harmony mod): rewards via AppDomain `Economics_Plugin` / `Economics_ApiType`.
- Load order: `0Permissions` → `TruePVE` → `0GrimmNPC` → `DefendableHomes`.
- **Vanilla teams + clans:** `IsTeam` uses `RelationshipManager` and Facepunch `ClanManager` (`BasePlayer.clanId` / `serverClan`) when `clan.enabled` is on. Optional Harmony `Clans_ApiType` / `Friends_ApiType` still apply if those mods are loaded.

## Commands

| Command | Description |
|---------|-------------|
| `giveflare <skinOrDifficulty> [steamid] [amount]` | Server console / Shop: give a flare to a player (skin ID or `EASY` / `MEDIUM` / `HARD`). In-game admin chat: give a flare to yourself (`giveflare EASY 1`). Shop Command products: `giveflare 2888602635 %steamid%` (same skins as the default config). |
| `defstop` | Stop the event you are standing in (admin, or `defendablehomes.defstop` permission + event owner). |
| Config `CheckCommand` (default `checkfoundations`) | In-game: draw foundation validity for the cupboard you are looking at. |

## Harmony patches (Oxide hook map)

| Patch (game method) | Oxide hook(s) |
|---------------------|----------------|
| `BaseCombatEntity.Hurt(HitInfo)` prefix | `OnEntityTakeDamage(ScientistNPC)` |
| `BaseCombatEntity.Die(HitInfo)` postfix | `OnEntityDeath(ScientistNPC)` |
| `BasePlayer.Die(HitInfo)` prefix | `OnPlayerDeath` |
| `Planner.DoBuild(Target, Construction)` prefix | `CanBuild` |
| `BaseNetworkable.Kill` prefix | `OnEntityKill(BuildingBlock)` |
| `PlayerLoot.StartLootingEntity` prefix | `CanLootEntity(HackableLockedCrate)` |
| `Item.CanStack` prefix | `CanStackItem` |
| `Item.SplitItem` prefix | `OnItemSplit` |
| `DroppedItem.OnDroppedOn` prefix | `CanCombineDroppedItem` |
| `LootContainer.SpawnLoot` postfix | `OnLootSpawn` (custom flares in crates) |
| `ThrownWeapon.SetUpThrownWeapon` postfix | `OnExplosiveThrown` / `OnExplosiveDropped` (flare start) |
| `NPCPlayer.CreateCorpse` postfix | `OnCorpsePopulate` |
| `BaseNetworkable.Spawn` postfix | `OnEntitySpawned(HackableLockedCrate)` |
| `ConsoleSystem.Index.Server.Find(StringView)` postfix (manual) | command routing fallback |

GrimmNPC-originated hooks (`OnCustomNpcTarget`, `OnBomberExplosion`, `OnCustomNpcParentEnd`) are delivered through AppDomain `Harmony_CallHookList` from `0GrimmNPC`.

## Soft-disabled vs Oxide

- **Friends / GUIAnnouncements / Notify / ServerRewards / IQEconomic / XPerience** — skipped when absent. Team/clan sharing uses vanilla teams plus Facepunch `ClanManager`. Optional Harmony Clans/Friends mods still bind via AppDomain if present.
- `Interface.CallHook` is a no-op (no Oxide hook bus). Core event logic does not depend on it.
- Remote version check is disabled.

## Build / deploy

```powershell
# from .cursor/HarmonyMods/DefendableHomes
./build.ps1
```

Copies **only** `DefendableHomes.dll` to `HarmonyMods/DefendableHomes.dll`. Rebuild **0GrimmNPC** as well (public `AddTargetRaid` / `SetCurrentWeapon` + Harmony CallHook bus). If you use TruePVE, rebuild it so auto turrets can target event NPCs.

```
harmony.load 0GrimmNPC
harmony.load DefendableHomes
```
