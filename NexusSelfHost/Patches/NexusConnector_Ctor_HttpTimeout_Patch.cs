using System;
using System.Net.Http;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NexusSelfHost.Patches
{
    /// <summary>
    /// Zone RPC posts large bodies to /zone/message via HttpClient. Under load (save, slow API, hairpin NAT)
    /// the default timeout can cancel SendAsync and surface TaskCanceledException in BroadcastPlayerManifest /
    /// RefreshZoneStatus even with no players. Bump timeout once per connector instance.
    /// </summary>
    [HarmonyPatch]
    public static class NexusConnector_Ctor_HttpTimeout_Patch
    {
        private static bool _loggedOnce;

        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("Facepunch.Nexus.Connector.NexusConnector");
            var loggerType = AccessTools.TypeByName("Facepunch.Nexus.Logging.INexusLogger");
            if (t == null || loggerType == null)
                return null;
            return AccessTools.Constructor(t, new[] { loggerType, typeof(string) });
        }

        static void Postfix(object __instance)
        {
            FieldInfo field = null;
            for (var type = __instance.GetType(); type != null; type = type.BaseType)
            {
                field = type.GetField("HttpClient", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    break;
            }
            if (field?.GetValue(__instance) is not HttpClient client)
                return;
            try
            {
                client.Timeout = TimeSpan.FromMinutes(5);
            }
            catch (Exception ex)
            {
                if (!_loggedOnce)
                {
                    _loggedOnce = true;
                    Debug.Log("[NexusSelfHost] NexusConnector HttpClient.Timeout could not be set: " + ex.Message);
                }
            }
        }
    }
}
