using HarmonyLib;
using System.Reflection;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace CustomMapGen.Patches
{
    // Shore flatten runs after ProcessProceduralObjects so TerrainMeta.WaterMap is populated (AddToWaterMap runs there).
    // ProcessProceduralObjects.Process runs multiple times; the first run (e.g. "Loading Monument Prefabs") has water/height
    // not fully ready → 0 cells. We run only on the second invocation so shore flatten applies when data is ready.
    [HarmonyPatch(typeof(ProcessProceduralObjects), nameof(ProcessProceduralObjects.Process))]
    public static class GenerateErosion_ShoreFlatten_Patch
    {
        private static int _processInvocationCount;

        /// <summary>Reset at start of each map gen so shore flatten runs on 2nd ProcessProceduralObjects call.</summary>
        internal static void ResetInvocationCount()
        {
            _processInvocationCount = 0;
        }

        static void Postfix(ProcessProceduralObjects __instance, uint seed)
        {
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;
            if (CustomMapGen.IsLoadingExistingMap)
                return;

            _processInvocationCount++;

            if (_processInvocationCount != 2)
                return;

            var config = CustomMapGen.Instance.GetConfig();

            // Isolate ocean-topology issues: set DisableShoreFlattenPatch=true in config to skip this patch entirely.
            if (config.DisableShoreFlattenPatch)
            {
                if (config.DebugLogging)
                    UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] Shore flatten patch DISABLED (DisableShoreFlattenPatch=true).");
                return;
            }

            if (config.DebugLogging)
                UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] Shore flatten patch entered; FlattenShoreAndBay=" + (config.TerrainConfiguration != null && config.TerrainConfiguration.FlattenShoreAndBay));
            if (config.TerrainConfiguration == null || !config.TerrainConfiguration.FlattenShoreAndBay)
                return;

            TerrainHeightMap heightMap = TerrainMeta.HeightMap;
            TerrainWaterMap waterMap = TerrainMeta.WaterMap;
            
            if (config.DebugLogging)
                UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Shore flatten: heightMap={heightMap != null}, waterMap={waterMap != null}");
            if (heightMap == null || waterMap == null)
                return;
            
            // Use reflection to access internal members (res on TerrainMap, dst on TerrainMap<short>)
            var resField = typeof(TerrainHeightMap).BaseType?.GetField("res", System.Reflection.BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                var dstField = typeof(TerrainHeightMap).BaseType?.GetField("dst", System.Reflection.BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (resField == null || dstField == null)
                {
                    UnityEngine.Debug.LogWarning("[CustomMapGen] Could not access height map fields for shore flattening");
                    if (config.DebugLogging)
                        UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] Shore flatten: resField=" + (resField != null) + ", dstField=" + (dstField != null));
                    return;
                }
                
                int res = (int)resField.GetValue(heightMap);
                NativeArray<short> heightData = (NativeArray<short>)dstField.GetValue(heightMap);
                if (config.DebugLogging)
                    UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Shore flatten: res={res}, heightData valid={heightData.IsCreated}");
                
                float flattenRadius = 50f; // Distance from water to flatten (in world units)
                float flattenStrength = 0.5f; // How much to flatten (0-1)
                
                // Create a temporary height buffer
                NativeArray<short> smoothedHeights = new NativeArray<short>(res * res, Allocator.Temp);
                
                // Copy original heights
                for (int i = 0; i < res * res; i++)
                {
                    smoothedHeights[i] = heightData[i];
                }
            
                // Build skip set: cells near rivers or lakes so we don't overwrite river/lake terrain
                var skipFlatten = new bool[res * res];
                float worldSize = TerrainMeta.Size.x;
                const float riverLakeMargin = 80f;
                if (TerrainMeta.Path != null)
                {
                    var riversPath = TerrainPathAccess.GetRivers(TerrainMeta.Path);
                    if (riversPath != null)
                    {
                        foreach (var river in riversPath)
                        {
                            if (river?.Path?.Points == null) continue;
                            foreach (var p in river.Path.Points)
                            {
                                int gx = (int)((p.x / worldSize + 0.5f) * (res - 1));
                                int gz = (int)((p.z / worldSize + 0.5f) * (res - 1));
                                int margin = (int)(riverLakeMargin / worldSize * (res - 1));
                                for (int dx = -margin; dx <= margin; dx++)
                                    for (int dz = -margin; dz <= margin; dz++)
                                    {
                                        int nx = gx + dx, nz = gz + dz;
                                        if (nx >= 0 && nx < res && nz >= 0 && nz < res)
                                            skipFlatten[nx * res + nz] = true;
                                    }
                            }
                        }
                    }
                    var lakeObjsPath = TerrainPathAccess.GetLakeObjs(TerrainMeta.Path);
                    if (lakeObjsPath != null)
                    {
                        foreach (var lake in lakeObjsPath)
                        {
                            if (lake?.transform == null) continue;
                            var pos = lake.transform.position;
                            int gx = (int)((pos.x / worldSize + 0.5f) * (res - 1));
                            int gz = (int)((pos.z / worldSize + 0.5f) * (res - 1));
                            int margin = (int)(riverLakeMargin / worldSize * (res - 1));
                            for (int dx = -margin; dx <= margin; dx++)
                                for (int dz = -margin; dz <= margin; dz++)
                                {
                                    int nx = gx + dx, nz = gz + dz;
                                    if (nx >= 0 && nx < res && nz >= 0 && nz < res)
                                        skipFlatten[nx * res + nz] = true;
                                }
                        }
                    }
                }

                // Smooth heights near water (ocean only; skip river/lake cells so we don't overwrite carved terrain)
                int cellsFlattened = 0;
                for (int x = 1; x < res - 1; x++)
                {
                    for (int z = 1; z < res - 1; z++)
                    {
                        if (skipFlatten[x * res + z])
                            continue;
                        float normX = heightMap.Coordinate(x);
                        float normZ = heightMap.Coordinate(z);
                        
                        // Check if near water
                        float waterLevel = waterMap.GetHeight(normX, normZ);
                        float terrainHeight = heightMap.GetHeight(normX, normZ);
                        
                        if (waterLevel > 0f && terrainHeight < waterLevel + flattenRadius)
                        {
                            // Average with neighbors to flatten
                            float avgHeight = 0f;
                            int count = 0;
                            
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                for (int dz = -1; dz <= 1; dz++)
                                {
                                    int nx = x + dx;
                                    int nz = z + dz;
                                    if (nx >= 0 && nx < res && nz >= 0 && nz < res)
                                    {
                                        float neighborNormX = heightMap.Coordinate(nx);
                                        float neighborNormZ = heightMap.Coordinate(nz);
                                        avgHeight += heightMap.GetHeight(neighborNormX, neighborNormZ);
                                        count++;
                                    }
                                }
                            }
                            
                            if (count > 0)
                            {
                                avgHeight /= count;
                                float smoothed = Mathf.Lerp(terrainHeight, avgHeight, flattenStrength);
                                smoothedHeights[x * res + z] = (short)Mathf.Clamp(smoothed, short.MinValue, short.MaxValue);
                                cellsFlattened++;
                            }
                        }
                    }
                }
            
                // Apply smoothed heights
                for (int i = 0; i < res * res; i++)
                {
                    heightData[i] = smoothedHeights[i];
                }
                
                smoothedHeights.Dispose();

                if (config.DebugLogging)
                    UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Shore flatten: applied to {cellsFlattened} cells (res={res})");

                // DEBUG: Log mainland cells that have ocean topology (0x80). Use with DisableShoreFlattenPatch/DisablePowerlineLayoutPatch/DisableRailPatch/DisableRiverLayoutPatch to isolate which patch (if any) affects topology.
                TerrainTopologyMap topoMap = TerrainMeta.TopologyMap;
                if (config.DebugLogging && topoMap != null)
                {
                    float sizeX = TerrainMeta.Size.x;
                    float sizeZ = TerrainMeta.Size.z;
                    const int oceanMask = 0x80;
                    const float mainlandHeight01 = 0.5f;
                    int topoRes = res;
                    int mainlandOceanCount = 0;
                    var sampleCells = new System.Collections.Generic.List<string>();
                    int maxSamples = 50;
                    for (int tz = 0; tz < topoRes && sampleCells.Count < maxSamples; tz++)
                    {
                        for (int tx = 0; tx < topoRes && sampleCells.Count < maxSamples; tx++)
                        {
                            float normX = heightMap.Coordinate(tx);
                            float normZ = heightMap.Coordinate(tz);
                            if (heightMap.GetHeight01(normX, normZ) <= mainlandHeight01)
                                continue;
                            int current = topoMap.GetTopology(tx, tz);
                            if ((current & oceanMask) == 0)
                                continue;
                            mainlandOceanCount++;
                            float worldX = (normX - 0.5f) * sizeX;
                            float worldZ = (normZ - 0.5f) * sizeZ;
                            int grid150X = (int)System.Math.Floor(worldX / 150f);
                            int grid150Z = (int)System.Math.Floor(worldZ / 150f);
                            sampleCells.Add($"cell({tx},{tz}) world({worldX:F0},{worldZ:F0}) grid150({grid150X},{grid150Z})");
                        }
                    }
                    if (mainlandOceanCount > 0)
                    {
                        UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Topology scan: {mainlandOceanCount} mainland cells have ocean (0x80). Source: GenerateOceanTopology or AddToWaterMap (monument prefabs). Disable ShoreFlatten/PowerlineLayout/Rail/RiverLayout patches one by one to isolate. Sample (first {sampleCells.Count}), world=center-based, grid150=floor(world/150):");
                        foreach (var s in sampleCells)
                            UnityEngine.Debug.Log("[CustomMapGen] [DEBUG]   " + s);
                        if (mainlandOceanCount > maxSamples)
                            UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG]   ... and {mainlandOceanCount - maxSamples} more.");
                        UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] Match grid150(x,z) to your grids (e.g. grid150 -3,-12 = v-3 y-12).");
                    }
                }
        }
    }
}

