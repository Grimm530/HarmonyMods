using System;
using System.IO;
using HarmonyLib;
using Thorium.Rust.Core;
using Thorium.Rust.HarmonyPatches.Utility;
using Thorium.Rust.Models;
using Thorium.Rust.Services;
using UnityEngine;

namespace Thorium.Rust.HarmonyPatches.BasePlayer_Patch;

[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Hurt), typeof(HitInfo))]
internal static class BasePlayer_Hurt_Patch
{
    [HarmonyPrefix]
    private static void Prefix(BasePlayer __instance, HitInfo info)
    {
        try
        {
            if (!DataHandler.IsConfigured) return;

            var victimId = Helpers.GetSteamIdOrZero(__instance);
            if (victimId == 0L) return;

            var initiator = info.InitiatorPlayer;

            if (initiator == null)
            {
                var majority = (int)info.damageTypes.GetMajorityDamageType();
                if (majority != 10 && majority != 15) return;

                try
                {
                    if (DataHandler.PacketCache.Length <= DataHandler.MaxCacheSize)
                    {
                        DataHandler.TotalPackets++;
                        var packetCache = DataHandler.PacketCache;
                        ProtoBufManager.WriteZInt32(packetCache, 7);
                        ProtoBufManager.WriteString(packetCache, __instance.UserIDString);
                        ProtoBufManager.WriteSingle(packetCache, Time.time);

                        try
                        {
                            var pos = __instance.transform.position;
                            AntiCheatSnapshotProcessor.Enqueue(victimId,
                                PlayerSnapshot.Create(pos, __instance, SnapshotTypeEnums.HurtEnv,
                                    new CombatData { IsAiming = false, IsAttacking = false }));
                        }
                        catch
                        {
                        }
                    }
                }
                catch (IOException)
                {
                    DataHandler.PacketCache.SetLength(0);
                    DataHandler.PacketCache.Position = 0;
                    DataHandler.TotalPackets = 0;
                }

                return;
            }

            if (initiator == __instance) return;

            try
            {
                if (info.Weapon == null) return;

                var initiatorId = Helpers.GetSteamIdOrZero(initiator);
                if (initiatorId == 0L) return;


                if (info.Weapon.GetItem() == null) return;
                if (DataHandler.DamageCache.Length > DataHandler.MaxCacheSize) return;

                DataHandler.TotalDamagePackets++;
                var damageCache = DataHandler.DamageCache;
                var itemid = 0;
                var weaponShortname = string.Empty;
                var isProjectile = false;
                var boneName = string.Empty;

                try
                {
                    var weapon = info.Weapon;
                    var weaponItem = weapon?.GetItem();
                    if (weaponItem != null)
                        weaponShortname = weaponItem.info.shortname;

                    var proj = weapon as BaseProjectile;
                    if (proj != null)
                    {
                        itemid = proj.PrimaryMagazineAmmo.itemid;
                        isProjectile = true;
                    }

                    if (info.HitBone != 0)
                    {
                        var skeleton = __instance.skeletonProperties;
                        var bone = skeleton?.FindBone(info.HitBone);
                        if (bone != null)
                            boneName = bone.boneName ?? string.Empty;
                    }
                }
                catch
                {
                }

                ProtoBufManager.WriteInt64(damageCache, (long)(Time.time * 1000));
                ProtoBufManager.WriteInt64(damageCache, initiatorId);
                ProtoBufManager.WriteInt64(damageCache, victimId);
                ProtoBufManager.WriteZInt32(damageCache, itemid);
                ProtoBufManager.WriteSingle(damageCache, info.damageTypes.Total());
                ProtoBufManager.WriteSingle(damageCache, __instance.health);
                ProtoBufManager.WriteZInt32(damageCache, info.ProjectileID);
                ProtoBufManager.WriteSingle(damageCache, initiator.Distance(__instance.transform.position));
                ProtoBufManager.WriteString(damageCache, weaponShortname);
                ProtoBufManager.WriteBool(damageCache, isProjectile);
                ProtoBufManager.WriteBool(damageCache, info.isHeadshot);
                ProtoBufManager.WriteString(damageCache, boneName);
                ProtoBufManager.WriteVector(damageCache, info.HitPositionWorld);

                if (isProjectile)
                {
                    ProtoBufManager.WriteVector(damageCache, info.ProjectileVelocity);
                    ProtoBufManager.WriteSingle(damageCache, info.ProjectileDistance);
                }
                else
                {
                    ProtoBufManager.WriteVector(damageCache, Vector3.zero);
                    ProtoBufManager.WriteSingle(damageCache, 0f);
                }

                // build and enqueue snapshot representing this damage event
                try
                {
                    var pos = __instance.transform.position;
                    var snapshot = PlayerSnapshot.Create(pos, __instance, SnapshotTypeEnums.Hurt,
                        CombatData.FromPlayer(initiator), __instance.estimatedVelocity, __instance.IsOnGround());

                    AntiCheatSnapshotProcessor.Enqueue(victimId, snapshot);
                }
                catch
                {
                }

            }
            catch (IOException)
            {
                DataHandler.DamageCache.SetLength(0);
                DataHandler.DamageCache.Position = 0;
                DataHandler.TotalDamagePackets = 0;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Error in Hurt patch: " + ex);
        }
    }
}