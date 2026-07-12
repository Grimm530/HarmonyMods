using HarmonyLib;

namespace Backpacks.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die))]
    public static class Patch_BasePlayer_Die
    {
        static void Postfix(BasePlayer __instance)
        {
            if (__instance == null || __instance.IsNpc) return;
            BackpacksMod.Instance?.OnPlayerDie(__instance);
        }
    }
}
