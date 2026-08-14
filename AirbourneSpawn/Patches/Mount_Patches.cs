using HarmonyLib;
using UnityEngine;

namespace AirbourneSpawnHarmony.Patches
{
    [HarmonyPatch(typeof(BaseMountable), nameof(BaseMountable.MountPlayer))]
    internal static class BaseMountable_MountPlayer_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseMountable __instance, BasePlayer player)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || __instance == null || player == null) return;
            try { plugin.OnEntityMounted(__instance, player); }
            catch (System.Exception ex) { Hooks.Warn("OnEntityMounted", ex); }
        }
    }

    [HarmonyPatch(typeof(BaseMountable), nameof(BaseMountable.DismountPlayer))]
    internal static class BaseMountable_DismountPlayer_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseMountable __instance, BasePlayer player)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || __instance == null || player == null) return;
            try { plugin.OnEntityDismounted(__instance, player); }
            catch (System.Exception ex) { Hooks.Warn("OnEntityDismounted", ex); }
        }
    }
}
