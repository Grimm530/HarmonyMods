using HarmonyLib;
using UnityEngine;
using P = Oxide.Plugins.KillFeed;

namespace KillFeedHarmony.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) })]
    public static class BaseCombatEntity_Die_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            try { P.Dispatch_OnEntityDeath(__instance, info); }
            catch (System.Exception ex) { Debug.LogWarning("[KillFeed] OnEntityDeath: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Save), typeof(string), typeof(bool))]
    public static class SaveRestore_Save_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { P.Dispatch_OnServerSave(); }
            catch (System.Exception ex) { Debug.LogWarning("[KillFeed] OnServerSave: " + ex.Message); }
        }
    }
}
