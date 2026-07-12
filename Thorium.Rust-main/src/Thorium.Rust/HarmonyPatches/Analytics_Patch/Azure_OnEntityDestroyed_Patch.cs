using Facepunch.Rust;
using HarmonyLib;
using Thorium.Rust.Services;

namespace Thorium.Rust.HarmonyPatches.Analytics_Patch;

[HarmonyPatch(typeof(Analytics.Azure), "OnEntityDestroyed")]
public class Azure_OnEntityDestroyed_Patch
{
    [HarmonyPrefix]
    public static void OnEntityDestroyed(BaseEntity entity)
    {
        try
        {
            if (!DataHandler.IsConfigured) return;
            if (entity == null || entity.net == null) return;
            if (DataHandler.EntityCache.Length > DataHandler.MaxCacheSize) return;

            DataHandler.TotalEntityPackets++;
            var cache = DataHandler.EntityCache;

            ProtoBufManager.WriteBool(cache, false); // entityCreate = false
            ProtoBufManager.WriteInt64(cache, (long)entity.net.ID.Value);
        }
        catch
        {
        }
    }
}