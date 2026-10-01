using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RustEditStandalone.Components;
using RustEditStandalone.Core;
using RustEditStandalone.Data;
using RustIoConnection = RustEditStandalone.Data.SerializedConnectionData;
using RustIoData = RustEditStandalone.Data.SerializedIOData;
using RustIoEntity = RustEditStandalone.Data.SerializedIOEntity;
using UnityEngine;

namespace RustEditStandalone.Features;

public static class IoFeature
{
    private const float MatchDistanceSqr = 1f;
    private const int MissingLogCap = 5;
    private const int FailLogCap = 10;

    private static readonly HashSet<string> StripComponents = new(StringComparer.Ordinal)
    {
        "GroundWatch", "DestroyOnGroundMissing"
    };

    private static readonly string[] RustEditKeys = { "io", "rustedit_io" };

    private static readonly FieldInfo CounterTarget = typeof(PowerCounter).GetField(
        "targetCounterNumber",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly List<IoRecord> Records = new();
    private static readonly List<IOEntity> MapIoEntities = new();
    private static readonly HashSet<NetworkableId> MapIoIds = new();
    private static readonly HashSet<NetworkableId> UnlimitedTurrets = new();
    private static string _lastLoadMessage;

    public static void Initialize()
    {
        RustEditHub.OnLoaded += OnHubLoaded;
        RustEditHub.OnSpawned += OnSpawned;
        RustEditHub.Enqueue(ProcessRoutine());
    }

    public static void Shutdown()
    {
        RustEditHub.OnLoaded -= OnHubLoaded;
        RustEditHub.OnSpawned -= OnSpawned;
        MapIoEntities.Clear();
        MapIoIds.Clear();
        UnlimitedTurrets.Clear();
        Records.Clear();
        _lastLoadMessage = null;
    }

    public static void CollectEntities(List<BaseEntity> list)
    {
        for (int i = 0; i < MapIoEntities.Count; i++)
            if (MapIoEntities[i] != null) list.Add(MapIoEntities[i]);
    }

    public static bool IsMapIo(BaseNetworkable entity)
    {
        return entity != null && entity.net != null && MapIoIds.Contains(entity.net.ID);
    }

    public static bool IsUnlimitedTurret(BaseNetworkable entity)
    {
        return entity != null && entity.net != null && UnlimitedTurrets.Contains(entity.net.ID);
    }

    public static void ResetConnections()
    {
        _lastLoadMessage = null;
        ProcessIOEntities();
    }

    /// <summary>
    /// Read RustEdit XML/protobuf IO and the customgenerator JSON layer. Returns true when any entity was found.
    /// </summary>
    public static bool Load()
    {
        Records.Clear();
        string message = ReadSources(Records);
        if (message != _lastLoadMessage)
        {
            _lastLoadMessage = message;
            if (!string.IsNullOrEmpty(message))
                Debug.Log(message);
        }
        return Records.Count > 0;
    }

    private static void OnHubLoaded()
    {
        Load();
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
        Load();
        if (Records.Count == 0)
        {
            Debug.Log("[RustEditStandalone] ProcessIOEntities: no IO data. Wiring must come from a RustEdit IO layer or a customgenerator layer.");
            return;
        }

        var index = BuildIndex(Records);
        MapIoEntities.Clear();
        MapIoIds.Clear();
        UnlimitedTurrets.Clear();

        var matched = new List<(IoRecord Record, IOEntity Entity)>();
        int missing = 0;
        int missingLogged = 0;

        for (int s = 0; s < Records.Count; s++)
        {
            IoRecord record = Records[s];
            if (record == null || string.IsNullOrEmpty(record.Prefab)) continue;

            var entity = Find(index, record.Prefab, record.Position) as IOEntity;
            if (entity == null)
            {
                missing++;
                if (missingLogged++ < MissingLogCap)
                    Debug.LogWarning("[RustEditStandalone] IO: no " + Short(record.Prefab) + " at " + Format(record.Position));
                continue;
            }

            try
            {
                Harden(entity);
                Track(entity);
                ApplySettings(entity, record);
                matched.Add((record, entity));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RustEditStandalone] IO: settings failed on " + Short(record.Prefab) + ": " + ex.Message);
            }
        }

        int made = 0;
        int already = 0;
        int failed = 0;
        int failLogged = 0;
        var touched = new HashSet<IOEntity>();

        for (int m = 0; m < matched.Count; m++)
        {
            IoRecord record = matched[m].Record;
            IOEntity entity = matched[m].Entity;
            try
            {
                WireSide(entity, record, record.Outputs, isOutput: true, index, ref made, ref already, ref failed, ref failLogged, touched);
                WireSide(entity, record, record.Inputs, isOutput: false, index, ref made, ref already, ref failed, ref failLogged, touched);
            }
            catch (Exception ex)
            {
                failed++;
                Debug.LogWarning("[RustEditStandalone] IO: connect failed on " + Short(record.Prefab) + ": " + ex.Message);
            }
        }

        foreach (IOEntity entity in touched)
        {
            if (entity == null || entity.IsDestroyed) continue;
            try
            {
                EnsureSlotRefs(entity);
                entity.MarkDirtyForceUpdateOutputs();
                entity.SendNetworkUpdate();
                entity.SendChangedToRoot(forceUpdate: true);
                entity.RefreshIndustrialPreventBuilding();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RustEditStandalone] IO: update failed on " + Short(entity.PrefabName) + ": " + ex.Message);
            }
        }

        string summary = "[RustEditStandalone] IO: " + matched.Count + "/" + Records.Count + " entities found, " + made + " connections made";
        if (already > 0) summary += ", " + already + " already connected";
        if (failed > 0) summary += ", " + failed + " failed";
        if (missing > 0) summary += ", " + missing + " not found";
        Debug.Log(summary);

        SchedulePowerReport(matched);
    }

    private static void WireSide(
        IOEntity entity,
        IoRecord record,
        IoLink[] links,
        bool isOutput,
        Dictionary<string, List<BaseEntity>> index,
        ref int made,
        ref int already,
        ref int failed,
        ref int failLogged,
        HashSet<IOEntity> touched)
    {
        if (links == null) return;
        for (int i = 0; i < links.Length; i++)
        {
            IoLink link = links[i];
            if (link == null || string.IsNullOrEmpty(link.Prefab)) continue;

            var other = Find(index, link.Prefab, link.Position) as IOEntity;
            int outputSlot = isOutput ? i : link.Slot;
            int inputSlot = isOutput ? link.Slot : i;
            IOEntity source = isOutput ? entity : other;
            IOEntity target = isOutput ? other : entity;

            string reason = null;
            if (other == null)
                reason = Short(link.Prefab) + " not found at " + Format(link.Position);
            else if (source.outputs == null || outputSlot < 0 || outputSlot >= source.outputs.Length)
                reason = Short(isOutput ? record.Prefab : link.Prefab) + " has " + (source.outputs == null ? 0 : source.outputs.Length) + " outputs now, the map uses output " + outputSlot;
            else if (target.inputs == null || inputSlot < 0 || inputSlot >= target.inputs.Length)
                reason = Short(isOutput ? link.Prefab : record.Prefab) + " has " + (target.inputs == null ? 0 : target.inputs.Length) + " inputs now, the map uses input " + inputSlot;

            if (reason != null)
            {
                failed++;
                if (failLogged++ < FailLogCap)
                {
                    string from = isOutput ? Short(record.Prefab) : Short(link.Prefab);
                    string to = isOutput ? Short(link.Prefab) : Short(record.Prefab);
                    Debug.LogWarning("[RustEditStandalone] IO: can't connect " + from + " output " + outputSlot + " to " + to + " input " + inputSlot + ": " + reason);
                }
                continue;
            }

            IOEntity.IOSlot outSlot = source.outputs[outputSlot];
            IOEntity.IOSlot inSlot = target.inputs[inputSlot];
            if (outSlot == null || inSlot == null)
            {
                failed++;
                continue;
            }

            EnsureRef(outSlot);
            EnsureRef(inSlot);
            IOEntity existing = outSlot.connectedTo.Get();
            if (existing == target && outSlot.connectedToSlot == inputSlot)
            {
                already++;
                continue;
            }
            // Output records are authoritative. An input record must not replace a different wire.
            if (!isOutput && existing != null && existing != target)
            {
                already++;
                continue;
            }

            outSlot.connectedTo.Set(target);
            outSlot.connectedToSlot = inputSlot;
            outSlot.connectedTo.Init();
            inSlot.connectedTo.Set(source);
            inSlot.connectedToSlot = outputSlot;
            inSlot.connectedTo.Init();
            touched.Add(source);
            touched.Add(target);
            made++;
        }
    }

    private static void SchedulePowerReport(List<(IoRecord Record, IOEntity Entity)> matched)
    {
        var entities = new List<IOEntity>(matched.Count);
        for (int i = 0; i < matched.Count; i++)
            entities.Add(matched[i].Entity);

        var go = new GameObject("RustEditStandalone_IOPower");
        UnityEngine.Object.DontDestroyOnLoad(go);
        var report = go.AddComponent<PowerReport>();
        report.Entities = entities;
    }

    private static string ReadSources(List<IoRecord> into)
    {
        RustIoData best = null;
        int bestValid = 0;
        string bestName = null;

        for (int k = 0; k < RustEditKeys.Length; k++)
            ConsiderRustEdit(RustEditKeys[k], MapDataHelper.GetMapBytes(RustEditKeys[k]), ref best, ref bestValid, ref bestName);

        MapDataHelper.ForEachCustomLayer((name, data) =>
        {
            if (string.Equals(name, MapExtrasData.LayerName, StringComparison.OrdinalIgnoreCase))
                return;
            ConsiderRustEdit(name, data, ref best, ref bestValid, ref bestName);
        });

        if (bestValid > 0 && best?.entities != null)
        {
            for (int i = 0; i < best.entities.Count; i++)
            {
                IoRecord record = FromSerialized(best.entities[i]);
                if (record != null) into.Add(record);
            }
        }

        List<IoRecord> extras = ReadCustomGenerator(out string extrasError, out int extrasVersion);
        int rustCount = into.Count;
        if (extras != null && extras.Count > 0)
            MergeExtras(into, extras);

        if (rustCount == 0 && (extras == null || extras.Count == 0))
        {
            if (!string.IsNullOrEmpty(extrasError))
                return "[RustEditStandalone] No IO data found in map. customgenerator: " + extrasError;
            return "[RustEditStandalone] No IO data found in map.";
        }

        string msg = "[RustEditStandalone] IO data:";
        if (rustCount > 0)
            msg += " " + rustCount + " from RustEdit layer \"" + (bestName ?? "?") + "\" (" + bestValid + " valid)";
        if (extras != null && extras.Count > 0)
            msg += (rustCount > 0 ? ";" : "") + " " + extras.Count + " from customgenerator";
        if (extrasVersion > MapExtrasData.KnownVersion)
            msg += ". customgenerator version " + extrasVersion + " is newer than this mod (" + MapExtrasData.KnownVersion + ")";
        if (!string.IsNullOrEmpty(extrasError))
            msg += ". customgenerator: " + extrasError;
        return msg + ".";
    }

    private static void ConsiderRustEdit(string name, byte[] data, ref RustIoData best, ref int bestValid, ref string bestName)
    {
        if (data == null || data.Length < 10) return;
        if (string.Equals(name, MapExtrasData.LayerName, StringComparison.OrdinalIgnoreCase)) return;

        RustIoData parsed = null;
        if (IOXmlReader.LooksLikeXml(data))
        {
            if (!IOXmlReader.TryRead(data, out parsed)) return;
        }
        else if (!RustEditStandalone.Data.IODataDeserializer.TryDeserialize(data, out parsed))
        {
            return;
        }

        int valid = CountValid(parsed);
        if (valid > bestValid)
        {
            bestValid = valid;
            best = parsed;
            bestName = string.IsNullOrEmpty(name) ? "(unnamed)" : name;
        }
    }

    private static List<IoRecord> ReadCustomGenerator(out string error, out int version)
    {
        error = null;
        version = 0;
        byte[] bytes = null;
        try { bytes = World.GetMap(MapExtrasData.LayerName); }
        catch { /* layer missing */ }

        if (bytes == null || bytes.Length == 0)
        {
            var maps = World.Serialization?.world?.maps;
            if (maps != null)
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    var map = maps[i];
                    if (map?.data == null || map.data.Length == 0) continue;
                    if (!string.Equals(map.name, MapExtrasData.LayerName, StringComparison.OrdinalIgnoreCase)) continue;
                    bytes = map.data;
                    break;
                }
            }
        }

        if (bytes == null || bytes.Length == 0) return null;
        if (!MapExtrasData.TryFromBytes(bytes, out var extras, out error))
            return null;

        version = extras.Version;
        var list = new List<IoRecord>(extras.IO.Count);
        for (int i = 0; i < extras.IO.Count; i++)
        {
            IoRecord record = FromExtras(extras.IO[i]);
            if (record != null) list.Add(record);
        }
        return list;
    }

    private static void MergeExtras(List<IoRecord> into, List<IoRecord> extras)
    {
        for (int i = 0; i < extras.Count; i++)
        {
            IoRecord extra = extras[i];
            int existing = FindRecord(into, extra);
            if (existing >= 0) into[existing] = extra;
            else into.Add(extra);
        }
    }

    private static int FindRecord(List<IoRecord> list, IoRecord record)
    {
        string key = MapDataHelper.GetPrefabKey(record.Prefab);
        for (int i = 0; i < list.Count; i++)
        {
            IoRecord other = list[i];
            if (other == null) continue;
            if (!PrefabAlias(other.Prefab, record.Prefab, key)) continue;
            if ((other.Position - record.Position).sqrMagnitude <= MatchDistanceSqr)
                return i;
        }
        return -1;
    }

    private static bool PrefabAlias(string a, string b, string keyB)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
        string keyA = MapDataHelper.GetPrefabKey(a);
        if (string.IsNullOrEmpty(keyA)) return false;
        if (!string.IsNullOrEmpty(keyB) && keyA.Equals(keyB, StringComparison.OrdinalIgnoreCase)) return true;
        string key = MapDataHelper.GetPrefabKey(b);
        return !string.IsNullOrEmpty(key) && keyA.Equals(key, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountValid(RustIoData data)
    {
        if (data?.entities == null) return 0;
        int n = 0;
        for (int i = 0; i < data.entities.Count; i++)
        {
            var e = data.entities[i];
            if (e == null) continue;
            if (!string.IsNullOrEmpty(e.fullPath)) { n++; continue; }
            Vector3 p = e.position.ToVector3();
            if (Math.Abs(p.x) > 10f || Math.Abs(p.y) > 10f || Math.Abs(p.z) > 10f) n++;
        }
        return n;
    }

    private static Dictionary<string, List<BaseEntity>> BuildIndex(List<IoRecord> records)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < records.Count; i++)
        {
            IoRecord record = records[i];
            if (record == null) continue;
            Want(wanted, record.Prefab);
            WantLinks(wanted, record.Inputs);
            WantLinks(wanted, record.Outputs);
        }

        var index = new Dictionary<string, List<BaseEntity>>(StringComparer.OrdinalIgnoreCase);
        foreach (var ent in BaseNetworkable.serverEntities)
        {
            if (ent is not BaseEntity entity || string.IsNullOrEmpty(entity.PrefabName)) continue;
            string key = MapDataHelper.GetPrefabKey(entity.PrefabName);
            if (!wanted.Contains(entity.PrefabName) && (string.IsNullOrEmpty(key) || !wanted.Contains(key)))
                continue;
            AddIndex(index, entity.PrefabName, entity);
            if (!string.IsNullOrEmpty(key) && !key.Equals(entity.PrefabName, StringComparison.OrdinalIgnoreCase))
                AddIndex(index, key, entity);
        }
        return index;
    }

    private static void WantLinks(HashSet<string> wanted, IoLink[] links)
    {
        if (links == null) return;
        for (int i = 0; i < links.Length; i++)
            if (links[i] != null) Want(wanted, links[i].Prefab);
    }

    private static void Want(HashSet<string> wanted, string prefab)
    {
        if (string.IsNullOrEmpty(prefab)) return;
        wanted.Add(prefab);
        string key = MapDataHelper.GetPrefabKey(prefab);
        if (!string.IsNullOrEmpty(key)) wanted.Add(key);
    }

    private static void AddIndex(Dictionary<string, List<BaseEntity>> index, string key, BaseEntity entity)
    {
        if (!index.TryGetValue(key, out var list))
        {
            list = new List<BaseEntity>();
            index.Add(key, list);
        }
        list.Add(entity);
    }

    private static BaseEntity Find(Dictionary<string, List<BaseEntity>> index, string prefab, Vector3 position)
    {
        if (string.IsNullOrEmpty(prefab)) return null;
        if (!index.TryGetValue(prefab, out var list))
        {
            string key = MapDataHelper.GetPrefabKey(prefab);
            if (string.IsNullOrEmpty(key) || !index.TryGetValue(key, out list))
                return null;
        }

        BaseEntity best = null;
        float bestSq = MatchDistanceSqr;
        for (int i = 0; i < list.Count; i++)
        {
            BaseEntity item = list[i];
            if (item == null) continue;
            float sq = (item.transform.position - position).sqrMagnitude;
            if (sq <= bestSq)
            {
                best = item;
                bestSq = sq;
            }
        }
        return best;
    }

    private static void ApplySettings(IOEntity entity, IoRecord info)
    {
        if (entity is CardReader cardReader && info.AccessLevel > 0)
        {
            cardReader.accessLevel = info.AccessLevel;
            cardReader.SetFlagLocal(cardReader.AccessLevel1, info.AccessLevel == 1);
            cardReader.SetFlagLocal(cardReader.AccessLevel2, info.AccessLevel == 2);
            cardReader.SetFlagLocal(cardReader.AccessLevel3, info.AccessLevel == 3);
            if (info.TimerLength > 0f)
            {
                var monitor = entity.gameObject.GetComponent<CardReaderMonitor>() ?? entity.gameObject.AddComponent<CardReaderMonitor>();
                monitor.Setup(info.TimerLength);
            }
        }

        if (entity is TimerSwitch timerSwitch && info.TimerLength > 0f)
            timerSwitch.timerLength = info.TimerLength;
        if (entity is PressButton pressButton && info.TimerLength > 0f)
            pressButton.pressDuration = info.TimerLength;

        if (entity is RFReceiver rfReceiver && info.Frequency > 0)
            rfReceiver.SetFrequency(info.Frequency);
        if (entity is RFBroadcaster rfBroadcaster && info.Frequency > 0)
            rfBroadcaster.SetFrequency(info.Frequency);

        if (entity is ElectricalBranch branch && info.BranchAmount > 0)
            branch.branchAmount = info.BranchAmount;

        if (entity is PowerCounter counter)
        {
            if (info.TargetCounterNumber > 0 && CounterTarget != null)
                CounterTarget.SetValue(counter, Mathf.Clamp(info.TargetCounterNumber, 1, 999));
            counter.SetFlagLocal(BaseEntity.Flags.Reserved3, info.CounterPassthrough);
            counter.MarkDirty();
        }

        if (entity is DoorManipulator door && info.DoorEffect >= 0 && info.DoorEffect <= (int)DoorManipulator.DoorEffect.Toggle)
            door.powerAction = (DoorManipulator.DoorEffect)info.DoorEffect;

        if (entity is AutoTurret)
        {
            var mgr = entity.gameObject.GetComponent<AutoTurretManager>() ?? entity.gameObject.AddComponent<AutoTurretManager>();
            mgr.Setup(info.UnlimitedAmmo, info.PeaceKeeper, info.AutoTurretWeapon);
            if (info.UnlimitedAmmo && entity.net != null)
                UnlimitedTurrets.Add(entity.net.ID);
        }

        if (entity is Elevator elevator && info.Floors > 0)
            elevator.Floor = info.Floors;

        if (!string.IsNullOrEmpty(info.RcIdentifier) && entity is IRemoteControllable remote)
            remote.UpdateIdentifier(info.RcIdentifier, false);

        if (!string.IsNullOrEmpty(info.PhoneName))
        {
            var phone = entity.GetComponent<PhoneController>() ?? entity.GetComponentInChildren<PhoneController>();
            if (phone != null)
                phone.PhoneName = info.PhoneName;
        }

        if (entity.name.IndexOf("wheelswitch", StringComparison.OrdinalIgnoreCase) >= 0 ||
            (entity.PrefabName != null && entity.PrefabName.IndexOf("wheelswitch", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            if (entity.gameObject.GetComponent<IoToWheelSwitch>() == null)
                entity.gameObject.AddComponent<IoToWheelSwitch>();
        }

        entity.SendNetworkUpdate();
    }

    private static void EnsureSlotRefs(IOEntity entity)
    {
        EnsureRefs(entity.inputs);
        EnsureRefs(entity.outputs);
    }

    private static void EnsureRefs(IOEntity.IOSlot[] slots)
    {
        if (slots == null) return;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] != null) EnsureRef(slots[i]);
    }

    private static void EnsureRef(IOEntity.IOSlot slot)
    {
        if (slot.connectedTo == null)
            slot.connectedTo = new IOEntity.IORef();
    }

    private static void Track(IOEntity entity)
    {
        if (entity?.net == null) return;
        MapIoIds.Add(entity.net.ID);
        if (!MapIoEntities.Contains(entity))
            MapIoEntities.Add(entity);
    }

    private static IoRecord FromSerialized(RustIoEntity e)
    {
        if (e == null || string.IsNullOrEmpty(e.fullPath)) return null;
        return new IoRecord
        {
            Prefab = e.fullPath,
            Position = e.position.ToVector3(),
            Inputs = ToLinks(e.inputs),
            Outputs = ToLinks(e.outputs),
            AccessLevel = e.accessLevel,
            DoorEffect = e.doorEffect,
            TimerLength = e.timerLength,
            Frequency = e.frequency,
            UnlimitedAmmo = e.unlimitedAmmo,
            PeaceKeeper = e.peaceKeeper,
            AutoTurretWeapon = e.autoTurretWeapon,
            BranchAmount = e.branchAmount,
            TargetCounterNumber = e.targetCounterNumber,
            RcIdentifier = e.rcIdentifier,
            CounterPassthrough = e.counterPassthrough,
            Floors = e.floors,
            PhoneName = e.phoneName
        };
    }

    private static IoLink[] ToLinks(RustIoConnection[] links)
    {
        if (links == null || links.Length == 0) return null;
        var arr = new IoLink[links.Length];
        for (int i = 0; i < links.Length; i++)
        {
            RustIoConnection c = links[i];
            if (c == null || string.IsNullOrEmpty(c.fullPath)) continue;
            arr[i] = new IoLink
            {
                Prefab = c.fullPath,
                Position = c.position.ToVector3(),
                Slot = c.connectedTo
            };
        }
        return arr;
    }

    private static IoRecord FromExtras(MapExtrasIoEntity e)
    {
        if (e == null || string.IsNullOrEmpty(e.Prefab)) return null;
        return new IoRecord
        {
            Prefab = e.Prefab,
            Position = ToPos(e.Position),
            Inputs = ToLinks(e.Inputs),
            Outputs = ToLinks(e.Outputs),
            AccessLevel = e.AccessLevel,
            DoorEffect = e.DoorEffect,
            TimerLength = e.TimerLength,
            Frequency = e.Frequency,
            UnlimitedAmmo = e.UnlimitedAmmo,
            PeaceKeeper = e.PeaceKeeper,
            AutoTurretWeapon = e.AutoTurretWeapon,
            BranchAmount = e.BranchAmount,
            TargetCounterNumber = e.TargetCounterNumber,
            RcIdentifier = e.RcIdentifier,
            CounterPassthrough = e.CounterPassthrough,
            Floors = e.Floors,
            PhoneName = e.PhoneName
        };
    }

    private static IoLink[] ToLinks(List<MapExtrasIoConnection> links)
    {
        if (links == null || links.Count == 0) return null;
        var arr = new IoLink[links.Count];
        for (int i = 0; i < links.Count; i++)
        {
            MapExtrasIoConnection c = links[i];
            if (c == null || string.IsNullOrEmpty(c.Prefab)) continue;
            arr[i] = new IoLink
            {
                Prefab = c.Prefab,
                Position = ToPos(c.Position),
                Slot = c.Slot
            };
        }
        return arr;
    }

    private static Vector3 ToPos(float[] p)
    {
        if (p == null || p.Length < 3) return Vector3.zero;
        return new Vector3(p[0], p[1], p[2]);
    }

    private static string Short(string prefab)
    {
        if (string.IsNullOrEmpty(prefab)) return "?";
        return Path.GetFileNameWithoutExtension(prefab.Replace('\\', '/'));
    }

    private static string Format(Vector3 p)
    {
        return p.x.ToString("0.0") + ", " + p.y.ToString("0.0") + ", " + p.z.ToString("0.0");
    }

    private sealed class IoRecord
    {
        public string Prefab;
        public Vector3 Position;
        public IoLink[] Inputs;
        public IoLink[] Outputs;
        public int AccessLevel;
        public int DoorEffect = -1;
        public float TimerLength;
        public int Frequency;
        public bool UnlimitedAmmo;
        public bool PeaceKeeper;
        public string AutoTurretWeapon;
        public int BranchAmount;
        public int TargetCounterNumber;
        public string RcIdentifier;
        public bool CounterPassthrough;
        public int Floors = 1;
        public string PhoneName;
    }

    private sealed class IoLink
    {
        public string Prefab;
        public Vector3 Position;
        public int Slot;
    }

    private sealed class PowerReport : MonoBehaviour
    {
        public List<IOEntity> Entities;

        private void Awake()
        {
            Invoke(nameof(Report), 5f);
        }

        private void Report()
        {
            int total = Entities?.Count ?? 0;
            int powered = 0;
            if (Entities != null)
            {
                for (int i = 0; i < Entities.Count; i++)
                {
                    IOEntity entity = Entities[i];
                    if (entity != null && !entity.IsDestroyed && entity.IsPowered())
                        powered++;
                }
            }
            Debug.Log("[RustEditStandalone] IO: " + powered + "/" + total + " entities have power.");
            Destroy(gameObject);
        }
    }
}
