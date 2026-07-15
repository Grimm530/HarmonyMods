using System;
using HarmonyLib;
using UnityEngine;

namespace RemoverToolHarmony.Patches
{
    /// <summary>
    /// Oxide OnHammerHit / OnPlayerAttack — BaseMelee.DoAttackShared prefix (covers Hammer).
    /// When the plugin returns a non-null result the melee hit is cancelled (return false).
    /// </summary>
    [HarmonyPatch(typeof(BaseMelee), "DoAttackShared", new[] { typeof(HitInfo) })]
    internal static class BaseMelee_DoAttackShared_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseMelee __instance, HitInfo info)
        {
            var plugin = RemoverToolHarmonyMod.Instance?.Plugin;
            if (plugin == null) return true;
            if (!plugin.IsSubscribed("OnHammerHit") && !plugin.IsSubscribed("OnPlayerAttack")) return true;
            if (info == null) return true;

            try
            {
                var player = __instance?.GetOwnerPlayer();
                if (player == null) return true;
                var result = plugin.OnHammerHit(player, info);
                if (result != null) return false; // cancel the hit
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RemoverTool] OnHammerHit: " + ex.Message);
            }
            return true;
        }
    }

    /// <summary>Oxide OnActiveItemChanged — BasePlayer.UpdateActiveItem prefix/postfix (only when subscribed).</summary>
    [HarmonyPatch(typeof(BasePlayer), "UpdateActiveItem", new[] { typeof(ItemId) })]
    internal static class BasePlayer_UpdateActiveItem_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BasePlayer __instance, out Item __state)
        {
            __state = __instance?.GetActiveItem();
        }

        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance, Item __state)
        {
            if (__instance == null) return;
            var plugin = RemoverToolHarmonyMod.Instance?.Plugin;
            if (plugin == null || !plugin.IsSubscribed("OnActiveItemChanged")) return;

            var newItem = __instance.GetActiveItem();
            if (__state == newItem) return;
            try { plugin.OnActiveItemChanged(__instance, __state, newItem); }
            catch (Exception ex) { Debug.LogWarning("[RemoverTool] OnActiveItemChanged: " + ex.Message); }
        }
    }
}
