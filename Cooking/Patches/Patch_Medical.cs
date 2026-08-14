using HarmonyLib;
using UnityEngine;
using CookingPlugin = Oxide.Plugins.Cooking;

namespace CookingHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), "OnMedicalToolApplied")]
    public static class BasePlayer_OnMedicalToolApplied_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance, BasePlayer fromPlayer, bool canRevive)
        {
            if (__instance == null || fromPlayer == null || !canRevive) return;
            if (fromPlayer == __instance) return;
            try { CookingPlugin.Dispatch_OnPlayerRevive(fromPlayer, __instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnPlayerRevive: " + ex.Message); }
        }
    }
}
