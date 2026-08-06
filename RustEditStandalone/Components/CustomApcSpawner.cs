using System.Collections.Generic;
using RustEditStandalone.Core;
using RustEditStandalone.Data;
using UnityEngine;

namespace RustEditStandalone.Components;

public sealed class CustomApcSpawner : MonoBehaviour
{
    private const string BradleyPrefab = "assets/prefabs/npc/m2bradley/bradleyapc.prefab";

    private SerializedApcPath _path;
    private readonly List<Vector3> _nodes = new();
    private readonly List<Vector3> _interest = new();

    public BradleyAPC ActiveApc { get; private set; }
    public bool IsAlive => ActiveApc != null && !ActiveApc.IsDestroyed;

    public void Initialize(SerializedApcPath path)
    {
        _path = path;
        _nodes.Clear();
        _interest.Clear();

        if (path?.nodes != null)
        {
            for (int i = 0; i < path.nodes.Count; i++)
                if (path.nodes[i] != null) _nodes.Add(path.nodes[i].ToVector3());
        }
        if (path?.interestNodes != null)
        {
            for (int i = 0; i < path.interestNodes.Count; i++)
                if (path.interestNodes[i] != null) _interest.Add(path.interestNodes[i].ToVector3());
        }

        if (_nodes.Count == 0) return;
        transform.position = _nodes[0];
        Invoke(nameof(SpawnApc), 3f);
        InvokeRepeating(nameof(CheckRespawn), 5f, 5f);
    }

    public void KillApc()
    {
        if (ActiveApc != null && !ActiveApc.IsDestroyed)
            ActiveApc.Kill();
        ActiveApc = null;
    }

    public void ForceRespawn()
    {
        KillApc();
        SpawnApc();
    }

    public void SpawnApc()
    {
        if (_nodes.Count == 0) return;
        if (IsAlive) return;

        Vector3 spawnPos = _interest.Count > 0 ? _interest[0] : _nodes[0];
        var apc = MapDataHelper.InstantiatePrefab<BradleyAPC>(BradleyPrefab, spawnPos, Quaternion.identity);
        if (apc == null) return;

        apc.enableSaving = false;
        ActiveApc = apc;

        try
        {
            // Prefer custom path installation when available on this game build.
            apc.pathLooping = true;
            if (apc.currentPath != null)
            {
                apc.currentPath.Clear();
                for (int i = 0; i < _nodes.Count; i++)
                    apc.currentPath.Add(_nodes[i]);
            }
        }
        catch { /* path API may differ by build */ }

        RustEditApi.RaiseAPCSpawned(apc);
    }

    private void CheckRespawn()
    {
        if (IsAlive) return;
        ActiveApc = null;
        float delay = UnityEngine.Random.Range(5f, 15f) * 60f;
        CancelInvoke(nameof(SpawnApc));
        Invoke(nameof(SpawnApc), delay);
    }

    public string StatusLine()
    {
        string state = IsAlive ? "alive" : "dead";
        Vector3 p = IsAlive ? ActiveApc.transform.position : (_nodes.Count > 0 ? _nodes[0] : Vector3.zero);
        return $"APC {state} at ({p.x:F0},{p.y:F0},{p.z:F0}) nodes={_nodes.Count}";
    }
}
