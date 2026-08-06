using System;
using System.Collections.Generic;
using System.Reflection;
using RustEditStandalone.Core;
using UnityEngine;

namespace RustEditStandalone.Features;

public static class CustomTopologyFeature
{
    private const string Prefix = "custom_topology_";
    private static readonly Dictionary<string, byte[]> RawMaps = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, TerrainTopologyMap> Maps = new(StringComparer.OrdinalIgnoreCase);

    public static void Initialize()
    {
        RustEditHub.OnLoaded += Load;
    }

    public static void Shutdown()
    {
        RustEditHub.OnLoaded -= Load;
        ClearMaps();
    }

    private static void ClearMaps()
    {
        foreach (var kv in Maps)
        {
            if (kv.Value != null)
                UnityEngine.Object.Destroy(kv.Value.gameObject);
        }
        Maps.Clear();
        RawMaps.Clear();
    }

    private static void Load()
    {
        ClearMaps();

        MapDataHelper.ForEachCustomLayer((name, data) =>
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                return;
            string key = name.Substring(Prefix.Length);
            RawMaps[key] = data;

            try
            {
                var live = TerrainMeta.TopologyMap;
                if (live == null) return;

                FieldInfo resField = typeof(TerrainMap).GetField("res", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                int res = resField != null ? (int)resField.GetValue(live) : 0;
                if (res <= 0) return;

                var go = new GameObject("RustEdit_Topology_" + key);
                UnityEngine.Object.DontDestroyOnLoad(go);
                var map = go.AddComponent<TerrainTopologyMap>();
                resField.SetValue(map, res);
                map.InitArrays(res * res);
                map.FromByteArray(data);
                Maps[key] = map;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RustEditStandalone] Topology layer raw-only (" + key + "): " + ex.Message);
            }

            Debug.Log("[RustEditStandalone] Custom topology layer: " + key);
        });
    }

    public static string[] GetNames()
    {
        var arr = new string[RawMaps.Count];
        RawMaps.Keys.CopyTo(arr, 0);
        return arr;
    }

    public static bool TryGet(string name, out TerrainTopologyMap map) => Maps.TryGetValue(name, out map) && map != null;
}
