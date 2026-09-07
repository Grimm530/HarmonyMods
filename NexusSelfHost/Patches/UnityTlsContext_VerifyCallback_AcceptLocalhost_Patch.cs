using System;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace NexusSelfHost.Patches
{
    /// <summary>
    /// During WSS/HTTPS handshake, UnityTls invokes UnityTlsContext.VerifyCallback. If the server cert
    /// is not trusted (e.g. self-signed), it returns UNITYTLS_X509VERIFY_FLAG_NOT_TRUSTED and
    /// ProcessHandshake then throws. We Postfix: when the result is NOT_TRUSTED and the
    /// connection target is localhost or a private/server IP (self-hosted Nexus), treat as success.
    /// Accepts: 127.0.0.1, localhost, and any IPv4 address (e.g. server public IP 70.8.154.251).
    /// </summary>
    [HarmonyPatch]
    public static class UnityTlsContext_VerifyCallback_AcceptLocalhost_Patch
    {
        private static bool _loggedOnce;
        private static PropertyInfo _serverNameProp;
        private const uint NOT_TRUSTED_FLAG = 8u;
        private const uint SUCCESS_VALUE = 0u;
        private static readonly Regex IPv4Regex = new Regex(@"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$", RegexOptions.Compiled);

        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("Mono.Unity.UnityTlsContext");
            if (t == null) return null;
            var methods = t.GetMethods(AccessTools.allDeclared);
            foreach (var m in methods)
            {
                if (m.Name != "VerifyCallback" || m.IsStatic) continue;
                var ps = m.GetParameters();
                if (ps.Length == 2) return m;
            }
            return null;
        }

        static bool IsAcceptableNexusHost(string serverName)
        {
            if (string.IsNullOrEmpty(serverName)) return false;
            if (serverName.Equals("127.0.0.1", StringComparison.Ordinal)) return true;
            if (serverName.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (IPv4Regex.IsMatch(serverName)) return true;
            return false;
        }

        static void Postfix(object __instance, ref object __result)
        {
            if (__result == null) return;
            uint resultVal = (uint)Convert.ChangeType(__result, typeof(uint));
            if ((resultVal & NOT_TRUSTED_FLAG) == 0) return;

            string serverName = null;
            try
            {
                if (_serverNameProp == null)
                {
                    var ctxType = __instance.GetType();
                    var baseType = ctxType.BaseType;
                    while (baseType != null)
                    {
                        _serverNameProp = baseType.GetProperty("ServerName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (_serverNameProp != null) break;
                        baseType = baseType.BaseType;
                    }
                }
                serverName = _serverNameProp?.GetValue(__instance) as string;
            }
            catch { return; }

            if (!IsAcceptableNexusHost(serverName)) return;

            var enumType = __result.GetType();
            __result = Enum.ToObject(enumType, SUCCESS_VALUE);
            if (!_loggedOnce)
            {
                _loggedOnce = true;
                Debug.Log("[NexusSelfHost] Accepting self-signed certificate for " + (serverName ?? "?") + " in UnityTls (Nexus HTTP/WSS).");
            }
        }
    }
}
