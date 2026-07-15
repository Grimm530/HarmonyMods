# RaidableBases (Standalone Harmony Mod)

Raidable Bases as a **standalone Harmony mod** with **no Oxide dependency**. Source is under `.cursor/HarmonyMods/RaidableBases/`; the built DLL is copied to **HarmonyMods/** by `build.ps1`. Paste functionality is provided by the **CopyPaste Harmony mod** (`.cursor/HarmonyMods/CopyPaste/`).

## Mod identity

| Field | Value |
|-------|--------|
| **Original** | RaidableBases (nivex, 3.1.5) |
| **Layout** | Standalone Harmony mod; source split by `#region`; bridge in `RaidableBasesHarmony.cs` |
| **Oxide** | None — runs on Oxide-free servers |
| **Run as** | Harmony mod only (entry: `RaidableBases.RaidableBasesHarmonyEntry`, `IHarmonyModHooks`) |

## Project structure

| File | Content |
|------|--------|
| `RaidableBasesHarmony.cs` | Harmony bridge: `IHarmonyModHooks` entry, host, data layer, CopyPaste API, IPlayer, permission/timer stubs |
| `RaidableBasesStubs.cs` | Timer, attributes, CuiElementContainer |
| `RaidableBases.Main.cs` | Partial plugin class (base: `RaidableBasesBase`), fields, nested types |
| `RaidableBases.Hooks.cs` | Init/Unload/OnServerInitializedHarmony and **62 Oxide-style hook handlers** (invoked via Harmony patches → Interface.CallHook) |
| `RaidableBases.Spawn.cs` | #region Spawn |
| `RaidableBases.Paste.cs` | #region Paste (uses CopyPasteAPI) |
| `RaidableBases.Commands.cs` | #region Commands |
| `RaidableBases.Garbage.cs` | #region Garbage |
| `RaidableBases.IQDronePatrol.cs` | #region IQDronePatrol |
| `RaidableBases.Helpers.cs` | #region Helpers |
| `RaidableBases.DataFiles.cs` | #region Data files |
| `RaidableBases.Configuration.cs` | #region Configuration (HarmonyConfig when host present) |
| `RaidableBases.UI.cs` | #region UI |
| `RaidableBasesExtensionMethods.cs` | Extension methods (HasPermission, IsSteamId, etc.) |
| `Net48Compat.cs` | .NET 4.8 compatibility (e.g. string.Contains(char)) |

## Config and data

- **Config:** `HarmonyConfig/RaidableBases.json` (created/saved when running as Harmony mod). If `oxide/config/RaidableBases.json` exists, that path is used instead.
- **Data:** `HarmonyData/RaidableBases/` (Profiles, Base_Loot, etc.); paste files in `HarmonyData/copypaste/` (see CopyPaste mod).
- **Paths:** Per `.cursor/!Harmony-Assembly/HARMONY_MODS_GUIDE.md`.

**Data paths:** The Harmony mod always uses **`HarmonyData/RaidableBases/`** for Profiles, loot tables (Difficulty_Loot, Weekday_Loot, Base_Loot, Default_Loot), skins, and all other data files. **Paste files (base .json) are read from `HarmonyData/copypaste/`** so RaidableBases and the CopyPaste mod share one location. Put profile JSONs in `HarmonyData/RaidableBases/Profiles/` and raid base JSONs (e.g. `raideasy1.json`) in `HarmonyData/copypaste/`.

## Build and deploy

1. **References:** `RaidableBases.csproj` references **game assemblies** only (no Oxide). Point `HintPath` to `RustDedicated_Data/Managed/` (Assembly-CSharp, Rust.*, Facepunch.*, UnityEngine.*, etc.) and to **Rust.Harmony.dll** for `IHarmonyModHooks` / `OnHarmonyModLoadedArgs`.
2. **Build:** From repo root or mod folder:
   ```powershell
   .\.cursor\HarmonyMods\RaidableBases\build.ps1
   ```
3. **Output:** `RaidableBases.dll` is copied to **HarmonyMods/** only. Load with your Harmony loader (e.g. `harmony.load RaidableBases`).

## Loading

- Load **0Permissions** first (`HarmonyMods/0Permissions.dll`) for `raidablebases.*` perms/groups.
- Load **Kits** if profiles use Scientist/Murderer Kits.
- Load **CopyPaste** before RaidableBases.
- Then load **RaidableBases**. Entry: `RaidableBases.RaidableBasesHarmonyEntry`.
- Optional: **RaidableBasesUI** for dedicated CUI companion.

Profile kit names must exist in `HarmonyData/Kits/Kits.json`. Without Kits loaded, NPCs fall back to the profile Murderer/Scientist Loadout.

Lang strings: embedded defaults + override `HarmonyLanguage/RaidableBases.json`.

## Hooks (Oxide-style, dispatched via Harmony)

The mod implements **62 hook handlers** in `RaidableBases.Hooks.cs` (same as the Oxide plugin). Without Oxide, hooks are invoked by **Harmony patches** that call `Interface.CallHook(name, args)`. The bridge’s `Subscribe`/`Unsubscribe` register which hooks are active; `CallHook` invokes the mod’s handler when subscribed.

| Category | Hooks |
|----------|--------|
| **Lifecycle** | `OnNewSave`, `OnServerShutdown`, `OnServerInitialized`, `OnSunrise`, `OnSunset` |
| **Player** | `OnMapMarkerAdded`, `OnPlayerSleepEnded`, `OnPlayerLand`, `OnPlayerDeath`, `OnPlayerCommand`, `OnActiveItemChanged`, `OnPlayerDropActiveItem` |
| **Entity** | `OnEntitySpawned` (multiple overloads), `OnEntitySpawnedMLRS`, `OnEntityDeath` (multiple), `OnEntityKill`, `OnEntityBuilt`, `OnEntityGroundMissing`, `OnEntityEnter` (multiple), `OnEntityTakeDamage` |
| **Loot / items** | `OnLootEntityEnd`, `OnBackpackDrop`, `OnLoseCondition`, `OnNeverWear` |
| **Building** | `OnStructureUpgrade`, `OnCupboardAuthorize`, `OnCupboardProtectionCalculated`, `OnBaseRepair` |
| **Combat / traps** | `OnFireBallSpread`, `OnFireBallDamage`, `OnMlrsFire`, `OnNearbyTurretsScan`, `OnInterferenceUpdate`, `OnSamSiteTargetScan`, `OnTrapTrigger`, `OnExplosiveFuseSet` |
| **Elevators** | `OnElevatorButtonPress`, `OnButtonPress`, `OnElevatorMove`, `OnElevatorCall` |
| **NPC / third-party** | `OnLifeSupportSavingLife`, `OnRestoreUponDeath`, `OnCustomLootNPC`, `OnNpcKits`, `OnReflectDamage`, `OnRaidingUltimateTargetAcquire`, `OnClanMemberJoined`, `OnTeamAcceptInvite`, `OnNpcDuck`, `OnNpcDestinationSet` |
| **Zones** | `OnDeletedDynamicPVP`, `OnCreatedDynamicPVP` |
| **Console** | `OnServerCommand` |

**Patch coverage:** `Patches/` Harmony patches call into subscribed hooks:

| Patch area | Hooks |
|------------|--------|
| Init | `ServerMgr_Update`, `ItemManager_Initialize` |
| Death / spawn | `OnPlayerDeath`, `OnEntityDeath`, `OnEntitySpawned` |
| Damage | `CanEntityTakeDamage` / `OnEntityTakeDamage` |
| NPC | `OnNpcDuck`, `OnNpcDestinationSet` |
| Targeting | `OnEntityEnter`, `CanEntityBeTargeted`, `OnTrapTrigger`, `CanEntityTrapTrigger`, GunTrap/FlameTurret/AutoTurret |
| Zone | `CanBuild`, `CanLootEntity`, `OnEntityBuilt`, `OnLootEntityEnd`, `OnStructureUpgrade`, `OnEntityKill`, `OnPlayerSleepEnded`, `OnMapMarkerAdded` |
| Elevators | `OnElevatorButtonPress`, `OnElevatorMove`, `OnElevatorCall`, `OnButtonPress` |
| Condition | `OnLoseCondition`, `OnNeverWear` (`Item.LoseCondition`) |
| SAM / MLRS | `OnSamSiteTargetScan`, `CanEntityBeTargeted(SamSite)`, `OnMlrsFire` |
| Cupboard / fire | `OnCupboardAuthorize`, `OnCupboardProtectionCalculated`, `OnFireBallSpread`, `OnFireBallDamage` |
| Teams / clans | `OnTeamAcceptInvite`, `OnClanMemberJoined` (blocks native clan accept when hogging) |
| Commands | `Chat.say` + ConsoleSystem (`rb` / `buyraid` / …) + command blacklist |

**Allies:** Rust **Teams** (`RelationshipManager`) and vanilla **Clans** (`ClanManager` / `BasePlayer.clanId`) are first-class. Oxide Clans/Friends remain optional if present.

**Still optional / later:** backpack drop (Oxide Backpacks), Economics/ServerRewards/GUIAnnouncements bridges, `OnNewSave` / `OnServerShutdown`.

**Dependencies:** Load **Permissions** (groups/perms), **CopyPaste**, **Kits** (optional but recommended), then **RaidableBases**. Lang: `HarmonyLanguage/RaidableBases.json`. Optional UI companion: **RaidableBasesUI**.

## CopyPaste dependency

RaidableBases pastes bases using the **CopyPaste Harmony mod** API (via reflection): `PreLoadData`, `Paste`, `FindBestHeight`, `Version`, `IsPasteReady`. See `.cursor/HarmonyMods/CopyPaste/README.md` for the CopyPaste mod API. **Paste data is read from `HarmonyData/copypaste/`**. Put base JSON files (e.g. `raideasy1.json`, `raidmed1.json`) there; RaidableBases loads them and passes entity data to CopyPaste's paste API.

## Troubleshooting

### "Plugin isn't loading" / "Grid has failed initialization. No valid profiles exist!"

The Harmony mod **does** load (you’ll see `[RaidableBases] Harmony mod loaded. Config: ...`). The failure happens during deferred init when the grid is built:

1. **No profile files** – The mod needs at least one profile JSON in **`HarmonyData/RaidableBases/Profiles/`** (e.g. `Easy Bases.json`). On first load with an empty Profiles folder, default profiles are created automatically. The log will show `Using data from HarmonyData/RaidableBases` / `creating default profile files`.

2. **"Difficulty `Easy` is in the configuration file, but does not exist in any of the profiles"** – The config lists difficulty keys (Easy, Medium, Hard, etc.) that must each match at least one profile’s **Difficulty** (e.g. in the profile JSON, `"Difficulty": "Easy"`). Either add a profile with that difficulty or remove that difficulty from the config sections that list difficulties.

3. **CopyPaste** – Load the CopyPaste Harmony mod before RaidableBases and ensure paste data is in the copypaste folder; RaidableBases will still “load” but paste functionality requires CopyPaste.

### "[Ultimate Leaderboard] Start caching leaderboard" spam

That message is from the **Ultimate Leaderboard** Oxide plugin, not RaidableBases. If your log is filled with repeated `[Ultimate Leaderboard] Start caching leaderboard` lines, the cause is that plugin (e.g. a timer or hook firing too often). Updating, reconfiguring, or temporarily disabling Ultimate Leaderboard will address the spam; RaidableBases does not trigger it.

## Reference

- **Harmony mod guide:** `.cursor/!Harmony-Assembly/HARMONY_MODS_GUIDE.md`
- **CopyPaste Harmony mod:** `.cursor/HarmonyMods/CopyPaste/README.md`
