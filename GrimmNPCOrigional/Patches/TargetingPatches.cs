using HarmonyLib;
using Rust;
using Rust.Ai;
using System;
using UnityEngine;
using GrimmNPC;

namespace GrimmNPC.Patches
{
    /// <summary>
    /// Custom target selection logic for GrimmNPC NPCs.
    /// 
    /// Patches HumanNPC.GetBestTarget() (ScientistNPC inherits from HumanNPC).
    /// 
    /// Call Frequency:
    /// - During combat: ~10-20 calls/second per NPC
    /// - 1000 NPCs in combat: ~10,000-20,000 calls/second total
    /// 
    /// Targeting Logic:
    /// 1. Iterate over brain memory entities
    /// 2. Filter by CanTargetEntity() checks (respects CanTarget* config flags)
    /// 3. Filter by ChaseRange from HomePosition (uses GrimmNPC.GetDistanceFromHome() helper method)
    /// 4. Score by horizontal distance and LOS
    /// 5. Return best target
    /// 
    /// Key Fix: Uses GrimmNPC.GetDistanceFromHome() helper method (which calculates
    /// horizontal XZ distance manually) for all distance calculations to prevent NPCs
    /// from looking at ceiling when targets are at different Y levels.
    /// 
    /// Performance Optimizations:
    /// - Config caching (re-check every 5 seconds)
    /// - Fast memory iteration (direct access to brain.Senses.Memory.All)
    /// - Early exits for invalid targets
    /// - Debug logging throttled (every 2 seconds)
    /// 
    /// See INSTRUCTIONAL.md "Patch System - TargetingPatches" section for details.
    /// </summary>
    [HarmonyPatch(typeof(HumanNPC), nameof(HumanNPC.GetBestTarget))]
    public class HumanNPC_GetBestTarget_Patch
    {
        static bool Prefix(HumanNPC __instance, ref BaseEntity __result)
        {
            // Only intercept for custom NPCs (ScientistNPC inherits from HumanNPC)
            ScientistNPC npc = __instance as ScientistNPC;
            if (npc == null || !GrimmNPC.IsCustomNpc(npc)) 
                return true; // Continue to original
            
            try
            {
                // Direct targeting logic - no reflection
                __result = GetCustomBestTarget(npc);
                return false; // Skip original method
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[GrimmNPC] Error in GetBestTarget patch: {ex}");
                return true; // Fall back to original
            }
        }
        
        // PERFORMANCE: Cache config to avoid repeated lookups
        private static NpcConfig _cachedTargetingConfig = null;
        private static float _lastTargetingConfigCheck = 0f;
        private const float TARGETING_CONFIG_CACHE_DURATION = 5f;
        
        private static BaseEntity GetCustomBestTarget(ScientistNPC npc)
        {
            if (npc.Brain == null || npc.Brain.Senses == null || npc.Brain.Senses.Memory == null) return null;
            
            ulong netId = npc.net?.ID.Value ?? 0;
            if (netId == 0) return null;
            
            var npcData = GrimmNPC.GetNpcData(netId);
            if (npcData == null) return null;
            
            // PERFORMANCE: Cache config lookup
            float currentTime = Time.time;
            if (_cachedTargetingConfig == null || (currentTime - _lastTargetingConfigCheck) > TARGETING_CONFIG_CACHE_DURATION)
            {
                _cachedTargetingConfig = GrimmNPC.GetConfig();
                _lastTargetingConfigCheck = currentTime;
            }
            var config = _cachedTargetingConfig;
            
            var brain = npc.Brain;
            BaseEntity bestTarget = null;
            float bestScore = float.MinValue;
            bool debugLogging = config.EnableDebugLogging;
            
            // Fast iteration over memory (optimized for 1000 NPCs)
            var memoryAll = brain.Senses.Memory.All;
            if (memoryAll == null || memoryAll.Count == 0) return null;
            
            int filteredByChaseRange = 0;
            int filteredByCanTarget = 0;
            int totalEntities = memoryAll.Count;
            
            // 🛡️ GUARD NPC PRIORITY: Guard NPCs prioritize threats to their guard target
            if (npcData.IsGuardNpc && npcData.GuardTarget != null && !npcData.GuardTarget.IsDestroyed)
            {
                // Find threats to guard target (players attacking the guard target)
                BasePlayer threatToGuard = null;
                float bestThreatScore = float.MinValue;
                
                for (int i = 0; i < memoryAll.Count; i++)
                {
                    var info = memoryAll[i];
                    var entity = info.Entity;
                    if (entity == null || !(entity is BasePlayer player)) continue;
                    
                    // Check if this player is a threat to the guard target
                    // A threat is a player who has LOS to the guard target or is attacking it
                    if (!CanTargetEntity(entity, npc, npcData, config)) continue;
                    
                    // Check if player is within chase range from guard target (not from NPC's home)
                    float distanceToGuardTarget = Vector3.Distance(player.transform.position, npcData.GuardTarget.transform.position);
                    if (distanceToGuardTarget > npcData.ChaseRange) continue;
                    
                    // Check if player has LOS to guard target (threat indicator)
                    bool hasLosToGuardTarget = false;
                    if (brain.Senses != null && brain.Senses.Memory != null)
                    {
                        // Check if player can see guard target (simple distance check for now)
                        // In a full implementation, you'd check actual LOS
                        if (distanceToGuardTarget <= npcData.SenseRange)
                        {
                            hasLosToGuardTarget = true;
                        }
                    }
                    
                    if (hasLosToGuardTarget)
                    {
                        // Calculate threat score (closer to guard target = higher priority)
                        float threatScore = 1f - Mathf.InverseLerp(1f, npcData.SenseRange, distanceToGuardTarget);
                        if (threatScore > bestThreatScore)
                        {
                            bestThreatScore = threatScore;
                            threatToGuard = player;
                        }
                    }
                }
                
                if (threatToGuard != null)
                {
                    if (config.EnableDebugLogging && Time.frameCount % 480 == 0)
                    {
                        UnityEngine.Debug.Log($"[GrimmNPC Targeting] Guard NPC {npcData.Name ?? "Unknown"} prioritizing threat to guard target: " +
                            $"{threatToGuard.displayName ?? "Player"} (Distance to guard: {Vector3.Distance(threatToGuard.transform.position, npcData.GuardTarget.transform.position):F1}m)");
                    }
                    return threatToGuard;
                }
            }
            
            // 🎯 PRIORITY FIX: For raiding NPCs, check for LOS player targets FIRST
            // If raiding with a goal, only prioritize the goal when no LOS player exists
            // This ensures NPCs return fire when shot, but resume base attack when player ducks behind cover
            BasePlayer losPlayerTarget = null;
            if (npcData.IsRaidingNpc && npcData.RaidGoalActive)
            {
                // First pass: Find best LOS player target (if any)
                float bestLosPlayerScore = float.MinValue;
                for (int i = 0; i < memoryAll.Count; i++)
                {
                    var info = memoryAll[i];
                    var entity = info.Entity;
                    if (entity == null || !(entity is BasePlayer player)) continue;
                    
                    // Fast custom targeting checks
                    if (!CanTargetEntity(entity, npc, npcData, config)) continue;
                    
                    // Check if target is within chase range from home position (use horizontal distance)
                    // 🛡️ GUARD NPC: If guard NPC has guard target, don't chase beyond chase range (like NpcSpawn line 1743)
                    float distanceFromHome = GrimmNPC.GetDistanceFromHome(npcData, entity.transform.position);
                    if (distanceFromHome > npcData.ChaseRange)
                    {
                        // Guard NPCs with guard targets should not chase beyond range (like NpcSpawn)
                        if (npcData.IsGuardNpc && npcData.GuardTarget != null)
                        {
                            continue; // Don't chase if guard NPC has guard target
                        }
                        continue;
                    }
                    
                    // CRITICAL: Only consider players with LOS
                    if (!brain.Senses.Memory.IsLOS(player)) continue;
                    
                    // Calculate score for this LOS player
                    Vector3 diff = npc.transform.position - entity.transform.position;
                    float distance = Mathf.Sqrt(diff.x * diff.x + diff.z * diff.z);
                    float score = 1f - Mathf.InverseLerp(1f, brain.SenseRange, distance);
                    score += 2f; // LOS bonus
                    
                    if (score > bestLosPlayerScore)
                    {
                        bestLosPlayerScore = score;
                        losPlayerTarget = player;
                    }
                }
                
                // If we found a valid LOS player target, return it immediately (NPC should return fire)
                if (losPlayerTarget != null)
                {
                    if (config.EnableDebugLogging && Time.frameCount % 480 == 0)
                    {
                        UnityEngine.Debug.Log($"[GrimmNPC Targeting] NPC {npcData.Name ?? "Unknown"} prioritizing LOS player target over raid goal: " +
                            $"{losPlayerTarget.displayName ?? "Player"} (Distance: {Vector3.Distance(npc.transform.position, losPlayerTarget.transform.position):F1}m)");
                    }
                    return losPlayerTarget;
                }
                
                // No LOS player found - now check raid goal (NPC should focus on base)
                // Priority 1: Raid Goal Entity (if set and valid)
                if (npcData.RaidGoalEntityId != 0)
                {
                    BaseEntity raidGoalEntity = BaseNetworkable.serverEntities.Find(new NetworkableId(npcData.RaidGoalEntityId)) as BaseEntity;
                    if (raidGoalEntity != null && !raidGoalEntity.IsDestroyed && raidGoalEntity.Health() > 0f)
                    {
                        // Check if entity is within reasonable range
                        float distance = Vector3.Distance(npc.transform.position, raidGoalEntity.transform.position);
                        if (distance <= npcData.ChaseRange * 1.5f) // Allow slightly beyond ChaseRange for raid goals
                        {
                            if (config.EnableDebugLogging && Time.frameCount % 480 == 0)
                            {
                                UnityEngine.Debug.Log($"[GrimmNPC Targeting] NPC {npcData.Name ?? "Unknown"} prioritizing Raid Goal Entity (no LOS player): " +
                                    $"{raidGoalEntity.GetType().Name} (NetID: {npcData.RaidGoalEntityId}, Distance: {distance:F1}m)");
                            }
                            return raidGoalEntity;
                        }
                    }
                }
                
                // Priority 2: Raid Goal Position (if entity not set or destroyed)
                if (npcData.RaidGoalPosition != Vector3.zero)
                {
                    float distanceToGoal = Vector3.Distance(npc.transform.position, npcData.RaidGoalPosition);
                    if (distanceToGoal <= npcData.ChaseRange * 1.5f)
                    {
                        // Find nearest structure/entity at raid goal position
                        // For now, return null and let raiding system handle movement to position
                        // The raiding system (Raid.TickRaid) will handle attacking structures at this position
                        // We return null here to allow raiding system to work, but NPC will still move toward goal
                        if (config.EnableDebugLogging && Time.frameCount % 480 == 0)
                        {
                            UnityEngine.Debug.Log($"[GrimmNPC Targeting] NPC {npcData.Name ?? "Unknown"} has Raid Goal Position (no LOS player): " +
                                $"{npcData.RaidGoalPosition} (Distance: {distanceToGoal:F1}m) - raiding system will handle structure targeting");
                        }
                        // Return null to allow raiding system to handle structure targeting
                        // The raiding system will find and attack structures blocking path to goal
                    }
                }
            }
            
            // Normal targeting logic: iterate all memory entities and score them
            for (int i = 0; i < memoryAll.Count; i++)
            {
                var info = memoryAll[i];
                var entity = info.Entity;
                if (entity == null) continue;
                
                // Fast custom targeting checks
                if (!CanTargetEntity(entity, npc, npcData, config))
                {
                    filteredByCanTarget++;
                    continue;
                }
                
                // Check if target is within chase range from home position (use horizontal distance)
                // CRITICAL: Use helper method for consistency (prevents Y-axis issues)
                float distanceFromHome = GrimmNPC.GetDistanceFromHome(npcData, entity.transform.position);
                if (distanceFromHome > npcData.ChaseRange)
                {
                    filteredByChaseRange++;
                    // PERFORMANCE: Removed per-entity debug logging (too expensive)
                    continue; // Target too far from home
                }
                
                // 🎯 TARGETING PRIORITY OVERRIDE: For raiding NPCs, prioritize:
                // 1. Raid defenders (players damaging NPC) - highest priority
                // 2. Raid structures (already handled above via Raid Goal)
                // 3. Other players - lower priority
                
                // Check if this is a player damaging the NPC (raid defender)
                bool isRaidDefender = false;
                if (entity is BasePlayer player && npcData.IsRaidingNpc)
                {
                    // Check if player recently damaged this NPC
                    // This is a simplified check - in practice, you might track recent damage sources
                    // For now, we'll prioritize players in combat with the NPC
                    var npcBrain = npc.Brain;
                    if (npcBrain != null && npcBrain.Events != null && npcBrain.Events.Memory != null)
                    {
                        int memorySlot = npcBrain.Events.CurrentInputMemorySlot;
                        if (memorySlot >= 0)
                        {
                            var currentTarget = npcBrain.Events.Memory.Entity?.Get(memorySlot);
                            if (currentTarget == player)
                            {
                                isRaidDefender = true;
                            }
                        }
                    }
                }
                
                // Calculate score using GetSingle2 scoring system
                // Combines: distance (inverse lerp from 1m to SenseRange), vision cone, and LOS bonus
                float score = CalculateTargetScore(npc, entity, brain, isRaidDefender);
                
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = entity;
                }
            }
            
            // PERFORMANCE: Throttle debug logging (only every 2 seconds)
            if (debugLogging && Time.frameCount % 480 == 0 && totalEntities > 0)
            {
                UnityEngine.Debug.Log($"[GrimmNPC Targeting] GetBestTarget: " +
                    $"Total={totalEntities}, FilteredByCanTarget={filteredByCanTarget}, " +
                    $"FilteredByChaseRange={filteredByChaseRange}, BestTarget={bestTarget?.GetType().Name ?? "None"}");
            }
            
            return bestTarget;
        }
        
        /// <summary>
        /// Calculates target score using GetSingle2 scoring system.
        /// Score combines: distance (inverse lerp from 1m to SenseRange), vision cone dot product, and LOS bonus.
        /// </summary>
        private static float CalculateTargetScore(ScientistNPC npc, BaseEntity entity, BaseAIBrain brain, bool isRaidDefender)
        {
            if (npc == null || entity == null || brain == null) return float.MinValue;
            
            // Distance score: 1f - InverseLerp(1f, SenseRange, distance)
            // Closer targets get higher scores (1.0 at 1m, 0.0 at SenseRange)
            Vector3 diff = npc.transform.position - entity.transform.position;
            float distance = Mathf.Sqrt(diff.x * diff.x + diff.z * diff.z);
            float distanceScore = 1f - Mathf.InverseLerp(1f, brain.SenseRange, distance);
            
            // Vision cone score: InverseLerp(VisionCone, 1f, dot product) / 2f
            // Targets in vision cone get bonus (0.5 max bonus)
            float visionConeScore = 0f;
            if (npc.eyes != null && brain.VisionCone > 0f)
            {
                Vector3 directionToTarget = (entity.transform.position - npc.eyes.position).normalized;
                float dotProduct = Vector3.Dot(directionToTarget, npc.eyes.BodyForward());
                visionConeScore = Mathf.InverseLerp(brain.VisionCone, 1f, dotProduct) / 2f;
            }
            
            // LOS bonus: +2f if target has line of sight
            float losBonus = 0f;
            if (brain.Senses != null && brain.Senses.Memory != null && brain.Senses.Memory.IsLOS(entity))
            {
                losBonus = 2f;
            }
            
            // 🎯 PRIORITY BONUS: Raid defenders get highest priority
            float raidDefenderBonus = isRaidDefender ? 10f : 0f;
            
            return distanceScore + visionConeScore + losBonus + raidDefenderBonus;
        }
        
        private static bool CanTargetEntity(BaseEntity target, ScientistNPC npc, CustomNpcData npcData, NpcConfig config)
        {
            if (target == null || target.Health() <= 0f) return false;
            
            // Check if target type is in exclusion list
            string targetTypeName = target.GetType().Name;
            if (config.ExcludedTargetTypes != null && config.ExcludedTargetTypes.Count > 0)
            {
                if (config.ExcludedTargetTypes.Contains(targetTypeName))
                {
                    // Target type is excluded - don't target
                    return false;
                }
            }
            
            if (target is BasePlayer player)
            {
                if (player.IsDead()) return false;
                
                // Check for hook support (could be added via interface later)
                // For now, skip hook check as GrimmNPC doesn't use Oxide hooks
                
                // Check if player is Steam ID (real player)
                // Steam IDs are typically > 76561197960265728 (Steam ID 64 format)
                // EncryptedValue<ulong> can be compared directly to ulong
                if (player.userID > 76561197960265728UL)
                {
                    // Real player - use config flags
                    if (!config.CanTargetSleepingPlayer && player.IsSleeping()) return false;
                    if (!config.CanTargetWoundedPlayer && player.IsWounded()) return false;
                    if (!config.CanTargetSafeZonePlayer && player.InSafeZone()) return false;
                    // also checks _limitedNetworking - skip for now (not available in Harmony)
                    return true;
                }
                
                // Check skinID for custom NPCs (could be extended later)
                // checks if skinID matches known NPC skinIDs - skip for now
                
                // Check if NPCPlayer
                if (player is NPCPlayer npcPlayer)
                {
                    return CanTargetNpcPlayer(npcPlayer, config);
                }
                
                return false;
            }
            
            if (target is BaseAnimalNPC animal)
            {
                return CanTargetAnimal(animal, config);
            }
            
            // Drone targeting (only if not melee weapon)
            if (target is Drone drone)
            {
                // checks if CurrentWeapon is BaseMelee - for now, allow all weapons
                // This could be enhanced later to check actual weapon type
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Check if NPC can target another NPC player.
        /// </summary>
        private static bool CanTargetNpcPlayer(NPCPlayer target, NpcConfig config)
        {
            if (target == null) return false;
            
            // Always allow FrankensteinPet
            // Note: FrankensteinPet might not be available in Harmony mods, but keeping logic for compatibility
            try
            {
                if (target.GetType().Name == "FrankensteinPet")
                    return true;
            }
            catch { }
            
            // Never target custom NPCs (skinID == 11162132011012)
            if (target.skinID == GrimmNPC.CUSTOM_NPC_SKIN_ID)
                return false;
            
            return config.CanTargetNpc;
        }
        
        /// <summary>
        /// Check if NPC can target an animal.
        /// </summary>
        private static bool CanTargetAnimal(BaseAnimalNPC animal, NpcConfig config)
        {
            if (animal == null || animal.IsDead()) return false;
            
            // Never target specific skinID (11491311214163)
            if (animal.skinID == 11491311214163UL)
                return false;
            
            // Only target within 30m
            // Note: This distance check is done in scoring, but keeping for consistency
            // The actual distance filtering happens in GetCustomBestTarget
            
            return config.CanTargetAnimal;
        }
    }
    
    /// <summary>
    /// Prevents custom NPCs from targeting scarecrows
    /// </summary>
    [HarmonyPatch(typeof(HumanNPC), nameof(HumanNPC.GetBestTarget))]
    public class HumanNPC_GetBestTarget_Scarecrow_Patch
    {
        static void Postfix(HumanNPC __instance, ref BaseEntity __result)
        {
            if (__result == null) return;
            
            // Only process for custom NPCs (ScientistNPC inherits from HumanNPC)
            ScientistNPC npc = __instance as ScientistNPC;
            if (npc == null || !GrimmNPC.IsCustomNpc(npc)) return;
            
            var config = GrimmNPC.GetConfig();
            if (config.PreventScarecrowTargeting && __result is ScarecrowNPC)
            {
                __result = null; // Block targeting scarecrows
            }
        }
    }
}
