using Facepunch.Rust;
using HarmonyLib;
using Thorium.Rust.Services;
using UnityEngine;

namespace Thorium.Rust.HarmonyPatches.Analytics_Patch;

[HarmonyPatch(typeof(Analytics.Azure), "OnEntityBuilt")]
public class Azure_OnEntityBuilt_Patch
{
    [HarmonyPrefix]
    public static void Prefix(BaseEntity entity, BasePlayer player)
    {
        try
        {
            if (!DataHandler.IsConfigured) return;
            if (entity == null || entity.net == null || player == null) return;
            if (DataHandler.EntityCache.Length > DataHandler.MaxCacheSize) return;

            DataHandler.TotalEntityPackets++;
            var cache = DataHandler.EntityCache;

            ProtoBufManager.WriteBool(cache, true); // entityCreate = true
            ProtoBufManager.WriteInt64(cache, (long)entity.net.ID.Value);
            ProtoBufManager.WriteString(cache, player.UserIDString ?? string.Empty);
            ProtoBufManager.WriteUint(cache, entity.prefabID);
            ProtoBufManager.WriteString(cache, entity.ShortPrefabName ?? string.Empty);
            ProtoBufManager.WriteVector(cache, entity.ServerPosition);
            ProtoBufManager.WriteVector(cache, entity.ServerRotation.eulerAngles);
            ProtoBufManager.WriteVector(cache, entity.CenterPoint());
            ProtoBufManager.WriteVector(cache, entity.bounds.extents);
        }
        catch
        {
        }
    }
}