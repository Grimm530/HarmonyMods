using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace NexusSelfHost
{
    internal static class OutpostTransferRouting
    {
        internal sealed class Resolution
        {
            public Vector3 RootPosition;
            public Quaternion RootRotation = Quaternion.identity;
            public Vector3 LocalOffset;
            public Vector3 AppliedOffset;
            public Vector3 Destination;
            public string MatchSource = string.Empty;
            public string MatchName = string.Empty;
            public string OffsetSource = string.Empty;
            public string CurrentZoneKey = string.Empty;
        }

        private const float CenterMatchThreshold = 140f;

        public static bool TryResolveDestination(out Resolution resolution, out string error)
        {
            resolution = null;
            error = null;

            if (!TryFindOutpostRoot(out var rootTransform, out var matchSource, out var matchName))
            {
                error = "no Outpost monument or safe-zone trigger matched current map";
                return false;
            }

            var result = new Resolution
            {
                RootPosition = rootTransform.position,
                RootRotation = rootTransform.rotation,
                MatchSource = matchSource,
                MatchName = matchName,
                CurrentZoneKey = TryGetCurrentZoneKey() ?? string.Empty
            };

            var cfgOffset = NexusSelfHostOptions.OutpostTransferLocalOffset;
            var localOffset = new Vector3(cfgOffset.X, cfgOffset.Y, cfgOffset.Z);
            var offsetSource = "config.LocalOffset";

            if (TryGetDerivedReferenceOffset(rootTransform, result.CurrentZoneKey, out var derivedOffset))
            {
                if (IsEffectivelyZero(localOffset))
                {
                    localOffset = derivedOffset;
                    offsetSource = "referenceWorldPosition";
                }

                if (NexusSelfHostOptions.LogTransferFix)
                {
                    Debug.Log("[NexusSelfHost] Transfer debug: Outpost reference point derived LocalOffset=" +
                              FormatVec(derivedOffset) + " for zone '" + (result.CurrentZoneKey ?? string.Empty) + "'.");
                }
            }

            if (IsEffectivelyZero(localOffset))
            {
                localOffset = new Vector3(0f, NexusSelfHostOptions.OutpostTransferDefaultHeightAboveRoot, 0f);
                offsetSource = "defaultHeightAboveRoot";
            }

            var appliedOffset = NexusSelfHostOptions.OutpostTransferUseMonumentRotation
                ? rootTransform.rotation * localOffset
                : localOffset;

            result.LocalOffset = localOffset;
            result.AppliedOffset = appliedOffset;
            result.Destination = rootTransform.position + appliedOffset;
            result.OffsetSource = offsetSource;

            resolution = result;
            return true;
        }

        private static bool TryFindOutpostRoot(out Transform bestTransform, out string source, out string matchName)
        {
            bestTransform = null;
            source = null;
            matchName = null;

            if (TryFindOutpostFromMonuments(out bestTransform, out matchName))
            {
                source = "TerrainMeta.Path.Monuments";
                return true;
            }

            if (TryFindOutpostFromSafeZones(out bestTransform, out matchName))
            {
                source = "TriggerSafeZone.allSafeZones";
                return true;
            }

            return false;
        }

        private static bool TryFindOutpostFromMonuments(out Transform bestTransform, out string matchName)
        {
            bestTransform = null;
            matchName = null;

            var monuments = GetTerrainMetaCollection("Path", "Monuments");
            if (monuments == null || monuments.Count == 0)
                return false;

            var center = TryGetMapCenter(out var c) ? c : Vector3.zero;
            var bestScore = int.MinValue;
            var bestDistSq = float.MaxValue;

            foreach (var item in monuments)
            {
                if (item is not Component comp || comp == null)
                    continue;

                if (!TryScoreOutpostCandidate(comp.transform, comp.gameObject?.name, item, out var score, out var label))
                    continue;

                var distSq = HorizontalDistanceSq(comp.transform.position, center);
                if (score > bestScore || score == bestScore && distSq < bestDistSq)
                {
                    bestScore = score;
                    bestDistSq = distSq;
                    bestTransform = comp.transform;
                    matchName = label;
                }
            }

            return bestTransform != null;
        }

        private static bool TryFindOutpostFromSafeZones(out Transform bestTransform, out string matchName)
        {
            bestTransform = null;
            matchName = null;

            var triggerSafeZoneType = AccessTools.TypeByName("TriggerSafeZone");
            if (triggerSafeZoneType == null)
                return false;

            var allSafeZones = AccessTools.Field(triggerSafeZoneType, "allSafeZones")?.GetValue(null) as IList;
            if (allSafeZones == null || allSafeZones.Count == 0)
                return false;

            var monumentType = AccessTools.TypeByName("MonumentInfo");
            var center = TryGetMapCenter(out var c) ? c : Vector3.zero;
            var bestScore = int.MinValue;
            var bestDistSq = float.MaxValue;

            foreach (var item in allSafeZones)
            {
                if (item is not Component safeZone || safeZone == null)
                    continue;

                var root = safeZone.transform;
                var label = safeZone.gameObject?.name ?? "<unnamed>";
                object parentMonument = null;
                if (monumentType != null)
                    parentMonument = safeZone.GetComponentInParent(monumentType);
                if (parentMonument is Component monumentComp && monumentComp != null)
                {
                    root = monumentComp.transform;
                    label = monumentComp.gameObject?.name ?? label;
                }

                if (!TryScoreOutpostCandidate(root, label, parentMonument ?? item, out var score, out var scoredLabel))
                    continue;

                var distSq = HorizontalDistanceSq(root.position, center);
                if (score > bestScore || score == bestScore && distSq < bestDistSq)
                {
                    bestScore = score;
                    bestDistSq = distSq;
                    bestTransform = root;
                    matchName = scoredLabel;
                }
            }

            return bestTransform != null;
        }

        private static bool TryScoreOutpostCandidate(Transform root, string objectName, object source, out int score, out string label)
        {
            score = 0;
            label = objectName ?? "<unnamed>";
            if (root == null)
                return false;

            var text = ((objectName ?? string.Empty) + " " + GetDisplayText(source)).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var containsOutpost = text.Contains("outpost");
            var containsCompound = text.Contains("compound");
            var containsMediumCompound = text.Contains("monument/medium/compound");
            if (!containsOutpost && !containsCompound && !containsMediumCompound)
                return false;

            if (containsOutpost)
                score += 250;
            if (containsCompound)
                score += 200;
            if (containsMediumCompound)
                score += 100;
            if (GetBoolMember(source, "IsSafeZone"))
                score += 120;

            if (TryGetMapCenter(out var center))
            {
                var dist = Mathf.Sqrt(HorizontalDistanceSq(root.position, center));
                if (dist <= CenterMatchThreshold)
                    score += 50;
            }

            return true;
        }

        private static IList GetTerrainMetaCollection(string parentPropertyName, string childFieldName)
        {
            var terrainMetaType = AccessTools.TypeByName("TerrainMeta");
            if (terrainMetaType == null)
                return null;

            var parent = AccessTools.Property(terrainMetaType, parentPropertyName)?.GetValue(null, null);
            if (parent == null)
                return null;

            return AccessTools.Field(parent.GetType(), childFieldName)?.GetValue(parent) as IList
                   ?? AccessTools.Property(parent.GetType(), childFieldName)?.GetValue(parent, null) as IList;
        }

        private static bool TryGetMapCenter(out Vector3 center)
        {
            center = Vector3.zero;
            var terrainMetaType = AccessTools.TypeByName("TerrainMeta");
            if (terrainMetaType == null)
                return false;

            var position = AccessTools.Property(terrainMetaType, "Position")?.GetValue(null, null);
            var size = AccessTools.Property(terrainMetaType, "Size")?.GetValue(null, null);
            if (position is not Vector3 pos || size is not Vector3 sz)
                return false;

            center = pos + sz * 0.5f;
            return true;
        }

        private static bool TryGetDerivedReferenceOffset(Transform rootTransform, string currentZoneKey, out Vector3 derivedOffset)
        {
            derivedOffset = default;
            if (!NexusSelfHostOptions.OutpostTransferReferencePointEnabled)
                return false;

            var referenceZoneKey = NexusSelfHostOptions.OutpostTransferReferenceZoneKey;
            if (string.IsNullOrWhiteSpace(referenceZoneKey) ||
                !string.Equals(referenceZoneKey, currentZoneKey, StringComparison.OrdinalIgnoreCase))
                return false;

            var reference = NexusSelfHostOptions.OutpostTransferReferenceWorldPosition;
            var referenceWorld = new Vector3(reference.X, reference.Y, reference.Z);
            derivedOffset = Quaternion.Inverse(rootTransform.rotation) * (referenceWorld - rootTransform.position);
            return true;
        }

        private static string TryGetCurrentZoneKey()
        {
            var nexusServerType = AccessTools.TypeByName("NexusServer");
            var zoneKey = nexusServerType != null
                ? AccessTools.Property(nexusServerType, "ZoneKey")?.GetValue(null, null)
                : null;
            return zoneKey?.ToString();
        }

        private static string GetDisplayText(object source)
        {
            if (source == null)
                return string.Empty;

            var displayPhrase = Traverse.Create(source).Field("displayPhrase").GetValue()
                                ?? Traverse.Create(source).Property("displayPhrase").GetValue();
            if (displayPhrase == null)
                return string.Empty;

            var english = Traverse.Create(displayPhrase).Field("english").GetValue()
                          ?? Traverse.Create(displayPhrase).Property("english").GetValue();
            var token = Traverse.Create(displayPhrase).Field("token").GetValue()
                        ?? Traverse.Create(displayPhrase).Property("token").GetValue();
            return ((english?.ToString() ?? string.Empty) + " " + (token?.ToString() ?? string.Empty)).Trim();
        }

        private static bool GetBoolMember(object source, string memberName)
        {
            if (source == null)
                return false;

            var value = Traverse.Create(source).Field(memberName).GetValue()
                        ?? Traverse.Create(source).Property(memberName).GetValue();
            return value is bool b && b;
        }

        private static bool IsEffectivelyZero(Vector3 v)
        {
            return Mathf.Abs(v.x) < 0.01f && Mathf.Abs(v.y) < 0.01f && Mathf.Abs(v.z) < 0.01f;
        }

        private static float HorizontalDistanceSq(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static string FormatVec(Vector3 v)
        {
            return "(" + v.x.ToString("F2") + ", " + v.y.ToString("F2") + ", " + v.z.ToString("F2") + ")";
        }
    }
}
