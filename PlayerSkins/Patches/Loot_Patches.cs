using HarmonyLib;
using UnityEngine;

namespace PlayerSkinsHarmony.Patches
{
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.Clear))]
    internal static class PlayerLoot_Clear_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(PlayerLoot __instance)
        {
            if (!PlayerSkinsPlugin.ReskinLootHooksActive) return;
            BasePlayer player = __instance?.baseEntity;
            BaseEntity source = __instance?.entitySource;
            if (player == null || source is not StorageContainer sc) return;
            try { PlayerSkinsMod.Instance?.Plugin?.OnLootEntityEnd(player, sc); }
            catch (System.Exception ex) { Debug.LogWarning("[PlayerSkins] OnLootEntityEnd: " + ex.Message); }
        }
    }
}
