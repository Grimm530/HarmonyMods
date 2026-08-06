using System;
using System.Collections.Generic;
using System.Reflection;
using RustEditStandalone.Core;
using RustEditStandalone.Data;
using UnityEngine;

namespace RustEditStandalone.Features;

public static class VendingFeature
{
    private static readonly string[] MapKeys = { "vending", "rustedit_vending", "rustedit_vending_containers" };
    private static SerializedVendingContainerData _data;
    private static readonly List<NPCVendingMachine> Machines = new();
    private static FieldInfo _refillTimesField;

    public static void Initialize()
    {
        _refillTimesField = typeof(NPCVendingMachine).GetField("refillTimes",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        RustEditHub.OnLoaded += Load;
        RustEditHub.OnSpawned += OnSpawned;
    }

    public static void Shutdown()
    {
        RustEditHub.OnLoaded -= Load;
        RustEditHub.OnSpawned -= OnSpawned;
        Machines.Clear();
        _data = null;
    }

    public static void CollectEntities(List<BaseEntity> list)
    {
        for (int i = 0; i < Machines.Count; i++)
            if (Machines[i] != null) list.Add(Machines[i]);
    }

    private static void Load()
    {
        _data = null;
        if (MapDataHelper.TryGetMapXml(MapKeys, out SerializedVendingContainerData data))
        {
            _data = data;
            int count = _data?.Entities?.Count ?? 0;
            Debug.Log($"[RustEditStandalone] Vending profiles loaded: {count}");
        }
        else
            Debug.Log("[RustEditStandalone] No vending map data found.");
    }

    private static void OnSpawned(BaseEntity entity, string category)
    {
        if (entity is not NPCVendingMachine vm) return;
        if (_data?.Entities == null || _data.Entities.Count == 0) return;

        string filename = MapDataHelper.GetFilenameFromCategory(category);
        if (string.IsNullOrEmpty(filename))
            filename = MapDataHelper.GetFilenameFromCategory(vm.PrefabName);

        VendingContainerData profile = null;
        for (int i = 0; i < _data.Entities.Count; i++)
        {
            var e = _data.Entities[i];
            if (e?.Filename != null && e.Filename.Equals(filename, StringComparison.OrdinalIgnoreCase))
            {
                profile = e;
                break;
            }
        }

        if (profile == null)
        {
            for (int i = 0; i < _data.Entities.Count; i++)
            {
                var e = _data.Entities[i];
                if (e?.Items != null && e.Items.Count > 0) { profile = e; break; }
            }
        }

        if (profile?.Items == null || profile.Items.Count == 0) return;
        Populate(vm, profile);
        if (!Machines.Contains(vm)) Machines.Add(vm);
    }

    public static void Populate(NPCVendingMachine vm, VendingContainerData containerData)
    {
        if (vm == null || containerData?.Items == null) return;
        vm.enableSaving = false;

        var items = new List<VendingItemData>(containerData.Items);
        var orderList = new List<NPCVendingOrder.Entry>();
        int count = Mathf.Min(items.Count, 7);

        for (int i = 0; i < count; i++)
        {
            int idx = UnityEngine.Random.Range(0, items.Count);
            var itemData = items[idx];
            items.RemoveAt(idx);

            var sellDef = ItemManager.FindItemDefinition(itemData.SellItemShortname);
            var currencyDef = ItemManager.FindItemDefinition(itemData.CurrencyItemShortname);
            if (sellDef == null || currencyDef == null) continue;

            orderList.Add(new NPCVendingOrder.Entry
            {
                sellItem = sellDef,
                sellItemAmount = itemData.SellItemAmount,
                sellItemAsBP = itemData.SellItemBlueprint,
                currencyItem = currencyDef,
                currencyAmount = itemData.CurrencyItemAmount,
                currencyAsBP = itemData.CurrencyItemBlueprint,
                refillDelay = 10f,
                refillAmount = 1
            });
        }

        if (orderList.Count == 0) return;

        vm.vendingOrders = ScriptableObject.CreateInstance<NPCVendingOrder>();
        vm.vendingOrders.orders = orderList.ToArray();

        if (_refillTimesField != null)
        {
            var refillTimes = new float[orderList.Count];
            for (int i = 0; i < refillTimes.Length; i++)
                refillTimes[i] = Time.realtimeSinceStartup + 10f;
            _refillTimesField.SetValue(vm, refillTimes);
        }

        vm.InstallFromVendingOrders();
        if (BaseEntity.saveList.Contains(vm))
            BaseEntity.saveList.Remove(vm);
    }

    public static int RestockAll()
    {
        int n = 0;
        for (int i = Machines.Count - 1; i >= 0; i--)
        {
            var vm = Machines[i];
            if (vm == null || vm.IsDestroyed) { Machines.RemoveAt(i); continue; }
            if (RestockOne(vm)) n++;
        }
        return n;
    }

    public static bool RestockOne(NPCVendingMachine vm)
    {
        if (vm == null || _data?.Entities == null) return false;
        string filename = MapDataHelper.GetFilenameFromCategory(vm.PrefabName);
        for (int i = 0; i < _data.Entities.Count; i++)
        {
            var e = _data.Entities[i];
            if (e?.Filename != null && e.Filename.Equals(filename, StringComparison.OrdinalIgnoreCase) && e.Items != null)
            {
                Populate(vm, e);
                return true;
            }
        }
        if (_data.Entities.Count > 0 && _data.Entities[0]?.Items != null)
        {
            Populate(vm, _data.Entities[0]);
            return true;
        }
        return false;
    }
}
