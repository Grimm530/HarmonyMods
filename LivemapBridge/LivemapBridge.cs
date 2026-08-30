using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using UnityEngine;
using Building = BuildingManager.Building;
using Debug = UnityEngine.Debug;

namespace LivemapBridge;

public class LivemapBridgeMod : IHarmonyModHooks
{
    public static LivemapBridgeMod Instance { get; private set; }

    const string CmdName = "livemap.snapshot";
    const string CmdFull = "global.livemap.snapshot";

    ConsoleSystem.Command _cmd;
    ConfigFile _config;
    string _configPath;
    bool _wrotePanelWarning;
    bool _loopStarted;
    bool _unloaded;
    bool _monumentsDumped;
    bool _mapExported;
    int _mapExportAttempts;
    float _lastBuildRealtime = -999f;
    float _lastSlowTickLog;
    FieldInfo _convoyVehiclesField;
    FieldInfo _convoyEntityField;
    PropertyInfo _convoyInstanceProp;
    MethodInfo _convoyIsActiveMethod;
    MethodInfo _isConvoyEntityMethod;
    Type _armoredTrainModType;
    PropertyInfo _armoredTrainPluginProp;
    FieldInfo _armoredTrainControllerField;
    FieldInfo _armoredTrainWagonsField;
    FieldInfo _armoredTrainEngineField;
    FieldInfo _armoredTrainWagonCarField;
    bool _armoredTrainResolved;
    readonly List<PlayerRow> _players = new List<PlayerRow>(64);
    readonly List<VehicleRow> _vehicles = new List<VehicleRow>(32);
    readonly HashSet<ulong> _seenVehicleIds = new HashSet<ulong>();
    readonly StringBuilder _sb = new StringBuilder(262144);
    static readonly Dictionary<string, PrefabKind> PrefabKindCache = new Dictionary<string, PrefabKind>(128);

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;
        _unloaded = false;
        LoadConfig();
        RegisterCommand();
        StartLoop();
        Debug.Log("[LivemapBridge] Loaded. Writes " + _config.PanelOutputPath);
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        _unloaded = true;
        StopLoop();
        UnregisterCommand();
        LivemapBradleyTracker.Clear();
        Instance = null;
        Debug.Log("[LivemapBridge] Unloaded.");
    }

    void LoadConfig()
    {
        _config = new ConfigFile();
        try
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string dir = Path.Combine(root, "HarmonyConfig");
            Directory.CreateDirectory(dir);
            _configPath = Path.Combine(dir, "LivemapBridge.json");
            if (File.Exists(_configPath))
            {
                _config = JsonConvert.DeserializeObject<ConfigFile>(File.ReadAllText(_configPath)) ?? new ConfigFile();
            }
            else
            {
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(_config, Formatting.Indented));
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LivemapBridge] Config: " + ex.Message);
        }

        if (_config.IntervalSeconds < 0.5f)
            _config.IntervalSeconds = 0.5f;
        if (_config.BuildingsIntervalSeconds < 5f)
            _config.BuildingsIntervalSeconds = 30f;
        if (_config.VehicleScanIntervalSeconds < 2f)
            _config.VehicleScanIntervalSeconds = 5f;
    }

    void StartLoop(int attempt = 0)
    {
        if (_loopStarted)
            return;

        var handler = SingletonComponent<InvokeHandler>.Instance;
        if (handler != null)
        {
            try
            {
                InvokeHandler.CancelInvoke(handler, Tick);
            }
            catch
            {
            }
            InvokeHandler.InvokeRepeating(handler, Tick, 2f, _config.IntervalSeconds);
            _loopStarted = true;
            _lastBuildRealtime = Time.realtimeSinceStartup;
            LivemapBradleyTracker.SeedFromWorld();
            if (attempt > 0)
                Debug.Log("[LivemapBridge] Snapshot loop started after boot wait (" + attempt + ").");
            else
                Debug.Log("[LivemapBridge] Snapshot loop started.");
            return;
        }

        // Boot often loads Harmony before InvokeHandler exists. Retry like Minimap —
        // otherwise the map freezes on the last pre-restart snapshot (empty players).
        if (attempt > 120)
        {
            Debug.LogWarning("[LivemapBridge] InvokeHandler never ready; run: harmony.load LivemapBridge");
            return;
        }

        float delay = attempt < 20 ? 0.5f : 1f;
        if (ServerMgr.Instance != null)
        {
            ServerMgr.Instance.Invoke(() => StartLoop(attempt + 1), delay);
            return;
        }

        try
        {
            var go = new GameObject("LivemapBridge_InitWait");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<InitWaitBehaviour>().Begin(this, attempt);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LivemapBridge] StartLoop retry failed: " + ex.Message);
        }
    }

    void StopLoop()
    {
        _loopStarted = false;
        var handler = SingletonComponent<InvokeHandler>.Instance;
        if (handler == null)
            return;
        try
        {
            InvokeHandler.CancelInvoke(handler, Tick);
        }
        catch
        {
        }
    }

    class InitWaitBehaviour : MonoBehaviour
    {
        LivemapBridgeMod _mod;
        int _attempt;

        public void Begin(LivemapBridgeMod mod, int attempt)
        {
            _mod = mod;
            _attempt = attempt;
            StartCoroutine(Wait());
        }

        IEnumerator Wait()
        {
            yield return new WaitForSeconds(0.5f);
            var mod = _mod;
            var attempt = _attempt;
            Destroy(gameObject);
            mod?.StartLoop(attempt + 1);
        }
    }

    void RegisterCommand()
    {
        try
        {
            _cmd = new ConsoleSystem.Command
            {
                Name = CmdName,
                FullName = CmdFull,
                Variable = false,
                ServerAdmin = true,
                Call = CmdSnapshot
            };
            ConsoleSystem.Index.Server.Dict[CmdFull] = _cmd;
            if (ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict[CmdName] = _cmd;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LivemapBridge] Command failed: " + ex.Message);
        }
    }

    void UnregisterCommand()
    {
        try
        {
            ConsoleSystem.Index.Server.Dict?.Remove(CmdFull);
            ConsoleSystem.Index.Server.GlobalDict?.Remove(CmdName);
            _cmd = null;
        }
        catch
        {
        }
    }

    void CmdSnapshot(ConsoleSystem.Arg arg)
    {
        arg?.ReplyWith(BuildJson());
    }

    void Tick()
    {
        if (_unloaded || BaseNetworkable.serverEntities == null)
            return;

        var sw = Stopwatch.StartNew();
        MaybeDumpMonuments();
        MaybeExportMapImage();

        string json = BuildJson();
        QueueWrite(_config.HarmonyOutputPath, json);
        QueueWrite(_config.PanelOutputPath, json);

        float now = Time.realtimeSinceStartup;
        if (now - _lastBuildRealtime >= _config.BuildingsIntervalSeconds)
        {
            _lastBuildRealtime = now;
            string buildings = BuildBuildingsJson();
            QueueWrite(_config.HarmonyBuildingsPath, buildings);
            QueueWrite(_config.PanelBuildingsPath, buildings);
        }

        if (sw.ElapsedMilliseconds >= 80 && now - _lastSlowTickLog >= 10f)
        {
            _lastSlowTickLog = now;
            Debug.LogWarning("[LivemapBridge] Tick took " + sw.ElapsedMilliseconds + "ms (markers=" + MapMarker.serverMapMarkers.Count + " bradleys=" + LivemapBradleyTracker.Count + ")");
        }
    }

    void QueueWrite(string path, string json)
    {
        if (_unloaded || string.IsNullOrEmpty(path) || json == null)
            return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            if (_unloaded)
                return;
            WriteAtomic(path, json);
        });
    }

    void WriteAtomic(string path, string json)
    {
        if (string.IsNullOrEmpty(path))
            return;
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }
        catch (Exception ex)
        {
            if (string.Equals(path, _config.PanelOutputPath, StringComparison.OrdinalIgnoreCase))
            {
                if (!_wrotePanelWarning)
                {
                    _wrotePanelWarning = true;
                    Debug.LogWarning("[LivemapBridge] Could not write panel snapshot: " + ex.Message);
                }
            }
        }
    }

    string BuildJson()
    {
        _players.Clear();
        _vehicles.Clear();

        if (BasePlayer.activePlayerList != null)
        {
            foreach (BasePlayer p in BasePlayer.activePlayerList)
            {
                if (p == null || p.IsNpc || !p.IsConnected || p.transform == null)
                    continue;
                Vector3 pos = p.transform.position;
                float yaw = p.transform.eulerAngles.y;
                if (p.eyes != null)
                    yaw = p.eyes.rotation.eulerAngles.y;
                _players.Add(new PlayerRow
                {
                    id = p.UserIDString,
                    name = p.displayName ?? p.UserIDString,
                    x = pos.x,
                    y = pos.y,
                    z = pos.z,
                    yaw = yaw
                });
            }
        }

        AddVehicles(_vehicles);
        AddConvoy(_vehicles);
        AddArmoredTrain(_vehicles);

        uint size = World.Size;
        if (size < 1000)
            size = 4000;

        var snap = new Snapshot
        {
            live = true,
            worldSize = size,
            players = _players,
            vehicles = _vehicles
        };
        return JsonConvert.SerializeObject(snap);
    }

    string BuildBuildingsJson()
    {
        _sb.Length = 0;
        _sb.Append("{\"live\":true,\"blocks\":[");
        bool first = true;
        int count = 0;

        var dict = BuildingManager.server?.buildingDictionary;
        if (dict != null)
        {
            var buildings = dict.Values;
            for (int i = 0; i < buildings.Count && count < 15000; i++)
            {
                Building building = buildings[i];
                if (building == null)
                    continue;

                for (int p = 0; p < building.buildingPrivileges.Count && count < 15000; p++)
                {
                    BuildingPrivlidge cup = building.buildingPrivileges[p];
                    if (!TryAppendBlock(cup, 4, 0, 1f, false, ref first))
                        continue;
                    count++;
                }

                for (int b = 0; b < building.buildingBlocks.Count && count < 15000; b++)
                {
                    BuildingBlock block = building.buildingBlocks[b];
                    if (block == null || block.IsDestroyed || block.transform == null)
                        continue;
                    if (block.transform.position.y < -5f)
                        continue;
                    int kind = KindFromPrefabCached(block.ShortPrefabName, out float hScale, out bool triangle);
                    if (kind < 0)
                        continue;
                    int grade = (int)block.grade;
                    if (grade < 0) grade = 0;
                    if (grade > 4) grade = 4;
                    if (!TryAppendBlock(block, kind, grade, hScale, triangle, ref first))
                        continue;
                    count++;
                }
            }
        }

        _sb.Append("]}");
        return _sb.ToString();
    }

    bool TryAppendBlock(BaseEntity ent, int kind, int grade, float hScale, bool triangle, ref bool first)
    {
        if (ent == null || ent.IsDestroyed || ent.transform == null)
            return false;
        Vector3 pos = ent.transform.position;
        if (pos.y < -5f)
            return false;
        if (!first)
            _sb.Append(',');
        first = false;
        _sb.Append("{\"x\":");
        AppendFloat(pos.x);
        _sb.Append(",\"y\":");
        AppendFloat(pos.y);
        _sb.Append(",\"z\":");
        AppendFloat(pos.z);
        _sb.Append(",\"yaw\":");
        AppendFloat(ent.transform.eulerAngles.y);
        _sb.Append(",\"k\":");
        _sb.Append(kind);
        _sb.Append(",\"g\":");
        _sb.Append(grade);
        _sb.Append(",\"h\":");
        AppendFloat(hScale);
        _sb.Append(",\"t\":");
        _sb.Append(triangle ? 1 : 0);
        _sb.Append('}');
        return true;
    }

    void AppendFloat(float value)
    {
        _sb.Append(value.ToString("G9", CultureInfo.InvariantCulture));
    }

    // k: 0 foundation, 1 wall, 2 floor, 3 roof, 4 cupboard, 5 stairs/misc
    // t: 1 = triangle footprint (foundation/floor/roof.triangle)
    // h: wall height scale (half=0.5, low/third≈0.667)
    static int KindFromPrefabCached(string name, out float heightScale, out bool triangle)
    {
        if (!string.IsNullOrEmpty(name) && PrefabKindCache.TryGetValue(name, out PrefabKind cached))
        {
            heightScale = cached.h;
            triangle = cached.t;
            return cached.k;
        }
        int kind = KindFromPrefab(name, out heightScale, out triangle);
        if (!string.IsNullOrEmpty(name))
            PrefabKindCache[name] = new PrefabKind { k = kind, h = heightScale, t = triangle };
        return kind;
    }

    static int KindFromPrefab(string name, out float heightScale, out bool triangle)
    {
        heightScale = 1f;
        triangle = false;
        if (string.IsNullOrEmpty(name))
            return 5;
        string n = name.ToLowerInvariant();
        if (n.Contains("triangle"))
            triangle = true;
        if (n.Contains("cupboard"))
            return 4;
        if (n.Contains("foundation"))
            return 0;
        // floor before "frame" — floor.frame must not become a wall
        if (n.Contains("floor") || n.Contains("ramp"))
            return 2;
        if (n.Contains("roof"))
            return 3;
        if (n.Contains("stair") || n.Contains("steps"))
            return 5;
        if (n.Contains("wall.half") || n.Contains("halfwall"))
        {
            heightScale = 0.5f;
            return 1;
        }
        if (n.Contains("wall.low") || n.Contains("lowwall") || n.Contains("wall.third") || n.Contains("thirdwall"))
        {
            heightScale = 0.667f;
            return 1;
        }
        if (n.Contains("wall") || n.Contains("doorway") || n.Contains("window") || n.Contains("frame"))
            return 1;
        return 5;
    }

    void AddVehicles(List<VehicleRow> vehicles)
    {
        _seenVehicleIds.Clear();
        List<MapMarker> markers = MapMarker.serverMapMarkers;
        if (markers != null)
        {
            for (int i = 0; i < markers.Count; i++)
            {
                MapMarker marker = markers[i];
                if (marker == null || marker.IsDestroyed || marker is MapMarkerHelicopterFlee)
                    continue;
                if (marker.GetParentEntity() is not PatrolHelicopter heli)
                    continue;
                if (heli.IsDestroyed || heli.transform == null)
                    continue;
                ulong nid = heli.net != null ? heli.net.ID.Value : 0;
                if (nid != 0 && !_seenVehicleIds.Add(nid))
                    continue;
                AddVehicle(vehicles, heli, "patrolheli");
            }
        }

        PatrolHelicopter inst = PatrolHelicopter.Instance;
        if (inst != null && !inst.IsDestroyed && inst.transform != null)
        {
            ulong nid = inst.net != null ? inst.net.ID.Value : 0;
            if (nid == 0 || _seenVehicleIds.Add(nid))
                AddVehicle(vehicles, inst, "patrolheli");
        }

        List<BradleyAPC> bradleys = LivemapBradleyTracker.LiveList;
        for (int i = bradleys.Count - 1; i >= 0; i--)
        {
            BradleyAPC bradley = bradleys[i];
            if (bradley == null || bradley.IsDestroyed || bradley.transform == null)
            {
                bradleys.RemoveAt(i);
                continue;
            }
            if (IsConvoyVehicle(bradley))
                continue;
            AddVehicle(vehicles, bradley, "bradley");
        }
    }

    static void AddVehicle(List<VehicleRow> vehicles, BaseNetworkable ent, string type)
    {
        AddVehicleRow(vehicles, ent, type, type + "-" + VehicleNetId(ent));
    }

    static void AddConvoyVehicle(List<VehicleRow> vehicles, BaseEntity ent, string type)
    {
        AddVehicleRow(vehicles, ent, type, "convoy-" + VehicleNetId(ent));
    }

    static string VehicleNetId(BaseNetworkable ent)
    {
        return ent.net != null ? ent.net.ID.Value.ToString() : ent.GetInstanceID().ToString();
    }

    static void AddVehicleRow(List<VehicleRow> vehicles, BaseNetworkable ent, string type, string id)
    {
        Vector3 pos = ent.transform.position;
        vehicles.Add(new VehicleRow
        {
            id = id,
            type = type,
            x = pos.x,
            y = pos.y,
            z = pos.z,
            yaw = EntityMapYaw(ent)
        });
    }

    /// <summary>
    /// Yaw for the livemap from transform.forward (stable vs euler gimbal).
    /// Bradleys welded to a TrainCar use the wagon forward so they follow the track.
    /// </summary>
    static float EntityMapYaw(BaseNetworkable ent)
    {
        Transform t = ent.transform;
        BaseEntity parent = (ent as BaseEntity)?.GetParentEntity();
        if (parent is TrainCar)
            t = parent.transform;

        Vector3 f = t.forward;
        return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    void AddConvoy(List<VehicleRow> vehicles)
    {
        if (!IsConvoyEventActive())
            return;
        object inst = FindConvoyInstance();
        if (inst == null || _convoyVehiclesField == null)
            return;
        object raw = _convoyVehiclesField.GetValue(inst);
        if (raw is not IEnumerable enumerable)
            return;
        int n = 0;
        foreach (object item in enumerable)
        {
            if (item == null)
                continue;
            FieldInfo entField = _convoyEntityField;
            if (entField == null)
            {
                entField = item.GetType().GetField("Entity");
                if (entField == null)
                    continue;
                _convoyEntityField = entField;
            }
            if (entField.GetValue(item) is not BaseEntity ent || ent == null || ent.transform == null || ent.IsDestroyed)
                continue;
            AddConvoyVehicle(vehicles, ent, ConvoyVehicleType(ent));
            n++;
            if (n >= 12)
                break;
        }
    }

    /// <summary>
    /// Pull locomotive + wagons from the live ArmoredTrain event (not every world TrainCar).
    /// </summary>
    void AddArmoredTrain(List<VehicleRow> vehicles)
    {
        object controller = FindArmoredTrainController();
        if (controller == null)
            return;

        FieldInfo wagonsField = _armoredTrainWagonsField;
        FieldInfo engineField = _armoredTrainEngineField;
        if (wagonsField == null)
            return;

        var seen = new HashSet<ulong>();
        if (engineField?.GetValue(controller) is TrainEngine engine && engine != null && !engine.IsDestroyed && engine.transform != null)
        {
            ulong id = engine.net != null ? engine.net.ID.Value : 0;
            if (id != 0) seen.Add(id);
            AddVehicleRow(vehicles, engine, "locomotive", "atrain-loco-" + VehicleNetId(engine));
        }

        if (wagonsField.GetValue(controller) is not IEnumerable wagonList)
            return;

        int n = 0;
        foreach (object wagonData in wagonList)
        {
            if (wagonData == null)
                continue;
            FieldInfo carField = _armoredTrainWagonCarField;
            if (carField == null)
            {
                carField = wagonData.GetType().GetField("TrainCar", BindingFlags.Public | BindingFlags.Instance);
                if (carField == null)
                    continue;
                _armoredTrainWagonCarField = carField;
            }
            if (carField.GetValue(wagonData) is not TrainCar car || car == null || car.IsDestroyed || car.transform == null)
                continue;
            ulong nid = car.net != null ? car.net.ID.Value : 0;
            if (nid != 0 && !seen.Add(nid))
                continue;
            string type = TrainMapType(car);
            string prefix = type == "locomotive" ? "atrain-loco-" : "atrain-wagon-";
            AddVehicleRow(vehicles, car, type, prefix + VehicleNetId(car));
            n++;
            if (n >= 24)
                break;
        }
    }

    static string TrainMapType(TrainCar car)
    {
        if (car is TrainEngine)
            return "locomotive";
        string sn = (car.ShortPrefabName ?? "").ToLowerInvariant();
        if (sn.Contains("fuel"))
            return "wagon_fuel";
        if (sn.Contains("loot"))
            return "wagon_loot";
        if (sn.Contains("flat") || sn.Contains("unloadable"))
            return "wagon_flat";
        return "wagon";
    }

    object FindArmoredTrainController()
    {
        try
        {
            if (!_armoredTrainResolved)
            {
                _armoredTrainResolved = true;
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    Type modType;
                    try
                    {
                        modType = asms[i].GetType("ArmoredTrain.ArmoredTrainMod");
                    }
                    catch
                    {
                        continue;
                    }
                    if (modType == null)
                        continue;
                    _armoredTrainModType = modType;
                    _armoredTrainPluginProp = modType.GetProperty("Plugin", BindingFlags.Public | BindingFlags.Static);
                    break;
                }
            }

            if (_armoredTrainPluginProp == null)
                return null;

            object plugin = _armoredTrainPluginProp.GetValue(null);
            if (plugin == null)
                return null;

            if (_armoredTrainControllerField == null)
            {
                _armoredTrainControllerField = plugin.GetType().GetField("_eventController", BindingFlags.NonPublic | BindingFlags.Instance);
                if (_armoredTrainControllerField == null)
                    return null;
            }

            object controller = _armoredTrainControllerField.GetValue(plugin);
            if (controller == null)
                return null;

            if (_armoredTrainWagonsField == null)
            {
                Type ct = controller.GetType();
                _armoredTrainWagonsField = ct.GetField("_wagonDatas", BindingFlags.NonPublic | BindingFlags.Instance);
                _armoredTrainEngineField = ct.GetField("_trainEngine", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            return controller;
        }
        catch
        {
            return null;
        }
    }

    static string ConvoyVehicleType(BaseEntity ent)
    {
        if (ent is BradleyAPC)
            return "bradley";
        if (ent is ModularCar)
            return "modular_car";
        string sn = ent.ShortPrefabName ?? "";
        if (sn.IndexOf("travellingvendor", StringComparison.OrdinalIgnoreCase) >= 0)
            return "vendor";
        if (sn.IndexOf("sedan", StringComparison.OrdinalIgnoreCase) >= 0)
            return "sedan";
        return "sedan";
    }

    bool IsConvoyVehicle(BaseEntity ent)
    {
        if (ent?.net == null)
            return false;
        if (_isConvoyEntityMethod == null)
        {
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                Type state;
                try
                {
                    state = asms[i].GetType("Convoy.ConvoyState");
                }
                catch
                {
                    continue;
                }
                if (state == null)
                    continue;
                _isConvoyEntityMethod = state.GetMethod("IsConvoyEntity", BindingFlags.Public | BindingFlags.Static);
                break;
            }
        }
        if (_isConvoyEntityMethod == null)
            return false;
        try
        {
            return _isConvoyEntityMethod.Invoke(null, new object[] { ent.net.ID.Value }) is true;
        }
        catch
        {
            return false;
        }
    }

    bool IsConvoyEventActive()
    {
        if (_convoyIsActiveMethod == null)
        {
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                Type launcher;
                try
                {
                    launcher = asms[i].GetType("Convoy.EventLauncher");
                }
                catch
                {
                    continue;
                }
                if (launcher == null)
                    continue;
                _convoyIsActiveMethod = launcher.GetMethod("IsEventActive", BindingFlags.Public | BindingFlags.Static);
                break;
            }
        }
        if (_convoyIsActiveMethod == null)
            return FindConvoyInstance() != null;
        try
        {
            return _convoyIsActiveMethod.Invoke(null, null) is true;
        }
        catch
        {
            return false;
        }
    }

    void MaybeDumpMonuments()
    {
        if (_monumentsDumped || TerrainMeta.Path == null)
            return;

        // Use MonumentInfo only — Landmarks also includes dungeon entrances /
        // child markers that share the parent prefab name and look "offset".
        List<MonumentInfo> monuments = GetPathMonuments();
        if (monuments == null || monuments.Count < 4)
            return;

        string json = BuildMonumentsJson(monuments);
        if (string.IsNullOrEmpty(json))
            return;

        WriteAtomic(_config.HarmonyMonumentsPath, json);
        if (!string.IsNullOrEmpty(_config.PanelMonumentsPath))
            WriteAtomic(_config.PanelMonumentsPath, json);

        _monumentsDumped = true;
        Debug.Log("[LivemapBridge] Wrote " + monuments.Count + " monument placements (MonumentInfo only).");
    }

    List<MonumentInfo> GetPathMonuments()
    {
        try
        {
            FieldInfo fi = typeof(TerrainPath).GetField("Monuments", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return fi?.GetValue(TerrainMeta.Path) as List<MonumentInfo>;
        }
        catch
        {
            return null;
        }
    }

    string BuildMonumentsJson(List<MonumentInfo> monuments)
    {
        var rows = new List<MonumentRow>(64);
        var seen = new HashSet<string>();
        foreach (MonumentInfo mi in monuments)
        {
            if (mi == null || mi.transform == null)
                continue;

            string key = ResolveMonumentKey(mi);
            if (string.IsNullOrEmpty(key))
                continue;

            Vector3 pos = mi.transform.position;
            Quaternion rot = mi.transform.rotation;
            // Compound at world origin with no height is a bogus TerrainPath stub on some wipes.
            // CustomMapGen Outpost swap sits at map center with real terrain Y — keep that one.
            if (key == "compound" && Mathf.Abs(pos.x) < 2f && Mathf.Abs(pos.z) < 2f && pos.y < 10f)
                continue;
            // One row per unique key+rounded position (roadside can repeat).
            string dedupe = key + ":" + (int)Math.Round(pos.x) + ":" + (int)Math.Round(pos.z);
            if (!seen.Add(dedupe))
                continue;

            rows.Add(new MonumentRow
            {
                key = key,
                name = mi.transform.root != null ? mi.transform.root.name : mi.name,
                label = GetDisplayLabel(mi, key),
                x = pos.x,
                y = pos.y,
                z = pos.z,
                yaw = rot.eulerAngles.y,
                qx = rot.x,
                qy = rot.y,
                qz = rot.z,
                qw = rot.w,
                attachments = CollectMonumentAttachments(mi, key),
                excavatorRotor = key == "excavator" ? BuildExcavatorRotorState(mi) : null
            });
        }

        MaybeInjectCenterOutpost(rows);

        if (rows.Count == 0)
            return null;

        var snap = new MonumentSnap
        {
            live = true,
            source = "runtime_monuments",
            worldSize = World.Size,
            count = rows.Count,
            monuments = rows
        };
        return JsonConvert.SerializeObject(snap, Formatting.Indented);
    }

    static List<MonumentAttachment> CollectMonumentAttachments(MonumentInfo mi, string key)
    {
        if (mi == null || mi.transform == null || string.IsNullOrEmpty(key))
            return null;
        if (!key.StartsWith("desert_base"))
            return null;

        Transform root = mi.transform.root != null ? mi.transform.root : mi.transform;
        var list = new List<MonumentAttachment>(32);
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || t == root)
                continue;

            string n = (t.name ?? "").ToLowerInvariant();
            if (!n.Contains("tent_tunnel") && !n.Contains("tent_"))
                continue;

            Vector3 lp = root.InverseTransformPoint(t.position);
            list.Add(new MonumentAttachment
            {
                name = t.name,
                x = lp.x,
                y = lp.y,
                z = lp.z,
                yaw = t.localEulerAngles.y
            });
        }

        return list.Count > 0 ? list : null;
    }

    // Prefab-local Excavator_Yaw / bucket_excavator_head origin (not the core disc).
    static readonly Vector3 ExcavatorHeadLocal = new Vector3(-33.5f, 18f, 41.9f);

    static ExcavatorRotorRow BuildExcavatorRotorState(MonumentInfo mi)
    {
        if (mi == null || mi.transform == null)
            return null;

        float monYaw = mi.transform.eulerAngles.y;
        Vector3 monPos = mi.transform.position;
        Quaternion monRot = Quaternion.Euler(0f, monYaw, 0f);
        Vector3 expectedHead = monPos + monRot * ExcavatorHeadLocal;

        ExcavatorArm best = null;
        float bestDist = 40f; // must be near the yaw pivot, not just the monument origin

        foreach (BaseNetworkable ent in BaseNetworkable.serverEntities)
        {
            if (ent is not ExcavatorArm arm || arm.transform == null)
                continue;
            float d = Vector3.Distance(arm.transform.position, expectedHead);
            if (d > bestDist)
                continue;
            bestDist = d;
            best = arm;
        }

        // Fallback: nearest arm to monument origin (legacy), still export for debug.
        if (best == null)
        {
            bestDist = 200f;
            foreach (BaseNetworkable ent in BaseNetworkable.serverEntities)
            {
                if (ent is not ExcavatorArm arm || arm.transform == null)
                    continue;
                float d = Vector3.Distance(arm.transform.position, monPos);
                if (d > bestDist)
                    continue;
                bestDist = d;
                best = arm;
            }
        }

        if (best == null)
        {
            Debug.LogWarning($"[LivemapBridge] ExcavatorArm missing near head {expectedHead} (monument {monPos})");
            return new ExcavatorRotorRow
            {
                x = expectedHead.x,
                y = expectedHead.y,
                z = expectedHead.z,
                expectedX = expectedHead.x,
                expectedY = expectedHead.y,
                expectedZ = expectedHead.z,
                yaw1 = -4f + monYaw,
                yaw2 = 132.3f + monYaw,
                turnSpeed = 0.1f,
                movedAmount = 0f,
                mining = false,
                matched = false
            };
        }

        // Facepunch + RustEdit: arm sweep limits are fixed offsets from monument yaw.
        if (Mathf.Abs(best.yaw2 - best.yaw1) < 1f)
        {
            best.yaw1 = -4f + monYaw;
            best.yaw2 = 132.3f + monYaw;
        }

        float moved = 0f;
        try
        {
            FieldInfo fi = typeof(ExcavatorArm).GetField("movedAmount", BindingFlags.Instance | BindingFlags.NonPublic);
            if (fi != null)
                moved = (float)fi.GetValue(best);
        }
        catch
        {
            // ignore
        }

        float armDist = Vector3.Distance(best.transform.position, expectedHead);
        Debug.Log($"[LivemapBridge] ExcavatorArm at {best.transform.position} expectedHead {expectedHead} dist {armDist:F1}m matched={armDist <= 40f}");

        return new ExcavatorRotorRow
        {
            x = best.transform.position.x,
            y = best.transform.position.y,
            z = best.transform.position.z,
            expectedX = expectedHead.x,
            expectedY = expectedHead.y,
            expectedZ = expectedHead.z,
            yaw1 = best.yaw1,
            yaw2 = best.yaw2,
            turnSpeed = best.turnSpeed,
            movedAmount = moved,
            mining = best.IsMining(),
            matched = armDist <= 40f
        };
    }

    void MaybeInjectCenterOutpost(List<MonumentRow> rows)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].key == "compound")
                return;
        }

        float y = 0f;
        try
        {
            TerrainHeightMap hm = TerrainMeta.HeightMap;
            if (hm != null)
            {
                float norm = (World.Size > 0) ? (0.5f) : 0.5f;
                y = hm.GetHeight(norm, norm);
            }
        }
        catch
        {
            y = 31f;
        }

        rows.Add(new MonumentRow
        {
            key = "compound",
            name = "assets/bundled/prefabs/autospawn/monument/medium/compound.prefab",
            label = "Outpost",
            x = 0f,
            y = y,
            z = 0f,
            yaw = 0f,
            qx = 0f,
            qy = 0f,
            qz = 0f,
            qw = 1f
        });
    }

    static string ResolveMonumentKey(MonumentInfo mi)
    {
        string root = (mi.transform?.root?.name ?? mi.transform?.name ?? mi.name ?? "").ToLowerInvariant();
        string token = GetPhraseToken(mi);

        // Match specific names before broad path folders (…/harbor/ferry… contains "harbor").
        if (root.Contains("ferry_terminal") || token.Contains("ferry"))
            return "ferry_terminal";
        if (root.Contains("harbor_2") || token.Contains("harbor_2"))
            return "harbor_2";
        if (root.Contains("harbor_1") || token.Contains("large_harbor") || (root.Contains("harbor") && !root.Contains("ferry")))
            return "harbor_1";
        if (ContainsAny(token, root, "compound", "outpost"))
            return "compound";
        if (ContainsAny(token, root, "bandit"))
            return "bandit_town";
        if (ContainsAny(token, root, "launch_site", "launchsite"))
            return "launch_site";
        if (ContainsAny(token, root, "airfield"))
            return "airfield";
        if (ContainsAny(token, root, "trainyard", "train_yard"))
            return "trainyard";
        if (ContainsAny(token, root, "powerplant", "power_plant"))
            return "powerplant";
        if (ContainsAny(token, root, "water_treatment", "watertreatment"))
            return "water_treatment";
        if (ContainsAny(token, root, "excavator"))
            return "excavator";
        if (ContainsAny(token, root, "military_tunnel", "militarytunnel"))
            return "military_tunnel";
        // Cave monuments before "sewer" → radtown_small (cave_large_sewers_hard).
        if (root.Contains("cave_large_sewers") || token.Contains("cave_large_sewers"))
            return "cave_large_sewers_hard";
        if (root.Contains("cave_large_hard") || token.Contains("cave_large_hard"))
            return "cave_large_hard";
        if (root.Contains("cave_large_medium") || token.Contains("cave_large_medium"))
            return "cave_large_medium";
        if (root.Contains("cave_medium_easy") || token.Contains("cave_medium_easy"))
            return "cave_medium_easy";
        if (root.Contains("cave_medium_hard") || token.Contains("cave_medium_hard"))
            return "cave_medium_hard";
        if (root.Contains("cave_medium_medium") || token.Contains("cave_medium_medium"))
            return "cave_medium_medium";
        if (root.Contains("cave_small_easy") || token.Contains("cave_small_easy"))
            return "cave_small_easy";
        if (root.Contains("cave_small_hard") || token.Contains("cave_small_hard"))
            return "cave_small_hard";
        if (root.Contains("cave_small_medium") || token.Contains("cave_small_medium"))
            return "cave_small_medium";
        if (root.Contains("cave_") || token.Contains("cave_"))
        {
            // Fallback: basename stem when a new cave variant appears.
            string src = root.Contains("cave_") ? root : token;
            int i = src.IndexOf("cave_");
            if (i >= 0)
            {
                string stem = src.Substring(i);
                int end = stem.IndexOf('.');
                if (end > 0) stem = stem.Substring(0, end);
                int sp = stem.IndexOf(' ');
                if (sp > 0) stem = stem.Substring(0, sp);
                if (stem.Length > 5) return stem;
            }
        }
        if (ContainsAny(token, root, "junkyard", "junk_yard"))
            return "junkyard";
        if (ContainsAny(token, root, "sphere", "dome"))
            return "sphere_tank";
        if (ContainsAny(token, root, "satellite"))
            return "satellite_dish";
        if (ContainsAny(token, root, "oilrig", "oil_rig"))
            return root.Contains("oilrig_2") || token.Contains("oilrig_2") ? "oilrig_2" : "oilrig_1";
        if (ContainsAny(token, root, "silo", "missile"))
            return "missile_silo";
        if (ContainsAny(token, root, "arctic"))
            return "arctic_base";
        if (ContainsAny(token, root, "apartment"))
            return "apartments";
        if (ContainsAny(token, root, "lighthouse"))
            return "lighthouse";
        if (root.Contains("fishing_village_c") || token.Contains("fishing_village_c"))
            return "fishing_c";
        if (root.Contains("fishing_village_b") || token.Contains("fishing_village_b"))
            return "fishing_b";
        if (ContainsAny(token, root, "fishing"))
            return "fishing_a";
        if (root.Contains("stables_b") || token.Contains("stables_b"))
            return "stables_b";
        if (ContainsAny(token, root, "stable"))
            return "stables_a";
        if (root.Contains("quarry_c") || root.Contains("mining_quarry_c") || token.Contains("quarry_c"))
            return "quarry_c";
        if (root.Contains("quarry_b") || root.Contains("mining_quarry_b") || token.Contains("quarry_b"))
            return "quarry_b";
        if (ContainsAny(token, root, "quarry"))
            return "quarry_a";
        if (ContainsAny(token, root, "radtown_small") || token.Contains("sewer branch") || root.Contains("radtown_small"))
            return "radtown_small";
        if (ContainsAny(token, root, "radtown", "rad town"))
            return "radtown";
        if (ContainsAny(token, root, "supermarket"))
            return "supermarket";
        if (ContainsAny(token, root, "gas_station", "gasstation"))
            return "gas_station";
        if (ContainsAny(token, root, "warehouse"))
            return "warehouse";
        if (ContainsAny(token, root, "ziggurat"))
            return "jungle_ziggurat";
        if (ContainsAny(token, root, "desert_military_base_d"))
            return "desert_base_d";
        if (ContainsAny(token, root, "desert_military_base_c"))
            return "desert_base_c";
        if (ContainsAny(token, root, "desert_military_base_b"))
            return "desert_base_b";
        if (ContainsAny(token, root, "desert_military_base"))
            return "desert_base_a";

        return null;
    }

    static bool ContainsAny(string token, string root, params string[] parts)
    {
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i];
            if (token.Contains(p) || root.Contains(p))
                return true;
        }
        return false;
    }

    static string GetPhraseToken(MonumentInfo mi)
    {
        try
        {
            FieldInfo fi = typeof(LandmarkInfo).GetField("displayPhrase", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object phrase = fi?.GetValue(mi);
            if (phrase == null)
                return "";
            PropertyInfo prop = phrase.GetType().GetProperty("token", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return (prop?.GetValue(phrase, null) as string ?? "").ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    static string GetDisplayLabel(MonumentInfo mi, string key)
    {
        try
        {
            FieldInfo fi = typeof(LandmarkInfo).GetField("displayPhrase", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object phrase = fi?.GetValue(mi);
            if (phrase != null)
            {
                PropertyInfo eng = phrase.GetType().GetProperty("english", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                string label = eng?.GetValue(phrase, null) as string;
                if (!string.IsNullOrWhiteSpace(label))
                    return label.Trim();
            }
        }
        catch
        {
            // fall through
        }
        return key?.Replace('_', ' ') ?? "";
    }

    object FindConvoyInstance()
    {
        if (_convoyInstanceProp != null)
            return _convoyInstanceProp.GetValue(null);

        Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < asms.Length; i++)
        {
            Type t;
            try
            {
                t = asms[i].GetType("Convoy.EventController");
            }
            catch
            {
                continue;
            }
            if (t == null)
                continue;
            PropertyInfo prop = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (prop == null)
                continue;
            _convoyInstanceProp = prop;
            _convoyVehiclesField = t.GetField("_vehicles", BindingFlags.NonPublic | BindingFlags.Instance);
            return prop.GetValue(null);
        }
        return null;
    }

    void MaybeExportMapImage()
    {
        if (_mapExported || TerrainMeta.Size.x <= 0f)
            return;

        _mapExportAttempts++;
        if (_mapExportAttempts > 900)
            return;

        string pngPath = FindMinimapOverworldPng(out int renderRes);
        if (string.IsNullOrEmpty(pngPath))
            return;

        try
        {
            byte[] png = File.ReadAllBytes(pngPath);
            if (png == null || png.Length == 0)
                return;

            string harmonyMap = _config.HarmonyMapPath;
            string panelMap = _config.PanelMapPath;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (_unloaded)
                    return;
                WriteBytesAtomic(harmonyMap, png);
                if (!string.IsNullOrEmpty(panelMap))
                    WriteBytesAtomic(panelMap, png);
            });

            string metaJson = BuildTerrainMetaJson(pngPath, renderRes);
            var metaObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(metaJson);
            if (metaObj != null)
                metaObj["mapImage"] = Path.GetFileName(panelMap ?? harmonyMap);
            metaJson = JsonConvert.SerializeObject(metaObj, Formatting.Indented);
            WriteAtomic(_config.HarmonyTerrainPath, metaJson);
            if (!string.IsNullOrEmpty(_config.PanelTerrainPath))
                WriteAtomic(_config.PanelTerrainPath, metaJson);

            _mapExported = true;
            Debug.Log("[LivemapBridge] Exported Minimap overworld → " + panelMap + " (" + renderRes + "px, oceanMargin " + ReadMinimapOceanMargin() + ")");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LivemapBridge] Map export: " + ex.Message);
        }
    }

    string FindMinimapOverworldPng(out int renderRes)
    {
        renderRes = ReadMinimapRenderResolution();
        string cacheDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "HarmonyData", "Minimap", "cache"));
        if (!Directory.Exists(cacheDir))
            return null;

        int world = (int)World.Size;
        uint seed = World.Seed;
        string pattern = "world.minimap." + world + "_" + seed + "_*.png";
        string[] files = Directory.GetFiles(cacheDir, pattern);
        if (files == null || files.Length == 0)
            return null;

        string best = null;
        int bestRes = -1;
        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file) ?? "";
            string[] parts = name.Split('_');
            if (parts.Length < 2)
                continue;
            if (!int.TryParse(parts[parts.Length - 1], out int res))
                continue;
            if (res > bestRes)
            {
                bestRes = res;
                best = file;
            }
        }

        if (best != null)
            renderRes = bestRes;
        return best;
    }

    string BuildTerrainMetaJson(string minimapPng, int renderRes)
    {
        int oceanMargin = ReadMinimapOceanMargin();
        float water01 = SampleEdgeWater01();
        var meta = new Dictionary<string, object>
        {
            ["worldSize"] = (int)TerrainMeta.Size.x,
            ["water01"] = water01,
            ["seed"] = World.Seed,
            ["mapImage"] = "map.png",
            ["mapImageSource"] = "minimap",
            ["oceanMargin"] = oceanMargin,
            ["renderResolution"] = renderRes,
            ["mapImageSize"] = new[] { renderRes, renderRes },
            ["minimapCache"] = minimapPng,
            ["live"] = true,
            // height.bin uses Facepunch .map encoding — NOT TerrainMeta.Size.y / Position.y.
            ["heightScale"] = 2000.0,
            ["terrainPosY"] = -1500.0,
            ["heightBin"] = "height.bin",
            ["resolution"] = 513
        };
        MergeHeightMeta(meta, _config.PanelTerrainPath);
        MergeHeightMeta(meta, _config.HarmonyTerrainPath);
        return JsonConvert.SerializeObject(meta, Formatting.Indented);
    }

    static void MergeHeightMeta(Dictionary<string, object> meta, string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;
        try
        {
            var existing = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(path));
            if (existing == null)
                return;
            foreach (string key in new[] {
                "heightBin", "resolution", "sourceResolution", "mapFile", "water01"
            })
            {
                if (existing.TryGetValue(key, out object val) && val != null)
                    meta[key] = val;
            }
        }
        catch
        {
        }
    }

    static float SampleEdgeWater01()
    {
        TerrainHeightMap hm = TerrainMeta.HeightMap;
        if (hm == null)
            return 0.5f;

        var samples = new List<float>(260);
        for (int i = 0; i <= 64; i++)
        {
            float u = i / 64f;
            samples.Add(hm.GetHeight01(u, 0f));
            samples.Add(hm.GetHeight01(u, 1f));
            samples.Add(hm.GetHeight01(0f, u));
            samples.Add(hm.GetHeight01(1f, u));
        }
        samples.Sort();
        return samples[samples.Count / 2];
    }

    static int ReadMinimapOceanMargin()
    {
        try
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "HarmonyConfig", "Minimap.json"));
            if (!File.Exists(path))
                return 500;
            string json = File.ReadAllText(path);
            var m = System.Text.RegularExpressions.Regex.Match(json, "\"Ocean margin[^\"]*\"\\s*:\\s*(\\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int margin))
                return margin;
        }
        catch
        {
        }
        return 500;
    }

    static int ReadMinimapRenderResolution()
    {
        try
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "HarmonyConfig", "Minimap.json"));
            if (!File.Exists(path))
                return 4096;
            string json = File.ReadAllText(path);
            var m = System.Text.RegularExpressions.Regex.Match(json, "\"Render resolution[^\"]*\"\\s*:\\s*(\\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int res))
                return res;
        }
        catch
        {
        }
        return 4096;
    }

    void WriteBytesAtomic(string path, byte[] data)
    {
        if (string.IsNullOrEmpty(path) || data == null || data.Length == 0)
            return;
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        if (File.Exists(path))
            File.Replace(tmp, path, null);
        else
            File.Move(tmp, path);
    }

    struct PrefabKind
    {
        public int k;
        public float h;
        public bool t;
    }

    public class ConfigFile
    {
        public float IntervalSeconds = 1f;
        public float BuildingsIntervalSeconds = 30f;
        public float VehicleScanIntervalSeconds = 5f;
        public string HarmonyOutputPath = @"C:\svr1\HarmonyData\Livemap\snapshot.json";
        public string PanelOutputPath = @"C:\!WEB RCON PANEL\livemap\data\snapshot.json";
        public string HarmonyBuildingsPath = @"C:\svr1\HarmonyData\Livemap\buildings.json";
        public string PanelBuildingsPath = @"C:\!WEB RCON PANEL\livemap\data\buildings.json";
        public string HarmonyMonumentsPath = @"C:\svr1\HarmonyData\Livemap\monuments.json";
        public string PanelMonumentsPath = @"C:\!WEB RCON PANEL\livemap\data\monuments.json";
        public string HarmonyMapPath = @"C:\svr1\HarmonyData\Livemap\map.png";
        public string PanelMapPath = @"C:\!WEB RCON PANEL\livemap\data\map.png";
        public string HarmonyTerrainPath = @"C:\svr1\HarmonyData\Livemap\terrain.json";
        public string PanelTerrainPath = @"C:\!WEB RCON PANEL\livemap\data\terrain.json";
    }

    class Snapshot
    {
        public bool live;
        public uint worldSize;
        public List<PlayerRow> players;
        public List<VehicleRow> vehicles;
    }

    class PlayerRow
    {
        public string id;
        public string name;
        public float x;
        public float y;
        public float z;
        public float yaw;
    }

    class VehicleRow
    {
        public string id;
        public string type;
        public float x;
        public float y;
        public float z;
        public float yaw;
    }

    class MonumentSnap
    {
        public bool live;
        public string source;
        public uint worldSize;
        public int count;
        public List<MonumentRow> monuments;
    }

    class MonumentRow
    {
        public string key;
        public string name;
        public string label;
        public float x;
        public float y;
        public float z;
        public float yaw;
        public float qx;
        public float qy;
        public float qz;
        public float qw;
        public List<MonumentAttachment> attachments;
        public ExcavatorRotorRow excavatorRotor;
    }

    class ExcavatorRotorRow
    {
        public float x;
        public float y;
        public float z;
        public float expectedX;
        public float expectedY;
        public float expectedZ;
        public float yaw1;
        public float yaw2;
        public float turnSpeed;
        public float movedAmount;
        public bool mining;
        public bool matched;
    }

    class MonumentAttachment
    {
        public string name;
        public float x;
        public float y;
        public float z;
        public float yaw;
    }
}
