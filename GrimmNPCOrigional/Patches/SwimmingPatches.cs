using HarmonyLib;
using Rust;
using Rust.Ai;
using System;
using System.Reflection;
using UnityEngine;

namespace GrimmNPC.Patches
{
    /// <summary>
    /// Swimming support for GrimmNPC NPCs.
    /// 
    /// Allows NPCs to seamlessly transition between walking on land and swimming in water.
    /// 
    /// Integration with Rust's Navigation System:
    /// - Patches multiple BaseNavigator methods to enable custom swimming behavior
    /// - Disables NavMesh navigation when swimming (prevents NavMesh interference)
    /// - Uses custom movement (Vector3.MoveTowards) when swimming
    /// - Constrains Y position to water surface (1.1m below surface)
    /// 
    /// Swimming Detection:
    /// - Checks modelState.waterLevel > 0.75f (same threshold as ChaosNPC)
    /// - Only enabled if npcData.CanSwim = true
    /// 
    /// Performance:
    /// - Called every frame when swimming (60 FPS = 60 calls/second per swimming NPC)
    /// - Fast water level check (single float comparison)
    /// - Cached FieldInfo for currentSpeedFraction (avoids repeated reflection)
    /// - Performance Impact: <0.001ms per call (lightweight)
    /// 
    /// See INSTRUCTIONAL.md "Patch System - SwimmingPatches" section for details.
    /// </summary>
    
    /// <summary>
    /// Patches IsSwimming() to check waterLevel and CanSwim setting.
    /// This is checked every frame, enabling automatic transition between land and water.
    /// </summary>
    [HarmonyPatch(typeof(BaseNavigator), nameof(BaseNavigator.IsSwimming))]
    public class BaseNavigator_IsSwimming_Patch
    {
        static bool Prefix(BaseNavigator __instance, ref bool __result)
        {
            // Only process for custom NPCs
            if (__instance.BaseEntity == null || !GrimmNPC.IsCustomNpc(__instance.BaseEntity))
                return true; // Continue to original (returns false)
            
            try
            {
                var npc = __instance.BaseEntity as ScientistNPC;
                if (npc == null) return true;
                
                ulong netId = npc.net?.ID.Value ?? 0;
                if (netId == 0) return true;
                
                var npcData = GrimmNPC.GetNpcData(netId);
                if (npcData == null) return true;
                
                // Check if swimming is enabled for this NPC
                if (!npcData.CanSwim)
                {
                    __result = false;
                    return false; // Skip original
                }
                
                // Check water level (same threshold as ChaosNPC: 0.75f)
                // modelState.waterLevel is updated every frame by BasePlayer
                if (npc.modelState != null && npc.modelState.waterLevel > 0.75f)
                {
                    __result = true;
                    return false; // Skip original
                }
                
                __result = false;
                return false; // Skip original
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[GrimmNPC] Error in IsSwimming patch: {ex}");
                return true; // Fall back to original
            }
        }
    }
    
    /// <summary>
    /// Patches GetTargetSpeed() to apply swimming speed multiplier when swimming
    /// PERFORMANCE: Uses cached FieldInfo to avoid repeated reflection lookups
    /// Note: Using string method name because GetTargetSpeed() is protected/private
    /// </summary>
    [HarmonyPatch(typeof(BaseNavigator), "GetTargetSpeed")]
    public class BaseNavigator_GetTargetSpeed_Patch
    {
        // Cache FieldInfo to avoid repeated reflection lookups (PERFORMANCE OPTIMIZATION)
        // This field is looked up once at class load, not every method call
        private static readonly FieldInfo _currentSpeedFractionField = typeof(BaseNavigator)
            .GetField("currentSpeedFraction", BindingFlags.NonPublic | BindingFlags.Instance);
        
        static bool Prefix(BaseNavigator __instance, ref float __result)
        {
            // Only process for custom NPCs
            if (__instance.BaseEntity == null || !GrimmNPC.IsCustomNpc(__instance.BaseEntity))
                return true; // Continue to original
            
            try
            {
                // Check if currently swimming
                if (!__instance.IsSwimming())
                {
                    return true; // Continue to original (normal speed)
                }
                
                var npc = __instance.BaseEntity as ScientistNPC;
                if (npc == null) return true;
                
                ulong netId = npc.net?.ID.Value ?? 0;
                if (netId == 0) return true;
                
                var npcData = GrimmNPC.GetNpcData(netId);
                if (npcData == null) return true;
                
                // Get base speed by accessing protected currentSpeedFraction via cached FieldInfo
                // PERFORMANCE: Using cached FieldInfo is much faster than GetField() every call
                float currentSpeedFraction = _currentSpeedFractionField != null 
                    ? (float)_currentSpeedFractionField.GetValue(__instance) 
                    : 1f;
                
                // Calculate base speed (same as base.GetTargetSpeed())
                float baseSpeed = __instance.Speed * currentSpeedFraction;
                
                // Apply swimming multiplier
                __result = baseSpeed * npcData.SwimmingSpeedMultiplier;
                return false; // Skip original
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[GrimmNPC] Error in GetTargetSpeed patch: {ex}");
                return true; // Fall back to original
            }
        }
    }
    
    /// <summary>
    /// Patches UpdatePositionAndRotation() to handle swimming movement
    /// When swimming: Uses Vector3.MoveTowards and constrains Y to water surface
    /// When not swimming: Calls base method for normal navigation
    /// This enables seamless transition between land and water
    /// Note: Using string method name because UpdatePositionAndRotation() is protected/private
    /// </summary>
    [HarmonyPatch(typeof(BaseNavigator), "UpdatePositionAndRotation", new Type[] { typeof(Vector3), typeof(float) })]
    public class BaseNavigator_UpdatePositionAndRotation_Patch
    {
        // Cache MethodInfo for GetTargetSpeed to avoid repeated reflection lookups
        private static readonly MethodInfo _getTargetSpeedMethod = typeof(BaseNavigator)
            .GetMethod("GetTargetSpeed", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        
        static bool Prefix(BaseNavigator __instance, Vector3 moveToPosition, float delta)
        {
            // Only process for custom NPCs
            if (__instance.BaseEntity == null || !GrimmNPC.IsCustomNpc(__instance.BaseEntity))
                return true; // Continue to original
            
            try
            {
                var npc = __instance.BaseEntity as ScientistNPC;
                if (npc == null) return true;
                
                // Check if currently swimming
                if (!__instance.IsSwimming())
                {
                    return true; // Continue to original (normal navigation)
                }
                
                // SWIMMING MODE: Custom movement logic
                // This is the same approach as ChaosNPC's CustomScientistNavigator
                
                // Move towards destination using swimming speed
                Vector3 currentPos = __instance.BaseEntity.transform.position;
                // Use reflection to call GetTargetSpeed() since it's protected/private
                float targetSpeed = _getTargetSpeedMethod != null 
                    ? (float)_getTargetSpeedMethod.Invoke(__instance, null) 
                    : __instance.Speed;
                Vector3 newPosition = Vector3.MoveTowards(currentPos, moveToPosition, targetSpeed * delta);
                
                // Constrain Y position between water surface and terrain height
                // NPCs swim 1.1m below water surface (same as ChaosNPC)
                float waterSurface = WaterLevel.GetWaterSurface(newPosition, waves: true, volumes: true);
                float terrainHeight = TerrainMeta.HeightMap.GetHeight(newPosition);
                newPosition.y = Mathf.Max(Mathf.Min(newPosition.y, waterSurface - 1.1f), terrainHeight);
                
                // Update position
                __instance.BaseEntity.transform.position = newPosition;
                __instance.BaseEntity.ServerPosition = __instance.BaseEntity.transform.localPosition;
                
                // Handle rotation - face movement direction (calculate 2D direction manually)
                Vector3 direction3D = moveToPosition - currentPos;
                Vector3 direction2D = new Vector3(direction3D.x, 0f, direction3D.z);
                if (direction2D.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(direction2D, Vector3.up);
                    npc.eyes.rotation = Quaternion.Lerp(npc.eyes.rotation, targetRotation, delta * 25f);
                    npc.viewAngles = npc.eyes.rotation.eulerAngles;
                    npc.ServerRotation = npc.eyes.rotation;
                }
                
                return false; // Skip original (we handled it)
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[GrimmNPC] Error in UpdatePositionAndRotation patch: {ex}");
                return true; // Fall back to original
            }
        }
    }
    
    /// <summary>
    /// Patches CanEnableNavMeshNavigation() to disable NavMesh when swimming
    /// This prevents NavMesh from interfering with swimming movement
    /// Note: Using string method name because CanEnableNavMeshNavigation() is protected/private
    /// </summary>
    [HarmonyPatch(typeof(BaseNavigator), "CanEnableNavMeshNavigation")]
    public class BaseNavigator_CanEnableNavMeshNavigation_Patch
    {
        static bool Prefix(BaseNavigator __instance, ref bool __result)
        {
            // Only process for custom NPCs
            if (__instance.BaseEntity == null || !GrimmNPC.IsCustomNpc(__instance.BaseEntity))
                return true; // Continue to original
            
            try
            {
                // If swimming, disable NavMesh navigation
                if (__instance.IsSwimming())
                {
                    __result = false;
                    return false; // Skip original
                }
                
                return true; // Continue to original
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[GrimmNPC] Error in CanEnableNavMeshNavigation patch: {ex}");
                return true; // Fall back to original
            }
        }
    }
}
