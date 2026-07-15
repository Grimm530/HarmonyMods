using System;
using HarmonyLib;
using UnityEngine;

namespace ShopHarmony.Patches
{
    /// <summary>Block horse claim (hitching-post / for-sale) when player is at owned-horse limit.</summary>
    [HarmonyPatch(typeof(RidableHorse), nameof(RidableHorse.SERVER_Claim))]
    public static class RidableHorse_SERVER_Claim_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(RidableHorse __instance, BaseEntity.RPCMessage msg)
        {
            try
            {
                var limiter = ShopHarmonyMod.Instance?.Plugin?.HorseLimiter;
                if (limiter == null || !limiter.Enabled) return true;
                BasePlayer player = msg.player;
                return limiter.AllowClaim(__instance, player);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Shop Horse] SERVER_Claim prefix: " + ex.Message);
                return true;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(RidableHorse __instance, BaseEntity.RPCMessage msg)
        {
            try
            {
                var limiter = ShopHarmonyMod.Instance?.Plugin?.HorseLimiter;
                if (limiter == null || !limiter.Enabled) return;
                BasePlayer player = msg.player;
                if (player == null || __instance == null || __instance.IsDestroyed) return;
                // Successful claim clears IsForSale (Reserved2).
                if (!__instance.IsForSale)
                    limiter.OnClaimed(__instance, player);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Shop Horse] SERVER_Claim postfix: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    public static class Horse_BaseNetworkable_Spawn_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseNetworkable __instance)
        {
            if (__instance is not RidableHorse horse) return;
            try
            {
                ShopHarmonyMod.Instance?.Plugin?.HorseLimiter?.OnHorseSpawned(horse);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Shop Horse] OnSpawn: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill),
        new[] { typeof(BaseNetworkable.DestroyMode), typeof(bool) })]
    public static class Horse_BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseNetworkable __instance)
        {
            if (__instance is not RidableHorse horse) return;
            try
            {
                ShopHarmonyMod.Instance?.Plugin?.HorseLimiter?.OnHorseKilled(horse);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Shop Horse] OnKill: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BaseMountable), nameof(BaseMountable.MountPlayer), new[] { typeof(BasePlayer) })]
    public static class Horse_BaseMountable_MountPlayer_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseMountable __instance, BasePlayer player)
        {
            if (__instance == null) return;
            try
            {
                var horse = __instance.GetComponentInParent<RidableHorse>();
                if (horse != null)
                    ShopHarmonyMod.Instance?.Plugin?.HorseLimiter?.OnHorseMounted(horse);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Shop Horse] OnMount: " + ex.Message);
            }
        }
    }
}
