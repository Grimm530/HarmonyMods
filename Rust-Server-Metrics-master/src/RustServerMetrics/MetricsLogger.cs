using HarmonyLib;
using Network;
using Newtonsoft.Json;
using RustServerMetrics.Config;
using RustServerMetrics.HarmonyPatches.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace RustServerMetrics
{
    public class MetricsLogger : SingletonComponent<MetricsLogger>
    {
        const string CONFIGURATION_PATH = "HarmonyData/ServerMetrics/Configuration.json";
        readonly static Regex PLUGIN_NAME_REGEX = new Regex(@"_|[^\w\d]");
        readonly StringBuilder _stringBuilder = new();
        readonly Dictionary<ulong, Action> _playerStatsActions = new();
        readonly Dictionary<ulong, uint> _perfReportDelayCounter = new();

        class NetworkUpdateData
        {
            public int count;
            public long bytes;

            public NetworkUpdateData(int count, long bytes)
            {
                this.count = count;
                this.bytes = bytes;
            }
        }

        readonly IReadOnlyDictionary<Message.Type, NetworkUpdateData> _networkUpdates = Enum.GetValues(typeof(Message.Type)).Cast<Message.Type>().Distinct().ToDictionary(x => x, z => new NetworkUpdateData(0, 0));

        // Plugin metrics tracking for separating initialization from runtime
        class PluginMetricsTracker
        {
            public double PreviousHookTime { get; set; }
            public double? InitializationTime { get; set; }
            public List<double> RunningHookTimes { get; } = new List<double>();
            public double PeakRunningTime { get; private set; }
            public double MinRunningTime { get; private set; } = double.MaxValue;
            public bool InitializationComplete { get; set; }
            public int MeasurementCount { get; set; }

            public void AddRunningTime(double time)
            {
                if (time < 0) return; // Skip negative deltas (shouldn't happen, but safety check)
                
                RunningHookTimes.Add(time);
                if (time > PeakRunningTime) PeakRunningTime = time;
                if (time < MinRunningTime) MinRunningTime = time;
            }

            public double GetAverageRunningTime()
            {
                if (RunningHookTimes.Count == 0) return 0;
                return RunningHookTimes.Average();
            }

            public void ResetMinIfNeeded()
            {
                if (MinRunningTime == double.MaxValue) MinRunningTime = 0;
            }
        }

        readonly Dictionary<string, PluginMetricsTracker> _pluginMetrics = new Dictionary<string, PluginMetricsTracker>();
        const double INITIALIZATION_PERIOD_SECONDS = 120.0; // Consider first 2 minutes as initialization period
        double? _serverStartTime;

        public readonly MetricsTimeStorage<MethodInfo> ServerInvokes = new("invoke_execution", LogMethodInfo);
        public readonly MetricsTimeStorage<string> ServerRpcCalls = new("rpc_calls", LogMethodName);
        public readonly MetricsTimeStorage<string> WorkQueueTimes = new("work_queue", LogMethodName);
        public readonly MetricsTimeStorage<string> ServerUpdate = new("server_update", LogMethodName);
        public readonly MetricsTimeStorage<string> TimeWarnings = new("timewarnings", LogMethodName);
        public readonly MetricsTimeStorage<string> ServerConsoleCommands = new("console_commands", (builder, command) =>
        {
            builder.Append(",command=\"");
            builder.Append(command);
        });

        public bool Ready { get; private set; }
        internal ConfigData Configuration { get; private set; }

        Uri _baseUri;
        internal readonly HashSet<string> _requestedClientPerf = new(1000);
        readonly int _performanceReport_RequestId = UnityEngine.Random.Range(-2147483648, 2147483647);
        ReportUploader _reportUploader;
        Message.Type _lastMessageType;
        bool _firstReportGenerated;

        public Uri BaseUri
        {
            get
            {
                if (_baseUri != null)
                    return _baseUri;

                var uri = new Uri(Configuration.databaseUrl);
                _baseUri = new Uri(uri, $"/write?db={Configuration.databaseName}&precision=ms&u={Configuration.databaseUser}&p={Configuration.databasePassword}");
                return _baseUri;
            }
        }

        #region Initialization

        internal static void Initialize()
        {
            new GameObject().AddComponent<MetricsLogger>();
        }

        internal void OnServerStarted()
        {
            RustServerMetricsLoader.__serverStarted = true;
            _serverStartTime = Time.time; // Track server start time for initialization detection
            
            Debug.Log($"[ServerMetrics]: Applying Startup Patches");
            var assembly = GetType().Assembly;

            // Access HarmonyLoader via reflection
            object harmonyInstance = null;
            var harmonyLoaderType = Type.GetType("HarmonyLoader, Harmony-Assembly");
            if (harmonyLoaderType != null)
            {
                var loadedModsField = harmonyLoaderType.GetField("loadedMods", BindingFlags.NonPublic | BindingFlags.Static);
                if (loadedModsField != null)
                {
                    var loadedMods = loadedModsField.GetValue(null);
                    if (loadedMods != null)
                    {
                        // Try to find our mod's harmony instance
                        var enumerator = (loadedMods as System.Collections.IEnumerable)?.GetEnumerator();
                        if (enumerator != null)
                        {
                            while (enumerator.MoveNext())
                            {
                                var mod = enumerator.Current;
                                var assemblyProperty = mod?.GetType().GetProperty("Assembly");
                                var modAssembly = assemblyProperty?.GetValue(mod) as System.Reflection.Assembly;
                                
                                if (modAssembly == assembly)
                                {
                                    var harmonyProperty = mod?.GetType().GetProperty("Harmony");
                                    var harmonyObject = harmonyProperty?.GetValue(mod);
                                    var harmonyObjectProperty = harmonyObject?.GetType().GetProperty("harmonyObject");
                                    harmonyInstance = harmonyObjectProperty?.GetValue(harmonyObject);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            
            if (harmonyInstance == null)
            {
                RustServerMetricsLoader.__harmonyInstance ??= new Harmony("RustServerMetrics" + "PATCH");
                harmonyInstance = RustServerMetricsLoader.__harmonyInstance;
            }

            var nestedTypes = assembly.GetTypes();
            foreach (var nestedType in nestedTypes)
            {
                if (nestedType.GetCustomAttribute<DelayedHarmonyPatchAttribute>(false) == null) continue;
                
                var patchProcessor = new PatchClassProcessor((Harmony)harmonyInstance, nestedType);
                Debug.Log(patchProcessor.Patch() == null ? $"[ServerMetrics]: Failed to apply patch: {nestedType.Name}" : $"[ServerMetrics]: Applied Startup Patch: {nestedType.Name}");
            }
        }

        public override void Awake()
        {
            base.Awake();
            _reportUploader = gameObject.AddComponent<ReportUploader>();
            RegisterCommands();

            LoadConfiguration();
            if (ValidateConfiguration())
            {
                if (!Configuration.enabled)
                {
                    Debug.LogWarning("[ServerMetrics]: Metrics gathering has been disabled in the configuration");
                    return;
                }

                StartLoggingMetrics();
                Ready = true;
            }
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
            
            // Collect HarmonyMod metrics every 5 seconds (less frequent than Oxide plugins)
            InvokeRepeating(OnHarmonyModMetrics, UnityEngine.Random.Range(1f, 2f), 5f);
        }

        #endregion


        internal void OnPlayerInit(BasePlayer player)
        {
            if (!Ready) return;
            if (!Configuration.gatherPlayerMetrics) return;
            var action = new Action(() => GatherPlayerSecondStats(player));
            if (_playerStatsActions.TryGetValue(player.userID, out Action existingAction))
                player.CancelInvoke(existingAction);
            _playerStatsActions[player.userID] = action;
            player.InvokeRepeating(action, UnityEngine.Random.Range(0.5f, 1.5f), 1f);
        }

        internal void OnPlayerDisconnected(BasePlayer player)
        {
            if (!Ready) return;
            if (!Configuration.gatherPlayerMetrics) return;
            if (_playerStatsActions.TryGetValue(player.userID, out Action action))
                player.CancelInvoke(action);
            _playerStatsActions.Remove(player.userID);
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
            
            var data = _networkUpdates[_lastMessageType];
            if (sendInfo.connection != null)
            {
                data.count++;
                data.bytes += write.Length;
            }
            else if (sendInfo.connections != null)
            {
                var count = sendInfo.connections.Count;
                data.count += count;
                data.bytes += write.Length * count;
            }
        }

        internal void OnOxidePluginMetrics(Dictionary<string, double> metrics)
        {
            if (!Ready) return;
            if (metrics.Count < 1) return;

            double currentTime = Time.time;
            bool isInitializationPeriod = _serverStartTime.HasValue && 
                                         (currentTime - _serverStartTime.Value) < INITIALIZATION_PERIOD_SECONDS;

            foreach (var metric in metrics)
            {
                string pluginName = PLUGIN_NAME_REGEX.Replace(metric.Key, string.Empty);
                double currentHookTime = metric.Value;

                // Get or create tracker for this plugin
                bool isFirstMeasurement = !_pluginMetrics.TryGetValue(pluginName, out PluginMetricsTracker tracker);
                if (isFirstMeasurement)
                {
                    tracker = new PluginMetricsTracker
                    {
                        PreviousHookTime = currentHookTime,
                        MeasurementCount = 0
                    };
                    _pluginMetrics[pluginName] = tracker;
                }

                tracker.MeasurementCount++;

                // Calculate delta (hook time since last measurement)
                double delta = currentHookTime - tracker.PreviousHookTime;
                tracker.PreviousHookTime = currentHookTime;

                // Handle initialization vs runtime
                if (isInitializationPeriod && !tracker.InitializationComplete)
                {
                    // Still in initialization period - accumulate initialization time
                    if (isFirstMeasurement)
                    {
                        // First measurement: treat entire cumulative value as initialization
                        // (since TotalHookTime is cumulative from plugin load)
                        tracker.InitializationTime = currentHookTime;
                    }
                    else
                    {
                        // Subsequent measurements: add delta to initialization time
                        if (!tracker.InitializationTime.HasValue)
                        {
                            tracker.InitializationTime = delta;
                        }
                        else
                        {
                            tracker.InitializationTime += delta;
                        }
                    }
                }
                else
                {
                    // Initialization complete - track as running time
                    if (!tracker.InitializationComplete)
                    {
                        // First measurement after initialization - mark as complete
                        tracker.InitializationComplete = true;
                        // If we have accumulated initialization time, upload it
                        if (tracker.InitializationTime.HasValue && tracker.InitializationTime.Value > 0)
                        {
                            UploadPacket("oxide_plugins", pluginName, (builder, name) =>
                            {
                                builder.Append(",plugin=\"");
                                builder.Append(name);
                                builder.Append("\" initTime=");
                                builder.Append(tracker.InitializationTime.Value);
                            });
                        }
                    }

                    // Track running hook time (delta)
                    if (delta > 0) // Only track positive deltas
                    {
                        tracker.AddRunningTime(delta);
                    }
                }

                // Upload current metrics
                // Always upload current hookTime for backward compatibility
                UploadPacket("oxide_plugins", pluginName, (builder, name) =>
                {
                    builder.Append(",plugin=\"");
                    builder.Append(name);
                    builder.Append("\" hookTime=");
                    builder.Append(currentHookTime);
                });

                // Upload running metrics if we have data
                if (tracker.InitializationComplete && tracker.RunningHookTimes.Count > 0)
                {
                    tracker.ResetMinIfNeeded();
                    
                    double avgRunningTime = tracker.GetAverageRunningTime();
                    
                    UploadPacket("oxide_plugins", pluginName, (builder, name) =>
                    {
                        builder.Append(",plugin=\"");
                        builder.Append(name);
                        builder.Append("\" peakRunningTime=");
                        builder.Append(tracker.PeakRunningTime);
                        builder.Append(",avgRunningTime=");
                        builder.Append(avgRunningTime);
                        builder.Append(",minRunningTime=");
                        builder.Append(tracker.MinRunningTime);
                    });
                }
            }
        }

        internal void OnHarmonyModMetrics()
        {
            if (!Ready) return;
            
            try
            {
                // Try to find HarmonyLoader type - try multiple assembly names
                Type harmonyLoaderType = null;
                
                // First try the common assembly names
                harmonyLoaderType = Type.GetType("HarmonyLoader, Rust.Harmony")
                    ?? Type.GetType("HarmonyLoader, Harmony-Assembly");
                
                // If not found, search all loaded assemblies
                if (harmonyLoaderType == null)
                {
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            harmonyLoaderType = assembly.GetType("HarmonyLoader");
                            if (harmonyLoaderType != null) break;
                        }
                        catch { }
                    }
                }
                
                if (harmonyLoaderType == null)
                {
                    if (Configuration.debugLogging)
                    {
                        var assemblyNames = AppDomain.CurrentDomain.GetAssemblies()
                            .Where(a => a != null && !a.IsDynamic)
                            .Select(a => 
                            {
                                try
                                {
                                    var name = a.GetName();
                                    return name != null ? name.Name : "Unknown";
                                }
                                catch
                                {
                                    return "Unknown";
                                }
                            })
                            .Where(n => n != null && (n.Contains("Harmony") || n.Contains("Rust")))
                            .ToList();
                        Debug.LogWarning("[ServerMetrics] HarmonyLoader type not found. Searched assemblies: " + 
                            string.Join(", ", assemblyNames));
                    }
                    return;
                }

                var getHarmonyModsMethod = harmonyLoaderType.GetMethod("GetHarmonyMods", BindingFlags.Public | BindingFlags.Static);
                if (getHarmonyModsMethod == null)
                {
                    Debug.LogWarning("[ServerMetrics] GetHarmonyMods method not found on type: " + harmonyLoaderType.FullName);
                    return;
                }

                var harmonyMods = getHarmonyModsMethod.Invoke(null, null);
                if (harmonyMods == null)
                {
                    Debug.LogWarning("[ServerMetrics] GetHarmonyMods returned null (no mods loaded?)");
                    return;
                }

                // HarmonyModInfo is a struct, so we can iterate directly
                var enumerable = harmonyMods as System.Collections.IEnumerable;
                if (enumerable == null)
                {
                    Debug.LogWarning("[ServerMetrics] HarmonyMods is not enumerable. Type: " + harmonyMods.GetType().FullName);
                    return;
                }

                // Get HarmonyModInfo type to access its fields (Name and Version are fields, not properties)
                var harmonyModInfoType = Type.GetType("HarmonyModInfo, Rust.Harmony")
                    ?? Type.GetType("HarmonyModInfo, Harmony-Assembly");
                    
                if (harmonyModInfoType == null && harmonyLoaderType.Assembly != null)
                {
                    harmonyModInfoType = harmonyLoaderType.Assembly.GetType("HarmonyModInfo");
                }
                    
                if (harmonyModInfoType == null)
                {
                    if (Configuration.debugLogging)
                    {
                        Debug.LogWarning("[ServerMetrics] HarmonyModInfo type not found");
                    }
                    return;
                }

                var nameField = harmonyModInfoType.GetField("Name");
                var versionField = harmonyModInfoType.GetField("Version");
                
                if (nameField == null)
                {
                    Debug.LogWarning("[ServerMetrics] HarmonyModInfo.Name field not found");
                    return;
                }

                int modCount = 0;
                // Iterate through HarmonyMods
                foreach (var modInfo in enumerable)
                {
                    if (modInfo == null) continue;

                    // Get mod name and version (they are fields, not properties)
                    string modName = null;
                    try
                    {
                        var nameValue = nameField.GetValue(modInfo);
                        modName = nameValue as string;
                    }
                    catch
                    {
                        // Skip this mod if we can't get its name
                        continue;
                    }
                    
                    if (string.IsNullOrEmpty(modName)) continue;

                    string version = "unknown";
                    if (versionField != null)
                    {
                        version = versionField.GetValue(modInfo) as string ?? "unknown";
                    }

                    // Upload HarmonyMod metric (using hookTime=1 to indicate loaded status)
                    // Format matches Oxide plugins exactly - no type tag, appears as regular plugin
                    UploadPacket("oxide_plugins", modName, (builder, pluginName) =>
                    {
                        builder.Append(",plugin=\"");
                        builder.Append(PLUGIN_NAME_REGEX.Replace(pluginName, string.Empty));
                        builder.Append("\" hookTime=1");
                    });
                    
                    modCount++;
                }

                if (modCount > 0 && Configuration.debugLogging)
                {
                    Debug.Log($"[ServerMetrics] Collected metrics for {modCount} HarmonyMod(s)");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ServerMetrics] Failed to collect HarmonyMod metrics: {ex}");
            }
        }

        internal bool OnClientPerformanceReport(ClientPerformanceReport clientPerformanceReport)
        {
            if (clientPerformanceReport.request_id != _performanceReport_RequestId) return false;

            UploadPacket("client_performance", clientPerformanceReport, (builder, report) =>
            {
                builder.Append(",steamid=");
                builder.Append(report.user_id);
                builder.Append(" memory=");
                builder.Append(report.memory_system);
                builder.Append("i,fps=");
                builder.Append(report.fps);
            });

            _requestedClientPerf.Remove(clientPerformanceReport.user_id);
            return true;
        }

        void GatherPlayerSecondStats(BasePlayer player)
        {
            if (!player.IsReceivingSnapshot)
            {
                _perfReportDelayCounter.TryGetValue(player.userID, out uint perfReportCounter);
                if (perfReportCounter < 4)
                {
                    _perfReportDelayCounter[player.userID] = perfReportCounter + 1;
                }
                else
                {
                    _perfReportDelayCounter[player.userID] = 0;
                    player.ClientRPCPlayer(null, player, "GetPerformanceReport", "legacy", _performanceReport_RequestId);
                }
            }

            UploadPacket("connection_latency", player, (builder, basePlayer) =>
            {
                var ip = basePlayer.net.connection.ipaddress;

                builder.Append(",steamid=");
                builder.Append(basePlayer.UserIDString);
                builder.Append(",ip=");
                builder.Append(ip.Substring(0, ip.LastIndexOf(':')));
                builder.Append(" ping=");
                builder.Append(Net.sv.GetAveragePing(basePlayer.net.connection));
                builder.Append("i,packet_loss=");
                builder.Append(Net.sv.GetStat(basePlayer.net.connection, BaseNetwork.StatTypeLong.PacketLossLastSecond));
                builder.Append("i ");
            });
        }

        void LogNetworkUpdates()
        {
            if (_networkUpdates.Count < 1) return;
            var epochNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            _stringBuilder.Clear();
            _stringBuilder.Append("network_updates,server=");
            _stringBuilder.Append(Configuration.serverTag);
            _stringBuilder.Append(" ");

            var enumerator = _networkUpdates.GetEnumerator();
            if (enumerator.MoveNext())
            {
                var networkUpdate = enumerator.Current;
                var key = networkUpdate.Key.ToString();
                var value = networkUpdate.Value;
                // Count first named {type}
                _stringBuilder.Append(key);
                _stringBuilder.Append("=");
                _stringBuilder.Append(value.count.ToString());
                _stringBuilder.Append("i");
                value.count = 0;

                // Bytes second named as "{type}_bytes"
                _stringBuilder.Append(",");
                _stringBuilder.Append(key);
                _stringBuilder.Append("_bytes");
                _stringBuilder.Append("=");
                _stringBuilder.Append(value.bytes.ToString());
                _stringBuilder.Append("i");
                value.bytes = 0;

                while (enumerator.MoveNext())
                {
                    networkUpdate = enumerator.Current;
                    key = networkUpdate.Key.ToString();
                    value = networkUpdate.Value;

                    // Count first named {type}
                    _stringBuilder.Append(",");
                    _stringBuilder.Append(key);
                    _stringBuilder.Append("=");
                    _stringBuilder.Append(value.count.ToString());
                    _stringBuilder.Append("i");
                    value.count = 0;

                    // Bytes second named as "{type}_bytes"
                    _stringBuilder.Append(",");
                    _stringBuilder.Append(key);
                    _stringBuilder.Append("_bytes");
                    _stringBuilder.Append("=");
                    _stringBuilder.Append(value.bytes.ToString());
                    _stringBuilder.Append("i");
                    value.bytes = 0;
                }
            }

            _stringBuilder.Append(" ");
            _stringBuilder.Append(epochNow);
            _reportUploader.AddToSendBuffer(_stringBuilder.ToString());
        }

        internal void OnPerformanceReportGenerated()
        {
            if (!Ready) return;
            if (!_firstReportGenerated)
            {
                _firstReportGenerated = true;
                return;
            }
            var current = Performance.current;
            // Performance.Tick is a struct; skip if report not yet populated (e.g. map generation, RCON)
            if (current.frameRate == 0 && current.frameTime == 0) return;
            var epochNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            _stringBuilder.Clear();
            _stringBuilder.Append("framerate,server=");
            _stringBuilder.Append(Configuration.serverTag);
            _stringBuilder.Append(" instant=");
            _stringBuilder.Append(current.frameRate);
            _stringBuilder.Append(",average=");
            _stringBuilder.Append(current.frameRateAverage);
            _stringBuilder.Append(" ");
            _stringBuilder.Append(epochNow);
            _stringBuilder.Append("\n");

            _stringBuilder.Append("frametime,server=");
            _stringBuilder.Append(Configuration.serverTag);
            _stringBuilder.Append(" instant=");
            _stringBuilder.Append(current.frameTime);
            _stringBuilder.Append(",average=");
            _stringBuilder.Append(current.frameTimeAverage);
            _stringBuilder.Append(" ");
            _stringBuilder.Append(epochNow);
            _stringBuilder.Append("\n");

            _stringBuilder.Append("memory,server=");
            _stringBuilder.Append(Configuration.serverTag);
            _stringBuilder.Append(" used=");
            _stringBuilder.Append(current.memoryUsageSystem);
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
            _stringBuilder.Append(Configuration.serverTag);
            _stringBuilder.Append(" load_balancer=");
            _stringBuilder.Append(current.loadBalancerTasks);
            _stringBuilder.Append("i,invoke_handler=");
            _stringBuilder.Append(current.invokeHandlerTasks);
            _stringBuilder.Append("i,workshop_skins_queue=");
            _stringBuilder.Append(current.workshopSkinsQueued);
            _stringBuilder.Append("i ");
            _stringBuilder.Append(epochNow);
            _stringBuilder.Append("\n");

            if (Net.sv == null) return; // Server network not up (e.g. map generation, RCON port conflict)
            var bytesReceivedLastSecond = Net.sv.GetStat(null, BaseNetwork.StatTypeLong.BytesReceived_LastSecond);
            var bytesSentLastSecond = Net.sv.GetStat(null, BaseNetwork.StatTypeLong.BytesSent_LastSecond);
            var packetLossLastSecond = Net.sv.GetStat(null, BaseNetwork.StatTypeLong.PacketLossLastSecond);

            _stringBuilder.Append("network,server=");
            _stringBuilder.Append(Configuration.serverTag);
            _stringBuilder.Append(" bytes_received=");
            _stringBuilder.Append(bytesReceivedLastSecond);
            _stringBuilder.Append("i,bytes_sent=");
            _stringBuilder.Append(bytesSentLastSecond);
            _stringBuilder.Append("i,packet_loss=");
            _stringBuilder.Append(packetLossLastSecond);
            _stringBuilder.Append("i ");
            _stringBuilder.Append(epochNow);
            _stringBuilder.Append("\n");

            if (ServerMgr.Instance?.connectionQueue == null) return; // Server not fully started (e.g. map generation)
            _stringBuilder.Append("players,server=");
            _stringBuilder.Append(Configuration.serverTag);
            _stringBuilder.Append(" count=");
            _stringBuilder.Append(BasePlayer.activePlayerList.Count);
            _stringBuilder.Append("i,joining=");
            _stringBuilder.Append(ServerMgr.Instance.connectionQueue.Joining);
            _stringBuilder.Append("i,queued=");
            _stringBuilder.Append(ServerMgr.Instance.connectionQueue.Queued);
            _stringBuilder.Append("i ");
            _stringBuilder.Append(epochNow);
            _stringBuilder.Append("\n");

            _stringBuilder.Append("entities,server=");
            _stringBuilder.Append(Configuration.serverTag);
            _stringBuilder.Append(" count=");
            _stringBuilder.Append(BaseNetworkable.serverEntities.Count);
            _stringBuilder.Append("i ");
            _stringBuilder.Append(epochNow);
            _reportUploader.AddToSendBuffer(_stringBuilder.ToString());
        }


        #region Helpers

        public void UploadPacket<T>(string ID, T data, Action<StringBuilder, T> serializer)
        {
            if (Configuration == null || string.IsNullOrEmpty(Configuration.serverTag))
            {
                if (Configuration?.debugLogging == true)
                {
                    Debug.LogWarning("[ServerMetrics] Cannot upload packet: Configuration or serverTag is null");
                }
                return;
            }
            
            var epochNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            _stringBuilder.Clear();
            _stringBuilder.Append(ID);
            _stringBuilder.Append(",server=");
            _stringBuilder.Append(Configuration.serverTag);

            serializer.Invoke(_stringBuilder, data);

            _stringBuilder.Append(" ");
            _stringBuilder.Append(epochNow);

            AddToSendBuffer(_stringBuilder.ToString());
        }

        public void AddToSendBuffer(string toString) => _reportUploader.AddToSendBuffer(toString);

        private static void LogMethodInfo(StringBuilder builder, MethodInfo info)
        {
            builder.Append(",behaviour=\"");
            builder.Append(info.DeclaringType?.Name);
            builder.Append("\",method=\"");
            builder.Append(info.Name);
        }

        private static void LogMethodName(StringBuilder builder, string info)
        {
            builder.Append(",behaviour=\"");

            foreach (var cursor in info)
            {
                if (cursor == '.')
                    builder.Append("\",method=\"");
                else
                    builder.Append(cursor);
            }
        }
        #endregion


        #region Commands

        void RegisterCommands()
        {
            const string commandPrefix = "servermetrics";
            ConsoleSystem.Command reloadCfgCommand = new ConsoleSystem.Command()
            {
                Name = "reloadcfg",
                Parent = commandPrefix,
                FullName = commandPrefix + "." + "reloadcfg",
                ServerAdmin = true,
                Variable = false,
                Call = new Action<ConsoleSystem.Arg>(ReloadCfgCommand)
            };

            ConsoleSystem.Command statusCommand = new ConsoleSystem.Command()
            {
                Name = "status",
                Parent = commandPrefix,
                FullName = commandPrefix + "." + "status",
                ServerAdmin = true,
                Variable = false,
                Call = new Action<ConsoleSystem.Arg>(StatusCommand)
            };

            ConsoleSystem.Index.Server.Dict[commandPrefix + "." + "reloadcfg"] = reloadCfgCommand;
            ConsoleSystem.Index.Server.Dict[commandPrefix + "." + "status"] = statusCommand;

            // Would be nice if this had a public setter, or better yet, a register command helper
            // update: now it does
            ConsoleSystem.Index.All = ConsoleSystem.Index.All.Concat(new[] { reloadCfgCommand, statusCommand }).ToArray();
        }

        void StatusCommand(ConsoleSystem.Arg arg)
        {
            _stringBuilder.Clear();
            _stringBuilder.AppendLine("[ServerMetrics]: Status");
            _stringBuilder.AppendLine("Overview");
            _stringBuilder.Append("\tReady: "); _stringBuilder.Append(Ready); _stringBuilder.AppendLine();
            _stringBuilder.AppendLine("Report Uploader:");
            _stringBuilder.Append("\tRunning: "); _stringBuilder.Append(_reportUploader.IsRunning); _stringBuilder.AppendLine();
            _stringBuilder.Append("\tIn Buffer: "); _stringBuilder.Append(_reportUploader.BufferSize); _stringBuilder.AppendLine();
            arg.ReplyWith(_stringBuilder.ToString());
        }

        void ReloadCfgCommand(ConsoleSystem.Arg arg)
        {
            LoadConfiguration();
            if (!ValidateConfiguration() || Configuration.enabled == false)
            {
                Ready = false;

                // why is there no cancel all invokes method ...
                var list = new List<InvokeAction>();
                InvokeHandler.FindInvokes(this, list);
                foreach (var invoke in list)
                {
                    CancelInvoke(invoke.action);
                }

                foreach (var player in _playerStatsActions)
                {
                    var basePlayer = BasePlayer.FindByID(player.Key);
                    if (basePlayer == null) continue;
                    basePlayer.CancelInvoke(player.Value);
                }
                _reportUploader.Stop();

                if (!Configuration.enabled)
                {
                    arg.ReplyWith("[ServerMetrics]: Metrics gathering has been disabled in the configuration");
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
            arg.ReplyWith("[ServerMetrics]: Configuration reloaded");
        }

        #endregion


        #region Configuration

        bool ValidateConfiguration()
        {
            if (Configuration == null) return false;

            bool valid = true;
            if (Configuration.databaseUrl == ConfigData.DEFAULT_INFLUX_DB_URL)
            {
                Debug.LogError("[ServerMetrics]: Default database url detected in configuration, loading aborted");
                valid = false;
            }

            if (Configuration.databaseName == ConfigData.DEFAULT_INFLUX_DB_NAME)
            {
                Debug.LogError("[ServerMetrics]: Default database name detected in configuration, loading aborted");
                valid = false;
            }

            if (Configuration.serverTag == ConfigData.DEFAULT_SERVER_TAG)
            {
                Debug.LogError("[ServerMetrics]: Default server tag detected in configuration, loading aborted");
                valid = false;
            }

            return valid;
        }

        void LoadConfiguration()
        {
            try
            {
                var configStr = File.ReadAllText(CONFIGURATION_PATH);
                Configuration = JsonConvert.DeserializeObject<ConfigData>(configStr);
                if (Configuration == null) Configuration = new ConfigData();
                var uri = new Uri(Configuration.databaseUrl);
                _baseUri = new Uri(uri, $"/write?db={Configuration.databaseName}&precision=ms&u={Configuration.databaseUser}&p={Configuration.databasePassword}");
            }
            catch
            {
                Debug.LogError("[ServerMetrics]: The configuration seems to be missing or malformed. Defaults will be loaded.");
                Configuration = new ConfigData();

                if (File.Exists(CONFIGURATION_PATH))
                {
                    return;
                }
            }
            SaveConfiguration();
        }

        void SaveConfiguration()
        {
            try
            {
                var configFileInfo = new FileInfo(CONFIGURATION_PATH);
                if (!configFileInfo.Directory.Exists) configFileInfo.Directory.Create();
                var serializedConfiguration = JsonConvert.SerializeObject(Configuration, Formatting.Indented);
                File.WriteAllText(CONFIGURATION_PATH, serializedConfiguration);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ServerMetrics]: Failed to write configuration file");
                Debug.LogException(ex);
            }
        }

        #endregion
    }
}

