using HarmonyLib;
using UnityEngine;

namespace DynamicCupShareHarmony.Patches
{
    [HarmonyPatch(typeof(RelationshipManager.PlayerTeam), nameof(RelationshipManager.PlayerTeam.AddPlayer), typeof(BasePlayer), typeof(bool))]
    internal static class PlayerTeam_AddPlayer_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(RelationshipManager.PlayerTeam __instance, BasePlayer player, bool skipDirtyUpdate, bool __result)
        {
            if (!__result) return;
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return;
            try { plugin.OnTeamAcceptInvite(__instance, player); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] OnTeamAcceptInvite: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(RelationshipManager.PlayerTeam), nameof(RelationshipManager.PlayerTeam.RemovePlayer), typeof(ulong))]
    internal static class PlayerTeam_RemovePlayer_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(RelationshipManager.PlayerTeam __instance, ulong playerID, bool __result)
        {
            if (!__result) return;
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return;
            try { plugin.OnTeamMemberRemoved(__instance, playerID); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] OnTeamLeave: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(RelationshipManager.PlayerTeam), nameof(RelationshipManager.PlayerTeam.AddPlayer), typeof(ulong), typeof(bool))]
    internal static class PlayerTeam_AddPlayerId_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(RelationshipManager.PlayerTeam __instance, ulong playerId, bool skipDirtyUpdate, bool __result)
        {
            if (!__result) return;
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return;
            try { plugin.OnTeamMemberAdded(__instance, playerId); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] OnTeamMemberAdded: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(RelationshipManager), nameof(RelationshipManager.DisbandTeam))]
    internal static class RelationshipManager_DisbandTeam_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(RelationshipManager.PlayerTeam teamToDisband)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null || teamToDisband == null) return;
            try { plugin.OnTeamDisband(teamToDisband); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] OnTeamDisband: " + ex.Message); }
        }
    }
}
