# GrimmNPC - Complete Instructional Guide

## Harmony Mod Performance Philosophy

GrimmNPC uses IL (Intermediate Language) patching for direct method access without reflection overhead. Critical for performance-sensitive AI operations.

**Performance Principles**:
1. Direct IL patching (native speed, no reflection in hot paths)
2. Cache reflection lookups (PropertyInfo/FieldInfo as static fields)
3. Early exit checks (`if (!GrimmNPC.IsCustomNpc(entity)) return;`)
4. Avoid allocations in hot paths (thousands of calls/second)
5. Throttle expensive operations (time/frame-based)
6. Direct method calls in hot paths (no reflection)

**Reflection Guidelines**:
- **Acceptable**: Spawn-time setup, cached lookups, avoiding dependencies (if cached)
- **NOT Acceptable**: Hot paths (Think() 4000/sec, SetDestination() 5-10/sec), repeated lookups in loops, every-frame operations

---

## Table of Contents
1. [Project Overview](#project-overview)
2. [Project Structure](#project-structure)
3. [Core Architecture](#core-architecture)
4. [Configuration System](#configuration-system)
5. [Data Management](#data-management)
6. [Patch System](#patch-system)
7. [Complete Initialization Chain](#complete-initialization-chain)
8. [Movement Update System](#movement-update-system)
9. [AI Brain Thinking and Navigation](#ai-brain-thinking-and-navigation)
10. [Pathfinding and Navigation Integration](#pathfinding-and-navigation-integration)
11. [API Reference](#api-reference)
12. [Performance Optimizations](#performance-optimizations)
13. [Extending the Mod](#extending-the-mod)
14. [Troubleshooting](#troubleshooting)

---

## Project Overview

GrimmNPC is a Harmony mod for Rust that customizes NPC behavior. Optimized for 1000+ NPCs with 100x performance improvement over Oxide plugins.

**Features**: Direct IL patching, HomePosition/RoamRange/ChaseRange enforcement, combat enhancement (rush/strafe), swimming support, turret targeting control, assist system, dormancy management.

**Performance**: Spawn <1s (vs 27.9s), CPU ~1.2% (vs 120%), Memory ~50MB (vs 200MB), Think() 4000/sec (1000 NPCs × 4 calls/sec).

---

## Project Structure

```
GrimmNPC/
├── GrimmNPC.cs                    # Main mod class, config/data management, raiding system
├── GrimmNPC.csproj                # Project file (targets .NET Framework 4.8)
├── SpecialWeaponsHandler.cs       # Centralized special weapon handling (rockets, MGL, flamethrower, bows, satchels with cycle)
├── Patches/                        # Harmony patches directory
│   ├── SpawnPatches.cs            # NPC spawning and initialization
│   ├── ThinkPatches.cs            # AI brain think loop (hot path)
│   ├── TargetingPatches.cs        # Target selection logic
│   ├── DamagePatches.cs           # Damage handling and turret scaling
│   ├── NavigationPatches.cs       # Roam range enforcement
│   ├── SwimmingPatches.cs         # Swimming support
│   └── TurretTargetingPatches.cs  # Turret targeting prevention
├── config.json                     # Default config (legacy location)
├── data.json                       # Runtime data (used NPC user IDs)
├── README.md                       # User-facing documentation
├── DEBUG_GUIDE.md                  # Debugging guide
└── INSTRUCTIONAL.md                # This file
```

### Build Output
- **Source**: `.cursor/HarmonyMods/GrimmNPC/`
- **Compiled DLL**: `bin/Release/net48/GrimmNPC.dll`
- **Deployment**: Copy to `HarmonyMods/GrimmNPC.dll` (root directory)

### Configuration Files
- **Config**: `HarmonyConfig/GrimmNPC.json` (auto-migrated from old location)
- **Data**: `.cursor/HarmonyMods/GrimmNPC/data.json`

---

## Core Architecture

### Main Class: `GrimmNPC`

**Location**: `GrimmNPC.cs`

**Purpose**: Core mod class implementing `IHarmonyModHooks` interface.

**Key Responsibilities**:
- Mod lifecycle management (OnLoaded, OnUnloaded)
- Configuration loading/saving
- Data persistence (used NPC user IDs)
- NPC registration/unregistration
- Public API for patches

**Key Static Members**:
```csharp
public static GrimmNPC Instance { get; private set; }
public static readonly ulong CUSTOM_NPC_SKIN_ID = 11162132011012UL;
```

**Key Private Members**:
```csharp
private readonly Dictionary<ulong, CustomNpcData> _npcs;  // Registered NPCs (keyed by netId)
private readonly HashSet<ulong> _usedNpcUserIds;          // Used user IDs (prevents duplicates)
private readonly Dictionary<int, CustomNpcData> _pending; // Pending registrations (keyed by instance ID)
```

**Key Methods**:
- `OnLoaded(OnHarmonyModLoadedArgs args)` - Called when mod loads
- `OnUnloaded(OnHarmonyModUnloadedArgs args)` - Called when mod unloads
- `RegisterPending(BaseEntity entity, CustomNpcData npcData)` - Register NPC data BEFORE spawn (recommended)
- `RegisterNpc(ulong netId, CustomNpcData npcData)` - Register custom NPC AFTER spawn
- `UnregisterNpc(ulong netId)` - Unregister NPC
- `GetNpcData(ulong netId)` - Get NPC data by network ID
- `IsCustomNpc(BaseEntity entity)` - Check if entity is custom NPC
- `GetConfig()` - Get current configuration
- `ValidateHomePosition(CustomNpcData npcData, Vector3 fallbackPosition)` - Validate and fix HomePosition (prevents clustering)
- `GetDistanceFromHome(CustomNpcData npcData, Vector3 currentPosition)` - Get horizontal distance from HomePosition (prevents Y-axis issues)

**Weapon Handling**:
- `SpecialWeaponsHandler` centralizes special weapon logic (rockets, grenade launchers, flamethrowers, bows, satchels, blowpipes, crossbows, flintlock)
- **Integrated with HumanNPC's targeting system**: Uses `GetAttackEntity()`, `EngagementRange()`, `IsTargetInRange()`, `CanSeeTarget()`, and `ModifyAIAim()` for accurate targeting across all weapon types
- Weapon mechanics are shared across combat/raiding to avoid raid-gated behavior
- Satchel throwing uses coroutine pattern (based on RaidingZombies) with proper wind-up, aim stabilization, and cycle management
- Satchel cycle state machine: Throw → Wait Explosion → Melee Attack → Retreat → Repeat
- **Rocket launcher firing**: Uses BotReSpawn's approach - gets launcher entity, checks/decrements ammo, creates projectile from ammo type, signals launcher to fire
- **Universal weapon support**: All weapon types (BaseProjectile, BaseLauncher, AttackEntity) benefit from HumanNPC's native targeting methods

**NPC Identification**:
- Custom NPCs are identified by `skinID == CUSTOM_NPC_SKIN_ID` (11162132011012UL)
- This allows patches to quickly filter custom NPCs from vanilla NPCs

### Data Classes

#### `NpcConfig`
**Location**: `GrimmNPC.cs`

**Purpose**: Global configuration for all NPCs.

**Properties**:
- `CanTargetAnimal`, `CanTargetNpc`, `CanTargetSleepingPlayer`, `CanTargetWoundedPlayer`, `CanTargetSafeZonePlayer` - Targeting flags
- `PreventScarecrowTargeting` - Block targeting scarecrows
- `ExcludedTargetTypes` - Entity type names to exclude from targeting and assist (e.g., ["ZombieNPC"])
- `ForceRespectAiDormant` - Respect server AI dormant setting
- `DefaultSleepDistance` - Default dormancy distance (160m)
- `Prefab` - Default NPC prefab path
- `EnableDebugLogging` - Debug logging (performance impact)
- `EnableNavMeshValidation` - Unused, requires UnityEngine.AI
- `EnableAssistCallouts` - Enable assist system
- `AssistRange` - Assist callout range (100m)

#### `CustomNpcData`
**Location**: `GrimmNPC.cs`

**Purpose**: Per-NPC configuration data.

**Properties**:
- `UserID` - Unique user ID (prevents duplicates)
- `Name` - Display name (applied to `displayName` during spawn)
- `Health`, `DamageScale`, `TurretDamageScale`, `AimConeScale` - Combat stats
- `CanBeTargetedByAutoTurrets`, `CanBeTargetedByGunTraps`, `CanBeTargetedByFlameTurrets`, `CanBeTargetedByAPC` - Turret targeting flags
- `HomePosition` - Spawn/home position (CRITICAL for clustering prevention, auto-validated if zero/invalid)
- `RoamRange` - Max horizontal distance from home (50m default, enforced 1Hz when NOT in combat)
- `ChaseRange` - Max horizontal chase distance from home (100m default, filters targets)
- `SenseRange` - Detection range (50m default, sets `brain.SenseRange`, re-initializes sense system)
- `CanSleep`, `SleepDistance` - Dormancy settings (160m default)
- `AreaMask` - NavMesh area mask (1 terrain default, auto-configured to 25 for monuments)
- `AgentTypeID` - NavMesh agent type ID (-1372625422 terrain default, auto-configured for monuments)
- `NavmeshLocked` - If true, navmesh configuration is locked and dynamic switching will not override it. Default: false (allows dynamic switching for roaming NPCs)
- `CanSwim`, `SwimmingSpeedMultiplier` - Swimming settings (0.4f default, swims at surface 1.1m below)
- `AlwaysStrafeInCombat` - Continuous lateral movement (false default, 60Hz vs 15Hz smart strafe)
- `StrafeRadius` - Lateral strafe distance (3f default, 2-4m recommended)
- `StrafeInterval` - Combat movement update interval in frames (1 when AlwaysStrafe=true, 4 otherwise)
- `StrafeOnlyWhenAttacking` - Only strafe while attacking (true default, prevents wall-hugging)
- `IsRaidingNpc` - Enable raiding behavior (false default, see Raiding API section)
- `RaidSettings` - Raiding configuration (see Raiding API section)
- `RaidGoalActive` - Active Raid Goal flag (false default, see Raiding API section)
- `RaidGoalPosition` - Raid Goal position (Vector3.zero default, fallback if entity invalid)
- `RaidGoalEntityId` - Raid Goal entity network ID (0UL default, primary target)

#### NPC Initialization - Available Settings (External Plugins)
These are **plugin-owned** settings that are commonly used during NPC initialization. These are the ONLY properties that external plugins need to set. All other configuration is handled automatically by GrimmNPC.

**Plugin-Owned Properties** (set via reflection):
- `UserID` - Unique user ID (prevents duplicates)
- `Name` - Display name
- `Health` - NPC health
- `HomePosition` - Spawn/home position (CRITICAL - prevents clustering)
- `RoamRange` - Max horizontal distance from home
- `ChaseRange` - Max horizontal chase distance from home
- `SenseRange` - Detection range
- `DamageScale` - Damage multiplier
- `TurretDamageScale` - Turret damage multiplier
- `AimConeScale` - Aim cone multiplier
- `AreaMask` - NavMesh area mask (default: 1 for terrain, triggers auto-detection)
- `AgentTypeID` - NavMesh agent type ID (default: -1372625422 for terrain, triggers auto-detection)
- `CanSwim` - Swimming enabled
- `SwimmingSpeedMultiplier` - Swimming speed multiplier
- `AlwaysStrafeInCombat` - Continuous lateral movement
- `StrafeRadius` - Lateral strafe distance
- `StrafeInterval` - Combat movement update interval
- `StrafeOnlyWhenAttacking` - Only strafe while attacking
- `IsRaidingNpc` - Enable raiding behavior
- `RaidSettings` - Raiding configuration
- `RaidGoalActive` - Active Raid Goal flag
- `RaidGoalPosition` - Raid Goal position
- `RaidGoalEntityId` - Raid Goal entity network ID
- And other `CustomNpcData` properties as needed

**GrimmNPC-Owned Configuration** (managed automatically, plugins should NOT set):
- `NpcConfig` properties (CanTargetAnimal, CanTargetNpc, ExcludedTargetTypes, AssistRange, etc.) - These are global settings managed by GrimmNPC's config file. Plugins do NOT need to configure these.

**Global Settings** (not per-NPC, but commonly referenced):
- `InfiniteAmmo` (global, default `true`) - Set via `NpcConfig.InfiniteAmmo` in GrimmNPC's config file, not per-NPC

**NavMesh Auto-Detection**: 
- **Default behavior**: Defaults (AreaMask=1, AgentTypeID=-1372625422) trigger auto-detection during spawn
- **Manual override**: Set `AreaMask`/`AgentTypeID` manually before registration to skip auto-detection
- **Plugin integration**: Plugins (BetterNpc/BossMonster) intentionally use defaults to trigger auto-detection
- **Note**: Game (BradleyAPC) does NOT auto-detect - uses hardcoded parameters. GrimmNPC's auto-detection is a feature.

#### `NpcData`
**Location**: `GrimmNPC.cs`

**Purpose**: Runtime data persistence.

**Properties**:
- `UsedNpcUserIds` - List of used user IDs (prevents duplicate NPCs)

---

## Configuration System

**Location**: `HarmonyConfig/GrimmNPC.json` (auto-migrated from legacy `.cursor/HarmonyMods/GrimmNPC/config.json`)

**Loading**: `LoadConfig()` checks primary location → legacy → creates default → saves to primary.

**Access**: `GrimmNPC.GetConfig()` - cached in hot paths (re-checked every 5 seconds).

**Default Config**: All `CanTarget*` false, `PreventScarecrowTargeting` true, `ExcludedTargetTypes` empty, `ForceRespectAiDormant` false, `DefaultSleepDistance` 160m, `EnableDebugLogging` false, `EnableAssistCallouts` true, `AssistRange` 100m.

---

## Data Management

**Location**: `.cursor/HarmonyMods/GrimmNPC/data.json` (saved on mod unload)

**Data**: `UsedNpcUserIds` list. Pending registrations are NOT persisted (only used during spawn).

**NPC Registration**:
- `RegisterPending(entity, npcData)` - **RECOMMENDED**: Register BEFORE spawn (when netId=0). Stored in `_pending` keyed by instance ID. Consumed during ServerInit to prevent default 50m RoamRange.
- `RegisterNpc(netId, npcData)` - Register AFTER spawn (when netId available). Stored in `_npcs` keyed by netId. Adds UserID to `_usedNpcUserIds`.

**Critical**: Pending registration ensures ServerInit has correct data. HomePosition must be set correctly to prevent clustering.

**Unregistration**: `UnregisterNpc(netId)` - Removes from `_npcs` and `_usedNpcUserIds`.

---

## Patch System

### Patch Architecture

All patches use Harmony's `[HarmonyPatch]` attribute. Patches are organized by functionality in the `Patches/` directory.

### Patch Types

#### 1. SpawnPatches.cs

**Purpose**: NPC spawning and initialization. Postfix on `ScientistNPC.ServerInit()` and `BaseAIBrain.InitializeAI()`.

**Functionality**:
- In-place config (no component destruction, 27.9s → <1s)
- Pending registration: Checks `ConsumePending()` first, prevents default 50m RoamRange
- WaitForNavMesh gate: Checks `AI.move` and `MonumentNavMesh.IsBuilding` to prevent spawn thrash
- Configures: HomePosition, health/damage/name (applies to `displayName`), brain (SenseRange, TargetLostRange)
- **CRITICAL**: Re-initializes sense system after setting SenseRange to ensure player detection works immediately. Sets `HostileTargetsOnly=true` and `SenseTypes=EntityType.Player` if not already configured, then calls `Senses.Init()` and `Senses.Update()` to start detecting targets.
- Auto NavMesh detection: Pattern monument → building block → terrain. Triggers on default values (AreaMask=1, AgentTypeID=-1372625422). Uses `BaseNavigator.GetNavMeshAgentID("Humanoid")` for monuments (matches game's approach). **Note**: Game does NOT auto-detect - uses hardcoded parameters. GrimmNPC's auto-detection is a feature that plugins intentionally use.
- Navigator: CanUseBaseNav/NavMesh/AStar, DefaultArea (HumanNPC for monuments, Walkable for terrain), MoveTowardsSpeed, FaceMoveTowardsTarget, AreaMask, AgentTypeID
- NavMeshAgent: Cached reflection, sets `areaMask`, `agentTypeID`, `updateRotation=false`, `updatePosition=false`
- Navigator unpause: Unpauses navigator, forces `DetermineNavigationType()` if None

**Key Methods**: `ApplyCustomConfig()`, `DetectAndConfigureNavMesh()`, `DetectAndConfigureMonumentNavMesh()`, `IsPositionOnBuildingBlock()`, `IsWaitingForNavMesh()`

**Requirements**: `using ConVar;` (AI.move), `using UnityEngine;` (use `UnityEngine.Physics.Raycast()` not `ConVar.Physics`)

#### 2. ThinkPatches.cs

**Purpose**: AI brain think loop (HOT PATH - 4000 calls/second). Postfix on `BaseAIBrain.Think()`.

**Functionality**:
- Dormancy management (respects `ForceRespectAiDormant`)
- Roam enforcement (HomePosition/RoamRange, only when NOT in combat)
- Combat enhancement: Always-Strafe (continuous, every frame, 0.1m epsilon) or Smart Strafe (LOS/distance checks, every 4th frame, 0.5m epsilon). Uses `HumanNPC.CanSeeTarget()` for LOS
- **Dynamic navmesh switching** (1Hz, only for NPCs with NavmeshLocked=false): Automatically updates AreaMask/AgentTypeID based on current position. Enables NPCs to roam freely between monuments, terrain, and bases without NavMesh errors. Detection: monument bounds → AreaMask=25, building blocks/terrain → AreaMask=1. Only updates when navmesh type changes. Logs only on actual changes. This matches the behavior of other plugins (BotReSpawn, FrankensteinPet) that use BaseNavigator's automatic navigation type detection.
- **Wake-up escalation**: If NPC has valid target in memory within SenseRange but LOS is false for ≥1.5-2 seconds, forces Chase state. Requires target stability (same target for ≥1.5s) and throttles state forcing (once per 2 seconds). Prefers Chase with cautious movement over Combat aim lock. Never applies facing override when LOS is false - sets move destination to last known position instead.
- **Raid Goal validation**: Validates RaidGoalEntityId and RaidGoalPosition, clears if invalid
- **Idle behavior**: Stops navigation, rotation, and destination setting when truly idle (no valid target, no roam destination, no raid task, no assist call active). NPCs with Raid Goal are never idle - they always have a valid task.
- Assist system (calls for help on combat entry)
- Debug logging (throttled 2s, destination tracking: Combat/Chase/Roam/ReturnHome/None)

**Key Methods**: `ProcessCustomThink()`, `EnforceRoamRange()` (horizontal XZ), `EnhanceCombatBehavior()` (uses `target.CenterPoint()`), `UpdateNavmeshForCurrentPosition()` (dynamic navmesh switching), `CallForAssist()`, `CheckWakeUpEscalation()` (target stability, state forcing), `ValidateRaidGoal()` (validates Raid Goal), `HandleIdleBehavior()` (stops NPC when truly idle)

**Performance**: Config cached (5s), dormancy 0.5s, roam 1s, navmesh fix 1s, debug 2s, combat every 4th frame, wake-up check 1.5-2s intervals, state force throttle 2s. Uses horizontal distances to prevent Y-axis issues. Stationary NPCs (RoamRange <= 5m) stop at home.

#### 3. TargetingPatches.cs

**Purpose**: Custom target selection. Prefix/postfix on `HumanNPC.GetBestTarget()`.

**Functionality**: Replaces vanilla targeting. Respects `CanTarget*` flags, excludes `ExcludedTargetTypes`, enforces ChaseRange from HomePosition, prevents scarecrow targeting. **Raid Goal Override**: For raiding NPCs with `RaidGoalActive`, prioritizes Raid Goal entity over player targets.

**Method**: `GetCustomBestTarget()` - iterates memory, filters by exclusion → `CanTargetEntity()` → ChaseRange (horizontal distance), scores by distance/LOS. **Raid Goal Check**: If `IsRaidingNpc` and `RaidGoalActive`, first attempts to retrieve `RaidGoalEntity` from `BaseNetworkable.serverEntities.Find()`. If valid and within range, returns as primary target, overriding player targeting.

**Performance**: Config cached (5s), fast memory iteration, debug throttled (2s). Uses `HumanNPC.CanSeeTarget()` for LOS. Manual 2D distance: `Mathf.Sqrt(diff.x * diff.x + diff.z * diff.z)` (Vector3Ex unavailable).

#### 4. DamagePatches.cs

**Purpose**: Damage and turret scaling. Postfix on `BaseCombatEntity.OnAttacked()`.

**Functionality**: Turret damage scaling (`TurretDamageScale`), turret targeting prevention (`CanBeTargetedBy*` flags), wakes NPC on player attack.

**Method**: `ProcessCustomDamage()`. Supports AutoTurret, GunTrap, FlameTurret.

#### 5. NavigationPatches.cs

**Purpose**: Enforce roam range. Prefix on `BaseNavigator.SetDestination()`.

**Functionality**: 
- Clamps destination to RoamRange boundary using horizontal (XZ) distances. Y-axis fix: Base Navigation forces Y to NPC's current Y. NavMesh preserves Y but falls back to HomePosition.Y if delta > 100m.

**Performance**: <0.001ms per call. Intercepts before pathfinding executes.

#### 6. SwimmingPatches.cs

**Purpose**: Swimming support. Prefixes on `BaseNavigator.IsSwimming()`, `GetTargetSpeed()`, `UpdatePositionAndRotation()`, `CanEnableNavMeshNavigation()`.

**Functionality**: Checks `CanSwim` and `waterLevel > 0.75f`. Applies `SwimmingSpeedMultiplier`, custom movement (Vector3.MoveTowards), constrains Y to water surface - 1.1m, disables NavMesh when swimming.

**Requirements**: `UnityEngine.PhysicsModule.dll` reference. Use `UnityEngine.Physics.Raycast()` (not `ConVar.Physics`). Cached FieldInfo for `currentSpeedFraction`.

**Note**: GrimmNPC NPCs swim at surface (1.1m below), unlike vanilla NPCs.

#### 7. TurretTargetingPatches.cs

**Purpose**: Prevent turret targeting. Prefix on `AutoTurret.ShouldTarget()`.

**Functionality**: Blocks AutoTurret targeting if `CanBeTargetedByAutoTurrets` is false. Note: GunTrap/FlameTurret use `CheckTrigger()` (not patched).

---

## Complete Initialization Chain

### GrimmNPC Integration with Rust Initialization

GrimmNPC patches integrate into Rust's NPC initialization chain at specific points. Understanding this flow is critical for debugging spawn issues and understanding when patches execute.

**Initialization Flow with GrimmNPC Patches:**
```
BaseNetworkable.ServerInit()
  → BasePlayer.ServerInit()
    → NPCPlayer.ServerInit()
      → [POTENTIAL] BaseNavigator.Init() (if !LegacyNavigation, called directly in NPCPlayer.ServerInit)
      → HumanNPC.ServerInit()
        → ScientistNPC.ServerInit()
          → [GrimmNPC] ScientistNPC_ServerInit_Patch.Postfix()
            → ApplyCustomConfig()
              → Configure Health, Damage, Name
              → Configure Brain (SenseRange, TargetLostRange)
              → Configure Navigator (CanUseBaseNav, CanUseNavMesh, DefaultArea, AreaMask, AgentTypeID)
          → BaseAIBrain.Start()
            → BaseAIBrain.InitializeAI()
              → [IF NOT ALREADY INITIALIZED] BaseNavigator.Init()
                → BaseNavigator.PlaceOnNavMesh(2f)
                  → BaseNavigator.GetNearestNavmeshPosition()
                    → NavMesh.SamplePosition()
              → [GrimmNPC] BaseAIBrain_InitializeAI_Patch.Postfix()
                → Unpause Navigator (if paused)
                → Determine Navigation Type (if None)
                → Set Navigation Type
```

**CRITICAL TIMING ISSUE**: `NPCPlayer.ServerInit()` may call `BaseNavigator.Init()` before `ScientistNPC.ServerInit()` completes, meaning GrimmNPC's patch runs after `Init()` may have been called.

**Impact**: If `Init()` called early:
- `PlaceOnNavMesh()` uses wrong `defaultAreaMask` (from default `DefaultArea`)
- `navMeshQueryFilter.agentTypeID` uses wrong value (from default Agent settings)
- NPC may fail to place on navmesh or use wrong navmesh type

**Solution**: GrimmNPC sets navigator properties AFTER potential early `Init()` call:
1. **DefaultArea Update**: If `DefaultArea` changed after `Init()`, manually updates `defaultAreaMask` via reflection
2. **navMeshQueryFilter Update**: If `Init()` already called, updates `navMeshQueryFilter.agentTypeID` and `areaMask` to match new Agent settings
3. **Agent Properties**: Sets `Agent.areaMask` and `Agent.agentTypeID` via reflection (always works)

This ensures correct navmesh configuration regardless of timing.

### GrimmNPC Patch Execution Points

#### 1. SpawnPatches.cs - ServerInit Postfix

**When**: After `ScientistNPC.ServerInit()` completes

**What Happens**:
1. **NPC Identification**: Checks `skinID == CUSTOM_NPC_SKIN_ID` (11162132011012UL)
2. **Data Retrieval**: Gets or creates `CustomNpcData` from registration
3. **HomePosition Setup**: Sets HomePosition to spawn position (CRITICAL for clustering prevention)
4. **Component Configuration**: Applies config to existing components WITHOUT destroying them

**Key Configuration Steps**:
- Health/Damage: Sets `startHealth`, `_health`, `damageScale`
- Name: Applies `npcData.Name` to `displayName` (for map markers)
- Brain: Sets `SenseRange`, `TargetLostRange = SenseRange * 2f`
- **Sense System Re-initialization**: CRITICAL - Re-initializes `Senses.Init()` after setting SenseRange to ensure player detection works immediately. Sets `HostileTargetsOnly=false` (CRITICAL: allows NPCs to detect all players, not just those who have attacked) and `SenseTypes=EntityType.Player`, then forces immediate `Senses.Update()` to start detecting targets. This fixes the issue where NPCs couldn't detect players until shot.
- Auto NavMesh Detection: If defaults (AreaMask=1, AgentTypeID=-1372625422), detects monument → building block → terrain. Uses `BaseNavigator.GetNavMeshAgentID("Humanoid")` for monuments
- Navigator: Enables CanUseBaseNav/NavMesh/AStar, sets DefaultArea (HumanNPC for monuments, Walkable for terrain), MoveTowardsSpeed, FaceMoveTowardsTarget
- NavMeshAgent: Sets `areaMask`, `agentTypeID` via reflection, `updateRotation=false`, `updatePosition=false`
- Post-Init handling: Updates `defaultAreaMask` and `navMeshQueryFilter` if `Init()` was already called
- **Navmesh Unlocking**: After spawn-time configuration completes, navmesh remains unlocked (`NavmeshLocked=false`) to allow dynamic switching. This enables NPCs to roam freely between monuments, terrain, and bases without NavMesh errors.

**Timing**: Runs after components initialized but before `BaseAIBrain.InitializeAI()`.

#### 1a. SpawnPatches.cs - InitializeAI Postfix

**When**: After `BaseAIBrain.InitializeAI()` completes.

**Functionality**: Unpauses navigator (`paused=false`), forces `DetermineNavigationType()` if `CurrentNavigationType=None`, sets navigation type. Uses cached reflection.

**WaitForNavMesh Gate**: Checks `AI.move` and `MonumentNavMesh.IsBuilding` before applying config (prevents spawn thrash).

**NavMesh Placement**: GrimmNPC doesn't call `PlaceOnNavMesh()` directly. Rust's `BaseNavigator.Init()` calls it automatically.

### Registration vs. Spawn Timing

**CRITICAL**: NPC data must be available when `ServerInit` executes, otherwise defaults (50m RoamRange) are created.

**Recommended**: `RegisterPending(npc, npcData)` BEFORE `Spawn()` → stored in `_pending` → `ServerInit` consumes and registers with netId → correct data applied.

**Alternative**: `Spawn()` → `ServerInit` creates defaults (50m RoamRange) → `RegisterNpc()` too late (defaults already applied).

**Why Pending Matters**: ServerInit needs data immediately. Defaults can't be rewound. Fallback creates default `CustomNpcData` (RoamRange=50m, ChaseRange=100m, HomePosition=spawn).

### Hybrid System: NpcSpawn Extension and BetterNpc

- **BossMonster**: Spawns HumanNPC/ScientistNPC directly, sets `skinID = CUSTOM_NPC_SKIN_ID`, calls `GrimmNPC.RegisterPending(entity, npcData)` before `Spawn()`. Fully integrated with GrimmNPC (roam, chase, navmesh, damage, turret targeting).
- **Oxide.Ext.NpcSpawn**: The extension implements the full NpcSpawn plugin (kits, presets, `SpawnNpc`/`SpawnPreset`). When it spawns an NPC it now **optionally** registers with GrimmNPC via reflection: before `Spawn()` it sets `skinID`, builds `CustomNpcData` from its `NpcConfig`, and calls `GrimmNPC.RegisterPending(entity, npcData)` if the GrimmNPC mod is loaded. So NPCs spawned by BetterNpc (or any plugin using `NpcSpawn.Call("SpawnNpc", pos, config)`) get GrimmNPC roam/chase/navmesh without those plugins referencing GrimmNPC.
- **BetterNpc**: Does **not** use `oxide/data/NpcSpawn/Preset` by name. It uses its own JSON (e.g. `oxide/data/BetterNpc/Monument/Airfield.json`) and builds a config object, then calls `NpcSpawn.Call("SpawnNpc", pos, config)`. The extension then creates the NPC and, when GrimmNPC is present, registers it so GrimmNPC applies behavior. The **NpcSpawn Preset** folder is used only when something calls `SpawnPreset(position, presetName)` (e.g. preset file names like `The-Sentry.json`, `Mercenary.json`).
- **Monument name matching (BetterNpc)**: BetterNpc loads files from `BetterNpc/Monument/{fileName}.json` and spawns only when a monument’s name matches `fileName`. The monument name comes from `monument.displayPhrase.english` (e.g. "Airfield", "Launch Site"). If NPCs don’t appear at a monument, confirm the map has that monument and that `displayPhrase.english` matches the filename exactly (e.g. "Airfield" for `Airfield.json`). Temporarily enabling minimal logs or adding a one-off log of `GetNameMonument(monument)` for each monument helps verify names.

### Initialization Phases Summary

**Phase 1 (Plugin)**: Create entity, set `skinID = CUSTOM_NPC_SKIN_ID`, call `RegisterPending(npc, npcData)` BEFORE `Spawn()`. **CRITICAL**: Registration must happen before spawn or ServerInit creates defaults (50m RoamRange).

**Phase 2 (GrimmNPC ServerInit Patch)**: Checks `skinID`, gets pending registration (or creates defaults), validates HomePosition, applies in-place config (no component destruction).

**Phase 3**: Sets health/damage/name from npcData.

**Phase 4**: Sets `brain.SenseRange`, `TargetLostRange = SenseRange * 2f`, re-initializes `Senses.Init()` (CRITICAL for immediate player detection).

**Phase 5**: Auto-detects navmesh (monument → building block → terrain), configures Navigator/NavMeshAgent, sets `updateRotation=false` and `updatePosition=false` (navigator handles these).

**Phase 6 (InitializeAI Patch)**: Unpauses navigator, forces `DetermineNavigationType()` if None.

**Phase 7 (Plugin)**: Post-spawn config (brain properties, items, custom components).

---

## Movement Update System

GrimmNPC influences movement via: NavigationPatches (clamps `SetDestination()`), ThinkPatches (roam enforcement/combat enhancement), SwimmingPatches (swimming behavior).

**Rust Flow**: `StartMovementTick()` (10Hz) → `TickMovement()` → `DoMovementTick()` → `BaseNavigator.Think()` → `UpdateNavigation()` → [GrimmNPC patches].

**Movement Types**:
1. **Roam Enforcement** (ThinkPatches, 1Hz): Checks horizontal distance from HomePosition, if outside RoamRange and NOT in combat, calls `SetDestination()` with clamped position (Fast if >20m, Normal otherwise)
2. **Combat Enhancement** (ThinkPatches, every 4th frame): Optimal distance (8-12m ranged, melee range), checks destination delta (0.5m epsilon), LOS/distance checks, uses `target.CenterPoint()`, preserves Y. Types: Rush (too far), Strafe (too close, LOS check), Maintain Distance (optimal, LOS check)
3. **Dynamic Navmesh Switching** (ThinkPatches, 1Hz): Automatically updates AreaMask/AgentTypeID based on current position. Enables NPCs to roam freely between monuments, terrain, and bases. Detection: monument bounds → AreaMask=25, building blocks/terrain → AreaMask=1. Only updates when navmesh type changes. Only runs for NPCs with NavmeshLocked=false. Uses bounds-only authority for monuments (no sampling). This matches the behavior of other plugins (BotReSpawn, FrankensteinPet).
4. **Destination Clamping** (NavigationPatches, every `SetDestination()`): Prefix intercepts, uses horizontal distances, clamps to RoamRange boundary (preserves Y, XZ plane)
5. **Swimming** (SwimmingPatches, every frame): `UpdatePositionAndRotation()` prefix, `Vector3.MoveTowards()`, constrains Y (water surface - 1.1m), applies `SwimmingSpeedMultiplier`

### Movement Update Timing Summary

| Operation | Frequency | Trigger | Patch Location |
|-----------|-----------|---------|----------------|
| Movement Tick | 10Hz (0.1s) | Rust's system | N/A (not patched) |
| AI Think | 4Hz (0.25s) | Rust's system | ThinkPatches.cs (postfix) |
| Roam Enforcement | 1Hz (1.0s) | Throttled in Think() | ThinkPatches.cs |
| Dynamic Navmesh Switching | 1Hz (1.0s) | Throttled in Think() | ThinkPatches.cs |
| Combat Enhancement | 0.25Hz (4 frames) | Throttled in Think() | ThinkPatches.cs |
| Destination Clamping | On-demand | Every SetDestination() | NavigationPatches.cs |
| Swimming Movement | Every frame | When swimming | SwimmingPatches.cs |

---

## AI Brain Thinking and Navigation

### GrimmNPC's Think() Patch Integration

GrimmNPC patches `BaseAIBrain.Think()` with a postfix that executes after vanilla AI thinking completes. This allows GrimmNPC to enhance behavior without interfering with core AI logic.

**AI Thinking Flow with GrimmNPC**:
```
HumanNPC.ServerThink(delta)  // Called every 0.1s
  → BaseAIBrain.ShouldServerThink()  // Checks think rate (0.25s default)
    → BaseAIBrain.DoThink()
      → BaseAIBrain.Think(delta)
        → Senses.Update()  // Updates memory, detects targets
        → CurrentState.StateThink(delta, brain, entity)  // State-based behavior
          → ChaseState: Calls SetDestination() to chase target
          → RoamState: Calls SetDestination() to roam point
          → CombatState: Calls SetDestination() for combat movement
        → Events.Tick(delta, stateStatus)  // Processes state events
        → [GrimmNPC] BaseAIBrain_Think_Patch.Postfix()
          → ProcessCustomThink()
            → Dormancy Management (throttled 0.5s)
            → Roam Enforcement (throttled 1.0s)
            → Combat Enhancement (throttled 4 frames)
            → Assist System (on combat entry)
            → Debug Logging (throttled 2.0s)
```

### State-Based Navigation Integration

GrimmNPC does NOT patch state selection or state execution. Instead, it enhances behavior AFTER states execute:

#### 1. ChaseState Integration

**Vanilla Behavior**:
- `ChaseState.StateThink()` calls `SetDestination()` to chase target
- Uses `AIInformationZone.GetBestMovePointNear()` for pathfinding

**GrimmNPC Enhancement**:
- **NavigationPatches**: Clamps destination to RoamRange (if outside)
- **ThinkPatches**: Does NOT enforce roam range during chase (allows combat)
- **TargetingPatches**: Filters targets by ChaseRange from HomePosition

**Result**: NPCs can chase targets within ChaseRange, but destinations are clamped to RoamRange boundary.

#### 2. RoamState Integration

**Vanilla Behavior**:
- `RoamState.StateThink()` calls `SetDestination()` to roam point
- Uses `HumanPathFinder.GetBestRoamPoint()` for pathfinding

**GrimmNPC Enhancement**:
- **ThinkPatches**: Enforces RoamRange when NOT in combat (every 1s)
- **NavigationPatches**: Clamps destination to RoamRange
- **ThinkPatches**: Generates random roam points if NPC is idle and within range

**Result**: NPCs stay within RoamRange while roaming, with automatic return if they wander too far.

#### 3. CombatState Integration

**Vanilla Behavior**:
- `CombatState.StateThink()` calls `SetDestination()` for combat movement
- Uses random positions around combat start position

**GrimmNPC Enhancement**:
- **ThinkPatches**: Enhances combat behavior (rush, strafe, maintain distance)
- **ThinkPatches**: Uses horizontal XZ distances and `target.CenterPoint()`
- **ThinkPatches**: Preserves NPC's Y position (ground-based movement)
- **NavigationPatches**: Does NOT clamp combat destinations (allows full combat range)

**Result**: More dynamic combat behavior with proper distance management and no ceiling-looking issues.

### Think() Patch Method Details

#### ProcessCustomThink() - Main Processing

**Execution**: Every `BaseAIBrain.Think()` call (4Hz by default)

**Steps**:
1. **Early Exit**: Filters for custom NPCs only
2. **Config Caching**: Re-checks config every 5 seconds
3. **State Detection**: Tracks state changes for assist system
4. **Throttled Operations**: Executes expensive operations at intervals

**Performance**: 0.003ms per call (vs 0.3ms with Oxide reflection)

#### Dormancy Management

**When**: Every 0.5 seconds (throttled)

**What Happens**:
- Checks `ForceRespectAiDormant` config
- If enabled, respects server AI dormant settings
- Uses `SleepDistance` from NPC data (default 160m)

**Integration**: Works with Rust's dormancy system (`AIInformationZone` registration).

#### Roam Enforcement

**When**: Every 1.0 second (throttled), only when NOT in combat

**What Happens**:
- Uses `GrimmNPC.GetDistanceFromHome()` to calculate horizontal distance from HomePosition
- If outside RoamRange: Calculates return direction (XZ plane), calls `Navigator.SetDestination()` with clamped position
- If stationary (RoamRange <= 5m) and at home: Stops movement
- If idle and within range: Generates random roam point
- Debug logging: Logs destination with reason "ReturnHome" or "RoamPoint" (when enabled)

**Critical**: Does NOT enforce during combat/chase (allows full combat range). Uses horizontal distances.

#### Combat Enhancement

**When**: Update frequency controlled by `StrafeInterval` (default: 1 when `AlwaysStrafeInCombat=true`, 4 otherwise), only when in combat

**CRITICAL**: Only runs when: combat state (Combat/Chase/CombatStationary), target not null, and if `StrafeOnlyWhenAttacking` true then actively attacking (`svActiveItemID != 0`). Prevents wall-hugging.

**Process**: Checks combat state → gets target (early exit if null) → `StrafeOnlyWhenAttacking` gate → optimal distance (8-12m ranged, melee range) → movement type (rush/strafe/maintain).

**Always-Strafe Mode** (`AlwaysStrafeInCombat=true`): Continuous lateral movement, `StrafeRadius` (3m default), every frame (`StrafeInterval=1`), 0.1m epsilon.

**Smart Strafe Mode** (default): LOS/distance checks, every 4th frame, 0.5m epsilon.

Uses `target.CenterPoint()`, horizontal XZ distances. Debug logs destination with reason "Combat(Melee/Ranged)".

#### Assist System

**When**: On combat state entry (state change tracking).

**Process**: Checks `ExcludedTargetTypes` (skips if target excluded), finds nearby NPCs within `AssistRange` (100m), adds target to memory via `Senses.Memory.SetKnown()`, alerts to threat. Uses `BaseEntity.Query.Server.GetBrainsInSphere()`.

#### Dynamic Navmesh Switching

**When**: Every 1.0 second (throttled), only for NPCs with `NavmeshLocked=false`.

**Purpose**: Enables NPCs to roam freely between monuments, terrain, and bases without NavMesh errors. Automatically switches navmesh type based on current position.

**What Happens**:
- **Position-based detection**: Checks current position to determine required navmesh type:
  - Monument bounds → AreaMask=25, AgentTypeID from monument navmesh, DefaultArea="HumanNPC"
  - Building blocks/terrain → AreaMask=1, AgentTypeID=-1372625422, DefaultArea="Walkable"
- **Bounds-only authority**: Uses `monument.IsInBounds(position)` for monument detection (no sampling).
- **Building block detection**: Uses same raycast pattern as `BaseNavigator.DetermineNavigationType()` (LayerMask 2097152).
- **Update only on change**: Only updates AreaMask/AgentTypeID when navmesh type has changed (prevents unnecessary updates).
- **Updates all components**: Updates npcData, NavAgent, DefaultArea, and navMeshQueryFilter.
- Handles NPCs moving between different navmesh types seamlessly

**Performance**: Minimal CPU impact - throttled to 1Hz, fast bounds/raycast checks, reflection only when update is needed (only when position changes navmesh type).

**Debug Logging**: Logs only on actual changes ("Updated navmesh" message when navmesh type changes). No per-tick logging to prevent console spam.

**Navmesh Unlocking**: After spawn-time configuration completes, navmesh remains unlocked (`NavmeshLocked=false`) to allow dynamic switching. This enables seamless navigation across different navmesh types, matching the behavior of other plugins (BotReSpawn, FrankensteinPet).

---

## Pathfinding and Navigation Integration

GrimmNPC doesn't patch pathfinding systems directly. Influences via: destination clamping (RoamRange), `SetDestination()` calls, navigator configuration (BaseNav/NavMesh).

**Flow**: `State.StateThink()` → `AIInformationZone.GetBestMovePointNear()`/`GetBestRoamPoint()` → `Navigator.SetDestination()` → [GrimmNPC] NavigationPatches clamps → `BaseNavigator.SetDestination()` → `DetermineNavigationType()` → pathfinding executes → [GrimmNPC] SwimmingPatches if swimming.

**Destination Clamping**: NavigationPatches prefix intercepts, uses `GrimmNPC.GetDistanceFromHome()`, clamps to RoamRange boundary (XZ plane, preserves Y), returns clamped position. Pathfinding always operates within RoamRange.

## CRITICAL: Agent Configuration Requirements

**MUST be set BEFORE navigation operations:**

**Agent.agentTypeID**:
- Terrain: `-1372625422` (ground/island, player buildings with Base Nav fallback)
- Monument: `0` (default) or monument-specific from `GetMonumentAgentTypeID(position)`
- Detection: monument → building block → terrain default

**Agent.areaMask**:
- Terrain: `1` ("Walkable" area)
- Monument: `25` (monument navmesh area)
- Detection: monument → building block → terrain default

**Building Blocks**: `IsPositionOnBuildingBlock()` (raycast, layerMask `1 << 21`). Config: `areaMask=1`, `agentTypeID=-1372625422`, `updateRotation=false`, `updatePosition=false`, `CanUseBaseNav=true`, `CanUseNavMesh=true`. Returns `NavigationType.Base`. Pet privilege check: owner authed OR no authed players → allows navigation.

### Navigator Configuration

**SpawnPatches.cs** configures navigator during initialization following the exact order specified in `Base_NavMesh_Complete_Guide.md`:

**Initialization Order** (per Base_NavMesh_Complete_Guide.md):
1. **Detect building block** at spawn position ✓ (via `DetectAndConfigureNavMesh()` → `IsPositionOnBuildingBlock()`)
2. **Configure NavMeshAgent** (areaMask, agentTypeID, updateRotation, updatePosition) - Done AFTER Init() may have been called
   - Note: `NPCPlayer.ServerInit()` may call `BaseNavigator.Init()` before this patch runs
   - The guide states Agent properties can be set "BEFORE calling Init() or AFTER initialization"
   - GrimmNPC sets them AFTER initialization and updates `navMeshQueryFilter` if needed
3. **Call BaseNavigator.Init()** - Done by Rust (not by GrimmNPC)
4. **Enable Base Navigation flags** AFTER Init() ✓ (done in `ApplyCustomConfig()`)
5. **Verify initialization** ✓ (navigator exists check)

**Configuration**:
```csharp
navigator.CanUseBaseNav = true;   // Enable building block navigation
navigator.CanUseNavMesh = true;    // Enable navmesh navigation
navigator.CanUseAStar = true;      // Enable AStar pathfinding as fallback

// CRITICAL: DefaultArea must match navmesh type for proper sampling during placement
// PlaceOnNavMesh() → GetNearestNavmeshPosition() uses defaultAreaMask derived from DefaultArea,
// not NavMeshAgent.areaMask. This ensures successful placement.
// Follows game's pattern (BradleyAPC.SpawnScientist):
// - Monuments: DefaultArea = "HumanNPC" (matches game's approach for normal spawns)
// - Terrain: DefaultArea = "Walkable" (matches game's approach for road spawns)
// TIMING NOTE: If Init() was already called (by NPCPlayer.ServerInit()), we need to update
// defaultAreaMask manually via reflection after changing DefaultArea
string previousDefaultArea = navigator.DefaultArea;
if (npcData.AreaMask == 25)
{
    navigator.DefaultArea = "HumanNPC"; // Monument navmesh (matches game's approach)
}
else
{
    navigator.DefaultArea = "Walkable"; // Terrain navmesh (matches game's approach)
}

// If Init() was already called and DefaultArea changed, update defaultAreaMask manually
if (previousDefaultArea != navigator.DefaultArea)
{
    // Update defaultAreaMask field via reflection (uses NavMesh.GetAreaFromName() via reflection)
}

navigator.MoveTowardsSpeed = BaseNavigator.NavigationSpeed.Normal; // Movement speed preference
navigator.FaceMoveTowardsTarget = true; // Face target when moving towards it

// NavMeshAgent properties set via reflection (avoids requiring UnityEngine.AIModule reference)
navAgent.areaMask = npcData.AreaMask;        // NavMesh area mask
navAgent.agentTypeID = npcData.AgentTypeID;   // NavMesh agent type
navAgent.updateRotation = false;              // Navigator handles rotation (CRITICAL for Base Navigation)
navAgent.updatePosition = false;               // Navigator handles position (CRITICAL for Base Navigation)

// CRITICAL: Navigator handles position and rotation updates, not the Agent
// This prevents Agent from interfering with Base Navigation movement on building blocks
// Reference: Base_NavMesh_Complete_Guide.md lines 199-200, 621-623

// CRITICAL: Also update navMeshQueryFilter if Init() was already called
// Init() sets navMeshQueryFilter from Agent properties, but if Init() ran before we set Agent,
// we need to update navMeshQueryFilter manually via reflection
// Updates both navMeshQueryFilter.agentTypeID and navMeshQueryFilter.areaMask
```

**Navigator Property Configuration**:
- **CanUseAStar**: Enables AStar pathfinding as fallback
- **DefaultArea**: CRITICAL - Set based on navmesh type. `PlaceOnNavMesh()` uses `defaultAreaMask` derived from `DefaultArea`, not `NavMeshAgent.areaMask`
- **MoveTowardsSpeed**: Sets movement speed preference to Normal
- **FaceMoveTowardsTarget**: Makes NPCs face target when moving
- **StoppingDistance**: Defaults to 0.5f

**Note**: GrimmNPC uses reflection to set NavMeshAgent properties and update internal fields to avoid requiring `UnityEngine.AIModule` reference.

**Post-Init() Handling**: If `Init()` called early, GrimmNPC updates `DefaultArea`, recalculates `defaultAreaMask`, sets Agent properties, and updates `navMeshQueryFilter` via reflection.

**Navigation Type Selection**:
- Rust's `BaseNavigator.DetermineNavigationType()` automatically selects:
  - `NavigationType.Base` if on building blocks (LayerMask 2097152 - building blocks layer)
  - `NavigationType.NavMesh` if on navmesh
  - Falls back to NavMesh if BaseNav unavailable

**Base Navigation Detection**:
- GrimmNPC's building block detection matches Rust's internal logic exactly
- Implemented in `IsPositionOnBuildingBlock()` method
- Uses raycast detection from `position + Vector3.up * navTypeHeightOffset` downward with `navTypeDistance` as max distance
- Uses BaseNavigator's `navTypeHeightOffset` and `navTypeDistance` (accessed via reflection, cached, defaults to 0.5f and 1f if reflection fails)
- LayerMask: `2097152` (building blocks layer = 1 << 21)
- Validates hit entity is `BuildingBlock` or `SimpleBuildingBlock`
- Reflection cached on first use (initialized in static constructor)

**NavMesh Configuration**:
- **Terrain NavMesh**: `areaMask = 1`, `agentTypeID = -1372625422` (default), `DefaultArea = "Walkable"`
- **Monument NavMesh**: `areaMask = 25`, `agentTypeID` from `BaseNavigator.GetNavMeshAgentID("Humanoid")` (matches game's approach), `DefaultArea = "HumanNPC"`
- **Important**: AreaMask, AgentTypeID, and DefaultArea must match the navmesh type

**Result**: NPCs can navigate on both building blocks and navmesh, with automatic type selection based on position.

**Automatic NavMesh Detection**:
GrimmNPC automatically detects navmesh type and configures settings during spawn. **This is a GrimmNPC feature - the game does NOT auto-detect navmesh type.**

**Game's Approach (BradleyAPC)**:
- Does NOT auto-detect navmesh type
- Uses hardcoded `roadSpawned` boolean parameter to determine settings:
  - `roadSpawned=true` → `agentTypeID=GetNavMeshAgentID("Animal")`, `DefaultArea="Walkable"`
  - `roadSpawned=false` → `agentTypeID=GetNavMeshAgentID("Humanoid")`, `DefaultArea="HumanNPC"`
- Does NOT set `areaMask` (because BradleyAPC doesn't spawn on monuments)

**GrimmNPC's Auto-Detection** (runs in `SpawnPatches.cs` during `ApplyCustomConfig()`):

**Detection Pattern** (executed by `DetectAndConfigureNavMesh()`):
1. **Monument Detection**: Checks spawn position against `TerrainMeta.Path.Monuments` using `monument.IsInBounds(position)`. **CRITICAL**: Does NOT require `monument.HasNavmesh == true` - configures for monument navmesh (AreaMask=25) even if navmesh is still building. **Follows game's exact pattern** (like `BradleyAPC.SpawnScientist`): Uses `BaseNavigator.GetNavMeshAgentID("Humanoid")` to get agent type, sets `AreaMask = 25` and `DefaultArea = "HumanNPC"` for monuments
2. **Building Block Detection**: Only if monument detection failed. Uses exact same logic as `BaseNavigator.DetermineNavigationType()`, sets `AreaMask = 1` and `AgentTypeID = -1372625422`
3. **Terrain Default**: Only if both failed. Uses default terrain values (`AreaMask = 1`, `AgentTypeID = -1372625422`, `DefaultArea = "Walkable"`)

**Auto-Detection Triggers**:
- When creating default npcData (if registration failed before spawn)
- When npcData uses default terrain values (`AreaMask = 1`, `AgentTypeID = -1372625422`)
- **CRITICAL**: Also triggers if NPC is on a monument but has wrong settings (AreaMask=1 instead of 25) - automatically fixes incorrect settings
- **Manual Override**: Custom `AreaMask`/`AgentTypeID` values (other than defaults) skip auto-detection

**Plugin Integration**:
- **BetterNpc/BossMonster**: Intentionally register with default terrain values (AreaMask=1, AgentTypeID=-1372625422) to trigger GrimmNPC's auto-detection
- **Plugins CAN override**: Set `AreaMask`/`AgentTypeID` manually before registration to skip auto-detection (e.g., Cargo ship uses AreaMask=25)
- **TypeNavMesh config**: Plugins have `TypeNavMesh` config (0=terrain, 1=monument), but they use defaults to let GrimmNPC auto-detect instead

**Monument Detection Behavior**: 
- Checks monument bounds FIRST (using `IsInBounds()`), then configures for monument navmesh
- Does NOT require `HasNavmesh == true` - navmesh might still be building when NPC spawns
- **Uses game's exact method**: `BaseNavigator.GetNavMeshAgentID("Humanoid")` to get agent type (matches `BradleyAPC.SpawnScientist` approach)
- Always configures for monument navmesh (`AreaMask=25`, `DefaultArea="HumanNPC"`) if on monument, even if navmesh isn't built yet
- Falls back to monument-specific agent type from `MonumentNavMesh` component if `GetNavMeshAgentID()` fails
- Final fallback: default agent type (0) if all detection fails

**Agent Type Detection**:
- **Primary**: Uses `BaseNavigator.GetNavMeshAgentID("Humanoid")` (matches game's approach in `BradleyAPC.SpawnScientist`)
- **Fallback 1**: Gets from `MonumentNavMesh.NavMeshAgentTypeIndex` via reflection if `GetNavMeshAgentID()` fails
- **Fallback 2**: Defaults to `agentTypeID = 0` if all detection fails

**DefaultArea Configuration**:
- **Monuments**: `DefaultArea = "HumanNPC"` (matches game's approach for normal spawns in `BradleyAPC`)
- **Terrain**: `DefaultArea = "Walkable"` (matches game's approach for road spawns in `BradleyAPC`)
- This ensures `defaultAreaMask` is calculated correctly during `BaseNavigator.Init()`

**Fallback Behavior**: If monument detection fails, defaults to terrain values (`AreaMask=1`, `AgentTypeID=-1372625422`, `DefaultArea="Walkable"`). For edge cases, manually set `AreaMask = 25`, `AgentTypeID` from `GetNavMeshAgentID("Humanoid")`, and `DefaultArea = "HumanNPC"` before registration.

**Implementation**: `SpawnPatches.cs` `DetectAndConfigureNavMesh()`, `DetectAndConfigureMonumentNavMesh()`, `IsPositionOnBuildingBlock()`

### Pathfinding Performance

**GrimmNPC's Impact**:
- **Destination Clamping**: Fast prefix check (minimal overhead)
- **No Pathfinding Patches**: Does NOT patch expensive pathfinding operations
- **Navigator Configuration**: One-time setup during spawn (not in hot path)

**Pathfinding Frequency**:
- **State Updates**: 4Hz (every 0.25s) - states call `SetDestination()`
- **Roam Enforcement**: 1Hz (every 1.0s) - calls `SetDestination()` when needed
- **Combat Enhancement**: 0.25Hz (every 4 frames) - calls `SetDestination()` when needed

**Total SetDestination() Calls**: ~5-10 per second per NPC (depending on state and activity).

### AI Information Zone Integration

GrimmNPC does NOT patch `AIInformationZone` methods, but its destination clamping affects how zones are used:

**Vanilla Pathfinding**:
- States call `AIInformationZone.GetBestMovePointNear()` or `GetBestRoamPoint()`
- Zones evaluate move points based on distance, LOS, usage status
- Returns best `AIMovePoint` position
- State calls `SetDestination()` with move point position

**GrimmNPC's Influence**: If move point is outside RoamRange, `NavigationPatches` clamps it. Pathfinding still executes, but destination is within allowed range.

**Result**: NPCs benefit from Rust's optimized pathfinding while staying within their designated area.

---

## API Reference

### External Plugin Integration - Key Principles

**IMPORTANT FOR PLUGIN DEVELOPERS**:
1. **Config Independence**: External plugins (BetterNpc, DefendableHomes, etc.) do NOT need to be updated when GrimmNPC's internal configuration (`NpcConfig`) changes. GrimmNPC manages all global settings automatically.
2. **Plugin-Owned Properties Only**: Plugins should only set properties they care about via reflection (UserID, Name, Health, HomePosition, RoamRange, etc.). All other configuration is handled by GrimmNPC.
3. **Stable API**: The reflection-based integration API (`RegisterPending`, `RegisterNpc`, `UnregisterNpc`, `CustomNpcData` properties) is stable and backward-compatible. New properties have defaults, ensuring existing plugins continue to work.
4. **No Config File Dependencies**: Plugins do NOT need to read or modify GrimmNPC's config file (`HarmonyConfig/GrimmNPC.json`). All global settings are managed internally by GrimmNPC.
5. **Automatic Feature Application**: New features added to GrimmNPC (e.g., assist system, dynamic navmesh switching) are automatically applied to all registered NPCs without requiring plugin updates.
6. **Oxide Hook Integration**: GrimmNPC provides `CallOxideHook()` for calling Oxide hooks from Harmony mods. This method is performance-optimized with cached reflection lookups, ensuring minimal overhead even when called infrequently (on death, guard target destroyed, etc.).

**Example**: If GrimmNPC adds a new `NpcConfig` property like `EnableNewFeature`, plugins do NOT need to:
- Update their code to set this property
- Read GrimmNPC's config file
- Handle this property in any way

GrimmNPC automatically applies this setting to all NPCs based on its own config file.

### NavMesh Auto-Detection vs Manual Configuration

**Question**: Does the game auto-detect navmesh, or should plugins configure it manually?

**Answer**:
- **Game (BradleyAPC)**: Does NOT auto-detect. Uses hardcoded `roadSpawned` parameter to determine settings.
- **GrimmNPC**: DOES auto-detect navmesh type when defaults are used (AreaMask=1, AgentTypeID=-1372625422).
- **Plugins (BetterNpc/BossMonster)**: Intentionally use defaults to trigger GrimmNPC's auto-detection.
- **Manual Override**: Plugins CAN override by setting `AreaMask`/`AgentTypeID` manually before registration (e.g., Cargo ship uses AreaMask=25).

**How It Works**:
1. Plugin registers with default terrain values (AreaMask=1, AgentTypeID=-1372625422)
2. GrimmNPC's `SpawnPatches` detects navmesh type during spawn (monument → building block → terrain)
3. Auto-detection configures correct settings (AreaMask=25 for monuments, etc.)
4. If plugin sets custom values, auto-detection is skipped (manual override)

**Recommendation**: Use defaults to trigger auto-detection (works for 99% of cases). Only override manually for special cases (e.g., Cargo ship).

### Raiding API

**Purpose**: Shared raiding behavior for custom NPCs. NPCs will attack doors, walls, and foundations that block line of sight to a player or Raid Goal (TC/structure), but only if the building privilege is auth-restricted and the target player is authed (for player targets).

**Key Rules**:
- NPC must have `IsRaidingNpc = true`.
- NPC can raid with either:
  - **Player target** with **no LOS** to that player (original behavior)
  - **Raid Goal** (TC/structure) - NPCs with `RaidGoalActive = true` will raid structures blocking path to the goal
- If `RaidSettings.DisableAtMonuments = true`, raiding is disabled on monuments.
- If `RaidSettings.RequireTargetAuth = true`, raiding only happens when the target player is authed on the building privilege (does not apply to Raid Goal targets).

**CustomNpcData Fields**:
```csharp
public bool IsRaidingNpc { get; set; } = false;
public RaidSettings RaidSettings { get; set; } = new RaidSettings();

// 🏠 Raid Goal System (for structure/TC targeting)
public bool RaidGoalActive { get; set; } = false;
public Vector3 RaidGoalPosition { get; set; } = Vector3.zero;
public ulong RaidGoalEntityId { get; set; } = 0UL;
```

**RaidSettings Defaults**:
```csharp
public class RaidSettings
{
    public bool Enable { get; set; } = true;
    public bool RequireTargetAuth { get; set; } = true;
    public bool DisableAtMonuments { get; set; } = true;
    public bool AllowTargetSleeping { get; set; } = false;
    public bool AllowTargetOffline { get; set; } = false;
    public bool AllowExplosives { get; set; } = true;
    public float AttackRangeMelee { get; set; } = 6f;
    public float AttackRangeRanged { get; set; } = 30f;
    public float FallbackDamageMelee { get; set; } = 25f;
    public float FallbackDamageRanged { get; set; } = 50f;
}
```

**Usage Pattern (Plugin Integration)**:
```csharp
// Enable raiding for this NPC
SetProperty(npcData, "IsRaidingNpc", true);

// 🏠 Set Raid Goal (for structure/TC targeting)
SetProperty(npcData, "RaidGoalActive", true);
SetProperty(npcData, "RaidGoalPosition", toolCupboard.transform.position);
SetProperty(npcData, "RaidGoalEntityId", toolCupboard.net?.ID.Value ?? 0UL);

// Optional: override raid settings (example)
var raidSettings = new RaidSettings
{
    Enable = true,
    RequireTargetAuth = true,
    DisableAtMonuments = true,
    AllowExplosives = true,
    AttackRangeMelee = 6f,
    AttackRangeRanged = 25f
};
GrimmNPC.Raid.SetRaidSettings(netId, raidSettings);
```

**Raid Goal System**:
- **Purpose**: Allows NPCs to target structures (TC, doors, walls, foundations) without requiring a player target
- **RaidGoalActive**: When `true`, NPC prioritizes Raid Goal over player targets
- **RaidGoalPosition**: Fallback position if `RaidGoalEntityId` is invalid
- **RaidGoalEntityId**: Network ID of the target entity (TC, door, wall, etc.)
- **Behavior**: When Raid Goal is active, NPCs will:
  1. Prioritize Raid Goal entity in targeting (if in range)
  2. Find and attack blocking structures (doors, walls, foundations) between NPC and Raid Goal
  3. Search for nearby structures if Raid Goal entity is not in memory
  4. Move toward Raid Goal position if no target is in memory

**Notes**:
- The raid system uses line-of-sight raycasts toward the target player or Raid Goal to find blocking doors/walls/foundations.
- Priority order is **Doors → Walls → Foundations**.
- Weapon mechanics are handled by `SpecialWeaponsHandler` and are **not** gated behind raid state.
- Special weapons use coroutine-style firing (rockets, grenade launchers, flamethrowers, bows) with stance/aim locks, cadence, reload timing, LOS checks, and ShotTest fallbacks where applicable.
- **Weapon selection**: Raiding uses belt items to select special weapons (rocket launcher, multiple grenade launcher, flamethrower) and falls back to standard ranged/melee as needed.
- **Ammo**: For rocket/grenade launchers, ensure ammo exists in weapon contents or the handler will fall back to standard damage.
- **Targeting integration**: All weapons use HumanNPC's native targeting system:
  - `GetAttackEntity()` - Gets current weapon entity (works for all weapon types)
  - `EngagementRange()` - Gets effective range from weapon's `effectiveRange` property
  - `IsTargetInRange()` - Checks if target is within engagement range
  - `CanSeeTarget()` - Accurate LOS detection (more reliable than custom methods)
  - `ModifyAIAim()` - Weapon-specific aim adjustments (sway, cone, etc.)
- **Weapon type support**: All weapon types are supported:
  - `BaseProjectile` (rifles, pistols, blowpipes, bows, crossbows, flintlock)
  - `BaseLauncher` (rocket launchers, MGL)
  - `AttackEntity` (flamethrowers, melee, thrown weapons)
- **Satchel Cycle**: NPCs with satchels use a state machine cycle: Throw → Wait Explosion → Melee Attack → Retreat → Repeat (see Satchel Cycle section below).

**External Plugin Tips (Weapon/Ammo Setup)**:
```csharp
// Example: ensure rocket launcher has ammo in its contents
Item launcher = ItemManager.CreateByName("rocket.launcher", 1, 0);
if (launcher?.contents != null)
{
    Item ammo = ItemManager.CreateByName("rocket_basic", 1, 0);
    if (ammo != null)
        launcher.contents.AddItem(ammo.info, 1);
}
npc.inventory.GiveItem(launcher, npc.inventory.containerBelt);

// Optional: force equip and reload
npc.UpdateActiveItem(launcher.uid);
BaseProjectile projectile = npc.GetHeldEntity() as BaseProjectile;
if (projectile != null && projectile.primaryMagazine != null && projectile.primaryMagazine.contents <= 0)
    projectile.ServerReload();
```

**External Plugin Tips (Kits / Inventory)**:
- If your plugin uses the Kits plugin, apply the kit **after spawn** and **before** you call `EquipTest()` so the NPC equips kit items.
- If a kit is applied, make sure it includes belt items required for raiding (explosives/launchers). Otherwise the NPC will keep its default weapon and never raid.
- If you skip kits, manually populate belt/wear containers and then call `npc.EquipTest()`.

```csharp
// Example: apply kit and then equip
npc.inventory.containerWear.Clear();
npc.inventory.containerBelt.Clear();
npc.inventory.containerMain.Clear();

object kitResult = Interface.CallHook("GiveKit", npc, "RaidKit");
if (kitResult == null || kitResult.ToString().ToLower() == "false")
{
    // Fallback: manually give belt items if kit failed
    Item launcher = ItemManager.CreateByName("rocket.launcher", 1, 0);
    npc.inventory.GiveItem(launcher, npc.inventory.containerBelt);
}

npc.EquipTest();
```

### Satchel Cycle System

**Purpose**: Manages satchel-throwing NPCs (like Sledge NPCs) with a proper cycle: throw satchels → wait for explosion → melee attack → retreat → repeat. Based on RaidingZombies.cs pattern.

**Location**: `SpecialWeaponsHandler.cs`

**State Machine Phases**:
1. **Throwing**: Throw 3 satchels (one at a time with cooldown)
2. **WaitExplosion**: Wait 8 seconds for satchels to explode
3. **MeleeAttack**: Melee attack the base for 30 seconds
4. **Retreat**: Retreat to 12m distance and wait 10 seconds (allows satchels to fully explode)
5. **Repeat**: Cycle resets to Throwing phase

**Key Constants**:
```csharp
private const int SatchelsPerCycle = 3;              // Number of satchels per cycle
private const float SatchelExplosionWaitTime = 8f;    // Wait for explosions
private const float MeleeAttackDuration = 30f;        // Melee attack duration
private const float RetreatDuration = 10f;           // Retreat duration (satchels take time to explode)
private const float RetreatDistance = 12f;           // Safe distance from explosions
private const float SatchelOptimalRange = 8f;         // Optimal throw range
private const float SatchelMinRange = 2f;            // Minimum throw distance
private const float SatchelMaxRange = 15f;           // Maximum throw distance
```

**Wall Detection**:
- NPCs check for walls/structures within 8m before throwing (like RaidingZombies.hasWall())
- Uses `HasWallInThrowableRange()` method with raycast on construction layer
- Only throws when wall is detected in range (prevents wasted throws)

**Movement Pattern** (like RaidingZombies):
- When not in optimal range: Moves around target in 2-12m range (uses `PathFinder.GetRandomPositionAround()`)
- When in range with wall: Stops and throws satchel
- During wait/retreat: Stops movement to avoid explosions

**Satchel Throwing Coroutine** (based on RaidingZombies.ThrowWeaponBoom):
1. **Equip explosive**: Equips satchel from belt
2. **Wind-up delay**: Waits 1.5 seconds (like RaidingZombies)
3. **LOS check**: Verifies line of sight to target
4. **Aim stabilization**: Stops movement, sets aim direction, brief delay (0.1s)
5. **Server-side throw**: Uses `ServerThrowSatchel()` with proper velocity calculation
6. **Cleanup**: Waits 1 second, restores normal weapon, clears state

**ServerThrowSatchel Implementation**:
- Creates thrown entity server-side (like RaidingZombies.ServerThrow)
- Calculates velocity with upward angle (10-20 degrees)
- Adjusts throw speed based on distance (4.5f-6f)
- Disables dud chance (sets `DudTimedExplosive.dudChance = 0f`)
- Spawns entity with proper velocity and angular velocity

**Integration with DefendableHomes**:
- DefendableHomes checks if NPC is raiding with `RaidGoalActive` before calling `UseExplosiveOnTarget()`
- Prevents interference - GrimmNPC's raiding system handles all weapon firing for raiding NPCs
- DefendableHomes only handles non-raiding NPCs or fallback scenarios

**Cycle State Tracking**:
- Stored in `WeaponState.SatchelPhase` (enum: None, Throwing, WaitExplosion, MeleeAttack, Retreat)
- Tracks `SatchelsThrownThisCycle`, `SatchelPhaseStartTime`, `MeleeAttackEndTime`
- Automatically transitions between phases based on timing and conditions

**Weapon Selection During Cycle**:
- **Throwing/WaitExplosion/Retreat phases**: NPCs use satchels (if available)
- **MeleeAttack phase**: NPCs use melee weapons (satchels disabled)
- Prevents "chopping air" by ensuring melee only happens during melee phase

**Notes**:
- Cycle only activates for NPCs with satchels (`explosive.satchel` in belt)
- NPCs without satchels use normal raiding behavior (rockets, melee, etc.)
- Cycle resets automatically after retreat phase completes
- NPCs move toward Raid Goal even when no target is in memory (ensures all NPCs participate in raiding)

### Plugin Integration Pattern

**CRITICAL: Config Independence**
- **External plugins do NOT need updates when GrimmNPC's config changes**: GrimmNPC handles all internal configuration automatically. Plugins only need to set properties they care about (UserID, Name, Health, HomePosition, RoamRange, etc.).
- **Stable Reflection API**: The `CustomNpcData` property names and registration methods (`RegisterPending`, `RegisterNpc`, `UnregisterNpc`) are stable and backward-compatible. Changes to GrimmNPC's internal `NpcConfig` structure do NOT affect external plugins.
- **Plugin-Owned vs. GrimmNPC-Owned Settings**: 
  - **Plugin-Owned**: Properties that plugins set via reflection (UserID, Name, Health, HomePosition, RoamRange, ChaseRange, SenseRange, DamageScale, etc.). These are per-NPC settings.
  - **GrimmNPC-Owned**: Internal configuration (`NpcConfig`) that GrimmNPC manages automatically (CanTargetAnimal, CanTargetNpc, ExcludedTargetTypes, AssistRange, etc.). Plugins should NOT try to set these.
- **Automatic Configuration**: GrimmNPC automatically applies its internal config to all registered NPCs. Plugins don't need to know about or configure these settings.
- **Backward Compatibility**: New properties added to `CustomNpcData` have default values, so existing plugins continue to work without updates.

**Complete Integration Steps** (for Oxide plugins using reflection):

**Step 1: Find GrimmNPC Type**
```csharp
// Search all loaded assemblies (Harmony mods use renamed assemblies)
Type grimmNpcType = null;
Assembly grimmNpcAssembly = null;

foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
{
    // Try full namespace first
    grimmNpcType = assembly.GetType("GrimmNPC.GrimmNPC");
    if (grimmNpcType == null)
    {
        // Try without namespace
        grimmNpcType = assembly.GetType("GrimmNPC");
    }
    
    if (grimmNpcType != null)
    {
        grimmNpcAssembly = assembly;
        break;
    }
}

if (grimmNpcType == null)
{
    PrintError("GrimmNPC not found - integration disabled");
    return;
}
```

**Step 2: Get CustomNpcData Type**
```csharp
// Get CustomNpcData type from same assembly
Type customNpcDataType = grimmNpcAssembly.GetType("GrimmNPC.CustomNpcData");
if (customNpcDataType == null)
{
    PrintError("GrimmNPC.CustomNpcData type not found - integration disabled");
    return;
}
```

**Step 3: Get Registration Methods**
```csharp
// Get static methods
MethodInfo registerPendingMethod = grimmNpcType.GetMethod("RegisterPending", 
    BindingFlags.Public | BindingFlags.Static);
MethodInfo registerNpcMethod = grimmNpcType.GetMethod("RegisterNpc", 
    BindingFlags.Public | BindingFlags.Static);
MethodInfo unregisterNpcMethod = grimmNpcType.GetMethod("UnregisterNpc", 
    BindingFlags.Public | BindingFlags.Static);

if (registerPendingMethod == null || registerNpcMethod == null || unregisterNpcMethod == null)
{
    PrintError("GrimmNPC methods not found - integration disabled");
    return;
}
```

**Step 4: Verify Instance**
```csharp
// Verify Instance is not null
PropertyInfo instanceProperty = grimmNpcType.GetProperty("Instance", 
    BindingFlags.Public | BindingFlags.Static);
if (instanceProperty == null)
{
    PrintError("GrimmNPC.Instance property not found - integration disabled");
    return;
}

object instance = instanceProperty.GetValue(null);
if (instance == null)
{
    PrintError("GrimmNPC.Instance is null - ensure GrimmNPC is loaded before this plugin");
    return;
}
```

**Step 5: Create CustomNpcData and Register**
```csharp
// Create NPC entity
var npc = GameManager.server.CreateEntity(prefab, spawnPosition) as ScientistNPC;
npc.skinID = 11162132011012UL;  // CUSTOM_NPC_SKIN_ID

// Create CustomNpcData instance
object npcData = Activator.CreateInstance(customNpcDataType);

// Set properties via reflection (CRITICAL: HomePosition must be set)
SetProperty(npcData, "UserID", GetUniqueUserId());
SetProperty(npcData, "Name", "MyNPC");
SetProperty(npcData, "Health", 200f);
SetProperty(npcData, "HomePosition", spawnPosition);  // CRITICAL - prevents clustering
SetProperty(npcData, "RoamRange", 50f);
SetProperty(npcData, "ChaseRange", 100f);
SetProperty(npcData, "SenseRange", 50f);
SetProperty(npcData, "DamageScale", 1.0f);

// Use defaults to trigger auto-detection (AreaMask=1, AgentTypeID=-1372625422)
SetProperty(npcData, "AreaMask", 1);
SetProperty(npcData, "AgentTypeID", -1372625422);

// Register BEFORE spawn (CRITICAL)
registerPendingMethod.Invoke(null, new object[] { npc, npcData });

// Now spawn
npc.Spawn();
```

**Helper Method for Setting Properties**:
```csharp
private void SetProperty(object obj, string propertyName, object value)
{
    var prop = obj.GetType().GetProperty(propertyName);
    if (prop != null && prop.CanWrite)
    {
        prop.SetValue(obj, value);
    }
}
```

**Step 6: Unregister When NPC Dies**
```csharp
void OnEntityDeath(ScientistNPC npc)
{
    if (npc == null) return;
    
    ulong netId = npc.net?.ID.Value ?? 0;
    if (netId == 0) return;
    
    // Unregister from GrimmNPC
    if (_grimmNpcAvailable && _unregisterNpcMethod != null)
    {
        try
        {
            _unregisterNpcMethod.Invoke(null, new object[] { netId });
        }
        catch (Exception ex)
        {
            PrintError($"Failed to unregister NPC from GrimmNPC: {ex}");
        }
    }
}
```

**Critical Notes**:
- **Cache Reflection Lookups**: Store types/methods as static fields (don't look up every spawn). Initialize once in `OnServerInitialized()`.
- **Registration Timing**: Must happen BEFORE `npc.Spawn()` or ServerInit creates defaults (50m RoamRange)
- **HomePosition**: Must be set correctly (unique per NPC) to prevent clustering. Use spawn position if not specified.
- **Auto-Detection**: Use defaults (AreaMask=1, AgentTypeID=-1372625422) to trigger auto-detection. Only override manually for special cases (e.g., monuments).
- **Error Handling**: If `GrimmNPC.Instance` is null or registration fails, abort spawning (no fallbacks). Log errors for debugging.
- **Unregistration**: Always unregister NPCs when they die to prevent memory leaks.
- **Config Independence**: Plugins only need to set properties they care about. GrimmNPC's internal config (`NpcConfig`) is managed automatically and does NOT require plugin updates when changed. New `CustomNpcData` properties have defaults, ensuring backward compatibility.

---

**`RegisterPending(entity, npcData)`** - **RECOMMENDED**: Register BEFORE spawn (netId=0). Prevents default 50m RoamRange. Data consumed during ServerInit.

**`RegisterNpc(netId, npcData)`** - Register AFTER spawn. Warning: ServerInit may have already created defaults.

**`UnregisterNpc(netId)`** - Unregister NPC.

**`GetNpcData(netId)`** - Get NPC data by network ID. Returns `CustomNpcData` or `null`.

**`IsCustomNpc(entity)`** - Check if custom NPC. Returns `true` if `entity.skinID == CUSTOM_NPC_SKIN_ID`.

### API Usage Patterns

**Recommended Pattern** (RegisterPending BEFORE Spawn):
```csharp
var npc = GameManager.server.CreateEntity(prefab, spawnPosition) as ScientistNPC;
npc.skinID = GrimmNPC.CUSTOM_NPC_SKIN_ID;
var npcData = new CustomNpcData { HomePosition = spawnPosition, RoamRange = 50f, /* ... */ };
GrimmNPC.RegisterPending(npc, npcData);  // BEFORE spawn
npc.Spawn();
```

**Monument Spawn** (use defaults to trigger auto-detection):
```csharp
var npcData = new CustomNpcData { HomePosition = spawnPosition, AreaMask = 1, AgentTypeID = -1372625422 };
GrimmNPC.RegisterPending(npc, npcData);  // Auto-detection configures monument navmesh
```

**Manual Override** (skip auto-detection):
```csharp
var npcData = new CustomNpcData { AreaMask = 25, AgentTypeID = BaseNavigator.GetNavMeshAgentID("Humanoid") };
GrimmNPC.RegisterPending(npc, npcData);  // Manual override, no auto-detection
```

#### `GrimmNPC.GetConfig()`

**Purpose**: Get current configuration.

**Returns**: `NpcConfig` instance (or default if mod not loaded)

#### `GrimmNPC.IsUserIdUsed(ulong userId)`

**Purpose**: Check if user ID is already used.

**Parameters**:
- `userId` - User ID to check

**Returns**: `true` if used, `false` otherwise

#### `GrimmNPC.MarkUserIdUsed(ulong userId)`

**Purpose**: Mark user ID as used.

**Parameters**:
- `userId` - User ID to mark

#### `GrimmNPC.ValidateHomePosition(CustomNpcData npcData, Vector3 fallbackPosition)`

**Purpose**: Validates HomePosition is set correctly.

**Parameters**:
- `npcData` - NPC data to validate
- `fallbackPosition` - Position to use if HomePosition is invalid

**Returns**: `true` if HomePosition was updated, `false` otherwise

**Usage**:
```csharp
if (GrimmNPC.ValidateHomePosition(npcData, spawnPosition))
{
    // HomePosition was updated from zero/invalid to spawn position
}
```

**Critical**: Used by patches to ensure HomePosition is not zero (prevents clustering). See INSTRUCTIONAL.md "Troubleshooting - NPCs Clustering" section.

#### `GrimmNPC.GetDistanceFromHome(CustomNpcData npcData, Vector3 currentPosition)`

**Purpose**: Gets the horizontal (XZ plane) distance from HomePosition.

**Parameters**:
- `npcData` - NPC data containing HomePosition
- `currentPosition` - Current position to measure from

**Returns**: Horizontal distance from HomePosition in meters (0f if npcData is null or HomePosition is zero)

**Usage**:
```csharp
float distance = GrimmNPC.GetDistanceFromHome(npcData, npc.transform.position);
if (distance > npcData.RoamRange)
{
    // NPC is outside roam range
}
```

**Performance**: O(1) - single distance calculation using manual 2D math: `Mathf.Sqrt(diff.x * diff.x + diff.z * diff.z)`

**Note**: `Vector3Ex` is not available in Harmony mods, so manual calculations are used instead.

**Used in**: ThinkPatches (roam enforcement), TargetingPatches (chase range), NavigationPatches (destination clamping)

**Critical**: Uses horizontal (XZ) distances to prevent Y-axis issues. This prevents NPCs from looking at ceiling when targets are at different Y levels.

#### `GrimmNPC.CallOxideHook(string hookName, params object[] args)`

**Purpose**: Calls an Oxide hook via reflection (Harmony mods don't have direct Oxide.Core access). This method is performance-optimized with cached reflection lookups.

**Parameters**:
- `hookName` - Name of the hook to call (e.g., "OnBomberExplosion", "OnCustomNpcGuardTargetEnd")
- `args` - Arguments to pass to the hook (0-2 arguments supported efficiently, more via params overload)

**Returns**: `true` if hook was called successfully, `false` if Oxide is not available or hook call failed

**Performance**: 
- **First call**: ~50-100ms (one-time initialization of cached references)
- **Subsequent calls**: ~0.1-0.5ms (uses cached MethodInfo and Oxide instance)
- **Optimization**: All reflection lookups (assembly, MethodInfo, Oxide instance) are cached on first use

**Usage**:
```csharp
// Call hook with 0 arguments
GrimmNPC.CallOxideHook("OnCustomNpcGuardTargetEnd", npc);

// Call hook with 1 argument
GrimmNPC.CallOxideHook("OnBomberExplosion", npc);

// Call hook with 2 arguments
GrimmNPC.CallOxideHook("OnBomberExplosion", npc, target);
```

**When Called**: Infrequently (on NPC death, guard target destroyed, bomber explosion, etc.)

**Note**: This method gracefully handles cases where Oxide is not available (returns `false` without throwing exceptions). External plugins can listen to these hooks using standard Oxide hook syntax.

#### `GrimmNPC.ConsumePending(BaseEntity entity)`

**Purpose**: Internal method used by SpawnPatches to retrieve pending registration data.

**Parameters**:
- `entity` - NPC entity

**Returns**: `CustomNpcData` if found in pending registrations, `null` otherwise

**Usage**: Internal use only. Called by `SpawnPatches.ApplyCustomConfig()` during ServerInit to check for pending registrations before creating defaults.

**Process**:
1. Looks up entity by instance ID in `_pending` dictionary
2. If found, removes from pending and returns data
3. If not found, returns null (triggers default NPC creation)

---

## Performance Optimizations

**Think() (4000 calls/sec)**: Direct IL patching (0.003ms vs 0.3ms Oxide), config cached (5s), early exit (`skinID` check), throttled: dormancy 0.5s, roam 1s, navmesh fix 1s, debug 2s, combat every 4th frame. Impact: ~1.2% CPU (vs 120% Oxide), 100x improvement.

**Targeting (10-20 calls/sec in combat)**: Config cached (5s), fast memory iteration, early exits, horizontal distance helper, debug throttled (2s). Minimal overhead.

**SetDestination() (5-10 calls/sec)**: Fast prefix check, helper method, vector math only, no throttling, redundant call prevention (0.5m epsilon). <0.001ms per call, reduces calls 20-30% in combat.

**Swimming (60 calls/sec)**: Cached FieldInfo, fast water check, `Vector3.MoveTowards()`. <0.001ms per call.

**Memory**: Dictionaries pre-sized (1000 capacity): `_npcs`, `_usedNpcUserIds`, `_previousStates`, `_lastDormancyCheck`, `_lastRoamEnforcement`. Prevents O(n) resizing. ~200 bytes/NPC, 200KB for 1000 NPCs (vs 200MB Oxide).

**Spawn**: In-place config (no component destruction): 27.9s → <1s (28x faster).

### Update Frequency Summary

| System | Update Rate | GrimmNPC Interaction | Performance Impact |
|--------|-------------|---------------------|-------------------|
| Movement Tick | 10Hz (0.1s) | Indirect (via SetDestination) | None (not patched) |
| AI Think | 4Hz (0.25s) | Postfix patch | 0.003ms per call |
| Targeting | On-demand | Prefix patch | <0.001ms per call |
| SetDestination | 5-10Hz (smart strafe) / 60Hz (always-strafe) | Prefix patch + redundant call prevention | <0.001ms per call |
| Swimming | 60Hz (when swimming) | Prefix patches | <0.001ms per call |
| Dormancy Check | 2Hz (0.5s) | Throttled in Think() | Minimal |
| Roam Enforcement | 1Hz (1.0s) | Throttled in Think() | Minimal |
| Dynamic Navmesh Switching | 1Hz (1.0s) | Throttled in Think(), only for NavmeshLocked=false | Minimal (only updates when navmesh type changes) |
| Combat Enhancement | 0.25Hz (4 frames, smart strafe) / 60Hz (always-strafe) | Throttled in Think() by StrafeInterval | Minimal (smart) / Moderate (always-strafe) |
| Debug Logging | 0.5Hz (2.0s) | Throttled in Think() | Moderate (if enabled) |

### Performance Best Practices

1. Disable debug logging in production (string formatting, file I/O overhead)
2. Throttle expensive operations (time/frame-based)
3. Cache config lookups (re-check periodically)
4. Early exits (filter custom NPCs first)
5. Pre-size dictionaries (prevent resizing)
6. Avoid reflection in hot paths (cache FieldInfo/PropertyInfo)
7. Use horizontal distances (`GrimmNPC.GetDistanceFromHome()`, manual 2D math, Vector3Ex unavailable)
8. Avoid redundant SetDestination calls (check delta: 0.5m smart strafe, 0.1m always-strafe)
9. Smart strafe logic (LOS/distance checks, use `AlwaysStrafeInCombat=false` for many NPCs)
10. Always-strafe performance (60Hz vs 15Hz, consider `StrafeInterval=2/4`)
11. WaitForNavMesh gate (prevents spawn/placement thrash)
12. Match game detection logic (`navTypeHeightOffset`, `navTypeDistance`)

---

## Extending the Mod

### Adding a New Patch

1. **Create patch file** in `Patches/` directory
2. **Use HarmonyPatch attribute**:
```csharp
[HarmonyPatch(typeof(TargetClass), nameof(TargetClass.TargetMethod))]
public class TargetClass_TargetMethod_Patch
{
    static void Prefix(TargetClass __instance) { }
    static void Postfix(TargetClass __instance) { }
    static bool Prefix(TargetClass __instance, ref ReturnType __result) { return true; }
}
```

3. **Filter for custom NPCs**:
```csharp
if (!GrimmNPC.IsCustomNpc(__instance.baseEntity)) return true;
```

4. **Access NPC data**:
```csharp
ulong netId = npc.net?.ID.Value ?? 0;
var npcData = GrimmNPC.GetNpcData(netId);
if (npcData == null) return;
```

### Adding a New Config Property

1. **Add property to `NpcConfig`**:
```csharp
public bool MyNewFeature { get; set; } = false;
```

2. **Update default config** in `NpcConfig.Default()` if needed

3. **Access in patches**:
```csharp
var config = GrimmNPC.GetConfig();
if (config.MyNewFeature) { /* ... */ }
```

### Adding a New NPC Data Property

1. **Add property to `CustomNpcData`**:
```csharp
public float MyNewValue { get; set; } = 10f;
```

2. **Set when registering NPC**:
```csharp
var npcData = new CustomNpcData
{
    MyNewValue = 20f
};
GrimmNPC.RegisterNpc(netId, npcData);
```

**Example: Always-Strafe Combat Behavior**:
```csharp
var npcData = new CustomNpcData
{
    Name = "Combat Guard",
    AlwaysStrafeInCombat = true,
    StrafeRadius = 3f,
    StrafeInterval = 1,
    StrafeOnlyWhenAttacking = true
};
GrimmNPC.RegisterNpc(netId, npcData);
```

**Example: Dormancy Configuration**:
```csharp
var npcData = new CustomNpcData
{
    Name = "Guard",
    CanSleep = true,
    SleepDistance = 150f,
    RoamRange = 5f
};
GrimmNPC.RegisterPending(npc, npcData);
```

3. **Access in patches**:
```csharp
var npcData = GrimmNPC.GetNpcData(netId);
if (npcData != null)
{
    float value = npcData.MyNewValue;
}
```

### Performance Considerations

**When adding new features**:
1. **Cache expensive lookups** (config, NPC data)
2. **Throttle frequent operations** (use Time.frameCount or Time.time)
3. **Use early exits** (filter custom NPCs first)
4. **Avoid reflection in hot paths** (cache FieldInfo/PropertyInfo)
5. **Size dictionaries appropriately** (prevent resizing)

---

## Troubleshooting

### Common Issues

#### 1. NPCs Clustering at Same Point

**Symptoms**: All NPCs converge to same location.

**Causes**: HomePosition not set correctly (shared/zero/invalid), registration before spawn position set, multiple NPCs share same HomePosition.

**Solutions**: Set unique HomePosition per NPC (use spawn position), verify `GrimmNPC.ValidateHomePosition()` updates zero values, check logs for unique HomePositions.

**Diagnosis**: Check `npcData.HomePosition` values - should be unique per NPC. Shared HomePosition causes clustering.

#### 1a. NPCs Using Wrong RoamRange (50m instead of configured)

**Symptoms**: NPCs roam 50m despite 5m config.

**Causes**: Registration AFTER spawn (ServerInit creates defaults), pending registration not used, `GrimmNPC.Instance` null.

**Solutions**: Use `RegisterPending()` BEFORE `Spawn()`, check logs for "Consumed pending registration" (not "Registered NEW NPC"), verify `GrimmNPC.Instance` not null.

**Diagnosis**: Check logs - "Registered NEW NPC" = wrong (registered after spawn), "Consumed pending registration" = correct (registered before spawn).

#### 2. NPCs Not Moving

**Symptoms**: NPCs spawn but don't move.

**Causes**: Stationary (RoamRange <= 5m, intended), navigator misconfigured, NavMesh unavailable, navigator paused (`Is paused: True` - FIXED), navigation type None (`type: None` - FIXED).

**Solutions**: Check RoamRange, verify `CanUseBaseNav`/`CanUseNavMesh`, ensure `BaseAIBrain_InitializeAI_Patch` loaded.

#### 2c. Monument bosses (e.g. Launch Site): `Navmesh enabled: False`, `type: None`, no chase

**Symptoms**: Admin/debug shows `NavMeshAgent.enabled == false`, navigator `type: None`, boss does not move; a boss at a smaller monument (e.g. Sewer Branch) with the same `agentTypeID` / `areaMask` still works.

**Primary cause (FIXED in `ThinkPatches`)**: `BaseNavigator.Stop()` ends in `SetNavMeshEnabled(false)`, which **disables** the Unity `NavMeshAgent`. Game `BaseNavigator.SetDestination` **returns false** when `!Agent.isActiveAndEnabled` or `!Agent.isOnNavMesh` — it does **not** turn the agent back on. GrimmNPC used `Stop()` in **HandleIdleBehavior** and **EnforceRoamRange** to mean “wait at spawn with no target.” Large monuments keep NPCs in Idle/Roam longer before a target hits the brain memory slot, so `Stop()` ran more often; afterward chase/combat could never set a path. Smaller monuments often entered Chase before that path fired, so the bug looked “Launch Site only.”

**Ownership**: **GrimmNPC** owns idle/roam halting for custom scientists — use **`NavMeshAgent.isStopped = true`** (after ensuring the agent is enabled/on-mesh) instead of **`Navigator.Stop()`** for that case. **BossMonster** may still call `Stop()` on teleports; GrimmNPC **`EnsureNavigatorAgentReadyBeforeSetDestination`** before combat/roam `SetDestination` re-binds the agent. **Stock Rust** still owns `BaseNavigator.Think` / path stepping; plugins must not leave the agent disabled and expect `SetDestination` to recover.

**Verification**: On affected bosses, confirm `NavMeshAgent.enabled == true`, `isOnNavMesh == true`, then chase/strafe under fire.

#### 2a. NPCs Looking at Ceiling / Spinning

**Symptoms**: NPCs look upward, spin, act like player above.

**Causes**: Y-axis in distance calculations, incorrect target height, bad combat destination Y values.

**Solutions** (FIXED): Uses `GrimmNPC.GetDistanceFromHome()` (horizontal XZ), `target.CenterPoint()` for height, preserves NPC Y, NavigationPatches forces Y for Base Navigation.

#### 2b. NPCs Sprinting to Walls / Wall-Hugging

**Symptoms**: NPCs sprint to walls, ignore players, choose wall-adjacent destinations.

**Causes**: Y-axis bug (FIXED), roam points near boundaries, Base Navigation favors perimeters, combat enhancement without target (FIXED).

**Solutions** (FIXED): Y-axis fix (forces destination Y), combat enhancement only in combat with target, debug logging uses horizontal distances.

#### 3. NPCs Not Targeting Players

**Symptoms**: NPCs don't attack until player attacks first, `Memory: 0 entities` in debug logs, NPCs walk past players without reacting.

**Causes**: Targeting filters, ChaseRange/SenseRange too small, sense system not initialized or not re-initialized after SenseRange set, player not in memory, LOS issues, `HostileTargetsOnly=true` preventing detection of non-hostile players.

**Solutions** (FIXED): 
- GrimmNPC now re-initializes sense system after setting SenseRange during spawn. This ensures `Senses.Init()` is called with correct SenseRange and player detection properties.
- **CRITICAL FIX**: Sets `HostileTargetsOnly=false` (not `true`) to allow NPCs to detect all players immediately, not just those who have already attacked. When `HostileTargetsOnly=true`, Rust's `AIBrainSenses.AiCaresAbout()` filters out non-hostile players from detection.
- Check `CanTarget*` flags, `ExcludedTargetTypes`, ChaseRange/SenseRange, verify sense system initialization in logs.
- Verify `HostileTargetsOnly=false` in spawn logs: `[GrimmNPC Spawn] Re-initialized sense system for Scientist: SenseRange=50m, HostileTargetsOnly=False`

#### 4. Performance Issues

**Symptoms**: High CPU usage, lag.

**Causes**: Debug logging enabled, too many NPCs (1000+), inefficient patches.

**Solutions**: Disable debug logging, reduce NPC count, check throttling intervals.

#### 5. NPCs Not Swimming

**Symptoms**: NPCs don't enter water or swim.

**Causes**: `CanSwim` not true, water level detection failed, patches not loaded.

**Solutions**: Set `CanSwim: true`, check `waterLevel > 0.75f`, verify patches loaded. Note: GrimmNPC NPCs swim at surface (1.1m below), unlike vanilla NPCs that walk on bottom.

#### 6. Wrong NavMesh Type on Monuments

**Symptoms**: NPCs use terrain navmesh on monuments, pathfinding failures, stuck NPCs, `Agent.isOnNavMesh = false`, `AreaMask: 1 (should be 25)` in logs.

**Causes**: Auto-detection failed, manual override wrong values, navmesh not built yet, `DefaultArea` mismatch, detection required `HasNavmesh == true` (FIXED).

**Solutions** (FIXED): 
- **Auto-detection enabled by default**: GrimmNPC automatically detects navmesh type when defaults are used (AreaMask=1, AgentTypeID=-1372625422)
- Detection now checks monument bounds FIRST without requiring `HasNavmesh == true`
- **Uses game's exact method**: `BaseNavigator.GetNavMeshAgentID("Humanoid")` to get agent type (matches `BradleyAPC.SpawnScientist` approach)
- Always configures for monument navmesh (`AreaMask=25`, `DefaultArea="HumanNPC"`) if on monument, even if navmesh isn't built yet
- Detection automatically fixes wrong settings if NPC is on monument but has AreaMask=1
- **Dynamic navmesh switching**: GrimmNPC includes dynamic navmesh switching (1Hz) that automatically updates AreaMask/AgentTypeID based on current position. Enables NPCs to roam freely between monuments, terrain, and bases without NavMesh errors. Uses bounds-only authority for monuments (no sampling). **CRITICAL**: Navmesh remains unlocked after spawn-time configuration to allow dynamic switching.
- Check auto-detection logs for monument detection results (should show "Auto-detected monument navmesh" with correct AreaMask=25)
- Check dynamic navmesh logs for "Updated navmesh" messages (indicates NPC moved between different navmesh types and settings were updated automatically).
- **Manual override**: Set `AreaMask=25` and `AgentTypeID` from `GetNavMeshAgentID("Humanoid")` before registration to skip auto-detection
- Wait for navmesh (`MonumentNavMesh.IsBuilding=false`) - NPC will use correct settings when navmesh is built
- Verify `DefaultArea` is set to "HumanNPC" for monuments (not "Walkable")

**Note**: Game (BradleyAPC) does NOT auto-detect navmesh type - it uses hardcoded `roadSpawned` parameter. GrimmNPC's auto-detection is a feature that plugins (BetterNpc/BossMonster) intentionally use by registering with default terrain values.

#### 7. Base Navigation Not Working

**Symptoms**: NPCs cannot navigate on building blocks.

**Causes**: Detection mismatch, `CanUseBaseNav` not enabled, wrong parameters.

**Solutions** (FIXED): Uses exact detection logic (matches `BaseNavigator.DetermineNavigationType()`, cached reflection for `navTypeHeightOffset`/`navTypeDistance`), verify `CanUseBaseNav`/`CanUseNavMesh`, verify `Agent.updateRotation=false`/`updatePosition=false`, check detection logs, verify layer mask `2097152`.

#### 8. NPCs Getting on Railings / Navigating to High Positions

**Symptoms**: NPCs get on railings on narrow staircases (e.g., Dome/Sphere Tank), navigate to positions too high above ground.

**Causes**: Pathfinding choosing destinations on railings or obstacles, Y-axis issues with destination positions.

**Solutions**: NPCs navigate well without special railing prevention. The Y-axis fix (Base Navigation forces Y to NPC's current Y, NavMesh preserves Y) handles most cases. Pathfinding system handles navigation naturally.

#### 9. Build Errors - Physics and ConVar Namespaces

**Symptoms**: Compilation errors: `Physics`/`RaycastHit`/`AI.move` not found, ambiguous `Physics` reference, `CanSee` method not found.

**Solutions**: Add `UnityEngine.PhysicsModule.dll` reference, use `UnityEngine.Physics.Raycast()` (not `ConVar.Physics`), add `using ConVar;` for `AI.move`, use `HumanNPC.CanSeeTarget()` (not `eyes.CanSee()`).

### Debug Logging

**Enable**: Set `EnableDebugLogging: true` in `HarmonyConfig/GrimmNPC.json`

**Log Types**:
1. **Registration**: `[GrimmNPC Register] Registered NEW NPC: ...`
2. **Spawn**: `[GrimmNPC Spawn] Registered NEW NPC: ...`
3. **Think**: `[GrimmNPC Debug] NPC: ... State: ... Dest: (x, y, z) | DestDist: ...m | DestReason: ...`
   - `DestDist` uses horizontal (XZ) distance, not 3D distance
   - Destination Reasons: `Combat`, `Chase`, `Roam`, `ReturnHome`, `None`
4. **Roam**: `[GrimmNPC Roam] NPC ... setting destination: Dest=(x,y,z), Reason=RoamPoint/ReturnHome, ...`
5. **Combat**: `[GrimmNPC Combat] NPC ... setting destination: Dest=(x,y,z), Reason=Combat(Melee/Ranged), ...`
6. **Targeting**: `[GrimmNPC Targeting] GetBestTarget: ...`
7. **Assist**: `[GrimmNPC Assist] NPC ... calling for help!`

**Performance Impact**: Debug logging adds overhead. Disable in production. Destination logging is throttled to every 2 seconds.

### Building and Deployment

**Build**: `cd .cursor/HarmonyMods/GrimmNPC && dotnet build GrimmNPC.csproj -c Release`

**Deploy**: Copy `bin/Release/net48/GrimmNPC.dll` to `HarmonyMods/GrimmNPC.dll`, reload `harmony.load`, check logs for `[GrimmNPC] Loaded`.

**Dependencies**: .NET Framework 4.8, Rust.Harmony.dll, 0Harmony.dll, Assembly-CSharp*.dll, Facepunch.*.dll, Rust.Data.dll, Newtonsoft.Json.dll, UnityEngine.CoreModule.dll, UnityEngine.PhysicsModule.dll.

---

## Key Design Decisions

1. **SkinID Identification**: `skinID == 11162132011012UL` (fast, no reflection, works before ServerInit). Alternative: Component tags (slower).

2. **In-Place Config**: No component destruction (27.9s → <1s). Alternative: Destroy/recreate (too slow).

3. **Throttled Operations**: Dormancy 0.5s, roam 1s, navmesh fix 1s, debug 2s, combat every 4th frame. Reduces CPU in hot paths.

4. **Config Caching**: Re-check every 5 seconds. Avoids repeated file I/O.

5. **HomePosition Enforcement**: Check distance, clamp destinations, return wanderers. Prevents clustering.

6. **Combat Enhancement**: Rush/strafe/maintain distance. Melee: rush in. Ranged: 8-12m, strafe when close. Throttled every 4th frame.
7. **Dynamic Navmesh Switching**: Automatically updates AreaMask/AgentTypeID based on current position. Throttled to 1Hz, only runs for NPCs with NavmeshLocked=false. Enables NPCs to roam freely between monuments, terrain, and bases without NavMesh errors. Uses bounds-only authority for monuments (no sampling). **CRITICAL**: Navmesh remains unlocked after spawn-time configuration to allow dynamic switching. This matches the behavior of other plugins (BotReSpawn, FrankensteinPet).

---

## Future Enhancements

**Potential**: NavMesh validation (UnityEngine.AI), GunTrap/FlameTurret patching (`CheckTrigger()`), state machine patching, pathfinding optimization, loot system, dialogue system, squad system, dynamic spawning.

**Performance**: Batch processing, spatial partitioning, async processing, memory pooling.

---

## Key Assembly Files Reference

**GrimmNPC**: GrimmNPC.cs (main class), GrimmNPC.csproj, Patches/ (SpawnPatches, ThinkPatches, TargetingPatches, DamagePatches, NavigationPatches, SwimmingPatches, TurretTargetingPatches).

**Rust Core**: BaseNetworkable, BasePlayer, NPCPlayer, HumanNPC, ScientistNPC.

**AI System**: BaseAIBrain (Think, InitializeAI, StartMovementTick), ScientistBrain.

**Navigation**: BaseNavigator (SetDestination, DetermineNavigationType, PlaceOnNavMesh, Think, UpdateNavigation), NPCNavigator.

**Pathfinding**: HumanPathFinder, BasePathFinder, AIInformationZone (GetBestMovePointNear, GetBestRoamPoint, GetBestCoverPoint), AIMovePoint, AICoverPoint.

**NavMesh**: MonumentNavMesh, NavMeshTools, NPCSpawner.

**Movement**: BaseNpc (TickNavigationWater, TickAi).

**States**: ChaseState, RoamState, CombatState.

**Turrets**: AutoTurret (ShouldTarget patched), GunTrap, FlameTurret.

**Damage**: BaseCombatEntity (OnAttacked patched).

**Call Chains**: ServerInit → ApplyCustomConfig → InitializeAI → Init → PlaceOnNavMesh | Think → StateThink → SetDestination → ProcessCustomThink | TickMovement → Think → UpdateNavigation → [patches] | GetBestTarget → GetCustomBestTarget

---

## Plugin Compatibility

### BetterNpc Integration

**Status**: ✅ **Fully Compatible** - No conflicts with GrimmNPC's special weapons or think patches.

**BetterNpc Behavior**:
- Registers NPCs with GrimmNPC before spawn (uses `RegisterPending()`)
- Equips items after spawn (NextTick callbacks)
- Unpauses navigator as fallback (if GrimmNPC's InitializeAI patch didn't run)
- Does NOT patch attack methods or interfere with weapon handling
- Does NOT set `brain.HostileTargetsOnly` after spawn

**GrimmNPC Override Behavior**:
- **HostileTargetsOnly**: GrimmNPC sets `brain.HostileTargetsOnly = false` during spawn (SpawnPatches.cs) to ensure immediate player detection, regardless of BetterNpc's config value. This is intentional and documented.
- **Special Weapons**: GrimmNPC's `SpecialWeaponsHandler` handles all special weapon attacks (rockets, grenades, flamethrowers, bows, etc.) via `HandleCombatAttack()` called from ThinkPatches. BetterNpc does NOT interfere with this.
- **Think Patches**: GrimmNPC's `BaseAIBrain.Think()` postfix patch enhances combat behavior and calls special weapons handler. BetterNpc does NOT patch Think() or interfere with combat behavior.

**No Conflicts**:
- BetterNpc has no attack-related hooks (`OnNpcAttack`, `OnEntityAttacked`, etc.)
- BetterNpc does NOT patch `HumanNPC.GetBestTarget()`, `BaseAIBrain.Think()`, or any attack methods
- BetterNpc does NOT call `ShotTest()`, `ServerUse()`, or `SignalBroadcast()` on weapons
- BetterNpc only handles: NPC registration, item equipping, navigator unpause (fallback)

**Result**: GrimmNPC's special weapons system and think patches work seamlessly with BetterNpc-spawned NPCs. All weapon types (blowpipes, bows, crossbows, rockets, etc.) benefit from HumanNPC's native targeting system integration.

---

## Conclusion

Complete reference for understanding, maintaining, and extending GrimmNPC. Key takeaways: Performance critical (optimized hot paths), HomePosition critical (prevents clustering), organized patches, centralized config, public API, raiding system with Raid Goal support, satchel cycle state machine, BetterNpc compatibility verified. See `DEBUG_GUIDE.md` for debugging, `README.md` for user docs.

---

## Recent Updates (2025-01-15)

### Special Weapons Targeting Integration
- **Integrated with HumanNPC's native targeting system**: All special weapons now use HumanNPC's methods for accurate targeting
  - `GetAim()` - Uses `AttackEntity.ModifyAIAim()` for weapon-specific aim adjustments (sway, cone, etc.)
  - `IsTargetInEngagementRange()` - Uses HumanNPC's `IsTargetInRange()` and `EngagementRange()` methods
  - `CanSeeTarget()` - Uses HumanNPC's native LOS detection (more accurate than custom methods)
  - `GetEngagementRange()` - Gets effective range from weapon's `effectiveRange` property with proper multipliers
- **Rocket launcher fix**: Fixed rocket launcher firing using BotReSpawn's approach:
  - Gets `BaseLauncher` entity from `GetHeldEntity()`
  - Checks/decrements `primaryMagazine.contents` for ammo
  - Gets projectile prefab from `launcher.primaryMagazine.ammoType.GetComponent<ItemModProjectile>()`
  - Creates projectile from component's resource path (not hardcoded)
  - Calls `launcher.SignalBroadcast(BaseEntity.Signal.Attack)` on launcher entity (not NPC)
- **Universal weapon support**: All weapon types now benefit from improved targeting:
  - `BaseProjectile` weapons (rifles, pistols, blowpipes, bows, crossbows, flintlock) use `GetAim()` + `ShotTest()`
  - `BaseLauncher` weapons (rocket launchers, MGL) use BotReSpawn-style firing with `GetAim()`
  - `AttackEntity` weapons (flamethrowers, melee) use `GetAim()` for aim direction
- **Benefits**: Better accuracy, consistent with vanilla NPC behavior, respects weapon `effectiveRange` and `aiOnlyInRange` settings
- **Immediate targeting fix**: Fixed NPCs not detecting players until shot by setting `HostileTargetsOnly=false` during sense system initialization. NPCs now detect and target players immediately when they spawn in (with LOS), matching old version behavior.

### Satchel Cycle System
- Implemented state machine for satchel-throwing NPCs (Throw → Wait → Melee → Retreat → Repeat)
- Based on RaidingZombies.cs coroutine pattern
- Wall detection before throwing (prevents wasted throws)
- Proper retreat distance (12m) and duration (10s) to avoid explosions
- Movement pattern matches RaidingZombies (2-12m range around target)

### Raid Goal System
- Added `RaidGoalActive`, `RaidGoalPosition`, `RaidGoalEntityId` to `CustomNpcData`
- NPCs can target structures (TC, doors, walls) without requiring player targets
- Raiding system finds and attacks blocking structures between NPC and Raid Goal
- NPCs move toward Raid Goal even when no target is in memory

### Wake-Up Escalation
- NPCs with valid targets but no LOS for ≥1.5-2s are forced into Chase state
- Target stability required (same target for ≥1.5s) to prevent spinning
- State forcing throttled (once per 2 seconds)
- Prefers Chase with cautious movement over Combat aim lock

### DefendableHomes Integration
- DefendableHomes checks for raiding NPCs before interfering with weapon firing
- Prevents `UseExplosiveOnTarget()` from conflicting with GrimmNPC's satchel cycle
- All weapon firing for raiding NPCs handled by GrimmNPC's raiding system
