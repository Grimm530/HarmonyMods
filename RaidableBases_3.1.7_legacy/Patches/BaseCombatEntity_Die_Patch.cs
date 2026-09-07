/*
 * Invokes RaidableBases OnEntityDeath hook for any BaseCombatEntity (and subclasses: BuildingPrivlidge, StorageContainer, etc.).
 */
using HarmonyLib;
using UnityEngine;

namespace RaidableBases
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), typeof(HitInfo))]
    internal static class BaseCombatEntity_Die_Patch
    {
        [HarmonyPostfix]
        static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            if (__instance == null)
                return;
            Interface.CallHook("OnEntityDeath", __instance, info);
        }
    }
}
