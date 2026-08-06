using System.Collections.Generic;
using RustEditStandalone.Core;
using UnityEngine;

namespace RustEditStandalone.Features;

public static class ShopKeeperFeature
{
    private static readonly List<NPCShopKeeper> Keepers = new();

    public static void Initialize()
    {
        RustEditHub.OnSpawned += OnSpawned;
        RustEditHub.OnServerInit += Link;
    }

    public static void Shutdown()
    {
        RustEditHub.OnSpawned -= OnSpawned;
        RustEditHub.OnServerInit -= Link;
        Keepers.Clear();
    }

    private static void OnSpawned(BaseEntity entity, string category)
    {
        if (entity is NPCShopKeeper keeper && !Keepers.Contains(keeper))
            Keepers.Add(keeper);
    }

    private static void Link()
    {
        int linked = 0;
        for (int i = Keepers.Count - 1; i >= 0; i--)
        {
            var keeper = Keepers[i];
            if (keeper == null || keeper.IsDestroyed)
            {
                Keepers.RemoveAt(i);
                continue;
            }

            InvisibleVendingMachine nearest = null;
            float best = 1f * 1f;
            var hits = Physics.OverlapSphere(keeper.transform.position, 1f);
            for (int h = 0; h < hits.Length; h++)
            {
                var vm = hits[h]?.GetComponentInParent<InvisibleVendingMachine>();
                if (vm == null) continue;
                float sq = (vm.transform.position - keeper.transform.position).sqrMagnitude;
                if (sq < best)
                {
                    best = sq;
                    nearest = vm;
                }
            }

            if (nearest == null) continue;
            try
            {
                keeper.machine = nearest;
                nearest.SetAttachedNPC(keeper);
                linked++;
            }
            catch
            {
                // API may vary by build
                try { keeper.machine = nearest; linked++; } catch { }
            }
        }
        Debug.Log($"[RustEditStandalone] ShopKeepers linked: {linked}");
    }
}
