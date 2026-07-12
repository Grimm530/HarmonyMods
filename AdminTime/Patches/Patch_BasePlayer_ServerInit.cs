using HarmonyLib;

namespace AdminTime.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.ServerInit))]
    public static class Patch_BasePlayer_ServerInit
    {
        static void Postfix(BasePlayer __instance)
        {
            if (__instance == null || __instance.IsNpc) return;
            AdminTimeMod.Instance?.OnPlayerConnected(__instance);
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class Patch_BasePlayer_OnDisconnected
    {
        static void Postfix(BasePlayer __instance)
        {
            if (__instance == null) return;
            AdminTimeMod.Instance?.OnPlayerDisconnected(__instance);
        }
    }
}
