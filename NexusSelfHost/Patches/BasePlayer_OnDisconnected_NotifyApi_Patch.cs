using System;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace NexusSelfHost.Patches
{
    /// <summary>
    /// After the game handles client disconnect, POST to self-hosted Nexus API
    /// <c>POST /zone/player/disconnect?playerId=</c> so canonical home matches last zone (same Bearer as other zone calls).
    /// Opt out: <c>NEXUS_NOTIFY_PLAYER_DISCONNECT=0</c>. Skips Facepunch default API URL.
    /// Skips when <c>BaseEntity.IsTransferring()</c> is true so a Nexus warp does not overwrite API home
    /// (RegisterTransfers already set home to the destination before the source kicks the client).
    /// </summary>
    [HarmonyPatch]
    public static class BasePlayer_OnDisconnected_NotifyApi_Patch
    {
        private static bool _tlsHooked;
        private static bool _loggedOnce;

        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("BasePlayer");
            if (t == null)
            {
                if (!_loggedOnce) { _loggedOnce = true; Debug.Log("[NexusSelfHost] BasePlayer not found, skipping player/disconnect patch."); }
                return null;
            }

            var m = AccessTools.Method(t, "OnDisconnected");
            if (m == null)
            {
                if (!_loggedOnce) { _loggedOnce = true; Debug.Log("[NexusSelfHost] BasePlayer.OnDisconnected not found, skipping player/disconnect patch."); }
                return null;
            }

            if (!_loggedOnce)
            {
                _loggedOnce = true;
                Debug.Log("[NexusSelfHost] Patching BasePlayer.OnDisconnected -> POST /zone/player/disconnect (disable with NEXUS_NOTIFY_PLAYER_DISCONNECT=0).");
            }

            return m;
        }

        static void Postfix(object __instance)
        {
            if (__instance == null) return;
            if (string.Equals(Environment.GetEnvironmentVariable("NEXUS_NOTIFY_PLAYER_DISCONNECT"), "0", StringComparison.OrdinalIgnoreCase))
                return;

            // NexusServer.TransferEntityImpl: RegisterTransfers(toZone) runs before KickAfterServerTransfer.
            // Notifying disconnect from the source zone would set home back to source - skip during transfer kick.
            if (IsTransferringEntity(__instance))
                return;

            if (!TryGetNexusEndpointAndSecret(out var endpoint, out var secret))
                return;
            if (endpoint.IndexOf("facepunch.com", StringComparison.OrdinalIgnoreCase) >= 0)
                return;

            var uidProp = AccessTools.Property(__instance.GetType(), "userID");
            if (uidProp == null) return;
            var uidObj = uidProp.GetValue(__instance, null);
            if (uidObj == null) return;
            string playerId = uidObj.ToString();
            if (string.IsNullOrEmpty(playerId)) return;

            EnsureTlsCallback();
            string baseUrl = endpoint.TrimEnd('/');
            string url = baseUrl + "/zone/player/disconnect?playerId=" + Uri.EscapeDataString(playerId);

            _ = Task.Run(() => SendDisconnectAsync(url, secret));
        }

        private static void EnsureTlsCallback()
        {
            if (_tlsHooked) return;
            try
            {
                System.Net.ServicePointManager.ServerCertificateValidationCallback =
                    (sender, certificate, chain, sslPolicyErrors) => true;
                _tlsHooked = true;
            }
            catch
            {
                /* ignore */
            }
        }

        private static bool IsTransferringEntity(object entity)
        {
            var m = AccessTools.Method(entity.GetType(), "IsTransferring");
            if (m == null) return false;
            try
            {
                return m.Invoke(entity, null) is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetNexusEndpointAndSecret(out string endpoint, out string secret)
        {
            endpoint = null;
            secret = null;
            var nexusType = AccessTools.TypeByName("ConVar.Nexus");
            if (nexusType == null) return false;
            var epF = AccessTools.Field(nexusType, "endpoint");
            var skF = AccessTools.Field(nexusType, "secretKey");
            if (epF == null) return false;
            endpoint = (string)epF.GetValue(null);
            secret = skF != null ? (string)skF.GetValue(null) : null;
            return !string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(secret);
        }

        private static async Task SendDisconnectAsync(string url, string secret)
        {
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) })
                {
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);
                    using (var content = new StringContent("{}", Encoding.UTF8, "application/json"))
                    {
                        var response = await client.PostAsync(url, content).ConfigureAwait(false);
                        if (NexusServer_Initialize_EndpointFromEnv_Patch.DebugEnabled &&
                            !response.IsSuccessStatusCode)
                        {
                            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            Debug.LogWarning("[NexusSelfHost DEBUG] player/disconnect HTTP " + (int)response.StatusCode + " " + body);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (NexusServer_Initialize_EndpointFromEnv_Patch.DebugEnabled)
                    Debug.LogWarning("[NexusSelfHost DEBUG] player/disconnect failed: " + ex.Message);
            }
        }
    }
}
