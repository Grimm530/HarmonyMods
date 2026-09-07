using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using HarmonyLib;

namespace NexusSelfHost.Patches
{
    /// <summary>
    /// Facepunch NexusConnector sets HttpClient.DefaultRequestHeaders.Authorization in NexusZoneConnector's ctor
    /// and SendRequestImpl also sets HttpRequestMessage.Headers.Authorization when authToken is set.
    /// HttpClient merges both into a single wire header: "Bearer secret, Bearer secret", which confuses operators
    /// and triggers Nexus API DebugAuth warnings. For each SendAsync, temporarily clear DefaultRequestHeaders
    /// Authorization when the request already carries Authorization, then restore after SendAsync returns its Task.
    /// The merge for the outbound message happens synchronously at the start of SendAsync, before the returned Task runs.
    /// </summary>
    [HarmonyPatch]
    public static class HttpClient_SendAsync_DedupeAuthorization_Patch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(HttpClient), nameof(HttpClient.SendAsync), new[] { typeof(HttpRequestMessage), typeof(CancellationToken) });
        }

        static void Prefix(HttpClient __instance, HttpRequestMessage request, CancellationToken cancellationToken, ref object __state)
        {
            __state = null;
            try
            {
                if (request?.Headers?.Authorization == null)
                    return;
                var def = __instance.DefaultRequestHeaders?.Authorization;
                if (def == null)
                    return;
                __state = def;
                __instance.DefaultRequestHeaders.Authorization = null;
            }
            catch
            {
                __state = null;
            }
        }

        static void Postfix(HttpClient __instance, ref object __state)
        {
            if (__state is not AuthenticationHeaderValue ahv)
                return;
            try
            {
                __instance.DefaultRequestHeaders.Authorization = ahv;
            }
            catch
            {
                /* ignore */
            }
            finally
            {
                __state = null;
            }
        }
    }
}
