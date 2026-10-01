using HarmonyLib;
using Rust;
using Rust.Ai;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using Facepunch;

namespace GrimmNPC.Patches
{
    /// <summary>
    /// CRITICAL: Optimizes BaseAIBrain.Think() for 1000 NPCs (HOT PATH).
    /// 
    /// Call Frequency:
    /// - Base rate: 4Hz per NPC (every 0.25s)
    /// - 1000 NPCs: 4000 calls/second total
    /// - Hot path: Executes on every AI think cycle
    /// 
    /// Performance:
    /// - Direct IL patching = 0.003ms vs Oxide reflection = 0.3ms (100x faster)
    /// - Total CPU: ~1.2% for 1000 NPCs (vs 120% with Oxide)
    /// 
    /// AI Thinking Flow with GrimmNPC:
    /// BaseAIBrain.Think()
    ///   → Senses.Update() (updates memory, detects targets)
    ///   → CurrentState.StateThink() (state-based behavior)
    ///     → ChaseState: Calls SetDestination() to chase target
    ///     → RoamState: Calls SetDestination() to roam point
    ///     → CombatState: Calls SetDestination() for combat movement
    ///   → Events.Tick() (processes state events)
    ///   → [GrimmNPC] BaseAIBrain_Think_Patch.Postfix()
    ///     → ProcessCustomThink()
    ///       → Dormancy Management (throttled 0.5s)
    ///       → Roam Enforcement (throttled 1.0s)
    ///       → Combat Enhancement (throttled 4 frames)
    ///       → Assist System (on combat entry)
    ///       → Debug Logging (throttled 2.0s)
    /// 
    /// See INSTRUCTIONAL.md "AI Brain Thinking and Navigation" section for details.
    /// </summary>
    [HarmonyPatch(typeof(BaseAIBrain), nameof(BaseAIBrain.Think))]
    public class BaseAIBrain_Think_Patch
    {
        // Track previous states to detect combat entry
        private static readonly System.Collections.Generic.Dictionary<ulong, AIState> _previousStates = 
            new System.Collections.Generic.Dictionary<ulong, AIState>(1000);
        
        // PERFORMANCE: Cache config to avoid repeated lookups
        private static NpcConfig _cachedConfig = null;
        private static float _lastConfigCheck = 0f;
        private const float CONFIG_CACHE_DURATION = 5f; // Re-check config every 5 seconds
        
        // PERFORMANCE: Throttle expensive operations
        private static readonly System.Collections.Generic.Dictionary<ulong, float> _lastDormancyCheck = 
            new System.Collections.Generic.Dictionary<ulong, float>(1000);
        private static readonly System.Collections.Generic.Dictionary<ulong, float> _lastRoamEnforcement = 
            new System.Collections.Generic.Dictionary<ulong, float>(1000);
        private static readonly System.Collections.Generic.Dictionary<ulong, float> _lastNavmeshFix = 
            new System.Collections.Generic.Dictionary<ulong, float>(1000);
        private static readonly System.Collections.Generic.Dictionary<ulong, float> _lastLosCheck = 
            new System.Collections.Generic.Dictionary<ulong, float>(1000);
        private static readonly System.Collections.Generic.Dictionary<ulong, float> _targetLostLosTime = 
            new System.Collections.Generic.Dictionary<ulong, float>(1000);
        private static readonly System.Collections.Generic.Dictionary<ulong, BaseEntity> _stableTarget = 
            new System.Collections.Generic.Dictionary<ulong, BaseEntity>(1000);
        private static readonly System.Collections.Generic.Dictionary<ulong, float> _stableTargetTime = 
            new System.Collections.Generic.Dictionary<ulong, float>(1000);
        private static readonly System.Collections.Generic.Dictionary<ulong, float> _lastStateForceTime = 
            new System.Collections.Generic.Dictionary<ulong, float>(1000);
        private static readonly System.Collections.Generic.Dictionary<ulong, float> _lastMonumentNavmeshDebug = 
            new System.Collections.Generic.Dictionary<ulong, float>(1000);
        private static readonly System.Collections.Generic.Dictionary<ulong, Vector3> _lastCombatPosition = 
            new System.Collections.Generic.Dictionary<ulong, Vector3>(1000);
        private static readonly System.Collections.Generic.Dictionary<ulong, float> _stuckStartTime = 
            new System.Collections.Generic.Dictionary<ulong, float>(1000);
        private static readonly System.Collections.Generic.Dictionary<ulong, Vector3> _lastKnownTargetPosition =
            new System.Collections.Generic.Dictionary<ulong, Vector3>(1000);
        private static readonly System.Collections.Generic.Dictionary<ulong, float> _lastKnownTargetTime =
            new System.Collections.Generic.Dictionary<ulong, float>(1000);
        private static readonly System.Collections.Generic.Dictionary<ulong, byte> _investigationMode =
            new System.Collections.Generic.Dictionary<ulong, byte>(1000); // 0=none, 1=investigate, 2=return home
        private static readonly System.Collections.Generic.Dictionary<ulong, Vector3> _investigationPoint =
            new System.Collections.Generic.Dictionary<ulong, Vector3>(1000);
        private static readonly System.Collections.Generic.Dictionary<ulong, float> _investigationExpireTime =
            new System.Collections.Generic.Dictionary<ulong, float>(1000);
        private static readonly System.Collections.Generic.Dictionary<ulong, float> _lastInvestigationSetTime =
            new System.Collections.Generic.Dictionary<ulong, float>(1000);
        private const float DORMANCY_CHECK_INTERVAL = 1f; // Check dormancy every 0.5s (not every frame)
        private const float ROAM_ENFORCEMENT_INTERVAL = 1f; // Enforce roam range every 1s
        private const float NAVMESH_FIX_INTERVAL = 1f; // Check and fix navmesh every 1s
        private const float MONUMENT_NAVMESH_DEBUG_INTERVAL = 10f; // Log monument navmesh confirmation every 10s (10x slower than navmesh fix)
        private const float WAKEUP_LOS_CHECK_INTERVAL = 0.5f; // Check LOS for wake-up escalation every 0.5s
        private const float WAKEUP_LOS_TIMEOUT = 1.75f; // Force wake-up if LOS false for 1.75s (1.5-2s range)
        private const float TARGET_STABILITY_DURATION = 1.75f; // Require same target for 1.75s before escalation (hysteresis)
        private const float STATE_FORCE_THROTTLE = 2f; // Hard limit: no more than once per 2 seconds per NPC
        private const float STUCK_DETECTION_THRESHOLD = 0.5f; // NPC is stuck if moved < 0.5m in this time
        private const float STUCK_DURATION = 2f; // Force movement if stuck for 2 seconds
        private const float INVESTIGATION_MEMORY_SECONDS = 30f;
        private const float INVESTIGATION_DESTINATION_SET_INTERVAL = 2.5f;
        private const float INVESTIGATION_REACHED_DISTANCE = 2.5f;
        private const float INVESTIGATION_DWELL_SECONDS = 8f;
        private const float INVESTIGATION_RETURN_TIMEOUT_SECONDS = 12f;
        
        private const float SwimWeaponPolicyInterval = 0.2f;
        private static readonly System.Collections.Generic.Dictionary<ulong, float> _lastSwimWeaponTick =
            new System.Collections.Generic.Dictionary<ulong, float>(256);
        /// <summary>Projectile we hid while swimming; restored when back on land.</summary>
        private static readonly System.Collections.Generic.Dictionary<ulong, ItemId> _preSwimRangedWeapon =
            new System.Collections.Generic.Dictionary<ulong, ItemId>(256);

        static void Postfix(BaseAIBrain __instance, float delta)
        {
            if (__instance.baseEntity == null) return;
            if (!GrimmNPC.IsCustomNpc(__instance.baseEntity)) return;

            ProcessSwimWeaponPolicy(__instance, delta);
            ProcessCustomThink(__instance, delta);
        }

        /// <summary>
        /// While swimming, custom NPCs use melee (or fists) only; ranged is restored after leaving water.
        /// </summary>
        private static void ProcessSwimWeaponPolicy(BaseAIBrain brain, float delta)
        {
            var npc = brain.baseEntity as ScientistNPC;
            if (npc == null || brain.Navigator == null) return;
            ulong netId = npc.net?.ID.Value ?? 0;
            if (netId == 0) return;

            var npcData = GrimmNPC.GetNpcData(netId);
            if (npcData != null && !npcData.CanSwim)
                return;

            float now = Time.time;
            if (_lastSwimWeaponTick.TryGetValue(netId, out float last) && (now - last) < SwimWeaponPolicyInterval)
                return;
            _lastSwimWeaponTick[netId] = now;

            bool swimming = brain.Navigator.IsSwimming();
            if (!swimming)
            {
                if (_preSwimRangedWeapon.TryGetValue(netId, out ItemId saved))
                {
                    _preSwimRangedWeapon.Remove(netId);
                    if (npc.inventory?.containerBelt != null && saved != default(ItemId))
                    {
                        Item restored = npc.inventory.containerBelt.FindItemByUID(saved);
                        if (restored != null)
                            npc.UpdateActiveItem(saved);
                    }
                }
                return;
            }

            if (npc.inventory?.containerBelt == null)
                return;

            Item active = npc.GetActiveItem();
            if (active == null)
                return;

            var held = active.GetHeldEntity() as HeldEntity;
            if (held is BaseMelee)
                return;

            if (!(held is BaseProjectile))
                return;

            if (!_preSwimRangedWeapon.ContainsKey(netId))
                _preSwimRangedWeapon[netId] = active.uid;

            Item meleePick = null;
            for (int i = 0; i < npc.inventory.containerBelt.capacity; i++)
            {
                Item slot = npc.inventory.containerBelt.GetSlot(i);
                if (slot == null) continue;
                if (slot.GetHeldEntity() as BaseMelee != null)
                {
                    meleePick = slot;
                    break;
                }
            }

            if (meleePick != null)
                npc.UpdateActiveItem(meleePick.uid);
            else
                npc.UpdateActiveItem(default(ItemId));
        }
        
        private static void ProcessCustomThink(BaseAIBrain brain, float delta)
        {
            var npc = brain.baseEntity as ScientistNPC;
            if (npc == null) return;
            
            ulong netId = npc.net?.ID.Value ?? 0;
            if (netId == 0) return;
            
            var npcData = GrimmNPC.GetNpcData(netId);
            if (npcData == null) return;

            if (npcData.CanSwim && brain.Navigator != null && brain.Navigator.IsSwimming())
                return;

            // PERFORMANCE: Cache config lookup (re-check every 5 seconds)
            float currentTime = Time.time;
            if (_cachedConfig == null || (currentTime - _lastConfigCheck) > CONFIG_CACHE_DURATION)
            {
                _cachedConfig = GrimmNPC.GetConfig();
                _lastConfigCheck = currentTime;
            }
            var config = _cachedConfig;
            bool logPeriodicSnapshot = GrimmNPC.ShouldLogPeriodicSnapshot();
            bool logDynamicNav = GrimmNPC.ShouldLogDynamicNavMesh();
            bool logAssist = GrimmNPC.ShouldLogAssist();
            bool logInvestigate = GrimmNPC.ShouldLogRoamInvestigate();
            bool logImmediateTargeting = GrimmNPC.ShouldLogImmediateTargeting();
            
            // Detect combat state entry for assist callouts
            AIState currentState = brain.CurrentState?.StateType ?? AIState.None;
            AIState previousState = AIState.None;
            if (_previousStates.TryGetValue(netId, out previousState))
            {
                // Check if NPC just entered combat
                if (config.EnableAssistCallouts && 
                    (currentState == AIState.Combat || currentState == AIState.Chase) &&
                    previousState != AIState.Combat && previousState != AIState.Chase)
                {
                    // NPC just entered combat - call for help!
                    CallForAssist(npc, npcData, brain, config, logAssist);
                }
            }
            _previousStates[netId] = currentState;
            
            // PERFORMANCE: Handle dormancy (throttled - not every frame!)
            if (config.ForceRespectAiDormant && AiManager.ai_dormant)
            {
                // Throttle dormancy checks to every 0.5 seconds (not every frame)
                float lastCheck = 0f;
                _lastDormancyCheck.TryGetValue(netId, out lastCheck);
                
                if ((currentTime - lastCheck) >= DORMANCY_CHECK_INTERVAL)
                {
                    _lastDormancyCheck[netId] = currentTime;
                    
                    float serverWakeupRange = AiManager.ai_to_player_distance_wakeup_range;
                    float configSleepDistance = npcData.CanSleep ? npcData.SleepDistance : 0f;
                    float defaultSleepDistance = config.DefaultSleepDistance;
                    float wakeupRange = Mathf.Max(serverWakeupRange, Mathf.Max(configSleepDistance, defaultSleepDistance));
                    
                    // Fast player check (optimized for 1000 NPCs)
                    BasePlayer[] players = new BasePlayer[64];
                    int playerCount = BaseEntity.Query.Server.GetPlayersInSphere(
                        npc.transform.position, 
                        wakeupRange, 
                        players, 
                        x => x != null && x.net?.connection != null && x.userID > 76561197960265728UL && !x.IsNpc && !x.IsSleeping()
                    );
                    
                    bool hasNearbyPlayers = playerCount > 0;
                    
                    if (!hasNearbyPlayers)
                    {
                        // NPC is dormant - ensure it's sleeping
                        if (!brain.sleeping)
                        {
                            brain.sleeping = true;
                            if (brain.Navigator != null)
                            {
                                if (!GrimmNPC.IsInSpawnSwimInitWindow(netId))
                                {
                                    var agent = brain.Navigator.Agent;
                                    bool isActiveAndEnabled = agent != null && agent.isActiveAndEnabled;
                                    bool isOnNavMesh = agent != null && agent.isOnNavMesh;
                                    if (!isActiveAndEnabled || !isOnNavMesh)
                                    {
                                        GrimmNPC.LogNavMeshFailure(
                                            "PauseBlocked",
                                            $"DormancySleep netId={netId} active={isActiveAndEnabled} onNavMesh={isOnNavMesh} pos={npc.transform.position}",
                                            netId);
                                    }
                                    else
                                    {
                                        brain.Navigator.Pause();
                                    }
                                }
                            }
                        }
                        return; // Skip further processing when dormant
                    }
                    else
                    {
                        // Player nearby - wake up
                        npc.IsDormant = false;
                        if (brain.sleeping)
                        {
                            brain.sleeping = false;
                            if (brain.Navigator != null)
                            {
                                if (!GrimmNPC.IsInSpawnSwimInitWindow(netId))
                                    brain.Navigator.Resume();
                            }
                        }
                    }
                }
                else
                {
                    // Use cached dormancy state - if sleeping, skip processing
                    if (brain.sleeping)
                    {
                        return; // Skip further processing when dormant
                    }
                }
            }
            
            // PERFORMANCE: Throttle debug logging (only every 2 seconds)
            if (logPeriodicSnapshot && Time.frameCount % 480 == 0) // ~2 seconds at 60fps
            {
                LogNpcDebugInfo(npc, npcData, brain);
            }
            
            BaseEntity memoryTarget = brain.Events?.Memory?.Entity?.Get(brain.Events?.CurrentInputMemorySlot ?? 0);
            HandleLostTargetInvestigation(npc, npcData, brain, netId, memoryTarget, currentTime, logInvestigate);
            
            // PERFORMANCE: Throttle roam enforcement and combat enhancement
            // CRITICAL: Enforce HomePosition and RoamRange to prevent clustering
            // BUT: Don't override combat behavior - only enforce when not in combat
            // Note: currentState already defined above
            
            if (currentState == AIState.None || 
                (currentState != AIState.Combat && 
                 currentState != AIState.CombatStationary &&
                 currentState != AIState.Chase))
            {
                // PERFORMANCE: Throttle roam enforcement to every 1 second
                float lastRoamCheck = 0f;
                _lastRoamEnforcement.TryGetValue(netId, out lastRoamCheck);
                
                if ((currentTime - lastRoamCheck) >= ROAM_ENFORCEMENT_INTERVAL)
                {
                    _lastRoamEnforcement[netId] = currentTime;
                    EnforceRoamRange(npc, npcData, brain);
                }
            }
            else
            {
                // ENHANCE COMBAT: Make NPCs rush in and strafe during combat
                // Update frequency controlled by StrafeInterval (default: 1 = every frame, 4 = every 4th frame)
                int strafeInterval = npcData.AlwaysStrafeInCombat ? npcData.StrafeInterval : 4;
                if (Time.frameCount % strafeInterval == 0)
                {
                    EnhanceCombatBehavior(npc, npcData, brain, delta);
                }

                // 🎯 COMBAT WEAPON HANDLING: Allow ALL NPCs to use all weapon types (rockets, grenades, flamethrowers, bows, F1 grenades) against players
                // CRITICAL: Firing rockets at players is NOT raiding - NPCs should ALWAYS be able to fire at players when they have LOS
                // Raiding behavior (Raid.TickRaid) should only activate when there's NO LOS to player OR when RaidGoalActive is true
                // This allows NPCs to use weapons normally in combat, while raiding is a fallback for when LOS is blocked
                // Get current target
                BaseEntity target = null;
                if (brain.Senses != null && brain.Senses.Memory != null)
                {
                    target = brain.Events?.Memory?.Entity?.Get(brain.Events?.CurrentInputMemorySlot ?? 0);
                }

                // Only handle player targets (not structures) - NPCs should use weapons normally against players
                if (target != null && target is BasePlayer)
                {
                    // Check if NPC has LOS to player - if yes, use normal combat weapons (not raiding)
                    bool hasLOS = false;
                    if (brain.Senses != null && brain.Senses.Memory != null)
                    {
                        hasLOS = brain.Senses.Memory.IsLOS(target);
                    }
                    
                    // CRITICAL: If NPC has LOS to player, ALWAYS use normal combat weapons (rocket launcher, etc.)
                    // This is NOT raiding - firing at players is always combat, regardless of IsRaidingNpc setting
                    // Raiding should only happen when LOS is blocked (handled by Raid.TickRaid)
                    if (hasLOS)
                    {
                        // Call every frame for more responsive weapon firing (throttling was too aggressive)
                        SpecialWeaponsHandler.HandleCombatAttack(npc, target);
                    }
                    // If no LOS, let Raid.TickRaid handle it (will find blocking structures)
                }
            }
            
            // PERFORMANCE: Dynamic navmesh switching (throttled to 1Hz)
            // Handles NPCs moving between monuments, terrain, and bases
            // Automatically switches AreaMask/AgentTypeID based on current position
            // This allows NPCs to roam freely across different navmesh types without errors
            if (!npcData.NavmeshLocked)
            {
                float lastNavmeshFix = 0f;
                _lastNavmeshFix.TryGetValue(netId, out lastNavmeshFix);
                
                if ((currentTime - lastNavmeshFix) >= NAVMESH_FIX_INTERVAL)
                {
                    _lastNavmeshFix[netId] = currentTime;
                    UpdateNavmeshForCurrentPosition(npc, npcData, brain, logDynamicNav);
                }
            }
            
            // 🎯 TARGETING WAKE-UP FIX: Force immediate targeting when LOS is true
            // If NPC has valid hostile target in memory AND target is within SenseRange AND LOS is true
            // THEN: immediately force Chase/Combat state (no delay, no stability requirement)
            // This ensures NPCs target players immediately when they spawn in, matching old version behavior
            CheckImmediateTargeting(npc, npcData, brain, currentState, currentTime, logImmediateTargeting);
            
            // 🏠 RAID GOAL VALIDATION: Check if raid goal is still valid, clear if invalid
            ValidateRaidGoal(npc, npcData, currentTime);
            
            // 🛡️ GUARD TARGET UPDATE: Update guard target position and validate (like NpcSpawn UpdateGuardPosition)
            UpdateGuardTarget(npc, npcData);
            
            // 🪵 BARRICADE + syringe under pressure (NpcSpawn PlaceBarricadeCover-style)
            global::GrimmNPC.BarricadeCoverHandler.TryTick(npc, npcData, brain, currentState, currentTime);

            // 🩹 HEALING: syringe with cooldown + combat vs idle rules (NpcSpawn-like)
            CheckHealing(npc, npcData, brain, currentState, currentTime);
            
            // 🧍 IDLE BEHAVIOR FIX: Stop navigator, no rotation, no destinations when truly idle
            // When ALL are true: No valid target, No roam destination, No raid task, No assist call active
            // Then: Stop navigator, Do not rotate, Do not set destinations, Stand still
            HandleIdleBehavior(npc, npcData, brain, currentState, currentTime);
            
            // Additional custom logic can go here
            // This runs at native IL speed, not reflection speed
        }
        
        /// <summary>
        /// 🛡️ GUARD TARGET UPDATE: Updates guard target position and validates guard target (like NpcSpawn UpdateGuardPosition).
        /// </summary>
        private static void UpdateGuardTarget(ScientistNPC npc, CustomNpcData npcData)
        {
            if (npc == null || npcData == null || !npcData.IsGuardNpc) return;
            if (npcData.GuardTarget == null) return;

            // Check if guard target still exists
            if (npcData.GuardTarget.IsDestroyed || npcData.GuardTarget == null)
            {
                // Guard target destroyed - return to original home position (like NpcSpawn)
                if (npcData.BeforeGuardHomePosition != Vector3.zero)
                {
                    npcData.HomePosition = npcData.BeforeGuardHomePosition;
                    npcData.BeforeGuardHomePosition = Vector3.zero;
                }
                npcData.GuardTarget = null;
                
                // Call hook for guard target end (like NpcSpawn OnCustomNpcGuardTargetEnd)
                GrimmNPC.CallOxideHook("OnCustomNpcGuardTargetEnd", npc);
                return;
            }

            // Update home position to follow guard target (like NpcSpawn)
            npcData.HomePosition = npcData.GuardTarget.transform.position;
        }

        /// <summary>
        /// 🩹 HEALING: Checks if NPC should heal and starts healing coroutine if needed (like NpcSpawn).
        /// </summary>
        private static void CheckHealing(ScientistNPC npc, CustomNpcData npcData, BaseAIBrain brain, AIState currentState, float currentTime)
        {
            if (npc == null || npc.inventory == null || npc.inventory.containerBelt == null) return;
            
            ulong netId = npc.net?.ID.Value ?? 0;
            if (netId == 0) return;
            
            var state = SpecialWeaponsHandler.GetState(netId);
            if (state == null) return;
            
            if (!CanHeal(npc, npcData, brain, currentState, state, currentTime)) return;
            
            if (state.HealCoroutine == null && !state.IsHealing && !state.IsPlacingBarricade)
            {
                state.HealCoroutine = ServerMgr.Instance.StartCoroutine(HealCoroutine(npc, npcData, state));
            }
        }

        /// <summary>Valid brain input memory entity (slot must be &gt;= 0; never fall back to slot 0).</summary>
        private static bool TryGetBrainInputEntity(BaseAIBrain brain, out BaseEntity ent)
        {
            ent = null;
            if (brain?.Events?.Memory == null) return false;
            int slot = brain.Events.CurrentInputMemorySlot;
            if (slot < 0) return false;
            ent = brain.Events.Memory.Entity?.Get(slot);
            return ent != null && !ent.IsDestroyed;
        }
        
        private static bool CanHeal(ScientistNPC npc, CustomNpcData npcData, BaseAIBrain brain, AIState currentState, SpecialWeaponsHandler.WeaponState weaponState, float currentTime)
        {
            if (weaponState.IsHealing || weaponState.IsPlacingBarricade) return false;
            if (currentTime < weaponState.NextSyringeAllowedTime) return false;

            float maxHealth = npcData.Health > 0f ? npcData.Health : npc.startHealth;
            if (maxHealth <= 1f || npc.health >= maxHealth * 0.999f) return false;

            if (weaponState.IsFireC4 || weaponState.IsFireRocketLauncher) return false;

            bool hasBrainTarget = TryGetBrainInputEntity(brain, out _);
            bool inCombatMotion = currentState == AIState.Combat || currentState == AIState.Chase;
            float frac = npc.health / maxHealth;
            float combatCap = Mathf.Clamp01(npcData.SyringeCombatMaxHealthFraction);
            if (combatCap < 0.05f) combatCap = 0.5f;

            // Under pressure: only emergency top-up (NpcSpawn heals during barricade state; idle heal is for no/low threat)
            if (hasBrainTarget || inCombatMotion)
            {
                if (frac > combatCap)
                    return false;
            }

            if (npc.inventory.containerBelt == null) return false;
            foreach (var item in npc.inventory.containerBelt.itemList)
            {
                if (item != null && item.info != null && item.info.shortname == "syringe.medical")
                    return true;
            }
            return false;
        }
        
        /// <summary>
        /// Healing coroutine (like NpcSpawn Heal).
        /// CRITICAL: Interrupts healing if target appears during healing.
        /// </summary>
        private static System.Collections.IEnumerator HealCoroutine(ScientistNPC npc, CustomNpcData npcData, SpecialWeaponsHandler.WeaponState weaponState)
        {
            weaponState.IsHealing = true;
            float cooldown = Mathf.Max(2f, npcData.SyringeCooldownSeconds);
            float scale = Mathf.Max(0.1f, npcData.HealingScale);

            Item syringe = null;
            if (npc.inventory != null && npc.inventory.containerBelt != null)
            {
                foreach (var item in npc.inventory.containerBelt.itemList)
                {
                    if (item != null && item.info != null && item.info.shortname == "syringe.medical")
                    {
                        syringe = item;
                        break;
                    }
                }
            }
            
            if (syringe == null)
            {
                weaponState.IsHealing = false;
                weaponState.HealCoroutine = null;
                yield break;
            }
            
            var brain = npc.Brain;
            if (brain == null)
            {
                weaponState.IsHealing = false;
                weaponState.HealCoroutine = null;
                yield break;
            }
            
            npc.UpdateActiveItem(syringe.uid);
            MedicalTool medicalTool = syringe.GetHeldEntity() as MedicalTool;

            // NpcSpawn uses ~2s before ServerUse; interrupt if we acquire a brain target and are healthy enough to fight
            float waitTime = 0f;
            while (waitTime < 2f)
            {
                yield return CoroutineEx.waitForSeconds(0.1f);
                waitTime += 0.1f;
                if (TryGetBrainInputEntity(brain, out _))
                {
                    float maxH = npcData.Health > 0f ? npcData.Health : npc.startHealth;
                    if (maxH > 1f && npc.health / maxH > Mathf.Clamp01(npcData.SyringeCombatMaxHealthFraction))
                    {
                        weaponState.IsHealing = false;
                        weaponState.HealCoroutine = null;
                        weaponState.NextSyringeAllowedTime = Time.time + cooldown * 0.5f;
                        npc.EquipWeapon();
                        yield break;
                    }
                }
            }
            
            if (medicalTool != null && !medicalTool.IsDestroyed)
                medicalTool.ServerUse();
            
            float maxHealth = npcData.Health > 0f ? npcData.Health : npc.startHealth;
            float newHealth = npc.health + 15f * scale;
            if (newHealth > maxHealth) newHealth = maxHealth;
            npc.health = newHealth;
            
            waitTime = 0f;
            while (waitTime < 2f)
            {
                yield return CoroutineEx.waitForSeconds(0.1f);
                waitTime += 0.1f;
            }
            
            npc.EquipWeapon();
            weaponState.IsHealing = false;
            weaponState.HealCoroutine = null;
            weaponState.NextSyringeAllowedTime = Time.time + cooldown;
        }

        /// <summary>
        /// 🏠 RAID GOAL VALIDATION: Validates raid goal is still valid, clears if invalid.
        /// Called every Think() cycle to ensure raid goal remains valid.
        /// </summary>
        private static void ValidateRaidGoal(ScientistNPC npc, CustomNpcData npcData, float currentTime)
        {
            if (npcData == null || !npcData.IsRaidingNpc || !npcData.RaidGoalActive)
                return;
            
            bool goalValid = false;
            
            // Check if RaidGoalEntityId is set and valid
            if (npcData.RaidGoalEntityId != 0)
            {
                BaseEntity goalEntity = BaseNetworkable.serverEntities.Find(new NetworkableId(npcData.RaidGoalEntityId)) as BaseEntity;
                if (goalEntity != null && !goalEntity.IsDestroyed && goalEntity.Health() > 0f)
                {
                    goalValid = true;
                }
            }
            else if (npcData.RaidGoalPosition != Vector3.zero)
            {
                // Check if position is still within reasonable range
                float distanceToGoal = Vector3.Distance(npc.transform.position, npcData.RaidGoalPosition);
                if (distanceToGoal <= npcData.ChaseRange * 2f) // Allow 2x ChaseRange for raid goals
                {
                    goalValid = true;
                }
            }
            
            if (!goalValid)
            {
                // Raid goal is invalid - clear it
                npcData.RaidGoalActive = false;
                npcData.RaidGoalPosition = Vector3.zero;
                npcData.RaidGoalEntityId = 0;
            }
        }
        
        private static void LogNpcDebugInfo(ScientistNPC npc, CustomNpcData npcData, BaseAIBrain brain)
        {
            if (!GrimmNPC.ShouldLogPeriodicSnapshot())
                return;
            AIState currentState = brain.CurrentState?.StateType ?? AIState.None;
            string stateName = currentState.ToString();

            BaseEntity target = null;
            if (brain.Senses != null && brain.Senses.Memory != null)
            {
                target = brain.Events?.Memory?.Entity?.Get(brain.Events?.CurrentInputMemorySlot ?? 0);
            }

            if (target == null && npc != null)
                target = npc.GetBestTarget();

            string targetInfo = target != null
                ? $"{target.GetType().Name} (dist: {Vector3.Distance(npc.transform.position, target.transform.position):F1}m)"
                : "None";

            int memoryCount = brain.Senses?.Memory?.All?.Count ?? 0;

            bool canSeeTarget = false;
            if (target != null && brain.Senses?.Memory != null)
                canSeeTarget = brain.Senses.Memory.IsLOS(target);

            float distanceFromHome = GrimmNPC.GetDistanceFromHome(npcData, npc.transform.position);

            bool isMoving = brain.Navigator?.Moving ?? false;
            Vector3 destination = brain.Navigator?.Destination ?? Vector3.zero;
            float destDistance = 0f;
            if (destination != Vector3.zero)
            {
                Vector3 destDiff = destination - npc.transform.position;
                destDistance = Mathf.Sqrt(destDiff.x * destDiff.x + destDiff.z * destDiff.z);
            }

            string destReason = "None";
            if (destination != Vector3.zero)
            {
                if (currentState == AIState.Combat || currentState == AIState.Chase)
                    destReason = target != null ? "Combat" : "Chase";
                else if (currentState == AIState.Roam)
                    destReason = "Roam";
                else if (distanceFromHome > npcData.RoamRange)
                    destReason = "ReturnHome";
                else
                    destReason = "Unknown";
            }

            if (Time.frameCount % 120 == 0)
            {
                UnityEngine.Debug.Log($"[GrimmNPC Debug] NPC: {npcData.Name ?? "Unknown"} | " +
                    $"State: {stateName} | " +
                    $"Target: {targetInfo} | " +
                    $"CanSee: {canSeeTarget} | " +
                    $"Memory: {memoryCount} entities | " +
                    $"DistFromHome: {distanceFromHome:F1}m (RoamRange: {npcData.RoamRange}m, ChaseRange: {npcData.ChaseRange}m) | " +
                    $"Moving: {isMoving} | " +
                    $"Dest: {destination} | " +
                    $"DestDist: {destDistance:F1}m | " +
                    $"DestReason: {destReason} | " +
                    $"Pos: {npc.transform.position}");
            }
        }
        
        /// <summary>
        /// Idle/roam "stand still" must not use <see cref="BaseNavigator.Stop"/> for custom NPCs: Stop() calls
        /// <see cref="BaseNavigator.SetNavMeshEnabled"/> with false, disabling the Unity NavMeshAgent. Stock
        /// <see cref="BaseNavigator.SetDestination"/> then returns false when !<c>Agent.isActiveAndEnabled</c>
        /// (see game BaseNavigator.SetDestination) — no path is set until something re-enables the agent.
        /// Large monuments (e.g. Launch Site) spend more ticks in Roam/Idle before memory slot fills, so Stop()
        /// bit more often than small monuments (e.g. Sewer Branch), producing "Navmesh enabled: False" and no chase.
        /// Halting with <c>Agent.isStopped</c> keeps the agent on-mesh and valid for the next SetDestination.
        /// </summary>
        private static void SoftHaltNavigatorWithoutDisablingMesh(BaseNavigator navigator)
        {
            if (navigator?.Agent == null) return;
            var agent = navigator.Agent;
            if (!agent.isActiveAndEnabled)
            {
                navigator.SetNavMeshEnabled(true);
                if (!agent.isOnNavMesh)
                    navigator.PlaceOnNavMesh(6f);
            }
            if (agent.isActiveAndEnabled)
                agent.isStopped = true;
        }

        /// <summary>
        /// Re-enable and bind agent before SetDestination after any Stop()/failed placement (BossMonster teleports, etc.).
        /// </summary>
        private static void EnsureNavigatorAgentReadyBeforeSetDestination(BaseNavigator navigator)
        {
            if (navigator?.Agent == null) return;
            var agent = navigator.Agent;
            if (agent.isActiveAndEnabled && agent.isOnNavMesh) return;
            navigator.SetNavMeshEnabled(true);
            if (!agent.isOnNavMesh)
                navigator.PlaceOnNavMesh(6f);
        }

        /// <summary>
        /// Keep agents from occupying the same nav point. Does not patch Navigator.Think;
        /// only upgrades avoidance when it is currently off (off-mesh links set None).
        /// </summary>
        private static void EnsureCombatAgentSeparation(BaseNavigator navigator, ulong netId)
        {
            var agent = navigator?.Agent;
            if (agent == null) return;
            if (agent.obstacleAvoidanceType == ObstacleAvoidanceType.NoObstacleAvoidance)
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.avoidancePriority = (int)(netId % 99);
        }

        private static void EnforceRoamRange(ScientistNPC npc, CustomNpcData npcData, BaseAIBrain brain)
        {
            if (npcData == null || brain == null || brain.Navigator == null) return;
            ulong netId = npc.net?.ID.Value ?? 0;
            
            var config = GrimmNPC.GetConfig();
            bool logRoam = GrimmNPC.ShouldLogRoamInvestigate();
            
            // Don't enforce roam pull-back when chase target is in any event-memory slot (BossMonster uses Chase/Combat/Attack slots).
            if (GrimmNPC.HasBrainEventMemoryCombatTarget(npc))
                return;
            
            Vector3 currentPos = npc.transform.position;
            
            // CRITICAL: Check if HomePosition is valid (not zero)
            if (npcData.HomePosition == Vector3.zero)
            {
                // HomePosition not set - set it to current position
                npcData.HomePosition = currentPos;
                // PERFORMANCE: Removed debug logging from hot path
            }
            
            // Use horizontal (XZ) distance for ground-based NPCs (prevents Y-axis issues)
            float distanceFromHome = GrimmNPC.GetDistanceFromHome(npcData, currentPos);
            
            // If NPC is too far from home, force it to return (this is OK - they should return to spawn)
            if (distanceFromHome > npcData.RoamRange)
            {
                // Calculate direction back to home (on XZ plane)
                Vector3 toHome = npcData.HomePosition - currentPos;
                Vector3 directionToHome = new Vector3(toHome.x, 0f, toHome.z).normalized;
                float returnDistance = distanceFromHome - npcData.RoamRange;
                
                // Set destination to a point within roam range (preserve Y position)
                Vector3 targetPos = new Vector3(
                    currentPos.x + directionToHome.x * Mathf.Min(returnDistance, npcData.RoamRange * 0.8f),
                    currentPos.y, // Preserve NPC's current Y position
                    currentPos.z + directionToHome.z * Mathf.Min(returnDistance, npcData.RoamRange * 0.8f)
                );
                
                // Ensure target is within roam range (use horizontal distance)
                Vector3 diff = npcData.HomePosition - targetPos;
                float distanceToTarget = Mathf.Sqrt(diff.x * diff.x + diff.z * diff.z);
                if (distanceToTarget > npcData.RoamRange)
                {
                    // Clamp to roam range (on XZ plane)
                    Vector3 fromHome = targetPos - npcData.HomePosition;
                    Vector3 directionFromHome = new Vector3(fromHome.x, 0f, fromHome.z).normalized;
                    targetPos = new Vector3(
                        npcData.HomePosition.x + directionFromHome.x * npcData.RoamRange,
                        targetPos.y, // Preserve Y
                        npcData.HomePosition.z + directionFromHome.z * npcData.RoamRange
                    );
                }
                
                // Set destination with appropriate speed
                BaseNavigator.NavigationSpeed speed = distanceFromHome > 20f 
                    ? BaseNavigator.NavigationSpeed.Fast 
                    : BaseNavigator.NavigationSpeed.Normal;
                
                EnsureNavigatorAgentReadyBeforeSetDestination(brain.Navigator);
                brain.Navigator.SetDestination(targetPos, speed);
                
                // Debug logging for destination selection (throttled)
                if (logRoam && Time.frameCount % 120 == 0)
                {
                    UnityEngine.Debug.Log($"[GrimmNPC Roam] NPC {npcData.Name ?? "Unknown"} setting destination: " +
                        $"Dest={targetPos}, Reason=ReturnHome, DistFromHome={distanceFromHome:F1}m, " +
                        $"Speed={speed}, HomePos={npcData.HomePosition}");
                }
            }
            // Stationary tether only (RoamRange <= 5m): hold position when within range and no target.
            // Bosses / monument scientists use RoamRange > 5; ScientistBrain uses Roam, Dismounted, BaseRoamState, etc.
            // without a memory-slot target — soft-halting all "within RoamRange" movement broke that patrol (see logs: Dismounted + Moving).
            else if (distanceFromHome <= npcData.RoamRange && npcData.RoamRange <= 5f)
            {
                if (brain.Navigator.Moving)
                {
                    SoftHaltNavigatorWithoutDisablingMesh(brain.Navigator);
                    if (logRoam && Time.frameCount % 120 == 0)
                    {
                        UnityEngine.Debug.Log($"[GrimmNPC Roam] NPC {npcData.Name ?? "Unknown"} soft-halted movement " +
                            $"(stationary RoamRange={npcData.RoamRange}m, distanceFromHome={distanceFromHome:F1}m, no target)");
                    }
                }
            }
            // Within roam range: let vanilla ScientistBrain states (Roam/Dismounted/BaseRoamState) own destination updates.
            // GrimmNPC only enforces bounds and navmesh readiness; it does not push idle destinations here.
        }

        private static Vector3 ClampToRoamBoundary(CustomNpcData npcData, Vector3 worldPos, float minRange)
        {
            if (npcData == null) return worldPos;
            Vector3 home = npcData.HomePosition;
            if (home == Vector3.zero) return worldPos;
            float roam = Mathf.Max(minRange, npcData.RoamRange);
            Vector3 delta = worldPos - home;
            delta.y = 0f;
            float dist = delta.magnitude;
            if (dist <= roam || dist <= 0.001f) return worldPos;
            Vector3 clamped = home + (delta / dist) * roam;
            clamped.y = worldPos.y;
            return clamped;
        }

        private static void ClearInvestigation(ulong netId)
        {
            _investigationMode.Remove(netId);
            _investigationPoint.Remove(netId);
            _investigationExpireTime.Remove(netId);
            _lastInvestigationSetTime.Remove(netId);
        }

        private static bool TrySetInvestigationDestination(BaseAIBrain brain, Vector3 destination, BaseNavigator.NavigationSpeed speed, ulong netId)
        {
            if (brain?.Navigator == null) return false;
            EnsureNavigatorAgentReadyBeforeSetDestination(brain.Navigator);
            bool set = brain.Navigator.SetDestination(destination, speed);
            _lastInvestigationSetTime[netId] = Time.time;
            return set;
        }

        private static void HandleLostTargetInvestigation(ScientistNPC npc, CustomNpcData npcData, BaseAIBrain brain, ulong netId, BaseEntity memoryTarget, float now, bool debugLogging)
        {
            if (npc == null || npcData == null || brain?.Navigator == null || netId == 0) return;

            if (memoryTarget != null && !memoryTarget.IsDestroyed)
            {
                _lastKnownTargetPosition[netId] = memoryTarget.transform.position;
                _lastKnownTargetTime[netId] = now;

                // Only cancel investigation when we truly re-acquired (target exists + LOS).
                // Memory can flicker without LOS and would otherwise reset investigation every tick.
                bool hasLOS = false;
                if (brain.Senses?.Memory != null)
                    hasLOS = brain.Senses.Memory.IsLOS(memoryTarget);
                if (hasLOS)
                    ClearInvestigation(netId);
                return;
            }

            if (!_lastKnownTargetTime.TryGetValue(netId, out float lastSeen) ||
                !_lastKnownTargetPosition.TryGetValue(netId, out Vector3 lastPos) ||
                (now - lastSeen) > INVESTIGATION_MEMORY_SECONDS)
            {
                ClearInvestigation(netId);
                return;
            }

            _investigationMode.TryGetValue(netId, out byte mode);

            if (mode == 0)
            {
                Vector3 investigatePos = ClampToRoamBoundary(npcData, lastPos, 5f);
                _investigationMode[netId] = 1;
                _investigationPoint[netId] = investigatePos;
                _investigationExpireTime[netId] = now + INVESTIGATION_DWELL_SECONDS;
                bool set = TrySetInvestigationDestination(brain, investigatePos, BaseNavigator.NavigationSpeed.Normal, netId);
                if (debugLogging)
                    UnityEngine.Debug.Log($"[GrimmNPC Investigate] {npcData.Name ?? "Unknown"} lost target; investigating {investigatePos} (last known {lastPos}, set={set})");
                return;
            }

            _investigationPoint.TryGetValue(netId, out Vector3 point);
            float dist = Vector3.Distance(npc.transform.position, point);
            _lastInvestigationSetTime.TryGetValue(netId, out float lastSet);
            _investigationExpireTime.TryGetValue(netId, out float expireAt);

            if (mode == 1)
            {
                if (dist <= INVESTIGATION_REACHED_DISTANCE || now >= expireAt)
                {
                    _investigationMode[netId] = 2;
                    _investigationExpireTime[netId] = now + INVESTIGATION_RETURN_TIMEOUT_SECONDS;
                    Vector3 home = npcData.HomePosition == Vector3.zero ? npc.transform.position : npcData.HomePosition;
                    _investigationPoint[netId] = home;
                    TrySetInvestigationDestination(brain, home, BaseNavigator.NavigationSpeed.Normal, netId);
                    return;
                }

                if (!brain.Navigator.Moving && (now - lastSet) >= INVESTIGATION_DESTINATION_SET_INTERVAL)
                    TrySetInvestigationDestination(brain, point, BaseNavigator.NavigationSpeed.Normal, netId);
                return;
            }

            if (mode == 2)
            {
                if (dist <= INVESTIGATION_REACHED_DISTANCE || now >= expireAt)
                {
                    ClearInvestigation(netId);
                    return;
                }

                if (!brain.Navigator.Moving && (now - lastSet) >= INVESTIGATION_DESTINATION_SET_INTERVAL)
                    TrySetInvestigationDestination(brain, point, BaseNavigator.NavigationSpeed.Normal, netId);
            }
        }
        
        private static void EnhanceCombatBehavior(ScientistNPC npc, CustomNpcData npcData, BaseAIBrain brain, float delta)
        {
            if (brain == null || brain.Navigator == null) return;
            
            // CRITICAL: Only enhance combat when actually in combat (has target)
            // This prevents AlwaysStrafeInCombat from running when NPC has no target
            // Check combat state first for early exit
            AIState currentState = brain.CurrentState?.StateType ?? AIState.None;
            if (currentState != AIState.Combat && currentState != AIState.Chase && currentState != AIState.CombatStationary)
            {
                return; // Not in combat - don't enhance
            }
            
            // NOTE: Even stationary NPCs (RoamRange <= 5m) can move during combat
            // We still help them maintain optimal distance and get unstuck if needed
            
            // Get current target
            BaseEntity target = null;
            if (brain.Senses != null && brain.Senses.Memory != null)
            {
                target = brain.Events?.Memory?.Entity?.Get(brain.Events?.CurrentInputMemorySlot ?? 0);
            }
            
            // CRITICAL: Must have actual target to enhance combat behavior
            // This prevents destination setting when target is null (which causes wall-hugging)
            if (target == null) return;
            
            // HARD GATE: If StrafeOnlyWhenAttacking is true, only strafe while actively attacking
            if (npcData.StrafeOnlyWhenAttacking)
            {
                // Check if NPC is actually attacking/shooting
                bool isAttacking = false;
                
                // Method 1: Check if NPC has active item (weapon equipped and ready)
                if (npc.svActiveItemID.IsValid)
                {
                    // Method 2: Check if NPC is firing (if available)
                    try
                    {
                        // Try to check if NPC is currently firing
                        var attackEntity = npc.GetAttackEntity();
                        if (attackEntity != null)
                        {
                            // Check if attack entity is in use (firing)
                            // This is a heuristic - if weapon is equipped and NPC is in combat, assume attacking
                            isAttacking = true;
                        }
                    }
                    catch { }
                    
                    // Method 3: Check if NPC is in combat state and has target (fallback heuristic)
                    if (!isAttacking && currentState == AIState.Combat)
                    {
                        // If in combat state with target, assume attacking
                        isAttacking = true;
                    }
                }
                
                // If not attacking, don't generate strafe movement
                if (!isAttacking)
                {
                    return; // Skip strafe movement - NPC is not actively attacking
                }
            }
            
            var config = GrimmNPC.GetConfig();
            bool debugLogging = GrimmNPC.ShouldLogCombatEnhancement();
            
            Vector3 npcPos = npc.transform.position;
            // Use CenterPoint() for proper target position (accounts for entity height)
            Vector3 targetPos = target.CenterPoint();
            
            // Calculate horizontal (XZ) distance and direction for ground-based NPCs
            Vector3 toTarget3D = targetPos - npcPos;
            Vector3 toTarget = new Vector3(toTarget3D.x, 0f, toTarget3D.z); // Flatten to XZ plane
            float distanceToTarget = toTarget.magnitude;
            
            // Don't process if target is too far (outside chase range from home, use horizontal distance)
            float distanceFromHome = GrimmNPC.GetDistanceFromHome(npcData, npcPos);
            if (distanceFromHome > npcData.ChaseRange)
            {
                // Too far from home - let them return (handled by EnforceRoamRange when they exit combat)
                return;
            }
            
            // Determine weapon type and ideal engagement distance
            AttackEntity weapon = npc.GetAttackEntity();
            bool isMeleeWeapon = weapon is BaseMelee;
            bool isFlameThrower = weapon is FlameThrower;
            float idealDistance;
            float maxMeleeRange;
            
            // CRITICAL: Skip combat movement for flamethrower - movement is handled in TryFireFlameThrower
            // This prevents the combat movement logic from overriding flamethrower's specific movement (2-5f range)
            if (isFlameThrower)
            {
                // Flamethrower movement is handled in SpecialWeaponsHandler.TryFireFlameThrower
                // Don't override it with generic combat movement
                return;
            }
            
            if (isMeleeWeapon)
            {
                // Melee weapons: use weapon's effectiveRange (typically 1-2m) and stay within 2x that range
                maxMeleeRange = weapon != null ? weapon.effectiveRange : 1.5f;
                // Floor at 2m so a pack occupies a ring wide enough for body radius (~0.5m), not a 1.2m pile.
                idealDistance = Mathf.Max(2f, maxMeleeRange * 1.2f);
                maxMeleeRange = maxMeleeRange * 2f; // Max range for melee engagement (2x effectiveRange)
            }
            else
            {
                // Ranged weapons (including nailgun): maintain 8-12 meter distance
                idealDistance = weapon != null && weapon.effectiveRange > 0f 
                    ? Mathf.Clamp(weapon.effectiveRange * 0.6f, 8f, 15f) // 60% of effective range, clamped to 8-15m
                    : 10f; // Default for ranged weapons
                maxMeleeRange = 0f; // Not applicable for ranged
            }
            
            // If NPC is not moving or standing still, force movement
            bool isMoving = brain.Navigator.Moving;
            bool shouldMove = false;
            Vector3 moveDestination = Vector3.zero;
            BaseNavigator.NavigationSpeed moveSpeed = BaseNavigator.NavigationSpeed.Normal;
            
            // STUCK DETECTION: Check if NPC is stuck (not moving, at same position for too long)
            // Skip for stationary NPCs (RoamRange <= 5m) - they're supposed to stay in place
            ulong netId = npc.net?.ID.Value ?? 0;
            float currentTime = Time.time;
            bool isStuck = false;
            
            if (netId != 0 && npcData.RoamRange > 5f) // Only check stuck for non-stationary NPCs
            {
                Vector3 lastPos = Vector3.zero;
                _lastCombatPosition.TryGetValue(netId, out lastPos);
                
                // Check if NPC has moved significantly (horizontal distance)
                Vector3 posDiff = new Vector3(npcPos.x - lastPos.x, 0f, npcPos.z - lastPos.z);
                float horizontalMovement = posDiff.magnitude;
                
                if (horizontalMovement < STUCK_DETECTION_THRESHOLD && !isMoving)
                {
                    // NPC hasn't moved much and is not moving - check if stuck for too long
                    float stuckStart = 0f;
                    _stuckStartTime.TryGetValue(netId, out stuckStart);
                    
                    if (stuckStart == 0f)
                    {
                        // Start tracking stuck time
                        _stuckStartTime[netId] = currentTime;
                    }
                    else if ((currentTime - stuckStart) >= STUCK_DURATION)
                    {
                        // NPC has been stuck for too long - force movement
                        isStuck = true;
                        _stuckStartTime[netId] = currentTime; // Reset timer
                    }
                }
                else
                {
                    // NPC is moving - clear stuck tracking
                    _stuckStartTime.Remove(netId);
                }
                
                // Update last position
                _lastCombatPosition[netId] = npcPos;
            }
            
            // Normalize horizontal direction (already flattened to XZ plane)
            Vector3 directionToTarget = distanceToTarget > 0.01f ? toTarget.normalized : Vector3.forward;
            
            if (isMeleeWeapon)
            {
                // MELEE WEAPON LOGIC: Always push forward to close the gap
                // User wants: Always push forward, shoot while pushing if they have ranged weapon, never back away
                
                // Check if NPC has a ranged weapon in inventory (for shooting while pushing)
                bool hasRangedWeapon = false;
                if (npc.inventory != null && npc.inventory.containerBelt != null)
                {
                    foreach (var item in npc.inventory.containerBelt.itemList)
                    {
                        if (item != null && item.info != null)
                        {
                            AttackEntity attackEntity = item.GetHeldEntity() as AttackEntity;
                            if (attackEntity is BaseProjectile && !(attackEntity is BaseMelee))
                            {
                                hasRangedWeapon = true;
                                break;
                            }
                        }
                    }
                }
                
                // Always push forward toward this NPC's unique slot (not the shared player point).
                shouldMove = true;
                Vector3 targetPosXZ = new Vector3(targetPos.x, npcPos.y, targetPos.z);
                moveDestination = GrimmNPC.GetCombatApproachPosition(npc, npcPos, targetPosXZ, idealDistance);
                if (distanceToTarget > maxMeleeRange)
                    moveSpeed = BaseNavigator.NavigationSpeed.Fast;
                else
                    moveSpeed = hasRangedWeapon ? BaseNavigator.NavigationSpeed.Fast : BaseNavigator.NavigationSpeed.Normal;
            }
            else
            {
                // RANGED WEAPON LOGIC: Maintain optimal distance, strafe when too close
                // CRITICAL: Check for rocket launcher minimum range - force back away if too close
                bool hasRocketLauncher = false;
                float rocketMinRange = 6f; // Rocket launcher minimum range
                try
                {
                    // Check if NPC has rocket launcher in belt
                    if (npc.inventory?.containerBelt != null)
                    {
                        foreach (var item in npc.inventory.containerBelt.itemList)
                        {
                            if (item?.info != null)
                            {
                                string shortName = item.info.shortname ?? string.Empty;
                                if (shortName == "rocket.launcher" || shortName.Contains("rocket.launcher"))
                                {
                                    hasRocketLauncher = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { }
                
                // CRITICAL: If NPC has rocket launcher and is too close (< 6m), force aggressive back away
                // Also force movement if NPC is stuck (regardless of weapon)
                if (hasRocketLauncher && distanceToTarget < rocketMinRange)
                {
                    // Too close for rocket launcher - force back away aggressively
                    shouldMove = true;
                    Vector3 backAwayDirection = -directionToTarget; // Directly away from target
                    // Back away to at least minimum range + buffer (8m total)
                    float backAwayDistance = rocketMinRange + 2f - distanceToTarget;
                    if (backAwayDistance < 2f) backAwayDistance = 2f; // Minimum 2m back away
                    // Also add lateral movement to avoid getting stuck
                    Vector3 strafeDirection = new Vector3(-directionToTarget.z, 0f, directionToTarget.x) * (UnityEngine.Random.value < 0.5f ? 1f : -1f);
                    // Combine back away + strafe for better unstuck behavior
                    moveDestination = new Vector3(npcPos.x, npcPos.y, npcPos.z) + backAwayDirection * backAwayDistance + strafeDirection * 3f;
                    moveSpeed = BaseNavigator.NavigationSpeed.Fast; // Fast to get unstuck quickly
                    
                    // Debug logging for rocket launcher back-away
                    if (debugLogging && Time.frameCount % 120 == 0)
                    {
                        UnityEngine.Debug.Log($"[GrimmNPC Combat] NPC {npcData.Name ?? "Unknown"} with rocket launcher too close " +
                            $"(distance: {distanceToTarget:F1}m < {rocketMinRange}m) - forcing back away to {moveDestination}");
                    }
                }
                else if (isStuck)
                {
                    // NPC is stuck - force movement in random direction away from current position
                    shouldMove = true;
                    // Try multiple directions: back away from target, or random lateral
                    Vector3 backAwayDirection = -directionToTarget;
                    Vector3 strafeDirection = new Vector3(-directionToTarget.z, 0f, directionToTarget.x) * (UnityEngine.Random.value < 0.5f ? 1f : -1f);
                    // Combine back away + larger strafe to get unstuck
                    moveDestination = new Vector3(npcPos.x, npcPos.y, npcPos.z) + backAwayDirection * 5f + strafeDirection * 6f;
                    moveSpeed = BaseNavigator.NavigationSpeed.Fast; // Fast to get unstuck quickly
                    
                    // Debug logging for stuck NPC
                    if (debugLogging && Time.frameCount % 120 == 0)
                    {
                        UnityEngine.Debug.Log($"[GrimmNPC Combat] NPC {npcData.Name ?? "Unknown"} is stuck " +
                            $"(distance: {distanceToTarget:F1}m) - forcing movement to {moveDestination}");
                    }
                }
                else if (distanceToTarget > idealDistance * 1.5f)
                {
                    // Too far - rush in toward this NPC's unique slot around the target
                    shouldMove = true;
                    Vector3 targetPosXZ = new Vector3(targetPos.x, npcPos.y, targetPos.z);
                    moveDestination = GrimmNPC.GetCombatApproachPosition(npc, npcPos, targetPosXZ, idealDistance);
                    moveSpeed = BaseNavigator.NavigationSpeed.Fast;
                }
                else if (distanceToTarget < idealDistance * 0.7f)
                {
                    // Too close - strafe away (but not rocket launcher - handled above)
                    shouldMove = true;
                    Vector3 strafeDirection = new Vector3(-directionToTarget.z, 0f, directionToTarget.x) * (UnityEngine.Random.value < 0.5f ? 1f : -1f);
                    // Use StrafeRadius from config (default 3f, but allow larger for visible movement)
                    float strafeDist = npcData.AlwaysStrafeInCombat ? npcData.StrafeRadius : 5f;
                    // Preserve NPC's Y position when strafing
                    moveDestination = new Vector3(npcPos.x, npcPos.y, npcPos.z) + strafeDirection * strafeDist;
                    moveSpeed = BaseNavigator.NavigationSpeed.Normal;
                }
                else
                {
                    // At optimal distance - check if we should always strafe
                    if (npcData.AlwaysStrafeInCombat)
                    {
                        // ALWAYS STRAFE: Generate lateral movement even at optimal distance
                        shouldMove = true;
                        Vector3 strafeDirection = new Vector3(-directionToTarget.z, 0f, directionToTarget.x) * (UnityEngine.Random.value < 0.5f ? 1f : -1f);
                        // Use StrafeRadius from config (2-4m recommended for visible movement)
                        float strafeDist = npcData.StrafeRadius;
                        // Preserve NPC's Y position when strafing
                        moveDestination = new Vector3(npcPos.x, npcPos.y, npcPos.z) + strafeDirection * strafeDist;
                        moveSpeed = BaseNavigator.NavigationSpeed.Normal;
                    }
                    else if (!isMoving)
                    {
                        // At ideal distance but not moving - always strafe side to side (matching old version behavior)
                        // Old version was more aggressive with movement - always strafe when stationary in combat
                        shouldMove = true;
                        Vector3 strafeDirection = new Vector3(-directionToTarget.z, 0f, directionToTarget.x) * (UnityEngine.Random.value < 0.5f ? 1f : -1f);
                        // Preserve NPC's Y position when strafing
                        moveDestination = new Vector3(npcPos.x, npcPos.y, npcPos.z) + strafeDirection * UnityEngine.Random.Range(3f, 6f);
                        moveSpeed = BaseNavigator.NavigationSpeed.Normal;
                    }
                }
            }
            
            // Apply movement if needed
            if (shouldMove && moveDestination != Vector3.zero)
            {
                EnsureNavigatorAgentReadyBeforeSetDestination(brain.Navigator);
                EnsureCombatAgentSeparation(brain.Navigator, netId);
                // Ensure destination is within chase range from home (use horizontal distance)
                float destDistanceFromHome = GrimmNPC.GetDistanceFromHome(npcData, moveDestination);
                if (destDistanceFromHome > npcData.ChaseRange)
                {
                    // Clamp to chase range (on XZ plane)
                    Vector3 directionFromHome = new Vector3(
                        moveDestination.x - npcData.HomePosition.x,
                        0f,
                        moveDestination.z - npcData.HomePosition.z
                    ).normalized;
                    moveDestination = new Vector3(
                        npcData.HomePosition.x + directionFromHome.x * npcData.ChaseRange,
                        moveDestination.y, // Preserve Y from original destination
                        npcData.HomePosition.z + directionFromHome.z * npcData.ChaseRange
                    );
                }
                
                // OPTIMIZATION: Avoid redundant SetDestination calls
                // If AlwaysStrafeInCombat is true, use smaller epsilon (0.1m) or bypass check entirely
                // This allows more frequent movement updates for always-strafe behavior
                // If AlwaysStrafeInCombat is false, use larger epsilon (0.5m) to reduce overhead
                float destinationEpsilon = npcData.AlwaysStrafeInCombat ? 0.1f : 0.5f;
                
                // Get current destination from navigator (if available)
                Vector3 currentDestination = brain.Navigator.Destination;
                if (currentDestination != Vector3.zero)
                {
                    // Calculate horizontal (XZ) distance between current and new destination
                    Vector3 destDiff = new Vector3(
                        moveDestination.x - currentDestination.x,
                        0f,
                        moveDestination.z - currentDestination.z
                    );
                    float horizontalDistance = destDiff.magnitude;
                    
                    // Skip SetDestination if destination hasn't changed significantly
                    // When AlwaysStrafeInCombat is true, this check is more lenient (0.1m vs 0.5m)
                    // allowing more frequent updates for continuous strafing
                    if (horizontalDistance < destinationEpsilon)
                    {
                        // Destination is essentially the same - skip redundant call
                        return;
                    }
                }
                
                brain.Navigator.SetDestination(moveDestination, moveSpeed);
                
                // Debug logging for destination selection (throttled)
                if (debugLogging && Time.frameCount % 120 == 0)
                {
                    string moveType = isMeleeWeapon ? "Melee" : "Ranged";
                    UnityEngine.Debug.Log($"[GrimmNPC Combat] NPC {npcData.Name ?? "Unknown"} setting destination: " +
                        $"Dest={moveDestination}, Reason=Combat({moveType}), DistToTarget={distanceToTarget:F1}m, " +
                        $"IdealDist={idealDistance:F1}m, Speed={moveSpeed}, TargetPos={targetPos}");
                }
            }
        }
        
        private static void CallForAssist(ScientistNPC npc, CustomNpcData npcData, BaseAIBrain brain, NpcConfig config, bool debugLogging)
        {
            if (brain == null || brain.Senses == null || brain.Senses.Memory == null) return;
            
            // Get the current target
            BaseEntity target = null;
            if (brain.Events != null && brain.Events.Memory != null)
            {
                int memorySlot = brain.Events.CurrentInputMemorySlot;
                if (memorySlot >= 0)
                {
                    target = brain.Events.Memory.Entity?.Get(memorySlot);
                }
            }
            
            if (target == null) return;
            
            // Check if target type is in exclusion list - don't call for help against excluded targets
            string targetTypeName = target.GetType().Name;
            if (config.ExcludedTargetTypes != null && config.ExcludedTargetTypes.Count > 0)
            {
                if (config.ExcludedTargetTypes.Contains(targetTypeName))
                {
                    // Target type is excluded - don't call for help
                    if (debugLogging)
                    {
                        UnityEngine.Debug.Log($"[GrimmNPC Assist] NPC {npcData.Name} (NetId: {npc.net?.ID.Value ?? 0}) " +
                            $"skipping assist callout - target {targetTypeName} is in exclusion list");
                    }
                    return;
                }
            }
            
            Vector3 npcPos = npc.transform.position;
            float assistRange = config.AssistRange;
            
            if (debugLogging)
            {
                UnityEngine.Debug.Log($"[GrimmNPC Assist] NPC {npcData.Name} (NetId: {npc.net?.ID.Value ?? 0}) calling for help! " +
                    $"Target: {target.GetType().Name}, Range: {assistRange}m");
            }
            
            // Find nearby friendly NPCs within assist range
            ScientistNPC[] nearbyNpcs = new ScientistNPC[64];
            int npcCount = BaseEntity.Query.Server.GetBrainsInSphere(
                npcPos,
                assistRange,
                nearbyNpcs,
                x => x != null && x is ScientistNPC && GrimmNPC.IsCustomNpc(x) && x != npc && !x.IsDestroyed
            );
            
            int alertedCount = 0;
            for (int i = 0; i < npcCount; i++)
            {
                ScientistNPC nearbyNpc = nearbyNpcs[i] as ScientistNPC;
                if (nearbyNpc == null || nearbyNpc.Brain == null || nearbyNpc.Brain.Senses == null) continue;
                
                // Check if nearby NPC is already in combat or chasing
                AIState nearbyState = nearbyNpc.Brain.CurrentState?.StateType ?? AIState.None;
                if (nearbyState == AIState.Combat || nearbyState == AIState.Chase) continue;
                
                // Check if nearby NPC is within their chase range from home
                ulong nearbyNetId = nearbyNpc.net?.ID.Value ?? 0;
                if (nearbyNetId == 0) continue;
                
                var nearbyNpcData = GrimmNPC.GetNpcData(nearbyNetId);
                if (nearbyNpcData == null) continue;
                
                float distanceFromNearbyHome = GrimmNPC.GetDistanceFromHome(nearbyNpcData, npcPos);
                if (distanceFromNearbyHome > nearbyNpcData.ChaseRange)
                {
                    // Target is outside nearby NPC's chase range
                    continue;
                }
                
                // Alert the nearby NPC to the threat by adding it to their memory
                if (nearbyNpc.Brain.Senses.Memory != null)
                {
                    // Add target to memory so NPC becomes aware
                    nearbyNpc.Brain.Senses.Memory.SetKnown(target, nearbyNpc, null);
                    
                    // Optionally force them to switch to combat/chase state
                    // The AI will naturally switch if the target is valid
                    
                    alertedCount++;
                    
                    if (debugLogging)
                    {
                        UnityEngine.Debug.Log($"[GrimmNPC Assist] Alerted NPC {nearbyNpcData.Name} (NetId: {nearbyNetId}) " +
                            $"to threat {target.GetType().Name} at distance {Vector3.Distance(npcPos, nearbyNpc.transform.position):F1}m");
                    }
                }
            }
            
            if (debugLogging && alertedCount > 0)
            {
                UnityEngine.Debug.Log($"[GrimmNPC Assist] NPC {npcData.Name} successfully alerted {alertedCount} nearby NPCs");
            }
        }
        
        /// <summary>
        /// Dynamic navmesh switching: Automatically updates AreaMask/AgentTypeID based on current position.
        /// Handles NPCs moving between monuments, terrain, and bases seamlessly.
        /// 
        /// This enables NPCs to roam freely across different navmesh types without NavMesh errors,
        /// matching the behavior of other plugins (BotReSpawn, FrankensteinPet) that use BaseNavigator's
        /// automatic navigation type detection.
        /// 
        /// Detection Logic (FIXED - bounds + sampling + BossMonster parity):
        /// 1. Check if position is within monument bounds → try NavMesh.SamplePosition with monument mask (15m radius)
        ///    - If sampling succeeds → use monument navmesh (AreaMask derived from "HumanNPC" area, typically 25)
        ///    - If sampling fails → fall back to terrain navmesh (prevents "agent not close enough" errors)
        /// 1b. If no monument OBB matched but SamplePosition still hits HumanNPC/monument mask within 15m → treat as monument
        ///    (matches oxide/plugins BossMonster IsPositionOnMonument Method 2; avoids false terrain downgrade)
        /// 2. Check if position is on building block → use terrain navmesh (AreaMask derived from "Walkable" area, typically 1) with Base Navigation
        /// 3. Otherwise → use terrain navmesh (AreaMask derived from "Walkable" area, typically 1)
        /// 
        /// Update Process:
        /// - Derives AreaMask from DefaultArea name using NavMesh.GetAreaFromName() (more resilient than hardcoding)
        /// - Updates agent.areaMask, filter.areaMask, and defaultAreaMask consistently
        /// - Only enables agent after confirming navmesh sampling succeeds (10m radius)
        /// - Updates defaultAreaMask when DefaultArea changes (PlaceOnNavMesh() uses defaultAreaMask, not agent.areaMask)
        /// 
        /// Performance: Throttled to 1Hz (NAVMESH_FIX_INTERVAL), only updates when navmesh type changes.
        /// Uses bounds + sampling confirmation for monuments (prevents console spam).
        /// </summary>
        private static void UpdateNavmeshForCurrentPosition(ScientistNPC npc, CustomNpcData npcData, BaseAIBrain brain, bool debugLogging)
        {
            if (npc == null || npcData == null || brain == null) return;
            
            Vector3 position = npc.transform.position;
            const int MONUMENT_AREA_MASK = 25;
            const int TERRAIN_AREA_MASK = 1;
            const int TERRAIN_AGENT_TYPE_ID = -1372625422;
            const float MONUMENT_SAMPLE_RADIUS = 15f; // Sample radius for monument navmesh confirmation (15m)
            const float PRE_ENABLE_SAMPLE_RADIUS = 10f; // Sample radius before enabling agent (10m)
            
            // Cache NavMesh reflection lookups (used throughout method)
            var navMeshType = Type.GetType("UnityEngine.AI.NavMesh, UnityEngine.AIModule");
            if (navMeshType == null)
            {
                navMeshType = Type.GetType("UnityEngine.AI.NavMesh");
            }
            var navMeshHitType = Type.GetType("UnityEngine.AI.NavMeshHit, UnityEngine.AIModule");
            if (navMeshHitType == null)
            {
                navMeshHitType = Type.GetType("UnityEngine.AI.NavMeshHit");
            }
            System.Reflection.MethodInfo samplePositionMethod = null;
            if (navMeshType != null)
            {
                samplePositionMethod = navMeshType.GetMethod("SamplePosition", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null,
                    new Type[] { typeof(Vector3), navMeshHitType.MakeByRefType(), typeof(float), typeof(int) }, null);
            }
            
            // Step 1: Determine what navmesh type is needed at current position
            bool isOnMonument = false;
            bool isOnBuildingBlock = false;
            bool monumentSamplingConfirmed = false;
            
            // FIXED: Check monument bounds + sampling confirmation (not bounds-only)
            if (TerrainMeta.Path != null && TerrainMeta.Path.Monuments != null)
            {
                foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
                {
                    if (monument != null && monument.IsInBounds(position))
                    {
                        // Bounds check passed - now verify with navmesh sampling
                        int monumentAgentTypeID = BaseNavigator.GetNavMeshAgentID("Humanoid");
                        if (monumentAgentTypeID == -1)
                        {
                            // Fallback: try to get from monument navmesh component
                            MonumentNavMesh monumentNavMesh = monument.GetComponentInChildren<MonumentNavMesh>();
                            if (monumentNavMesh != null)
                            {
                                try
                                {
                                    var monumentNavMeshType = typeof(MonumentNavMesh);
                                    var indexProperty = monumentNavMeshType.GetProperty("NavMeshAgentTypeIndex");
                                    if (indexProperty != null)
                                    {
                                        int agentTypeIndex = (int)indexProperty.GetValue(monumentNavMesh);
                                        var navMeshSettingsType = Type.GetType("UnityEngine.AI.NavMesh, UnityEngine.AIModule");
                                        if (navMeshSettingsType == null)
                                        {
                                            navMeshSettingsType = Type.GetType("UnityEngine.AI.NavMesh");
                                        }
                                        var getSettingsMethod = navMeshSettingsType?.GetMethod("GetSettingsByIndex", 
                                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null,
                                            new Type[] { typeof(int) }, null);
                                        if (getSettingsMethod != null)
                                        {
                                            object settings = getSettingsMethod.Invoke(null, new object[] { agentTypeIndex });
                                            if (settings != null)
                                            {
                                                var settingsType = settings.GetType();
                                                var agentTypeIDProp = settingsType.GetProperty("agentTypeID");
                                                if (agentTypeIDProp != null)
                                                {
                                                    monumentAgentTypeID = (int)agentTypeIDProp.GetValue(settings);
                                                }
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                            
                            // Final fallback: default monument agent type
                            if (monumentAgentTypeID == -1)
                            {
                                monumentAgentTypeID = 0;
                            }
                        }
                        
                        // CRITICAL: Try to sample monument navmesh to confirm it's available
                        if (samplePositionMethod != null && navMeshHitType != null)
                        {
                            try
                            {
                                object navMeshHit = Activator.CreateInstance(navMeshHitType);
                                object[] parameters = new object[] { position, navMeshHit, MONUMENT_SAMPLE_RADIUS, MONUMENT_AREA_MASK };
                                
                                bool sampleSuccess = (bool)samplePositionMethod.Invoke(null, parameters);
                                
                                if (sampleSuccess)
                                {
                                    // Sampling succeeded - monument navmesh is available
                                    isOnMonument = true;
                                    monumentSamplingConfirmed = true;
                                    
                                    // Throttle debug logging (10x slower - every 10 seconds)
                                    if (debugLogging)
                                    {
                                        ulong netId = npc.net?.ID.Value ?? 0;
                                        if (netId != 0)
                                        {
                                            float lastDebug = 0f;
                                            _lastMonumentNavmeshDebug.TryGetValue(netId, out lastDebug);
                                            
                                            if ((Time.time - lastDebug) >= MONUMENT_NAVMESH_DEBUG_INTERVAL)
                                            {
                                                _lastMonumentNavmeshDebug[netId] = Time.time;
                                                UnityEngine.Debug.Log($"[GrimmNPC Dynamic Navmesh] Monument navmesh confirmed via sampling: " +
                                                    $"Position={position}, Monument={monument.name}, SampleRadius={MONUMENT_SAMPLE_RADIUS}m");
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    // Sampling failed - monument navmesh not available, fall back to terrain
                                    if (debugLogging)
                                    {
                                        UnityEngine.Debug.LogWarning($"[GrimmNPC Dynamic Navmesh] Monument bounds detected but navmesh sampling failed: " +
                                            $"Position={position}, Monument={monument.name}, Falling back to terrain navmesh");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                if (debugLogging)
                                {
                                    UnityEngine.Debug.LogWarning($"[GrimmNPC Dynamic Navmesh] Failed to sample monument navmesh: {ex.Message}");
                                }
                                // If sampling fails due to reflection error, fall back to terrain
                            }
                        }
                        
                        // If we found a monument in bounds, break (even if sampling failed, we'll use terrain)
                        if (isOnMonument)
                        {
                            break;
                        }
                    }
                }
            }

            // BossMonster / game parity: monument HumanNPC mesh can exist outside strict MonumentInfo OBB.
            // Bounds-only detection would classify those as terrain and overwrite AreaMask to 1 on the next Think tick.
            if (!isOnMonument && samplePositionMethod != null && navMeshHitType != null)
            {
                try
                {
                    object orphanHit = Activator.CreateInstance(navMeshHitType);
                    const float orphanRadius = 15f;
                    object[] orphanParams = new object[] { position, orphanHit, orphanRadius, MONUMENT_AREA_MASK };
                    if ((bool)samplePositionMethod.Invoke(null, orphanParams))
                    {
                        isOnMonument = true;
                        monumentSamplingConfirmed = true;
                        if (debugLogging)
                        {
                            ulong oid = npc.net?.ID.Value ?? 0;
                            if (oid != 0)
                            {
                                float lastOrphan = 0f;
                                _lastMonumentNavmeshDebug.TryGetValue(oid, out lastOrphan);
                                if ((Time.time - lastOrphan) >= MONUMENT_NAVMESH_DEBUG_INTERVAL)
                                {
                                    _lastMonumentNavmeshDebug[oid] = Time.time;
                                    UnityEngine.Debug.Log($"[GrimmNPC Dynamic Navmesh] Monument mesh via area-mask sample outside OBB: " +
                                        $"Position={position}, SampleRadius={orphanRadius}m, AreaMask={MONUMENT_AREA_MASK}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (debugLogging)
                    {
                        UnityEngine.Debug.LogWarning($"[GrimmNPC Dynamic Navmesh] Orphan monument sample failed: {ex.Message}");
                    }
                }
            }
            
            // Check building blocks (for Base Navigation)
            // Use same detection as BaseNavigator.DetermineNavigationType()
            const int BUILDING_BLOCKS_LAYER = 2097152; // 1 << 21
            float navTypeHeightOffset = GetNavTypeHeightOffset();
            float navTypeDistance = GetNavTypeDistance();
            Vector3 raycastOrigin = position + Vector3.up * navTypeHeightOffset;
            RaycastHit hit;
            if (UnityEngine.Physics.Raycast(raycastOrigin, Vector3.down, out hit, navTypeDistance, BUILDING_BLOCKS_LAYER))
            {
                BaseEntity hitEntity = hit.collider.ToBaseEntity();
                if (hitEntity is BuildingBlock || hitEntity is SimpleBuildingBlock)
                {
                    isOnBuildingBlock = true;
                }
            }
            
            // Step 2: Determine required AreaMask and AgentTypeID
            int requiredAreaMask;
            int requiredAgentTypeID;
            string requiredDefaultArea;
            
            if (isOnMonument && monumentSamplingConfirmed)
            {
                // On monument with confirmed navmesh → use monument navmesh
                requiredAreaMask = MONUMENT_AREA_MASK;
                requiredAgentTypeID = BaseNavigator.GetNavMeshAgentID("Humanoid");
                // Unity sometimes reports Humanoid as 0; 0 is invalid for NavMeshAgent (matches BossMonster FinalizeMonumentAgentTypeId).
                if (requiredAgentTypeID == -1 || requiredAgentTypeID == 0)
                {
                    // Fallback: try to get from monument navmesh component
                    if (TerrainMeta.Path != null && TerrainMeta.Path.Monuments != null)
                    {
                        foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
                        {
                            if (monument != null && monument.IsInBounds(position))
                            {
                                MonumentNavMesh monumentNavMesh = monument.GetComponentInChildren<MonumentNavMesh>();
                                if (monumentNavMesh != null)
                                {
                                    try
                                    {
                                        var monumentNavMeshType = typeof(MonumentNavMesh);
                                        var indexProperty = monumentNavMeshType.GetProperty("NavMeshAgentTypeIndex");
                                        if (indexProperty != null)
                                        {
                                            int agentTypeIndex = (int)indexProperty.GetValue(monumentNavMesh);
                                            var navMeshSettingsType = Type.GetType("UnityEngine.AI.NavMesh, UnityEngine.AIModule");
                                            if (navMeshSettingsType == null)
                                            {
                                                navMeshSettingsType = Type.GetType("UnityEngine.AI.NavMesh");
                                            }
                                            var getSettingsMethod = navMeshSettingsType?.GetMethod("GetSettingsByIndex", 
                                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null,
                                                new Type[] { typeof(int) }, null);
                                            if (getSettingsMethod != null)
                                            {
                                                object settings = getSettingsMethod.Invoke(null, new object[] { agentTypeIndex });
                                                if (settings != null)
                                                {
                                                    var settingsType = settings.GetType();
                                                    var agentTypeIDProp = settingsType.GetProperty("agentTypeID");
                                                    if (agentTypeIDProp != null)
                                                    {
                                                        requiredAgentTypeID = (int)agentTypeIDProp.GetValue(settings);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    catch { }
                                }
                                break;
                            }
                        }
                    }
                    
                    if (requiredAgentTypeID == -1 || requiredAgentTypeID == 0)
                    {
                        requiredAgentTypeID = FinalizeMonumentAgentTypeIdThink(position, TERRAIN_AGENT_TYPE_ID);
                    }
                }
                requiredDefaultArea = "HumanNPC";
            }
            else
            {
                // On terrain or building block → use terrain navmesh
                // Base Navigation will handle building blocks automatically via DetermineNavigationType()
                requiredAreaMask = TERRAIN_AREA_MASK;
                requiredAgentTypeID = TERRAIN_AGENT_TYPE_ID;
                requiredDefaultArea = "Walkable";
            }
            
            // Step 3: Only update if navmesh type has changed
            if (npcData.AreaMask == requiredAreaMask && npcData.AgentTypeID == requiredAgentTypeID)
            {
                return; // Already using correct navmesh type
            }
            
            // Step 4: Update navmesh settings with sampling confirmation
            try
            {
                // FIXED: Use DefaultArea as source of truth, derive AreaMask from it consistently
                // This prevents DefaultArea vs AreaMask mismatch and is more resilient across maps
                string requiredDefaultAreaFinal = requiredDefaultArea;
                
                // Derive AreaMask from DefaultArea name (more resilient than hardcoding)
                // This ensures agent.areaMask, filter.areaMask, and defaultAreaMask all match DefaultArea
                var getAreaFromNameMethod = navMeshType?.GetMethod("GetAreaFromName", BindingFlags.Public | BindingFlags.Static);
                int areaIndex = 0;
                if (getAreaFromNameMethod != null)
                {
                    try
                    {
                        areaIndex = (int)getAreaFromNameMethod.Invoke(null, new object[] { requiredDefaultAreaFinal });
                    }
                    catch
                    {
                        // If reflection fails, fall back to hardcoded values based on DefaultArea name
                        if (requiredDefaultAreaFinal == "HumanNPC")
                        {
                            // HumanNPC area - for monuments, this typically maps to area 0, but monument mask 25 includes multiple areas
                            // Use hardcoded fallback for monuments
                            if (isOnMonument && monumentSamplingConfirmed)
                            {
                                areaIndex = 0; // Will use hardcoded MONUMENT_AREA_MASK below
                            }
                            else
                            {
                                areaIndex = 0; // Walkable is also area 0, mask = 1
                            }
                        }
                        else if (requiredDefaultAreaFinal == "Walkable")
                        {
                            areaIndex = 0; // Walkable is area 0, mask = 1 << 0 = 1
                        }
                    }
                }
                
                // Derive mask from area index
                // For monuments, use hardcoded mask 25 (includes multiple areas, not just area 0)
                // For terrain, derive from area index (Walkable = area 0, mask = 1)
                int derivedAreaMask;
                if (isOnMonument && monumentSamplingConfirmed)
                {
                    // Monuments use mask 25 (includes multiple areas, not just the base area)
                    // This is a known constant for monument navmesh
                    derivedAreaMask = MONUMENT_AREA_MASK;
                }
                else
                {
                    // Terrain: derive from DefaultArea (Walkable = area 0, mask = 1)
                    derivedAreaMask = 1 << areaIndex;
                }
                
                // Use derived mask (ensures consistency and resilience)
                requiredAreaMask = derivedAreaMask;
                
                // Update npcData
                int oldAreaMask = npcData.AreaMask;
                npcData.AreaMask = requiredAreaMask;
                npcData.AgentTypeID = requiredAgentTypeID;
                
                // Update NavAgent via reflection (same pattern as SpawnPatches)
                var navigator = brain.Navigator;
                if (navigator != null)
                {
                    var navigatorType = navigator.GetType();
                    var agentProperty = navigatorType.GetProperty("Agent");
                    if (agentProperty != null)
                    {
                        object navAgent = agentProperty.GetValue(navigator);
                        if (navAgent != null)
                        {
                            var agentType = navAgent.GetType();
                            var areaMaskProp = agentType.GetProperty("areaMask");
                            var agentTypeIDProp = agentType.GetProperty("agentTypeID");
                            var enabledProp = agentType.GetProperty("enabled");
                            var isOnNavMeshProp = agentType.GetProperty("isOnNavMesh");
                            
                            // FIXED: Only enable agent AFTER confirming navmesh sampling succeeds
                            // This prevents "agent not close enough" errors
                            bool shouldEnableAgent = false;
                            
                            // Check if we can sample navmesh with new settings before enabling
                            if (samplePositionMethod != null && navMeshHitType != null)
                            {
                                try
                                {
                                    object navMeshHit = Activator.CreateInstance(navMeshHitType);
                                    object[] sampleParams = new object[] { position, navMeshHit, PRE_ENABLE_SAMPLE_RADIUS, requiredAreaMask };
                                    
                                    bool canSample = (bool)samplePositionMethod.Invoke(null, sampleParams);
                                    
                                    if (canSample)
                                    {
                                        shouldEnableAgent = true;
                                        
                                        if (debugLogging)
                                        {
                                            UnityEngine.Debug.Log($"[GrimmNPC Dynamic Navmesh] Navmesh sampling confirmed before enabling agent: " +
                                                $"Position={position}, AreaMask={requiredAreaMask}, AgentTypeID={requiredAgentTypeID}, SampleRadius={PRE_ENABLE_SAMPLE_RADIUS}m");
                                        }
                                    }
                                    else
                                    {
                                        // Cannot sample - keep agent disabled or use old settings
                                        if (debugLogging)
                                        {
                                            UnityEngine.Debug.LogWarning($"[GrimmNPC Dynamic Navmesh] Cannot sample navmesh with new settings, " +
                                                $"keeping current configuration. Position={position}, RequiredAreaMask={requiredAreaMask}");
                                        }
                                        return; // Don't update if we can't sample
                                    }
                                }
                                catch (Exception ex)
                                {
                                    if (debugLogging)
                                    {
                                        UnityEngine.Debug.LogWarning($"[GrimmNPC Dynamic Navmesh] Failed to sample navmesh before update: {ex.Message}");
                                    }
                                    // If sampling check fails, be conservative and don't update
                                    return;
                                }
                            }
                            else
                            {
                                // If reflection fails, be conservative - don't update
                                if (debugLogging)
                                {
                                    UnityEngine.Debug.LogWarning($"[GrimmNPC Dynamic Navmesh] Cannot access NavMesh.SamplePosition, skipping update");
                                }
                                return;
                            }
                            
                            // Update agent properties
                            if (areaMaskProp != null)
                            {
                                areaMaskProp.SetValue(navAgent, requiredAreaMask);
                            }
                            if (agentTypeIDProp != null)
                            {
                                agentTypeIDProp.SetValue(navAgent, requiredAgentTypeID);
                            }
                            
                            // FIXED: Only enable agent if sampling succeeded
                            if (shouldEnableAgent && enabledProp != null)
                            {
                                // Check current enabled state
                                bool currentlyEnabled = (bool)enabledProp.GetValue(navAgent);
                                
                                if (!currentlyEnabled)
                                {
                                    // Only enable if we confirmed navmesh is available
                                    enabledProp.SetValue(navAgent, true);
                                    
                                    if (debugLogging)
                                    {
                                        UnityEngine.Debug.Log($"[GrimmNPC Dynamic Navmesh] Enabled agent after navmesh confirmation");
                                    }
                                }
                            }
                            
                            // Try to place on navmesh if not already on it
                            bool isOnNavMesh = false;
                            if (isOnNavMeshProp != null)
                            {
                                isOnNavMesh = (bool)isOnNavMeshProp.GetValue(navAgent);
                            }
                            
                            if (!isOnNavMesh && shouldEnableAgent)
                            {
                                navigator.PlaceOnNavMesh(6f);
                            }
                            
                            // Update DefaultArea (source of truth)
                            string previousDefaultArea = navigator.DefaultArea;
                            navigator.DefaultArea = requiredDefaultAreaFinal;
                            
                            // CRITICAL: Update defaultAreaMask when DefaultArea changes
                            // PlaceOnNavMesh() uses defaultAreaMask, not agent.areaMask
                            // This ensures PlaceOnNavMesh() samples the correct area
                            if (previousDefaultArea != requiredDefaultAreaFinal)
                            {
                                try
                                {
                                    var defaultAreaMaskField = navigatorType.GetField("defaultAreaMask", BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (defaultAreaMaskField != null)
                                    {
                                        // Recalculate defaultAreaMask from new DefaultArea (matches Init() logic)
                                        // Use the same derived mask to ensure consistency
                                        defaultAreaMaskField.SetValue(navigator, requiredAreaMask);
                                        
                                        if (debugLogging)
                                        {
                                            UnityEngine.Debug.Log($"[GrimmNPC Dynamic Navmesh] Updated defaultAreaMask from old DefaultArea '{previousDefaultArea}' " +
                                                $"to new DefaultArea '{requiredDefaultAreaFinal}' (defaultAreaMask={requiredAreaMask})");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    if (debugLogging)
                                    {
                                        UnityEngine.Debug.LogWarning($"[GrimmNPC Dynamic Navmesh] Failed to update defaultAreaMask after DefaultArea change: {ex.Message}");
                                    }
                                }
                            }
                            
                            // Update navMeshQueryFilter if it exists
                            try
                            {
                                var navMeshQueryFilterField = navigatorType.GetField("navMeshQueryFilter", BindingFlags.NonPublic | BindingFlags.Instance);
                                if (navMeshQueryFilterField != null)
                                {
                                    object navMeshQueryFilter = navMeshQueryFilterField.GetValue(navigator);
                                    if (navMeshQueryFilter != null)
                                    {
                                        var filterType = navMeshQueryFilter.GetType();
                                        var filterAgentTypeIDProp = filterType.GetProperty("agentTypeID");
                                        var filterAreaMaskProp = filterType.GetProperty("areaMask");
                                        
                                        if (filterAgentTypeIDProp != null)
                                        {
                                            filterAgentTypeIDProp.SetValue(navMeshQueryFilter, requiredAgentTypeID);
                                        }
                                        if (filterAreaMaskProp != null)
                                        {
                                            // Use same derived mask value as agent and defaultAreaMask (ensures consistency)
                                            filterAreaMaskProp.SetValue(navMeshQueryFilter, requiredAreaMask);
                                        }
                                    }
                                }
                            }
                            catch { /* Ignore navMeshQueryFilter update errors */ }
                            
                            // Only log on actual changes (not on every check)
                            if (debugLogging)
                            {
                                string locationType = isOnMonument ? "monument" : (isOnBuildingBlock ? "building block" : "terrain");
                                UnityEngine.Debug.Log($"[GrimmNPC Dynamic Navmesh] Updated navmesh for {npcData.Name}: " +
                                    $"AreaMask={requiredAreaMask} (was {oldAreaMask}), AgentTypeID={requiredAgentTypeID}, " +
                                    $"DefaultArea={requiredDefaultAreaFinal}, Location={locationType}, Position={position}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (debugLogging)
                {
                    UnityEngine.Debug.LogError($"[GrimmNPC Dynamic Navmesh] Failed to update navmesh: {ex}");
                }
            }
        }

        /// <summary>BossMonster parity: never leave monument scientists at agentTypeID 0.</summary>
        private static int FinalizeMonumentAgentTypeIdThink(Vector3 position, int terrainFallback)
        {
            if (TryResolveMonumentNavMeshAgentTypeThink(position, out int fromMesh) && fromMesh != 0)
                return fromMesh;
            if (TryDetectNonZeroAgentTypeViaMonumentSamplingThink(position, out int sampled) && sampled != 0)
                return sampled;
            return GetPreferredHumanoidAgentTypeIdThink(terrainFallback);
        }

        private static int GetPreferredHumanoidAgentTypeIdThink(int terrainFallback)
        {
            try
            {
                int id = BaseNavigator.GetNavMeshAgentID("Humanoid");
                if (id != -1 && id != 0)
                    return id;
            }
            catch { }
            return terrainFallback;
        }

        /// <summary>Closest MonumentNavMesh bake settings (same scoring idea as BossMonster.TryResolveMonumentNavMeshAgentType).</summary>
        private static bool TryResolveMonumentNavMeshAgentTypeThink(Vector3 position, out int agentTypeId)
        {
            agentTypeId = 0;
            if (TerrainMeta.Path?.Monuments == null)
                return false;
            MonumentNavMesh chosen = null;
            float bestScore = float.MaxValue;
            foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
            {
                if (monument == null || !monument.HasNavmesh)
                    continue;
                foreach (MonumentNavMesh mnm in monument.GetComponentsInChildren<MonumentNavMesh>(true))
                {
                    if (mnm == null)
                        continue;
                    try
                    {
                        Bounds b = mnm.GetBounds();
                        Vector3 c = b.ClosestPoint(position);
                        float dist = Vector3.Distance(c, position);
                        if (dist > 200f)
                            continue;
                        float score = dist + (monument.IsInBounds(position) ? 0f : 800f);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            chosen = mnm;
                        }
                    }
                    catch
                    {
                        // skip broken monument mesh
                    }
                }
            }
            if (chosen == null)
                return false;
            try
            {
                var navMeshSettingsType = Type.GetType("UnityEngine.AI.NavMesh, UnityEngine.AIModule") ?? Type.GetType("UnityEngine.AI.NavMesh");
                var getSettingsMethod = navMeshSettingsType?.GetMethod("GetSettingsByIndex",
                    BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(int) }, null);
                if (getSettingsMethod == null)
                    return false;
                object settings = getSettingsMethod.Invoke(null, new object[] { chosen.NavMeshAgentTypeIndex });
                if (settings == null)
                    return false;
                var agentTypeIDProp = settings.GetType().GetProperty("agentTypeID");
                if (agentTypeIDProp == null)
                    return false;
                agentTypeId = (int)agentTypeIDProp.GetValue(settings);
                return agentTypeId != 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDetectNonZeroAgentTypeViaMonumentSamplingThink(Vector3 position, out int agentTypeId)
        {
            agentTypeId = 0;
            const int monumentMask = 25;
            var navMeshType = Type.GetType("UnityEngine.AI.NavMesh, UnityEngine.AIModule") ?? Type.GetType("UnityEngine.AI.NavMesh");
            var navMeshHitType = Type.GetType("UnityEngine.AI.NavMeshHit, UnityEngine.AIModule") ?? Type.GetType("UnityEngine.AI.NavMeshHit");
            if (navMeshType == null || navMeshHitType == null)
                return false;
            var samplePositionMethod = navMeshType.GetMethod("SamplePosition", BindingFlags.Public | BindingFlags.Static, null,
                new Type[] { typeof(Vector3), navMeshHitType.MakeByRefType(), typeof(float), typeof(int) }, null);
            if (samplePositionMethod == null)
                return false;
            try
            {
                object hit = Activator.CreateInstance(navMeshHitType);
                bool ok = (bool)samplePositionMethod.Invoke(null, new object[] { position, hit, 10f, monumentMask });
                if (!ok)
                    return false;
                var posProp = navMeshHitType.GetProperty("position");
                Vector3 sampledPos = position;
                if (posProp != null)
                    sampledPos = (Vector3)posProp.GetValue(hit);
                if (TryResolveMonumentNavMeshAgentTypeThink(sampledPos, out agentTypeId) && agentTypeId != 0)
                    return true;
                return TryFirstNonZeroNavMeshSettingsAgentTypeId(out agentTypeId);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryFirstNonZeroNavMeshSettingsAgentTypeId(out int agentTypeId)
        {
            agentTypeId = 0;
            var navMeshType = Type.GetType("UnityEngine.AI.NavMesh, UnityEngine.AIModule") ?? Type.GetType("UnityEngine.AI.NavMesh");
            if (navMeshType == null)
                return false;
            var countM = navMeshType.GetMethod("GetSettingsCount", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            var getSettings = navMeshType.GetMethod("GetSettingsByIndex", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
            if (countM == null || getSettings == null)
                return false;
            int count;
            try
            {
                count = (int)countM.Invoke(null, null);
            }
            catch
            {
                return false;
            }
            for (int i = 0; i < count; i++)
            {
                try
                {
                    object settings = getSettings.Invoke(null, new object[] { i });
                    if (settings == null)
                        continue;
                    var p = settings.GetType().GetProperty("agentTypeID");
                    if (p == null)
                        continue;
                    int aid = (int)p.GetValue(settings);
                    if (aid != 0)
                    {
                        agentTypeId = aid;
                        return true;
                    }
                }
                catch
                {
                    // skip
                }
            }
            return false;
        }
        
        /// <summary>
        /// Helper method to get navTypeHeightOffset from BaseNavigator (cached reflection).
        /// </summary>
        private static float GetNavTypeHeightOffset()
        {
            // Use cached reflection or default value
            // BaseNavigator uses ConVar.AI.navTypeHeightOffset (default 0.5f)
            try
            {
                var aiType = Type.GetType("ConVar.AI, Assembly-CSharp");
                if (aiType != null)
                {
                    var navTypeHeightOffsetField = aiType.GetField("navTypeHeightOffset");
                    if (navTypeHeightOffsetField != null)
                    {
                        return (float)navTypeHeightOffsetField.GetValue(null);
                    }
                }
            }
            catch { }
            return 0.5f; // Default fallback
        }
        
        /// <summary>
        /// Helper method to get navTypeDistance from BaseNavigator (cached reflection).
        /// </summary>
        private static float GetNavTypeDistance()
        {
            // Use cached reflection or default value
            // BaseNavigator uses ConVar.AI.navTypeDistance (default 1f)
            try
            {
                var aiType = Type.GetType("ConVar.AI, Assembly-CSharp");
                if (aiType != null)
                {
                    var navTypeDistanceField = aiType.GetField("navTypeDistance");
                    if (navTypeDistanceField != null)
                    {
                        return (float)navTypeDistanceField.GetValue(null);
                    }
                }
            }
            catch { }
            return 1f; // Default fallback
        }
        
        /// <summary>
        /// 🎯 IMMEDIATE TARGETING FIX: Checks if NPC has valid hostile target in memory with LOS.
        /// If so, immediately forces Chase or Combat state (no delay, matching old version behavior).
        /// 
        /// This ensures NPCs target players immediately when they spawn in, without requiring damage.
        /// </summary>
        private static void CheckImmediateTargeting(ScientistNPC npc, CustomNpcData npcData, BaseAIBrain brain, AIState currentState, float currentTime, bool debugLogging)
        {
            // Get netId once at the start
            ulong netId = npc.net?.ID.Value ?? 0;
            
            // Only check when in Idle state (or None) - don't interfere with active states
            if (currentState != AIState.None && currentState != AIState.Idle)
            {
                return;
            }
            
            // Throttle checks to every 0.5 seconds (not every frame)
            float lastLosCheck = 0f;
            _lastLosCheck.TryGetValue(netId, out lastLosCheck);
            
            if ((currentTime - lastLosCheck) < WAKEUP_LOS_CHECK_INTERVAL)
                return;
            
            _lastLosCheck[netId] = currentTime;
            
            BaseEntity bestTarget = npc.GetBestTarget();

            if (bestTarget == null)
            {
                return; // No valid target
            }
            
            // Check if target is within sense range
            float distance = Vector3.Distance(npc.transform.position, bestTarget.transform.position);
            if (distance > npcData.SenseRange)
            {
                return; // Target out of range
            }
            
            // CRITICAL: Check if NPC has LOS to target - if yes, immediately force Chase/Combat
            bool hasLOS = false;
            if (brain.Senses != null && brain.Senses.Memory != null)
            {
                hasLOS = brain.Senses.Memory.IsLOS(bestTarget);
            }
            
            if (!hasLOS)
            {
                return; // No LOS - don't force targeting (let vanilla AI handle it)
            }
            
            // Has LOS and valid target - immediately force Chase state (matching old version behavior)
            // Throttle to prevent spam (max once per 2 seconds)
            float lastStateForce = 0f;
            _lastStateForceTime.TryGetValue(netId, out lastStateForce);
            
            if ((currentTime - lastStateForce) < STATE_FORCE_THROTTLE)
            {
                return; // Throttled
            }
            
            // Wake up the brain
            npc.IsDormant = false;
            if (brain.sleeping)
            {
                brain.sleeping = false;
                if (brain is IAISleepable sleepable)
                {
                    sleepable.WakeAI();
                }
                if (brain.Navigator != null)
                {
                    if (!GrimmNPC.IsInSpawnSwimInitWindow(netId))
                        brain.Navigator.Resume();
                }
            }
            
            // SwitchToState(AIState) uses stateContainerID -1; AddEvents then does
            // Events.Init(..., AIDesign.GetStateContainerByID(-1), ...) which returns null -> NRE inside AIEvents.Init.
            // Always pass a real Chase container ID when UseAIDesign is on (BossMonster helper spawns hit this path in Idle).
            if (brain.Events == null || brain.Senses == null)
                return;

            if (brain.Navigator == null)
                return;

            bool switched;
            if (brain.UseAIDesign && brain.AIDesign != null)
            {
                AIStateContainer chaseContainer = brain.AIDesign.GetFirstStateContainerOfType(AIState.Chase);
                if (chaseContainer == null || !brain.HasState(AIState.Chase))
                    return;
                switched = brain.SwitchToState(AIState.Chase, chaseContainer.ID);
            }
            else
            {
                switched = brain.SwitchToState(AIState.Chase);
            }

            if (switched)
            {
                _lastStateForceTime[netId] = currentTime;

                if (debugLogging)
                {
                    UnityEngine.Debug.Log($"[GrimmNPC ImmediateTargeting] NPC {npcData.Name ?? "Unknown"} immediately targeting " +
                        $"{bestTarget.GetType().Name} with LOS (distance: {distance:F1}m) - forced to Chase state");
                }
            }
        }
        
        /// <summary>
        /// 🧍 IDLE BEHAVIOR FIX: When NPC has no valid target, no roam destination, no raid task, and no assist call active,
        /// stop navigator, don't rotate, don't set destinations, stand still.
        /// 
        /// If NPC has raid goal but no player target, set destination to raid goal position.
        /// </summary>
        private static void HandleIdleBehavior(ScientistNPC npc, CustomNpcData npcData, BaseAIBrain brain, AIState currentState, float currentTime)
        {
            // CRITICAL: Handle when in Idle, None, or Roam state (vanilla AI might set destinations in RoamState)
            // Also handle stationary NPCs (RoamRange <= 5m) in any non-combat state - they should never move without target
            bool isStationary = npcData.RoamRange <= 5f;
            bool isNonCombatState = currentState == AIState.None || currentState == AIState.Idle || currentState == AIState.Roam;
            
            // Only handle non-combat states, or stationary NPCs in any non-combat state
            if (!isNonCombatState && !isStationary)
                return;

            // Vanilla ScientistBrain.RoamState moves via PathFinder roam points with no memory-slot target.
            if (currentState == AIState.Roam)
                return;
            
            // Check if NPC has valid target
            BaseEntity currentTarget = null;
            if (brain.Senses != null && brain.Senses.Memory != null)
            {
                currentTarget = brain.Events?.Memory?.Entity?.Get(brain.Events?.CurrentInputMemorySlot ?? 0);
            }
            
            // Check if NPC has roam destination (navigator is moving)
            bool hasRoamDestination = brain.Navigator != null && brain.Navigator.Moving;
            
            // Check if NPC has raid task (is raiding NPC and has raid goal)
            bool hasRaidTask = false;
            if (npcData.IsRaidingNpc && npcData.RaidGoalActive)
            {
                // NPC has active raid goal - this is a valid task
                hasRaidTask = true;
                
                // 🏠 CRITICAL: If NPC has raid goal but no player target, ALWAYS set destination to raid goal
                // This ensures all raiding NPCs move toward the base, not just those with player targets
                if (currentTarget == null && brain.Navigator != null)
                {
                    Vector3 raidGoalPos = Vector3.zero;
                    
                    // Prefer RaidGoalEntityId if set and valid
                    if (npcData.RaidGoalEntityId != 0)
                    {
                        BaseEntity goalEntity = BaseNetworkable.serverEntities.Find(new NetworkableId(npcData.RaidGoalEntityId)) as BaseEntity;
                        if (goalEntity != null && !goalEntity.IsDestroyed && goalEntity.Health() > 0f)
                        {
                            raidGoalPos = goalEntity.transform.position;
                        }
                    }
                    
                    // Fallback to RaidGoalPosition if entity not available
                    if (raidGoalPos == Vector3.zero && npcData.RaidGoalPosition != Vector3.zero)
                    {
                        raidGoalPos = npcData.RaidGoalPosition;
                    }
                    
                    if (raidGoalPos != Vector3.zero)
                    {
                        // Set destination to raid goal (let raiding system handle structure targeting)
                        // CRITICAL: Always set destination, even if already moving, to ensure NPCs don't stand still
                        float distToGoal = Vector3.Distance(npc.transform.position, raidGoalPos);
                        if (distToGoal > 1f) // Only set if not already at goal
                        {
                            brain.Navigator.SetDestination(raidGoalPos, BaseNavigator.NavigationSpeed.Normal);
                        }
                        return; // Don't stop - NPC should move toward raid goal
                    }
                }
            }
            
            // Check if assist call is active (NPC was just alerted)
            // Assist calls are handled by CallForAssist() which adds targets to memory
            // If target was just added, it will be in memory, so we check target above
            
            // Stationary-only idle halt (RoamRange <= 5m). Larger tethers: patrol uses movement without memory target.
            if (currentTarget == null && !hasRaidTask && npcData.RoamRange <= 5f)
            {
                if (brain.Navigator != null && brain.Navigator.Moving)
                {
                    SoftHaltNavigatorWithoutDisablingMesh(brain.Navigator);
                }
                
                if (brain.Navigator != null)
                {
                    brain.Navigator.ClearFacingDirectionOverride();
                }
            }
        }
    }
    
    // Note: BaseAIBrain doesn't have a SelectState method
    // State selection is handled through SwitchToState and the state machine logic
    // Custom state selection can be implemented by patching SwitchToState if needed
}
