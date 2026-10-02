// Kept livestock: leading, milking, shearing, familiarity, and refuse-target.
// Damage rules do not override these. Hooks are subscribed by ValidateCurrentDamageHook.
using HarmonyLib;
using Rust.Ai.Gen2;
using UnityEngine;
using TPVE = Harmony.Plugins.TruePVE;

namespace TruePVEHarmony.Patches
{
    [HarmonyPatch(typeof(LivestockAnimal), nameof(LivestockAnimal.TryLead))]
    public static class Patch_LivestockAnimal_TryLead
    {
        [HarmonyPrefix]
        public static bool Prefix(LivestockAnimal __instance, BasePlayer player)
        {
            if (player == null) return true;
            try
            {
                object result = TPVE.Dispatch_OnEntityDistanceCheck(__instance, player);
                return !(result is false);
            }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnEntityDistanceCheck: " + ex.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(Cow), nameof(Cow.TryMilk))]
    public static class Patch_Cow_TryMilk
    {
        [HarmonyPrefix]
        public static bool Prefix(Cow __instance, BasePlayer player)
        {
            if (player == null) return true;
            try
            {
                object result = TPVE.Dispatch_OnEntityVisibilityCheck(__instance, player);
                return !(result is false);
            }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnEntityVisibilityCheck: " + ex.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(Sheep), nameof(Sheep.OnAttacked))]
    public static class Patch_Sheep_OnAttacked
    {
        [HarmonyPrefix]
        public static bool Prefix(Sheep __instance, HitInfo info)
        {
            BasePlayer player = info?.InitiatorPlayer;
            if (player == null) return true;
            try
            {
                object result = TPVE.Dispatch_OnLivestockShear(player, info);
                return !(result is true);
            }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnLivestockShear: " + ex.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(LivestockAnimal), nameof(LivestockAnimal.AddFamiliarity))]
    public static class Patch_LivestockAnimal_AddFamiliarity
    {
        [HarmonyPrefix]
        public static bool Prefix(LivestockAnimal __instance, ulong userId, float seconds, LivestockAnimal.FamiliarityReason reason)
        {
            try
            {
                object result = TPVE.Dispatch_OnLivestockAnimalFamiliarityAdd(__instance, userId, seconds, reason);
                return result == null;
            }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnLivestockAnimalFamiliarityAdd: " + ex.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(LivestockAnimal), nameof(LivestockAnimal.RefusesToTarget))]
    public static class Patch_LivestockAnimal_RefusesToTarget
    {
        [HarmonyPrefix]
        public static bool Prefix(LivestockAnimal __instance, BaseEntity entity, ref bool __result)
        {
            if (entity is not BasePlayer target) return true;
            try
            {
                object result = TPVE.Dispatch_CanLivestockAnimalRefuseTarget(__instance, target);
                if (result is not bool refuse) return true;
                __result = refuse;
                return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] CanLivestockAnimalRefuseTarget: " + ex.Message); return true; }
        }
    }
}
