using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using HarmonyLib;
using UnityEngine;

namespace NexusSelfHost.Patches
{
    /// <summary>
    /// The Nexus zone socket uses WSS (WebSocket over TLS). When using a self-hosted Nexus API
    /// with the ASP.NET Core dev certificate (or any self-signed cert), Unity's TLS stack
    /// (UnityTls) fails with UNITYTLS_X509VERIFY_FLAG_NOT_TRUSTED because it doesn't use the
    /// Windows certificate store. This patch sets ServicePointManager.ServerCertificateValidationCallback
    /// before each Connect so that the certificate is accepted. (If the game uses UnityTls for
    /// WebSocket and ignores ServicePointManager, this may not help; then a proper dev cert
    /// trusted by Unity would be needed.)
    /// </summary>
    [HarmonyPatch]
    public static class NexusSocketConnector_Connect_AcceptCert_Patch
    {
        private static bool _loggedOnce;

        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("Facepunch.Nexus.Connector.NexusSocketConnector");
            if (t == null) return null;
            var m = AccessTools.Method(t, "Connect");
            if (m == null) return null;
            return m;
        }

        static void Prefix()
        {
            try
            {
                System.Net.ServicePointManager.ServerCertificateValidationCallback =
                    (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => true;
                if (!_loggedOnce)
                {
                    _loggedOnce = true;
                    Debug.Log("[NexusSelfHost] Patched certificate validation for Nexus zone socket (WSS) so self-signed cert is accepted.");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[NexusSelfHost] Failed to set ServerCertificateValidationCallback: " + ex.Message);
            }
        }
    }
}
