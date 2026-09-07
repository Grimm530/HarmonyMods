using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using NexusSelfHost;
using UnityEngine;

namespace NexusSelfHost.Patches
{
    /// <summary>
    /// After vanilla <c>TransferHandler.RepositionEntitiesFromTransfer</c> for <c>console</c> transfers:
    /// (1) If the root is still within <c>NEXUS_TRANSFER_CONSOLE_MIN_SEP</c> m horizontally of the <b>source</b> packet
    /// root, re-pick <c>ServerMgr.FindSpawnPoint(null, 0)</c> and shift the bundle (same as before).
    /// (2) <b>Always</b> snap the bundle upward if the root sits below walkable ground at its X/Z: ray down from high
    /// altitude via <c>TransformUtil.GetGroundInfo</c>, else <c>TerrainMeta.HeightMap.GetHeight</c>, then add vertical
    /// delta so the root is at least <c>NEXUS_TRANSFER_GROUND_MARGIN</c> m above that surface (fixes InsideTerrain /
    /// under-map deaths when X/Z already differed from source).
    /// Opt out: <c>NEXUS_TRANSFER_SKIP_SAFE_SPAWN=1</c>.
    /// </summary>
    [HarmonyPatch]
    [HarmonyPriority(Priority.First)]
    public static class TransferHandler_RepositionEntitiesConsoleSafeSpawn_Patch
    {
        private static bool _targetResolved;
        private static MethodBase _targetMethod;

        [ThreadStatic]
        private static bool _captured;

        [ThreadStatic]
        private static Vector3 _sourceRootPos;

        private static bool Skip => NexusSelfHostOptions.TransferSkipSafeSpawn;

        static MethodBase TargetMethod()
        {
            if (_targetResolved)
                return _targetMethod;

            _targetResolved = true;
            var t = AccessTools.TypeByName("Rust.Nexus.Handlers.TransferHandler");
            if (t == null)
            {
                Debug.Log("[NexusSelfHost] TransferHandler not found, skipping console safe-spawn reposition patch.");
                return null;
            }

            var m = AccessTools.Method(t, "RepositionEntitiesFromTransfer");
            if (m == null)
            {
                Debug.Log("[NexusSelfHost] TransferHandler.RepositionEntitiesFromTransfer not found, skipping console safe-spawn patch.");
                return null;
            }

            Debug.Log("[NexusSelfHost] Patching TransferHandler.RepositionEntitiesFromTransfer -> console transfer safe spawn (disable: NEXUS_TRANSFER_SKIP_SAFE_SPAWN=1).");
            _targetMethod = m;
            return m;
        }

        static void Prefix(object __instance)
        {
            _captured = false;
            if (Skip)
                return;

            var request = Traverse.Create(__instance).Property("Request").GetValue();
            if (request == null)
                return;

            var entities = AccessTools.Property(request.GetType(), "entities")?.GetValue(request) as IList;
            var entityCount = entities?.Count ?? 0;
            if (NexusSelfHostOptions.LogTransferEntry)
            {
                Debug.Log("[NexusSelfHost] Transfer entry: method=" + GetTransferMethod(request) +
                          " from=" + GetTransferField(request, "from") +
                          " to=" + GetTransferField(request, "to") +
                          " entities=" + entityCount);
            }

            if (entities == null || entities.Count == 0)
                return;

            if (!TryGetEntityPosition(entities[0], out var root))
                return;

            _sourceRootPos = root;
            _captured = true;
        }

        static void Postfix(object __instance)
        {
            try
            {
                if (Skip || !_captured)
                    return;

                var request = Traverse.Create(__instance).Property("Request").GetValue();
                if (request == null)
                    return;

                if (!string.Equals(GetTransferMethod(request), "console", StringComparison.OrdinalIgnoreCase))
                    return;

                var entities = AccessTools.Property(request.GetType(), "entities")?.GetValue(request) as IList;
                if (entities == null || entities.Count == 0)
                    return;

                if (!TryGetEntityPosition(entities[0], out var newRoot))
                    return;

                var logTransfer = NexusSelfHostOptions.LogTransferFix;
                var verboseTransfer = NexusSelfHostOptions.VerboseTransferFix;
                var minSep = NexusSelfHostOptions.TransferConsoleMinSepMeters;
                var horizSq = HorizontalDistanceSq(newRoot, _sourceRootPos);
                var horiz = Mathf.Sqrt(horizSq);
                var destinationRouted = false;
                var preferredSurfaceReferenceY = newRoot.y;
                var sourceZone = GetTransferField(request, "from");

                if (logTransfer)
                {
                    Debug.Log("[NexusSelfHost] Transfer debug: console source=" + FormatVec(_sourceRootPos) +
                              " afterVanilla=" + FormatVec(newRoot) + " horizSep=" + horiz.ToString("F2") +
                              "m minSep=" + minSep.ToString("F2") + "m");
                }

                if (NexusSelfHostOptions.PortalTransferEnabled &&
                    NexusSelfHostOptions.PortalTransferForceConsoleTransfers)
                {
                    if (PortalTransferRouting.TryResolveDestination(sourceZone, out var portalRouting, out var portalError))
                    {
                        var delta = portalRouting.Destination - newRoot;
                        ApplyPositionDeltaToRootlessEntities(entities, delta);
                        destinationRouted = true;
                        preferredSurfaceReferenceY = portalRouting.Destination.y;

                        Debug.Log("[NexusSelfHost] Console Nexus transfer: routed to portal arrival for sourceZone=" +
                                  (portalRouting.SourceZone ?? string.Empty) + " portal=" + portalRouting.PortalName +
                                  " monument=" + portalRouting.MonumentName + " anchor[" + portalRouting.AnchorIndex +
                                  "] mode=" + portalRouting.AnchorMode + " portalPos=" +
                                  FormatVec(portalRouting.PortalPosition) + " appliedOffset=" +
                                  FormatVec(portalRouting.AppliedOffset) + " final=" +
                                  FormatVec(portalRouting.Destination) + ".");

                        if (verboseTransfer)
                        {
                            Debug.Log("[NexusSelfHost] Transfer debug: portal route delta=" + FormatVec(delta) +
                                      " portalYaw=" + portalRouting.PortalRotation.eulerAngles.y.ToString("F1") +
                                      " useRotation=" + NexusSelfHostOptions.PortalTransferUsePortalRotation);
                        }
                    }
                    else if (logTransfer)
                    {
                        Debug.LogWarning("[NexusSelfHost] Transfer debug: portal routing enabled but no destination resolved (" +
                                         portalError + "). Falling back to secondary routing.");
                    }
                }

                if (!destinationRouted && horizSq < minSep * minSep)
                {
                    var maxTries = NexusSelfHostOptions.TransferConsoleRespawnTries;

                    var minPick = Mathf.Max(minSep * 1.5f, 25f);
                    if (TryPickSpawnPointAwayFrom(_sourceRootPos, maxTries, minPick, out var chosen))
                    {
                        var delta = chosen - newRoot;
                        ApplyPositionDeltaToRootlessEntities(entities, delta);
                        Debug.Log("[NexusSelfHost] Console Nexus transfer: spawn was still aligned to source X/Z (sep < " +
                                  minSep + " m). Shifted bundle (extra delta xz=(" + delta.x.ToString("F1") + ", " +
                                  delta.z.ToString("F1") + "), y=" + delta.y.ToString("F1") + ")).");

                        if (verboseTransfer)
                        {
                            Debug.Log("[NexusSelfHost] Transfer debug: respawn candidate chosen=" + FormatVec(chosen) +
                                      " tries<=" + maxTries + " requiredSep=" + minPick.ToString("F2") + "m");
                        }
                    }
                    else if (logTransfer)
                    {
                        Debug.LogWarning("[NexusSelfHost] Transfer debug: could not pick a farther procedural spawn; keeping vanilla horizontal position.");
                    }
                }
                else if (!destinationRouted && verboseTransfer)
                {
                    Debug.Log("[NexusSelfHost] Transfer debug: horizontal respawn skipped because separation already exceeds threshold.");
                }

                if (!TryGetEntityPosition(entities[0], out var rootAfterHorizontal))
                    return;

                var margin = NexusSelfHostOptions.TransferGroundMarginMeters;
                if (TryGetTerrainSurfaceY(rootAfterHorizontal.x, rootAfterHorizontal.z, preferredSurfaceReferenceY, out var surfaceY, out var surfaceSource))
                {
                    var targetY = surfaceY + margin;
                    var yDelta = targetY - rootAfterHorizontal.y;
                    if (yDelta > 0.05f)
                    {
                        ApplyPositionDeltaToRootlessEntities(entities, new Vector3(0f, yDelta, 0f));
                        Debug.Log("[NexusSelfHost] Console Nexus transfer: vertical snap to terrain (dy=" + yDelta.ToString("F2") +
                                  " m, margin=" + margin.ToString("F2") + " m) at xz=(" + rootAfterHorizontal.x.ToString("F1") +
                                  ", " + rootAfterHorizontal.z.ToString("F1") + ").");
                    }
                    else if (verboseTransfer)
                    {
                        Debug.Log("[NexusSelfHost] Transfer debug: no vertical lift needed. rootY=" + rootAfterHorizontal.y.ToString("F2") +
                                  " targetY=" + targetY.ToString("F2") + " surfaceY=" + surfaceY.ToString("F2"));
                    }

                    if (logTransfer)
                    {
                        Debug.Log("[NexusSelfHost] Transfer debug: terrain surface source=" + surfaceSource +
                                  " surfaceY=" + surfaceY.ToString("F2") + " margin=" + margin.ToString("F2") +
                                  " finalLift=" + Mathf.Max(0f, yDelta).ToString("F2"));
                    }
                }
                else if (logTransfer)
                {
                    Debug.LogWarning("[NexusSelfHost] Transfer debug: could not determine terrain surface for root " +
                                     FormatVec(rootAfterHorizontal) + "; no vertical snap applied.");
                }
            }
            finally
            {
                _captured = false;
            }
        }

        private static float HorizontalDistanceSq(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static string GetTransferMethod(object request)
        {
            var transfer = Traverse.Create(request).Property("transfer").GetValue()
                           ?? Traverse.Create(request).Field("transfer").GetValue();
            if (transfer == null)
                return string.Empty;
            var method = Traverse.Create(transfer).Property("method").GetValue()
                         ?? Traverse.Create(transfer).Field("method").GetValue();
            return method?.ToString() ?? string.Empty;
        }

        private static string GetTransferField(object request, string fieldName)
        {
            var transfer = Traverse.Create(request).Property("transfer").GetValue()
                           ?? Traverse.Create(request).Field("transfer").GetValue();
            if (transfer == null)
                return string.Empty;

            var value = Traverse.Create(transfer).Property(fieldName).GetValue()
                        ?? Traverse.Create(transfer).Field(fieldName).GetValue();
            return value?.ToString() ?? string.Empty;
        }

        private static bool TryGetEntityPosition(object entity, out Vector3 pos)
        {
            pos = default;
            if (entity == null)
                return false;

            var baseEntity = Traverse.Create(entity).Property("baseEntity").GetValue()
                             ?? Traverse.Create(entity).Field("baseEntity").GetValue();
            if (baseEntity == null)
                return false;

            var p = Traverse.Create(baseEntity).Property("pos").GetValue()
                    ?? Traverse.Create(baseEntity).Field("pos").GetValue();
            if (p is Vector3 v)
            {
                pos = v;
                return true;
            }

            return false;
        }

        private static void SetEntityPosition(object entity, Vector3 pos)
        {
            if (entity == null)
                return;
            var baseEntity = Traverse.Create(entity).Property("baseEntity").GetValue()
                             ?? Traverse.Create(entity).Field("baseEntity").GetValue();
            if (baseEntity == null)
                return;

            var t = baseEntity.GetType();
            var prop = AccessTools.Property(t, "pos");
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(baseEntity, pos, null);
                return;
            }

            AccessTools.Field(t, "pos")?.SetValue(baseEntity, pos);
        }

        private static bool HasValidParentUid(object entity)
        {
            var parent = Traverse.Create(entity).Property("parent").GetValue()
                           ?? Traverse.Create(entity).Field("parent").GetValue();
            if (parent == null)
                return false;

            var uid = Traverse.Create(parent).Property("uid").GetValue()
                      ?? Traverse.Create(parent).Field("uid").GetValue();
            if (uid == null)
                return false;

            var isValid = Traverse.Create(uid).Property("IsValid").GetValue();
            if (isValid is bool b)
                return b;

            var val = Traverse.Create(uid).Property("Value").GetValue();
            if (val is ulong u)
                return u != 0UL;

            return true;
        }

        private static void ApplyPositionDeltaToRootlessEntities(IList entities, Vector3 delta)
        {
            foreach (var item in entities)
            {
                if (item == null)
                    continue;
                if (!TryGetEntityPosition(item, out var p))
                    continue;
                if (HasValidParentUid(item))
                    continue;
                SetEntityPosition(item, p + delta);
            }
        }

        private static bool TryPickSpawnPointAwayFrom(Vector3 sourceRoot, int maxTries, float minHorizontal, out Vector3 pos)
        {
            pos = default;
            var sm = AccessTools.TypeByName("ServerMgr");
            var bp = AccessTools.TypeByName("BasePlayer");
            if (sm == null || bp == null)
                return false;

            var m = AccessTools.Method(sm, "FindSpawnPoint", new[] { bp, typeof(ulong) });
            if (m == null)
                return false;

            var minSq = minHorizontal * minHorizontal;
            object lastSpawn = null;
            for (var i = 0; i < maxTries; i++)
            {
                lastSpawn = m.Invoke(null, new object[] { null, 0UL });
                if (lastSpawn == null)
                    continue;
                var candidate = ExtractSpawnPointPosition(lastSpawn);
                if (HorizontalDistanceSq(candidate, sourceRoot) >= minSq)
                {
                    pos = candidate;
                    return true;
                }
            }

            if (lastSpawn == null)
                return false;

            pos = ExtractSpawnPointPosition(lastSpawn);
            return true;
        }

        private static Vector3 ExtractSpawnPointPosition(object spawnPointObj)
        {
            if (spawnPointObj == null)
                return default;
            var tr = Traverse.Create(spawnPointObj);
            var p = tr.Property("pos").GetValue() ?? tr.Field("pos").GetValue();
            return p is Vector3 v ? v : default;
        }

        private static bool TryGetTerrainSurfaceY(float x, float z, float referenceY, out float surfaceY, out string source)
        {
            surfaceY = 0f;
            source = null;

            if (TryGetPhysicsSurfaceY(x, z, referenceY, out surfaceY, out source))
                return true;

            var startY = Mathf.Max(750f, referenceY + 400f);
            var probe = new Vector3(x, startY, z);

            var tu = AccessTools.TypeByName("TransformUtil");
            if (tu != null)
            {
                var mi = AccessTools.Method(tu, "GetGroundInfo", new[]
                {
                    typeof(Vector3),
                    typeof(Vector3).MakeByRefType(),
                    typeof(Vector3).MakeByRefType(),
                    typeof(float),
                    typeof(Transform)
                });
                if (mi != null)
                {
                    var args = new object[] { probe, Vector3.zero, Vector3.zero, 2000f, null };
                    if (mi.Invoke(null, args) is bool ok && ok && args[1] is Vector3 hitPos)
                    {
                        surfaceY = hitPos.y;
                        source = "TransformUtil.GetGroundInfo";
                        return true;
                    }
                }
            }

            var tm = AccessTools.TypeByName("TerrainMeta");
            var hmProp = tm != null ? AccessTools.Property(tm, "HeightMap") : null;
            var hm = hmProp?.GetValue(null, null);
            if (hm == null)
                return false;

            var gh = AccessTools.Method(hm.GetType(), "GetHeight", new[] { typeof(Vector3) });
            if (gh == null)
                return false;

            var h = gh.Invoke(hm, new object[] { new Vector3(x, 0f, z) });
            if (h is float fy)
            {
                surfaceY = fy;
                source = "TerrainMeta.HeightMap.GetHeight";
                return true;
            }

            return false;
        }

        private static bool TryGetPhysicsSurfaceY(float x, float z, float referenceY, out float surfaceY, out string source)
        {
            surfaceY = 0f;
            source = null;

            var startY = Mathf.Max(referenceY + 64f, 750f);
            var origin = new Vector3(x, startY, z);
            var maxDistance = 2000f;

            var physicsType = AccessTools.TypeByName("UnityEngine.Physics");
            var queryTriggerType = AccessTools.TypeByName("UnityEngine.QueryTriggerInteraction");
            if (physicsType == null || queryTriggerType == null)
                return false;

            var raycastAll = AccessTools.Method(physicsType, "RaycastAll", new[]
            {
                typeof(Vector3),
                typeof(Vector3),
                typeof(float),
                typeof(int),
                queryTriggerType
            });
            if (raycastAll == null)
                return false;

            var ignoreTriggers = Enum.Parse(queryTriggerType, "Ignore");
            var rawHits = raycastAll.Invoke(null, new object[] { origin, Vector3.down, maxDistance, ~0, ignoreTriggers }) as Array;
            if (rawHits == null || rawHits.Length == 0)
                return false;

            var hits = new object[rawHits.Length];
            rawHits.CopyTo(hits, 0);
            Array.Sort(hits, CompareRaycastHitDistance);

            object bestNearReference = null;
            object bestAnyWalkable = null;
            var bestNearReferenceDelta = float.MaxValue;

            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                var collider = Traverse.Create(hit).Property("collider").GetValue()
                               ?? Traverse.Create(hit).Field("collider").GetValue();
                if (collider == null)
                    continue;

                var normalObj = Traverse.Create(hit).Property("normal").GetValue()
                                ?? Traverse.Create(hit).Field("normal").GetValue();
                if (normalObj is not Vector3 normal || normal.y < 0.35f)
                    continue;

                if (bestAnyWalkable == null)
                    bestAnyWalkable = hit;

                var pointObj = Traverse.Create(hit).Property("point").GetValue()
                               ?? Traverse.Create(hit).Field("point").GetValue();
                if (pointObj is Vector3 point)
                {
                    var deltaToReference = Mathf.Abs(point.y - referenceY);
                    if (deltaToReference < bestNearReferenceDelta)
                    {
                        bestNearReference = hit;
                        bestNearReferenceDelta = deltaToReference;
                    }
                }
            }

            var chosen = bestNearReference ?? bestAnyWalkable;
            if (chosen == null)
                return false;

            var finalCollider = Traverse.Create(chosen).Property("collider").GetValue()
                                ?? Traverse.Create(chosen).Field("collider").GetValue();
            var finalPointObj = Traverse.Create(chosen).Property("point").GetValue()
                                ?? Traverse.Create(chosen).Field("point").GetValue();
            var finalNormalObj = Traverse.Create(chosen).Property("normal").GetValue()
                                 ?? Traverse.Create(chosen).Field("normal").GetValue();
            if (finalPointObj is not Vector3 finalPoint || finalNormalObj is not Vector3 finalNormal)
                return false;

            surfaceY = finalPoint.y;
            var finalGo = Traverse.Create(finalCollider).Property("gameObject").GetValue()
                          ?? Traverse.Create(finalCollider).Field("gameObject").GetValue();
            var layerObj = finalGo != null
                ? Traverse.Create(finalGo).Property("layer").GetValue() ?? Traverse.Create(finalGo).Field("layer").GetValue()
                : null;
            source = "Physics.RaycastAll collider=" + SafeName(finalGo as GameObject) +
                     " layer=" + (layerObj?.ToString() ?? "?") +
                     " normalY=" + finalNormal.y.ToString("F2");
            return true;
        }

        private static string FormatVec(Vector3 v)
        {
            return "(" + v.x.ToString("F2") + ", " + v.y.ToString("F2") + ", " + v.z.ToString("F2") + ")";
        }

        private static string SafeName(GameObject go)
        {
            return go == null ? "<null>" : (string.IsNullOrWhiteSpace(go.name) ? "<unnamed>" : go.name);
        }

        private static int CompareRaycastHitDistance(object a, object b)
        {
            var aDistance = GetRaycastHitDistance(a);
            var bDistance = GetRaycastHitDistance(b);
            return aDistance.CompareTo(bDistance);
        }

        private static float GetRaycastHitDistance(object hit)
        {
            if (hit == null)
                return float.MaxValue;

            var distanceObj = Traverse.Create(hit).Property("distance").GetValue()
                              ?? Traverse.Create(hit).Field("distance").GetValue();
            return distanceObj is float f ? f : float.MaxValue;
        }
    }
}
