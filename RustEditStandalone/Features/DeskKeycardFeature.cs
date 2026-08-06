using System;
using System.Collections.Generic;
using RustEditStandalone.Config;
using RustEditStandalone.Core;
using UnityEngine;

namespace RustEditStandalone.Features;

public static class DeskKeycardFeature
{
    private static readonly string[] DeskHints =
    {
        "cardreader", "keycard", "lockedcrate", "desk"
    };

    private static readonly List<Handler> Handlers = new();
    private static GameObject _runner;

    private sealed class Handler
    {
        public Transform Spawner;
        public string Prefab;
        public Vector3 Pos;
        public Quaternion Rot;
        public BaseEntity Card;
    }

    public static void Initialize()
    {
        RustEditHub.OnSpawned += OnSpawned;
        RustEditHub.OnKilled += OnKilled;
        EnsureRunner();
    }

    public static void Shutdown()
    {
        RustEditHub.OnSpawned -= OnSpawned;
        RustEditHub.OnKilled -= OnKilled;
        Handlers.Clear();
        if (_runner != null) { UnityEngine.Object.Destroy(_runner); _runner = null; }
    }

    private static void OnSpawned(BaseEntity entity, string category)
    {
        if (entity == null) return;
        string prefab = entity.PrefabName ?? string.Empty;
        bool deskLike = false;
        for (int i = 0; i < DeskHints.Length; i++)
        {
            if (prefab.IndexOf(DeskHints[i], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                deskLike = true;
                break;
            }
        }
        if (!deskLike) return;

        Transform child = FindChildContaining(entity.transform, "card_spawner");
        if (child == null) return;

        Handlers.Add(new Handler
        {
            Spawner = child,
            Prefab = "keycard_green", // populated via item spawn in Populate
            Pos = child.position,
            Rot = child.rotation
        });
    }

    private static void OnKilled(BaseNetworkable networkable)
    {
        if (networkable is not BaseEntity entity) return;
        for (int i = 0; i < Handlers.Count; i++)
        {
            var h = Handlers[i];
            if (h.Card != entity) continue;
            h.Card = null;
            float delay = RustEditConfig.Data.Respawn.Keycard.RandomSeconds;
            EnsureRunner();
            int idx = i;
            _runner.GetComponent<Runner>().Schedule(() => SpawnCard(Handlers[idx]), delay);
            break;
        }
    }

    public static int PopulateAll()
    {
        int n = 0;
        for (int i = 0; i < Handlers.Count; i++)
        {
            if (Handlers[i].Card != null && !Handlers[i].Card.IsDestroyed) continue;
            if (SpawnCard(Handlers[i])) n++;
        }
        return n;
    }

    private static bool SpawnCard(Handler handler)
    {
        if (handler?.Spawner == null) return false;
        // Spawn a world item keycard at the desk spawner point
        Item item = ItemManager.CreateByName("keycard_green", 1, 0uL);
        if (item == null) return false;
        var dropped = item.Drop(handler.Pos + Vector3.up * 0.2f, Vector3.zero, handler.Rot);
        handler.Card = dropped;
        return dropped != null;
    }

    private static Transform FindChildContaining(Transform root, string part)
    {
        if (root == null) return null;
        if (root.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindChildContaining(root.GetChild(i), part);
            if (found != null) return found;
        }
        return null;
    }

    private static void EnsureRunner()
    {
        if (_runner != null) return;
        _runner = new GameObject("RustEditStandalone_Desk");
        UnityEngine.Object.DontDestroyOnLoad(_runner);
        _runner.AddComponent<Runner>();
    }

    private sealed class Runner : MonoBehaviour
    {
        public void Schedule(Action a, float d) => StartCoroutine(Run(a, d));
        private System.Collections.IEnumerator Run(Action a, float d)
        {
            if (d > 0) yield return new WaitForSeconds(d);
            try { a?.Invoke(); } catch { }
        }
    }
}
