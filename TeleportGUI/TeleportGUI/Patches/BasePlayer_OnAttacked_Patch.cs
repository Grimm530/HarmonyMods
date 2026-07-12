using HarmonyLib;

namespace TeleportGUI.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnAttacked))]
    public static class BasePlayer_OnAttacked_Patch
    {
        static void Postfix(BasePlayer __instance)
        {
            if (__instance == null || __instance.IsNpc) return;
            TeleportGUIMod.Instance?.OnPlayerTakeDamage(__instance);
        }
    }
}
