using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using Facepunch;

namespace Convoy
{
    public class ConvoyMod : IHarmonyModHooks
    {
        public static ConvoyMod Instance { get; private set; }

        public ConvoyConfig Config { get; private set; }

        /// <summary>Full plugin config for pathfinding + vehicle spawn (same Convoy.json with Route Settings, Convoy Presets, etc.).</summary>
        public ConvoyPluginConfig FullConfig { get; private set; }

        private ConsoleSystem.Command _convoystartCmd;
        private ConsoleSystem.Command _convoystopCmd;
        private readonly List<BaseEntity> _mapMarkers = new List<BaseEntity>();
        private Coroutine _autoEventCoroutine;
        private Coroutine _initCoroutine;
        private GameObject _runnerGo;
        private ModRunner _runner;
        private string _configFilePath;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            TryApplyFindPatch();
            LoadConfig();
            LogDebug("OnLoaded: config loaded, Debug=" + (Config?.Debug ?? false));
            ConvoyGrimmNpc.Bind();
            ConvoyPathManager.ConfigProvider = () => Instance?.FullConfig;
            ConvoyPathManager.CustomRoutesBaseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            ConvoyState.Clear();
            RegisterCommands();
            // Harmony loads before ServerMgr exists — defer auto-event + path cache until server is ready.
            EnsureRunner();
            _initCoroutine = _runner.StartCoroutine(WaitForServerThenInit());
            bool auto = Config?.MainConfig?.IsAutoEvent == true;
            UnityEngine.Debug.Log("[Convoy] Harmony mod loaded. convoystart/convoystop (server console or admin). Auto-event=" + auto + " (starts after ServerMgr ready). Config: HarmonyConfig/Convoy.json.");
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runnerGo = new GameObject("Convoy_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runnerGo);
            _runnerGo.hideFlags = HideFlags.HideAndDontSave;
            _runner = _runnerGo.AddComponent<ModRunner>();
        }

        private void DestroyRunner()
        {
            if (_runnerGo != null)
            {
                UnityEngine.Object.Destroy(_runnerGo);
                _runnerGo = null;
                _runner = null;
            }
        }

        private IEnumerator WaitForServerThenInit()
        {
            while (ServerMgr.Instance == null)
                yield return null;
            if (Instance == null) yield break;

            StartAutoEventTimerIfEnabled();
            yield return new WaitForSeconds(5f);
            if (Instance != null && FullConfig?.PathConfig != null)
            {
                ConvoyPathManager.StartCachingRoutes();
                LogDebug("Path caching started (PathType=" + FullConfig.PathConfig.PathType + ").");
            }
            _initCoroutine = null;
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            StopAutoEventTimer();
            if (_initCoroutine != null && _runner != null)
            {
                _runner.StopCoroutine(_initCoroutine);
                _initCoroutine = null;
            }
            UnregisterCommands();
            EventLauncher.StopEvent();
            DeleteMapMarkers();
            ConvoyPathManager.OnPluginUnloaded();
            ConvoyPathManager.ConfigProvider = null;
            ConvoyState.Clear();
            DestroyRunner();
            Instance = null;
            UnityEngine.Debug.Log("[Convoy] Harmony mod unloaded.");
        }

        private sealed class ModRunner : MonoBehaviour { }

        private bool IsEventActive()
        {
            return EventLauncher.IsEventActive();
        }

        private Vector3 GetDefaultEventPosition()
        {
            if (Config?.DefaultEventPosition != null && Config.DefaultEventPosition.Length >= 3)
            {
                var p = new Vector3(Config.DefaultEventPosition[0], Config.DefaultEventPosition[1], Config.DefaultEventPosition[2]);
                if (p.x != 0f || p.y != 100f || p.z != 0f)
                    return p;
            }
            return GetMapCenterPosition();
        }

        private static Vector3 GetMapCenterPosition()
        {
            try
            {
                if (TerrainMeta.HeightMap != null)
                {
                    Vector3 center = TerrainMeta.Position + new Vector3(TerrainMeta.Size.x * 0.5f, 0f, TerrainMeta.Size.z * 0.5f);
                    center.y = TerrainMeta.HeightMap.GetHeight(center);
                    return center;
                }
            }
            catch { }
            return new Vector3(0f, 100f, 0f);
        }

        private void StartAutoEventTimerIfEnabled()
        {
            StopAutoEventTimer();
            if (Config?.MainConfig == null || !Config.MainConfig.IsAutoEvent)
            {
                UnityEngine.Debug.Log("[Convoy] Auto-event disabled in Main Setting.");
                return;
            }
            int min = Math.Max(60, Config.MainConfig.MinTimeBetweenEvents);
            int max = Math.Max(min, Config.MainConfig.MaxTimeBetweenEvents);
            EnsureRunner();
            _autoEventCoroutine = _runner.StartCoroutine(AutoEventCoroutine(min, max));
            UnityEngine.Debug.Log("[Convoy] Auto-event timer armed. Next start in " + min + "-" + max + " sec.");
        }

        private void StopAutoEventTimer()
        {
            if (_autoEventCoroutine != null && _runner != null)
            {
                _runner.StopCoroutine(_autoEventCoroutine);
                _autoEventCoroutine = null;
            }
        }

        private IEnumerator AutoEventCoroutine(int minSec, int maxSec)
        {
            while (Instance != null && Config?.MainConfig?.IsAutoEvent == true)
            {
                // Unity int Range max is exclusive; use float Range so min==max still waits that many seconds.
                float wait = UnityEngine.Random.Range((float)minSec, (float)maxSec);
                UnityEngine.Debug.Log("[Convoy] Auto-event: waiting " + (int)wait + " sec until next start.");
                yield return new WaitForSeconds(wait);
                if (Instance == null) break;
                if (IsEventActive())
                {
                    LogDebug("AutoEvent: event already active, skipping start");
                    continue;
                }

                bool started = EventLauncher.DelayStartEvent();
                if (started)
                    UnityEngine.Debug.Log("[Convoy] Auto-event: starting convoy.");
                else
                    UnityEngine.Debug.LogWarning("[Convoy] Auto-event: start failed (will retry after next interval).");

                // Wait until the event ends before scheduling the next one.
                while (Instance != null && EventLauncher.IsEventActive())
                    yield return new WaitForSeconds(5f);

                if (Instance == null) break;
                UnityEngine.Debug.Log("[Convoy] Auto event ended. Scheduling next.");
            }
            _autoEventCoroutine = null;
        }

        private static string FormatPosition(Vector3 pos)
        {
            return string.Format("({0:F1}, {1:F1}, {2:F1})", pos.x, pos.y, pos.z);
        }

        private void StartConvoyEventMinimal()
        {
            Vector3 pos;
            string posStr;
            if (FullConfig?.PathConfig != null)
            {
                ConvoyPathManager.GenerateNewPath();
                if (ConvoyPathManager.CurrentPath != null && ConvoyPathManager.CurrentPath.StartPathPoint != null)
                {
                    pos = ConvoyPathManager.CurrentPath.StartPathPoint.Position;
                    posStr = FormatPosition(pos);
                    LogDebug("StartConvoyEventMinimal: using pathfinding start point " + posStr);
                }
                else
                {
                    pos = GetDefaultEventPosition();
                    posStr = FormatPosition(pos);
                    LogDebug("StartConvoyEventMinimal: no path found, using default " + posStr);
                }
            }
            else
            {
                pos = GetDefaultEventPosition();
                posStr = FormatPosition(pos);
                LogDebug("StartConvoyEventMinimal: no full config, at " + posStr);
            }
            DeleteMapMarkers();
            ConvoyState.Clear();
            CreateMapMarker(pos);
            ConvoyState.SetConvoyState(true, false, false, false);
            UnityEngine.Debug.Log("[Convoy] Convoy started. Map markers at " + posStr + ". Pathfinding " + (ConvoyPathManager.CurrentPath != null ? "OK (route ready)." : "skipped (no Route Settings or no path).") + " Use convoystop to stop. Vehicle spawn in next update.");
        }

        private void LoadConfig()
        {
            Config = Config ?? new ConvoyConfig();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;
            LogDebug("LoadConfig: BaseDirectory=" + baseDir);
            string harmonyConfigPath = Path.Combine(baseDir, "HarmonyConfig", "Convoy.json");
            string[] paths =
            {
                harmonyConfigPath,
                Path.Combine(baseDir, "oxide", "config", "Convoy.json"),
                Path.Combine(baseDir, "Config", "Convoy.json"),
                Path.Combine(baseDir, "Convoy.json"),
            };
            _configFilePath = harmonyConfigPath;
            foreach (string p in paths)
            {
                bool exists = File.Exists(p);
                LogDebug("LoadConfig: try path " + p + " exists=" + exists);
                if (exists)
                {
                    _configFilePath = p;
                    try
                    {
                        string json = File.ReadAllText(p);
                        Config = JsonConvert.DeserializeObject<ConvoyConfig>(json);
                        if (Config == null) Config = new ConvoyConfig();

                        ConvoyPluginConfig loaded = null;
                        try { loaded = JsonConvert.DeserializeObject<ConvoyPluginConfig>(json); } catch { loaded = null; }

                        // Only re-save if we parsed real content (has vehicle/NPC/event presets). Otherwise a partial
                        // deserialize would let MergeFullConfigDefaults wipe the file with empty defaults on save.
                        bool loadedHasContent = loaded != null
                            && loaded.EventConfigs != null && loaded.EventConfigs.Count > 0;

                        FullConfig = loaded;
                        MergeFullConfigDefaults();
                        PopulateConfigFromFullConfig();
                        if (Config.LootSettings == null) Config.LootSettings = new LootSettingsOptions();
                        if (Config.NpcPresets == null) Config.NpcPresets = new List<NpcPresetEntry>();
                        if (Config.CratePresets == null) Config.CratePresets = new List<CratePresetEntry>();
                        if (Config.MainConfig == null) Config.MainConfig = new MainConfig();
                        if (Config.MarkerConfig == null) Config.MarkerConfig = new MarkerConfig();
                        if (Config.MarkerConfig.Color1 == null) Config.MarkerConfig.Color1 = new ColorConfig();
                        if (Config.MarkerConfig.Color2 == null) Config.MarkerConfig.Color2 = new ColorConfig();
                        if (Config.DefaultEventPosition == null || Config.DefaultEventPosition.Length < 3) Config.DefaultEventPosition = new float[] { 0f, 100f, 0f };

                        if (loadedHasContent)
                            SaveConfig();
                        else
                            UnityEngine.Debug.LogWarning("[Convoy] Config at " + p + " parsed without vehicle/NPC presets; keeping file as-is (not overwriting with defaults).");

                        LogDebug("LoadConfig: loaded from " + p + " Debug=" + Config.Debug + " IsAutoEvent=" + Config.MainConfig.IsAutoEvent + " hasContent=" + loadedHasContent);
                        return;
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogWarning($"[Convoy] Failed to load config from {p}: {ex.Message}. Using defaults.");
                        LogDebug("LoadConfig: exception " + ex);
                    }
                    break;
                }
            }
            Config = new ConvoyConfig();
            FullConfig = ConvoyPluginConfig.GetDefault();
            PopulateConfigFromFullConfig();
            EnsureHarmonyConfigDir();
            SaveConfig();
            LogDebug("LoadConfig: no file found, wrote default config to " + _configFilePath);
        }

        private void MergeFullConfigDefaults()
        {
            if (FullConfig == null)
            {
                FullConfig = ConvoyPluginConfig.GetDefault();
                return;
            }
            var def = ConvoyPluginConfig.GetDefault();
            if (FullConfig.PathConfig == null) FullConfig.PathConfig = def.PathConfig;
            if (FullConfig.EventConfigs == null || FullConfig.EventConfigs.Count == 0) FullConfig.EventConfigs = def.EventConfigs;
            if (FullConfig.TravelingVendorConfigs == null) FullConfig.TravelingVendorConfigs = def.TravelingVendorConfigs;
            if (FullConfig.ModularCarConfigs == null) FullConfig.ModularCarConfigs = def.ModularCarConfigs;
            if (FullConfig.BradleyConfigs == null) FullConfig.BradleyConfigs = def.BradleyConfigs;
            if (FullConfig.SedanConfigs == null) FullConfig.SedanConfigs = def.SedanConfigs;
            if (FullConfig.BikeConfigs == null) FullConfig.BikeConfigs = def.BikeConfigs;
            if (FullConfig.KaruzaCarConfigs == null) FullConfig.KaruzaCarConfigs = def.KaruzaCarConfigs;
            if (FullConfig.HeliConfigs == null) FullConfig.HeliConfigs = def.HeliConfigs;
            if (FullConfig.TurretConfigs == null) FullConfig.TurretConfigs = def.TurretConfigs;
            if (FullConfig.SamsiteConfigs == null) FullConfig.SamsiteConfigs = def.SamsiteConfigs;
            if (FullConfig.CrateConfigs == null) FullConfig.CrateConfigs = def.CrateConfigs;
            if (FullConfig.NpcConfigs == null) FullConfig.NpcConfigs = def.NpcConfigs;
            if (FullConfig.MainConfig == null) FullConfig.MainConfig = def.MainConfig;
            if (FullConfig.BehaviorConfig == null) FullConfig.BehaviorConfig = def.BehaviorConfig;
            if (FullConfig.LootConfig == null) FullConfig.LootConfig = def.LootConfig;
            if (FullConfig.MarkerConfig == null) FullConfig.MarkerConfig = def.MarkerConfig;
            if (FullConfig.NotifyConfig == null) FullConfig.NotifyConfig = def.NotifyConfig;
            else
            {
                if (FullConfig.NotifyConfig.TimeNotifications == null)
                    FullConfig.NotifyConfig.TimeNotifications = def.NotifyConfig?.TimeNotifications ?? new HashSet<int> { 300, 60, 30, 5 };
                if (FullConfig.NotifyConfig.GameTipConfig == null)
                    FullConfig.NotifyConfig.GameTipConfig = def.NotifyConfig?.GameTipConfig ?? new ConvoyGameTipConfig { IsEnabled = true, Style = 0 };
            }
        }

        private void PopulateConfigFromFullConfig()
        {
            if (FullConfig == null) return;
            Config.Prefix = FullConfig.Prefix ?? Config.Prefix;
            Config.Debug = FullConfig.Debug;
            if (FullConfig.DefaultEventPosition != null && FullConfig.DefaultEventPosition.Length >= 3)
                Config.DefaultEventPosition = FullConfig.DefaultEventPosition;
            Config.EventDurationAutoSec = FullConfig.EventDurationAutoSec;
            if (FullConfig.MainConfig != null)
            {
                Config.MainConfig.IsAutoEvent = FullConfig.MainConfig.IsAutoEvent;
                Config.MainConfig.MinTimeBetweenEvents = FullConfig.MainConfig.MinTimeBetweenEvents;
                Config.MainConfig.MaxTimeBetweenEvents = FullConfig.MainConfig.MaxTimeBetweenEvents;
            }
            if (FullConfig.LootConfig != null)
            {
                if (Config.LootSettings == null) Config.LootSettings = new LootSettingsOptions();
                Config.LootSettings.LootFallsOnDestroy = FullConfig.LootConfig.DropLoot;
                Config.LootSettings.LootLossPercentOnDestroy = FullConfig.LootConfig.LootLossPercent;
                Config.LootSettings.ProhibitLootingWhenMoving = FullConfig.LootConfig.BlockLootingByMove;
                Config.LootSettings.ProhibitLootingWhenNpcsAlive = FullConfig.LootConfig.BlockLootingByNpcs;
                Config.LootSettings.ProhibitLootingWhenBradleyAlive = FullConfig.LootConfig.BlockLootingByBradleys;
                Config.LootSettings.ProhibitLootingWhenHeliAlive = FullConfig.LootConfig.BlockLootingByHeli;
            }
            if (FullConfig.MarkerConfig != null)
            {
                if (Config.MarkerConfig == null) Config.MarkerConfig = new MarkerConfig();
                Config.MarkerConfig.Enable = FullConfig.MarkerConfig.Enable;
                Config.MarkerConfig.UseShopMarker = FullConfig.MarkerConfig.UseShopMarker;
                Config.MarkerConfig.UseRingMarker = FullConfig.MarkerConfig.UseRingMarker;
                Config.MarkerConfig.Radius = FullConfig.MarkerConfig.Radius;
                Config.MarkerConfig.Alpha = FullConfig.MarkerConfig.Alpha;
                if (FullConfig.MarkerConfig.Color1 != null) Config.MarkerConfig.Color1 = FullConfig.MarkerConfig.Color1;
                if (FullConfig.MarkerConfig.Color2 != null) Config.MarkerConfig.Color2 = FullConfig.MarkerConfig.Color2;
            }
        }

        private void EnsureHarmonyConfigDir()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { }
        }

        private void SaveConfig()
        {
            if (FullConfig == null || string.IsNullOrEmpty(_configFilePath)) return;
            try
            {
                string json = JsonConvert.SerializeObject(FullConfig, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(_configFilePath, json);
                LogDebug("SaveConfig: wrote " + _configFilePath);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Convoy] Failed to save config: " + ex.Message);
            }
        }

        private void LogDebug(string message)
        {
            if (Config != null && Config.Debug)
                UnityEngine.Debug.Log("[Convoy DEBUG] " + message);
        }

        private void TryApplyFindPatch()
        {
            try
            {
                var harmony = new HarmonyLib.Harmony("com.facepunch.rust_dedicated.Convoy.find");
                if (!Patches.Patch_ConsoleSystem_Server_Find.TryApply(harmony))
                    LogDebug("ConsoleSystem.Index.Server.Find patch skipped (method not present). Commands use Dict/GlobalDict.");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Convoy] Find patch failed (non-fatal): " + ex.Message);
            }
        }

        private void RegisterCommands()
        {
            try
            {
                LogDebug("RegisterCommands: building Command objects");
                _convoystartCmd = new ConsoleSystem.Command
                {
                    Name = "convoystart",
                    FullName = "global.convoystart",
                    Variable = false,
                    ServerAdmin = true,
                    ServerUser = true,
                    AllowRunFromServer = true,
                    Call = CmdConvoyStart
                };
                _convoystopCmd = new ConsoleSystem.Command
                {
                    Name = "convoystop",
                    FullName = "global.convoystop",
                    Variable = false,
                    ServerAdmin = true,
                    ServerUser = true,
                    AllowRunFromServer = true,
                    Call = CmdConvoyStop
                };
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                LogDebug("RegisterCommands: Dict=" + (dict != null ? "ok" : "null") + " GlobalDict=" + (globalDict != null ? "ok" : "null"));
                if (dict != null)
                {
                    dict["global.convoystart"] = _convoystartCmd;
                    dict["global.convoystop"] = _convoystopCmd;
                    LogDebug("RegisterCommands: added to Dict");
                }
                if (globalDict != null)
                {
                    globalDict["convoystart"] = _convoystartCmd;
                    globalDict["convoystop"] = _convoystopCmd;
                    LogDebug("RegisterCommands: added to GlobalDict");
                }
                UnityEngine.Debug.Log("[Convoy] Commands registered: convoystart, convoystop (server console, chat /convoystart, or F1 convoystart)");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Convoy] Command registration failed: " + ex.Message);
                LogDebug("RegisterCommands: exception " + ex.ToString());
            }
        }

        private void UnregisterCommands()
        {
            try
            {
                if (ConsoleSystem.Index.Server.Dict != null)
                {
                    ConsoleSystem.Index.Server.Dict.Remove("global.convoystart");
                    ConsoleSystem.Index.Server.Dict.Remove("global.convoystop");
                }
                if (ConsoleSystem.Index.Server.GlobalDict != null)
                {
                    ConsoleSystem.Index.Server.GlobalDict?.Remove("convoystart");
                    ConsoleSystem.Index.Server.GlobalDict?.Remove("convoystop");
                }
            }
            catch { }
        }

        private void CmdConvoyStart(ConsoleSystem.Arg arg)
        {
            LogDebug("CmdConvoyStart: invoked. Connection=" + (arg.Connection != null));
            var player = arg.Connection?.player as BasePlayer;
            if (player != null && !player.IsAdmin)
            {
                arg.ReplyWith((Config?.Prefix ?? "[Convoy]") + " Only admins can start the convoy.");
                return;
            }

            if (EventLauncher.IsEventActive())
            {
                arg.ReplyWith((Config?.Prefix ?? "[Convoy]") + " A convoy event is already active.");
                return;
            }

            // Optional preset name argument.
            string presetName = null;
            try { if (arg.HasArgs(1)) presetName = arg.GetString(0); } catch { }

            bool ok = EventLauncher.DelayStartEvent(player, presetName);
            if (!ok && player == null)
                arg.ReplyWith((Config?.Prefix ?? "[Convoy]") + " Failed to start convoy (see console).");
            else if (ok && player == null)
                arg.ReplyWith((Config?.Prefix ?? "[Convoy]") + " Convoy event spawning.");
        }

        private void CmdConvoyStop(ConsoleSystem.Arg arg)
        {
            LogDebug("CmdConvoyStop: invoked");
            var player = arg.Connection?.player as BasePlayer;
            if (player != null && !player.IsAdmin)
            {
                arg.ReplyWith((Config?.Prefix ?? "[Convoy]") + " Only admins can stop the convoy.");
                return;
            }

            EventLauncher.StopEvent();
            DeleteMapMarkers();
            arg.ReplyWith((Config?.Prefix ?? "[Convoy]") + " Convoy stopped." + (Config?.MainConfig?.IsAutoEvent == true ? " Next event on timer." : ""));
        }

        private void CreateMapMarker(Vector3 position)
        {
            try
            {
                LogDebug("CreateMapMarker: position=" + FormatPosition(position) + " GameManager.server=" + (GameManager.server != null));
                if (GameManager.server == null)
                {
                    UnityEngine.Debug.LogWarning("[Convoy] CreateMapMarker: GameManager.server is null");
                    return;
                }
                var mc = Config?.MarkerConfig;
                if (mc == null || !mc.Enable)
                {
                    LogDebug("CreateMapMarker: MarkerConfig disabled, skipping markers");
                    return;
                }

                if (mc.UseRingMarker)
                {
                    var radius = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", position) as MapMarkerGenericRadius;
                    if (radius != null)
                    {
                        radius.enableSaving = false;
                        radius.Spawn();
                        radius.radius = mc.Radius > 0 ? mc.Radius : 0.2f;
                        radius.alpha = mc.Alpha;
                        if (mc.Color1 != null) radius.color1 = new Color(mc.Color1.R, mc.Color1.G, mc.Color1.B);
                        if (mc.Color2 != null) radius.color2 = new Color(mc.Color2.R, mc.Color2.G, mc.Color2.B);
                        radius.SendUpdate();
                        radius.SendNetworkUpdate();
                        lock (_mapMarkers) _mapMarkers.Add(radius);
                        LogDebug("CreateMapMarker: radius marker spawned. netId=" + (radius.net != null ? radius.net.ID.Value.ToString() : "?"));
                    }
                }

                if (mc.UseShopMarker)
                {
                    var vending = GameManager.server.CreateEntity("assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab", position) as VendingMachineMapMarker;
                    if (vending != null)
                    {
                        vending.enableSaving = false;
                        vending.Spawn();
                        vending.markerShopName = "Convoy (Harmony)";
                        vending.SendNetworkUpdate();
                        lock (_mapMarkers) _mapMarkers.Add(vending);
                        LogDebug("CreateMapMarker: vending marker spawned. netId=" + (vending.net != null ? vending.net.ID.Value.ToString() : "?"));
                    }
                    else
                        UnityEngine.Debug.LogWarning("[Convoy] CreateMapMarker: vending_mapmarker CreateEntity returned null");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Convoy] Create map marker failed: " + ex.Message);
                LogDebug("CreateMapMarker: exception " + ex.ToString());
            }
        }

        private void DeleteMapMarkers()
        {
            lock (_mapMarkers)
            {
                foreach (var e in _mapMarkers)
                {
                    if (e != null && !e.IsDestroyed)
                        e.Kill();
                }
                _mapMarkers.Clear();
            }
        }

        public NpcPresetEntry GetNpcPreset(string presetName)
        {
            if (string.IsNullOrEmpty(presetName) || Config?.NpcPresets == null) return null;
            foreach (var p in Config.NpcPresets)
                if (string.Equals(p.PresetName, presetName, StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }

        public CratePresetEntry GetCratePreset(string presetName)
        {
            if (string.IsNullOrEmpty(presetName) || Config?.CratePresets == null) return null;
            foreach (var p in Config.CratePresets)
                if (string.Equals(p.PresetName, presetName, StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }

        /// <summary>Used by patch: return our command when game's Server.Find returns null so server console finds convoystart/convoystop.</summary>
        public ConsoleSystem.Command GetConvoyCommand(string strName)
        {
            if (string.IsNullOrEmpty(strName) || _convoystartCmd == null || _convoystopCmd == null) return null;
            string n = strName.Trim().ToLowerInvariant();
            if (n == "global.convoystart" || n == "convoystart") return _convoystartCmd;
            if (n == "global.convoystop" || n == "convoystop") return _convoystopCmd;
            return null;
        }

        public bool ShouldBlockLootingConvoyCrate(BaseEntity targetEntity)
        {
            if (Config?.LootSettings == null || targetEntity?.net == null) return false;
            if (!ConvoyState.IsConvoyCrate((ulong)targetEntity.net.ID.Value)) return false;

            if (ConvoyState.IsMoving && Config.LootSettings.ProhibitLootingWhenMoving) return true;
            if (ConvoyState.NpcsAlive && Config.LootSettings.ProhibitLootingWhenNpcsAlive) return true;
            if (ConvoyState.BradleyAlive && Config.LootSettings.ProhibitLootingWhenBradleyAlive) return true;
            if (ConvoyState.HeliAlive && Config.LootSettings.ProhibitLootingWhenHeliAlive) return true;

            return false;
        }
    }
}
