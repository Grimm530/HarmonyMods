using System;
using System.Reflection;
using Facepunch.Nexus;
using Facepunch.Nexus.Models;
using HarmonyLib;
using UnityEngine;

namespace NexusSelfHost.Patches
{
    /// <summary>
    /// After <c>BasePlayer.EnterGame</c> (same flow as console <c>has spawned</c>), log whether Nexus had a
    /// <c>blueprints.12</c> blob on the in-memory <c>NexusPlayer</c>. <c>PlayerInit</c> runs earlier; patching here
    /// matches when operators look at the log right after spawn.
    /// Disable: <c>NEXUS_LOG_BLUEPRINT_CONNECT=0</c>.
    /// </summary>
    [HarmonyPatch]
    public static class BasePlayer_EnterGame_NexusBlueprintLog_Patch
    {
        private const string BlueprintKey = "blueprints.12";
        private static bool _targetLoggedOnce;

        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("BasePlayer");
            if (t == null)
            {
                if (!_targetLoggedOnce)
                {
                    _targetLoggedOnce = true;
                    Debug.Log("[NexusSelfHost] BasePlayer not found, skipping EnterGame blueprint log patch.");
                }
                return null;
            }

            var m = AccessTools.Method(t, "EnterGame");
            if (m == null)
            {
                if (!_targetLoggedOnce)
                {
                    _targetLoggedOnce = true;
                    Debug.Log("[NexusSelfHost] BasePlayer.EnterGame not found, skipping blueprint log patch.");
                }
                return null;
            }

            if (!_targetLoggedOnce)
            {
                _targetLoggedOnce = true;
                Debug.Log("[NexusSelfHost] Patching BasePlayer.EnterGame -> Nexus blueprint connect log (after \"has spawned\"; disable: NEXUS_LOG_BLUEPRINT_CONNECT=0).");
            }

            return m;
        }

        static void Postfix(object __instance)
        {
            if (string.Equals(Environment.GetEnvironmentVariable("NEXUS_LOG_BLUEPRINT_CONNECT"), "0", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                LogBlueprintStatus(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogError("[NexusSelfHost] Nexus blueprint connect log failed: " + ex);
            }
        }

        private static void LogBlueprintStatus(object __instance)
        {
            if (__instance == null) return;

            var isBotProp = AccessTools.Property(__instance.GetType(), "IsBot");
            if (isBotProp?.GetValue(__instance, null) is bool isBot && isBot)
                return;

            var nexusType = AccessTools.TypeByName("NexusServer");
            if (nexusType == null)
            {
                Debug.LogWarning("[NexusSelfHost] Nexus blueprints on connect: NexusServer type not found.");
                return;
            }

            var startedProp = AccessTools.Property(nexusType, "Started");
            if (startedProp?.GetValue(null, null) is not bool started || !started)
                return;

            ulong steamId = ReadSteamId(__instance);
            if (steamId == 0UL || steamId < 10000000UL)
            {
                Debug.LogWarning("[NexusSelfHost] Nexus blueprints on connect: could not read steam id (got " + steamId + ").");
                return;
            }

            string name = ReadDisplayName(__instance);

            var tryGetPlayer = FindTryGetPlayerMethod(nexusType);
            if (tryGetPlayer == null)
            {
                Debug.LogWarning("[NexusSelfHost] Nexus blueprints on connect: NexusServer.TryGetPlayer not found via reflection.");
                return;
            }

            var args = new object[] { steamId, null };
            bool hasPlayer;
            try
            {
                hasPlayer = (bool)tryGetPlayer.Invoke(null, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NexusSelfHost] Nexus TryGetPlayer failed for " + steamId + ": " + ex.Message);
                return;
            }

            if (!hasPlayer || args[1] is not NexusPlayer nexusPlayer)
            {
                Debug.LogWarning(
                    "[NexusSelfHost] Nexus blueprints on connect: " + name + "[" + steamId + "] - " +
                    "NO NexusPlayer in zone cache (login timing); cannot read blueprints.12.");
                return;
            }

            if (!nexusPlayer.TryGetVariable(BlueprintKey, out var variable))
            {
                Debug.LogWarning(
                    "[NexusSelfHost] Nexus blueprints on connect: " + name + "[" + steamId + "] - " +
                    "NexusPlayer OK but no variable '" + BlueprintKey + "' (API/login did not return it yet).");
                return;
            }

            if (variable.Type != VariableType.Binary)
            {
                Debug.LogWarning(
                    "[NexusSelfHost] Nexus blueprints on connect: " + name + "[" + steamId + "] - " +
                    "variable '" + BlueprintKey + "' has wrong type " + variable.Type + " (expected Binary).");
                return;
            }

            byte[] blob = variable.GetAsBinary();
            int len = blob?.Length ?? 0;
            int approxB64 = len <= 0 ? 0 : (len + 2) / 3 * 4;

            Debug.Log(
                "[NexusSelfHost] Nexus blueprints on connect: " + name + "[" + steamId + "] - " +
                "OK Binary protoBytes=" + len + " (approx base64Chars~" + approxB64 + ").");
        }

        /// <summary>Resolve TryGetPlayer(ulong, out NexusPlayer) without relying on exact generic closure matching.</summary>
        private static MethodInfo FindTryGetPlayerMethod(Type nexusServerType)
        {
            foreach (var m in nexusServerType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "TryGetPlayer") continue;
                var ps = m.GetParameters();
                if (ps.Length != 2 || ps[0].ParameterType != typeof(ulong) || !ps[1].ParameterType.IsByRef) continue;
                var elem = ps[1].ParameterType.GetElementType();
                if (elem != null && typeof(NexusPlayer).IsAssignableFrom(elem))
                    return m;
            }
            return null;
        }

        /// <summary>
        /// Set on <c>PlayerInit</c> before <c>EnterGame</c>; more reliable than reflecting <c>Network.Connection</c>
        /// (userid may be a property or live in another assembly).
        /// </summary>
        private static ulong ReadSteamId(object player)
        {
            for (var t = player.GetType(); t != null; t = t.BaseType)
            {
                const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var fs = t.GetField("UserIDString", inst);
                if (fs != null)
                {
                    if (fs.GetValue(player) is string s1 && ulong.TryParse(s1, System.Globalization.NumberStyles.Integer, null, out var id1) && id1 != 0UL)
                        return id1;
                    break;
                }
                var ps = t.GetProperty("UserIDString", inst);
                if (ps != null && ps.CanRead)
                {
                    if (ps.GetValue(player, null) is string s2 && ulong.TryParse(s2, System.Globalization.NumberStyles.Integer, null, out var id2) && id2 != 0UL)
                        return id2;
                    break;
                }
            }

            var netProp = AccessTools.Property(player.GetType(), "net");
            var net = netProp?.GetValue(player, null);
            if (net != null)
            {
                var connProp = AccessTools.Property(net.GetType(), "connection");
                var conn = connProp?.GetValue(net, null);
                if (TryReadConnectionUserId(conn, out var fromConn) && fromConn != 0UL)
                    return fromConn;
            }

            var p = AccessTools.Property(player.GetType(), "userID");
            if (p == null) return 0UL;
            var v = p.GetValue(player, null);
            if (v == null) return 0UL;

            var get = v.GetType().GetMethod("Get", Type.EmptyTypes);
            if (get != null)
            {
                try
                {
                    var inner = get.Invoke(v, null);
                    if (inner != null && TryConvertToUInt64(inner, out var fromGet) && fromGet != 0UL)
                        return fromGet;
                }
                catch
                {
                    /* fall through */
                }
            }

            var valueField = v.GetType().GetField("_value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (valueField != null)
            {
                var raw = valueField.GetValue(v);
                if (raw != null && TryConvertToUInt64(raw, out var fromField) && fromField != 0UL)
                    return fromField;
            }

            return 0UL;
        }

        private static bool TryReadConnectionUserId(object conn, out ulong userid)
        {
            userid = 0UL;
            if (conn == null) return false;
            var t = conn.GetType();
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var f in t.GetFields(inst))
            {
                if (!string.Equals(f.Name, "userid", StringComparison.OrdinalIgnoreCase)) continue;
                var val = f.GetValue(conn);
                if (TryConvertToUInt64(val, out userid) && userid != 0UL) return true;
            }
            foreach (var pr in t.GetProperties(inst))
            {
                if (!string.Equals(pr.Name, "userid", StringComparison.OrdinalIgnoreCase) || !pr.CanRead) continue;
                var val = pr.GetValue(conn, null);
                if (TryConvertToUInt64(val, out userid) && userid != 0UL) return true;
            }
            return false;
        }

        private static bool TryConvertToUInt64(object val, out ulong u)
        {
            u = 0UL;
            if (val == null) return false;
            switch (val)
            {
                case ulong x:
                    u = x;
                    return true;
                case long x when x >= 0:
                    u = (ulong)x;
                    return true;
                case int x when x >= 0:
                    u = (ulong)x;
                    return true;
            }
            try
            {
                u = Convert.ToUInt64(val);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadDisplayName(object player)
        {
            var p = AccessTools.Property(player.GetType(), "displayName");
            var s = p?.GetValue(player, null) as string;
            return string.IsNullOrEmpty(s) ? "?" : s;
        }
    }
}
