// Targeting hooks: OnTurretTarget, OnTrapTrigger, OnEntityEnter, OnNpcTarget, OnSamSiteTarget.
using HarmonyLib;
using UnityEngine;
using TPVE = Oxide.Plugins.TruePVE;

namespace TruePVEHarmony.Patches
{
    /// <summary>OnTurretTarget - clear the target when the ruleset says the turret must ignore it.</summary>
    [HarmonyPatch(typeof(AutoTurret), nameof(AutoTurret.SetTarget))]
    public static class Patch_AutoTurret_SetTarget
    {
        [HarmonyPrefix]
        public static void Prefix(AutoTurret __instance, ref BaseCombatEntity targ)
        {
            if (__instance == null || targ == null) return;
            try
            {
                if (TPVE.Dispatch_OnTurretTarget(__instance, targ) != null)
                    targ = null; // clear targeting (Oxide non-null == cancel)
            }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnTurretTarget: " + ex.Message); }
        }
    }

    /// <summary>OnSamSiteTarget - PlayerSamSitesIgnorePlayers / StaticSamSitesIgnorePlayers / SamSitesIgnoreMLRS.</summary>
    [HarmonyPatch(typeof(SamSite), "SetTarget")]
    public static class Patch_SamSite_SetTarget
    {
        [HarmonyPrefix]
        public static void Prefix(SamSite __instance, ref SamSite.ISamSiteTarget target)
        {
            if (__instance == null || target == null || target.IsUnityNull()) return;
            if (!(target is BaseEntity be)) return;
            try
            {
                if (TPVE.Dispatch_OnSamSiteTarget(__instance, be) != null)
                    target = null;
            }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnSamSiteTarget: " + ex.Message); }
        }
    }

    /// <summary>OnTrapTrigger - non-null cancels the trap firing.</summary>
    [HarmonyPatch(typeof(BaseTrap), nameof(BaseTrap.ObjectEntered))]
    public static class Patch_BaseTrap_ObjectEntered
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseTrap __instance, GameObject obj)
        {
            if (__instance == null || obj == null) return true;
            try { return TPVE.Dispatch_OnTrapTrigger(__instance, obj) == null; }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnTrapTrigger: " + ex.Message); return true; }
        }
    }

    /// <summary>OnEntityEnter - TargetTrigger (turrets) / TriggerEnterTimer (hoppers). Non-null cancels enter.</summary>
    [HarmonyPatch(typeof(TriggerBase), nameof(TriggerBase.OnEntityEnter), new[] { typeof(BaseEntity) })]
    public static class Patch_TriggerBase_OnEntityEnter
    {
        [HarmonyPrefix]
        public static bool Prefix(TriggerBase __instance, BaseEntity ent)
        {
            if (__instance == null || ent == null) return true;
            if (!(__instance is TargetTrigger || __instance is TriggerEnterTimer)) return true;
            try { return TPVE.Dispatch_OnEntityEnter(__instance, ent) == null; }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnEntityEnter: " + ex.Message); return true; }
        }
    }

    /// <summary>OnNpcTarget (animals) - non-null zeroes the desire to attack (protects sleepers).</summary>
    [HarmonyPatch(typeof(BaseNpc), nameof(BaseNpc.GetWantsToAttack))]
    public static class Patch_BaseNpc_GetWantsToAttack
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseNpc __instance, BaseEntity target, ref float __result)
        {
            if (__instance == null || !(target is BasePlayer bp)) return true;
            try
            {
                if (TPVE.Dispatch_OnNpcTarget(__instance, bp) != null) { __result = 0f; return false; }
            }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnNpcTarget: " + ex.Message); }
            return true;
        }
    }
}
