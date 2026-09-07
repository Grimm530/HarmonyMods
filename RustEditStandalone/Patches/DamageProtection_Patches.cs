using HarmonyLib;
using RustEditStandalone.Features;

namespace RustEditStandalone.Patches;

[HarmonyPatch(typeof(StabilityEntity), nameof(StabilityEntity.StabilityCheck))]
public static class StabilityEntity_StabilityCheck_Patch
{
    static bool Prefix(StabilityEntity __instance)
    {
        if (__instance != null && DeployableFeature.ShouldBlockStability(__instance))
            return false;
        return true;
    }
}

[HarmonyPatch(typeof(BaseTrap), nameof(BaseTrap.ObjectEntered))]
public static class BaseTrap_ObjectEntered_Patch
{
    static void Postfix(BaseTrap __instance)
    {
        DeployableFeature.OnTrapTriggered(__instance);
    }
}
