# GrimmNPC2 — GEN2 support layer (parity / foundation notes)

## Harmony global config (GEN2)

- **Path:** `HarmonyConfig/GrimmNPC2.json` only (created with defaults on first load if missing). This mod does **not** read `GrimmNPC.json` or any other Harmony mod config; it depends only on stock GEN2 assemblies plus the Harmony loader.
- **API:** `GrimmNPC2.GetConfig()` returns `GrimmNPC2Config`. Spawn prefabs are **not** configured here — plugins must spawn a `ScientistNPC2` asset path appropriate to the desired `Scientist2FSM*` graph.
- **Wired in this mod:** `TryEvaluateTargetAgainstPolicy` is applied to **automatic** sense targeting via a Harmony postfix on `SenseComponent.CanTarget` (mod `CanTarget*`, `PreventScarecrowTargeting`, `ExcludedTargetTypes`, merged with per-NPC `Ignore*` flags). `TryPropagateTargetToAssistGroup` (`EnableAssistCallouts`, `AssistRange` caps propagation radius).
- **Debug / diagnostics:** `EnableDebugLogging` → `GrimmNPC2.LogDebug` (spawn line in `SpawnPatches2`, nav failures when `EnableNavMeshValidation` is on). `ForceRespectAiDormant`, `DefaultSleepDistance`, `EnableSwimmingDebug`, `EnableRaidingForAllNpcs` are read via `GetConfig()` for future hooks or plugins (see `AI_Framework_GEN2.md`).

### Oxide plugins, `Instance`, and load order

- **`GrimmNPC2.Instance`** is set only in **`IHarmonyModHooks.OnLoaded`** (after `Harmony.PatchAll` for this mod). Until that runs, **`Instance` is null** and **`RegisterPending` / `TryRegisterPending`** return false (`ModNotLoaded`).
- **Harmony** mods load from `HarmonyMods\` per the stock loader (see `Harmony_Mod_Execution_Framework.md`); **Oxide** plugins load afterward. Anything that calls into GrimmNPC2 must treat the mod as **not ready** until `OnLoaded` has run.
- **Duplicate assemblies:** If `GrimmNPC2.dll` is loaded from **more than one** place (e.g. build output **and** `HarmonyMods\`), the CLR can have two copies of the type; only the Harmony hook instance sets **`Instance`** on **its** assembly. Ship **one** mod DLL under `HarmonyMods\` for production. Plugins that reflect `GrimmNPC2` (e.g. **GrimmBoss**) should bind to the assembly loaded from a path containing **`HarmonyMods`**.
- **GrimmBoss troubleshooting:** Messages such as *“waiting for Harmony mod runtime (Instance)”* or *“Boss spawn deferred…”* mean the plugin sees **`Instance == null`**. Ensure the mod is loaded (`harmony.load GrimmNPC2` if you unloaded it), then **`o.reload GrimmBoss`**. If the problem persists, check for a duplicate `GrimmNPC2.dll` load outside `HarmonyMods\`.

## NpcSpawn-style surface vs stock GEN2 (audit)

**Already covered (this mod):** presets/registry, home/tether/roam/chase ranges, sense memory + listen/sense range tuning (reflection), short-cone half-angle at spawn, nav swim flag + optional **nav speed multiplier** (reflection), `NpcShootingComponent` flags + optional **muzzle offset** (reflection), `canBeHeadshot`, damage scaling (Harmony), turret targeting toggle, assist/squad indices, explicit target propagation, FSM introspection, cover/noise/bark/zone accessors, **target policy helper** (`TryEvaluateTargetAgainstPolicy`), **whitelist/blacklist** CSV → parsed ids, **group alert radius** override for propagation, loot/corpse/animal/barricade/heal **metadata** on `CustomNpcData2`.

**Partially covered:** NpcSpawn “attack range / aim cone” knobs — stock **GEN2** uses fixed spread in `NpcShootingComponent` and weapon `effectiveRange`; Grimm exposes **hints** (`AimConeScale`, `DamageRangeHint`, …) for plugins, not automatic FSM replacement.

**Explicitly not ported (stock owns):** sense tick loop, target scoring, `FindTarget` selection, cover state transitions, barricade **AI** targeting, sleep FSM for scientists, `IOnNpcTarget` is already the Oxide hook for gating.

**Explicitly BossMonster2 / plugin gameplay:** loot table execution, corpse removal, boss phases, custom think loops.

---

## Support-layer features added (BossMonster2-ready)

- **Linkage & identity (config + runtime)**  
  - `CustomNpcData2`: `PresetId`, `AutoApplyPresetFromRegistry`, `FsmKindHint`, `OwnerNetId`, `SquadId`, `CustomNpcKind`, `AssistRadius`, `PropagateTargetToAssistGroup`.  
  - `CustomNpcRuntimeState2`: mirrors owner/squad/kind at spawn; `ResolvedFsmKind`; `IsReleased`, `MovementFrozenPolicy`, `HelperPhaseActive` for plugin-owned hints (not enforced by movement patches here).

- **Named profiles (preset registry)**  
  - `RegisterProfileTemplate` / `TryGetProfileTemplateClone` / `UnregisterProfileTemplate` / `TryApplyProfileTemplateById`.  
  - Optional spawn path: `AutoApplyPresetFromRegistry` + `PresetId` replaces pending data with a cloned template before `Normalize()` (`SpawnPatches2`).  
  - **FSM graphs are not swapped at runtime** — each profile should be paired with the correct `ScientistNPC2` **prefab** (`Scientist2FSM` vs `Scientist2FSM_Heavy` vs `Scientist2FSM_Shotgun`). Use `FsmKindHint` for tooling; spawn warns if hint ≠ `ResolvedFsmKind`.

- **GEN2 FSM introspection & catalog (`GrimmNPC2.Gen2Catalog.cs`, `GrimmNPC2.Gen2.cs`)**  
  - `ScientistGen2FsmKind` + `DetectScientistFsmKind` (Shotgun / Heavy / default `Scientist2FSM`).  
  - `Gen2ScientistStateNames`: string tokens matching stock `FSMStateBase.Name` for scientist-relevant states (Patrol, DogFight, Flank, …).  
  - `TryGetFsmCurrentStateName`, `TryGetFsmCurrentState`; `TryGetScientist2FsmDefault` / `Heavy` / `Shotgun`.  
  - `TryNpcFlankSpotFind` → stock `NPCFlankSpot.Find` (same path math as `State_Flank`; allocate/reuse `NavMeshPath` instances carefully).

- **Indices (generic, not boss gameplay)**  
  - `_netIdsByOwner`: helpers/minions keyed by owner net id.  
  - `_netIdsBySquad`: optional squad membership.  
  - Maintained on register/unregister; destroyed lists cleaned when empty.

- **Lifecycle / batch APIs**  
  - `TryRegisterPending(..., out CustomNpcRegisterResult)`.  
  - `UnregisterAllLinkedToOwner`, `UnregisterAllInSquad`.  
  - `GetNetIdsWithOwner`, `GetNetIdsInSquad`, `CountNetIdsWithOwner`.

- **Assist / alert primitives (bounded)**  
  - `TryPropagateTargetToAssistGroup`: explicit `SenseComponent.TrySetTarget` to linked NPCs within `AssistRadius` (owner list and/or squad list).  
  - `TrySetTargetWithCooldownPolicy` for alert-style calls.

- **GEN2 component surface (`GrimmNPC2.Gen2.cs`)**  
  - TryGet accessors: Sense, `LimitedTurnNavAgent`, `NpcShootingComponent`, `FSMComponent`, `BlackboardComponent`, `NPCEncounterTimer`, `NpcZoneComponent`, `RootMotionPlayer`, `CoverComponent`, `NpcBarkManager` singleton.  
  - `TryResolveEntity(ulong)` via `BaseNetworkable.serverEntities.TryGetEntity`.  
  - Blackboard wrappers mapping to stock `Add` / `Increment` / `Remove` / `Clear` / `Has` / `Count`.  
  - Zone: `TryIsPointInsideNpcZone`, `TryGetNpcZoneForEntity`.  
  - `TrySetFsmActive` (use sparingly; stock owns FSM).

- **Swim / water (GEN2-native; no Gen1 `BaseNavigator` patches)**  
  - Spawn: `LimitedTurnNavAgent.canSwim` is set from `CustomNpcData2.CanSwim` (`Patches/SpawnPatches2`).  
  - Helpers: `TryGetLimitedTurnNavIsSwimming`, `TrySetLimitedTurnNavCanSwim`.  
  - Rationale: stock `LimitedTurnNavAgent` exposes `canSwim`, swim speeds, and `IsSwimming` (Reserved1); FSM transitions (`Trans_IsSwimming`, `Trans_IsInWater_Slow`, `Trans_IsTargetInWater`, etc.) own state changes. Gen1-style blocking of nav ticks is not replicated here.

- **Deeper sense support (public API + spawn tuning)**  
  - Spawn: `timeToForgetSightings` (existing) plus **cached reflection** into private serialized fields `hearingRange`, `ShortRangeVisionCone.range` / optional **`.halfAngle`** (`ShortRangeVisionHalfAngleDegrees`), `LongRangeVisionRectangle.z` from `ListenRange` / `SenseRange` (`TryApplySpawnSenseRangeTuning`). If Rust renames fields, reflection silently no-ops; public memory field still applies.  
  - Optional **NpcSpawn migration**: `NpcSenseRange` &gt; 0 overwrites `SenseRange` during `Normalize()`.  
  - Wrappers: `TryGetSenseCurrentTarget`, `TrySenseForget`, `TryGetSenseVisibilityStatus`, `TryFindMostRelevantNoise`.

- **Movement / shooting spawn tuning (reflection; no Gen1 nav)**  
  - `NavSpeedMultiplier` scales serialized `LimitedTurnNavAgent` speed fields (`TryApplySpawnNavSpeedMultiplier`).  
  - `ShootingLocalOffset` replaces serialized `NpcShootingComponent` private `offset` when non-zero (`TryApplySpawnShootingOffset`).  
  - **Not exposed:** per-state FSM speed overrides (stock states own `Shooting.AllowShooting`, sprint gates, etc.).

- **Target policy metadata + helper (does not replace `SenseComponent.Tick`)**  
  - `HostileTargetsOnly`, `DisplaySashTargetsOnly`, `IgnoreSafeZonePlayers`, `IgnoreSleepingPlayers`, `IgnoreWoundedPlayers`, `NpcTargetPolicyMode`, `NpcWhitelistCsv` / `NpcBlacklistCsv` (comma-separated ulongs: **net id** and/or **user id**).  
  - `TryEvaluateTargetAgainstPolicy(npc, candidate, out reason)` — call **before** `TrySetTarget` when you want profile rules; stock `CanTarget` + Oxide `IOnNpcTarget` remain authoritative for automatic sense targeting.

- **Group alert vs assist radius**  
  - `GroupAlertEnabled` + `GroupAlertRadius` &gt; 0: `TryPropagateTargetToAssistGroup` uses `GroupAlertRadius` instead of `AssistRadius` for the distance check.

- **Loot / corpse / animal / barricade / heal (metadata for loaders & BossMonster2)**  
  - `LootPreset`, `LootTableJsonHint`, `CratePrefab`, `RemoveCorpseOnDeath`, `AnimalAttackMode`, `AnimalSenseRange`, `AnimalDamageScale`, `AnimalWhitelistCsv` / `AnimalBlacklistCsv`, `BarricadeHealthThreshold`, `BarricadeDistanceThreshold`, `HealingScale`, `CanSleep`, `SleepDistance`, `CanRunAwayWater`, combat **hints** (`DamageRangeHint`, `ShortRangeHint`, `AttackLengthMaxShortRangeScale`, `AttackRangeMultiplier`, `CheckVisionCone`, `VisionConeDegrees`, `AimConeScale`).  
  - Scientist spawn applies **none** of the animal-only numbers; they exist so presets stay portable to animal GEN2 or boss tooling.

- **`BaseEntityTargettingExtensions` (documented forwards)**  
  - `TargetingExtInSameNpcTeam`, `TargetingExtIsNonNpcPlayer`, `TargetingExtIsNpcPlayer`, `TargetingExtTryToNonNpcPlayer`.  
  - **Important:** `InSameNpcTeam` in assembly compares `GetType()` equality, not `NPCTeam` — use only for “same concrete type” checks.

- **Cover / `NpcCoverManager` (query + reservation only)**  
  - `TryGetNpcCoverManager`, `TryGetCoversAround`, `TryFindBestCover`, `TryGetNpcCoverReserved`, `TryNpcCoverReserve`, `TryNpcCoverRelease`.  
  - No tactical loop in GrimmNPC2; stock FSM cover states remain authoritative.

- **Debug**  
  - `BuildDebugSnapshot(ulong)` includes `canSwimCfg`, `fsmHint`, `fsmResolved`, `fsmState` (when resolvable), and live `navSwimming` when entity resolves.

## Intentionally left for BossMonster2 (not support layer)

- Boss attack cycles, AOE, teleports, invisibility, helper-wave scheduling, phases, rewards, anti-face-tank combat design, encounter scripting.  
- Any logic that decides *when* to spawn helpers or *how* boss abilities execute.  
- Full custom target selection replacing `SenseComponent` loops (no evidence this is needed vs stock GEN2).

## GEN2 stock ownership (GrimmNPC2 does not duplicate)

- **FSM state machines:** `Scientist2FSM*`, all `State_*` / `Trans_*` types, transition evaluation, and graph wiring live in the game assembly. GrimmNPC2 only exposes **detection**, **naming constants**, and **read-only** current-state helpers.  
- **Water combat behavior:** FSM states/transitions, `LimitedTurnNavAgent` movement, `SenseComponent` visibility ticks.  
- **Cover tactics:** `State_MoveToCoverHiddenFromTarget`, `State_StayInCover`, etc.  
- **Noise investigation:** `NpcNoiseManager` + sense tick; GrimmNPC2 only exposes `TryFindMostRelevantNoise`.  
- **Predator / croc-only states** (`State_Croc*`, `State_WolfHurt`, …): listed in `AI_Framework_GEN2.md`; not specialized in GrimmNPC2 unless you spawn those prefabs.

## Still deferred / future verification

- **Vision/hearing reflection fragility:** If a future Rust build renames `hearingRange` / `ShortRangeVisionCone` / `LongRangeVisionRectangle`, spawn tuning for listen/sense range stops until GrimmNPC2 updates field names (public `timeToForgetSightings` unaffected).  
- **Nav speed / shooting offset reflection:** Same caveat if private field names change (`sneakSpeed`, …, `offset`).  
- **Spawn submerged / shoreline edge cases:** No extra “swim init guard” beyond `canSwim` + stock nav readiness; revisit only if live servers show broken spawns in water.  
- **`BossMonster_Instructional.md`:** Align naming when that doc exists in-repo.

## NpcSpawn candidate classification (reference)

| Concept | Disposition |
|--------|-------------|
| `DamageRange`, `ShortRange`, `AttackLengthMaxShortRangeScale`, `AttackRangeMultiplier` | **Hints** on `CustomNpcData2`; stock uses weapon range + sense geometry. |
| `CheckVisionCone`, `VisionCone` | **Half-angle** spawn tuning via `ShortRangeVisionHalfAngleDegrees`; `VisionConeDegrees` kept as doc/migration; cone checks are stock sense. |
| `AimConeScale` | **Metadata**; GEN2 spread is fixed in `NpcShootingComponent` unless you mod elsewhere. |
| `Speed` | **NavSpeedMultiplier** at spawn (reflection). |
| `BaseOffSet` | **ShootingLocalOffset** → `NpcShootingComponent` `offset`. |
| `CanRunAwayWater`, `CanSleep`, `SleepDistance` | **Metadata** (GEN2 FSM + `canSwim` own water escape; no scientist sleep FSM wired here). |
| `HostileTargetsOnly`, `DisplaySashTargetsOnly`, safe zone / sleep / wounded | **Policy** + `TryEvaluateTargetAgainstPolicy`. |
| `NpcAttackMode`, `NpcSenseRange` | **Mode** opaque int; **NpcSenseRange** normalizes into `SenseRange`. |
| `NpcWhitelist` / `NpcBlacklist` | **CSV → ulong[]** + policy helper. |
| `Animal*` | **Metadata** for animal GEN2 / shared presets. |
| `LootPreset`, `LootTable`, `CratePrefab`, `IsRemoveCorpse` | **Metadata** for loot/corpse plugins. |
| `GroupAlertEnabled`, `GroupAlertRadius`, receivers | **Radius** + existing owner/squad propagation (implicit receivers). |
| `BarricadeHealthThreshold`, `BarricadeDistanceThreshold`, `HealingScale` | **Metadata** for plugins (stock barricade targeting not duplicated). |

## Prior foundation (unchanged intent)

- Normalized combat/turret/memory/tether policy, spawn-time shooting/sense tweaks, destroy cleanup, home-tether destination helper — see `GrimmNPC2.cs` and `Patches/SpawnPatches2.cs`.
