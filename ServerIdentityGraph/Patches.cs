using HarmonyLib;

namespace ServerIdentityGraph.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    internal static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            if (__instance == null || !__instance.IsConnected)
                return;

            // Team is ready immediately; vanilla clan name arrives asynchronously.
            __instance.Invoke(() => IdentityCollector.Record(__instance), 1f);
            __instance.Invoke(() => IdentityCollector.Record(__instance), 8f);
        }
    }

    [HarmonyPatch(typeof(RelationshipManager.PlayerTeam), nameof(RelationshipManager.PlayerTeam.AddPlayer), typeof(ulong), typeof(bool))]
    internal static class PlayerTeam_AddPlayer_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(RelationshipManager.PlayerTeam __instance, ulong playerId, bool __result)
        {
            if (!__result || __instance == null || playerId == 0)
                return;
            IdentityCollector.RecordTeam(__instance, playerId);
        }
    }
}
