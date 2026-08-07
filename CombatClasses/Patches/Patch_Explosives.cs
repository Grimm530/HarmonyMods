using HarmonyLib;
using UnityEngine;
using CCPlugin = Oxide.Plugins.CombatClasses;

namespace CombatClassesHarmony.Patches
{
    [HarmonyPatch(typeof(ThrownWeapon), "SetUpThrownWeapon")]
    public static class ThrownWeapon_SetUpThrownWeapon_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThrownWeapon __instance, BaseEntity ent)
        {
            if (__instance == null || ent == null) return;
            var player = __instance.GetOwnerPlayer();
            if (player == null) return;
            try { CCPlugin.Dispatch_OnExplosiveThrown(player, ent, __instance); }
            catch (System.Exception ex) { Debug.LogWarning("[CombatClasses] OnExplosiveThrown: " + ex.Message); }
        }
    }

    /// <summary>OnExplosiveDud — plugin no-ops (SkillTree owns dudless); patch retained for API parity.</summary>
    [HarmonyPatch(typeof(DudTimedExplosive), nameof(DudTimedExplosive.BecomeDud))]
    public static class DudTimedExplosive_BecomeDud_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(DudTimedExplosive __instance)
        {
            object result = CCPlugin.Dispatch_OnExplosiveDud(__instance);
            if (result is bool b && !b)
            {
                try
                {
                    var explode = AccessTools.Method(typeof(TimedExplosive), nameof(TimedExplosive.Explode));
                    explode?.Invoke(__instance, null);
                }
                catch { }
                return false;
            }
            return true;
        }
    }

    /// <summary>OnRocketLaunched — BaseLauncher.ProjectileLaunched_Server fires with the launched projectile.</summary>
    [HarmonyPatch(typeof(BaseLauncher), nameof(BaseLauncher.ProjectileLaunched_Server))]
    public static class BaseLauncher_ProjectileLaunched_Server_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseLauncher __instance, ServerProjectile justLaunched)
        {
            if (__instance == null || justLaunched == null) return;
            var player = __instance.GetOwnerPlayer();
            if (player == null) return;
            var entity = justLaunched.baseEntity as BaseEntity;
            if (entity == null) return;
            try { CCPlugin.Dispatch_OnRocketLaunched(player, entity); }
            catch (System.Exception ex) { Debug.LogWarning("[CombatClasses] OnRocketLaunched: " + ex.Message); }
        }
    }
}
