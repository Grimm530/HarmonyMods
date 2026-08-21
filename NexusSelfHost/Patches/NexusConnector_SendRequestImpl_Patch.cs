using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NexusSelfHost.Patches
{
    /// <summary>
    /// When the game calls GetZoneDetails() it uses GetRequest(url) with no authToken.
    /// SendRequestImpl then sets request.Headers.Authorization = null, which overrides
    /// the HttpClient.DefaultRequestHeaders.Authorization set in NexusZoneConnector's ctor.
    /// Prefix: if authToken is null, use the connector's HttpClient.DefaultRequestHeaders.Authorization.Parameter.
    /// Set env NEXUS_DEBUG=1 for verbose console logs (turn off once handshake works).
    /// </summary>
    [HarmonyPatch]
    public static class NexusConnector_SendRequestImpl_Patch
    {
        private static bool _prefixLoggedOnce;
        private static int _prefixCallCount;

        static IEnumerable<MethodBase> TargetMethods()
        {
            var connectorType = AccessTools.TypeByName("Facepunch.Nexus.Connector.NexusConnector");
            if (connectorType == null)
            {
                Debug.Log("[NexusSelfHost] TargetMethods: NexusConnector type not found.");
                yield break;
            }
            var openMethod = AccessTools.Method(connectorType, "SendRequestImpl");
            if (openMethod == null || !openMethod.IsGenericMethodDefinition)
            {
                Debug.Log("[NexusSelfHost] TargetMethods: SendRequestImpl not found or not generic.");
                yield break;
            }
            var asm = connectorType.Assembly;
            var typeNames = new[] {
                "Facepunch.Nexus.Models.ZoneDetails",
                "Facepunch.Nexus.Models.ZonePlayerDetails",
                "Facepunch.Nexus.Models.ZonePlayerLogin",
                "Facepunch.Nexus.Models.RegisterTransfersResponse",
                "Facepunch.Nexus.Models.CompleteTransfersResponse"
            };
            var resolved = new List<MethodBase>();
            foreach (var typeName in typeNames)
            {
                var t = asm.GetType(typeName, throwOnError: false)
                    ?? AccessTools.TypeByName(typeName);
                if (t != null)
                {
                    var closed = ((MethodInfo)openMethod).MakeGenericMethod(t);
                    resolved.Add(closed);
                }
            }
            Debug.Log($"[NexusSelfHost] TargetMethods: patching {resolved.Count} closed generic(s): " +
                string.Join(", ", resolved.ConvertAll(m => (m.DeclaringType?.Name ?? "?") + "." + m.Name)));
            foreach (var m in resolved)
                yield return m;
        }

        static void Prefix(object __instance, ref string authToken)
        {
            bool debug = NexusServer_Initialize_EndpointFromEnv_Patch.DebugEnabled;
            _prefixCallCount++;
            if (debug)
                Debug.Log("[NexusSelfHost DEBUG] SendRequestImpl Prefix call #" + _prefixCallCount + " ENTRY: authToken=" + (authToken == null ? "null" : "\"" + authToken + "\" length=" + authToken.Length));

            var type = __instance?.GetType();
            System.Reflection.FieldInfo field = null;
            while (type != null)
            {
                field = type.GetField("HttpClient", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field != null)
                    break;
                type = type.BaseType;
            }
            if (field == null)
            {
                if (!_prefixLoggedOnce) { _prefixLoggedOnce = true; Debug.Log("[NexusSelfHost] Prefix ran but HttpClient field not found (instance=" + (__instance?.GetType().Name ?? "null") + ")."); }
                return;
            }
            if (!(field.GetValue(__instance) is HttpClient client))
            {
                if (!_prefixLoggedOnce) { _prefixLoggedOnce = true; Debug.Log("[NexusSelfHost] Prefix ran but GetValue(HttpClient) was null or not HttpClient."); }
                return;
            }
            var auth = client.DefaultRequestHeaders?.Authorization;
            if (debug)
                Debug.Log("[NexusSelfHost DEBUG] DefaultRequestHeaders.Authorization: Scheme=\"" + (auth?.Scheme ?? "null") + "\", Parameter=" + (auth?.Parameter == null ? "null" : "\"" + auth.Parameter + "\" length=" + auth.Parameter.Length));

            if (auth?.Parameter != null)
            {
                int prevLen = authToken?.Length ?? 0;
                string oldToken = authToken;
                authToken = auth.Parameter;
                if (debug)
                    Debug.Log("[NexusSelfHost DEBUG] SendRequestImpl PATCH: replaced authToken from \"" + (oldToken ?? "null") + "\" to \"" + authToken + "\" (length " + authToken.Length + "). Game will set request.Headers.Authorization = Bearer " + authToken.Length + " chars.");
                if (!_prefixLoggedOnce) { _prefixLoggedOnce = true; Debug.Log("[NexusSelfHost] Prefix ran: authToken from DefaultRequestHeaders (length=" + authToken.Length + (prevLen != authToken.Length ? ", replaced passed-in length=" + prevLen : "") + ")."); }
            }
            else if (!_prefixLoggedOnce)
            {
                _prefixLoggedOnce = true;
                Debug.Log("[NexusSelfHost] Prefix ran but DefaultRequestHeaders.Authorization.Parameter was null (no default Bearer).");
            }
            if (debug)
                Debug.Log("[NexusSelfHost DEBUG] SendRequestImpl Prefix EXIT: authToken=\"" + (authToken ?? "null") + "\" length=" + (authToken?.Length ?? 0));
        }
    }
}
