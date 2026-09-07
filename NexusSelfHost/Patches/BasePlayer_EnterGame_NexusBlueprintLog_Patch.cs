using System;
using System.Reflection;
using Facepunch.Nexus;
using Facepunch.Nexus.Models;
using HarmonyLib;
using NexusSelfHost;
using UnityEngine;

namespace NexusSelfHost.Patches
{
    /// <summary>
    /// After <c>BasePlayer.EnterGame</c> (same flow as console <c>has spawned</c>), log whether Nexus had a
    /// <c>blueprints.12</c> blob on the in-memory <c>NexusPlayer</c>. <c>PlayerInit</c> runs earlier; patching here
    /// matches when operators look at the log right after spawn.
    /// Disable: <c>NEXUS_LOG_BLUEPRINT_CONNECT=0</c>.
    /// Patched from <see cref="ServerMgr_Initialize_DeferredEnterGamePatch"/> after <c>ServerMgr.Initialize</c>
    /// so <c>FileStorage</c> is not initialized during early <c>PatchAll</c> (see HARMONY_MODS_GUIDE.md).
    /// </summary>
    public static class BasePlayer_EnterGame_NexusBlueprintLog_Patch
    {
        private const string BlueprintKey = "blueprints.12";
        private const float TransferRescueLiftMeters = 2f;
        private const float PortalRescueMatchDistanceMeters = 12f;

        [ThreadStatic]
        private static bool _enteredFromTransfer;

        public static void Prefix(object __instance)
        {
            _enteredFromTransfer = false;
            try
            {
                _enteredFromTransfer = ReadBool(__instance, "LoadingAfterTransfer") ||
                                       InvokeBool(__instance, "IsLoadingAfterTransfer") ||
                                       InvokeBool(__instance, "IsTransferProtected");
            }
            catch
            {
                _enteredFromTransfer = false;
            }
        }

        public static void Postfix(object __instance)
        {
            try
            {
                if (_enteredFromTransfer)
                    RescueUnsafeTransferSpawn(__instance);

                if (NexusSelfHostOptions.LogBlueprintOnConnect)
                    LogBlueprintStatus(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogError("[NexusSelfHost] EnterGame post-processing failed: " + ex);
            }
            finally
            {
                _enteredFromTransfer = false;
            }
        }

        private static void RescueUnsafeTransferSpawn(object __instance)
        {
            if (__instance == null)
                return;

            if (!TryGetPlayerPosition(__instance, out var currentPos))
                return;

            if (!NeedsTransferRescue(currentPos, out var surfaceY, out var surfaceSource))
                return;

            if (TryFindNearbyPortalRescue(currentPos, out var portalRescuePos, out var portalRescueRot, out var portalSource))
            {
                ApplyTeleport(__instance, portalRescuePos, portalRescueRot);
                Debug.LogWarning("[NexusSelfHost] Transfer rescue: moved transferred player from " + FormatVec(currentPos) +
                                 " to nearby portal rescue " + FormatVec(portalRescuePos) + " because spawn was below safe surface " +
                                 "(surfaceY=" + surfaceY.ToString("F2") + ", source=" + surfaceSource + ", portalSource=" +
                                 portalSource + ", lift=" + TransferRescueLiftMeters.ToString("F2") + "m).");
                return;
            }

            if (!TryFindFallbackSpawn(out var fallbackPos, out var fallbackRot, out var spawnSource))
            {
                Debug.LogWarning("[NexusSelfHost] Transfer rescue: unsafe transferred spawn detected at " +
                                 FormatVec(currentPos) + " but no fallback spawn point was available.");
                return;
            }

            var liftedFallbackPos = fallbackPos + new Vector3(0f, TransferRescueLiftMeters, 0f);
            ApplyTeleport(__instance, liftedFallbackPos, fallbackRot);
            Debug.LogWarning("[NexusSelfHost] Transfer rescue: moved transferred player from " + FormatVec(currentPos) +
                             " to fallback spawn " + FormatVec(liftedFallbackPos) + " because spawn was below safe surface " +
                             "(surfaceY=" + surfaceY.ToString("F2") + ", source=" + surfaceSource + ", spawnSource=" +
                             spawnSource + ", lift=" + TransferRescueLiftMeters.ToString("F2") + "m).");
        }

        private static bool NeedsTransferRescue(Vector3 currentPos, out float surfaceY, out string surfaceSource)
        {
            surfaceY = 0f;
            surfaceSource = "none";

            if (currentPos.y < -5f)
            {
                surfaceSource = "position.y<-5";
                return true;
            }

            if (!TryGetSurfaceY(currentPos.x, currentPos.z, currentPos.y, out surfaceY, out surfaceSource))
                return false;

            return currentPos.y < surfaceY - 3f;
        }

        private static bool TryFindFallbackSpawn(out Vector3 pos, out Quaternion rot, out string source)
        {
            pos = default;
            rot = Quaternion.identity;
            source = "none";

            var serverMgrType = AccessTools.TypeByName("ServerMgr");
            var basePlayerType = AccessTools.TypeByName("BasePlayer");
            if (serverMgrType == null || basePlayerType == null)
                return false;

            var findSpawnPoint = AccessTools.Method(serverMgrType, "FindSpawnPoint", new[] { basePlayerType, typeof(ulong) });
            if (findSpawnPoint == null)
                return false;

            var spawnPoint = findSpawnPoint.Invoke(null, new object[] { null, 0UL });
            if (spawnPoint == null)
                return false;

            var tr = Traverse.Create(spawnPoint);
            var rawPos = tr.Property("pos").GetValue() ?? tr.Field("pos").GetValue();
            var rawRot = tr.Property("rot").GetValue() ?? tr.Field("rot").GetValue();
            if (rawPos is not Vector3 spawnPos)
                return false;

            pos = spawnPos;
            rot = rawRot is Quaternion spawnRot ? spawnRot : Quaternion.identity;
            source = "ServerMgr.FindSpawnPoint";
            return true;
        }

        private static bool TryFindNearbyPortalRescue(Vector3 currentPos, out Vector3 pos, out Quaternion rot, out string source)
        {
            pos = default;
            rot = Quaternion.identity;
            source = "none";

            if (!PortalTransferRouting.TryResolveNearestDestination(currentPos, PortalRescueMatchDistanceMeters, out var resolution, out var error) ||
                resolution == null)
            {
                source = error ?? "no nearby portal resolution";
                return false;
            }

            if (!TryGetSurfaceY(resolution.Destination.x, resolution.Destination.z, resolution.Destination.y, out var surfaceY, out var surfaceSource))
            {
                source = "portal '" + resolution.PortalName + "' surface lookup failed";
                return false;
            }

            pos = new Vector3(resolution.Destination.x, surfaceY + TransferRescueLiftMeters, resolution.Destination.z);
            rot = resolution.PortalRotation;
            source = "portal=" + resolution.PortalName + " surface=" + surfaceSource;
            return true;
        }

        private static void ApplyTeleport(object player, Vector3 position, Quaternion rotation)
        {
            var playerType = player.GetType();
            var teleport = AccessTools.Method(playerType, "Teleport", new[] { typeof(Vector3) });
            teleport?.Invoke(player, new object[] { position });

            var transform = Traverse.Create(player).Property("transform").GetValue() as Transform;
            if (transform != null)
                transform.rotation = rotation;

            var sendUpdate = AccessTools.Method(playerType, "SendNetworkUpdateImmediate", Type.EmptyTypes);
            sendUpdate?.Invoke(player, null);

            var clientRpc = AccessTools.Method(playerType, "ClientRPC", new[] { AccessTools.TypeByName("RpcTarget"), typeof(string), typeof(Vector3) });
            var rpcTargetType = AccessTools.TypeByName("RpcTarget");
            if (clientRpc != null && rpcTargetType != null)
            {
                var playerFactory = AccessTools.Method(rpcTargetType, "Player", new[] { typeof(string), playerType });
                if (playerFactory != null)
                {
                    var target = playerFactory.Invoke(null, new[] { "ForceViewAnglesTo", player });
                    clientRpc.Invoke(player, new[] { target, "ForceViewAnglesTo", rotation.eulerAngles });
                }
            }
        }

        private static bool TryGetPlayerPosition(object player, out Vector3 pos)
        {
            pos = default;
            var transform = Traverse.Create(player).Property("transform").GetValue() as Transform;
            if (transform == null)
                return false;

            pos = transform.position;
            return true;
        }

        private static bool TryGetSurfaceY(float x, float z, float referenceY, out float surfaceY, out string source)
        {
            surfaceY = 0f;
            source = "none";

            var startY = Mathf.Max(referenceY + 64f, 750f);
            var probe = new Vector3(x, startY, z);

            var transformUtil = AccessTools.TypeByName("TransformUtil");
            if (transformUtil != null)
            {
                var groundInfo = AccessTools.Method(transformUtil, "GetGroundInfo", new[]
                {
                    typeof(Vector3),
                    typeof(Vector3).MakeByRefType(),
                    typeof(Vector3).MakeByRefType(),
                    typeof(float),
                    typeof(Transform)
                });
                if (groundInfo != null)
                {
                    var args = new object[] { probe, Vector3.zero, Vector3.zero, 2000f, null };
                    if (groundInfo.Invoke(null, args) is bool ok && ok && args[1] is Vector3 hitPos)
                    {
                        surfaceY = hitPos.y;
                        source = "TransformUtil.GetGroundInfo";
                        return true;
                    }
                }
            }

            var terrainMetaType = AccessTools.TypeByName("TerrainMeta");
            var heightMap = terrainMetaType != null ? AccessTools.Property(terrainMetaType, "HeightMap")?.GetValue(null, null) : null;
            if (heightMap == null)
                return false;

            var getHeight = AccessTools.Method(heightMap.GetType(), "GetHeight", new[] { typeof(Vector3) });
            if (getHeight == null)
                return false;

            if (getHeight.Invoke(heightMap, new object[] { new Vector3(x, 0f, z) }) is not float fy)
                return false;

            surfaceY = fy;
            source = "TerrainMeta.HeightMap.GetHeight";
            return true;
        }

        private static bool ReadBool(object instance, string propertyName)
        {
            if (instance == null)
                return false;

            var value = Traverse.Create(instance).Property(propertyName).GetValue()
                        ?? Traverse.Create(instance).Field(propertyName).GetValue();
            return value is bool b && b;
        }

        private static bool InvokeBool(object instance, string methodName)
        {
            if (instance == null)
                return false;

            var method = AccessTools.Method(instance.GetType(), methodName, Type.EmptyTypes);
            return method?.Invoke(instance, null) is bool b && b;
        }

        private static string FormatVec(Vector3 v)
        {
            return "(" + v.x.ToString("F2") + ", " + v.y.ToString("F2") + ", " + v.z.ToString("F2") + ")";
        }

        private static void LogBlueprintStatus(object __instance)
        {

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
