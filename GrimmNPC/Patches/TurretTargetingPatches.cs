using HarmonyLib;
using Rust;
using System;
using UnityEngine;

namespace GrimmNPC.Patches
{
    /// <summary>
    /// Prevents turrets from targeting custom NPCs based on configuration.
    /// 
    /// Patches AutoTurret.ShouldTarget() to block targeting if CanBeTargetedByAutoTurrets is false.
    /// 
    /// Integration:
    /// - AutoTurret: Patched via ShouldTarget() method
    /// - GunTrap: Not patched yet (uses CheckTrigger() method)
    /// - FlameTurret: Not patched yet (uses CheckTrigger() method)
    /// 
    /// Process:
    /// 1. Check if target is custom NPC (fast skinID check)
    /// 2. Get NPC data from registration
    /// 3. Check CanBeTargetedByAutoTurrets flag
    /// 4. Return false to block targeting if flag is false
    /// 
    /// Performance:
    /// - Runs on every turret targeting check (not in hot path)
    /// - Fast entity type and skinID checks
    /// - Minimal overhead
    /// 
    /// See INSTRUCTIONAL.md "Patch System - TurretTargetingPatches" section for details.
    /// </summary>
    [HarmonyPatch(typeof(AutoTurret), nameof(AutoTurret.ShouldTarget))]
    public class AutoTurret_ShouldTarget_Patch
    {
        static bool Prefix(AutoTurret __instance, BaseCombatEntity targ, ref bool __result)
        {
            if (targ == null) return true;
            if (!GrimmNPC.IsCustomNpc(targ)) return true;
            
            var npc = targ as ScientistNPC;
            if (npc == null) return true;
            
            ulong netId = npc.net?.ID.Value ?? 0;
            if (netId == 0) return true;
            
            var npcData = GrimmNPC.GetNpcData(netId);
            if (npcData != null && !npcData.CanBeTargetedByAutoTurrets)
            {
                __result = false; // Block targeting
                return false; // Skip original method
            }
            
            return true; // Continue to original
        }
    }
    
    // Note: GunTrap and FlameTurret use CheckTrigger() and Oxide hooks
    // These can be patched later if needed by patching CheckTrigger method
}
