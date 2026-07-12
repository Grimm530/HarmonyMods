using HarmonyLib;

namespace IndustrialTransferSpeed.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), "CanCompletePickup")]
    public static class IndustrialStorageAdaptor_CanCompletePickup_Composter_Patch
    {
        static bool Prefix(BaseCombatEntity __instance, ref bool __result)
        {
            if (__instance is IndustrialStorageAdaptor adaptor && ComposterStorageAdaptor.IsManagedAdaptor(adaptor))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}
