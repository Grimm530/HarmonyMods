using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using Convoy.Patches;
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
        private string _configFilePath;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            GrimmCoreHurtRegistration.Register();
            LoadConfig();
            LogDebug("OnLoaded: config loaded, Debug=" + (Config?.Debug ?? false));
            ConvoyPathManager.ConfigProvider = () => Instance?.FullConfig;
            ConvoyPathManager.CustomRoutesBaseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            ConvoyState.Clear();
            RegisterCommands();
            try
            {
                var findHarmony = new HarmonyLib.Harmony("com.facepunch.rust_dedicated.Convoy.find");
                if (!Patches.Patch_ConsoleSystem_Server_Find.TryApply(findHarmony))
                    UnityEngine.Debug.LogWarning("[Convoy] Could not patch ConsoleSystem.Find(StringView); convoystart may fail.");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Convoy] Find patch failed: " + ex.Message);
            }
            StartAutoEventTimerIfEnabled();
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.StartCoroutine(StartPathCachingDelayed());
            UnityEngine.Debug.Log("[Convoy] Harmony mod loaded. convoystart/convoystop (server console or admin). Auto-event on timer per Main Setting. Convoy.json from legacy/config or HarmonyConfig.");
        }

        private IEnumerator StartPathCachingDelayed()
        {
            yield return new WaitForSeconds(8f);
            if (Instance != null && FullConfig?.PathConfig != null)
            {
                ConvoyPathManager.StartCachingRoutes();
                LogDebug("Path caching started (PathType=" + FullConfig.PathConfig.PathType + ").");
            }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            StopAutoEventTimer();
            UnregisterCommands();
            DeleteMapMarkers();
            ConvoyPathManager.OnPluginUnloaded();
            ConvoyPathManager.ConfigProvider = null;
            ConvoyState.Clear();
            GrimmCoreHurtRegistration.Unregister();
            Instance = null;
            UnityEngine.Debug.Log("[Convoy] Harmony mod unloaded.");
        }

        private bool IsEventActive()
        {
            lock (_mapMarkers) { return _mapMarkers.Count > 0; }
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
            if (Config?.MainConfig == null || !Config.MainConfig.IsAutoEvent) return;
            int min = Math.Max(60, Config.MainConfig.MinTimeBetweenEvents);
            int max = Math.Max(min, Config.MainConfig.MaxTimeBetweenEvents);
            LogDebug("StartAutoEventTimer: Min=" + min + " Max=" + max);
            if (ServerMgr.Instance != null)
                _autoEventCoroutine = ServerMgr.Instance.StartCoroutine(AutoEventCoroutine(min, max));
        }

        private void StopAutoEventTimer()
        {
            if (_autoEventCoroutine != null && ServerMgr.Instance != null)
            {
                ServerMgr.Instance.StopCoroutine(_autoEventCoroutine);
                _autoEventCoroutine = null;
            }
        }

        private IEnumerator AutoEventCoroutine(int minSec, int maxSec)
        {
            while (Instance != null && Config?.MainConfig?.IsAutoEvent == true)
            {
                float wait = UnityEngine.Random.Range(minSec, maxSec);
                LogDebug("AutoEvent: waiting " + wait + " sec until next start");
                yield return new WaitForSeconds(wait);
                if (Instance == null) break;
                if (IsEventActive())
                {
                    LogDebug("AutoEvent: event already active, skipping start");
                    continue;
                }
                StartConvoyEventMinimal();
                int duration = Config?.EventDurationAutoSec ?? 3600;
                if (duration <= 0)
                {
                    LogDebug("AutoEvent: event started, duration=0 (run until convoystop)");
                    yield break;
                }
                LogDebug("AutoEvent: event started, auto-stop in " + duration + " sec");
                yield return new WaitForSeconds(duration);
                if (Instance == null) break;
                DeleteMapMarkers();
                ConvoyState.Clear();
                UnityEngine.Debug.Log("[Convoy] Auto event ended. Next event in " + UnityEngine.Random.Range(minSec, maxSec) + " sec.");
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
                        try { FullConfig = JsonConvert.DeserializeObject<ConvoyPluginConfig>(json); } catch { FullConfig = null; }
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
                        SaveConfig();
                        LogDebug("LoadConfig: loaded from " + p + " Debug=" + Config.Debug + " IsAutoEvent=" + Config.MainConfig.IsAutoEvent);
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
            LogDebug("CmdConvoyStart: invoked. Connection=" + (arg.Connection != null) + " arg.FullString=" + arg.FullString.ToString());
            var player = arg.Connection?.player as BasePlayer;
            Vector3 position;
            if (player == null)
            {
                if (FullConfig?.PathConfig != null)
                {
                    ConvoyPathManager.GenerateNewPath();
                    if (ConvoyPathManager.CurrentPath != null && ConvoyPathManager.CurrentPath.StartPathPoint != null)
                        position = ConvoyPathManager.CurrentPath.StartPathPoint.Position;
                    else
                        position = GetDefaultEventPosition();
                }
                else
                    position = GetDefaultEventPosition();
                LogDebug("CmdConvoyStart: no player (server console) - position " + FormatPosition(position));
            }
            else
            {
                LogDebug("CmdConvoyStart: player=" + player.displayName + " userId=" + player.userID + " IsAdmin=" + player.IsAdmin);
                if (!player.IsAdmin)
                {
                    arg.ReplyWith((Config?.Prefix ?? "[Convoy]") + " Only admins can start the convoy.");
                    return;
                }
                position = player.transform != null ? player.transform.position : GetDefaultEventPosition();
            }

            DeleteMapMarkers();
            ConvoyState.Clear();

            LogDebug("CmdConvoyStart: creating map marker at " + position);
            CreateMapMarker(position);
            ConvoyState.SetConvoyState(true, false, false, false);

            string posStr = FormatPosition(position);
            string pathInfo = ConvoyPathManager.CurrentPath != null ? " Pathfinding OK." : "";
            string reply = player == null
                ? (Config?.Prefix ?? "[Convoy]") + " Convoy started (server). Map markers at " + posStr + "." + pathInfo
                : (Config?.Prefix ?? "[Convoy]") + " Convoy started. Map markers at " + posStr + ".";
            LogDebug("CmdConvoyStart: replying " + reply);
            arg.ReplyWith(reply);
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
            DeleteMapMarkers();
            ConvoyState.Clear();
            if (Config?.MainConfig?.IsAutoEvent == true)
                StartAutoEventTimerIfEnabled();
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
