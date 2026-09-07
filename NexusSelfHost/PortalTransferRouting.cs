using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace NexusSelfHost
{
    internal static class PortalTransferRouting
    {
        internal sealed class Resolution
        {
            public string SourceZone = string.Empty;
            public string PortalName = string.Empty;
            public string MonumentName = string.Empty;
            public string AnchorMode = string.Empty;
            public int AnchorIndex;
            public Vector3 PortalPosition;
            public Quaternion PortalRotation = Quaternion.identity;
            public Vector3 AppliedOffset;
            public Vector3 Destination;
        }

        private static readonly string ConfigPath = Path.Combine(Environment.CurrentDirectory, "HarmonyConfig", "NexusStaticPortals.json");

        public static bool TryResolveDestination(string sourceZone, out Resolution resolution, out string error)
        {
            resolution = null;
            error = null;

            if (string.IsNullOrWhiteSpace(sourceZone))
            {
                error = "source zone is empty";
                return false;
            }

            if (!TryLoadConfig(out var cfg, out error))
                return false;

            if (cfg?.Portals == null || cfg.Portals.Count == 0)
            {
                error = "portal config has no portal entries";
                return false;
            }

            foreach (var portal in cfg.Portals)
            {
                if (portal == null || string.IsNullOrWhiteSpace(portal.NexusTransferTargetZoneKey))
                    continue;

                if (!string.Equals(portal.NexusTransferTargetZoneKey.Trim(), sourceZone.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (portal.EntranceAnchors == null || portal.EntranceAnchors.Count == 0)
                    continue;

                for (var i = 0; i < portal.EntranceAnchors.Count; i++)
                {
                    var anchor = portal.EntranceAnchors[i];
                    if (anchor == null)
                        continue;

                    if (!TryResolveAnchorTransform(anchor, out var portalPos, out var portalRot, out var monumentName, out var failureReason))
                    {
                        error = "portal '" + portal.Name + "' anchor[" + i + "] failed: " + failureReason;
                        continue;
                    }

                    var localOffset = new Vector3(0f, NexusSelfHostOptions.PortalTransferUpOffsetMeters, NexusSelfHostOptions.PortalTransferForwardOffsetMeters);
                    var appliedOffset = NexusSelfHostOptions.PortalTransferUsePortalRotation
                        ? portalRot * localOffset
                        : localOffset;

                    resolution = new Resolution
                    {
                        SourceZone = sourceZone.Trim(),
                        PortalName = portal.Name ?? string.Empty,
                        MonumentName = monumentName ?? string.Empty,
                        AnchorMode = anchor.UseMonumentRelativeTransform ? "monument-relative" : "fixed-world",
                        AnchorIndex = i,
                        PortalPosition = portalPos,
                        PortalRotation = portalRot,
                        AppliedOffset = appliedOffset,
                        Destination = portalPos + appliedOffset
                    };

                    return true;
                }
            }

            error ??= "no portal in NexusStaticPortals.json matched source zone '" + sourceZone.Trim() + "'";
            return false;
        }

        public static bool TryResolveNearestDestination(Vector3 currentPosition, float maxHorizontalDistance, out Resolution resolution, out string error)
        {
            resolution = null;
            error = null;

            if (!TryLoadConfig(out var cfg, out error))
                return false;

            if (cfg?.Portals == null || cfg.Portals.Count == 0)
            {
                error = "portal config has no portal entries";
                return false;
            }

            var maxSq = maxHorizontalDistance * maxHorizontalDistance;
            var bestDistSq = float.MaxValue;

            foreach (var portal in cfg.Portals)
            {
                if (portal?.EntranceAnchors == null || portal.EntranceAnchors.Count == 0)
                    continue;

                for (var i = 0; i < portal.EntranceAnchors.Count; i++)
                {
                    var anchor = portal.EntranceAnchors[i];
                    if (anchor == null)
                        continue;

                    if (!TryResolveAnchorTransform(anchor, out var portalPos, out var portalRot, out var monumentName, out var failureReason))
                    {
                        error = "portal '" + portal.Name + "' anchor[" + i + "] failed: " + failureReason;
                        continue;
                    }

                    var localOffset = new Vector3(0f, NexusSelfHostOptions.PortalTransferUpOffsetMeters, NexusSelfHostOptions.PortalTransferForwardOffsetMeters);
                    var appliedOffset = NexusSelfHostOptions.PortalTransferUsePortalRotation
                        ? portalRot * localOffset
                        : localOffset;
                    var destination = portalPos + appliedOffset;
                    var distSq = HorizontalDistanceSq(currentPosition, portalPos);
                    if (distSq > maxSq || distSq >= bestDistSq)
                        continue;

                    bestDistSq = distSq;
                    resolution = new Resolution
                    {
                        SourceZone = portal.NexusTransferTargetZoneKey?.Trim() ?? string.Empty,
                        PortalName = portal.Name ?? string.Empty,
                        MonumentName = monumentName ?? string.Empty,
                        AnchorMode = anchor.UseMonumentRelativeTransform ? "monument-relative" : "fixed-world",
                        AnchorIndex = i,
                        PortalPosition = portalPos,
                        PortalRotation = portalRot,
                        AppliedOffset = appliedOffset,
                        Destination = destination
                    };
                }
            }

            if (resolution != null)
                return true;

            error ??= "no nearby portal matched current position " + currentPosition;
            return false;
        }

        private static bool TryResolveAnchorTransform(PortalAnchorDefinition anchor, out Vector3 worldPos, out Quaternion rotation, out string monumentName, out string failureReason)
        {
            worldPos = Vector3.zero;
            rotation = Quaternion.identity;
            monumentName = string.Empty;
            failureReason = null;

            if (anchor.UseMonumentRelativeTransform)
            {
                if (!TryFindMatchingMonumentAnchor(anchor, out var monument, out worldPos, out rotation, out monumentName))
                {
                    failureReason = "no matching monument found for '" + (anchor.MonumentNameContains ?? "compound") + "'";
                    return false;
                }
                return true;
            }

            if (!anchor.UseFixedWorldTransform)
            {
                failureReason = "anchor has no supported transform mode enabled";
                return false;
            }

            worldPos = anchor.WorldPosition.ToVector3() + anchor.WorldPositionOffset.ToVector3();
            if (worldPos == Vector3.zero)
            {
                failureReason = "world position is zero";
                return false;
            }

            rotation = Quaternion.Euler(anchor.WorldEulerAngles.ToVector3());
            worldPos += rotation * anchor.FixedWorldLocalOffset.ToVector3();
            return true;
        }

        private static bool TryFindMatchingMonumentAnchor(PortalAnchorDefinition anchor, out Component bestMonument, out Vector3 bestWorldPos, out Quaternion bestRotation, out string bestName)
        {
            bestMonument = null;
            bestWorldPos = Vector3.zero;
            bestRotation = Quaternion.identity;
            bestName = string.Empty;

            var needle = string.IsNullOrWhiteSpace(anchor?.MonumentNameContains) ? "compound" : anchor.MonumentNameContains.Trim();
            var center = TryGetMapCenter(out var c) ? c : Vector3.zero;

            var bestScore = int.MinValue;
            var bestAnchorY = float.MinValue;
            var bestDistSq = float.MaxValue;

            foreach (var monument in EnumerateMonumentCandidates())
            {
                if (monument is not Component component || component == null || !MonumentMatches(component, needle))
                    continue;

                var score = ScoreMonumentCandidate(component, needle);
                var rotation = component.transform.rotation * Quaternion.Euler(anchor.LocalEulerAngles.ToVector3());
                var worldPos = component.transform.TransformPoint(anchor.LocalPosition.ToVector3());
                worldPos += anchor.WorldPositionOffset.ToVector3();
                worldPos += rotation * anchor.FixedWorldLocalOffset.ToVector3();
                var distSq = HorizontalDistanceSq(component.transform.position, center);

                if (bestMonument == null ||
                    score > bestScore ||
                    score == bestScore && worldPos.y > bestAnchorY + 0.01f ||
                    score == bestScore && Mathf.Abs(worldPos.y - bestAnchorY) <= 0.01f && distSq < bestDistSq)
                {
                    bestMonument = component;
                    bestScore = score;
                    bestAnchorY = worldPos.y;
                    bestWorldPos = worldPos;
                    bestRotation = rotation;
                    bestName = GetMonumentDebugName(component);
                    bestDistSq = distSq;
                }
            }

            return bestMonument != null;
        }

        private static IEnumerable EnumerateMonumentCandidates()
        {
            var yielded = false;
            var monuments = GetTerrainMetaCollection("Path", "Monuments");
            if (monuments != null)
            {
                yielded = true;
                foreach (var monument in monuments)
                    yield return monument;
            }

            if (yielded)
                yield break;

            var monumentType = HarmonyLib.AccessTools.TypeByName("MonumentInfo");
            if (monumentType == null)
                yield break;

            var sceneMonuments = UnityEngine.Object.FindObjectsOfType(monumentType);
            if (sceneMonuments == null)
                yield break;

            foreach (var monument in sceneMonuments)
                yield return monument;
        }

        private static bool MonumentMatches(Component monument, string needle)
        {
            if (monument == null || string.IsNullOrWhiteSpace(needle))
                return false;

            if (ContainsIgnoreCase(monument.name, needle))
                return true;

            var displayPhrase = HarmonyLib.Traverse.Create(monument).Field("displayPhrase").GetValue()
                                ?? HarmonyLib.Traverse.Create(monument).Property("displayPhrase").GetValue();
            if (displayPhrase != null)
            {
                var token = HarmonyLib.Traverse.Create(displayPhrase).Field("token").GetValue()
                           ?? HarmonyLib.Traverse.Create(displayPhrase).Property("token").GetValue();
                var english = HarmonyLib.Traverse.Create(displayPhrase).Field("english").GetValue()
                             ?? HarmonyLib.Traverse.Create(displayPhrase).Property("english").GetValue();

                if (ContainsIgnoreCase(token?.ToString(), needle))
                    return true;

                if (ContainsIgnoreCase(english?.ToString(), needle))
                    return true;
            }

            return false;
        }

        private static bool ContainsIgnoreCase(string value, string needle)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int ScoreMonumentCandidate(Component monument, string needle)
        {
            var score = 0;
            var objectName = monument?.name ?? string.Empty;
            var displayText = GetDisplayText(monument);
            var combined = (objectName + " " + displayText).ToLowerInvariant();

            if (ContainsIgnoreCase(objectName, needle))
                score += 250;
            if (ContainsIgnoreCase(displayText, needle))
                score += 250;
            if (combined.Contains("outpost"))
                score += 120;
            if (combined.Contains("compound"))
                score += 120;
            if (GetBoolMember(monument, "IsSafeZone"))
                score += 120;

            return score;
        }

        private static string GetMonumentDebugName(Component monument)
        {
            if (monument == null)
                return string.Empty;

            var displayPhrase = HarmonyLib.Traverse.Create(monument).Field("displayPhrase").GetValue()
                                ?? HarmonyLib.Traverse.Create(monument).Property("displayPhrase").GetValue();
            if (displayPhrase != null)
            {
                var token = HarmonyLib.Traverse.Create(displayPhrase).Field("token").GetValue()
                           ?? HarmonyLib.Traverse.Create(displayPhrase).Property("token").GetValue();
                if (!string.IsNullOrWhiteSpace(token?.ToString()))
                    return token.ToString();
            }

            return monument.name ?? string.Empty;
        }

        private static string GetDisplayText(object source)
        {
            if (source == null)
                return string.Empty;

            var displayPhrase = HarmonyLib.Traverse.Create(source).Field("displayPhrase").GetValue()
                                ?? HarmonyLib.Traverse.Create(source).Property("displayPhrase").GetValue();
            if (displayPhrase == null)
                return string.Empty;

            var english = HarmonyLib.Traverse.Create(displayPhrase).Field("english").GetValue()
                          ?? HarmonyLib.Traverse.Create(displayPhrase).Property("english").GetValue();
            var token = HarmonyLib.Traverse.Create(displayPhrase).Field("token").GetValue()
                        ?? HarmonyLib.Traverse.Create(displayPhrase).Property("token").GetValue();
            return ((english?.ToString() ?? string.Empty) + " " + (token?.ToString() ?? string.Empty)).Trim();
        }

        private static bool GetBoolMember(object source, string memberName)
        {
            if (source == null)
                return false;

            var value = HarmonyLib.Traverse.Create(source).Field(memberName).GetValue()
                        ?? HarmonyLib.Traverse.Create(source).Property(memberName).GetValue();
            return value is bool b && b;
        }

        private static IList GetTerrainMetaCollection(string parentPropertyName, string childFieldName)
        {
            var terrainMetaType = HarmonyLib.AccessTools.TypeByName("TerrainMeta");
            if (terrainMetaType == null)
                return null;

            var parent = HarmonyLib.AccessTools.Property(terrainMetaType, parentPropertyName)?.GetValue(null, null);
            if (parent == null)
                return null;

            return HarmonyLib.AccessTools.Field(parent.GetType(), childFieldName)?.GetValue(parent) as IList
                   ?? HarmonyLib.AccessTools.Property(parent.GetType(), childFieldName)?.GetValue(parent, null) as IList;
        }

        private static bool TryGetMapCenter(out Vector3 center)
        {
            center = Vector3.zero;
            var terrainMetaType = HarmonyLib.AccessTools.TypeByName("TerrainMeta");
            if (terrainMetaType == null)
                return false;

            var position = HarmonyLib.AccessTools.Property(terrainMetaType, "Position")?.GetValue(null, null);
            var size = HarmonyLib.AccessTools.Property(terrainMetaType, "Size")?.GetValue(null, null);
            if (position is not Vector3 pos || size is not Vector3 sz)
                return false;

            center = pos + sz * 0.5f;
            return true;
        }

        private static float HorizontalDistanceSq(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static bool TryLoadConfig(out PortalTransferConfigData config, out string error)
        {
            config = null;
            error = null;

            if (!File.Exists(ConfigPath))
            {
                error = "missing config '" + ConfigPath + "'";
                return false;
            }

            try
            {
                config = JsonConvert.DeserializeObject<PortalTransferConfigData>(File.ReadAllText(ConfigPath));
                if (config == null)
                {
                    error = "config deserialized to null";
                    return false;
                }

                config.Portals ??= new List<PortalDefinition>();
                foreach (var portal in config.Portals)
                {
                    if (portal == null)
                        continue;
                    portal.Name ??= string.Empty;
                    portal.NexusTransferTargetZoneKey ??= string.Empty;
                    portal.EntranceAnchors ??= new List<PortalAnchorDefinition>();
                    foreach (var anchor in portal.EntranceAnchors)
                    {
                        if (anchor == null)
                            continue;
                        anchor.MonumentNameContains ??= "compound";
                        anchor.WorldPosition ??= new Vec3Data();
                        anchor.WorldEulerAngles ??= new Vec3Data();
                        anchor.LocalPosition ??= new Vec3Data();
                        anchor.LocalEulerAngles ??= new Vec3Data();
                        anchor.WorldPositionOffset ??= new Vec3Data();
                        anchor.FixedWorldLocalOffset ??= new Vec3Data();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private sealed class PortalTransferConfigData
        {
            [JsonProperty("Portals")]
            public List<PortalDefinition> Portals { get; set; } = new();
        }

        private sealed class PortalDefinition
        {
            [JsonProperty("Name")]
            public string Name { get; set; } = string.Empty;

            [JsonProperty("NexusTransferTargetZoneKey")]
            public string NexusTransferTargetZoneKey { get; set; } = string.Empty;

            [JsonProperty("EntranceAnchors")]
            public List<PortalAnchorDefinition> EntranceAnchors { get; set; } = new();
        }

        private sealed class PortalAnchorDefinition
        {
            [JsonProperty("UseFixedWorldTransform")]
            public bool UseFixedWorldTransform { get; set; }

            [JsonProperty("UseMonumentRelativeTransform")]
            public bool UseMonumentRelativeTransform { get; set; }

            [JsonProperty("MonumentNameContains")]
            public string MonumentNameContains { get; set; } = "compound";

            [JsonProperty("WorldPosition")]
            public Vec3Data WorldPosition { get; set; } = new();

            [JsonProperty("WorldEulerAngles")]
            public Vec3Data WorldEulerAngles { get; set; } = new();

            [JsonProperty("LocalPosition")]
            public Vec3Data LocalPosition { get; set; } = new();

            [JsonProperty("LocalEulerAngles")]
            public Vec3Data LocalEulerAngles { get; set; } = new();

            [JsonProperty("WorldPositionOffset")]
            public Vec3Data WorldPositionOffset { get; set; } = new();

            [JsonProperty("FixedWorldLocalOffset")]
            public Vec3Data FixedWorldLocalOffset { get; set; } = new();
        }

        private sealed class Vec3Data
        {
            [JsonProperty("x")]
            public float X { get; set; }

            [JsonProperty("y")]
            public float Y { get; set; }

            [JsonProperty("z")]
            public float Z { get; set; }

            public Vector3 ToVector3()
            {
                return new Vector3(X, Y, Z);
            }
        }
    }
}
