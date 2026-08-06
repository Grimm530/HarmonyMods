using System;
using System.Collections;
using System.Collections.Generic;
using RustEditStandalone.Components;
using RustEditStandalone.Core;
using RustEditStandalone.Data;
using UnityEngine;

namespace RustEditStandalone.Features;

public static class IoFeature
{
    private static readonly HashSet<string> StripComponents = new(StringComparer.Ordinal)
    {
        "GroundWatch", "DestroyOnGroundMissing"
    };

    private static SerializedIOData _ioData;
    private static readonly List<IOEntity> MapIoEntities = new();
    private static readonly HashSet<NetworkableId> MapIoIds = new();
    private static readonly HashSet<NetworkableId> UnlimitedTurrets = new();

    public static void Initialize()
    {
        RustEditHub.OnLoaded += Load;
        RustEditHub.OnSpawned += OnSpawned;
        RustEditHub.Enqueue(ProcessRoutine());
    }

    public static void Shutdown()
    {
        RustEditHub.OnLoaded -= Load;
        RustEditHub.OnSpawned -= OnSpawned;
        MapIoEntities.Clear();
        MapIoIds.Clear();
        UnlimitedTurrets.Clear();
        _ioData = null;
    }

    public static void CollectEntities(List<BaseEntity> list)
    {
        for (int i = 0; i < MapIoEntities.Count; i++)
            if (MapIoEntities[i] != null) list.Add(MapIoEntities[i]);
    }

    public static bool IsMapIo(BaseNetworkable entity)
    {
        return entity != null && MapIoIds.Contains(entity.net.ID);
    }

    public static bool IsUnlimitedTurret(BaseNetworkable entity)
    {
        return entity != null && UnlimitedTurrets.Contains(entity.net.ID);
    }

    public static void ResetConnections()
    {
        RustEditHub.Enqueue(ProcessRoutine());
        RustEditHub.NotifyServerReady();
    }

    private static void Load()
    {
        _ioData = null;
        SerializedIOData best = null;
        int bestValid = 0;

        foreach (string key in new[] { "io", "rustedit_io" })
        {
            byte[] bytes = MapDataHelper.GetMapBytes(key);
            if (bytes == null) continue;
            if (IODataDeserializer.TryDeserialize(bytes, out var data) && data?.entities != null)
            {
                int valid = CountValid(data);
                if (valid > bestValid) { bestValid = valid; best = data; }
            }
        }

        MapDataHelper.ForEachCustomLayer((_, data) =>
        {
            if (IODataDeserializer.TryDeserialize(data, out var io) && io?.entities != null)
            {
                int valid = CountValid(io);
                if (valid > bestValid) { bestValid = valid; best = io; }
            }
        });

        _ioData = bestValid > 0 ? best : null;
        if (_ioData?.entities != null)
            Debug.Log($"[RustEditStandalone] IO data loaded: {_ioData.entities.Count} entities ({bestValid} valid).");
        else
            Debug.Log("[RustEditStandalone] No IO data found in map.");
    }

    private static int CountValid(SerializedIOData data)
    {
        if (data?.entities == null) return 0;
        int n = 0;
        for (int i = 0; i < data.entities.Count; i++)
        {
            var e = data.entities[i];
            if (e == null) continue;
            if (!string.IsNullOrEmpty(e.fullPath)) { n++; continue; }
            var p = e.position.ToVector3();
            if (Math.Abs(p.x) > 10 || Math.Abs(p.y) > 10 || Math.Abs(p.z) > 10) n++;
        }
        return n;
    }

    private static void OnSpawned(BaseEntity entity, string category)
    {
        if (entity is not IOEntity io) return;
        Harden(io);
    }

    private static void Harden(IOEntity entity)
    {
        if (entity == null) return;
        entity.enableSaving = false;
        Strip(entity.gameObject);
        if (entity is DecayEntity decay)
            decay.CancelInvoke(nameof(DecayEntity.DecayTick));
    }

    private static void Strip(GameObject go)
    {
        var comps = go.GetComponents<Component>();
        for (int i = 0; i < comps.Length; i++)
        {
            var c = comps[i];
            if (c == null) continue;
            if (StripComponents.Contains(c.GetType().Name))
                UnityEngine.Object.Destroy(c);
        }
    }

    private static IEnumerator ProcessRoutine()
    {
        yield return new WaitForSeconds(3f);
        ProcessIOEntities();
    }

    public static void ProcessIOEntities()
    {
        if (_ioData?.entities == null || _ioData.entities.Count == 0)
        {
            Debug.Log("[RustEditStandalone] ProcessIOEntities: no IO data.");
            return;
        }

        var ioEntities = new List<IOEntity>();
        foreach (var ent in BaseNetworkable.serverEntities)
        {
            if (ent is IOEntity io)
                ioEntities.Add(io);
        }

        if (ioEntities.Count == 0)
        {
            Debug.Log("[RustEditStandalone] ProcessIOEntities: no IO entities in world.");
            return;
        }

        MapIoEntities.Clear();
        MapIoIds.Clear();
        UnlimitedTurrets.Clear();

        int matched = 0, inputConns = 0, outputConns = 0;

        for (int s = 0; s < _ioData.entities.Count; s++)
        {
            var ser = _ioData.entities[s];
            var pos = ser.position.ToVector3();
            bool hasPath = !string.IsNullOrEmpty(ser?.fullPath);
            bool hasPos = PositionSignificant(pos);
            if (!hasPath && !hasPos) continue;

            var entity = FindIOEntity(ioEntities, ser.fullPath ?? "", pos);
            if (entity == null) continue;

            matched++;
            Harden(entity);
            Track(entity);
            ApplySettings(entity, ser);

            if (ser.inputs != null && entity.inputs != null)
            {
                for (int i = 0; i < ser.inputs.Length && i < entity.inputs.Length; i++)
                {
                    var conn = ser.inputs[i];
                    if (conn == null) continue;
                    var source = FindIOEntity(ioEntities, conn.fullPath, conn.position.ToVector3());
                    if (source?.outputs == null) continue;
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

            if (ser.outputs != null && entity.outputs != null)
            {
                for (int o = 0; o < ser.outputs.Length && o < entity.outputs.Length; o++)
                {
                    var conn = ser.outputs[o];
                    if (conn == null) continue;
                    var target = FindIOEntity(ioEntities, conn.fullPath, conn.position.ToVector3());
                    if (target?.inputs == null) continue;
                    int targetInputSlot = conn.connectedTo;
                    if (targetInputSlot < 0 || targetInputSlot >= target.inputs.Length) continue;
                    entity.ConnectTo(target, o, targetInputSlot, new List<Vector3>(), new List<float>(), Array.Empty<IOEntity.LineAnchor>());
                    outputConns++;
                }
            }

            entity.Init();
            entity.MarkDirtyForceUpdateOutputs();
            entity.SendNetworkUpdate();
            entity.SendChangedToRoot(forceUpdate: true);
            entity.RefreshIndustrialPreventBuilding();
        }

        Debug.Log($"[RustEditStandalone] ProcessIOEntities done: {matched} entities ({inputConns} in / {outputConns} out).");
    }

    private static void Track(IOEntity entity)
    {
        if (entity?.net == null) return;
        MapIoIds.Add(entity.net.ID);
        if (!MapIoEntities.Contains(entity))
            MapIoEntities.Add(entity);
    }

    private static void ApplySettings(IOEntity entity, SerializedIOEntity ser)
    {
        if (entity is CardReader cardReader)
        {
            cardReader.accessLevel = ser.accessLevel + 1;
            var monitor = entity.gameObject.GetComponent<CardReaderMonitor>() ?? entity.gameObject.AddComponent<CardReaderMonitor>();
            monitor.Setup(ser.timerLength);
        }
        if (entity is TimerSwitch timerSwitch)
            timerSwitch.timerLength = ser.timerLength;
        if (entity is PressButton pressButton)
            pressButton.pressDuration = ser.timerLength;
        if (entity is RFReceiver rfReceiver)
            rfReceiver.frequency = ser.frequency;
        if (entity is RFBroadcaster rfBroadcaster)
            rfBroadcaster.frequency = ser.frequency;
        if (entity is ElectricalBranch branch)
            branch.branchAmount = ser.branchAmount;
        if (entity is PowerCounter counter)
            counter.counterNumber = ser.targetCounterNumber;
        if (entity is DoorManipulator doorManip)
            doorManip.powerAction = (DoorManipulator.DoorEffect)ser.doorEffect;
        if (entity is AutoTurret autoTurret)
        {
            var mgr = entity.gameObject.GetComponent<AutoTurretManager>() ?? entity.gameObject.AddComponent<AutoTurretManager>();
            mgr.Setup(ser.unlimitedAmmo, ser.peaceKeeper, ser.autoTurretWeapon);
            if (ser.unlimitedAmmo && entity.net != null)
                UnlimitedTurrets.Add(entity.net.ID);
        }
        if (entity is Elevator elevator && ser.floors > 0)
        {
            // floors applied when API available on build
        }
        if (entity.name.IndexOf("wheelswitch", StringComparison.OrdinalIgnoreCase) >= 0 ||
            entity.PrefabName.IndexOf("wheelswitch", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (entity.gameObject.GetComponent<IoToWheelSwitch>() == null)
                entity.gameObject.AddComponent<IoToWheelSwitch>();
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

        if (matchPath && matchPos)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var x = list[i];
                if (PathMatch(x.PrefabName, fullPath) && x.transform.position == position) return x;
            }
            for (int i = 0; i < list.Count; i++)
            {
                var x = list[i];
                if (PathMatch(x.PrefabName, fullPath) && PositionMatch(x.transform.position, position, tolerance)) return x;
            }
        }

        if (matchPath)
        {
            IOEntity closest = null;
            float bestSq = float.MaxValue;
            int candidates = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (!PathMatch(c.PrefabName, fullPath)) continue;
                candidates++;
                float sq = (c.transform.position - position).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; closest = c; }
            }
            if (candidates == 1 || closest != null) return closest;
        }

        if (matchPos)
        {
            IOEntity closest = null;
            float bestSq = tolerance * tolerance * 3;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
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
        string keyA = MapDataHelper.GetPrefabKey(prefabName);
        string keyB = MapDataHelper.GetPrefabKey(fullPath);
        return !string.IsNullOrEmpty(keyA) && keyA.Equals(keyB, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PositionMatch(Vector3 a, Vector3 b, float tolerance)
    {
        return Math.Abs(a.x - b.x) < tolerance && Math.Abs(a.y - b.y) < tolerance && Math.Abs(a.z - b.z) < tolerance;
    }
}
