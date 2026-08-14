# SkillTree Harmony Mod

Port of SkillTree 1.7.14 (imthenewguy / Grimm530) to the Oxide-free Harmony-first stack.

## Identity

| | |
|---|---|
| Assembly | `SkillTree.dll` |
| Namespace | `SkillTreeHarmony` / `Oxide.Plugins.SkillTree` (partial) |
| Entry point | `SkillTreeHarmony.SkillTreeMod : IHarmonyModHooks` |

## Paths

| Resource | Path |
|---|---|
| Config | `HarmonyConfig/SkillTree.json` |
| Shared data (PCDDATA) | `HarmonyData/SkillTree.json` |
| Default player data dir | `HarmonyData/SkillTree/` |
| Custom player data dir | Configured via `CustomSkillTreeDataDirectory` in config (currently `C:\!DataPersistence\oxide\data\SkillTree`) |
| Logs | `HarmonyData/SkillTree/logs/` |
| Language overrides | `HarmonyLanguage/SkillTree.json` (optional) |

The `CustomSkillTreeDataDirectory` is already set in the live config to the DataPersistence path, so player skill data writes there automatically.

## Build

```powershell
cd c:\!2XRUST\.cursor\HarmonyMods\SkillTree
.\build.ps1
```

Output: `c:\!2XRUST\HarmonyMods\SkillTree.dll`

Set `RUST_MANAGED_PATH` or `RUST_SERVER_ROOT` env vars if the auto-detected paths are wrong.

## Load Order

Facepunch loads `HarmonyMods/*.dll` alphabetically (filesystem order). Typical order here:

`MovementSpeed` → `Permissions` → `SkillTree`

**Do not rely on a manual load sequence.** SkillTree binds via ready callbacks:

- `Permissions_ReadyCallbacks` → re-register skilltree.* permissions
- `MovementSpeed_ReadyCallbacks` → re-resolve MovementSpeed + re-apply Road Runner / Swim Speed for online players

## Commands

### Chat (players)
| Command | Description |
|---|---|
| `/st` `/skilltree` `/skills` | Open skill tree menu |
| `/score` `/scoreboard` | Open XP scoreboard |
| Various config-driven commands | See config `chat_commands` section |

Undotted chat aliases (`st`, `skilltree`, `skills`, `score`, `scoreboard`, plus config-driven names) are registered as **unreplicated** server console commands. Player chat (`/st`, `/setgenes`, …) is handled by `ChatSayBridge` on `chat.say`. They must **not** be added to `Index.Server.Replicated` — clients have no ConsoleGen entries and spam `Replicated convar not found on client: global.setgenes` (etc.) on join. UI/console handlers stay unreplicated.

### Console (admin)
| Command | Description |
|---|---|
| `ST_UI <args>` | CUI callback (internal) |
| Config-driven console commands | Registered from plugin cmd calls |

## Architecture

```
SkillTree.dll
  SkillTreeHarmony.SkillTreeMod        IHarmonyModHooks entry point
  SkillTreeHarmony.ModRunner           MonoBehaviour: NextTick + coroutines
  SkillTreeHarmony.PermissionsBridge   Reflection bridge to PermissionsHarmony
  Oxide.Plugins.SkillTree (partial x2)
    SkillTreePlugin.cs                 Original plugin body (untouched)
    SkillTreeDispatch.cs               Lifecycle + Dispatch_* static methods
  OxideCompat.cs                       Oxide.Core / Oxide.Plugins shims
  RustCui.cs                           Oxide.Game.Rust.Cui shims
  Patches/                             Harmony patches -> Dispatch_* calls
```

## Optional Integrations

ImageLibrary, Economics, ServerRewards, RaidableBases, ZoneManager, and other optional plugins are resolved via AppDomain at runtime. If absent, SkillTree degrades gracefully (no images, no economy respec, etc.).

## 1.7.14 highlights (vs 1.7.12 / prior Harmony 1.7.122 port)

- Prestige History UI; Cooking coop skills (Clever Incubator / Soft Touch / Factory Farmer)
- Underwater: Frugal Wrighter, Heated Shot, Wind Catcher
- Raiding Strategist (siege damage); Harvesting Replenish; Team Friendly Fire
- Recycler buffs via nested `GetRecyclerStats` Harmony postfix (August wipe API)
- Heli XP via `OnPatrolHelicopterTakeDamage` (wired in Hurt patch + Dispatch)
- Preserved port adaptations: `CustomSkillTreeDataDirectory`, OnFuelConsume NRE fix, scoreboard `TryParse`, `WoundingTick` medkit chance

## Yield / gather patches (must match Oxide timing)

SkillTree yield buffs mutate the live `Item` (or `itemList`) **before** `GiveItem`. Broken postfix/`item=null` ports silently no-op because `HandleDispenser` returns on `item == null`.

| Oxide hook | Game site | Patch |
|---|---|---|
| `OnDispenserGather` | `ResourceDispenser.GiveResourceFromItem` after `ItemManager.CreateByItemID` | Transpiler → `Dispatch_OnDispenserGather(dispenser, player, item)` |
| `OnDispenserBonus` | `ResourceDispenser.AssignFinishBonus` after `ItemManager.Create` | Transpiler → `Dispatch_OnDispenserBonus(dispenser, player, item)` |
| `OnCollectiblePickup` | `CollectibleEntity.DoPickup` start (mutates `itemList`) | Prefix |
| `OnGrowableGathered` | `GrowableEntity.GiveFruit(player, amount, applyCondition, eat)` after Create | Transpiler → `Dispatch_OnGrowableGathered(plant, item, player)` |
| `CanTakeCutting` | `GrowableEntity.TakeClones` (bonus clones side-effect; returns null) | Prefix |
| `OnEntityDeath` (barrels / animals / etc.) | `BaseCombatEntity.Die` **before** `OnDied`/`DropItems` | Transpiler → `Dispatch_OnEntityDeath` |
| `OnEntityDeath` (ore nodes) | `ResourceEntity.OnDied` **before** `Kill` | Transpiler → `Dispatch_OnEntityDeath` |
| `OnPlayerDeath` | `BasePlayer.Die` CallHook (cancelable) | Transpiler → `Dispatch_OnPlayerDeathHook` |

**Loot Magnet timing:** Oxide `OnEntityDeath` runs while barrel inventory is still full. A Die **Postfix** is too late (`IsDestroyed` early-out + loot already on the ground). InstantBarrel does the same inventory→player move by intercepting `OnAttacked` earlier; SkillTree magnet still needs the death-hook timing when InstantBarrel does not handle the hit.

## Perk hook wiring (`Patch_PerkHooks.cs`)

Many buffs had `Dispatch_*` stubs with **no Harmony callers** (dead copies). Callers now replace Oxide `Interface.CallHook` at the game site (or Prefix where cancel/`ref` result is needed).

Previously dead / miswired (now fixed): Free_Bullet_Chance, Extended_Mag, Research_Refund, Lock_Picker, Recycler_Speed/Efficiency, Extra_Fish / Fishing_Luck, Vehicle_Mechanic, Mining/Woodcutting Hotspot, Double_Bandage_Heal, food/tea stack (Rationer/Iron_Stomach/Tea_*), Rocket_Velocity, Dudless_Explosive, scientist kill XP, Node_Spawn_Chance, OnBonusItemDropped magnet, metal-detector dig XP, flyhack Roadrunner, Bear OnNpcTarget, Build_Craft card swipe, **Loot Magnet** (`Loot_Pickup` via `OnEntityDeath` — must replace `Die` CallHook, not Postfix after `DropItems`/`IsDestroyed`).

Still external: **ZoneManager NoSkillZones** (needs ZoneManager enter/exit bridge → `Dispatch_OnEnterZone` / `OnExitZone`).

On load, watch for `[SkillTree] CallHookReplace: did not find '…'` — means a Rust IL change broke that hook replace.

## Known Compile Risks

- `Patch_MiscGameHooks.cs`: Several game methods (`AntiHack.ReportViolation`, `ResearchTable.ResearchPrice`, `ScientistNPC.CanTargetEntity`, `BaseMelee.ServerUse`) use internal method names that may differ across Rust updates. If any patch class fails to compile, comment it out and add to a `[HarmonyPatch]` manually or remove from `.csproj`.
- `Patch_ItemCrafter.cs`: `ItemCrafter.CraftItem` postfix applies `Craft_Speed`. `FinishCrafting` replaces Oxide `OnItemCraftFinished` **before** `GiveItem` (a method postfix is too late — stacking zeros `item.amount` and `Craft_Duplicate` logs `Creating item with less than 1 amount!`). `ItemManager.Create` prefix retags that Facepunch amount error as `[SkillTree]` when SkillTree is the caller. `RepairBench.RepairAnItem` prefix applies `MaxRepair` / `Free_Repairs`.
- `Patch_MiscGameHooks.cs` `BaseNetworkable_Spawned_Patch`: May be high-frequency. Monitor performance on busy servers.
- Fish bait/bite/tension apply stay on internal plugin Harmony patches; catch/luck/XP use `Patch_PerkHooks`.
