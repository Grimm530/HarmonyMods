using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Facepunch.Rust.Profiling;
using HarmonyLib;
using Network;
using Newtonsoft.Json;
using HarmonyMetrics.Config;
using HarmonyMetrics.HarmonyPatches.Utility;
using UnityEngine;

namespace HarmonyMetrics;

public class MetricsLogger : SingletonComponent<MetricsLogger>
{
    private readonly StringBuilder _stringBuilder = new StringBuilder();
    private readonly Dictionary<ulong, Action> _playerStatsActions = new Dictionary<ulong, Action>();
    private readonly Dictionary<ulong, uint> _perfReportDelayCounter = new Dictionary<ulong, uint>();
    private readonly HashSet<string> _knownHarmonyMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();
    private bool _harmonySnapshotPrimed;
    private bool _delayedPatchesApplied;
    private bool _delayedPatchesRunning;
    private Coroutine _delayedPatchCoroutine;

    private class NetworkUpdateData
    {
        public int Count;
        public long Bytes;

        public NetworkUpdateData(int count, long bytes)
        {
            Count = count;
            Bytes = bytes;
        }
    }

    private readonly Dictionary<Message.Type, NetworkUpdateData> _networkUpdates = CreateNetworkUpdates();
    private static readonly Dictionary<Message.Type, string> MessageTypeNames = CreateMessageTypeNames();

    public readonly MetricsTimeStorage<System.Reflection.MethodInfo> ServerInvokes = new MetricsTimeStorage<System.Reflection.MethodInfo>("invoke_execution", LogMethodInfo);
    public readonly MetricsTimeStorage<string> ServerRpcCalls = new MetricsTimeStorage<string>("rpc_calls", LogMethodName);
    public readonly MetricsTimeStorage<string> WorkQueueTimes = new MetricsTimeStorage<string>("work_queue", LogMethodName);
    public readonly MetricsTimeStorage<string> ServerUpdate = new MetricsTimeStorage<string>("server_update", LogMethodName);
    public readonly MetricsTimeStorage<string> TimeWarnings = new MetricsTimeStorage<string>("timewarnings", LogMethodName);
    public readonly MetricsTimeStorage<string> ServerConsoleCommands = new MetricsTimeStorage<string>("console_commands", LogConsoleCommand);

    public static bool IsReady;
    public bool Ready { get { return IsReady; } private set { IsReady = value; } }
    internal ConfigData Configuration { get; private set; }

    private Uri _baseUri;
    private readonly int _performanceReportRequestId = UnityEngine.Random.Range(1, int.MaxValue);
    private ReportUploader _reportUploader;
    private Message.Type _lastMessageType;
    private bool _firstReportGenerated;
    private int _lastFrameID;
    private System.Diagnostics.Process _currentProcess;

    private double _profServerMgrUpdate;
    private double _profNetCycle;
    private double _profPhysicsSync;
    private double _profCompanionTick;
    private double _profBasePlayerCycle;
    private int _profSampleCount;

    public Uri BaseUri
    {
        get
        {
            if (_baseUri != null)
            {
                return _baseUri;
            }

            _baseUri = new Uri(new Uri(Configuration.DatabaseUrl),
                "/write?db=" + Configuration.DatabaseName + "&precision=ms&u=" + Configuration.DatabaseUser + "&p=" + Configuration.DatabasePassword);
            return _baseUri;
        }
    }

    internal static void Initialize()
    {
        DestroyOrphanedLoggers();
        if (Instance != null)
        {
            return;
        }

        var go = new GameObject("HarmonyMetrics");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<MetricsLogger>();
    }

    private static void DestroyOrphanedLoggers()
    {
        var objects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (var i = 0; i < objects.Length; i++)
        {
            var go = objects[i];
            if (go == null || go.name != "HarmonyMetrics")
            {
                continue;
            }

            // DontDestroyOnLoad objects often report an invalid scene — still destroy orphans.
            if (Instance != null && go == Instance.gameObject)
            {
                continue;
            }

            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    internal static string ConfigurationPath
    {
        get
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(root, "HarmonyConfig", "HarmonyMetrics.json");
        }
    }

    internal void OnServerStarted()
    {
        if (_delayedPatchesApplied || _delayedPatchesRunning)
        {
            return;
        }

        HarmonyMetricsLoader.ServerStarted = true;
        HarmonyMetricsLoader.DelayedHarmony ??= new Harmony("HarmonyMetrics.Delayed");
        HarmonyMetricsLoader.DelayedHarmony.UnpatchAll("HarmonyMetrics.Delayed");

        Debug.Log("[HarmonyMetrics]: Scheduling startup patches across frames (RPC/work-queue timing off unless enabled in config)");
        _delayedPatchesRunning = true;
        _delayedPatchCoroutine = StartCoroutine(ApplyDelayedPatchesRoutine());
    }

    private IEnumerator ApplyDelayedPatchesRoutine()
    {
        // Let the harmony.load console command finish and the current frame render first.
        yield return null;
        yield return null;

        try
        {
            yield return DelayedPatchApplicator.Apply(this, HarmonyMetricsLoader.DelayedHarmony, Configuration);
            _delayedPatchesApplied = true;
        }
        finally
        {
            _delayedPatchesRunning = false;
            _delayedPatchCoroutine = null;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        if (Instance == null)
        {
            Instance = this;
        }

        _reportUploader = gameObject.AddComponent<ReportUploader>();
        RegisterCommands();

        LoadConfiguration();
        if (!ValidateConfiguration())
        {
            return;
        }

        if (!Configuration.Enabled)
        {
            Debug.LogWarning("[HarmonyMetrics]: Metrics gathering has been disabled in the configuration");
            return;
        }

        StartLoggingMetrics();
        Ready = true;
    }

    public void StartLoggingMetrics()
    {
        InvokeRepeating(LogNetworkUpdates, UnityEngine.Random.Range(0.25f, 0.75f), 0.5f);
        InvokeRepeating(ServerInvokes.SerializeToStringBuilder, UnityEngine.Random.Range(0f, 1f), 1f);
        InvokeRepeating(ServerRpcCalls.SerializeToStringBuilder, UnityEngine.Random.Range(0f, 1f), 1f);
        InvokeRepeating(ServerConsoleCommands.SerializeToStringBuilder, UnityEngine.Random.Range(0f, 1f), 1f);
        InvokeRepeating(WorkQueueTimes.SerializeToStringBuilder, UnityEngine.Random.Range(0f, 1f), 1f);
        InvokeRepeating(ServerUpdate.SerializeToStringBuilder, UnityEngine.Random.Range(0f, 1f), 1f);
        InvokeRepeating(TimeWarnings.SerializeToStringBuilder, UnityEngine.Random.Range(0f, 1f), 1f);
        InvokeRepeating(LogHarmonyMods, UnityEngine.Random.Range(2f, 4f), 30f);
    }

    internal void OnPlayerInit(BasePlayer player)
    {
        if (!Ready || Configuration == null || !Configuration.GatherPlayerMetrics || player == null)
        {
            return;
        }

        var userId = player.userID.Get();
        Action action = () => GatherPlayerSecondStats(player);
        Action existingAction;
        if (_playerStatsActions.TryGetValue(userId, out existingAction))
        {
            player.CancelInvoke(existingAction);
        }

        _playerStatsActions[userId] = action;
        player.InvokeRepeating(action, UnityEngine.Random.Range(0.5f, 1.5f), 1f);
    }

    internal void OnPlayerDisconnected(BasePlayer player)
    {
        if (!Ready || player == null)
        {
            return;
        }

        var userId = player.userID.Get();
        Action action;
        if (_playerStatsActions.TryGetValue(userId, out action))
        {
            player.CancelInvoke(action);
        }

        _playerStatsActions.Remove(userId);
        _perfReportDelayCounter.Remove(userId);
    }

    internal void OnNetWritePacketID(Message.Type messageType)
    {
        if (!Ready)
        {
            return;
        }

        _lastMessageType = messageType;
    }

    internal void OnNetWriteSend(NetWrite write, SendInfo sendInfo)
    {
        if (!Ready)
        {
            return;
        }

        NetworkUpdateData data;
        if (!_networkUpdates.TryGetValue(_lastMessageType, out data))
        {
            return;
        }

        if (sendInfo.connection != null)
        {
            data.Count++;
            data.Bytes += write.Length;
        }
        else if (sendInfo.connections != null)
        {
            var count = sendInfo.connections.Count;
            data.Count += count;
            data.Bytes += write.Length * count;
        }
    }

    internal void OnHarmonyModLoaded(string dllName)
    {
        if (!Ready)
        {
            return;
        }

        UploadPacket("harmony_mod_event", StripDll(dllName), (builder, name) =>
        {
            builder.Append(",mod=\"");
            AppendSanitized(builder, name);
            builder.Append("\",event=\"load\" value=1i");
        });
    }

    internal void OnHarmonyModUnloaded(string name)
    {
        if (!Ready)
        {
            return;
        }

        UploadPacket("harmony_mod_event", StripDll(name), (builder, modName) =>
        {
            builder.Append(",mod=\"");
            AppendSanitized(builder, modName);
            builder.Append("\",event=\"unload\" value=1i");
        });
    }

    public static bool TryHandleClientPerformanceReport(ProtoBuf.PerformanceReport clientPerformanceReport)
    {
        try
        {
            if (clientPerformanceReport == null || !IsReady)
            {
                return false;
            }

            var logger = Instance;
            if (logger == null)
            {
                return false;
            }

            return logger.OnClientPerformanceReport(clientPerformanceReport);
        }
        catch (Exception ex)
        {
            Debug.LogError("[HarmonyMetrics] Client performance report handler failed: " + ex.Message);
            return false;
        }
    }

    public bool OnClientPerformanceReport(ProtoBuf.PerformanceReport clientPerformanceReport)
    {
        if (clientPerformanceReport == null || clientPerformanceReport.request_id != _performanceReportRequestId)
        {
            return false;
        }

        UploadPacket("client_performance", clientPerformanceReport, (builder, report) =>
        {
            builder.Append(",steamid=");
            builder.Append(report.user_id);
            builder.Append(" memory=");
            builder.Append(report.memory_system);
            builder.Append("i,fps=");
            builder.Append(report.fps);
        });

        return true;
    }

    internal void AccumulateRuntimeProfiler()
    {
        if (!Ready)
        {
            return;
        }

        _profServerMgrUpdate += RuntimeProfiler.ServerMgr_Update.TotalMilliseconds;
        _profNetCycle += RuntimeProfiler.Net_Cycle.TotalMilliseconds;
        _profPhysicsSync += RuntimeProfiler.Physics_SyncTransforms.TotalMilliseconds;
        _profCompanionTick += RuntimeProfiler.Companion_Tick.TotalMilliseconds;
        _profBasePlayerCycle += RuntimeProfiler.BasePlayer_ServerCycle.TotalMilliseconds;
        _profSampleCount++;
    }

    private void GatherPlayerSecondStats(BasePlayer player)
    {
        if (player == null || player.net == null || player.net.connection == null)
        {
            return;
        }

        if (!player.IsReceivingSnapshot)
        {
            var userId = player.userID.Get();
            uint perfReportCounter;
            _perfReportDelayCounter.TryGetValue(userId, out perfReportCounter);
            if (perfReportCounter < 4)
            {
                _perfReportDelayCounter[userId] = perfReportCounter + 1;
            }
            else
            {
                _perfReportDelayCounter[userId] = 0;
                player.ClientRPC(RpcTarget.Player("GetPerformanceReport", player), "none", _performanceReportRequestId);
            }
        }

        UploadPacket("connection_latency", player, (builder, basePlayer) =>
        {
            var ip = basePlayer.net.connection.ipaddress;
            var colon = ip.LastIndexOf(':');

            builder.Append(",steamid=");
            builder.Append(basePlayer.UserIDString);
            builder.Append(",ip=");
            builder.Append(colon > 0 ? ip.Substring(0, colon) : ip);
            builder.Append(" ping=");
            builder.Append(Net.sv.GetAveragePing(basePlayer.net.connection));
            builder.Append("i,packet_loss=");
            builder.Append(Net.sv.GetStat(basePlayer.net.connection, BaseNetwork.StatTypeLong.PacketLossLastSecond));
            builder.Append("i ");
        });
    }

    private void LogNetworkUpdates()
    {
        if (_networkUpdates.Count < 1 || Configuration == null)
        {
            return;
        }

        var serverTag = Configuration.ServerTag;
        var epochNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _stringBuilder.Clear();
        _stringBuilder.Append("network_updates,server=");
        _stringBuilder.Append(serverTag);
        _stringBuilder.Append(" ");

        var first = true;
        foreach (var networkUpdate in _networkUpdates)
        {
            string key;
            if (!MessageTypeNames.TryGetValue(networkUpdate.Key, out key))
            {
                key = networkUpdate.Key.ToString();
            }

            var value = networkUpdate.Value;
            if (!first)
            {
                _stringBuilder.Append(",");
            }

            first = false;
            _stringBuilder.Append(key);
            _stringBuilder.Append("=");
            _stringBuilder.Append(value.Count);
            _stringBuilder.Append("i,");
            _stringBuilder.Append(key);
            _stringBuilder.Append("_bytes=");
            _stringBuilder.Append(value.Bytes);
            _stringBuilder.Append("i");
            value.Count = 0;
            value.Bytes = 0;
        }

        _stringBuilder.Append(" ");
        _stringBuilder.Append(epochNow);
        AddToSendBuffer(_stringBuilder.ToString());
    }

    internal void OnPerformanceReportGenerated()
    {
        if (!Ready)
        {
            return;
        }

        if (!_firstReportGenerated)
        {
            _firstReportGenerated = true;
            return;
        }

        var current = Performance.current;
        if (current.frameID == _lastFrameID)
        {
            return;
        }

        _lastFrameID = current.frameID;
        var serverTag = Configuration.ServerTag;
        var epochNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        LogPerformanceReport(current, epochNow, serverTag);
        LogCpuSample(current.performanceSample, epochNow, serverTag);
        LogRuntimeProfiler(epochNow, serverTag);
    }

    private void LogPerformanceReport(Performance.Tick current, string epochNow, string serverTag)
    {
        _stringBuilder.Clear();

        _stringBuilder.Append("framerate,server=");
        _stringBuilder.Append(serverTag);
        _stringBuilder.Append(" instant=");
        AppendInfluxFloat(current.frameRate);
        _stringBuilder.Append(",average=");
        AppendInfluxFloat(current.frameRateAverage);
        _stringBuilder.Append(" ");
        _stringBuilder.Append(epochNow);
        _stringBuilder.Append("\n");

        _stringBuilder.Append("frametime,server=");
        _stringBuilder.Append(serverTag);
        _stringBuilder.Append(" instant=");
        AppendInfluxFloat(current.frameTime);
        _stringBuilder.Append(",average=");
        AppendInfluxFloat(current.frameTimeAverage);
        _stringBuilder.Append(" ");
        _stringBuilder.Append(epochNow);
        _stringBuilder.Append("\n");

        _stringBuilder.Append("memory,server=");
        _stringBuilder.Append(serverTag);
        _stringBuilder.Append(" used=");
        _stringBuilder.Append(GetMemoryUsage(current));
        _stringBuilder.Append("i,collections=");
        _stringBuilder.Append(current.memoryCollections);
        _stringBuilder.Append("i,allocations=");
        _stringBuilder.Append(current.memoryAllocations);
        _stringBuilder.Append("i,gc=");
        _stringBuilder.Append(current.gcTriggered);
        _stringBuilder.Append(" ");
        _stringBuilder.Append(epochNow);
        _stringBuilder.Append("\n");

        _stringBuilder.Append("tasks,server=");
        _stringBuilder.Append(serverTag);
        _stringBuilder.Append(" load_balancer=");
        _stringBuilder.Append(current.loadBalancerTasks);
        _stringBuilder.Append("i,invoke_handler=");
        _stringBuilder.Append(current.invokeHandlerTasks);
        _stringBuilder.Append("i,workshop_skins_queue=");
        _stringBuilder.Append(current.workshopSkinsQueued);
        _stringBuilder.Append("i ");
        _stringBuilder.Append(epochNow);
        _stringBuilder.Append("\n");

        var bytesReceivedLastSecond = Net.sv != null ? Net.sv.GetStat(null, BaseNetwork.StatTypeLong.BytesReceived_LastSecond) : 0;
        var bytesSentLastSecond = Net.sv != null ? Net.sv.GetStat(null, BaseNetwork.StatTypeLong.BytesSent_LastSecond) : 0;
        var packetLossLastSecond = Net.sv != null ? Net.sv.GetStat(null, BaseNetwork.StatTypeLong.PacketLossLastSecond) : 0;

        _stringBuilder.Append("network,server=");
        _stringBuilder.Append(serverTag);
        _stringBuilder.Append(" bytes_received=");
        _stringBuilder.Append(bytesReceivedLastSecond);
        _stringBuilder.Append("i,bytes_sent=");
        _stringBuilder.Append(bytesSentLastSecond);
        _stringBuilder.Append("i,packet_loss=");
        _stringBuilder.Append(packetLossLastSecond);
        _stringBuilder.Append("i ");
        _stringBuilder.Append(epochNow);
        _stringBuilder.Append("\n");

        _stringBuilder.Append("players,server=");
        _stringBuilder.Append(serverTag);
        _stringBuilder.Append(" count=");
        _stringBuilder.Append(BasePlayer.activePlayerList.Count);
        _stringBuilder.Append("i,sleeping=");
        _stringBuilder.Append(BasePlayer.sleepingPlayerList.Count);
        _stringBuilder.Append("i,joining=");
        _stringBuilder.Append(ServerMgr.Instance != null ? ServerMgr.Instance.connectionQueue.Joining : 0);
        _stringBuilder.Append("i,queued=");
        _stringBuilder.Append(ServerMgr.Instance != null ? ServerMgr.Instance.connectionQueue.Queued : 0);
        _stringBuilder.Append("i,bots=");
        _stringBuilder.Append(BasePlayer.bots.Count);
        _stringBuilder.Append("i");

        NpcCensus.BotSnapshot bots = default;
        NpcCensus.AnimalSnapshot animals = default;
        var censusEnabled = Configuration.GatherNpcCensus;
        if (censusEnabled)
        {
            bots = NpcCensus.CountBots();
            animals = NpcCensus.CountAnimals();
            _stringBuilder.Append(",bots_vanilla=");
            _stringBuilder.Append(bots.Vanilla);
            _stringBuilder.Append("i,bots_mod=");
            _stringBuilder.Append(bots.Mod);
            _stringBuilder.Append("i,bots_grimm=");
            _stringBuilder.Append(bots.Grimm);
            _stringBuilder.Append("i,bots_zombie=");
            _stringBuilder.Append(bots.Zombie);
            _stringBuilder.Append("i,bots_grimm_other=");
            _stringBuilder.Append(bots.GrimmOther);
            _stringBuilder.Append("i,bots_personalnpc=");
            _stringBuilder.Append(bots.PersonalNpc);
        _stringBuilder.Append("i,registry_grimm=");
        _stringBuilder.Append(bots.RegistryGrimm);
        _stringBuilder.Append("i,registry_grimm_live=");
        _stringBuilder.Append(bots.RegistryGrimmLive);
        _stringBuilder.Append("i,registry_grimm_stale=");
        _stringBuilder.Append(bots.RegistryGrimmStale);
        _stringBuilder.Append("i,registry_zombie=");
        _stringBuilder.Append(bots.RegistryZombie);
        _stringBuilder.Append("i,registry_hordes=");
        _stringBuilder.Append(bots.RegistryHordes);
        _stringBuilder.Append("i");
        }

        _stringBuilder.Append(" ");
        _stringBuilder.Append(epochNow);
        _stringBuilder.Append("\n");

        if (censusEnabled)
        {
            AppendNpcCensusLine(bots, animals, epochNow, serverTag);
        }

        _stringBuilder.Append("entities,server=");
        _stringBuilder.Append(serverTag);
        _stringBuilder.Append(" count=");
        _stringBuilder.Append(BaseNetworkable.serverEntities.Count);
        _stringBuilder.Append("i ");
        _stringBuilder.Append(epochNow);

        AddToSendBuffer(_stringBuilder.ToString());
    }

    private void AppendInfluxFloat(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            _stringBuilder.Append('0');
            return;
        }

        _stringBuilder.Append(value);
    }

    private void AppendInfluxFloat(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            _stringBuilder.Append('0');
            return;
        }

        _stringBuilder.Append(value);
    }

    private void AppendNpcCensusLine(NpcCensus.BotSnapshot bots, NpcCensus.AnimalSnapshot animals, string epochNow, string serverTag)
    {
        _stringBuilder.Append("npc_census,server=");
        _stringBuilder.Append(serverTag);
        _stringBuilder.Append(" bots_total=");
        _stringBuilder.Append(bots.Total);
        _stringBuilder.Append("i,bots_vanilla=");
        _stringBuilder.Append(bots.Vanilla);
        _stringBuilder.Append("i,bots_mod=");
        _stringBuilder.Append(bots.Mod);
        _stringBuilder.Append("i,bots_grimm=");
        _stringBuilder.Append(bots.Grimm);
        _stringBuilder.Append("i,bots_zombie=");
        _stringBuilder.Append(bots.Zombie);
        _stringBuilder.Append("i,bots_grimm_other=");
        _stringBuilder.Append(bots.GrimmOther);
        _stringBuilder.Append("i,bots_personalnpc=");
        _stringBuilder.Append(bots.PersonalNpc);
        _stringBuilder.Append("i,registry_grimm=");
        _stringBuilder.Append(bots.RegistryGrimm);
        _stringBuilder.Append("i,registry_grimm_live=");
        _stringBuilder.Append(bots.RegistryGrimmLive);
        _stringBuilder.Append("i,registry_grimm_stale=");
        _stringBuilder.Append(bots.RegistryGrimmStale);
        _stringBuilder.Append("i,registry_zombie=");
        _stringBuilder.Append(bots.RegistryZombie);
        _stringBuilder.Append("i,registry_hordes=");
        _stringBuilder.Append(bots.RegistryHordes);
        _stringBuilder.Append("i,vanilla_tunnel=");
        _stringBuilder.Append(bots.VanillaTunnel);
        _stringBuilder.Append("i,vanilla_underwater=");
        _stringBuilder.Append(bots.VanillaUnderwater);
        _stringBuilder.Append("i,vanilla_bandit=");
        _stringBuilder.Append(bots.VanillaBandit);
        _stringBuilder.Append("i,vanilla_scarecrow=");
        _stringBuilder.Append(bots.VanillaScarecrow);
        _stringBuilder.Append("i,vanilla_scientist=");
        _stringBuilder.Append(bots.VanillaScientist);
        _stringBuilder.Append("i,vanilla_human=");
        _stringBuilder.Append(bots.VanillaHuman);
        _stringBuilder.Append("i,vanilla_frankenstein=");
        _stringBuilder.Append(bots.VanillaFrankenstein);
        _stringBuilder.Append("i,vanilla_other=");
        _stringBuilder.Append(bots.VanillaOther);
        _stringBuilder.Append("i,animals_total=");
        _stringBuilder.Append(animals.Total);
        _stringBuilder.Append("i,animals_vanilla=");
        _stringBuilder.Append(animals.Vanilla);
        _stringBuilder.Append("i,animals_mod=");
        _stringBuilder.Append(animals.Mod);
        _stringBuilder.Append("i ");
        _stringBuilder.Append(epochNow);
        _stringBuilder.Append("\n");
    }

    private void LogCpuSample(PerformanceSamplePoint sample, string epochNow, string serverTag)
    {
        _stringBuilder.Clear();
        _stringBuilder.Append("cpu_sample,server=");
        _stringBuilder.Append(serverTag);
        _stringBuilder.Append(" update_ms=");
        _stringBuilder.Append(sample.Update.TotalMilliseconds);
        _stringBuilder.Append(",lateupdate_ms=");
        _stringBuilder.Append(sample.LateUpdate.TotalMilliseconds);
        _stringBuilder.Append(",fixedupdate_ms=");
        _stringBuilder.Append(sample.FixedUpdate.TotalMilliseconds);
        _stringBuilder.Append(",physics_ms=");
        _stringBuilder.Append(sample.PhysicsUpdate.TotalMilliseconds);
        _stringBuilder.Append(",prelateupdate_ms=");
        _stringBuilder.Append(sample.PreLateUpdate.TotalMilliseconds);
        _stringBuilder.Append(",total_cpu_ms=");
        _stringBuilder.Append(sample.TotalCPU.TotalMilliseconds);
        _stringBuilder.Append(",update_count=");
        _stringBuilder.Append(sample.UpdateCount);
        _stringBuilder.Append("i,fixedupdate_count=");
        _stringBuilder.Append(sample.FixedUpdateCount);
        _stringBuilder.Append("i ");
        _stringBuilder.Append(epochNow);
        AddToSendBuffer(_stringBuilder.ToString());
    }

    private void LogRuntimeProfiler(string epochNow, string serverTag)
    {
        var count = _profSampleCount > 0 ? _profSampleCount : 1;
        _stringBuilder.Clear();
        _stringBuilder.Append("runtime_profiler,server=");
        _stringBuilder.Append(serverTag);
        _stringBuilder.Append(" servermgr_update_ms=");
        _stringBuilder.Append(_profServerMgrUpdate);
        _stringBuilder.Append(",net_cycle_ms=");
        _stringBuilder.Append(_profNetCycle);
        _stringBuilder.Append(",physics_sync_ms=");
        _stringBuilder.Append(_profPhysicsSync);
        _stringBuilder.Append(",companion_tick_ms=");
        _stringBuilder.Append(_profCompanionTick);
        _stringBuilder.Append(",baseplayer_cycle_ms=");
        _stringBuilder.Append(_profBasePlayerCycle);
        _stringBuilder.Append(",frames=");
        _stringBuilder.Append(count);
        _stringBuilder.Append("i ");
        _stringBuilder.Append(epochNow);
        AddToSendBuffer(_stringBuilder.ToString());

        _profServerMgrUpdate = 0;
        _profNetCycle = 0;
        _profPhysicsSync = 0;
        _profCompanionTick = 0;
        _profBasePlayerCycle = 0;
        _profSampleCount = 0;
    }

    private void LogHarmonyMods()
    {
        if (!Ready || Configuration == null || !Configuration.GatherHarmonyModMetrics)
        {
            return;
        }

        var mods = HarmonyLoader.GetHarmonyMods();
        var totalMods = 0;

        foreach (var mod in mods)
        {
            totalMods++;
            UploadPacket("harmony_mods", mod, (builder, info) =>
            {
                builder.Append(",mod=\"");
                AppendSanitized(builder, info.Name);
                builder.Append("\" version=\"");
                builder.Append(info.Version ?? "unknown");
                builder.Append("\",patches=0i");
            });
        }

        UploadPacket("harmony_mod_count", totalMods, (builder, count) =>
        {
            builder.Append(" count=");
            builder.Append(count);
            builder.Append("i,patches=0i");
        });

        EmitHarmonyModEvents(mods);
    }

    private void EmitHarmonyModEvents(IEnumerable<HarmonyModInfo> mods)
    {
        var currentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            if (!string.IsNullOrEmpty(mod.Name))
            {
                currentNames.Add(mod.Name);
            }
        }

        if (_harmonySnapshotPrimed)
        {
            foreach (var name in currentNames)
            {
                if (!_knownHarmonyMods.Contains(name))
                {
                    OnHarmonyModLoaded(name);
                }
            }

            foreach (var name in _knownHarmonyMods)
            {
                if (!currentNames.Contains(name))
                {
                    OnHarmonyModUnloaded(name);
                }
            }
        }

        _knownHarmonyMods.Clear();
        foreach (var name in currentNames)
        {
            _knownHarmonyMods.Add(name);
        }

        _harmonySnapshotPrimed = true;
    }

    public void UploadPacket<T>(string id, T data, Action<StringBuilder, T> serializer)
    {
        if (Configuration == null)
        {
            return;
        }

        var serverTag = Configuration.ServerTag;
        var epochNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _stringBuilder.Clear();
        _stringBuilder.Append(id);
        _stringBuilder.Append(",server=");
        _stringBuilder.Append(serverTag);
        serializer.Invoke(_stringBuilder, data);
        _stringBuilder.Append(" ");
        _stringBuilder.Append(epochNow);
        AddToSendBuffer(_stringBuilder.ToString());
    }

    public void AddToSendBuffer(string toString)
    {
        if (_reportUploader == null)
        {
            return;
        }

        _reportUploader.AddToSendBuffer(toString);
    }

    private long GetMemoryUsage(Performance.Tick performanceTick)
    {
        if (performanceTick.memoryUsageSystem > 0)
        {
            return performanceTick.memoryUsageSystem;
        }

        _currentProcess ??= System.Diagnostics.Process.GetCurrentProcess();
        _currentProcess.Refresh();
        return _currentProcess.WorkingSet64 / 1024 / 1024;
    }

    private static Dictionary<Message.Type, NetworkUpdateData> CreateNetworkUpdates()
    {
        var dict = new Dictionary<Message.Type, NetworkUpdateData>();
        var values = (Message.Type[])Enum.GetValues(typeof(Message.Type));
        for (var i = 0; i < values.Length; i++)
        {
            if (!dict.ContainsKey(values[i]))
            {
                dict[values[i]] = new NetworkUpdateData(0, 0);
            }
        }

        return dict;
    }

    private static Dictionary<Message.Type, string> CreateMessageTypeNames()
    {
        var dict = new Dictionary<Message.Type, string>();
        var values = (Message.Type[])Enum.GetValues(typeof(Message.Type));
        for (var i = 0; i < values.Length; i++)
        {
            if (!dict.ContainsKey(values[i]))
            {
                dict[values[i]] = values[i].ToString();
            }
        }

        return dict;
    }

    private static string StripDll(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "unknown";
        }

        var file = System.IO.Path.GetFileName(name);
        if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return file.Substring(0, file.Length - 4);
        }

        return file;
    }

    private static void AppendSanitized(StringBuilder builder, string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
            {
                builder.Append(c);
            }
        }
    }

    private static void LogMethodInfo(StringBuilder builder, System.Reflection.MethodInfo info)
    {
        builder.Append(",behaviour=\"");
        builder.Append(info.DeclaringType != null ? info.DeclaringType.Name : "unknown");
        builder.Append("\",method=\"");
        builder.Append(info.Name);
    }

    private static void LogMethodName(StringBuilder builder, string info)
    {
        builder.Append(",behaviour=\"");
        var start = 0;
        var dot = info.IndexOf('.');
        while (dot >= 0)
        {
            builder.Append(info, start, dot - start);
            builder.Append("\",method=\"");
            start = dot + 1;
            dot = info.IndexOf('.', start);
        }

        builder.Append(info, start, info.Length - start);
    }

    private static void LogConsoleCommand(StringBuilder builder, string command)
    {
        builder.Append(",command=\"");
        builder.Append(command);
    }

    private bool _commandsRegistered;

    private void RegisterCommands()
    {
        if (_commandsRegistered)
        {
            return;
        }

        _commandsRegistered = true;
        RegisterCommand("harmonymetrics.reloadcfg", "reloadcfg", ReloadCfgCommand);
        RegisterCommand("harmonymetrics.status", "status", StatusCommand);
    }

    private void RegisterCommand(string fullName, string name, Action<ConsoleSystem.Arg> handler)
    {
        var cmd = new ConsoleSystem.Command
        {
            Name = name,
            Parent = "harmonymetrics",
            FullName = fullName,
            ServerAdmin = true,
            Variable = false,
            Call = handler
        };

        ConsoleSystem.Index.Server.Dict[fullName] = cmd;
        _registeredCommands.Add(cmd);
    }

    private void StatusCommand(ConsoleSystem.Arg arg)
    {
        _stringBuilder.Clear();
        _stringBuilder.AppendLine("[HarmonyMetrics]: Status");
        _stringBuilder.Append("\tReady: ");
        _stringBuilder.Append(Ready);
        _stringBuilder.AppendLine();
        _stringBuilder.Append("\tUploader running: ");
        _stringBuilder.Append(_reportUploader != null && _reportUploader.IsRunning);
        _stringBuilder.AppendLine();
        _stringBuilder.Append("\tBuffer: ");
        _stringBuilder.Append(_reportUploader != null ? _reportUploader.BufferSize : 0);
        _stringBuilder.AppendLine();

        var mods = HarmonyLoader.GetHarmonyMods();
        var modCount = 0;
        foreach (var unused in mods)
        {
            modCount++;
        }

        _stringBuilder.Append("\tHarmony mods: ");
        _stringBuilder.Append(modCount);
        _stringBuilder.AppendLine();
        if (Configuration == null || Configuration.GatherNpcCensus)
        {
            NpcCensus.AppendStatus(_stringBuilder);
        }

        arg.ReplyWith(_stringBuilder.ToString());
    }

    private void ReloadCfgCommand(ConsoleSystem.Arg arg)
    {
        LoadConfiguration();
        if (!ValidateConfiguration() || Configuration.Enabled == false)
        {
            Ready = false;
            var list = new List<InvokeAction>();
            InvokeHandler.FindInvokes(this, list);
            for (var i = 0; i < list.Count; i++)
            {
                CancelInvoke(list[i].action);
            }

            foreach (var player in _playerStatsActions)
            {
                var basePlayer = BasePlayer.FindByID(player.Key);
                if (basePlayer == null) continue;
                basePlayer.CancelInvoke(player.Value);
            }

            if (_reportUploader != null)
            {
                _reportUploader.Stop();
            }

            if (!Configuration.Enabled)
            {
                arg.ReplyWith("[HarmonyMetrics]: Metrics gathering has been disabled in the configuration");
                return;
            }
        }
        else if (!Ready)
        {
            Ready = true;
            foreach (var player in BasePlayer.activePlayerList)
            {
                OnPlayerInit(player);
            }

            StartLoggingMetrics();
        }

        arg.ReplyWith("[HarmonyMetrics]: Configuration reloaded");
    }

    private bool ValidateConfiguration()
    {
        if (Configuration == null) return false;

        var valid = true;
        if (Configuration.DatabaseUrl == ConfigData.DefaultInfluxDbUrl)
        {
            Debug.LogError("[HarmonyMetrics]: Default database url detected in configuration, loading aborted");
            valid = false;
        }

        if (Configuration.DatabaseName == ConfigData.DefaultInfluxDBName)
        {
            Debug.LogError("[HarmonyMetrics]: Default database name detected in configuration, loading aborted");
            valid = false;
        }

        if (Configuration.ServerTag == ConfigData.DefaultServerTag)
        {
            Debug.LogError("[HarmonyMetrics]: Default server tag detected in configuration, loading aborted");
            valid = false;
        }

        return valid;
    }

    private void LoadConfiguration()
    {
        var path = ConfigurationPath;
        Debug.Log("[HarmonyMetrics]: Config path: " + path);
        try
        {
            var configStr = File.ReadAllText(path);
            Configuration = JsonConvert.DeserializeObject<ConfigData>(configStr) ?? new ConfigData();
            var uri = new Uri(Configuration.DatabaseUrl);
            _baseUri = new Uri(uri, "/write?db=" + Configuration.DatabaseName + "&precision=ms&u=" + Configuration.DatabaseUser + "&p=" + Configuration.DatabasePassword);
        }
        catch (Exception ex)
        {
            Debug.LogError("[HarmonyMetrics]: Failed to read config (" + path + "): " + ex.Message);
            Configuration = new ConfigData();

            if (File.Exists(path))
            {
                return;
            }
        }

        SaveConfiguration();
    }

    private void SaveConfiguration()
    {
        try
        {
            var path = ConfigurationPath;
            var configFileInfo = new FileInfo(path);
            if (configFileInfo.Directory != null && !configFileInfo.Directory.Exists)
            {
                configFileInfo.Directory.Create();
            }

            var serializedConfiguration = JsonConvert.SerializeObject(Configuration, Formatting.Indented);
            File.WriteAllText(path, serializedConfiguration);
        }
        catch (Exception ex)
        {
            Debug.LogError("[HarmonyMetrics]: Failed to write configuration file");
            Debug.LogException(ex);
        }
    }
}
