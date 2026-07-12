using HarmonyLib;

namespace Backpacks.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.ServerInit))]
    public static class Patch_BasePlayer_ServerInit
    {
        static void Postfix(BasePlayer __instance)
        {
            if (__instance == null || __instance.IsNpc) return;
            BackpacksMod.Instance?.ShowBackpackButton(__instance);
        }
    }
}
