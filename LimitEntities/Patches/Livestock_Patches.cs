using HarmonyLib;
using Rust.Ai.Gen2;

namespace LimitEntities.Patches;

[HarmonyPatch(typeof(LivestockAnimal), nameof(LivestockAnimal.TakeHomeFrom))]
internal static class LivestockAnimal_TakeHomeFrom_Patch
{
    [HarmonyPostfix]
    public static void Postfix(LivestockAnimal __instance)
    {
        LivestockLimits.OnHomeChanged(__instance, killInfantIfOver: true);
    }
}

[HarmonyPatch(typeof(LivestockAnimal), "TryAdoptHome")]
internal static class LivestockAnimal_TryAdoptHome_Patch
{
    [HarmonyPostfix]
    public static void Postfix(LivestockAnimal __instance)
    {
        LivestockLimits.OnHomeChanged(__instance, killInfantIfOver: false);
    }
}

[HarmonyPatch(typeof(LivestockAnimal), nameof(LivestockAnimal.GrowIntoAdult))]
internal static class LivestockAnimal_GrowIntoAdult_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(LivestockAnimal __instance, ref LivestockAnimal __result)
    {
        if (!LivestockLimits.BlockAgeUp(__instance))
            return true;
        __result = null;
        return false;
    }
}
