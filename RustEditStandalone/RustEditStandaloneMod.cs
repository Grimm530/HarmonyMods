using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using HarmonyLib;
using UnityEngine;

namespace RustEditStandalone;

/// <summary>
/// Harmony mod that replicates Oxide.Ext.RustEdit functionality without Oxide.
/// Populates vending machines and restores IO (electrical) connections on custom maps.
/// </summary>
public class RustEditStandaloneMod : IHarmonyModHooks
{
    public static RustEditStandaloneMod Instance { get; private set; }

    private SerializedVendingContainerData _vendingData;
    private readonly List<VendingEntry> _vendingEntries = new();
    private SerializedIOData _ioData;
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
        _refillTimesField = typeof(NPCVendingMachine).GetField("refillTimes",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
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
        _ioData = null;
        if (World.Serialization?.world?.maps == null) return;

        SerializedIOData best = null;
        int bestValidCount = 0;
        string bestLayerName = null;
        var standardSkip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "height", "splat", "biome", "topology", "alpha", "water", "terrain" };

        foreach (string key in new[] { "rustedit_io", "io" })
        {
            var ioBytes = World.GetMap(key);
            if (ioBytes == null || ioBytes.Length < 10) continue;
            if (TryDeserializeIOData(ioBytes, out var data) && data?.entities != null)
            {
                int valid = CountValidIOEntities(data);
                if (valid > bestValidCount)
                {
                    bestValidCount = valid;
                    best = data;
                    bestLayerName = key;
                }
            }
        }

        foreach (var map in World.Serialization.world.maps)
        {
            if (map?.data == null || map.data.Length < 10) continue;
            if (standardSkip.Contains(map.name ?? "")) continue;
            if (TryDeserializeIOData(map.data, out var data) && data?.entities != null)
            {
                int valid = CountValidIOEntities(data);
                if (valid > bestValidCount)
                {
                    bestValidCount = valid;
                    best = data;
                    bestLayerName = map.name ?? "(unnamed)";
                }
            }
        }

        _ioData = best;

        if (_ioData?.entities != null && _ioData.entities.Count > 0)
        {
            int valid = CountValidIOEntities(_ioData);
            Debug.Log($"[RustEditStandalone] IO data loaded: {_ioData.entities.Count} entities ({valid} with path/position) from layer \"{bestLayerName}\".");
            if (valid == 0)
            {
                Debug.Log("[RustEditStandalone] No valid IO entities (all empty path and 0,0,0) - treating as no IO data. Map may use a different IO format or key.");
                _ioData = null;
            }
        }
        else
        {
            var layerNames = new List<string>();
            if (World.Serialization?.world?.maps != null)
            {
                foreach (var m in World.Serialization.world.maps)
                    if (m?.data != null && m.data.Length >= 10)
                        layerNames.Add(m.name ?? "(unnamed)");
            }
            Debug.Log($"[RustEditStandalone] No IO data found in map. Map layers with data: [{string.Join(", ", layerNames)}]");
        }
    }

    private static int CountValidIOEntities(SerializedIOData data)
    {
        if (data?.entities == null) return 0;
        int n = 0;
        foreach (var e in data.entities)
        {
            if (e == null) continue;
            if (!string.IsNullOrEmpty(e.fullPath)) { n++; continue; }
            var p = e.position.ToVector3();
            if (Math.Abs(p.x) > 10 || Math.Abs(p.y) > 10 || Math.Abs(p.z) > 10) n++;
        }
        return n;
    }

    private static bool TryDeserializeIOData(byte[] data, out SerializedIOData result)
    {
        result = null;
        if (data == null || data.Length < 4) return false;
        try
        {
            return IODataDeserializer.TryDeserialize(data, out result);
        }
        catch
        {
            return false;
        }
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

        if (_ioData != null && !_ioProcessScheduled)
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
    /// Restore RustEdit IO (electrical) connections from map data. Call after world prefabs have spawned.
    /// Wiring is done exactly as saved in the map (like in-game: explicit output-to-input links). We do not invent connections.
    /// Map-placed entities are server-owned (no building privilege); the map's IO layer stores which entity connects to which.
    /// Test generators (server-owned, no owner) are the power sources; we only connect consumers to them when the map says so.
    /// </summary>
    public void ProcessIOEntities()
    {
        if (_ioData?.entities == null || _ioData.entities.Count == 0)
        {
            Debug.Log("[RustEditStandalone] ProcessIOEntities: no IO data in map. Wiring must come from the map's IO layer (RustEdit saves it when you wire in the editor). Skipping.");
            return;
        }

        var ioEntities = BaseNetworkable.serverEntities
            .Where(x => x is IOEntity)
            .Cast<IOEntity>()
            .ToList();

        if (ioEntities.Count == 0)
        {
            Debug.Log("[RustEditStandalone] ProcessIOEntities: no IO entities in world, skipping.");
            return;
        }

        int matched = 0;
        int inputConns = 0;
        int outputConns = 0;

        foreach (var ser in _ioData.entities)
        {
            var pos = ser.position.ToVector3();
            bool hasPath = !string.IsNullOrEmpty(ser?.fullPath);
            bool hasPos = PositionSignificant(pos);
            if (!hasPath && !hasPos) continue;

            var entity = FindIOEntity(ioEntities, ser.fullPath ?? "", pos);
            if (entity == null) continue;

            matched++;
            entity.enableSaving = false;

            ApplyIOEntitySettings(entity, ser);

            // Wire inputs: set this entity's input to source, AND source's output to this entity (both sides)
            if (ser.inputs != null && entity.inputs != null)
            {
                for (int i = 0; i < ser.inputs.Length && i < entity.inputs.Length; i++)
                {
                    var conn = ser.inputs[i];
                    if (conn == null) continue;

                    var source = FindIOEntity(ioEntities, conn.fullPath, conn.position.ToVector3());
                    if (source == null || source.outputs == null) continue;

                    int sourceOutputSlot = conn.connectedTo;
                    if (sourceOutputSlot < 0 || sourceOutputSlot >= source.outputs.Length) continue;

                    var inSlot = entity.inputs[i];
                    if (inSlot.connectedTo == null) inSlot.connectedTo = new IOEntity.IORef();
                    inSlot.connectedTo.ioEnt = source;
                    inSlot.connectedToSlot = sourceOutputSlot;
                    inSlot.connectedTo.Init();

                    var outSlot = source.outputs[sourceOutputSlot];
                    if (outSlot.connectedTo == null) outSlot.connectedTo = new IOEntity.IORef();
                    outSlot.connectedTo.Set(entity);
                    outSlot.connectedToSlot = i;
                    outSlot.connectedTo.Init();

                    inputConns++;
                }
            }

            // Wire outputs: ConnectTo sets both sides
            if (ser.outputs != null && entity.outputs != null)
            {
                for (int o = 0; o < ser.outputs.Length && o < entity.outputs.Length; o++)
                {
                    var conn = ser.outputs[o];
                    if (conn == null) continue;

                    var target = FindIOEntity(ioEntities, conn.fullPath, conn.position.ToVector3());
                    if (target == null || target.inputs == null) continue;

                    int targetInputSlot = conn.connectedTo;
                    if (targetInputSlot < 0 || targetInputSlot >= target.inputs.Length) continue;

                    entity.ConnectTo(target, o, targetInputSlot, new List<Vector3>(), new List<float>(), new IOEntity.LineAnchor[0]);
                    outputConns++;
                }
            }

            entity.Init();
            entity.MarkDirtyForceUpdateOutputs();
            entity.SendNetworkUpdate();
            entity.SendChangedToRoot(forceUpdate: true);
            entity.RefreshIndustrialPreventBuilding();
        }

        if (matched == 0 && _ioData.entities.Count > 0 && ioEntities.Count > 0)
            LogIOMatchDebug(_ioData.entities, ioEntities);

        Debug.Log($"[RustEditStandalone] ProcessIOEntities done: {matched} entities wired ({inputConns} inputs, {outputConns} outputs).");
    }

    private static void LogIOMatchDebug(List<SerializedIOEntity> serialized, List<IOEntity> world)
    {
        var ser = serialized[0];
        var pos = ser.position.ToVector3();
        Debug.Log($"[RustEditStandalone] DEBUG (0 matched): serialized[0] fullPath=\"{ser.fullPath}\" key=\"{GetPrefabKey(ser.fullPath)}\" pos=({pos.x:F1},{pos.y:F1},{pos.z:F1})");
        int n = Math.Min(5, world.Count);
        for (int i = 0; i < n; i++)
        {
            var e = world[i];
            var p = e.transform.position;
            bool pathMatch = PathMatch(e.PrefabName, ser.fullPath);
            Debug.Log($"[RustEditStandalone] DEBUG   world[{i}] PrefabName=\"{e.PrefabName}\" key=\"{GetPrefabKey(e.PrefabName)}\" pos=({p.x:F1},{p.y:F1},{p.z:F1}) PathMatch={pathMatch}");
        }
    }

    private static bool PositionSignificant(Vector3 p)
    {
        const float min = 10f;
        return Math.Abs(p.x) > min || Math.Abs(p.y) > min || Math.Abs(p.z) > min;
    }

    private static IOEntity FindIOEntity(List<IOEntity> list, string fullPath, Vector3 position)
    {
        const float tolerance = 1f;
        bool matchPath = !string.IsNullOrEmpty(fullPath);
        bool matchPos = PositionSignificant(position);

        // Prefer exact position match (like Oxide) to avoid wrong entity when two same prefab are close
        if (matchPath && matchPos)
        {
            var e = list.Find(x => PathMatch(x.PrefabName, fullPath) && x.transform.position == position);
            if (e != null) return e;
            e = list.Find(x => PathMatch(x.PrefabName, fullPath) && PositionMatch(x.transform.position, position, tolerance));
            if (e != null) return e;
        }
        if (matchPath)
        {
            var candidates = list.Where(x => PathMatch(x.PrefabName, fullPath)).ToList();
            if (candidates.Count == 1) return candidates[0];
            if (candidates.Count > 1)
            {
                IOEntity closest = null;
                float bestSq = float.MaxValue;
                foreach (var c in candidates)
                {
                    float sq = (c.transform.position - position).sqrMagnitude;
                    if (sq < bestSq) { bestSq = sq; closest = c; }
                }
                return closest;
            }
        }
        if (matchPos)
        {
            IOEntity closest = null;
            float bestSq = tolerance * tolerance * 3;
            foreach (var c in list)
            {
                float sq = (c.transform.position - position).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; closest = c; }
            }
            return closest;
        }
        return null;
    }

    private static bool PathMatch(string prefabName, string fullPath)
    {
        if (string.IsNullOrEmpty(prefabName) || string.IsNullOrEmpty(fullPath)) return false;
        if (prefabName.Equals(fullPath, StringComparison.OrdinalIgnoreCase)) return true;
        string keyA = GetPrefabKey(prefabName);
        string keyB = GetPrefabKey(fullPath);
        if (string.IsNullOrEmpty(keyA) || string.IsNullOrEmpty(keyB)) return false;
        return keyA.Equals(keyB, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalize prefab path to a comparable key: last path segment, no extension (e.g. "timerswitch").
    /// So "assets/.../timerswitch.deployed" and "timerswitch" both match.
    /// </summary>
    private static string GetPrefabKey(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        int lastSlash = path.LastIndexOf('/');
        string segment = lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
        int dot = segment.LastIndexOf('.');
        string name = dot > 0 ? segment.Substring(0, dot) : segment;
        return name.Trim();
    }

    private static bool PositionMatch(Vector3 a, Vector3 b, float tolerance = 0.5f)
    {
        return Math.Abs(a.x - b.x) < tolerance && Math.Abs(a.y - b.y) < tolerance && Math.Abs(a.z - b.z) < tolerance;
    }

    private static void ApplyIOEntitySettings(IOEntity entity, SerializedIOEntity ser)
    {
        if (entity is CardReader cardReader)
        {
            cardReader.accessLevel = ser.accessLevel;
        }
        if (entity is TimerSwitch timerSwitch)
        {
            timerSwitch.timerLength = ser.timerLength;
        }
        if (entity is PressButton pressButton)
        {
            pressButton.pressDuration = ser.timerLength;
        }
        if (entity is RFReceiver rfReceiver)
        {
            rfReceiver.frequency = ser.frequency;
        }
        if (entity is RFBroadcaster rfBroadcaster)
        {
            rfBroadcaster.frequency = ser.frequency;
        }
        if (entity is ElectricalBranch branch)
        {
            branch.branchAmount = ser.branchAmount;
        }
        if (entity is PowerCounter counter)
        {
            counter.counterNumber = ser.targetCounterNumber;
        }
        if (entity is DoorManipulator doorManip)
        {
            doorManip.powerAction = (DoorManipulator.DoorEffect)ser.doorEffect;
        }
        if (entity is AutoTurret autoTurret)
        {
            autoTurret.SetPeacekeepermode(ser.peaceKeeper);
            if (ser.unlimitedAmmo)
            {
                autoTurret.inventory?.MarkDirty();
            }
        }
    }
}
