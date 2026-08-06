using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using UnityEngine;

namespace RustEditStandalone.Core;

/// <summary>
/// Map-layer lookup matching Oxide.Ext.RustEdit Helper.GetMap:
/// XOR logical name with prefab count, optional AES Base64 name, then Xml deserialize.
/// Always falls back to scanning non-terrain layers.
/// </summary>
public static class MapDataHelper
{
    public static int PrefabCount { get; set; }

    private static readonly HashSet<string> TerrainLayers = new(StringComparer.OrdinalIgnoreCase)
    {
        "height", "splat", "biome", "topology", "alpha", "water", "terrain"
    };

    // Common salts used by RustEdit-era encryption; unknown salt is non-fatal (XOR + scan still work).
    private static readonly byte[][] AesSalts =
    {
        Encoding.UTF8.GetBytes("RustEditMapSalt"),
        Encoding.UTF8.GetBytes("Oxide.Ext.RustEdit"),
        Encoding.ASCII.GetBytes("salt"),
        new byte[] { 0x52, 0x75, 0x73, 0x74, 0x45, 0x64, 0x69, 0x74 }
    };

    public static void SyncPrefabCount()
    {
        try
        {
            if (World.Serialization?.world?.prefabs != null)
                PrefabCount = World.Serialization.world.prefabs.Count;
        }
        catch { /* ignore */ }
    }

    public static string XorName(string logicalName, int prefabCount)
    {
        if (string.IsNullOrEmpty(logicalName)) return string.Empty;
        var sb = new StringBuilder(logicalName.Length);
        for (int i = 0; i < logicalName.Length; i++)
            sb.Append((char)(logicalName[i] ^ prefabCount));
        return sb.ToString();
    }

    public static byte[] GetMapBytes(string logicalName)
    {
        if (string.IsNullOrEmpty(logicalName)) return null;
        SyncPrefabCount();
        int count = PrefabCount;

        try
        {
            byte[] xored = World.GetMap(XorName(logicalName, count));
            if (xored != null && xored.Length > 0) return xored;
        }
        catch { /* ignore */ }

        try
        {
            byte[] plain = World.GetMap(logicalName);
            if (plain != null && plain.Length > 0) return plain;
        }
        catch { /* ignore */ }

        for (int i = 0; i < AesSalts.Length; i++)
        {
            try
            {
                string aesName = AesEncryptMapName(logicalName, count, AesSalts[i]);
                if (string.IsNullOrEmpty(aesName)) continue;
                byte[] bytes = World.GetMap(aesName);
                if (bytes != null && bytes.Length > 0) return bytes;
            }
            catch { /* ignore */ }
        }

        return null;
    }

    public static bool TryGetMapXml<T>(string logicalName, out T result) where T : class
    {
        result = null;
        byte[] bytes = GetMapBytes(logicalName);
        if (bytes != null && TryDeserializeXml(bytes, out result) && result != null)
            return true;

        return TryScanLayersXml(out result);
    }

    public static bool TryGetMapXml(string[] logicalNames, Type type, out object result)
    {
        result = null;
        if (logicalNames != null)
        {
            for (int i = 0; i < logicalNames.Length; i++)
            {
                byte[] bytes = GetMapBytes(logicalNames[i]);
                if (bytes != null && TryDeserializeXml(bytes, type, out result) && result != null)
                    return true;
            }
        }
        return TryScanLayersXml(type, out result);
    }

    public static bool TryGetMapXml<T>(string[] logicalNames, out T result) where T : class
    {
        result = null;
        if (TryGetMapXml(logicalNames, typeof(T), out object obj) && obj is T typed)
        {
            result = typed;
            return true;
        }
        return false;
    }

    public static bool TryDeserializeXml<T>(byte[] data, out T result) where T : class
    {
        result = null;
        if (!TryDeserializeXml(data, typeof(T), out object obj)) return false;
        result = obj as T;
        return result != null;
    }

    public static bool TryDeserializeXml(byte[] data, Type type, out object result)
    {
        result = null;
        if (data == null || data.Length < 4 || type == null) return false;
        try
        {
            using var ms = new MemoryStream(data);
            var serializer = new XmlSerializer(type);
            result = serializer.Deserialize(ms);
            return result != null;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryScanLayersXml<T>(out T result) where T : class
    {
        result = null;
        if (!TryScanLayersXml(typeof(T), out object obj)) return false;
        result = obj as T;
        return result != null;
    }

    public static bool TryScanLayersXml(Type type, out object result)
    {
        result = null;
        var maps = World.Serialization?.world?.maps;
        if (maps == null) return false;

        for (int i = 0; i < maps.Count; i++)
        {
            var map = maps[i];
            if (map?.data == null || map.data.Length < 10) continue;
            if (TerrainLayers.Contains(map.name ?? string.Empty)) continue;
            if (TryDeserializeXml(map.data, type, out result) && result != null)
                return true;
        }
        return false;
    }

    public static void ForEachCustomLayer(Action<string, byte[]> action)
    {
        if (action == null) return;
        var maps = World.Serialization?.world?.maps;
        if (maps == null) return;
        for (int i = 0; i < maps.Count; i++)
        {
            var map = maps[i];
            if (map?.data == null || map.data.Length < 10) continue;
            if (TerrainLayers.Contains(map.name ?? string.Empty)) continue;
            action(map.name ?? string.Empty, map.data);
        }
    }

    public static string GetFilenameFromCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) return string.Empty;
        string[] parts = category.Split('_', ':');
        if (parts.Length > 3) return parts[3];
        int lastSlash = category.LastIndexOf('/');
        string name = lastSlash >= 0 ? category.Substring(lastSlash + 1) : category;
        int dot = name.IndexOf('.');
        return dot > 0 ? name.Substring(0, dot) : name;
    }

    public static string GetPrefabKey(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        int lastSlash = path.LastIndexOf('/');
        string segment = lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
        int dot = segment.LastIndexOf('.');
        return (dot > 0 ? segment.Substring(0, dot) : segment).Trim();
    }

    public static T InstantiatePrefab<T>(string prefabPath, Vector3 position, Quaternion rotation) where T : Component
    {
        if (string.IsNullOrEmpty(prefabPath)) return null;
        GameObject prefab = GameManager.server.FindPrefab(prefabPath);
        if (prefab == null)
        {
            Debug.LogWarning("[RustEditStandalone] Prefab not found: " + prefabPath);
            return null;
        }

        var spawnable = prefab.GetComponent<Spawnable>();
        if (spawnable != null)
            UnityEngine.Object.DestroyImmediate(spawnable, true);

        if (!prefab.activeSelf)
            prefab.SetActive(true);

        GameObject go = UnityEngine.Object.Instantiate(prefab, position, rotation);
        if (go == null) return null;
        go.name = prefabPath;
        go.SetActive(true);

        var entity = go.GetComponent<BaseEntity>();
        if (entity != null)
        {
            entity.enableSaving = false;
            entity.Spawn();
        }
        return go.GetComponent<T>();
    }

    private static string AesEncryptMapName(string logicalName, int prefabCount, byte[] salt)
    {
        if (salt == null || salt.Length == 0) return null;
        byte[] bytes = Encoding.Unicode.GetBytes(logicalName);
        using var aes = Aes.Create();
        var derive = new Rfc2898DeriveBytes(prefabCount.ToString(), salt);
        aes.Key = derive.GetBytes(32);
        aes.IV = derive.GetBytes(16);
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            cs.Write(bytes, 0, bytes.Length);
            cs.FlushFinalBlock();
        }
        return Convert.ToBase64String(ms.ToArray());
    }
}
