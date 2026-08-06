using System.Collections.Generic;
using RustEditStandalone.Core;
using RustEditStandalone.Data;
using UnityEngine;

namespace RustEditStandalone.Features;

public static class OceanFeature
{
    private static readonly string[] MapKeys = { "ocean", "oceanpath", "path", "rustedit_ocean", "rustedit_oceanpath" };
    private static List<Vector3> _path;

    public static void Initialize()
    {
        RustEditHub.OnLoaded += Load;
    }

    public static void Shutdown()
    {
        RustEditHub.OnLoaded -= Load;
        _path = null;
    }

    private static void Load()
    {
        _path = null;
        if (!MapDataHelper.TryGetMapXml(MapKeys, out SerializedPathList data) || data?.vectorData == null)
        {
            Debug.Log("[RustEditStandalone] No custom ocean path data.");
            return;
        }

        _path = new List<Vector3>(data.vectorData.Count);
        for (int i = 0; i < data.vectorData.Count; i++)
            if (data.vectorData[i] != null)
                _path.Add(data.vectorData[i].ToVector3());

        Debug.Log($"[RustEditStandalone] Ocean path loaded: {_path.Count} points.");
    }

    public static bool TryGetCustomPath(out List<Vector3> path)
    {
        // WorldSetup may call GenerateOceanPatrolPath before the first prefab track / OnLoaded.
        if (_path == null || _path.Count == 0)
            Load();
        path = _path;
        return _path != null && _path.Count > 0;
    }

    public static void Show(BasePlayer player, float seconds)
    {
        if (player == null || _path == null) return;
        float dur = seconds <= 0 ? 30f : seconds;
        for (int i = 0; i < _path.Count; i++)
        {
            player.SendConsoleCommand("ddraw.sphere", dur, Color.cyan, _path[i], 2f);
            if (i + 1 < _path.Count)
                player.SendConsoleCommand("ddraw.line", dur, Color.cyan, _path[i], _path[i + 1]);
        }
    }
}
