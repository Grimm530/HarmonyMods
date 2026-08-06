using System.Collections;
using System.Collections.Generic;
using RustEditStandalone.Components;
using RustEditStandalone.Config;
using RustEditStandalone.Core;
using RustEditStandalone.Data;
using UnityEngine;

namespace RustEditStandalone.Features;

public static class ApcFeature
{
    private static readonly string[] MapKeys = { "apc", "rustedit_apc", "bradley", "rustedit_bradley" };
    public static readonly List<CustomApcSpawner> Spawners = new();
    private static SerializedApcPathList _data;

    public static void Initialize()
    {
        if (!RustEditConfig.Data.Spawnables.APC) return;
        RustEditHub.OnLoaded += Load;
        RustEditHub.Enqueue(CreateSpawners());
    }

    public static void Shutdown()
    {
        RustEditHub.OnLoaded -= Load;
        for (int i = 0; i < Spawners.Count; i++)
            if (Spawners[i] != null) Object.Destroy(Spawners[i].gameObject);
        Spawners.Clear();
        _data = null;
    }

    private static void Load()
    {
        _data = null;
        if (MapDataHelper.TryGetMapXml(MapKeys, out SerializedApcPathList data))
        {
            _data = data;
            Debug.Log($"[RustEditStandalone] APC paths: {_data?.paths?.Count ?? 0}");
        }
    }

    private static IEnumerator CreateSpawners()
    {
        yield return new WaitForSeconds(1f);
        if (_data?.paths == null) yield break;
        for (int i = 0; i < _data.paths.Count; i++)
        {
            var path = _data.paths[i];
            if (path?.nodes == null || path.nodes.Count == 0) continue;
            var go = new GameObject("RustEdit_APC_" + i);
            Object.DontDestroyOnLoad(go);
            var spawner = go.AddComponent<CustomApcSpawner>();
            spawner.Initialize(path);
            Spawners.Add(spawner);
        }
    }

    public static string Status()
    {
        if (Spawners.Count == 0) return "No custom APC spawners.";
        var lines = new List<string>();
        for (int i = 0; i < Spawners.Count; i++)
            lines.Add(Spawners[i]?.StatusLine() ?? "null");
        return string.Join("\n", lines);
    }

    public static int KillAll()
    {
        int n = 0;
        for (int i = 0; i < Spawners.Count; i++)
        {
            if (Spawners[i] != null && Spawners[i].IsAlive)
            {
                Spawners[i].KillApc();
                n++;
            }
        }
        return n;
    }

    public static int RespawnAll()
    {
        int n = 0;
        for (int i = 0; i < Spawners.Count; i++)
        {
            if (Spawners[i] == null) continue;
            Spawners[i].ForceRespawn();
            n++;
        }
        return n;
    }
}
