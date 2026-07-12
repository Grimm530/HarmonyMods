using HarmonyLib;
using UnityEngine;

namespace IndustrialTransferSpeed.Patches
{
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity))]
    public static class PlayerLoot_StartLootingEntity_Planter_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerLoot __instance, BaseEntity targetEntity, bool __result)
        {
            if (!__result || targetEntity is not PlanterBox planter || IndustrialTransferSpeedMod.Instance == null)
            {
                return;
            }

            BasePlayer player = ((Component)__instance).GetComponentInParent<BasePlayer>();
            if (player != null)
            {
                IndustrialTransferSpeedMod.Instance.OnPlanterLootStarted(player, planter);
            }
        }
    }
}
