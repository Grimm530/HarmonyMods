# ArmoredTrain (Harmony port)

A near-verbatim Harmony port of the **ArmoredTrain** Oxide plugin (Adem). The original plugin body
(`EventController`, `WagonCustomizer`, `SpawnPositionFinder`, `ZoneController`, `NpcSpawnManager`,
`GuiManager`, `NotifyManager`, etc.) is retained as-is inside `ArmoredTrainPlugin.cs`; a thin Oxide
compatibility shim + Harmony patches drive it instead of the Oxide runtime.

## Identity

- **Mod DLL:** `HarmonyMods/ArmoredTrain.dll`
- **Harmony ID:** `com.facepunch.rust_dedicated.ArmoredTrain`
- **Entry point:** `ArmoredTrain.ArmoredTrainMod : IHarmonyModHooks`
- **Target framework:** `net48`

## Requirements / Dependencies

- **0GrimmNPC** (hard requirement for NPCs; DLL name). `NpcSpawn` calls are routed to GrimmNPC by reflection
  (`ArmoredTrainGrimmNpc` / `NpcSpawnBridge`), mirroring the Convoy port. If 0GrimmNPC is missing the
  event still runs but train NPCs will not spawn.
- **0PveMode** (optional, for `"PVE Mode Setting"`). Resolved lazily via AppDomain `PveMode_ApiType`;
  ArmoredTrain registers a ready callback so it still binds if it loads before `0PveMode`.
- **Krafs.Publicizer** (build-time only) publicizes `Assembly-CSharp` so the original plugin's use of
  internal game fields/methods (e.g. `TrainEngine.engineForce`, `HackableLockedCrate.hackSeconds`)
  compiles unchanged. This is a compile dependency only; nothing extra ships.

## Commands

Registered on the server console, F1 console, and chat (admin only):

| Command | Description |
| --- | --- |
| `atrainstart [preset]` | Start the event (optional preset name). |
| `atrainstartunderground [preset]` | Force the event to start on an underground route. |
| `atrainstartaboveground [preset]` | Force the event to start on an aboveground route. |
| `atrainstop` | Stop the current event. |
| `atrainpoint` | (Run by an admin in-game) capture the current position as a custom event point. |
| `savecustomwagon <presetName> <wagonShortPrefabName>` | Save a custom wagon layout to data. |

## Config & Data paths

- **Config:** `HarmonyConfig/ArmoredTrain.json` — the existing Oxide config is used verbatim; all
  `JsonProperty` names are unchanged so it deserializes with no edits. Written back to the same path.
- **Data:** `HarmonyData/ArmoredTrain/` — `Halloween.json`, `NewYear.json`, custom wagon profiles.
- **Images:** `HarmonyData/ArmoredTrain/Images/` (plugin PNGs first), then `HarmonyData/Images/`,
  then `HarmonyImages/ArmoredTrain/`. Neon-sign assets (`cocacola_*.png`, `snowflake.png`) already live
  under `HarmonyData/ArmoredTrain/Images/`. Countdown GUI needs `Tab_Adem.png`, `Clock_Adem.png`,
  `Crates_Adem.png`, `Soldiers_Adem.png` in one of those folders — if missing, GUI soft-fails and the
  event still runs (no exception / no unload).

## Build

```powershell
# from .cursor/HarmonyMods/ArmoredTrain
./build.ps1
```

`build.ps1` builds `ArmoredTrain/ArmoredTrain.csproj` (Release) and copies **only** `ArmoredTrain.dll`
to `HarmonyMods/ArmoredTrain.dll`. No referenced Rust/Unity assemblies are copied.

## Harmony patches (what hooks map to)

Oxide hooks were replaced with patches on real game methods. Each patch calls a static dispatcher on
the ported plugin (`ArmoredTrainDispatch.cs`); a **non-null** hook result means "block", matching
Oxide semantics (the Harmony prefix returns `false`).

| Patch (game method) | Oxide hook(s) ported |
| --- | --- |
| `BaseCombatEntity.Hurt(HitInfo)` prefix | `OnEntityTakeDamage` (TrainCar, PatrolHelicopter, BradleyAPC, AutoTurret, SamSite, ElectricSwitch, PowerCounter, BasePlayer/ScientistNPC) |
| `BaseCombatEntity.Die(HitInfo)` postfix | `OnEntityDeath` (heli/turret/bradley rewards, driver-killed) |
| `BaseNetworkable.Spawn()` postfix | `OnEntitySpawned` (bradley gibs cleanup, bradley_crate burn) |
| `BaseMountable.AttemptMount(BasePlayer,bool)` prefix | `CanMountEntity` |
| `PlayerLoot.StartLootingEntity(BaseEntity,bool)` prefix+postfix | `CanLootEntity` + `OnLootEntity` |
| `PlayerLoot.Clear()` prefix | `OnLootEntityEnd` (destroy emptied event crates) |
| `HackableLockedCrate.RPC_Hack(RPCMessage)` prefix | `CanHackCrate` |
| `ElectricSwitch.RPC_Switch(RPCMessage)` prefix+postfix | `OnSwitchToggle` + `OnSwitchToggled` |
| `BasePlayer.StartSleeping()` postfix | `OnPlayerSleep` |
| `TrainCar.RPC_WantsUncouple` prefix | `OnTrainCarUncouple` |
| `TrainCoupling.TryCouple` prefix | `CanTrainCarCouple` |
| `TriggerTrainCollisions.OnObjectAdded` postfix | `OnEntityEnter` (destroy colliding non-event wagons) |
| `AutoTurret.AddSelfAuthorize` prefix | `OnTurretAuthorize` |
| `BaseCombatEntity.CanCompletePickup` prefix | `CanPickupEntity` (switch/counter) |
| `NPCPlayer.CreateCorpse` postfix | `OnCorpsePopulate` |
| `BradleyAPC.UpdateTargetList` postfix | `CanBradleyApcTarget` |
| `PatrolHelicopterAI.UpdateTargetList` postfix | `CanHelicopterTarget` |
| `ConsoleSystem.Index.Server.Find(StringView)` postfix (manual) | command routing fallback |

`Subscribe`/`Unsubscribe` are replaced by the dispatchers early-outing when no event is active
(`_eventController.IsTrain*(...)` returns false), so patches are always registered but cheap when idle.

## Fully ported vs soft-disabled

**Fully ported (verbatim logic behind API shims):**
- Event lifecycle: `EventLauncher`, `EventController`, route/spawn finding, zone control.
- Train assembly, wagon customization, custom wagons, presets, movement, reverse, handbrake.
- NPCs via GrimmNPC (reflection bridge), Bradley/Heli/turret/samsite combat + rewards.
- Loot lock / aggression, hackable crate handling, crate burn, corpse cleanup.
- Config + data + image loading (soft-fail), GUI, notifications.
- Game tips use `gametip.showtoast_translated` (never the obsolete `gametip.showtoast`).
- `EncryptedValue<ulong>` / `IsRealPlayer` fixed per Framework §14.

**Soft-disabled / optional (null-checked, never hard-required):**
- **PveMode**, **Economics** (+ ServerRewards/IQEconomic), **DiscordMessages**, **GUIAnnouncements**,
  **Notify**, **DynamicPVP**, **TrainHomes**, **AlphaLoot** — all resolved through the Oxide shim's
  `plugins.Exists(...)` which only reports GrimmNPC/NpcSpawn as present. Calls are skipped when absent.
- `Interface.CallHook(...)` is a no-op (no Oxide hook bus); core logic never depends on it.

## Remaining gaps vs Oxide

These are lower-priority / harder to bind cleanly; core event behavior does not depend on them:

- `OnCustomNpcTarget` — GrimmNPC-side targeting; dispatcher exists but no stock game patch (GrimmNPC handles mounted combat).
- `OnSamSiteModeToggle` / `OnCounterModeToggle` / `OnCounterTargetChange` — dispatchers exist; no dedicated RPC patch yet (aggression still via loot/damage).
- `OnEntityKill(TrainCar)` — intentionally **not** ported as a kill-blocker (would break the event's own wagon cleanup). Travelling Vendor collisions are mitigated via the TriggerTrainCollisions path instead.
