using HarmonyLib;
using UnityEngine;
using CookingPlugin = Oxide.Plugins.Cooking;

namespace CookingHarmony.Patches
{
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity), new[] { typeof(BaseEntity), typeof(bool) })]
    public static class PlayerLoot_StartLootingEntity_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerLoot __instance, BaseEntity targetEntity, bool __result)
        {
            if (!__result || __instance == null || targetEntity == null) return;
            BasePlayer player = __instance.baseEntity;
            if (player == null) return;
            try { CookingPlugin.Dispatch_OnLootEntity(player, targetEntity); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnLootEntity: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.Clear))]
    public static class PlayerLoot_Clear_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(PlayerLoot __instance)
        {
            BasePlayer player = __instance?.baseEntity;
            BaseEntity source = __instance?.entitySource;
            if (player == null || source == null) return;
            try { CookingPlugin.Dispatch_OnLootEntityEnd(player, source); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnLootEntityEnd: " + ex.Message); }
        }
    }
}
