using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NexusSelfHost.Patches
{
    /// <summary>
    /// When the Nexus zone WebSocket (WSS) is down (API restart drops connections; self-signed cert; slow reconnect),
    /// NexusZoneClient.Update() runs every ~30s and calls DispatchError("Socket is not connected for zone X"),
    /// which spams the log. The zone socket is only for real-time push (messages from Nexus to the server);
    /// HTTP (zone/info, zone/map, zone/variables, etc.) still works. We suppress this specific error so
    /// the log is not filled with repeated exceptions.
    /// </summary>
    [HarmonyPatch]
    public static class BaseNexusClient_DispatchError_SuppressSocketNotConnected_Patch
    {
        private static bool _loggedOnce;

        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("Facepunch.Nexus.BaseNexusClient");
            if (t == null) return null;
            var m = AccessTools.Method(t, "DispatchError", new[] { typeof(Exception) });
            return m;
        }

        static bool Prefix(Exception exception)
        {
            if (exception?.Message == null) return true;
            if (!exception.Message.StartsWith("Socket is not connected for zone ", StringComparison.Ordinal))
                return true;

            if (!_loggedOnce)
            {
                _loggedOnce = true;
                Debug.Log("[NexusSelfHost] Zone WebSocket is not connected (e.g. Nexus API just restarted, WSS still reconnecting, or self-signed TLS rejected for wss). Suppressing repeated 'Socket is not connected' errors. HTTP (zone/info, map, variables) still works.");
            }
            return false; // skip original DispatchError
        }
    }
}
