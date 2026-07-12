using System;
using HarmonyLib;
using Thorium.Rust.Core;
using Thorium.Rust.HarmonyPatches.Utility;
using Thorium.Rust.Models;
using Thorium.Rust.Services;
using UnityEngine;

namespace Thorium.Rust.HarmonyPatches.BasePlayer_Patch;

[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die), typeof(HitInfo))]
internal static class BasePlayer_Die_Patch
{
    [HarmonyPrefix]
    private static void Prefix(BasePlayer __instance, HitInfo info)
    {
        try
        {
            if (!DataHandler.IsConfigured) return;

            var victimId = Helpers.GetSteamIdUlongOrZero(__instance);
            if (info == null || __instance == null || victimId == 0UL || __instance.IsSleeping()) return;

            var initiator = info.InitiatorPlayer;
            var initiatorId = Helpers.GetSteamIdUlongOrZero(initiator);
            if (initiator == null || initiatorId == 0UL) return;

            var projectileId = info.ProjectileID;

            var activeItem = initiator.GetActiveItem();
            if (DataHandler.PvpCache.Length > DataHandler.MaxCacheSize) return;

            DataHandler.TotalPvpPackets++;
            var pvpCache = DataHandler.PvpCache;
            var weaponShort = activeItem == null ? "None" : activeItem.info.shortname;
            var action = initiator == __instance ? "Suicide" : weaponShort;
            var distance = Vector3.Distance(__instance.transform.position,
                initiator.transform.position);

            var boneName = "N/A";
            try
            {
                var skeleton = __instance.skeletonProperties;
                var bone = skeleton?.FindBone(info.HitBone)?.boneName;
                if (!string.IsNullOrEmpty(bone)) boneName = bone;
            }
            catch
            {
            }

            ProtoBufManager.WriteString(pvpCache, initiator.UserIDString);
            ProtoBufManager.WriteString(pvpCache, initiator.displayName);
            ProtoBufManager.WriteString(pvpCache, __instance.UserIDString);
            ProtoBufManager.WriteString(pvpCache, __instance.displayName);
            ProtoBufManager.WriteString(pvpCache, action);
            ProtoBufManager.WriteString(pvpCache, boneName);
            ProtoBufManager.WriteSingle(pvpCache, distance);
            ProtoBufManager.WriteVector(pvpCache, __instance.transform.position);
            ProtoBufManager.WriteZInt32(pvpCache, projectileId);

            try
            {
                var steamId = unchecked((long)victimId);
                var pos = __instance.transform.position;
                var snapshot = PlayerSnapshot.Create(pos, __instance, SnapshotTypeEnums.Die,
                    CombatData.FromPlayer(initiator), __instance.estimatedVelocity, __instance.IsOnGround());

                AntiCheatSnapshotProcessor.Enqueue(steamId, snapshot);
            }
            catch
            {
            }
        }
        catch (Exception ex)
        {
            Log.Error("Error in Die patch: " + ex);
        }
    }
}