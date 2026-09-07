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
    /// Normalizes the secret (comma / stray "Bearer " in the parameter). Duplicate wire headers are prevented by
    /// HttpClient_SendAsync_DedupeAuthorization_Patch (DefaultRequestHeaders vs per-request Authorization merge).
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
            var openMethod = AccessTools.Method(connectorType, "SendRequestImpl") as MethodInfo;
            if (openMethod == null || !openMethod.IsGenericMethodDefinition)
            {
                Debug.Log("[NexusSelfHost] TargetMethods: SendRequestImpl not found or not generic.");
                yield break;
            }

            var asm = connectorType.Assembly;
            var resolved = new List<MethodBase>();

            // Only patch known closed SendRequestImpl<T>. Do not scan asm.GetTypes(): some valid
            // MakeGenericMethod(T) targets still break Harmony/MonoMod (ImportGenericParameter / NotSupportedException).
            var typeNames = new[]
            {
                "Facepunch.Nexus.Models.ZoneDetails",
                "Facepunch.Nexus.Models.ZonePlayerDetails",
                "Facepunch.Nexus.Models.ZonePlayerLogin",
                "Facepunch.Nexus.Models.RegisterTransfersResponse",
                "Facepunch.Nexus.Models.CompleteTransfersResponse",
                "System.Int32"
            };
            foreach (var typeName in typeNames)
            {
                var t = asm.GetType(typeName, throwOnError: false) ?? AccessTools.TypeByName(typeName);
                if (t == null)
                    continue;
                try
                {
                    resolved.Add(openMethod.MakeGenericMethod(t));
                }
                catch
                {
                    /* type does not satisfy generic constraints */
                }
            }

            Debug.Log($"[NexusSelfHost] TargetMethods: patching {resolved.Count} SendRequestImpl<T> closed generic(s).");
            foreach (var m in resolved)
                yield return m;
        }

        /// <summary>Strip duplicate "..., Bearer ..." merge and accidental "Bearer " inside the parameter.</summary>
        internal static string NormalizeBearerSecretParameter(string parameter)
        {
            if (string.IsNullOrWhiteSpace(parameter))
                return parameter;
            var s = parameter.Trim();
            var comma = s.IndexOf(',');
            if (comma >= 0)
                s = s.Substring(0, comma).Trim();
            if (s.StartsWith("Bearer ", System.StringComparison.OrdinalIgnoreCase))
                s = s.Substring(7).Trim();
            return s.Length == 0 ? null : s;
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

            string rawForRequest = authToken;
            if (auth?.Parameter != null)
            {
                int prevLen = authToken?.Length ?? 0;
                string oldToken = authToken;
                rawForRequest = auth.Parameter;
                authToken = rawForRequest;
                if (debug)
                    Debug.Log("[NexusSelfHost DEBUG] SendRequestImpl PATCH: replaced authToken from \"" + (oldToken ?? "null") + "\" to raw from DefaultRequestHeaders (length " + rawForRequest.Length + ").");
                if (!_prefixLoggedOnce) { _prefixLoggedOnce = true; Debug.Log("[NexusSelfHost] Prefix ran: authToken from DefaultRequestHeaders (length=" + rawForRequest.Length + (prevLen != rawForRequest.Length ? ", replaced passed-in length=" + prevLen : "") + ")."); }
            }
            else if (string.IsNullOrEmpty(rawForRequest))
            {
                if (!_prefixLoggedOnce)
                {
                    _prefixLoggedOnce = true;
                    Debug.Log("[NexusSelfHost] Prefix ran but DefaultRequestHeaders.Authorization.Parameter was null (no default Bearer).");
                }
                if (debug)
                    Debug.Log("[NexusSelfHost DEBUG] SendRequestImpl Prefix EXIT: authToken=null");
                return;
            }

            var clean = NormalizeBearerSecretParameter(rawForRequest) ?? rawForRequest;
            if (clean != rawForRequest && debug)
                Debug.Log("[NexusSelfHost DEBUG] Normalized Bearer parameter (removed comma/duplicate prefix).");

            authToken = clean;
            // Do not set DefaultRequestHeaders.Authorization here: NexusZoneConnector ctor already sets it, and
            // SendRequestImpl sets per-request Authorization — merging produced duplicate "Bearer x, Bearer x" on the wire.
            // HttpClient_SendAsync_DedupeAuthorization_Patch clears defaults for the duration of SendAsync.

            if (debug)
                Debug.Log("[NexusSelfHost DEBUG] SendRequestImpl Prefix EXIT: authToken=\"" + (authToken ?? "null") + "\" length=" + (authToken?.Length ?? 0));
        }
    }
}
