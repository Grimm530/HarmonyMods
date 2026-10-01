using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;
using HarmonyLib;
using RustEditStandalone.Features;
using UnityEngine;

namespace RustEditStandalone;

/// <summary>
/// Harmony mod that replicates Oxide.Ext.RustEdit functionality without legacy plugin host.
/// Populates vending machines and restores IO (electrical) connections on custom maps.
/// </summary>
public class RustEditStandaloneMod : IHarmonyModHooks
{
    public static RustEditStandaloneMod Instance { get; private set; }

    private SerializedVendingContainerData _vendingData;
    private readonly List<VendingEntry> _vendingEntries = new();
    private bool _hasIo;
    private FieldInfo _refillTimesField;
    private bool _initialized;
    private bool _ioProcessScheduled;

    private struct VendingEntry
    {
        public string PrefabName;
        public VendingContainerData ContainerData;
    }

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;
        GrimmCoreHurtRegistration.Register();
        _refillTimesField = typeof(NPCVendingMachine).GetField("refillTimes",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        GrimmCoreHurtRegistration.Unregister();
        Instance = null;
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        LoadVendingData();
        LoadIOData();
        _initialized = true;
    }

    private void LoadIOData()
    {
        _hasIo = IoFeature.Load();
    }

    private void LoadVendingData()
    {
        _vendingData = null;
        _vendingEntries.Clear();

        if (World.Serialization?.world?.maps == null) return;

        byte[] vendingBytes = null;

        // Try known RustEdit map keys
        foreach (string key in new[] { "rustedit_vending", "rustedit_vending_containers" })
        {
            vendingBytes = World.GetMap(key);
            if (vendingBytes != null && vendingBytes.Length > 0) break;
        }

        // Fallback: try all maps - RustEdit may use obfuscated keys
        if (vendingBytes == null)
        {
            foreach (var map in World.Serialization.world.maps)
            {
                if (map?.data == null || map.data.Length < 20) continue;
                // Skip standard Rust map layers
                if (map.name is "height" or "splat" or "biome" or "topology" or "alpha" or "water" or "terrain")
                    continue;

                if (TryDeserializeVendingData(map.data, out var data))
                {
                    _vendingData = data;
                    break;
                }
            }
        }
        else if (TryDeserializeVendingData(vendingBytes, out var data))
        {
            _vendingData = data;
        }

        if (_vendingData?.Entities == null) return;

        // Build lookup - match by filename from prefab path
        foreach (var entity in _vendingData.Entities)
        {
            if (string.IsNullOrEmpty(entity.Filename)) continue;
            _vendingEntries.Add(new VendingEntry { PrefabName = entity.Filename, ContainerData = entity });
        }
    }

    private static bool TryDeserializeVendingData(byte[] data, out SerializedVendingContainerData result)
    {
        result = null;
        if (data == null || data.Length < 10) return false;

        try
        {
            using var ms = new MemoryStream(data);
            var serializer = new XmlSerializer(typeof(SerializedVendingContainerData));
            result = (SerializedVendingContainerData)serializer.Deserialize(ms);
            return result?.Entities != null;
        }
        catch
        {
            return false;
        }
    }

    private static string GetFilenameFromPrefab(string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName)) return string.Empty;
        // RustEdit extracts from path - try segment index 3 (e.g. "vendingmachine" from path)
        var parts = prefabName.Split('_', ':');
        if (parts.Length > 3) return parts[3];
        // Fallback: last path component without extension
        var lastSlash = prefabName.LastIndexOf('/');
        var name = lastSlash >= 0 ? prefabName.Substring(lastSlash + 1) : prefabName;
        var dot = name.IndexOf('.');
        return dot > 0 ? name.Substring(0, dot) : name;
    }

    internal void OnPrefabSpawned(GameObject go, string category)
    {
        if (go == null) return;

        EnsureInitialized();

        if (_hasIo && !_ioProcessScheduled)
        {
            _ioProcessScheduled = true;
            RustEditIOProcessor.ScheduleProcessIO();
        }

        var vendingMachine = go.GetComponent<NPCVendingMachine>();
        if (vendingMachine == null) return;

        if (_vendingData?.Entities == null) return;

        string filename = GetFilenameFromPrefab(vendingMachine.PrefabName);
        var containerData = _vendingData.Entities.Find(x =>
            x.Filename.Equals(filename, StringComparison.OrdinalIgnoreCase));

        if (containerData == null)
        {
            // Fallback: use first profile with items (RustEdit may use different filename format)
            foreach (var e in _vendingData.Entities)
            {
                if (e.Items != null && e.Items.Count > 0)
                {
                    containerData = e;
                    break;
                }
            }
        }

        if (containerData?.Items == null || containerData.Items.Count == 0) return;

        PopulateVendingMachine(vendingMachine, containerData);
    }

    private void PopulateVendingMachine(NPCVendingMachine vm, VendingContainerData containerData)
    {
        if (vm == null || containerData?.Items == null) return;

        vm.enableSaving = false;

        var items = new List<VendingItemData>(containerData.Items);
        var orderList = new List<NPCVendingOrder.Entry>();
        int count = Mathf.Min(items.Count, 7);

        for (int i = 0; i < count; i++)
        {
            var itemData = items[UnityEngine.Random.Range(0, items.Count)];
            items.Remove(itemData);

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

        // Set refill times via reflection (private field)
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

    /// <summary>
    /// Restore IO connections after world prefabs have spawned.
    /// RustEdit XML/protobuf layers and the customgenerator JSON layer are applied by <see cref="IoFeature"/>.
    /// </summary>
    public void ProcessIOEntities()
    {
        IoFeature.ProcessIOEntities();
    }
}
