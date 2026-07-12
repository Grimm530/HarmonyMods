using HarmonyLib;
using UnityEngine;

namespace RaidableBases
{
    /// <summary>
    /// Oxide calls OnNpcDuck / OnNpcDestinationSet into the plugin when subscribed.
    /// Wire the same hooks for HumanoidNPC so roam/duck behavior matches Oxide RaidableBases.
    /// </summary>
    [HarmonyPatch(typeof(HumanNPC), nameof(HumanNPC.SetDucked))]
    internal static class HumanNPC_SetDucked_Patch
    {
        private static bool Prefix(HumanNPC __instance, bool flag)
        {
            if (__instance is not RaidableBases.HumanoidNPC npc)
            {
                return true;
            }
            var result = Interface.CallHook("OnNpcDuck", npc);
            // Oxide: returning non-null blocks the duck (RaidableBases returns true).
            return result == null;
        }
    }

    [HarmonyPatch(typeof(BaseNavigator), nameof(BaseNavigator.SetDestination), typeof(Vector3), typeof(BaseNavigator.NavigationSpeed), typeof(float), typeof(float))]
    internal static class BaseNavigator_SetDestination_NpcRoam_Patch
    {
        private static bool Prefix(BaseNavigator __instance, Vector3 pos)
        {
            var entity = __instance.GetComponent<RaidableBases.HumanoidNPC>()
                ?? __instance.BaseEntity as RaidableBases.HumanoidNPC;
            if (entity == null)
            {
                return true;
            }
            var result = Interface.CallHook("OnNpcDestinationSet", entity, pos);
            return result == null;
        }
    }
}
