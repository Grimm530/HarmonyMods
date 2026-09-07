// Team join → LootDefender MemberId merge (mid-fight contribution).
using System;
using HarmonyLib;
using UnityEngine;
using TPVE = Harmony.Plugins.TruePVE;

namespace TruePVEHarmony.Patches
{
    [HarmonyPatch(typeof(RelationshipManager.PlayerTeam), nameof(RelationshipManager.PlayerTeam.AcceptInvite))]
    public static class Patch_PlayerTeam_AcceptInvite
    {
        [HarmonyPostfix]
        public static void Postfix(RelationshipManager.PlayerTeam __instance, BasePlayer player)
        {
            try
            {
                if (player == null) return;
                player.Invoke(() =>
                {
                    if (player == null || player.IsDestroyed) return;
                    if (player.currentTeam != __instance.teamID) return;
                    TPVE.Dispatch_OnTeamAcceptInvite(__instance, player);
                }, 0.01f);
            }
            catch (Exception ex) { Debug.LogWarning("[TruePVE] OnTeamAcceptInvite: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(RelationshipManager.PlayerTeam), nameof(RelationshipManager.PlayerTeam.AddPlayer), new[] { typeof(BasePlayer), typeof(bool) })]
    public static class Patch_PlayerTeam_AddPlayer
    {
        [HarmonyPostfix]
        public static void Postfix(RelationshipManager.PlayerTeam __instance, BasePlayer player, bool skipDirtyUpdate, bool __result)
        {
            if (!__result || player == null) return;
            try { TPVE.Dispatch_OnTeamAcceptInvite(__instance, player); }
            catch (Exception ex) { Debug.LogWarning("[TruePVE] OnTeamAddPlayer: " + ex.Message); }
        }
    }
}
