using HarmonyLib;
using RustEditStandalone.Features;

namespace RustEditStandalone.Patches;

[HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
public static class BaseCombatEntity_Hurt_Patch
{
    static bool Prefix(BaseCombatEntity __instance, HitInfo info)
    {
        if (__instance == null) return true;
        if (!DeployableFeature.ShouldBlockDamage(__instance) && !IoFeature.IsMapIo(__instance))
            return true;

        // Cancel damage for map-placed entities / IO
        if (info != null)
        {
            info.damageTypes.ScaleAll(0f);
            info.DidHit = false;
        }
        return false;
    }
}

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
