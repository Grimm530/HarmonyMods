using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

using static CustomGenerator.ExtConfig;
public class SwapMonument {
    private static WorldSerialization _mainMap = new WorldSerialization();
    private static WorldSerialization _swapMap = new WorldSerialization();
    private static List<Monument> monuments = new List<Monument>();
    private static string mapPath = string.Empty;

    public static void Initiate(string path) {
        mapPath = path;
        _mainMap.Load(mapPath);

        Log(_mainMap.world.prefabs.Count);
        LoadMonuments();
        SwapMonuments();

        if (!Config.Swap.SaveBothMaps)
            _mainMap.Save(mapPath);
        else _mainMap.Save(mapPath.Replace(".map", ".swapped.map"));
    }

    private static void SwapMonuments() {
        foreach (Monument monument in monuments) {
            // Build match strings: primary is the prefab shortname from filename.
            // Center "Outpost" safe zone in procedural maps is compound.prefab, not outpost — so when
            // swapping outpost (file outpost.prefab.map or outpost.map), also match "compound".
            var matchNames = new List<string> { monument.prefabShortname };
            string shortnameNorm = monument.prefabShortname.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                ? monument.prefabShortname.Substring(0, monument.prefabShortname.Length - 7)
                : monument.prefabShortname;
            if (string.Equals(shortnameNorm, "outpost", StringComparison.OrdinalIgnoreCase))
                matchNames.Add("compound");

            var matchPrefabs = _mainMap.world.prefabs.Where(x => {
                string name = StringPool.Get(x.id);
                return matchNames.Any(m => name.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);
            }).ToList();

            // For outpost: only replace the monument at MAP CENTER (the safe zone), not any "outpost" at north/corner.
            // The generator can place an outpost at a corner; we only want to swap the center compound with our custom outpost.
            if (string.Equals(shortnameNorm, "outpost", StringComparison.OrdinalIgnoreCase) && matchPrefabs.Count > 1) {
                float centerX = (tempData.mapsize > 0 ? tempData.mapsize : 4000f) / 2f;
                float centerZ = centerX;
                matchPrefabs = matchPrefabs
                    .OrderBy(p => (p.position.x - centerX) * (p.position.x - centerX) + (p.position.z - centerZ) * (p.position.z - centerZ))
                    .Take(1)
                    .ToList();
                Log($"{monument.prefabShortname}: targeting center only (1 prefab at map center)");
            }

            if (matchPrefabs.Count() == 0) continue;
            foreach (var firstfab in matchPrefabs) {
                _swapMap.Load(monument.path);
                // On dedicated server, WorldSerialization.Load() may not populate .world; prefabs can be null/empty.
                if (_swapMap.world == null || _swapMap.world.prefabs == null || _swapMap.world.prefabs.Count == 0) {
                    Log($"{monument.prefabShortname}: swap map loaded 0 prefabs (Load may not populate on dedicated server) — keeping original");
                    continue;
                }
                var replacement = MapHander.CreatePrefabFromMap(firstfab.position, firstfab.rotation, _swapMap.world.prefabs);
                if (replacement == null || replacement.Count == 0) {
                    Log($"{monument.prefabShortname}: created 0 replacement prefabs — keeping original");
                    continue;
                }
                _mainMap.world.prefabs.Remove(firstfab);
                _mainMap.world.prefabs.AddRange(replacement);
                Log($"{monument.prefabShortname}: replaced with {replacement.Count} prefab(s)");
            }
        }
    }

    private static void LoadMonuments() {
        string prefabsDir = Path.Combine(System.Environment.CurrentDirectory ?? ".", "maps", "prefabs");
        if (!Directory.Exists(prefabsDir)) Directory.CreateDirectory(prefabsDir);

        string[] files = Directory.GetFiles(prefabsDir, "*.map");
        foreach (string file in files) {
            if (string.IsNullOrEmpty(file) || !Path.GetFileName(file).EndsWith(".map", StringComparison.OrdinalIgnoreCase)) continue;

            string prefabShortname = Path.GetFileNameWithoutExtension(file);
            string fullPath = Path.GetFullPath(file);
            monuments.Add(new Monument(prefabShortname, fullPath));
        }
    }

    class Monument {
        public string prefabShortname;
        public string path;

        public Monument(string prefabShortname, string path) {
            this.prefabShortname = prefabShortname;
            this.path = path;
        }
    }

    static void Log(object obj) => Debug.Log("[SWAP MN] " + obj);
}


public class MapHander
{
    private static PrefabData CreatePrefab(uint PrefabID, VectorData position, VectorData rotation, VectorData scale, string category = "Monument")
    {
        var prefab = new PrefabData()
        {
            category = category,
            id = PrefabID,
            position = position,
            rotation = rotation,
            scale = scale
        };
        return prefab;
    }

    private static VectorData CalculateLocalPos(VectorData placePos, VectorData globalPos, VectorData rotation) => RotateVector(new VectorData(globalPos.x - placePos.x, globalPos.y - placePos.y, globalPos.z - placePos.z), rotation);

    private static VectorData RotateVector(VectorData vector, VectorData rotation) {
        float radX = rotation.x * (float)Math.PI / 180.0f;
        float radY = rotation.y * (float)Math.PI / 180.0f;
        float radZ = rotation.z * (float)Math.PI / 180.0f;

        float cosX = (float)Math.Cos(radX), sinX = (float)Math.Sin(radX);
        float cosY = (float)Math.Cos(radY), sinY = (float)Math.Sin(radY);
        float cosZ = (float)Math.Cos(radZ), sinZ = (float)Math.Sin(radZ);

        float newY = vector.y * cosX - vector.z * sinX;
        float newZ = vector.y * sinX + vector.z * cosX;
        vector.y = newY;
        vector.z = newZ;

        float newX = vector.x * cosY + vector.z * sinY;
        newZ = vector.z * cosY - vector.x * sinY;
        vector.x = newX;
        vector.z = newZ;

        newX = vector.x * cosZ - vector.y * sinZ;
        newY = vector.x * sinZ + vector.y * cosZ;
        vector.x = newX;
        vector.y = newY;

        return vector;
    }

    public static List<PrefabData> CreatePrefabFromMap(VectorData startPos, VectorData rotation, List<PrefabData> prefabs)
    {
        List<PrefabData> createdPrefabs = new List<PrefabData>();
        bool first = true;
        foreach (var prefab in prefabs) {
            createdPrefabs.Add(
                CreatePrefab(
                    (prefab.id == 2749405185u) ? 504351302u : prefab.id,
                    Calculate(startPos, prefab.position, prefab.scale, prefabs, rotation),
                    first ? rotation : CalculateRot(rotation, prefab.rotation),
                    (prefab.id == 2749405185u) ? new VectorData(0, 0, 0) : prefab.scale,
                    prefab.category
            ));
            first = false;
        }
        return createdPrefabs;
    }

    private static VectorData Calculate(VectorData globalPos, VectorData position, VectorData scale, List<PrefabData> prefabs, VectorData firstPrefabRotation) {
        VectorData localPos = CalculateLocalPos(prefabs[0].position, position, firstPrefabRotation);
        return new VectorData(globalPos.x + localPos.x, globalPos.y + localPos.y, globalPos.z + localPos.z);
    }

    private static VectorData CalculateRot(VectorData globalRot, VectorData localRot) => new VectorData(globalRot.x + localRot.x, globalRot.y + localRot.y, globalRot.z + localRot.z);
}
