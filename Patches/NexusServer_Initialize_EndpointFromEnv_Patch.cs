using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NexusSelfHost.Patches
{
    /// <summary>
    /// Nexus initializes before command-line ConVars may be fully applied. If nexus.endpoint
    /// is still the Facepunch default, set it from NEXUS_ENDPOINT so the self-hosted API is used.
    /// Set env NEXUS_DEBUG=1 for verbose console logs (turn off once handshake works).
    /// </summary>
    [HarmonyPatch]
    public static class NexusServer_Initialize_EndpointFromEnv_Patch
    {
        private const string FacepunchDefault = "https://api.facepunch.com/api/nexus/";
        private static bool _logged;

        internal static bool DebugEnabled => !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("NEXUS_DEBUG"));

        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("NexusServer");
            if (t == null)
            {
                Debug.Log("[NexusSelfHost] NexusServer type not found, skipping endpoint-from-env patch.");
                return null;
            }
            var m = AccessTools.Method(t, "Initialize");
            if (m == null) return null;
            if (!_logged) { _logged = true; Debug.Log("[NexusSelfHost] Patching NexusServer.Initialize to apply NEXUS_ENDPOINT when endpoint is default. Set NEXUS_DEBUG=1 for verbose logs."); }
            return m;
        }

        static void Prefix()
        {
            var nexusType = AccessTools.TypeByName("ConVar.Nexus");
            if (nexusType == null) return;
            var endpointField = AccessTools.Field(nexusType, "endpoint");
            var secretKeyField = AccessTools.Field(nexusType, "secretKey");
            if (endpointField == null) return;

            string currentEndpoint = (string)endpointField.GetValue(null);
            string currentSecret = secretKeyField != null ? (string)secretKeyField.GetValue(null) : null;
            if (DebugEnabled)
                Debug.Log("[NexusSelfHost DEBUG] ConVar BEFORE: nexus.endpoint=\"" + (currentEndpoint ?? "null") + "\" length=" + (currentEndpoint?.Length ?? 0)
                    + ", nexus.secretKey=\"" + (currentSecret ?? "null") + "\" length=" + (currentSecret?.Length ?? 0));

            string endpointEnv = System.Environment.GetEnvironmentVariable("NEXUS_ENDPOINT");
            bool applyEndpoint = !string.IsNullOrWhiteSpace(endpointEnv) &&
                (string.IsNullOrWhiteSpace(currentEndpoint) ||
                 string.Equals(currentEndpoint.TrimEnd('/'), FacepunchDefault.TrimEnd('/'), System.StringComparison.OrdinalIgnoreCase) ||
                 currentEndpoint.IndexOf("facepunch.com", System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (applyEndpoint)
            {
                string env = endpointEnv.TrimEnd('/');
                if (!env.StartsWith("http", System.StringComparison.OrdinalIgnoreCase))
                    env = "http://" + env;
                endpointField.SetValue(null, env + "/");
                Debug.Log("[NexusSelfHost] Set nexus.endpoint from NEXUS_ENDPOINT: " + env + "/");
            }
            if (DebugEnabled)
            {
                string afterEndpoint = (string)endpointField.GetValue(null);
                string afterSecret = secretKeyField != null ? (string)secretKeyField.GetValue(null) : null;
                Debug.Log("[NexusSelfHost DEBUG] ConVar AFTER: nexus.endpoint=\"" + (afterEndpoint ?? "null") + "\", nexus.secretKey=\"" + (afterSecret ?? "null") + "\" length=" + (afterSecret?.Length ?? 0));
            }
        }
    }
}
