using HarmonyLib;
using UnityEngine;

namespace AirbourneSpawnHarmony.Patches
{
    [HarmonyPatch(typeof(Item), nameof(Item.Remove))]
    internal static class Item_Remove_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Item __instance)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnItemRemove(__instance); }
            catch (System.Exception ex) { Hooks.Warn("OnItemRemove", ex); }
        }
    }
}
