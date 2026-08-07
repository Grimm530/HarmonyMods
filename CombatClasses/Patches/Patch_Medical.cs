using HarmonyLib;
using UnityEngine;
using CCPlugin = Oxide.Plugins.CombatClasses;

namespace CombatClassesHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), "OnMedicalToolApplied")]
    public static class BasePlayer_OnMedicalToolApplied_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance, BasePlayer fromPlayer, bool canRevive)
        {
            if (__instance == null || fromPlayer == null || !canRevive) return;
            if (fromPlayer == __instance) return;
            try { CCPlugin.Dispatch_OnPlayerRevive(fromPlayer, __instance); }
            catch (System.Exception ex) { Debug.LogWarning("[CombatClasses] OnPlayerRevive: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.RecoverFromWounded))]
    public static class BasePlayer_RecoverFromWounded_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            if (__instance == null || __instance.IsWounded()) return;
            try { CCPlugin.Dispatch_OnPlayerRecovered(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[CombatClasses] OnPlayerRecovered: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(MedicalTool), "ServerUse")]
    public static class MedicalTool_ServerUse_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(MedicalTool __instance)
        {
            var player = __instance?.GetOwnerPlayer();
            if (player == null) return true;
            object r = CCPlugin.Dispatch_OnHealingItemUse(__instance, player);
            return r == null;
        }
    }
}
