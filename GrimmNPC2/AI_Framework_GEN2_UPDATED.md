# AI Framework GEN2 — GrimmNPC2 dictionary / build map

**Source of truth (code):** `.cursor/!Assembly-CSharp-RUST/Rust.Ai.Gen2`  
**Inventory:** `173` `.cs` files (complete; do not shrink this list when editing).

**Authority order (read before changing behavior assumptions):**

1. Decompiled game assembly under `.cursor/!Assembly-CSharp-RUST/` (especially `Rust.Ai.Gen2`).
2. `AI_NPC_Plugin_Execution_Framework.md` — execution architecture for Gen2 vs Gen1, patch discipline, spawn/FSM boundaries.
3. `AI_NPC_Plugin_Execution_Framework_Navigation_Reference.md` — **subordinate** troubleshooting only; never overrides (1)–(2).
4. `HarmonyMods/GrimmNPC/README.md` — **gap-analysis only** for Gen1 feature parity targets; **do not** treat Gen1 stack as Gen2 truth.

**Confidence legend (used throughout):**

| Tag | Meaning |
|-----|---------|
| **V** | Verified from assembly snippet or from `AI_NPC_Plugin_Execution_Framework.md` §0.2 / Gen2 rules |
| **I** | Inferred from filename, public attributes, or obvious subsystem role |
| **M** | Must open the `.cs` in assembly before codegen / tier changes — filename or role is insufficient |

---

## 1. Purpose and usage

This file is the **GEN2 dictionary and build map for GrimmNPC2**, not a flat inventory. Use it to:

| Use | How |
|-----|-----|
| **Feature planning** | Map plugin/mod work to **subsystem families** (FSM, senses, nav, shooting, cover, zones, bark/noise) and see what stock already provides. |
| **Subsystem discovery** | Jump from a behavior goal to **likely types** (states, transitions, components) without guessing Gen1 APIs. |
| **Missing-file audits** | Compare GrimmNPC2 coverage against **Tier 1–2** files and §8 gaps. |
| **Patch target selection** | Prefer **Gen2-specific** hooks only after prefab/FSM understanding (`AI_NPC_Plugin_Execution_Framework.md` AOR-3); this doc names **candidate types**. |
| **Parity tracking vs Gen1** | Use `GrimmNPC/README.md` only as a **checklist of features**; implement against **GEN2** types listed here. |

**Agents:** Treat tiers as **hypotheses** until **V** or manual inspection updates an entry.

---

## 2. Scope boundaries

| Scope | What belongs | GrimmNPC2 first-pass stance |
|-------|----------------|------------------------------|
| **Humanoid GEN2 (`ScientistNPC2`)** | `ScientistNPC2`, `Scientist2FSM*`, humanoid states/transitions, shooting/cover/zone/bark wired into those FSMs | **Primary** design target |
| **Shared GEN2 runtime** | `BaseNPC2`, `FSMComponent`, `FSMStateBase`, `FSMTransitionBase`, `SenseComponent`, `LimitedTurnNavAgent`, `BlackboardComponent`, payloads/slow transitions | **Required** to understand any GEN2 NPC |
| **Predator / animal GEN2** | `Wolf2*`, `Crocodile*`, `Tiger*`, `Panther`, predator-tuned states | **Listed and tiered**; low priority unless you spawn those prefabs |
| **Nav / cover / positioning helpers** | Cover groups, flank/overwatch hints, push helper, navmesh modifier volume | **Tier 2–3** — needed for “full” combat AI, not for a minimal scaffold |
| **Audio / bark / noise / voicelines** | `NpcBark*`, `NpcNoise*`, `NPCVoiceline*`, footstep | **Tier 2–3** for immersion; **not** optional for stock scientist FSM wiring (`NpcBarkManager` is on `Scientist2FSM` — **V**) |
| **Dev / test / empty stubs** | `AIArena2`, `NavPathTester`, `RootMotionTester` | **Tier 4–5** — usually irrelevant to shipping GrimmNPC2 behavior |

**Hard boundary:** GEN2 does **not** use `BaseAIBrain` / `ScientistBrain` / `BaseNavigator` as the Gen1 humanoid stack does; behavior is **FSM-driven** (`AI_NPC_Plugin_Execution_Framework.md` §0.2).

---

## 3. Core runtime dependency map

Directed relationships below are **partial** (behavioral, not full UML). **V** marks edges confirmed from assembly.

### 3.1 Verified edges (assembly)

- **`ScientistNPC2` → `BaseNPC2`**: humanoid GEN2 entity root; `[SoftRequireComponent(typeof(Scientist2FSM))]` — **V** (`ScientistNPC2.cs`).
- **`BaseNPC2`**: `BaseCombatEntity`; server registers with `Query.Server.AddBrain` / removes on destroy — **V** (`BaseNPC2.cs`).
- **`Scientist2FSM` → `FSMComponent`**: adds `[SoftRequireComponent(typeof(BlackboardComponent), typeof(NpcBarkManager))]`, `[SoftRequireComponent(typeof(NpcZoneComponent), typeof(NPCEncounterTimer), typeof(NpcShootingComponent))]`, `[SoftRequireComponent(typeof(LimitedTurnNavAgent), typeof(RootMotionPlayer), typeof(SenseComponent))]` — **V** (`Scientist2FSM.cs`).
- **`Scientist2FSM_Heavy` / `Scientist2FSM_Shotgun`**: same broad component pattern (Heavy omits some states; uses `State_Dead` vs `State_ScientistDead` in places) — **V** (file headers).
- **`FSMComponent`**: tick queue runs `SenseComponent.Tick`, `NPCEncounterTimer.Tick` (if present), FSM `Tick`, optional `NpcBarkComponent.Tick`; `[SoftRequireComponent(typeof(LimitedTurnNavAgent), typeof(RootMotionPlayer), typeof(SenseComponent))]` and `[SoftRequireComponent(typeof(BlackboardComponent), typeof(NPCEncounterTimer))]` — **V** (`FSMComponent.cs`).
- **`LimitedTurnNavAgent`**: `[SoftRequireComponent(typeof(NavMeshAgent))]` — **V** (`LimitedTurnNavAgent.cs`).

### 3.2 Important types — roles and GrimmNPC2 notes

| Type | Category | Likely purpose | Why GrimmNPC2 cares | Tier | Conf |
|------|----------|----------------|----------------------|------|------|
| `ScientistNPC2` | Entity | Humanoid GEN2 pawn | Plugin spawn / Harmony targets for custom scientists | Core | V |
| `BaseNPC2` | Entity | Non-player NPC2 base; brain query registration | Any GEN2 NPC semantics | Core | V |
| `FSMComponent` | Runtime | Work-queue FSM tick, state changes, senses refresh gating | Replacing/thin-wrapping “brain” | Core | V |
| `FSMStateBase` / `FSMTransitionBase` | Runtime | State graph primitives | Custom states/transitions | Core | I |
| `FSMSlowTransitionBase` | Runtime | Expensive / throttled transition checks | Performance-sensitive guards | Important | I |
| `FSMPayload` / `EFSMStateStatus` | Runtime | State payload + status enum | Extending FSM | Core / Important | I |
| `SenseComponent` | Perception | Targeting, LKP, visibility, surprise hooks | GrimmNPC2 already calls `TrySetTarget`, `FindLKP` | Core | V |
| `LimitedTurnNavAgent` | Navigation | NavMesh movement, speeds, steering | GrimmNPC2 already calls `SetDestination` | Core | V |
| `BlackboardComponent` | Data | FSM shared memory / flags | Plugin coordination with stock FSM | Core | V |
| `NpcShootingComponent` | Combat | Weapon / engagement for GEN2 | Scientist FSM **requires** component | Core | V |
| `NpcBarkManager` | Audio / logic | Bark routing | Scientist FSM **requires** | Core | V |
| `NPCEncounterTimer` | Encounter | Cooldowns / encounter pacing | FSM + Scientist FSM **require** | Core | V |
| `NpcZone` / `NpcZoneComponent` | Zone | Spatial AI zones | Scientist FSM **requires** `NpcZoneComponent` | Core | V |
| `RootMotionPlayer` | Animation / move | Root motion driver | FSM **requires** | Core | V |
| `CoverComponent` / `Cover` / `CoverGroup` | Cover | Cover points / selection | Combat realism | Important | I |
| `NpcCoverManager` | Cover | Cover aggregation | Combat flows | Important | I |

---

## 4. GrimmNPC2 relevance tiers (definitions)

Every file in §5 is assigned exactly one tier:

| Tier | Meaning |
|------|---------|
| **T1 — Core for GrimmNPC2** | Required or very likely required for a **working humanoid GEN2** path aligned with `ScientistNPC2` / `Scientist2FSM*`. |
| **T2 — Important for combat/behavior completeness** | Not always needed for “entity exists + moves,” but expected for **realistic** scientist combat/cover/teams/extensions. |
| **T3 — Situational / advanced** | Specialized states, helpers, or immersion systems. |
| **T4 — Likely out of scope for first-pass GrimmNPC2** | Predator-specific, empty stubs, dev tests, or content not needed until later. Still kept in the dictionary. |
| **T5 — Manual verification required** | Tier placement or role is **not** safe from filename alone; inspect assembly before relying on the entry. |

---

## 5. Per-file dictionary (complete inventory — 173 files)

Columns: **Cat** = short category; **Applies** = Humanoid / Scientist / Predator / FSM-Shared / Dev / All; **Responsibility** = one-line; **Use** = GrimmNPC2 angle; **C** = confidence (V/I/M).

### 5.1 Non-state files (`State_*` / `Trans_*` excluded) — 68 files

| File | Cat | Tier | Applies | Responsibility | GrimmNPC2 use | C |
|------|-----|------|---------|----------------|---------------|---|
| `AIArena2.cs` | Dev / stub | T4 | Dev | Empty `FacepunchBehaviour` client stub | Ignore unless arena tooling | I |
| `BaseEntityTargettingExtensions.cs` | Combat | T2 | All | Extension methods for target selection / checks | Combat alignment with stock GEN2 | I |
| `BaseNPC2.cs` | Entity | T1 | All GEN2 | `BaseCombatEntity` NPC2; `Query.Server` brain registration | Base type for any GEN2 spawn logic | V |
| `BlackboardComponent.cs` | FSM data | T1 | FSM-Shared | Shared FSM memory / flags | Required by scientist FSM (**V**) | V |
| `BoxCoverGroup.cs` | Cover geom | T3 | Humanoid | Axis-aligned cover grouping | Advanced cover layouts | I |
| `ClawMarkSpawner.cs` | Predator FX | T4 | Predator | Claw VFX / marks | Tiger content | I |
| `Cover.cs` | Cover | T2 | Humanoid | Single cover point / data | Combat positioning | I |
| `CoverComponent.cs` | Cover | T2 | Humanoid | Cover selection usage on entity | Combat | I |
| `CoverGroup.cs` | Cover | T2 | Humanoid | Cover group container | Combat | I |
| `Crocodile.cs` | Entity | T4 | Predator | Crocodile NPC2 definition | Not scientist | I |
| `CrocodileFSM.cs` | FSM def | T4 | Predator | Croc FSM graph | Not scientist | I |
| `EFSMStateStatus.cs` | Enum | T1 | FSM-Shared | State status values | FSM plumbing | I |
| `ENPCVoicelineCategory.cs` | Enum | T3 | Audio | Voiceline categories | Voice/bark content | I |
| `ENpcVoicelineImportance.cs` | Enum | T3 | Audio | Voiceline priority | Voice/bark | I |
| `FireConstants.cs` | Combat | T2 | Humanoid | Fire / burning constants | Fire-related AI | I |
| `FSMComponent.cs` | FSM runtime | T1 | FSM-Shared | Work-queue FSM; ties senses, encounter, bark | Core brain replacement | V |
| `FSMPayload.cs` | FSM runtime | T1 | FSM-Shared | Payload for state transitions | State machine data | I |
| `FSMSlowTransitionBase.cs` | FSM runtime | T2 | FSM-Shared | Base for expensive transition checks | Performance | I |
| `FSMStateBase.cs` | FSM runtime | T1 | FSM-Shared | State base class | Custom states | I |
| `FSMTransitionBase.cs` | FSM runtime | T1 | FSM-Shared | Transition base class | Custom transitions | I |
| `IParametrized.cs` | Interface | T2 | FSM-Shared | Parameterized transition/state hook | Generic FSM API | I |
| `LimitedTurnNavAgent.cs` | Navigation | T1 | All | NavMesh movement, steering, speeds | Already used by GrimmNPC2 | V |
| `LineCoverGroup.cs` | Cover geom | T3 | Humanoid | Line-shaped cover | Advanced cover | I |
| `LockState.cs` | Concurrency | T2 | FSM-Shared | Re-entrancy / lock handles for state changes | FSM safety | V |
| `NavGeneratedCoverGroup.cs` | Cover | T3 | Humanoid | Cover from nav / generation | Cover tooling | I |
| `NavMeshPathEx.cs` | Navigation | T2 | All | Path utilities | Nav debugging / helpers | I |
| `NavPathTester.cs` | Dev | T4 | Dev | Path testing helper | Not shipping AI | I |
| `NavPathTestType.cs` | Dev | T4 | Dev | Enum for path tests | Dev | I |
| `NPCAnimController.cs` | Animation | T2 | Humanoid | Animation control bridge | Movement / combat visuals | M |
| `NpcBarkComponent.cs` | Bark | T2 | Scientist | Per-NPC bark tick | Heavy/Shotgun reference `NpcBarkComponent` (**V**) | V |
| `NpcBarkManager.cs` | Bark | T1 | Scientist | Bark scheduling / rules | Required on `Scientist2FSM` (**V**) | V |
| `NpcCoverManager.cs` | Cover | T2 | Humanoid | Global / entity cover management | Combat | I |
| `NPCEncounterTimer.cs` | Encounter | T1 | Scientist | Encounter timers inside FSM tick | Required by `FSMComponent` + scientist FSM (**V**) | V |
| `NPCFlankSpot.cs` | Positioning | T3 | Humanoid | Flank position hints | Flank behavior | I |
| `NpcGrenade.cs` | Combat | T2 | Humanoid | Grenade entity/logic for NPCs | Grenade states | I |
| `NpcGrenadePositionHint.cs` | Positioning | T3 | Humanoid | Grenade toss hints | Grenade accuracy | I |
| `NPCHumanoidFootstepComponent.cs` | Noise | T3 | Humanoid | Footstep noise emission | Stealth / noise systems | I |
| `NpcLevelScript.cs` | Level | T5 | All | Level script bridge for NPCs | Map-specific scripting | M |
| `NpcLevelTrigger.cs` | Level | T5 | All | Trigger volumes for NPC level logic | Map triggers | M |
| `NpcNoiseEvent.cs` | Noise | T3 | All | Noise event struct/logic | Hearing / investigation | I |
| `NpcNoiseIntensity.cs` | Noise | T3 | All | Noise strength enum/logic | Hearing | I |
| `NpcNoiseManager.cs` | Noise | T3 | All | Noise aggregation | Hearing | I |
| `NPCOverwatchSpot.cs` | Positioning | T3 | Humanoid | Overwatch positions | Support AI | I |
| `NpcPositionHint.cs` | Positioning | T3 | All | Generic position hints | Spacing / staging | I |
| `NpcPushHelper.cs` | Physics | T3 | All | Push / shove helper | Melee / crowding | I |
| `NPCRangeConstants.cs` | Combat | T2 | Humanoid | Range constants for NPC combat | Tuning parity | I |
| `NpcShootingComponent.cs` | Combat | T1 | Scientist | Shooting / weapon handling for GEN2 | Required on scientist FSM (**V**) | V |
| `NpcSleepingComponent.cs` | Ambient | T3 | Humanoid | Sleep behavior | Camp / sleeper NPCs | I |
| `NPCTeam.cs` | Team | T2 | All | Team IDs / relationships | Multi-NPC coordination | I |
| `NPCVoiceline.cs` | Audio | T3 | All | Single voiceline entry | Voice | I |
| `NPCVoicelinesDatabase.cs` | Audio | T3 | All | Voiceline DB | Voice | I |
| `NpcZone.cs` | Zone | T2 | Scientist | Zone definition instance | Zone rules | I |
| `NpcZoneComponent.cs` | Zone | T1 | Scientist | Zone membership component | Required on scientist FSM (**V**) | V |
| `Panther.cs` | Entity | T4 | Predator | Panther NPC2 | Not scientist | I |
| `PillarCoverGroup.cs` | Cover geom | T3 | Humanoid | Pillar cover layout | Advanced cover | I |
| `RootMotionPlayer.cs` | Animation | T1 | Scientist | Root motion playback | Required by FSM (**V**) | V |
| `RootMotionTester.cs` | Dev | T4 | Dev | Root motion test | Dev | I |
| `RustNavmeshModifierVolume.cs` | Nav | T5 | All | Navmesh modifier volume | Prefab / map setup | M |
| `Scientist2FSM_Heavy.cs` | FSM def | T1 | Scientist | Heavy-weapon scientist graph | Variant prefab | V |
| `Scientist2FSM_Shotgun.cs` | FSM def | T1 | Scientist | Shotgun scientist graph | Variant prefab | V |
| `Scientist2FSM.cs` | FSM def | T1 | Scientist | Default scientist FSM | Primary scientist behavior | V |
| `ScientistNPC2.cs` | Entity | T1 | Scientist | Humanoid GEN2 scientist entity | GrimmNPC2 spawn target | V |
| `SenseComponent.cs` | Perception | T1 | All | Senses, LKP, targeting | GrimmNPC2 already uses API | V |
| `Tiger.cs` | Entity | T4 | Predator | Tiger NPC2 | Not scientist | I |
| `TigerFSM.cs` | FSM def | T4 | Predator | Tiger FSM | Not scientist | I |
| `TigerSneakTelegraphGrowl.cs` | Predator | T4 | Predator | Sneak telegraph helper | Tiger-only | I |
| `Wolf2.cs` | Entity | T4 | Predator | Wolf NPC2 | Not scientist | I |
| `Wolf2FSM.cs` | FSM def | T4 | Predator | Wolf FSM | Not scientist | I |

### 5.2 States (`State_*`) — 56 files

| File | Cat | Tier | Applies | Responsibility | GrimmNPC2 use | C |
|------|-----|------|---------|----------------|---------------|---|
| `State_ApproachFire.cs` | State | T3 | Humanoid / animal | Move toward fire stimulus | Niche environmental | I |
| `State_ApproachFood.cs` | State | T4 | Predator / animal | Approach food | Animal feeding | I |
| `State_Attack.cs` | State | T2 | All | Generic attack state | Combat | I |
| `State_AttackUnreachable.cs` | State | T3 | All | Attack when path/LOS blocked | Combat edge cases | I |
| `State_AttackUnreachableWarped.cs` | State | T3 | All | Warped variant of unreachable attack | Combat edge cases | I |
| `State_AttackWithTracking.cs` | State | T3 | Predator | Tracking attack | Predator | I |
| `State_Bark.cs` | State | T3 | All | Perform bark animation/event | Audio / intimidation | I |
| `State_BringFoodBackToWater.cs` | State | T4 | Croc | Carry food to water | Croc-only | I |
| `State_Circle.cs` | State | T3 | Predator | Circle prey | Predator combat | I |
| `State_CircleDynamic.cs` | State | T3 | Predator | Dynamic circling | Predator | I |
| `State_CrocBreakFoundation.cs` | State | T4 | Croc | Melee structure | Croc-only | I |
| `State_CrocCharge.cs` | State | T4 | Croc | Charge attack | Croc-only | I |
| `State_CrocIntimidate.cs` | State | T4 | Croc | Intimidate | Croc-only | I |
| `State_CrocTurn.cs` | State | T4 | Croc | Turn-in-place / reposition | Croc-only | I |
| `State_Dead.cs` | State | T2 | All | Generic death handling | Heavy scientist variant uses (**V**) | V |
| `State_DeadlyAttack.cs` | State | T3 | Predator | High-commit attack | Predator | I |
| `State_DogFight.cs` | State | T1 | Scientist | Close combat / strafe shooting | Scientist FSM (**V**) | V |
| `State_DragCorpse.cs` | State | T4 | Predator | Drag corpse | Predator | I |
| `State_EatFood.cs` | State | T4 | Animal | Eat food | Animal | I |
| `State_FastSneak.cs` | State | T3 | Humanoid | Fast sneak movement | Stealth | I |
| `State_Flank.cs` | State | T1 | Scientist | Flank maneuver | Scientist FSM (**V**) | V |
| `State_Flee.cs` | State | T3 | All | Flee threat | Survival | I |
| `State_FleeFire.cs` | State | T3 | All | Flee from fire | Environmental | I |
| `State_FleeToHide.cs` | State | T3 | All | Flee to concealment | Survival | I |
| `State_GoBackToWater.cs` | State | T4 | Croc | Return to water | Croc-only | I |
| `State_Growl.cs` | State | T4 | Predator | Growl | Predator | I |
| `State_Howl.cs` | State | T4 | Wolf | Howl | Wolf | I |
| `State_Hurt.cs` | State | T2 | All | Hurt reaction | Combat feedback | I |
| `State_HurtWithAdditive.cs` | State | T3 | All | Hurt with additive anim | Combat feedback | I |
| `State_Intimidated.cs` | State | T3 | All | Intimidated stance | Social AI | I |
| `State_LandOrSwimAttack.cs` | State | T4 | Croc | Amphibious attack | Croc / water | I |
| `State_MoveToBreakFoundation.cs` | State | T4 | Croc | Move to break building | Croc raid | I |
| `State_MoveToCoverHiddenFromTarget.cs` | State | T1 | Scientist | Relocate to hidden cover | Scientist FSM (**V**) | V |
| `State_MoveToLastReachablePointNearTarget.cs` | State | T3 | All | Move to last reachable nav point | Path failures | I |
| `State_MoveToPointWithLosOnTarget.cs` | State | T1 | Scientist | Reposition for LOS | Scientist FSM (**V**) | V |
| `State_MoveToTarget.cs` | State | T2 | All | Move toward target | Chase / engage | I |
| `State_Nothing.cs` | State | T1 | FSM-Shared | No-op / grouping node | FSM graph structure (**V**) | V |
| `State_Observe.cs` | State | T3 | All | Observe / idle focus | Suspense | I |
| `State_Patrol.cs` | State | T1 | Scientist | Patrol route | Scientist FSM (**V**) | V |
| `State_PatrolIdle.cs` | State | T1 | Scientist | Idle between patrol | Scientist FSM (**V**) | V |
| `State_PlayAnimation.cs` | State | T3 | All | One-shot anim | Scripting | I |
| `State_PlayAnimationBase.cs` | State | T2 | FSM-Shared | Animation state base | Scripting | I |
| `State_PlayAnimationRM.cs` | State | T3 | All | Root-motion animation play | Scripting | I |
| `State_PlayAnimLoop.cs` | State | T3 | All | Loop anim | Scripting | I |
| `State_PlayRandomAnimation.cs` | State | T3 | All | Random anim | Scripting | I |
| `State_Roam.cs` | State | T2 | All | Roam / wander | Open-world | I |
| `State_Roar.cs` | State | T4 | Predator | Roar | Predator | I |
| `State_ScientistDead.cs` | State | T1 | Scientist | Scientist-specific death | Default scientist FSM (**V**) | V |
| `State_ScientistRush.cs` | State | T1 | Scientist | Rush / aggressive push | Scientist FSM (**V**) | V |
| `State_ScientistSurprised.cs` | State | T1 | Scientist | Surprised reaction | Scientist FSM (**V**) | V |
| `State_ScriptedNade.cs` | State | T2 | Scientist | Scripted grenade throw | Scientist FSM (**V**) | V |
| `State_Search.cs` | State | T1 | Scientist | Search last known area | Scientist FSM (**V**) | V |
| `State_StayInCover.cs` | State | T1 | Scientist | Hold cover | Scientist FSM (**V**) | V |
| `State_ThrowGrenade.cs` | State | T2 | Scientist | Throw grenade | Scientist FSM (**V**) | V |
| `State_TryAmbushUnderwater.cs` | State | T4 | Croc | Underwater ambush | Croc | I |
| `State_WolfHurt.cs` | State | T4 | Wolf | Wolf hurt | Wolf | I |

### 5.3 Transitions (`Trans_*`) — 49 files

| File | Cat | Tier | Applies | Responsibility | GrimmNPC2 use | C |
|------|-----|------|---------|----------------|---------------|---|
| `Trans_AlwaysValid.cs` | Trans | T2 | FSM-Shared | Always true | Graph glue | I |
| `Trans_And.cs` | Trans | T2 | FSM-Shared | Boolean AND | Composition | I |
| `Trans_Bark.cs` | Trans | T3 | All | Bark-related condition | Audio | I |
| `Trans_BlackboardCounterGte.cs` | Trans | T2 | FSM-Shared | Counter threshold | FSM memory | I |
| `Trans_CanReachTarget_Slow.cs` | Trans | T2 | All | Slow reachability check | Nav expensive | I |
| `Trans_CanSeeTarget.cs` | Trans | T2 | All | LOS / see checks | Core combat | I |
| `Trans_CanThrowGrenade.cs` | Trans | T2 | Scientist | Grenade readiness | Grenade flows | I |
| `Trans_Composite.cs` | Trans | T2 | FSM-Shared | Composite transition | Composition | I |
| `Trans_Cooldown.cs` | Trans | T2 | FSM-Shared | Cooldown gate | Pacing | I |
| `Trans_CrocHasStraightPathToTarget.cs` | Trans | T4 | Croc | Straight-line path test | Croc-only | I |
| `Trans_ElapsedTime.cs` | Trans | T2 | FSM-Shared | Time elapsed | Timing | I |
| `Trans_ElapsedTimeRandomized.cs` | Trans | T2 | FSM-Shared | Randomized timer | Timing | I |
| `Trans_HasBlackboardBool.cs` | Trans | T2 | FSM-Shared | Blackboard flag | FSM memory | I |
| `Trans_HasTarget.cs` | Trans | T1 | All | Has current target | Core graph | I |
| `Trans_HeardNoise.cs` | Trans | T3 | All | Heard noise event | Investigation | I |
| `Trans_InitialAlliesNotFighting.cs` | Trans | T3 | Humanoid | Allies idle | Coordination | I |
| `Trans_IsFlankedByTarget.cs` | Trans | T3 | Humanoid | Flank detection | Tactical | I |
| `Trans_IsHealthBelowPercentage.cs` | Trans | T2 | All | Health threshold | Retreat / finisher | I |
| `Trans_IsInTargetViewCone.cs` | Trans | T2 | All | Inside target view cone | Stealth / suppression | I |
| `Trans_IsInWater_Slow.cs` | Trans | T3 | All | In water (slow check) | Water combat | I |
| `Trans_IsMuzzleClear_Slow.cs` | Trans | T2 | Scientist | Muzzle clearance | Shooting | I |
| `Trans_IsNavmeshReady.cs` | Trans | T2 | All | Navmesh ready gate | Spawn / readiness | I |
| `Trans_IsReloading.cs` | Trans | T2 | Scientist | Reloading | Combat timing | I |
| `Trans_IsSwimming.cs` | Trans | T3 | All | Swimming | Water | I |
| `Trans_IsTargetDown.cs` | Trans | T2 | All | Target dead/incap | Combat | I |
| `Trans_IsTargetInWater.cs` | Trans | T3 | All | Target in water | Water combat | I |
| `Trans_IsTargetLkpInOurZone.cs` | Trans | T2 | Scientist | LKP inside NPC zone | Zone AI | I |
| `Trans_IsTargetOnNavmesh_Slow.cs` | Trans | T2 | All | Target on navmesh | Path validity | I |
| `Trans_IsTargetProtectedByMount.cs` | Trans | T3 | Humanoid | Target on mount | Combat fairness | I |
| `Trans_IsTargetRunning.cs` | Trans | T2 | All | Target sprinting | Pursuit tuning | I |
| `Trans_IsTargetTooFarFromWater.cs` | Trans | T4 | Croc | Distance from water | Croc | I |
| `Trans_IsWatchedByTarget.cs` | Trans | T3 | All | Being watched | Stealth | I |
| `Trans_Lambda.cs` | Trans | T5 | FSM-Shared | Custom predicate hook | Extension point | M |
| `Trans_Or.cs` | Trans | T2 | FSM-Shared | Boolean OR | Composition | I |
| `Trans_RandomChance.cs` | Trans | T3 | FSM-Shared | Random branch | Variation | I |
| `Trans_SeesFood.cs` | Trans | T4 | Animal | Sees food | Animal | I |
| `Trans_TargetCamping.cs` | Trans | T3 | Humanoid | Target stationary / camping | Punish camping | I |
| `Trans_TargetInFront.cs` | Trans | T2 | All | Target in frontal arc | Melee / aiming | I |
| `Trans_TargetInRange.cs` | Trans | T2 | All | Distance range | Engagement | I |
| `Trans_TargetIsInSafeZone.cs` | Trans | T3 | Humanoid | Safe zone check | PvE areas | I |
| `Trans_TargetIsLowHealth.cs` | Trans | T2 | All | Execute threshold | Aggression | I |
| `Trans_TargetIsNearFire.cs` | Trans | T3 | All | Near fire | Environmental | I |
| `Trans_TargetIsUndergeared.cs` | Trans | T3 | Humanoid | Gear-based aggression | Risk assessment | I |
| `Trans_TargetLkpInRange.cs` | Trans | T2 | All | LKP distance | Search / chase | I |
| `Trans_TargetLost.cs` | Trans | T1 | All | Lost target | Disengage / search | I |
| `Trans_TargetSurprised.cs` | Trans | T2 | Scientist | Target surprised state | Opener | I |
| `Trans_TooFarFromWater.cs` | Trans | T4 | Croc | Self far from water | Croc | I |
| `Trans_Triggerable.cs` | Trans | T1 | FSM-Shared | External trigger | Hit/death graphs (**V**) | V |
| `Trans_Triggerable_HitInfo.cs` | Trans | T1 | FSM-Shared | HitInfo trigger | Hurt/death (**V**) | V |

---

## 6. Minimum ScientistNPC2 / humanoid GEN2 working set (“start here”)

Use this as the **first inspection bundle** when extending GrimmNPC2 beyond the current scaffold.

| Layer | Files (non-exhaustive but ordered) |
|-------|-------------------------------------|
| **Entity root** | `ScientistNPC2.cs`, `BaseNPC2.cs` |
| **FSM definition** | `Scientist2FSM.cs`, `Scientist2FSM_Heavy.cs`, `Scientist2FSM_Shotgun.cs`, `FSMComponent.cs`, `FSMStateBase.cs`, `FSMTransitionBase.cs`, `FSMPayload.cs`, `EFSMStateStatus.cs`, `LockState.cs` |
| **Perception / targeting** | `SenseComponent.cs`, `BaseEntityTargettingExtensions.cs` |
| **Movement** | `LimitedTurnNavAgent.cs`, `NavMeshPathEx.cs` (helpers), `RootMotionPlayer.cs` |
| **Combat** | `NpcShootingComponent.cs`, `NPCRangeConstants.cs`, `FireConstants.cs` |
| **Cover / reposition** | `CoverComponent.cs`, `Cover.cs`, `CoverGroup.cs`, `NpcCoverManager.cs`, `State_MoveToCoverHiddenFromTarget.cs`, `State_MoveToPointWithLosOnTarget.cs`, `State_StayInCover.cs` |
| **Zone / encounter** | `NpcZoneComponent.cs`, `NpcZone.cs`, `NPCEncounterTimer.cs`, `Trans_IsTargetLkpInOurZone.cs` |
| **Bark / VO** | `NpcBarkManager.cs`, `NpcBarkComponent.cs`, `NPCVoiceline*.cs`, `ENPCVoicelineCategory.cs`, `ENpcVoicelineImportance.cs` |
| **Core scientist states** | `State_PatrolIdle.cs`, `State_Patrol.cs`, `State_Search.cs`, `State_ScientistRush.cs`, `State_ScientistDead.cs` **or** `State_Dead.cs` (variant), `State_DogFight.cs`, `State_ScientistSurprised.cs`, `State_Flank.cs`, `State_ThrowGrenade.cs`, `State_ScriptedNade.cs`, `State_Nothing.cs` |
| **Core transitions** | `Trans_HasTarget.cs`, `Trans_TargetLost.cs`, `Trans_CanSeeTarget.cs`, `Trans_Triggerable.cs`, `Trans_Triggerable_HitInfo.cs`, `Trans_TargetInRange.cs`, `Trans_TargetLkpInRange.cs` |

---

## 7. State and transition interpretation (behavior buckets)

Grouped by **purpose**; **scientist-relevant** vs **animal/predator/water** called out.

| Bucket | Examples (`State_*` / `Trans_*`) | Scientist / humanoid? | Notes |
|--------|----------------------------------|------------------------|-------|
| **Patrol / roam / search** | `State_Patrol`, `State_PatrolIdle`, `State_Roam`, `State_Search`, `Trans_TargetLost`, `Trans_TargetLkpInRange` | **Yes** | Core idle-to-combat pipeline |
| **Combat engagement** | `State_DogFight`, `State_Attack`, `State_MoveToTarget`, `State_ScientistRush`, `Trans_HasTarget`, `Trans_CanSeeTarget`, `Trans_TargetInRange`, `Trans_IsMuzzleClear_Slow`, `Trans_IsReloading` | **Yes** | Scientist FSM centers on rush + dogfight |
| **Cover / flank** | `State_MoveToCoverHiddenFromTarget`, `State_StayInCover`, `State_Flank`, `State_MoveToPointWithLosOnTarget`, `Trans_IsFlankedByTarget` | **Yes** | Humanoid tactical play |
| **Surprise / opener** | `State_ScientistSurprised`, `Trans_TargetSurprised` | **Yes** | Scientist-specific |
| **Grenades** | `State_ThrowGrenade`, `State_ScriptedNade`, `Trans_CanThrowGrenade` | **Yes** | Heavy/scripted variants |
| **Death / hurt** | `State_ScientistDead`, `State_Dead`, `State_Hurt*`, `Trans_IsTargetDown`, `Trans_IsHealthBelowPercentage`, `Trans_Triggerable_HitInfo` | **Yes** | Heavy uses `State_Dead` (**V**) |
| **Swimming / water** | `Trans_IsSwimming`, `Trans_IsInWater_Slow`, `Trans_IsTargetInWater`, `Trans_IsTargetTooFarFromWater`, `Trans_TooFarFromWater`, `State_LandOrSwimAttack`, `State_TryAmbushUnderwater` | Partial | Scientists may use water transitions on mixed maps; **croc-heavy** otherwise |
| **Food / feeding** | `State_ApproachFood`, `State_EatFood`, `Trans_SeesFood` | **Animal** | Not scientist-first |
| **Fire / environment** | `State_ApproachFire`, `State_FleeFire`, `Trans_TargetIsNearFire` | **Situational** | Niche |
| **Croc-specific** | `State_Croc*`, `State_GoBackToWater`, `State_BringFoodBackToWater`, `Trans_CrocHasStraightPathToTarget`, water-distance transitions | **No** (croc) | Tier 4 for GrimmNPC2 |
| **Tiger / wolf / generic predator** | `State_Circle*`, `State_Roar`, `State_Growl`, `State_Howl`, `State_WolfHurt`, `State_DragCorpse`, `State_DeadlyAttack` | **No** | Tier 4 unless you port predators |
| **Animation / scripting** | `State_PlayAnimation*`, `State_Nothing` | **Shared** | `State_Nothing` is structural (**V**) |

---

## 8. What GrimmNPC2 is still missing relative to a real GEN2 implementation

GrimmNPC2 today (**I** from `GrimmNPC2.cs` + `SpawnPatches2.cs`): pending/custom data, netId registry, **`SenseComponent`** + **`LimitedTurnNavAgent`** helpers, **`ScientistNPC2` spawn postfix** (home position, initial `SetDestination`, name). **No** ownership of the stock FSM graph.

Likely **missing subsystem categories** (check against §3–6):

| Gap | Why it matters |
|-----|----------------|
| **FSM lifecycle** | `FSMComponent.SetFsmActive` / work-queue membership — custom spawns may need correct enable/disable (**M** until verified for your prefab path). |
| **Scientist FSM component graph** | `Scientist2FSM` expects **Blackboard**, **NpcBarkManager**, **NpcZoneComponent**, **NPCEncounterTimer**, **NpcShootingComponent**, **RootMotionPlayer**, **SenseComponent**, **LimitedTurnNavAgent** — GrimmNPC2 does not yet configure or validate these (**V** attributes). |
| **Blackboard coordination** | Shared flags/counters (`Trans_HasBlackboardBool`, `Trans_BlackboardCounterGte`) — plugin logic may need to align with stock transitions. |
| **Combat / shooting** | `NpcShootingComponent` not referenced from GrimmNPC2 API — external plugins cannot yet tune shooting without new hooks/wrappers. |
| **Cover pipeline** | `CoverComponent` / `NpcCoverManager` / cover groups — not exposed; scientist states depend on them for full behavior. |
| **Zones** | `NpcZone` / `NpcZoneComponent` — no plugin integration for custom zones or tests. |
| **Bark / noise / VO** | No hooks for encounter voice or noise investigation parity with stock FSM. |
| **State orchestration** | No plugin-level **state** or **transition** injection; all behavior still stock-defined. |
| **Cleanup** | `UnregisterNpc` exists; no guaranteed pairing with entity **Kill/Destroy** or **FSM stop** in snippets shown — risk of stale registry (**I**). |
| **Targeting vs senses** | `TrySetTarget` used; full parity with Gen1 custom targeting may need blackboard + FSM awareness. |

---

## 9. Manual assembly verification queue

Prioritized for **GrimmNPC2 / `ScientistNPC2`**. Inspect top-down; after each inspection, update §5 entry (**V**/notes) and adjust tier if needed.

**Tier A — must read first**

1. `ScientistNPC2.cs`
2. `BaseNPC2.cs`
3. `FSMComponent.cs`
4. `FSMStateBase.cs`
5. `FSMTransitionBase.cs`
6. `Scientist2FSM.cs`
7. `Scientist2FSM_Heavy.cs`
8. `Scientist2FSM_Shotgun.cs`
9. `SenseComponent.cs`
10. `LimitedTurnNavAgent.cs`
11. `NpcShootingComponent.cs`
12. `BlackboardComponent.cs`
13. `CoverComponent.cs`
14. `NpcZone.cs`
15. `NpcZoneComponent.cs`

**Tier B — combat / cover / helpers**

- `NpcCoverManager.cs`, `Cover.cs`, `CoverGroup.cs`, `BaseEntityTargettingExtensions.cs`, `NPCRangeConstants.cs`, `NpcBarkManager.cs`, `NpcBarkComponent.cs`, `NPCEncounterTimer.cs`, `RootMotionPlayer.cs`, `FSMSlowTransitionBase.cs`, `Trans_IsMuzzleClear_Slow.cs`, `Trans_CanSeeTarget.cs`, `Trans_HasTarget.cs`, `Trans_TargetLost.cs`

**Tier C — ambiguous / content-dependent (T5-heavy)**

- `NPCAnimController.cs`, `NpcLevelScript.cs`, `NpcLevelTrigger.cs`, `RustNavmeshModifierVolume.cs`, `Trans_Lambda.cs`, `AIArena2.cs`

**Predator-only (when spawning those prefabs)**

- `Wolf2*.cs`, `Crocodile*.cs`, `Tiger*.cs`, `Panther.cs`, predator `State_Croc*` / `Trans_Croc*`

---

## 10. Rules for future maintenance

1. **Never remove files** from the inventory; **T4** / low priority still stays listed. Count must remain **173** unless the game assembly folder changes (then rescan and bump the header).  
2. When you **inspect** a file, update its row: add **V**, short **verified notes**, and shrink **M** where appropriate.  
3. **Separate** verified facts (**V**) from filename inference (**I**). Do not “upgrade” **I** to **V** without reading code or framework.  
4. **Tiers** are hypotheses: change tier when assembly proof or prefab usage contradicts the table.  
5. Keep **ScientistNPC2** / humanoid relevance explicit in `Applies` / `Use` columns — predator rows stay for completeness, not emphasis.  
6. **Execution behavior** (what plugins may/must not patch) stays governed by `AI_NPC_Plugin_Execution_Framework.md`; this document **indexes** GEN2, it does not override AOR / Hard Rules.  
7. On Rust updates, diff `Rust.Ai.Gen2` and **append** new files with initial **I** or **M**; **never** silently drop renamed files — cross-reference old names in notes until resolved.

---

### Tier roll-up (counts from §5 dictionary rows — recompute after assembly changes)

| Tier | Count | Note |
|------|-------|------|
| T1 | 34 | Scientist-critical states/transitions + core FSM/entity/nav/sense/shoot/zone/bark/encounter |
| T2 | 50 | Combat completeness, cover, teams, most shared transitions, secondary states |
| T3 | 52 | Situational states/transitions, noise, positioning hints, advanced anim |
| T4 | 33 | Predators, croc/wolf/tiger-only, dev test tools, empty `AIArena2` stub |
| T5 | 4 | `NpcLevelScript`, `NpcLevelTrigger`, `RustNavmeshModifierVolume`, `Trans_Lambda` — role/tier unsafe from name alone |

*Verification: 34 + 50 + 52 + 33 + 4 = **173**.*
