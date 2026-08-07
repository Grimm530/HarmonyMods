using HarmonyLib;
using UnityEngine;
using CCPlugin = Oxide.Plugins.CombatClasses;

namespace CombatClassesHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), "OnReceiveTick", new[] { typeof(PlayerTick), typeof(bool) })]
    public static class BasePlayer_OnReceiveTick_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            if (__instance == null || __instance.serverInput == null) return;
            var inst = CCPlugin.GetModInstance();
            if (inst == null || !inst.IsReady) return;
            if (!CCPlugin.IsHookSubscribed("OnPlayerInput")) return;
            try { CCPlugin.Dispatch_OnPlayerInput(__instance, __instance.serverInput); }
            catch (System.Exception ex) { Debug.LogWarning("[CombatClasses] OnPlayerInput: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), "UpdateActiveItem", new[] { typeof(ItemId) })]
    public static class BasePlayer_UpdateActiveItem_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BasePlayer __instance, out Item __state)
        {
            __state = __instance?.GetActiveItem();
        }

        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance, Item __state)
        {
            if (__instance == null) return;
            var newItem = __instance.GetActiveItem();
            if (__state == newItem) return;
            try { CCPlugin.Dispatch_OnActiveItemChanged(__instance, __state, newItem); }
            catch (System.Exception ex) { Debug.LogWarning("[CombatClasses] OnActiveItemChanged: " + ex.Message); }
        }
    }
}
