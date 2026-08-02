using System;
using HarmonyLib;
using Rust;
using UnityEngine;

namespace GrimmNPC.Patches
{
    /// <summary>
    /// Handles damage and turret scaling for custom NPCs.
    /// 
    /// Patches BaseCombatEntity.OnAttacked() to:
    /// - Apply turret damage scaling (TurretDamageScale)
    /// - Prevent turret targeting (if CanBeTargetedBy* is false)
    /// - Wake up NPC when attacked by player
    /// 
    /// Turret Types Supported:
    /// - AutoTurret (patched via ShouldTarget())
    /// - GunTrap (damage scaling only, targeting not patched yet)
    /// - FlameTurret (damage scaling only, targeting not patched yet)
    /// 
    /// Performance:
    /// - Runs on every damage event (not in hot path)
    /// - Fast entity type checks
    /// - Minimal overhead
    /// 
    /// See INSTRUCTIONAL.md "Patch System - DamagePatches" section for details.
    /// </summary>
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.OnAttacked), new Type[] { typeof(HitInfo) })]
    public class BaseCombatEntity_OnAttacked_Patch
    {
        static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            if (__instance == null || info == null) return;
            if (!GrimmNPC.IsCustomNpc(__instance)) return;

            ProcessCustomDamage(__instance, info);
        }
        
        private static void ProcessCustomDamage(BaseCombatEntity victim, HitInfo info)
        {
            var npc = victim as ScientistNPC;
            if (npc == null) return;
            
            ulong netId = npc.net?.ID.Value ?? 0;
            if (netId == 0) return;
            
            var npcData = GrimmNPC.GetNpcData(netId);
            if (npcData == null) return;
            
            var attacker = info.Initiator;
            
            // Handle turret damage scaling
            if (attacker is AutoTurret)
            {
                if (!npcData.CanBeTargetedByAutoTurrets)
                {
                    info.damageTypes.ScaleAll(0f);
                    return;
                }
                info.damageTypes.ScaleAll(npcData.TurretDamageScale);
            }
            else if (attacker is GunTrap)
            {
                if (!npcData.CanBeTargetedByGunTraps)
                {
                    info.damageTypes.ScaleAll(0f);
                    return;
                }
                info.damageTypes.ScaleAll(npcData.TurretDamageScale);
            }
            else if (attacker is FlameTurret)
            {
                if (!npcData.CanBeTargetedByFlameTurrets)
                {
                    info.damageTypes.ScaleAll(0f);
                    return;
                }
                info.damageTypes.ScaleAll(npcData.TurretDamageScale);
            }
            
            // Wake up NPC if attacked by player
            if (attacker is BasePlayer player)
            {
                npc.IsDormant = false;
                var brain = npc.GetComponent<BaseAIBrain>();
                if (brain != null && brain.sleeping)
                {
                    brain.sleeping = false;
                    if (brain is IAISleepable sleepable)
                    {
                        sleepable.WakeAI();
                    }
                }
            }
        }
    }
}
