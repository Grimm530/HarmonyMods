using HarmonyLib;

namespace IndustrialTransferSpeed.Patches
{
    [HarmonyPatch(typeof(StorageContainer), nameof(StorageContainer.PlayerStoppedLooting))]
    public static class StorageContainer_PlayerStoppedLooting_Planter_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(StorageContainer __instance, BasePlayer player)
        {
            if (__instance is PlanterBox && IndustrialTransferSpeedMod.Instance != null)
            {
                IndustrialTransferSpeedMod.Instance.OnPlanterLootEnded(player);
            }
        }
    }
}
