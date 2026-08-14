# TruePVE (Harmony Mod)

A near-verbatim Harmony port of the **Oxide TruePVE 2.4.21** plugin (by Nivex, based on
ignignokt84's original). It runs the full Oxide RuleSet engine as a Rust Harmony mod with
**no Oxide runtime dependency** - the Oxide APIs it needs are provided by an in-repo shim
(`OxideCompat.cs`).

## Identity

- **Name:** TruePVE
- **Version:** 2.4.21 (Oxide port)
- **Type:** Harmony mod - load with `harmony.load TruePVE`
- **Config:** `HarmonyConfig/TruePVE.json`
- **Data:** `HarmonyData/TruePVE/` (lockouts, mappings state, etc. when used)
- **Permissions:** Harmony `0Permissions` mod (`Permissions_ApiType` + `PermissionsBridge`)
- **Economics / RustRewards:** Harmony mods via AppDomain `Economics_ApiType` / `RustRewards_ApiType`
  (and `*_Plugin` Call wrappers). Loot Defender currency falls back to `Economics.Deposit` when
  ShoppyStock is absent. XP still prefers SkillTree when loaded.

## server.pve browser tag (hybrid)

Vanilla `server.pve` is what puts the **PVE** tag on Steam / the server browser. Oxide TruePVE
normally wants it off because vanilla PvP/building reflect early-outs before TruePVE's damage
hook runs.

This port supports a hybrid (default **on**):

- Config: `"Use Game server.pve For Server Browser Tag": true` under Configuration Options
- Sets `ConVar.Server.pve = true` for listing
- Temporarily suppresses vanilla reflect inside `BasePlayer.Hurt` / `BuildingBlock.Hurt` while
  TruePVE `handleDamage` is active, so **RuleSets still own damage**

Set the option to `false` if you want classic Oxide behavior (`server.pve` off).

## What this port supports

The full TruePVE feature set from `HarmonyConfig/TruePVE.json`:

- **RuleSets** - named collections of allow/deny rules with a default ruleset and per-ruleset flags.
- **Rule/Entity Groups** - group prefabs/types and reference them in rules.
- **Mappings** - map ZoneManager zones / event names to rulesets.
- **Schedule** - time-based ruleset switching.
- **Loot Defender** - locks Bradley / Heli / NPC loot + damage to the player/team that earned it,
  with lockouts, bypass permissions, and optional UI.
- **Prevent Looting** - block looting of players/corpses/backpacks/storage per permission and rules.
- **Supply Drop** settings, **Kill Notifications**, and the various **Configuration Options**
  (reflect damage, twig damage, sleeper handling, trap/turret targeting, MLRS, sprays, etc.).

Config is read into the plugin's `Configuration` object **before** `Init()` runs, exactly like Oxide.
The user's existing `HarmonyConfig/TruePVE.json` is preserved (missing keys are added on save-back,
values are never reset).

## Architecture (how the Oxide plugin runs under Harmony)

| File | Role |
|------|------|
| `TruePVEPlugin.cs` | The Oxide plugin body, copied near-verbatim (`public partial class TruePVE : RustPlugin`). Only lifecycle/visibility edits were made. |
| `TruePVEDispatch.cs` | Same partial class: static instance accessors (`SetInstance`/`GetModInstance`/`ClearInstance`), lifecycle wrappers (`CallInit`/`CallOnServerInitialized`/`CallUnload`), soft `PluginReference` binding, and `Dispatch_*` methods invoked by the patch files. |
| `OxideCompat.cs` | Oxide API shim: `Interface.Oxide`, `RustPlugin`, `Timer`, `Permission`, `Lang`, `DynamicConfigFile` (resolves `HarmonyConfig`/`HarmonyData`), covalence `IPlayer`/`IPlayerManager`, `AddCovalenceCommand`, `SendReply`, `Subscribe`/`Unsubscribe`/`IsSubscribed`, `VersionNumber`, etc. |
| `TruePVEMod.cs` | `IHarmonyModHooks` entry point: `ModRunner` (NextTick + coroutine host), constructs the plugin, loads config + default messages, waits for `ServerMgr`, calls `Init` -> `OnServerInitialized`, registers chat/console commands from the covalence command lists, re-registers permissions when the Permissions mod becomes ready, and tears everything down on unload. |
| `PermissionsBridge.cs` | Reflective bridge to the `PermissionsHarmony.PermissionsMod` (register permissions, query user perms, ready callback). |
| `RustCui.cs` | Oxide `Oxide.Game.Rust.Cui` CUI helper (for Loot Defender / lockout UI). |
| `Patches/*.cs` | Harmony patches that translate vanilla game methods into Oxide hook calls via the `Dispatch_*` methods. |
| `GlobalUsings.cs` | `Object` -> `UnityEngine.Object`, `Timer` -> `Oxide.Core.Libraries.Timer`. |

Harmony's `HarmonyLoader` auto-runs `PatchAll` on the mod assembly, so the `Patches/*` classes and
the embedded `[HarmonyPatch]` GrowableEntity planter guards are applied automatically - no manual
`Harmony.PatchAll()` call is needed.

Every dispatch honors `IsSubscribed(hookName)`, matching Oxide's subscribe/unsubscribe behavior
(e.g. `ValidateCurrentDamageHook` toggling `OnEntityTakeDamage`) so unused hooks cost nothing.

## Hooks patched

| Oxide hook | Patch target |
|---|---|
| `OnEntityTakeDamage` | `BaseCombatEntity.Hurt(HitInfo)` prefix (null = allow, non-null = block) |
| `OnEntityDeath` | `BaseCombatEntity.Die(HitInfo)` postfix (heli/bradley/npc routing for Loot Defender) |
| `CanLootEntity` / `CanLootPlayer` / `OnStartBeingLooted` / `OnLootEntity` / `OnLootPlayer` | `PlayerLoot.StartLootingEntity` |
| `CanLootGrowableEntity` | embedded `GrowableEntity.TakeClones` / `PickFruit` patches |
| `OnTurretTarget` | `AutoTurret.SetTarget` |
| `OnTrapTrigger` | `BaseTrap.ObjectEntered` |
| `OnEntityEnter` | `TriggerBase.OnEntityEnter` (TargetTrigger / TriggerEnterTimer) |
| `OnNpcTarget` | `BaseNpc.GetWantsToAttack` |
| `OnEntitySpawned` | `BaseNetworkable.Spawn` postfix (dispatch by type) |
| `OnEntityBuilt` | `Planner.DoBuild` postfix |
| `OnPlayerConnected` / `OnPlayerDisconnected` / `OnPlayerSleep` / `OnPlayerSleepEnded` | `BasePlayer` lifecycle |
| `OnCupboardAuthorize` | `BuildingPrivlidge.AddPlayer` |
| `OnMlrsFire` | `MLRS.Fire` |
| `OnTimedExplosiveExplode` | `TimedExplosive.Explode` |
| `OnServerSave` / `OnNewSave` | `SaveRestore.Save` / `SaveRestore.Load` |
| chat commands (`/tpve` etc.) | `ConVar.Chat.say` prefix -> `TruePVEMod.OnChatCommand` |

## Commands

Chat + console commands are registered from the plugin's covalence command list at startup, so all
the standard TruePVE commands work: `tpve` (and `tpve.*` subcommands), `tpve_prod`, `tpve_enable`,
`plshare`, `plunshare`, `sharelist`, `shareclear`, `checkit`, `lockouts`, `lockui`.

PreventLooting loot-share used to be `/share` / `/unshare`. Those names belong to **DynamicCupShare**
when that mod is loaded; use `/plshare` and `/plunshare` instead. If DynamicCupShare is not loaded,
`/share` and `/unshare` still map to PreventLooting.

## Build / deploy

1. **Recommended:** run `build.ps1` in `.cursor/HarmonyMods/TruePVE/`. It locates
   `RustDedicated_Data\Managed`, builds Release, and copies **only** `TruePVE.dll` to
   `<server root>/HarmonyMods/TruePVE.dll`.
2. **Manual:**
   `dotnet build .cursor/HarmonyMods/TruePVE/TruePVE.csproj -c Release -p:RustManaged="D:\!RustServer\RustDedicated_Data\Managed"`

The project builds against the game's own framework facades
(`DisableImplicitFrameworkReferences=true` + `mscorlib`/`System.*`/`netstandard`/`System.Memory`)
so current `Assembly-CSharp` RPC signatures using `ReadOnlySpan`, and the plugin's `Index`/`Range`
slicing, compile cleanly on `net48`.

## Load / verify

```
harmony.load TruePVE
```

On load you should see:

```
[TruePVE] Harmony mod loaded (Oxide port 2.4.21). Config: HarmonyConfig/TruePVE.json. NOTE: leave server.pve = false; the RuleSet engine owns PvE.
[TruePVE] Server initialized.
```

Then confirm behavior in-game: `/tpve` prints the current ruleset/status, PvP damage follows your
default ruleset, and Loot Defender locks apply on Bradley/Heli/NPC kills.

## Language

Default EN + RU messages are registered from the plugin's embedded `LoadDefaultMessages()`
(`lang.RegisterMessages`). No external language file is required; the shim's lang layer resolves
per-user language automatically.

## Intentionally skipped / soft-failed

- **Soft / deferred:** `CanHelicopterStrafeTarget`, `CanWaterBallSplash`, `OnSprayCreate`,
  `OnWallpaperRemove` (plugin logic present; patches not wired yet — those edges stay vanilla).
- **Apartment-only** code paths soft-fail if the corresponding types are absent.
- **Discord webhooks** post through the shim's WebRequests; if no webhook is configured they are no-ops.
- `Timer.Reset` is a no-op in the coroutine-backed timer shim (minor parity gap; timers still fire).

Everything under **RuleSets, Mappings, Entity Groups, Schedule, Loot Defender, Prevent Looting,
Supply Drop, Kill Notifications and Configuration Options** in `TruePVE.json` is honored.
