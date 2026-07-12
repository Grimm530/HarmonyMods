using HarmonyLib;

namespace TeleportGUI.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die))]
    public static class BasePlayer_Die_Patch
    {
        static void Postfix(BasePlayer __instance)
        {
            if (__instance == null || __instance.IsNpc) return;
            TeleportGUIMod.Instance?.OnPlayerDie(__instance);
        }
    }
}
