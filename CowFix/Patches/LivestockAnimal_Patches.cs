using HarmonyLib;
using Rust.Ai.Gen2;

namespace CowFix.Patches;

[HarmonyPatch(typeof(LivestockAnimal), nameof(LivestockAnimal.OnPlayerSensed))]
internal static class LivestockAnimal_OnPlayerSensed_Patch
{
    [HarmonyPrefix]
    public static void Prefix(ref float deltaTime)
    {
        float cap = SenseComponent.maxRefreshIntervalSeconds;
        if (cap < 0.05f)
            cap = 0.05f;
        if (deltaTime > cap)
            deltaTime = cap;
    }
}

[HarmonyPatch(typeof(LivestockAnimal), nameof(LivestockAnimal.ServerInit))]
internal static class LivestockAnimal_ServerInit_Patch
{
    [HarmonyPostfix]
    public static void Postfix(LivestockAnimal __instance)
    {
        FalseBond.TryClear(__instance);
    }
}
