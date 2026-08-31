using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using UnityEngine;
using Building = BuildingManager.Building;
using Debug = UnityEngine.Debug;
using LivemapBridge.MapCreation;

namespace LivemapBridge;

public class LivemapBridgeMod : IHarmonyModHooks
{
    public static LivemapBridgeMod Instance { get; private set; }

    const string CmdName = "livemap.snapshot";
    const string CmdFull = "global.livemap.snapshot";
    const string CmdRenderName = "livemap.render";
    const string CmdRenderFull = "global.livemap.render";

    ConsoleSystem.Command _cmd;
    ConsoleSystem.Command _cmdRender;
    ConfigFile _config;
    string _configPath;
    bool _wrotePanelWarning;
    bool _ingestSnapshotBusy;
    float _lastIngestWarn;
    bool _loopStarted;
    bool _unloaded;
    bool _monumentsDumped;
    bool _mapExported;
    int _mapExportAttempts;
    float _nextBuildAt = -999f;
    float _lastSlowTickLog;
    FieldInfo _convoyVehiclesField;
    FieldInfo _convoyEntityField;
    PropertyInfo _convoyInstanceProp;
    MethodInfo _convoyIsActiveMethod;
    MethodInfo _isConvoyEntityMethod;
    bool _convoyTypesResolved;
    bool _convoyEntityMethodResolved;
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
    static readonly Dictionary<string, PrefabKind> PrefabKindCache = new Dictionary<string, PrefabKind>(128);

    const int MaxBuildingBlocks = 15000;
    const int BuildingSliceMs = 2;
    const float BuildingPumpInterval = 0.05f;
    readonly BlockSnap[] _blockBuf = new BlockSnap[MaxBuildingBlocks];
    int _blockCount;
    int _buildBuildingIndex;
    int _buildPrivIndex;
    int _buildBlockIndex;
    bool _buildCollecting;
    bool _buildingsWritePending;
    bool _buildingsPumpStarted;

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;
        _unloaded = false;
        LoadConfig();
        RegisterCommand();
        StartLoop();
        Debug.Log("[LivemapBridge] Loaded. Writes " + _config.PanelOutputPath +
                  (string.IsNullOrEmpty(_config.IngestUrl) ? "" : " ingest " + _config.IngestUrl));
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

            NormalizeInstancePaths(root);
            File.WriteAllText(_configPath, JsonConvert.SerializeObject(_config, Formatting.Indented));
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

    void NormalizeInstancePaths(string root)
    {
        string live = Path.Combine(root, "HarmonyData", "Livemap");
        Directory.CreateDirectory(live);
        _config.HarmonyOutputPath = Path.Combine(live, "snapshot.json");
        _config.HarmonyBuildingsPath = Path.Combine(live, "buildings.json");
        _config.HarmonyMonumentsPath = Path.Combine(live, "monuments.json");
        _config.HarmonyMapPath = Path.Combine(live, "map.png");
        _config.HarmonyTerrainPath = Path.Combine(live, "terrain.json");
        _config.HarmonyHeightPath = Path.Combine(live, "height.bin");

        string panelId = GuessLocalPanelServerId(root);
        if (!string.IsNullOrEmpty(panelId))
        {
            if (!string.IsNullOrEmpty(_config.PanelServerId) &&
                !string.Equals(_config.PanelServerId, panelId, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("[LivemapBridge] PanelServerId was for another instance (" +
                                 _config.PanelServerId + "); this root is " + panelId + ". Clearing copied ingest token.");
                _config.IngestToken = "";
            }
            _config.PanelServerId = panelId;
            ApplyPanelDir(Path.Combine(@"C:\!WEB RCON PANEL", "db", "orgs", "grimmzone", "servers", panelId, "livemap"));
            return;
        }

        if (IsSharedLegacyPanelPath(_config.PanelOutputPath) || IsForeignGameServerPath(_config.PanelOutputPath, root))
        {
            Debug.LogError("[LivemapBridge] Refusing panel path " + _config.PanelOutputPath +
                           " (shared livemap/data or another game server). Set PanelServerId or IngestToken.");
            ClearPanelPaths();
        }
    }

    void ApplyPanelDir(string panelDir)
    {
        Directory.CreateDirectory(panelDir);
        _config.PanelOutputPath = Path.Combine(panelDir, "snapshot.json");
        _config.PanelBuildingsPath = Path.Combine(panelDir, "buildings.json");
        _config.PanelMonumentsPath = Path.Combine(panelDir, "monuments.json");
        _config.PanelMapPath = Path.Combine(panelDir, "map.png");
        _config.PanelTerrainPath = Path.Combine(panelDir, "terrain.json");
        _config.PanelHeightPath = Path.Combine(panelDir, "height.bin");
    }

    void ClearPanelPaths()
    {
        _config.PanelOutputPath = "";
        _config.PanelBuildingsPath = "";
        _config.PanelMonumentsPath = "";
        _config.PanelMapPath = "";
        _config.PanelTerrainPath = "";
        _config.PanelHeightPath = "";
    }

    static string GuessLocalPanelServerId(string root)
    {
        string n = (root ?? "").TrimEnd('\\', '/');
        if (string.Equals(n, @"C:\svr1", StringComparison.OrdinalIgnoreCase))
            return "4982e71e3c3765f36f244e5e4af874ba";
        if (string.Equals(n, @"C:\StagingSvr", StringComparison.OrdinalIgnoreCase))
            return "9eb0aa00c1a145d3b249e8ba6cc86e47";
        if (string.Equals(n, @"C:\!2XRUST", StringComparison.OrdinalIgnoreCase))
            return "409debab5ecc19a841830ef0a14b93db";
        return "";
    }

    static bool IsSharedLegacyPanelPath(string panelPath)
    {
        if (string.IsNullOrEmpty(panelPath))
            return false;
        string n = panelPath.Replace('/', '\\');
        return n.IndexOf(@"livemap\data\", StringComparison.OrdinalIgnoreCase) >= 0 ||
               n.EndsWith(@"livemap\data", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsForeignGameServerPath(string panelPath, string root)
    {
        if (string.IsNullOrEmpty(panelPath))
            return false;
        try
        {
            string full = Path.GetFullPath(panelPath);
            string[] others = { @"C:\svr1", @"C:\StagingSvr", @"C:\!2XRUST" };
            string thisRoot = Path.GetFullPath(root).TrimEnd('\\', '/');
            foreach (string other in others)
            {
                string o = Path.GetFullPath(other).TrimEnd('\\', '/');
                if (string.Equals(o, thisRoot, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (full.StartsWith(o + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
        }
        return false;
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
                InvokeHandler.CancelInvoke(handler, PumpBuildings);
            }
            catch
            {
            }
            InvokeHandler.InvokeRepeating(handler, Tick, 2f, _config.IntervalSeconds);
            _loopStarted = true;
            _buildingsPumpStarted = false;
            _buildCollecting = false;
            _nextBuildAt = Time.realtimeSinceStartup + _config.BuildingsIntervalSeconds;
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
        _buildCollecting = false;
        _buildingsPumpStarted = false;
        _buildingsWritePending = false;
        try
        {
            InvokeHandler.CancelInvoke(handler, Tick);
            InvokeHandler.CancelInvoke(handler, PumpBuildings);
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

            _cmdRender = new ConsoleSystem.Command
            {
                Name = CmdRenderName,
                FullName = CmdRenderFull,
                Variable = false,
                ServerAdmin = true,
                Call = CmdRender
            };
            ConsoleSystem.Index.Server.Dict[CmdRenderFull] = _cmdRender;
            if (ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict[CmdRenderName] = _cmdRender;
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
            ConsoleSystem.Index.Server.Dict?.Remove(CmdRenderFull);
            ConsoleSystem.Index.Server.GlobalDict?.Remove(CmdName);
            ConsoleSystem.Index.Server.GlobalDict?.Remove(CmdRenderName);
            _cmd = null;
            _cmdRender = null;
        }
        catch
        {
        }
    }

    void CmdSnapshot(ConsoleSystem.Arg arg)
    {
        arg?.ReplyWith(BuildJson());
    }

    void CmdRender(ConsoleSystem.Arg arg)
    {
        _mapExported = false;
        _mapExportAttempts = 0;
        MaybeExportMapImage();
        arg?.ReplyWith(_mapExported ? "livemap.render ok" : "livemap.render waiting for terrain");
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
        QueueIngestJson("snapshot", json);

        MaybeStartBuildingDump();

        float now = Time.realtimeSinceStartup;
        if (sw.ElapsedMilliseconds >= 80 && now - _lastSlowTickLog >= 10f)
        {
            _lastSlowTickLog = now;
            Debug.LogWarning("[LivemapBridge] Tick took " + sw.ElapsedMilliseconds + "ms (bradleys=" + LivemapBradleyTracker.Count + ")");
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

    void MaybeStartBuildingDump()
    {
        if (_unloaded || _buildCollecting || _buildingsWritePending)
            return;
        if (Time.realtimeSinceStartup < _nextBuildAt)
            return;

        _buildCollecting = true;
        _blockCount = 0;
        _buildBuildingIndex = 0;
        _buildPrivIndex = 0;
        _buildBlockIndex = 0;
        _nextBuildAt = Time.realtimeSinceStartup + _config.BuildingsIntervalSeconds;

        if (_buildingsPumpStarted)
            return;
        var handler = SingletonComponent<InvokeHandler>.Instance;
        if (handler == null)
            return;
        InvokeHandler.InvokeRepeating(handler, PumpBuildings, 0f, BuildingPumpInterval);
        _buildingsPumpStarted = true;
    }

    void PumpBuildings()
    {
        if (_unloaded)
            return;
        if (!_buildCollecting)
            return;
        CollectBuildingSlice();
        if (_buildCollecting)
            return;
        QueueBuildingsWrite();
        StopBuildingPump();
    }

    void StopBuildingPump()
    {
        if (!_buildingsPumpStarted)
            return;
        _buildingsPumpStarted = false;
        var handler = SingletonComponent<InvokeHandler>.Instance;
        if (handler == null)
            return;
        try
        {
            InvokeHandler.CancelInvoke(handler, PumpBuildings);
        }
        catch
        {
        }
    }

    void CollectBuildingSlice()
    {
        var dict = BuildingManager.server?.buildingDictionary;
        if (dict == null)
        {
            _buildCollecting = false;
            return;
        }

        var buildings = dict.Values;
        var sw = Stopwatch.StartNew();
        while (_buildBuildingIndex < buildings.Count && _blockCount < MaxBuildingBlocks)
        {
            if (sw.ElapsedMilliseconds >= BuildingSliceMs)
                return;

            Building building = buildings[_buildBuildingIndex];
            if (building == null)
            {
                _buildBuildingIndex++;
                _buildPrivIndex = 0;
                _buildBlockIndex = 0;
                continue;
            }

            int cups = building.buildingPrivileges.Count;
            while (_buildPrivIndex < cups && _blockCount < MaxBuildingBlocks)
            {
                if (sw.ElapsedMilliseconds >= BuildingSliceMs)
                    return;
                TryCopyBlock(building.buildingPrivileges[_buildPrivIndex], 4, 0, 1f, false);
                _buildPrivIndex++;
            }

            int blocks = building.buildingBlocks.Count;
            while (_buildBlockIndex < blocks && _blockCount < MaxBuildingBlocks)
            {
                if (sw.ElapsedMilliseconds >= BuildingSliceMs)
                    return;
                BuildingBlock block = building.buildingBlocks[_buildBlockIndex];
                _buildBlockIndex++;
                if (block == null || block.IsDestroyed || block.transform == null)
                    continue;
                int kind = KindFromPrefabCached(block.ShortPrefabName, out float hScale, out bool triangle);
                if (kind < 0)
                    continue;
                int grade = (int)block.grade;
                if (grade < 0) grade = 0;
                if (grade > 4) grade = 4;
                TryCopyBlock(block, kind, grade, hScale, triangle);
            }

            _buildBuildingIndex++;
            _buildPrivIndex = 0;
            _buildBlockIndex = 0;
        }

        _buildCollecting = false;
    }

    void TryCopyBlock(BaseEntity ent, int kind, int grade, float hScale, bool triangle)
    {
        if (ent == null || ent.IsDestroyed || ent.transform == null)
            return;
        Vector3 pos = ent.transform.position;
        if (pos.y < -5f)
            return;
        if (_blockCount >= MaxBuildingBlocks)
            return;
        _blockBuf[_blockCount++] = new BlockSnap
        {
            x = pos.x,
            y = pos.y,
            z = pos.z,
            yaw = ent.transform.eulerAngles.y,
            h = hScale,
            k = (byte)kind,
            g = (byte)grade,
            t = triangle ? (byte)1 : (byte)0
        };
    }

    void QueueBuildingsWrite()
    {
        int n = _blockCount;
        var copy = new BlockSnap[n];
        if (n > 0)
            Array.Copy(_blockBuf, copy, n);
        _buildingsWritePending = true;
        string harmonyPath = _config.HarmonyBuildingsPath;
        string panelPath = _config.PanelBuildingsPath;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (_unloaded)
                    return;
                string json = FormatBlocksJson(copy);
                WriteAtomic(harmonyPath, json);
                WriteAtomic(panelPath, json);
                QueueIngestJson("buildings", json);
            }
            finally
            {
                _buildingsWritePending = false;
            }
        });
    }

    static string FormatBlocksJson(BlockSnap[] blocks)
    {
        var sb = new StringBuilder(blocks.Length * 72 + 32);
        sb.Append("{\"live\":true,\"blocks\":[");
        for (int i = 0; i < blocks.Length; i++)
        {
            if (i > 0)
                sb.Append(',');
            BlockSnap b = blocks[i];
            sb.Append("{\"x\":");
            AppendFloat(sb, b.x);
            sb.Append(",\"y\":");
            AppendFloat(sb, b.y);
            sb.Append(",\"z\":");
            AppendFloat(sb, b.z);
            sb.Append(",\"yaw\":");
            AppendFloat(sb, b.yaw);
            sb.Append(",\"k\":");
            sb.Append(b.k);
            sb.Append(",\"g\":");
            sb.Append(b.g);
            sb.Append(",\"h\":");
            AppendFloat(sb, b.h);
            sb.Append(",\"t\":");
            sb.Append(b.t);
            sb.Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    static void AppendFloat(StringBuilder sb, float value)
    {
        sb.Append(value.ToString("G9", CultureInfo.InvariantCulture));
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
        if (!_convoyEntityMethodResolved)
        {
            _convoyEntityMethodResolved = true;
            _isConvoyEntityMethod = FindTypeMethod("Convoy.ConvoyState", "IsConvoyEntity", BindingFlags.Public | BindingFlags.Static);
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
        if (!_convoyTypesResolved)
            ResolveConvoyTypes();
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

    void ResolveConvoyTypes()
    {
        _convoyTypesResolved = true;
        _convoyIsActiveMethod = FindTypeMethod("Convoy.EventLauncher", "IsEventActive", BindingFlags.Public | BindingFlags.Static);
        Type controller = FindType("Convoy.EventController");
        if (controller == null)
            return;
        _convoyInstanceProp = controller.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        _convoyVehiclesField = controller.GetField("_vehicles", BindingFlags.NonPublic | BindingFlags.Instance);
    }

    static Type FindType(string fullName)
    {
        Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < asms.Length; i++)
        {
            Type t;
            try
            {
                t = asms[i].GetType(fullName);
            }
            catch
            {
                continue;
            }
            if (t != null)
                return t;
        }
        return null;
    }

    static MethodInfo FindTypeMethod(string typeName, string methodName, BindingFlags flags)
    {
        Type t = FindType(typeName);
        return t?.GetMethod(methodName, flags);
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
        QueueIngestJson("monuments", json);

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
        if (!_convoyTypesResolved)
            ResolveConvoyTypes();
        if (_convoyInstanceProp == null)
            return null;
        return _convoyInstanceProp.GetValue(null);
    }

    void MaybeExportMapImage()
    {
        if (_mapExported || !OverworldRenderer.TerrainReady())
            return;

        _mapExportAttempts++;
        if (_mapExportAttempts > 900)
            return;

        string source = "livemap";
        int renderRes = OverworldRenderer.ClampResolution(_config.MapRenderResolution);
        string cachePath = "";
        byte[] png = null;

        if (_config.PreferMinimapCache)
        {
            cachePath = FindMinimapOverworldPng(out int cacheRes);
            if (!string.IsNullOrEmpty(cachePath))
            {
                try
                {
                    png = File.ReadAllBytes(cachePath);
                    if (png != null && png.Length > 0)
                    {
                        source = "minimap";
                        renderRes = cacheRes;
                    }
                    else
                        png = null;
                }
                catch
                {
                    png = null;
                }
            }
        }

        if (png == null)
        {
            try
            {
                png = OverworldRenderer.RenderPng(_config.MapRenderResolution, out renderRes);
                source = "livemap";
                cachePath = "";
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LivemapBridge] Map render: " + ex.Message);
                return;
            }
        }

        if (png == null || png.Length == 0)
            return;

        byte[] height = null;
        try
        {
            height = OverworldRenderer.RenderHeightBin(513);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LivemapBridge] Height dump: " + ex.Message);
        }

        try
        {
            string harmonyMap = _config.HarmonyMapPath;
            string panelMap = _config.PanelMapPath;
            byte[] pngCopy = png;
            byte[] heightCopy = height;
            string harmonyHeight = _config.HarmonyHeightPath;
            string panelHeight = _config.PanelHeightPath;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (_unloaded)
                    return;
                WriteBytesAtomic(harmonyMap, pngCopy);
                if (!string.IsNullOrEmpty(panelMap))
                    WriteBytesAtomic(panelMap, pngCopy);
                QueueIngestBytes("map", pngCopy, "image/png");
                if (heightCopy != null && heightCopy.Length > 0)
                {
                    WriteBytesAtomic(harmonyHeight, heightCopy);
                    if (!string.IsNullOrEmpty(panelHeight))
                        WriteBytesAtomic(panelHeight, heightCopy);
                    QueueIngestBytes("height", heightCopy, "application/octet-stream");
                }
            });

            string metaJson = BuildTerrainMetaJson(source, cachePath, renderRes, height != null);
            var metaObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(metaJson);
            if (metaObj != null)
                metaObj["mapImage"] = Path.GetFileName(panelMap ?? harmonyMap);
            metaJson = JsonConvert.SerializeObject(metaObj, Formatting.Indented);
            WriteAtomic(_config.HarmonyTerrainPath, metaJson);
            if (!string.IsNullOrEmpty(_config.PanelTerrainPath))
                WriteAtomic(_config.PanelTerrainPath, metaJson);
            QueueIngestJson("terrain", metaJson);

            _mapExported = true;
            Debug.Log("[LivemapBridge] Exported " + source + " overworld → " + (panelMap ?? harmonyMap) +
                      " (" + renderRes + "px, oceanMargin " + OverworldRenderer.OceanMargin + ")");
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

    string BuildTerrainMetaJson(string source, string minimapPng, int renderRes, bool wroteHeight)
    {
        int oceanMargin = source == "minimap" ? ReadMinimapOceanMargin() : OverworldRenderer.OceanMargin;
        float water01 = SampleEdgeWater01();
        var meta = new Dictionary<string, object>
        {
            ["worldSize"] = (int)TerrainMeta.Size.x,
            ["water01"] = water01,
            ["seed"] = World.Seed,
            ["mapImage"] = "map.png",
            ["mapImageSource"] = source,
            ["oceanMargin"] = oceanMargin,
            ["renderResolution"] = renderRes,
            ["mapImageSize"] = new[] { renderRes, renderRes },
            ["minimapCache"] = minimapPng ?? "",
            ["live"] = true,
            ["heightScale"] = TerrainMeta.Size.y,
            ["terrainPosY"] = TerrainMeta.Position.y,
            ["heightBin"] = "height.bin",
            ["resolution"] = 513
        };
        if (!wroteHeight)
            MergeHeightMeta(meta, _config.PanelTerrainPath);
        if (!wroteHeight)
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
            if (existing.TryGetValue("seed", out object seedObj) && seedObj != null)
            {
                uint existingSeed = Convert.ToUInt32(seedObj);
                if (existingSeed != 0 && existingSeed != World.Seed)
                    return;
            }
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

    void QueueIngestJson(string kind, string json)
    {
        if (string.IsNullOrEmpty(json))
            return;
        QueueIngestBytes(kind, Encoding.UTF8.GetBytes(json), "application/json");
    }

    void QueueIngestBytes(string kind, byte[] body, string contentType)
    {
        if (_unloaded || body == null || body.Length == 0)
            return;
        if (string.IsNullOrEmpty(_config.IngestUrl) || string.IsNullOrEmpty(_config.IngestToken))
            return;
        if (kind == "snapshot" && _ingestSnapshotBusy)
            return;
        if (kind == "snapshot")
            _ingestSnapshotBusy = true;
        string url = _config.IngestUrl.TrimEnd('/') + "/" + _config.IngestToken + "/" + kind;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (_unloaded)
                    return;
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = contentType;
                req.Timeout = kind == "snapshot" ? 8000 : 60000;
                req.ReadWriteTimeout = req.Timeout;
                req.ContentLength = body.Length;
                using (var stream = req.GetRequestStream())
                    stream.Write(body, 0, body.Length);
                using (var resp = (HttpWebResponse)req.GetResponse())
                    resp.Close();
            }
            catch (Exception ex)
            {
                if (Time.realtimeSinceStartup - _lastIngestWarn > 30f)
                {
                    _lastIngestWarn = Time.realtimeSinceStartup;
                    Debug.LogWarning("[LivemapBridge] Ingest " + kind + ": " + ex.Message);
                }
            }
            finally
            {
                if (kind == "snapshot")
                    _ingestSnapshotBusy = false;
            }
        });
    }

    struct BlockSnap
    {
        public float x;
        public float y;
        public float z;
        public float yaw;
        public float h;
        public byte k;
        public byte g;
        public byte t;
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
        public string PanelServerId = "";
        public string HarmonyOutputPath = "";
        public string PanelOutputPath = "";
        public string HarmonyBuildingsPath = "";
        public string PanelBuildingsPath = "";
        public string HarmonyMonumentsPath = "";
        public string PanelMonumentsPath = "";
        public string HarmonyMapPath = "";
        public string PanelMapPath = "";
        public string HarmonyTerrainPath = "";
        public string PanelTerrainPath = "";
        public string HarmonyHeightPath = "";
        public string PanelHeightPath = "";
        public int MapRenderResolution = 2048;
        public bool PreferMinimapCache = true;
        public string IngestUrl = "";
        public string IngestToken = "";
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
