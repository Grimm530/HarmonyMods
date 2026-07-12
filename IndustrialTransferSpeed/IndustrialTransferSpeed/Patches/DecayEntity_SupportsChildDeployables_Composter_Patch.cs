using HarmonyLib;

namespace IndustrialTransferSpeed.Patches
{
    [HarmonyPatch(typeof(DecayEntity), nameof(DecayEntity.SupportsChildDeployables))]
    public static class DecayEntity_SupportsChildDeployables_Composter_Patch
    {
        static void Postfix(DecayEntity __instance, ref bool __result)
        {
            if (__instance is Composter)
            {
                __result = true;
            }
        }
    }
}
