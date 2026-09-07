using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// Custom outpost.map rows often carry <see cref="AddToHeightMap"/> colliders. Those ProceduralObjects
    /// do not stamp terrain until ProcessProceduralObjects — after cliffs/decor — which leaves floating
    /// trees and ore around the carved compound pad. Flush the stamps immediately after the live swap,
    /// then strip anything still hovering after gen and after SpawnHandler.InitialSpawn.
    /// </summary>
    internal static class OutpostTerrainFix
    {
        internal const float CleanupRadius = 280f;
        internal const float FloatThreshold = 1.75f;

        static readonly FieldInfo ProceduralObjectsField = typeof(WorldSetup).GetField(
            "ProceduralObjects",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal static void FlushOutpostHeightStamps(Vector3 origin, float radius, bool debugLogging)
        {
            var setup = SingletonComponent<WorldSetup>.Instance;
            if (setup == null || ProceduralObjectsField == null)
                return;

            if (!(ProceduralObjectsField.GetValue(setup) is IList list) || list.Count == 0)
                return;

            float radiusSq = radius * radius;
            int flushed = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var procedural = list[i] as ProceduralObject;
                if (procedural == null || procedural.transform == null)
                    continue;
                if (!(procedural is AddToHeightMap))
                    continue;

                Vector3 pos = procedural.transform.position;
                float dx = pos.x - origin.x;
                float dz = pos.z - origin.z;
                if (dx * dx + dz * dz > radiusSq)
                    continue;

                try
                {
                    procedural.Process();
                    flushed++;
                }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogWarning("[CustomMapGen] Outpost AddToHeightMap stamp failed: " + ex.Message);
                }
                list.RemoveAt(i);
            }

            if (debugLogging)
                UnityEngine.Debug.Log($"[CustomMapGen] Flushed {flushed} outpost AddToHeightMap stamp(s) immediately after live swap (before cliffs/decor).");
        }

        /// <summary>
        /// Flatten a pad to placement Y (raise valleys and lower peaks) so the monument sits in the
        /// ground instead of floating. Fade keeps the edge from becoming a cliff.
        /// </summary>
        internal static void SeatMonumentOnTerrain(Vector3 origin, float radius, float fade, bool debugLogging, string label)
        {
            if (TerrainMeta.HeightMap == null)
                return;
            TerrainMeta.HeightMap.SetHeight(origin, 1f, radius, fade);
            if (debugLogging)
                UnityEngine.Debug.Log($"[CustomMapGen] Seated {label} pad to Y={origin.y:F2} (flatten radius={radius:F0} fade={fade:F0}).");
        }

        internal static void RemoveFloatingResourcesNearOutpost(string phase, bool includeEntities)
        {
            if (!World_AddPrefab_Patch.LiveOutpostSwapApplied)
                return;

            Vector3 origin = World_AddPrefab_Patch.CenterOutpostPositionIfCached
                ?? TerrainMeta.Position + TerrainMeta.Size * 0.5f;
            if (TerrainMeta.HeightMap == null)
                return;

            int removed = 0;
            float radiusSq = CleanupRadius * CleanupRadius;

            if (World.SpawnedPrefabs != null && World.SpawnedPrefabs.TryGetValue("Decor", out HashSet<GameObject> decor) && decor != null)
            {
                var snapshot = new List<GameObject>(decor);
                foreach (GameObject go in snapshot)
                    removed += TryDestroyIfFloating(go, origin, radiusSq);
            }

            if (includeEntities && BaseNetworkable.serverEntities != null)
            {
                var toKill = new List<BaseEntity>();
                foreach (BaseNetworkable net in BaseNetworkable.serverEntities)
                {
                    var entity = net as BaseEntity;
                    if (entity == null || entity.IsDestroyed)
                        continue;
                    if (!(entity is TreeEntity) && !(entity is OreResourceEntity) && !(entity is CollectibleEntity))
                        continue;
                    Vector3 pos = entity.transform.position;
                    float dx = pos.x - origin.x;
                    float dz = pos.z - origin.z;
                    if (dx * dx + dz * dz > radiusSq)
                        continue;
                    float terrainY = TerrainMeta.HeightMap.GetHeight(pos);
                    if (pos.y - terrainY <= FloatThreshold)
                        continue;
                    toKill.Add(entity);
                }
                foreach (BaseEntity entity in toKill)
                {
                    entity.Kill();
                    removed++;
                }
            }

            var config = CustomMapGen.Instance?.GetConfig();
            if (removed > 0 && config != null && config.DebugLogging)
                UnityEngine.Debug.Log($"[CustomMapGen] Removed {removed} floating tree/node/decor object(s) near center outpost ({phase}).");
        }

        private static int TryDestroyIfFloating(GameObject go, Vector3 origin, float radiusSq)
        {
            if (go == null)
                return 0;
            Vector3 pos = go.transform.position;
            float dx = pos.x - origin.x;
            float dz = pos.z - origin.z;
            if (dx * dx + dz * dz > radiusSq)
                return 0;
            float terrainY = TerrainMeta.HeightMap.GetHeight(pos);
            if (pos.y - terrainY <= FloatThreshold)
                return 0;
            UnityEngine.Object.Destroy(go);
            return 1;
        }
    }

    [HarmonyPatch(typeof(ProcessProceduralObjects), nameof(ProcessProceduralObjects.Process))]
    public static class ProcessProceduralObjects_RemoveFloatingOutpostDecor_Patch
    {
        static void Postfix()
        {
            if (!CustomMapGen.IsCustomMapGenEnabled() || World.Cached || World.Networked)
                return;
            OutpostTerrainFix.RemoveFloatingResourcesNearOutpost("ProceduralObjects", includeEntities: false);
        }
    }

    [HarmonyPatch(typeof(SpawnHandler), nameof(SpawnHandler.InitialSpawn))]
    public static class SpawnHandler_InitialSpawn_RemoveFloatingOutpostResources_Patch
    {
        static void Postfix()
        {
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;
            OutpostTerrainFix.RemoveFloatingResourcesNearOutpost("InitialSpawn", includeEntities: true);
        }
    }
}
