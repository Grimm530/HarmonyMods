using System;
using System.Reflection;
using HarmonyLib;
using NexusSelfHost;
using UnityEngine;

namespace NexusSelfHost.Patches
{
    /// <summary>
    /// <see cref="BasePlayer.PersistantPlayerInfo"/> caches the result of <see cref="UserPersistance.GetPlayerInfo"/>.
    /// For Nexus, blueprints come from <c>NexusPlayer</c> variables; that cache is never cleared on disconnect
    /// (<c>OnDisconnected</c> does not null it). Waking a sleeper reuses the same <c>BasePlayer</c>, so a bad
    /// first load (or pre-Nexus-login timing) sticks until the entity is destroyed. Invalidate at
    /// <c>PlayerInit</c> so the first snapshot after each connection refetches from Nexus/sqlite.
    /// Opt out: <c>NEXUS_SKIP_PERSIST_CACHE_INVALIDATE=1</c>.
    /// <para/>
    /// <b>Postfix</b>: after vanilla <c>PlayerInit</c> (which already called <c>SendAsSnapshot</c>), invalidate again,
    /// force-load <c>PersistantPlayerInfo</c> from Nexus, and send a second <c>SendAsSnapshot</c> so the client’s
    /// copy of <c>persistantData</c> (unlocked blueprints) matches the server. Without this, some restarts / ordering
    /// left the UI empty even when <c>NexusPlayer</c> had a valid <c>blueprints.12</c> blob.
    /// Opt out: <c>NEXUS_SKIP_BLUEPRINT_SNAPSHOT_RESYNC=1</c>.
    /// </summary>
    public static class BasePlayer_PlayerInit_InvalidatePersistCachePatch
    {
        public static void Prefix(object __instance)
        {
            if (NexusSelfHostOptions.SkipPersistCacheInvalidate)
                return;

            var nexusType = AccessTools.TypeByName("NexusServer");
            if (nexusType == null) return;

            var startedProp = nexusType.GetProperty("Started", BindingFlags.Public | BindingFlags.Static);
            if (startedProp?.GetValue(null) is not bool started || !started)
                return;


            var invalidate = AccessTools.Method(__instance.GetType(), "InvalidateCachedPeristantPlayer");
            if (invalidate == null)
            {
                Debug.LogWarning("[NexusSelfHost] PlayerInit persist invalidate: InvalidateCachedPeristantPlayer not found.");
                return;
            }

            try
            {
                invalidate.Invoke(__instance, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NexusSelfHost] PlayerInit persist invalidate failed: " + ex.Message);
            }
        }

        public static void Postfix(object __instance)
        {
            if (NexusSelfHostOptions.SkipBlueprintSnapshotResync)
                return;

            var nexusType = AccessTools.TypeByName("NexusServer");
            if (nexusType == null) return;

            var startedProp = nexusType.GetProperty("Started", BindingFlags.Public | BindingFlags.Static);
            if (startedProp?.GetValue(null) is not bool started || !started)
                return;


            try
            {
                var netProp = AccessTools.Property(__instance.GetType(), "net");
                var net = netProp?.GetValue(__instance);
                if (net == null) return;

                var connProp = AccessTools.Property(net.GetType(), "connection");
                var conn = connProp?.GetValue(net);
                if (conn == null) return;

                var invalidate = AccessTools.Method(__instance.GetType(), "InvalidateCachedPeristantPlayer");
                invalidate?.Invoke(__instance, null);

                var ppi = AccessTools.Property(__instance.GetType(), "PersistantPlayerInfo");
                _ = ppi?.GetValue(__instance);

                var sendAsSnapshot = AccessTools.Method(__instance.GetType(), "SendAsSnapshot", new[] { conn.GetType() });
                if (sendAsSnapshot == null)
                {
                    Debug.LogWarning("[NexusSelfHost] PlayerInit blueprint resync: SendAsSnapshot(Connection) not found.");
                    return;
                }

                sendAsSnapshot.Invoke(__instance, new[] { conn });

                if (NexusSelfHostOptions.LogPlayerInitResync)
                    Debug.Log("[NexusSelfHost] PlayerInit: persist reloaded from Nexus + second SendAsSnapshot (blueprint handshake resync).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NexusSelfHost] PlayerInit blueprint snapshot resync failed: " + ex.Message);
            }
        }
    }
}
