using HarmonyLib;

namespace Backpacks.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.EndSleeping))]
    public static class Patch_BasePlayer_EndSleeping
    {
        static void Postfix(BasePlayer __instance)
        {
            if (__instance == null || __instance.IsNpc) return;
            BackpacksMod.Instance?.ShowBackpackButton(__instance);
        }
    }
}
