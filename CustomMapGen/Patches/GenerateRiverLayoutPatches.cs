using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// When TrySpawningOutpostInCenter is on, rivers are laid out before the outpost is
    /// redirected to map center. Without clipping, a rare center-crossing river keeps its
    /// full path and ends up as floating river mesh/terrain on top of the outpost.
    /// </summary>
    [HarmonyPatch(typeof(GenerateRiverLayout), nameof(GenerateRiverLayout.Process))]
    public static class GenerateRiverLayout_Process_Patch
    {
        /// <summary>
        /// Keep path centerlines outside this radius of map center so Width/OuterFade
        /// (8 + 64) do not carve into the center outpost footprint (~180m clear).
        /// </summary>
        private const float CenterExclusionRadius = 250f;

        /// <summary>PathInterpolator requires at least 2 points; keep a usable stub minimum.</summary>
        private const int MinRemainingPoints = 8;

        /// <summary>Drop truncated stubs that would only be a short ditch beside the outpost.</summary>
        private const float MinRemainingLength = 120f;

        static bool Prefix(GenerateRiverLayout __instance, ref uint seed)
        {
            if (CustomMapGen.IsCustomMapGenEnabled())
            {
                var config = CustomMapGen.Instance.GetConfig();
                // Isolate topology/issues: set DisableRiverLayoutPatch=true to skip our patch (vanilla river behavior).
                if (config.DisableRiverLayoutPatch)
                    return true;
                if (config.RemoveRivers)
                {
                    UnityEngine.Debug.Log("[CustomMapGen] Rivers disabled - skipping GenerateRiverLayout.Process()");
                    return false;
                }
            }
            return true;
        }

        static void Postfix(GenerateRiverLayout __instance, uint seed)
        {
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;

            var config = CustomMapGen.Instance.GetConfig();
            if (config.DisableRiverLayoutPatch || config.RemoveRivers)
                return;
            if (!config.TrySpawningOutpostInCenter)
                return;

            ClipRiversAwayFromCenterOutpost(config.DebugLogging);
        }

        private static void ClipRiversAwayFromCenterOutpost(bool debugLogging)
        {
            var rivers = TerrainPathAccess.GetRivers(TerrainMeta.Path);
            if (rivers == null || rivers.Count == 0)
                return;

            Vector3 mapCenter = TerrainMeta.Position + TerrainMeta.Size * 0.5f;
            float radiusSq = CenterExclusionRadius * CenterExclusionRadius;
            int truncated = 0;
            int removed = 0;

            for (int i = rivers.Count - 1; i >= 0; i--)
            {
                PathList river = rivers[i];
                if (river?.Path?.Points == null || river.Path.Points.Length < 2)
                {
                    rivers.RemoveAt(i);
                    removed++;
                    continue;
                }

                Vector3[] points = river.Path.Points;
                if (!TryFindLongestSegmentOutsideRadius(points, mapCenter, radiusSq, out int segStart, out int segEndExclusive))
                {
                    if (debugLogging)
                        UnityEngine.Debug.Log($"[CustomMapGen] Removing {river.Name}: entirely inside center outpost exclusion ({CenterExclusionRadius:F0}m).");
                    rivers.RemoveAt(i);
                    removed++;
                    continue;
                }

                // Full path already clear of center.
                if (segStart == 0 && segEndExclusive == points.Length)
                    continue;

                int keepCount = segEndExclusive - segStart;
                float keepLength = MeasureLength(points, segStart, segEndExclusive);
                if (keepCount < MinRemainingPoints || keepLength < MinRemainingLength)
                {
                    if (debugLogging)
                        UnityEngine.Debug.Log($"[CustomMapGen] Removing {river.Name}: after center clip only {keepCount} pts / {keepLength:F0}m remain (min {MinRemainingPoints} pts / {MinRemainingLength:F0}m).");
                    rivers.RemoveAt(i);
                    removed++;
                    continue;
                }

                var kept = new Vector3[keepCount];
                for (int p = 0; p < keepCount; p++)
                    kept[p] = points[segStart + p];

                river.Path.Points = kept;
                river.Path.MinIndex = river.Path.DefaultMinIndex;
                river.Path.MaxIndex = river.Path.DefaultMaxIndex;
                river.Path.Smoothen(2, new Vector3(1f, 0f, 1f));
                river.Path.RecalculateTangents();
                truncated++;

                if (debugLogging)
                    UnityEngine.Debug.Log($"[CustomMapGen] Truncated {river.Name} around center outpost: kept pts [{segStart}..{segEndExclusive}) of {points.Length} ({keepLength:F0}m).");
            }

            if (truncated > 0 || removed > 0)
                UnityEngine.Debug.Log($"[CustomMapGen] Center-outpost river clip: truncated={truncated}, removed={removed}, remaining={rivers.Count} (exclusion={CenterExclusionRadius:F0}m).");
        }

        /// <summary>
        /// Finds the longest contiguous run of points whose XZ distance from center is outside the exclusion radius.
        /// Returns false if no usable outside segment exists.
        /// </summary>
        private static bool TryFindLongestSegmentOutsideRadius(Vector3[] points, Vector3 mapCenter, float radiusSq, out int bestStart, out int bestEndExclusive)
        {
            bestStart = 0;
            bestEndExclusive = 0;
            int bestLen = 0;
            int runStart = -1;

            for (int i = 0; i <= points.Length; i++)
            {
                bool outside = i < points.Length && IsOutsideExclusion(points[i], mapCenter, radiusSq);
                if (outside)
                {
                    if (runStart < 0)
                        runStart = i;
                    continue;
                }

                if (runStart >= 0)
                {
                    int runLen = i - runStart;
                    if (runLen > bestLen)
                    {
                        bestLen = runLen;
                        bestStart = runStart;
                        bestEndExclusive = i;
                    }
                    runStart = -1;
                }
            }

            return bestLen >= 2;
        }

        private static bool IsOutsideExclusion(Vector3 point, Vector3 mapCenter, float radiusSq)
        {
            float dx = point.x - mapCenter.x;
            float dz = point.z - mapCenter.z;
            return dx * dx + dz * dz > radiusSq;
        }

        private static float MeasureLength(Vector3[] points, int start, int endExclusive)
        {
            float length = 0f;
            for (int i = start; i < endExclusive - 1; i++)
                length += (points[i + 1] - points[i]).magnitude;
            return length;
        }
    }
}
