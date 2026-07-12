using System.Reflection;
using HarmonyLib;
using Rust;
using UnityEngine;

namespace Convoy.Patches
{
    /// <summary>
    /// Stock seats / BaseVehicle.GetDismountPosition fail for convoy NPCs on kinematic vehicles,
    /// which ends in Hurt(Suicide). Patch both entry points and block Suicide as a last resort.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_BaseMountable_GetDismountPosition
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(BaseMountable), nameof(BaseMountable.GetDismountPosition),
                new[] { typeof(BasePlayer), typeof(Vector3).MakeByRefType(), typeof(bool) });

        [HarmonyPrefix]
        public static bool Prefix(BasePlayer player, ref Vector3 res, ref bool __result)
        {
            if (!ConvoyDismountGuard.IsConvoyNpc(player)) return true;
            res = player.transform.position;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch]
    public static class Patch_BaseVehicle_GetDismountPosition
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(BaseVehicle), nameof(BaseVehicle.GetDismountPosition),
                new[] { typeof(BasePlayer), typeof(Vector3).MakeByRefType(), typeof(bool) });

        [HarmonyPrefix]
        public static bool Prefix(BasePlayer player, ref Vector3 res, ref bool __result)
        {
            if (!ConvoyDismountGuard.IsConvoyNpc(player)) return true;
            res = player.transform.position;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(BaseMountable), nameof(BaseMountable.DismountPlayer), new[] { typeof(BasePlayer), typeof(bool) })]
    public static class Patch_BaseMountable_DismountPlayer
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseMountable __instance, BasePlayer player, bool lite)
        {
            if (!ConvoyDismountGuard.IsConvoyNpc(player)) return true;

            // Intentional combat/cleanup dismount: allow lite path through.
            if (lite || ConvoyDismountGuard.AllowCombatDismount)
                return true;

            // Accidental hard dismount while seated on a moving convoy → soft dismount instead of Suicide.
            __instance.DismountPlayer(player, true);
            return false;
        }
    }

    /// <summary>Last resort: never let the invalid-dismount Suicide path kill a convoy NPC.</summary>
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    public static class Patch_BaseCombatEntity_Hurt_BlockConvoySuicide
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            if (info?.damageTypes == null) return true;
            if (info.damageTypes.Get(DamageType.Suicide) <= 0f) return true;
            if (!ConvoyDismountGuard.IsConvoyNpc(__instance as BasePlayer)) return true;
            return false;
        }
    }

    public static class ConvoyDismountGuard
    {
        /// <summary>When true, intentional RoamAllNpc / cleanup dismounts are allowed through.</summary>
        public static bool AllowCombatDismount;

        public static bool IsConvoyNpc(BasePlayer player)
        {
            if (player == null) return false;
            if (player.skinID == ConvoyGrimmNpc.CustomNpcSkinId) return true;
            if (player.net != null && ConvoyGrimmNpc.IsConvoyNpc((ulong)player.net.ID.Value)) return true;
            return false;
        }
    }
}
