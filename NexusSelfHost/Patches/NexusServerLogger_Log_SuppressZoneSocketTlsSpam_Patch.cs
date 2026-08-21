using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NexusSelfHost.Patches
{
    /// <summary>
    /// Stops the "Lost connection to Nexus zone socket" + TlsException from being written to the log.
    /// The ReconnectLoop logs this every ~5s with a full exception (100+ lines). That goes to
    /// NexusServerLogger.Log → Debug.LogError + Debug.LogException, hammering disk I/O 24/7.
    /// We suppress at the logger so nothing is written. Also suppress repeated "Connecting to nexus socket...".
    /// </summary>
    [HarmonyPatch]
    public static class NexusServerLogger_Log_SuppressZoneSocketTlsSpam_Patch
    {
        private static bool _loggedTlsOnce;
        private static bool _loggedConnectingOnce;

        private static bool IsZoneSocketTlsError(string message, Exception exception)
        {
            if (string.IsNullOrEmpty(message) || !message.StartsWith("Lost connection to Nexus zone socket", StringComparison.Ordinal))
                return false;
            if (exception == null) return true;
            var ex = exception;
            while (ex != null)
            {
                var msg = ex.Message ?? "";
                if (msg.IndexOf("UNITYTLS_X509VERIFY_FLAG_NOT_TRUSTED", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("Handshake failed", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                ex = ex.InnerException;
            }
            return false;
        }

        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("NexusServerLogger");
            if (t == null) return null;
            var levelType = AccessTools.TypeByName("Facepunch.Nexus.Logging.NexusLogLevel");
            if (levelType != null)
            {
                var m = AccessTools.Method(t, "Log", new[] { levelType, typeof(string), typeof(Exception) });
                if (m != null) return m;
            }
            foreach (var mi in t.GetMethods())
                if (mi.Name == "Log" && mi.GetParameters().Length == 3
                    && mi.GetParameters()[1].ParameterType == typeof(string)
                    && mi.GetParameters()[2].ParameterType == typeof(Exception))
                    return mi;
            return null;
        }

        static bool Prefix(object __instance, object level, string message, Exception exception)
        {
            if (message == null) return true;

            // Suppress repeated "Connecting to nexus socket..." (every 5s from ReconnectLoop)
            if (message == "Connecting to nexus socket...")
            {
                if (_loggedConnectingOnce) return false;
                _loggedConnectingOnce = true;
                return true;
            }

            if (!IsZoneSocketTlsError(message, exception)) return true;

            if (!_loggedTlsOnce)
            {
                _loggedTlsOnce = true;
                Debug.Log("[NexusSelfHost] Zone WebSocket TLS handshake failed (self-signed cert). Suppressing repeated log spam. HTTP API still works.");
            }
            return false; // skip original Log → no Debug.LogError, no Debug.LogException, no I/O
        }
    }
}
