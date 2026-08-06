using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConVar;
using Newtonsoft.Json;
using UnityEngine;
using Oxide.Ext.Chaos.UIFramework;

namespace TeleportGUI
{
    public partial class TeleportGUIMod : IHarmonyModHooks
    {
        public static TeleportGUIMod Instance { get; private set; }

        /// <summary>Used by vendored CommandCallbackHandler for Chaos UI button routing.</summary>
        public string Title => "TeleportGUI";

        /// <summary>Marker word carried by CUI button commands: "cui.endtest TELEPORTGUI &lt;action&gt;".</summary>
        public const string CuiMarker = "TELEPORTGUI";

        private TeleportGUIConfig _config;
        private TeleportGUIData _data;
        private TeleportGUIWarpData _warpData;
        private string _dataPath;
        private string _warpDataPath;
        private string _configPath;
        private readonly Dictionary<string, ConsoleSystem.Command> _registeredCommands = new Dictionary<string, ConsoleSystem.Command>();
        private static readonly Dictionary<ulong, Vector3> DeathLocations = new Dictionary<ulong, Vector3>();
        private readonly Dictionary<ulong, (string mode, int page, string search)> _uiState = new Dictionary<ulong, (string, int, string)>();
        private readonly Dictionary<ulong, Vector3> _pendingWarpPosition = new Dictionary<ulong, Vector3>();
        private readonly Dictionary<ulong, WarpForm> _pendingWarpForms = new Dictionary<ulong, WarpForm>();
        /// <summary>When set, OpenTeleportUI shows Create Home or Add Warp modal instead of main panel.</summary>
        private readonly Dictionary<ulong, string> _showingModal = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> _playersInDelayedTeleport = new Dictionary<ulong, string>();
        private readonly HashSet<ulong> _cancelTeleportRequested = new HashSet<ulong>();
        private readonly HashSet<string> _manualWarpChatCommands = new HashSet<string>(StringComparer.Ordinal);
        private const int UI_PER_PAGE = 8;

        private static readonly string[] BasePermissions =
        {
            "teleportgui.tp.use",
            "teleportgui.tp.tpcancel",
            "teleportgui.tp.tpback",
            "teleportgui.tp.tpback.admin",
            "teleportgui.tp.tphere",
            "teleportgui.tp.sleepers",
            "teleportgui.tp.autoaccept",
            "teleportgui.tp.location",
            "teleportgui.tp.grid",
            "teleportgui.tp.death",
            "teleportgui.tp.marker",
            "teleportgui.homes.use",
            "teleportgui.homes.back",
            "teleportgui.homes.back.admin",
            "teleportgui.homes.back.bypass",
            "teleportgui.homes.viewothershomes",
            "teleportgui.homes.deleteothershomes",
            "teleportgui.warps.use",
            "teleportgui.warps.back",
            "teleportgui.warps.back.admin",
            "teleportgui.warps.back.bypass",
            "teleportgui.warps.admin",
            "teleportgui.hideingui",
            "teleportgui.seeall",
            "teleportgui.admin"
        };

        private static double CurrentTime() => (double)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            LoadConfig();
            LoadData();
            MergeConfigWarpsIntoData();
            AssignHomeEntities();
            InitPermissions();
            PurgeOldUsers();
            RegisterCommands();
            RegisterCuiCommand();
            RegisterWarpChatCommands();
            TeleportGUILanguage.Initialize();
            TeleportGUIIntegrations.Initialize();
            InitializeMonumentWarps();
            SetupUIComponents();
            AppDomain.CurrentDomain.SetData("TeleportGUI_ApiType", typeof(TeleportGUIMod));
            UnityEngine.Debug.Log("[TeleportGUI] Harmony mod loaded. Config: HarmonyConfig/TeleportGUI.json. Data: HarmonyData/TeleportGUI/.");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            AppDomain.CurrentDomain.SetData("TeleportGUI_ApiType", null);
            ClearAllRequests();
            TeardownUIComponents();
            ShutdownMonumentWarps();
            TeleportGUILanguage.Shutdown();
            PermissionsBridge.Shutdown();
            UnregisterCuiCommand();
            UnregisterCommands();
            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p == null || !p.IsConnected) continue;
                DestroyTeleportUI(p);
                StopPopupDestroy(p);
                ChaosUI.Destroy(p, TPR_POPUP);
                ChaosUI.Destroy(p, TPP_POPUP);
            }
            _uiState.Clear();
            SaveData();
            Instance = null;
            UnityEngine.Debug.Log("[TeleportGUI] Harmony mod unloaded.");
        }

        private void InitPermissions()
        {
            var perms = new List<string>(BasePermissions);
            CollectVipPermissions(perms);
            CollectWarpPermissions(perms);
            PermissionsBridge.Initialize(perms);
        }

        private void CollectVipPermissions(List<string> perms)
        {
            void AddVip(Dictionary<string, int> map)
            {
                if (map == null) return;
                foreach (var key in map.Keys)
                {
                    if (string.IsNullOrEmpty(key)) continue;
                    string p = key.StartsWith("teleportgui.", StringComparison.OrdinalIgnoreCase)
                        ? key
                        : "teleportgui." + key;
                    if (!perms.Contains(p)) perms.Add(p);
                }
            }

            AddVip(_config?.Teleport?.Delay?.VIP);
            AddVip(_config?.Teleport?.Cooldown?.VIP);
            AddVip(_config?.Teleport?.Limits?.VIP);
            AddVip(_config?.Teleport?.Purchase?.VIP);
            AddVip(_config?.Home?.Delay?.VIP);
            AddVip(_config?.Home?.Cooldown?.VIP);
            AddVip(_config?.Home?.Limits?.VIP);
            AddVip(_config?.Home?.Purchase?.VIP);
            AddVip(_config?.Home?.MaxHomes?.VIP);
            AddVip(_config?.Warp?.Delay?.VIP);
            AddVip(_config?.Warp?.Cooldown?.VIP);
            AddVip(_config?.Warp?.Limits?.VIP);
            AddVip(_config?.Warp?.Purchase?.VIP);
        }

        private void CollectWarpPermissions(List<string> perms)
        {
            if (_warpData == null) return;
            foreach (var wp in _warpData.Values)
            {
                if (string.IsNullOrEmpty(wp?.Permission)) continue;
                string p = EnsureWarpPermission(wp.Permission);
                if (!perms.Contains(p)) perms.Add(p);
            }
        }

        private static string EnsureWarpPermission(string permission)
        {
            if (string.IsNullOrEmpty(permission)) return string.Empty;
            if (permission.StartsWith("teleportgui.", StringComparison.OrdinalIgnoreCase))
                return permission.ToLowerInvariant();
            return "teleportgui." + permission.ToLowerInvariant();
        }

        private bool HasPerm(BasePlayer player, string perm) =>
            PermissionsBridge.UserHasPermission(player, perm);

        private void LoadConfig()
        {
            var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var defaultPath = Path.Combine(serverRoot, "HarmonyConfig", "TeleportGUI.json");
            _configPath = defaultPath;
            if (File.Exists(defaultPath))
            {
                try
                {
                    var json = File.ReadAllText(defaultPath);
                    _config = JsonConvert.DeserializeObject<TeleportGUIConfig>(json);
                    if (_config == null) _config = new TeleportGUIConfig();
                    EnsureConfigDefaults();
                    return;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning("[TeleportGUI] Config load failed: " + ex.Message);
                }
            }

            _config = new TeleportGUIConfig();
            EnsureConfigDefaults();
            try
            {
                var dir = Path.GetDirectoryName(defaultPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(defaultPath, JsonConvert.SerializeObject(_config, Formatting.Indented));
            }
            catch { }
        }

        private void EnsureConfigDefaults()
        {
            if (_config == null) return;
            _config.Chat = _config.Chat ?? new TeleportGUIConfig.ChatOptions();
            _config.Teleport = _config.Teleport ?? new TeleportGUIConfig.TeleportOptions();
            _config.Home = _config.Home ?? new TeleportGUIConfig.HomeOptions();
            _config.Warp = _config.Warp ?? new TeleportGUIConfig.WarpOptions();
            _config.Admin = _config.Admin ?? new TeleportGUIConfig.AdminOptions();
            _config.UI = _config.UI ?? new TeleportGUIConfig.UIOptions();
            _config.UI.Colors = _config.UI.Colors ?? new TeleportGUIConfig.UIOptions.UIColors();
            _config.Conditions = _config.Conditions ?? new TeleportGUIConfig.TeleportConditions();

            if (_config.Teleport.CommandAliases == null || _config.Teleport.CommandAliases.Count == 0)
                _config.Teleport.CommandAliases = new List<string> { "tp", "tpr" };
            if (_config.Home.CommandAliases == null || _config.Home.CommandAliases.Count == 0)
                _config.Home.CommandAliases = new List<string> { "home", "sethome", "delhome", "deletehome" };
            if (_config.Warp.CommandAliases == null || _config.Warp.CommandAliases.Count == 0)
                _config.Warp.CommandAliases = new List<string> { "warp" };
            if (_config.TpBackCommandAliases == null || _config.TpBackCommandAliases.Count == 0)
                _config.TpBackCommandAliases = new List<string> { "tpback", "tpb", "back" };
            if (_config.DeathCommandAliases == null || _config.DeathCommandAliases.Count == 0)
                _config.DeathCommandAliases = new List<string> { "death", "tpdeath" };
        }

        private string GetDataFolder()
        {
            var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (_config != null && !string.IsNullOrWhiteSpace(_config.DataFolderPath))
            {
                var p = _config.DataFolderPath.Trim();
                if (Path.IsPathRooted(p)) return p;
                return Path.Combine(serverRoot, p);
            }
            return Path.Combine(serverRoot, "HarmonyData", "TeleportGUI");
        }

        private void LoadData()
        {
            var folder = GetDataFolder();
            _dataPath = Path.Combine(folder, "userdata.json");
            _warpDataPath = Path.Combine(folder, "warpdata.json");
            _data = new TeleportGUIData();
            _warpData = new TeleportGUIWarpData();

            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                if (File.Exists(_dataPath))
                {
                    var loaded = JsonConvert.DeserializeObject<TeleportGUIData>(File.ReadAllText(_dataPath));
                    if (loaded != null) _data = loaded;
                }
                else
                {
                    // One-time fallback: legacy TeleportGUI_Data.json
                    var legacy = Path.Combine(folder, "TeleportGUI_Data.json");
                    if (File.Exists(legacy))
                    {
                        var loaded = JsonConvert.DeserializeObject<TeleportGUIData>(File.ReadAllText(legacy));
                        if (loaded != null) _data = loaded;
                    }
                }

                if (File.Exists(_warpDataPath))
                {
                    var warps = JsonConvert.DeserializeObject<TeleportGUIWarpData>(File.ReadAllText(_warpDataPath));
                    if (warps != null) _warpData = warps;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[TeleportGUI] Data load failed: " + ex.Message);
            }

            if (_data.Users == null) _data.Users = new Dictionary<ulong, TeleportGUIData.UserData>();
            if (_warpData == null) _warpData = new TeleportGUIWarpData();
            // Keep WarpPoints mirror for interim UI/code paths.
            _data.WarpPoints = _warpData;

            if (_data.ShouldResetDailyUses())
            {
                foreach (var u in _data.Users.Values) u.ResetDailyUses();
                _data.MarkResetNow();
                SaveData();
            }
        }

        private void MergeConfigWarpsIntoData()
        {
            // Oxide schema stores monument warps in config and manual warps in warpdata.json.
            // Do not invent Outpost/Bandit into warpdata when using Oxide-format config.
        }

        public void SaveData()
        {
            try
            {
                var folder = GetDataFolder();
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                if (_data != null)
                {
                    // Avoid writing WarpPoints into userdata.json
                    var previous = _data.WarpPoints;
                    _data.WarpPoints = null;
                    File.WriteAllText(_dataPath, JsonConvert.SerializeObject(_data, Formatting.Indented));
                    _data.WarpPoints = previous ?? _warpData;
                }

                if (_warpData != null)
                    File.WriteAllText(_warpDataPath, JsonConvert.SerializeObject(_warpData, Formatting.Indented));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[TeleportGUI] Save failed: " + ex.Message);
            }
        }

        private void RegisterWarpChatCommands()
        {
            _manualWarpChatCommands.Clear();
            if (_warpData == null) return;
            foreach (var kvp in _warpData)
            {
                string cmdName = NormalizeWarpChatCommand(kvp.Value?.Command);
                if (string.IsNullOrEmpty(cmdName)) continue;
                if (_manualWarpChatCommands.Contains(cmdName)) continue;
                _manualWarpChatCommands.Add(cmdName);
                try
                {
                    var cmd = new ConsoleSystem.Command
                    {
                        Name = cmdName,
                        FullName = "global." + cmdName,
                        Variable = false,
                        ServerAdmin = false,
                        ServerUser = true,
                        Call = arg =>
                        {
                            var player = arg.Connection?.player as BasePlayer;
                            if (player == null) return;
                            CmdWarp(player, new[] { kvp.Key });
                        }
                    };
                    if (ConsoleSystem.Index.Server.Dict != null && !ConsoleSystem.Index.Server.Dict.ContainsKey("global." + cmdName))
                    {
                        ConsoleSystem.Index.Server.Dict["global." + cmdName] = cmd;
                        if (ConsoleSystem.Index.Server.GlobalDict != null)
                            ConsoleSystem.Index.Server.GlobalDict[cmdName] = cmd;
                        _registeredCommands[cmdName] = cmd;
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning("[TeleportGUI] Failed to register warp command /" + cmdName + ": " + ex.Message);
                }
            }
        }

        private static string NormalizeWarpChatCommand(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string s = raw.Trim();
            while (s.StartsWith("/", StringComparison.Ordinal))
                s = s.Substring(1).TrimStart();
            return string.IsNullOrEmpty(s) ? string.Empty : s.ToLowerInvariant();
        }

        private bool AdminsBypassLimits => _config?.AdminsBypass ?? false;
        private bool ShouldRecordDeath => _config?.RecordDeathLocation ?? true;

        private bool CanUse(BasePlayer player)
        {
            if (player == null || player.IsNpc) return false;
            if (AdminsBypassLimits && player.IsAdmin) return true;
            if (_config.AllowedSteamIds != null && _config.AllowedSteamIds.Count > 0 &&
                !_config.AllowedSteamIds.Contains(player.UserIDString) && !player.IsAdmin)
                return false;
            return HasPerm(player, "teleportgui.tp.use")
                   || HasPerm(player, "teleportgui.homes.use")
                   || HasPerm(player, "teleportgui.warps.use")
                   || HasPerm(player, "teleportgui.admin")
                   || player.IsAdmin;
        }

        private TeleportGUIData.UserData GetOrCreateUser(BasePlayer player)
        {
            if (!_data.Users.TryGetValue(player.userID, out var u))
                u = _data.Users[player.userID] = new TeleportGUIData.UserData();
            u.LastOnlineTime = CurrentTime();
            return u;
        }

        private List<string> GetTPAliases() => _config.Teleport?.CommandAliases ?? new List<string> { "tp", "tpr" };
        private List<string> GetHomeAliases() => _config.Home?.CommandAliases ?? new List<string> { "home", "sethome", "deletehome" };
        private List<string> GetWarpAliases() => _config.Warp?.CommandAliases ?? new List<string> { "warp" };

        private bool HasVipPermission(BasePlayer player, string permission)
        {
            if (string.IsNullOrWhiteSpace(permission)) return false;
            return HasPerm(player, permission.StartsWith("teleportgui.", StringComparison.OrdinalIgnoreCase)
                ? permission
                : "teleportgui." + permission);
        }

        private int GetTPDelay(BasePlayer player = null) => _config.Teleport?.Delay?.GetLowestOption(p => HasVipPermission(player, p)) ?? 5;
        private int GetTPCooldown(BasePlayer player = null) => _config.Teleport?.Cooldown?.GetLowestOption(p => HasVipPermission(player, p)) ?? 300;
        private int GetTPDailyLimit(BasePlayer player = null) => _config.Teleport?.Limits?.GetHighestOption(p => HasVipPermission(player, p)) ?? 10;
        private int GetMaxHomes(BasePlayer player = null) => _config.Home?.MaxHomes?.GetHighestOption(p => HasVipPermission(player, p)) ?? 5;
        private int GetHomeDelay(BasePlayer player = null) => _config.Home?.Delay?.GetLowestOption(p => HasVipPermission(player, p)) ?? 5;
        private int GetHomeCooldown(BasePlayer player = null) => _config.Home?.Cooldown?.GetLowestOption(p => HasVipPermission(player, p)) ?? 60;
        private int GetHomeDailyLimit(BasePlayer player = null) => _config.Home?.Limits?.GetHighestOption(p => HasVipPermission(player, p)) ?? 0;
        private int GetWarpDelay(BasePlayer player = null) => _config.Warp?.Delay?.GetLowestOption(p => HasVipPermission(player, p)) ?? 5;
        private int GetWarpCooldown(BasePlayer player = null) => _config.Warp?.Cooldown?.GetLowestOption(p => HasVipPermission(player, p)) ?? 120;
        private int GetWarpDailyLimit(BasePlayer player = null) => _config.Warp?.Limits?.GetHighestOption(p => HasVipPermission(player, p)) ?? 0;

        private void RegisterCommands()
        {
            var allAliases = new List<string>();
            allAliases.AddRange(GetTPAliases());
            allAliases.AddRange(GetHomeAliases());
            allAliases.AddRange(GetWarpAliases());
            if (_config.TpBackCommandAliases != null) allAliases.AddRange(_config.TpBackCommandAliases);
            if (_config.DeathCommandAliases != null) allAliases.AddRange(_config.DeathCommandAliases);
            allAliases.AddRange(new[]
            {
                "tpsave", "tpl", "tpllist", "tpr", "tprhere", "tpa", "tpd", "tpc",
                "tpgrid", "tpmarker", "tpdeath", "listhomes", "homec", "homeback",
                "warpback", "warpadd", "warpremove", "tpgui", "homegui", "warpgui",
                "tpadmin", "showmonumentbounds", "showgeneratedwarps"
            });

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in allAliases)
            {
                var key = name.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(key) || seen.Contains(key)) continue;
                seen.Add(key);
                try
                {
                    var cmd = new ConsoleSystem.Command
                    {
                        Name = key,
                        FullName = "global." + key,
                        Variable = false,
                        ServerAdmin = false,
                        ServerUser = true,
                        Call = HandleConsoleCommand
                    };
                    if (ConsoleSystem.Index.Server.Dict != null && !ConsoleSystem.Index.Server.Dict.ContainsKey("global." + key))
                    {
                        ConsoleSystem.Index.Server.Dict["global." + key] = cmd;
                        if (ConsoleSystem.Index.Server.GlobalDict != null)
                            ConsoleSystem.Index.Server.GlobalDict[key] = cmd;
                        _registeredCommands[key] = cmd;
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning("[TeleportGUI] Failed to register command " + key + ": " + ex.Message);
                }
            }
        }

        private void UnregisterCommands()
        {
            foreach (var kv in _registeredCommands)
            {
                try
                {
                    if (ConsoleSystem.Index.Server.Dict != null)
                        ConsoleSystem.Index.Server.Dict.Remove("global." + kv.Key);
                    if (ConsoleSystem.Index.Server.GlobalDict != null)
                        ConsoleSystem.Index.Server.GlobalDict?.Remove(kv.Key);
                }
                catch { }
            }
            _registeredCommands.Clear();
        }

        private ConsoleSystem.Command _cuiCommand;

        private void RegisterCuiCommand()
        {
            try
            {
                _cuiCommand = new ConsoleSystem.Command
                {
                    Name = "teleportgui.cui",
                    FullName = "global.teleportgui.cui",
                    Variable = false,
                    ServerAdmin = false,
                    ServerUser = true,
                    Client = true,
                    Call = OnCuiCommand
                };
                if (ConsoleSystem.Index.Server.Dict != null && !ConsoleSystem.Index.Server.Dict.ContainsKey("global.teleportgui.cui"))
                {
                    ConsoleSystem.Index.Server.Dict["global.teleportgui.cui"] = _cuiCommand;
                    if (ConsoleSystem.Index.Server.GlobalDict != null)
                        ConsoleSystem.Index.Server.GlobalDict["teleportgui.cui"] = _cuiCommand;
                }
            }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("[TeleportGUI] Cui command registration failed: " + ex.Message); }
        }

        private void UnregisterCuiCommand()
        {
            try
            {
                if (ConsoleSystem.Index.Server.Dict != null)
                    ConsoleSystem.Index.Server.Dict.Remove("global.teleportgui.cui");
                if (ConsoleSystem.Index.Server.GlobalDict != null)
                    ConsoleSystem.Index.Server.GlobalDict?.Remove("teleportgui.cui");
            }
            catch { }
        }

        private void OnCuiCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null || player.IsNpc) return;
            var args = arg.Args.AsStringArray();
            string cmd = args.Length > 0 ? (args[0] ?? "").Trim().ToLowerInvariant() : "";
            HandleCuiCommand(player, cmd, args.Length > 1 ? args.Skip(1).ToArray() : Array.Empty<string>());
        }

        /// <summary>
        /// Entry point for CUI buttons routed through the patched <c>cui.endtest TELEPORTGUI …</c> marker.
        /// Supports Chaos <c>teleportgui.callback</c> identifiers and legacy flat action strings.
        /// </summary>
        public void HandleCuiEndtest(ConsoleSystem.Arg arg, Array a)
        {
            var player = arg?.Connection?.player as BasePlayer ?? arg?.Player();
            if (player == null || player.IsNpc || a == null) return;

            if (a.Length >= 2)
            {
                string second = a.GetValue(1)?.ToString() ?? string.Empty;
                if (second.Equals("teleportgui.callback", StringComparison.OrdinalIgnoreCase) ||
                    second.StartsWith("teleportgui.callback ", StringComparison.OrdinalIgnoreCase))
                {
                    RouteChaosCallback(arg, a);
                    return;
                }
            }

            // Legacy flat actions: a[1] is the action; remainder are arguments.
            string cmd = a.Length > 1 ? (a.GetValue(1)?.ToString() ?? "").Trim().ToLowerInvariant() : "";
            string[] rest = a.Length > 2 ? new string[a.Length - 2] : Array.Empty<string>();
            for (int i = 2; i < a.Length; i++)
                rest[i - 2] = a.GetValue(i)?.ToString() ?? string.Empty;
            HandleCuiCommand(player, cmd, rest);
        }

        private void RouteChaosCallback(ConsoleSystem.Arg arg, Array a)
        {
            if (m_CallbackHandler == null) return;
            var sb = new System.Text.StringBuilder("teleportgui.callback");
            int start = 1;
            if (a.Length >= 2)
            {
                string second = a.GetValue(1)?.ToString() ?? "";
                if (second.Equals("teleportgui.callback", StringComparison.OrdinalIgnoreCase))
                    start = 2;
                else if (second.StartsWith("teleportgui.callback", StringComparison.OrdinalIgnoreCase))
                {
                    start = 2;
                    if (second.Length > "teleportgui.callback".Length)
                    {
                        string rest = second.Substring("teleportgui.callback".Length).Trim();
                        if (!string.IsNullOrEmpty(rest))
                        {
                            sb.Append(' ');
                            sb.Append(rest);
                        }
                    }
                }
            }

            for (int i = start; i < a.Length; i++)
            {
                sb.Append(' ');
                string s = a.GetValue(i)?.ToString() ?? string.Empty;
                if (s.IndexOfAny(new[] { ' ', '"' }) >= 0)
                    sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
                else
                    sb.Append(s);
            }

            try
            {
                var opt = ConsoleSystem.Option.Server.Quiet();
                if (arg.Connection != null)
                    opt = opt.FromConnection(arg.Connection);
                var uiArg = new ConsoleSystem.Arg(opt, sb.ToString());
                m_CallbackHandler.HandleCallback(uiArg);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[TeleportGUI] teleportgui.callback: " + ex.Message);
            }
        }

        private void HandleCuiCommand(BasePlayer player, string cmd, string[] args)
        {
            if (string.IsNullOrEmpty(cmd)) return;
            if (cmd == "close")
            {
                DestroyTeleportUI(player);
                _uiState.Remove(player.userID);
                _pendingWarpPosition.Remove(player.userID);
                _pendingWarpForms.Remove(player.userID);
                _showingModal.Remove(player.userID);
                return;
            }
            if (cmd == "settings.open")
            {
                var user = GetOrCreateUser(player);
                DestroyTeleportUI(player);
                var uiMode = _uiState.TryGetValue(player.userID, out var st) ? ParseUiMode(st.mode) : UiMode.Teleport;
                ShowTeleportSettingsUI(player, user, uiMode);
                return;
            }
            if (cmd == "settings.close")
            {
                OpenTeleportUI(player, "teleport");
                return;
            }
            if (cmd.StartsWith("settings.", StringComparison.Ordinal))
            {
                var user = GetOrCreateUser(player);
                string setting = cmd.Substring("settings.".Length);
                if (setting == "sleepers")
                {
                    if (!HasPerm(player, "teleportgui.tp.sleepers")) return;
                    user.ShowSleepers = !user.ShowSleepers;
                }
                else
                {
                    if (!HasPerm(player, "teleportgui.tp.autoaccept")) return;
                    TeleportGUIData.UserData.AutoAcceptEnum flag;
                    switch (setting)
                    {
                        case "aaclan": flag = TeleportGUIData.UserData.AutoAcceptEnum.Clans; break;
                        case "aafriend": flag = TeleportGUIData.UserData.AutoAcceptEnum.Friends; break;
                        case "aateam": flag = TeleportGUIData.UserData.AutoAcceptEnum.Teams; break;
                        case "aaall": flag = TeleportGUIData.UserData.AutoAcceptEnum.All; break;
                        default: return;
                    }
                    if ((user.AutoAccept & flag) != 0) user.AutoAccept &= ~flag;
                    else user.AutoAccept |= flag;
                }
                SaveData();
                HandleCuiCommand(player, "settings.open", Array.Empty<string>());
                return;
            }
            if (cmd == "popup.accept")
            {
                CmdTpAccept(player);
                return;
            }
            if (cmd == "popup.decline")
            {
                if (_incomingRequests.ContainsKey(player.userID)) CmdTpDecline(player);
                else if (_outgoingRequests.ContainsKey(player.userID)) CmdTpCancel(player);
                return;
            }
            if (cmd == "popup.timeout")
            {
                if (_incomingRequests.TryGetValue(player.userID, out var incoming)) ClearRequest(incoming, refund: true);
                else if (_outgoingRequests.TryGetValue(player.userID, out var outgoing)) ClearRequest(outgoing, refund: true);
                return;
            }
            if (cmd == "search")
            {
                string search = args != null && args.Length > 0 ? string.Join(" ", args).Trim() : "";
                if (!_uiState.TryGetValue(player.userID, out var state))
                    state = ("teleport", 0, "");
                _uiState[player.userID] = (state.mode, 0, search);
                OpenTeleportUI(player, state.mode);
                return;
            }
            if (cmd == "addhome.open")
            {
                _showingModal[player.userID] = "addhome";
                OpenTeleportUI(player, "home");
                return;
            }
            if (cmd == "addhome.cancel")
            {
                _showingModal.Remove(player.userID);
                OpenTeleportUI(player, "home");
                return;
            }
            if (cmd == "addwarp.open")
            {
                if (!HasPerm(player, "teleportgui.warps.admin")) return;
                _pendingWarpPosition[player.userID] = player.transform.position;
                _pendingWarpForms[player.userID] = new WarpForm();
                _showingModal[player.userID] = "addwarp";
                OpenTeleportUI(player, "warp");
                return;
            }
            if (cmd == "addwarp.cancel")
            {
                _showingModal.Remove(player.userID);
                _pendingWarpPosition.Remove(player.userID);
                _pendingWarpForms.Remove(player.userID);
                OpenTeleportUI(player, "warp");
                return;
            }
            if (cmd.StartsWith("warpfield.", StringComparison.Ordinal))
            {
                if (!HasPerm(player, "teleportgui.warps.admin")) return;
                if (!_pendingWarpForms.TryGetValue(player.userID, out var form))
                    form = _pendingWarpForms[player.userID] = new WarpForm();
                string value = args != null && args.Length > 0 ? string.Join(" ", args).Trim() : string.Empty;
                switch (cmd.Substring("warpfield.".Length))
                {
                    case "name": form.Name = value; break;
                    case "permission": form.Permission = value; break;
                    case "command": form.Command = value; break;
                    default: return;
                }
                _showingModal[player.userID] = "addwarp";
                OpenTeleportUI(player, "warp");
                return;
            }
            if (cmd == "addwarp.save")
            {
                if (!HasPerm(player, "teleportgui.warps.admin")) return;
                if (!_pendingWarpForms.TryGetValue(player.userID, out var form) || string.IsNullOrWhiteSpace(form.Name))
                {
                    SendMessage(player, "Enter a warp name.");
                    return;
                }
                string warpName = form.Name.Trim();
                if (_warpData.ContainsKey(warpName))
                {
                    SendMessage(player, "A warp called '" + warpName + "' already exists.");
                    return;
                }
                Vector3 savedPosition = _pendingWarpPosition.TryGetValue(player.userID, out var pendingPosition)
                    ? pendingPosition
                    : player.transform.position;
                string permission = (form.Permission ?? string.Empty).Trim().ToLowerInvariant();
                string command = NormalizeWarpChatCommand(form.Command);
                var warp = new TeleportGUIData.WarpPoint
                {
                    Position = savedPosition,
                    Permission = permission,
                    Command = command
                };
                _warpData[warpName] = warp;
                _data.WarpPoints = _warpData;
                if (!string.IsNullOrEmpty(permission))
                    PermissionsBridge.RegisterPermission(EnsureWarpPermission(permission));
                SaveData();
                RegisterWarpChatCommands();
                SendMessage(player, "Warp '" + warpName + "' added.");
                _pendingWarpForms.Remove(player.userID);
                _pendingWarpPosition.Remove(player.userID);
                _showingModal.Remove(player.userID);
                OpenTeleportUI(player, "warp");
                return;
            }
            if (cmd.StartsWith("deletehome."))
            {
                var name = cmd.Substring(11).Replace("_", " ").Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    CmdHome(player, "deletehome", new[] { name });
                    OpenTeleportUI(player, "home");
                }
                return;
            }
            if (cmd.StartsWith("deletewarp."))
            {
                if (!player.IsAdmin) return;
                var name = cmd.Substring(11).Replace("_", " ").Trim();
                if (!string.IsNullOrEmpty(name) && _data.WarpPoints != null && _data.WarpPoints.Remove(name))
                {
                    SaveData();
                    SendMessage(player, "Warp '" + name + "' removed.");
                }
                OpenTeleportUI(player, "warp");
                return;
            }
            if (cmd == "mode.teleport" || cmd == "mode.home" || cmd == "mode.warp")
            {
                var mode = cmd.Replace("mode.", "");
                if (!_uiState.TryGetValue(player.userID, out var state))
                    state = (mode, 0, "");
                _uiState[player.userID] = (mode, 0, state.search);
                OpenTeleportUI(player, mode);
                return;
            }
            if (cmd == "prev")
            {
                if (!_uiState.TryGetValue(player.userID, out var state)) return;
                if (state.page <= 0) return;
                _uiState[player.userID] = (state.mode, state.page - 1, state.search);
                OpenTeleportUI(player, state.mode);
                return;
            }
            if (cmd == "next")
            {
                if (!_uiState.TryGetValue(player.userID, out var state)) return;
                _uiState[player.userID] = (state.mode, state.page + 1, state.search);
                OpenTeleportUI(player, state.mode);
                return;
            }
            if (cmd.StartsWith("tpr."))
            {
                var userIdStr = cmd.Substring(4).Trim();
                DestroyTeleportUI(player);
                _uiState.Remove(player.userID);
                CmdTpr(player, new[] { userIdStr }, false);
                return;
            }
            if (cmd.StartsWith("tphere."))
            {
                var userIdStr = cmd.Substring(7).Trim();
                DestroyTeleportUI(player);
                _uiState.Remove(player.userID);
                CmdTpr(player, new[] { userIdStr }, true);
                return;
            }
            if (cmd.StartsWith("home."))
            {
                var name = cmd.Substring(5).Replace("_", " ").Trim();
                DestroyTeleportUI(player);
                _uiState.Remove(player.userID);
                CmdHome(player, "home", new[] { name });
                return;
            }
            if (cmd.StartsWith("warp."))
            {
                var name = cmd.Substring(5).Replace("_", " ").Trim();
                DestroyTeleportUI(player);
                _uiState.Remove(player.userID);
                CmdWarp(player, new[] { name });
                return;
            }
            if (cmd == "promptaddwarp")
            {
                if (!player.IsAdmin) return;
                _pendingWarpPosition[player.userID] = player.transform.position;
                OpenTeleportUI(player, "warp");
                return;
            }
            if (cmd == "addwarp")
            {
                if (!player.IsAdmin) return;
                string name = args != null && args.Length > 0 ? string.Join(" ", args).Trim() : "";
                if (string.IsNullOrEmpty(name))
                {
                    SendMessage(player, "Enter a warp name in the field and press Enter.");
                    return;
                }
                name = SanitizeWarpName(name);
                if (string.IsNullOrEmpty(name))
                {
                    SendMessage(player, "Invalid warp name.");
                    return;
                }
                Vector3 pos = _pendingWarpPosition.TryGetValue(player.userID, out var p) ? p : player.transform.position;
                _pendingWarpPosition.Remove(player.userID);
                _showingModal.Remove(player.userID);
                if (_data.WarpPoints == null) _data.WarpPoints = new Dictionary<string, TeleportGUIData.WarpPoint>();
                if (_warpData == null) _warpData = new TeleportGUIWarpData();
                var warp = TeleportGUIData.WarpPoint.FromVector3(pos);
                _warpData[name] = warp;
                _data.WarpPoints[name] = warp;
                SaveData();
                SendMessage(player, "Warp '" + name + "' added at your location.");
                OpenTeleportUI(player, "warp");
                return;
            }
            if (cmd == "addhome")
            {
                string name = args != null && args.Length > 0 ? string.Join(" ", args).Trim() : "";
                if (string.IsNullOrEmpty(name)) name = "home";
                else name = name.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    SendMessage(player, "Enter a home name in the field and press Enter.");
                    return;
                }
                _showingModal.Remove(player.userID);
                CmdHome(player, "sethome", new[] { name });
                OpenTeleportUI(player, "home");
                return;
            }
        }

        private static string SanitizeWarpName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            return new string(name.Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-').ToArray()).Trim();
        }

        public void OpenTeleportUI(BasePlayer player, string mode = "teleport")
        {
            if (player == null || player.IsNpc || !CanUse(player)) return;
            if (_config.UI?.DisableUI == true)
            {
                SendMessage(player, "Teleport GUI is disabled. Use chat commands: /tp <player>, /home <name>, /warp <name>.");
                return;
            }

            if (_showingModal.TryGetValue(player.userID, out var modal))
            {
                if (modal == "addhome")
                {
                    SaveHomeUI(player);
                    return;
                }
                if (modal == "addwarp")
                {
                    _pendingWarpForms.TryGetValue(player.userID, out var form);
                    SaveWarpUI(player, form?.Name ?? string.Empty, form?.Permission ?? string.Empty, form?.Command ?? string.Empty);
                    return;
                }
            }

            if (!_uiState.TryGetValue(player.userID, out var state))
                _uiState[player.userID] = (mode, 0, string.Empty);
            else
                _uiState[player.userID] = (mode, state.page, state.search);

            ShowTeleportUI(player, mode);
        }

        private string GetDailyLimitOrCooldownText(BasePlayer player, string mode)
        {
            var user = GetOrCreateUser(player);
            int limit = 0;
            int used = 0;
            double cooldownEnd = 0;
            if (mode == "teleport")
            {
                limit = GetTPDailyLimit(player);
                used = user.TPUsesToday;
                cooldownEnd = user.TPCooldownUntil;
            }
            else if (mode == "home")
            {
                limit = GetHomeDailyLimit(player);
                used = user.HomeUsesToday;
                cooldownEnd = user.HomeCooldownUntil;
            }
            else if (mode == "warp")
            {
                limit = GetWarpDailyLimit(player);
                used = user.WarpUsesToday;
                cooldownEnd = user.WarpCooldownUntil;
            }
            double now = CurrentTime();
            if (cooldownEnd > now)
            {
                int secs = (int)(cooldownEnd - now);
                return "Cooldown: " + secs + "s";
            }
            if (limit > 0)
            {
                int left = limit - used;
                if (left < 0) left = 0;
                return "Daily remaining: " + left;
            }
            if (limit == 0 && (mode == "teleport" || mode == "home" || mode == "warp"))
                return "Daily: Unlimited";
            return null;
        }

        private void PurgeOldUsers()
        {
            int days = _config.PurgeDays;
            if (days <= 0) return;
            double cutoff = CurrentTime() - (days * 86400.0);
            var toRemove = _data.Users.Where(kv => (kv.Value?.LastOnlineTime ?? 0) < cutoff).Select(kv => kv.Key).ToList();
            foreach (var id in toRemove)
                _data.Users.Remove(id);
            if (toRemove.Count > 0)
                SaveData();
        }

        private void HandleConsoleCommand(ConsoleSystem.Arg arg)
        {
            var cmd = arg.cmd?.Name?.Trim().ToLowerInvariant();
            if (string.Equals(cmd, "tpadmin", StringComparison.Ordinal))
            {
                HandleTpAdmin(arg);
                return;
            }
            var player = arg.Connection?.player as BasePlayer;
            if (player == null || player.IsNpc) return;
            if (string.IsNullOrEmpty(cmd)) return;
            var args = arg.Args.AsStringArray();
            RunCommand(player, cmd, args);
        }

        /// <summary>Called from Chat patch and from console. cmd = first word (e.g. tp, home), args = rest.</summary>
        public bool RunCommand(BasePlayer player, string cmd, string[] args)
        {
            if (player == null || player.IsNpc) return false;
            cmd = (cmd ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(cmd)) return false;

            if (!CanUse(player))
            {
                SendMessage(player, "You don't have permission to use teleport commands.");
                return true;
            }

            // Fixed source commands (handled before the configurable tp/home/warp aliases).
            switch (cmd)
            {
                case "tpsave": CmdTpSave(player, args); return true;
                case "tpl": CmdTpl(player, args); return true;
                case "tpllist": CmdTplList(player); return true;
                case "tpr": CmdTpr(player, args, false); return true;
                case "tprhere": CmdTpr(player, args, true); return true;
                case "tpa": CmdTpAccept(player); return true;
                case "tpd": CmdTpDecline(player); return true;
                case "tpc": CmdTpCancel(player); return true;
                case "tpgrid": CmdTpGrid(player, args); return true;
                case "tpmarker": CmdTpMarker(player, args); return true;
                case "tpdeath": CmdDeath(player, args); return true;
                case "listhomes": CmdListHomes(player, args); return true;
                case "homec": CmdHomeCancel(player); return true;
                case "homeback": CmdHomeBack(player, args); return true;
                case "warpback": CmdWarpBack(player, args); return true;
                case "warpadd": CmdWarpAdd(player, args); return true;
                case "warpremove": CmdWarpRemove(player, args); return true;
                case "tpgui": OpenTeleportUI(player, "teleport"); return true;
                case "homegui": OpenTeleportUI(player, "home"); return true;
                case "warpgui": OpenTeleportUI(player, "warp"); return true;
                case "showmonumentbounds":
                    if (!player.IsAdmin) { SendMessage(player, "You don't have permission."); return true; }
                    float boundsTime = 30f;
                    if (args != null && args.Length > 0) float.TryParse(args[0], out boundsTime);
                    ShowMonumentBounds(player, boundsTime > 0f ? boundsTime : 30f);
                    return true;
                case "showgeneratedwarps":
                    if (!player.IsAdmin) { SendMessage(player, "You don't have permission."); return true; }
                    ShowGeneratedWarps(player);
                    return true;
                case "tpadmin":
                    HandleTpAdmin(player, args, message => SendMessage(player, message));
                    return true;
            }

            if (IsAlias(cmd, GetTPAliases()))
            {
                if (args == null || args.Length == 0)
                {
                    OpenTeleportUI(player, "teleport");
                    return true;
                }
                CmdTP(player, args);
                return true;
            }
            if (IsAlias(cmd, GetHomeAliases()))
            {
                if (string.Equals(cmd, "home", StringComparison.OrdinalIgnoreCase) && (args == null || args.Length == 0))
                {
                    OpenTeleportUI(player, "home");
                    return true;
                }
                CmdHome(player, cmd, args);
                return true;
            }
            if (IsAlias(cmd, GetWarpAliases()))
            {
                if (args == null || args.Length == 0)
                {
                    OpenTeleportUI(player, "warp");
                    return true;
                }
                CmdWarp(player, args);
                return true;
            }
            if (IsAlias(cmd, _config.TpBackCommandAliases))
            {
                CmdTpBack(player, args);
                return true;
            }
            if (IsAlias(cmd, _config.DeathCommandAliases))
            {
                CmdDeath(player, args);
                return true;
            }
            return false;
        }

        private static bool IsAlias(string cmd, List<string> aliases)
        {
            if (aliases == null) return false;
            foreach (var a in aliases)
                if (string.Equals((a ?? "").Trim(), cmd, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void CmdTP(BasePlayer player, string[] args)
        {
            if (!HasPerm(player, "teleportgui.tp.use"))
            {
                SendMessage(player, "You don't have permission.");
                return;
            }
            if (!player.IsAdmin)
            {
                SendMessage(player, "Use /tpr <player> to request a teleport.");
                return;
            }
            if (args == null || args.Length == 0)
            {
                SendMessage(player, "Usage: /tp <player name or id>");
                return;
            }
            if (args.Length >= 3 &&
                float.TryParse(args[0], out float x) &&
                float.TryParse(args[1], out float y) &&
                float.TryParse(args[2], out float z))
            {
                DoTeleport(player, new Vector3(x, y, z));
                SendMessage(player, $"Teleported to {x:N1}, {y:N1}, {z:N1}.");
                return;
            }
            if (args.Length == 2)
            {
                var source = FindPlayer(args[0]);
                var destination = FindPlayer(args[1]);
                if (source == null || destination == null)
                {
                    SendMessage(player, "Player not found.");
                    return;
                }
                DoTeleport(source, destination.transform.position);
                SendMessage(player, "Teleported " + source.displayName + " to " + destination.displayName + ".");
                return;
            }
            var target = FindPlayer(args[0]);
            if (target == null)
            {
                SendMessage(player, "Player not found.");
                return;
            }
            if (target == player)
            {
                SendMessage(player, "You cannot teleport to yourself.");
                return;
            }
            if (!target.IsConnected)
            {
                SendMessage(player, "That player is not connected.");
                return;
            }

            var user = GetOrCreateUser(player);
            var now = CurrentTime();
            if (!AdminsBypassLimits || !player.IsAdmin)
            {
                if (GetTPCooldown(player) > 0 && user.IsOnTPCooldown(now))
                {
                    SendMessage(player, "TP is on cooldown.");
                    return;
                }
                if (GetTPDailyLimit(player) > 0 && user.TPUsesToday >= GetTPDailyLimit(player))
                {
                    SendMessage(player, "Daily TP limit reached.");
                    return;
                }
            }

            var dest = target.transform.position;
            int delaySec = (_config.Admin?.Instant == true && player.IsAdmin) ? 0 : Math.Max(0, GetTPDelay(player));
            var delay = (AdminsBypassLimits && player.IsAdmin) ? 0 : delaySec;
            if (delay > 0)
            {
                SendMessage(player, "Teleporting in " + delay + " seconds...");
                ServerMgr.Instance?.StartCoroutine(DelayedTeleport(player, dest, delay, () =>
                {
                    user.TPUsesToday++;
                    user.TPCooldownUntil = CurrentTime() + GetTPCooldown(player);
                }, "teleport"));
            }
            else
            {
                DoTeleport(player, dest);
                user.TPUsesToday++;
                user.TPCooldownUntil = CurrentTime() + GetTPCooldown(player);
            }
            SaveData();
        }

        private void CmdHome(BasePlayer player, string subCmd, string[] args)
        {
            if (!HasPerm(player, "teleportgui.homes.use"))
            {
                SendMessage(player, "You don't have permission.");
                return;
            }
            var user = GetOrCreateUser(player);
            var isSet = string.Equals(subCmd, "sethome", StringComparison.OrdinalIgnoreCase);
            var isDelete = string.Equals(subCmd, "deletehome", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(subCmd, "delhome", StringComparison.OrdinalIgnoreCase);
            if (args == null) args = Array.Empty<string>();

            if (string.Equals(subCmd, "home", StringComparison.OrdinalIgnoreCase) && args.Length > 1 &&
                string.Equals(args[0], "add", StringComparison.OrdinalIgnoreCase))
            {
                isSet = true;
                args = args.Skip(1).ToArray();
            }
            else if (string.Equals(subCmd, "home", StringComparison.OrdinalIgnoreCase) && args.Length > 1 &&
                     string.Equals(args[0], "remove", StringComparison.OrdinalIgnoreCase))
            {
                isDelete = true;
                args = args.Skip(1).ToArray();
            }

            if (isSet)
            {
                if (_config.Home?.SleepingBags?.DisableSetHomeCommand == true)
                {
                    SendMessage(player, "The set home command is disabled.");
                    return;
                }
                var name = args.Length > 0 ? string.Join(" ", args).Trim() : "home";
                if (string.IsNullOrEmpty(name)) name = "home";
                if (user.Homes.ContainsKey(name))
                {
                    SendMessage(player, "A home called '" + name + "' already exists.");
                    return;
                }
                var max = AdminsBypassLimits && player.IsAdmin ? 999 : GetMaxHomes(player);
                if (user.Homes.Count >= max && !user.Homes.ContainsKey(name))
                {
                    SendMessage(player, "Max homes (" + max + ") reached.");
                    return;
                }
                bool isOnTugBoat = player.GetParentEntity() is Tugboat;
                if (isOnTugBoat && _config.Home?.AllowSetHomeOnTugboat == false)
                {
                    SendMessage(player, "You cannot create homes on tugboats.");
                    return;
                }
                if (!CanSetHomeAtCurrentPosition(player, isOnTugBoat, user, out string setHomeError))
                {
                    if (!string.IsNullOrEmpty(setHomeError)) SendMessage(player, setHomeError);
                    return;
                }
                if (isOnTugBoat && player.GetParentEntity() is Tugboat tugboat)
                {
                    user.Homes[name] = new TeleportGUIData.UserData.HomePoint
                    {
                        Position = player.transform.position,
                        Offset = tugboat.transform.InverseTransformPoint(player.transform.position),
                        EntityID = tugboat.net?.ID.Value ?? 0UL
                    };
                }
                else
                {
                    user.Homes[name] = TeleportGUIData.UserData.HomePoint.FromVector3(player.transform.position);
                }
                SendMessage(player, "Home '" + name + "' set.");
                SaveData();
                return;
            }
            if (isDelete)
            {
                var name = args.Length > 0 ? string.Join(" ", args).Trim() : null;
                if (string.IsNullOrEmpty(name))
                {
                    SendMessage(player, "Usage: /deletehome <name>");
                    return;
                }
                if (user.Homes.Remove(name))
                    SendMessage(player, "Home '" + name + "' deleted.");
                else
                    SendMessage(player, "Home '" + name + "' not found.");
                SaveData();
                return;
            }

            if (args.Length == 0)
            {
                if (user.Homes.Count == 0)
                {
                    SendMessage(player, "You have no homes. Use: /sethome <name>");
                    return;
                }
                SendMessage(player, "Homes: " + string.Join(", ", user.Homes.Keys));
                return;
            }

            var homeName = string.Join(" ", args).Trim();
            if (!user.Homes.TryGetValue(homeName, out var homeData))
            {
                var key = user.Homes.Keys.FirstOrDefault(k => string.Equals(k, homeName, StringComparison.OrdinalIgnoreCase));
                if (key == null || !user.Homes.TryGetValue(key, out homeData))
                {
                    SendMessage(player, "Home not found. Use /home to list.");
                    return;
                }
            }

            if (!TryResolveHomePosition(homeData, player, out var dest, out string resolveError))
            {
                user.Homes.Remove(homeName);
                SaveData();
                SendMessage(player, string.IsNullOrEmpty(resolveError) ? ("Home '" + homeName + "' is no longer valid.") : resolveError);
                return;
            }
            if (IsInvalidBagSpawn(homeData, player, out _))
            {
                user.Homes.Remove(homeName);
                SaveData();
                SendMessage(player, "Home '" + homeName + "' is no longer assigned to you.");
                return;
            }
            if (homeData.EntityID != 0UL)
                dest += Vector3.up * 0.55f;
            if (IsInsideEntity(dest))
            {
                SendMessage(player, "Home '" + homeName + "' is currently blocked.");
                return;
            }
            var now = CurrentTime();
            if (!AdminsBypassLimits || !player.IsAdmin)
            {
                if (GetHomeCooldown(player) > 0 && user.IsOnHomeCooldown(now))
                {
                    SendMessage(player, "Home teleport is on cooldown.");
                    return;
                }
            }
            if (!MeetsPositionConditions(player, dest, false))
                return;
            if (!TryAuthorizePayment(player, user, TeleportPaymentKind.Home, out var receipt, out _))
                return;

            _lastHome[player.userID] = player.transform.position;
            int delaySec = (_config.Admin?.Instant == true && player.IsAdmin) ? 0 : Math.Max(0, GetHomeDelay(player));
            var delay = (AdminsBypassLimits && player.IsAdmin) ? 0 : delaySec;
            if (delay > 0)
            {
                SendMessage(player, "Teleporting home in " + delay + " seconds...");
                ShowPendingPositionPopup(player, delay, "Popup.Outgoing.TP.Home", homeName);
                ServerMgr.Instance?.StartCoroutine(DelayedTeleport(player, dest, delay, () =>
                {
                    user.HomeUsesToday++;
                    user.HomeCooldownUntil = CurrentTime() + GetHomeCooldown(player);
                }, "home", receipt));
            }
            else
            {
                DoTeleport(player, dest);
                user.HomeUsesToday++;
                user.HomeCooldownUntil = CurrentTime() + GetHomeCooldown(player);
            }
            SaveData();
        }

        private void CmdWarp(BasePlayer player, string[] args)
        {
            if (!HasPerm(player, "teleportgui.warps.use"))
            {
                SendMessage(player, "You don't have permission.");
                return;
            }
            bool hasAnyWarp = EnumerateAllWarps().Any();
            if (!hasAnyWarp)
            {
                SendMessage(player, "No warp points configured.");
                return;
            }
            if (args == null || args.Length == 0)
            {
                SendMessage(player, "Warps: " + string.Join(", ", EnumerateAllWarps()
                    .Where(kv => string.IsNullOrWhiteSpace(kv.Value?.Permission) || HasPerm(player, EnsureWarpPermission(kv.Value.Permission)))
                    .Select(kv => kv.Key)));
                return;
            }

            if (string.Equals(args[0], "list", StringComparison.OrdinalIgnoreCase))
            {
                var allowed = EnumerateAllWarps()
                    .Where(kv => string.IsNullOrWhiteSpace(kv.Value?.Permission) ||
                                 HasPerm(player, EnsureWarpPermission(kv.Value.Permission)))
                    .Select(kv => kv.Key);
                SendMessage(player, "Warps: " + string.Join(", ", allowed));
                return;
            }

            var name = string.Equals(args[0], "to", StringComparison.OrdinalIgnoreCase)
                ? string.Join(" ", args.Skip(1)).Trim()
                : string.Join(" ", args).Trim();
            if (!TryResolveAnyWarpPosition(name, out var dest, out var wp))
            {
                SendMessage(player, "Warp '" + name + "' not found.");
                return;
            }
            if (!string.IsNullOrWhiteSpace(wp.Permission) &&
                !HasPerm(player, EnsureWarpPermission(wp.Permission)))
            {
                SendMessage(player, "You don't have permission to use that warp.");
                return;
            }

            var user = GetOrCreateUser(player);
            var now = CurrentTime();
            if (!AdminsBypassLimits || !player.IsAdmin)
            {
                if (GetWarpCooldown(player) > 0 && user.IsOnWarpCooldown(now))
                {
                    SendMessage(player, "Warp is on cooldown.");
                    return;
                }
            }

            if (dest.sqrMagnitude < 1f)
            {
                SendMessage(player, "Warp '" + name + "' has no position set.");
                return;
            }
            if (!MeetsPositionConditions(player, dest, true))
                return;
            if (!TryAuthorizePayment(player, user, TeleportPaymentKind.Warp, out var receipt, out _))
                return;

            _lastWarp[player.userID] = player.transform.position;
            int delaySec = (_config.Admin?.Instant == true && player.IsAdmin) ? 0 : Math.Max(0, GetWarpDelay(player));
            var delay = (AdminsBypassLimits && player.IsAdmin) ? 0 : delaySec;
            if (delay > 0)
            {
                SendMessage(player, "Warping in " + delay + " seconds...");
                ShowPendingPositionPopup(player, delay, "Popup.Outgoing.TP.Warp", name);
                ServerMgr.Instance?.StartCoroutine(DelayedTeleport(player, dest, delay, () =>
                {
                    user.WarpUsesToday++;
                    user.WarpCooldownUntil = CurrentTime() + GetWarpCooldown(player);
                }, "warp", receipt));
            }
            else
            {
                DoTeleport(player, dest);
                user.WarpUsesToday++;
                user.WarpCooldownUntil = CurrentTime() + GetWarpCooldown(player);
            }
            SaveData();
        }

        private void CmdTpBack(BasePlayer player, string[] args)
        {
            if (!_data.Users.TryGetValue(player.userID, out var user))
            {
                SendMessage(player, "No previous location stored.");
                return;
            }
            if (!_lastTeleport.TryGetValue(player.userID, out var pos))
            {
                SendMessage(player, "No previous location stored.");
                return;
            }
            DoTeleport(player, pos);
            SendMessage(player, "Returned to previous location.");
        }

        private void CmdDeath(BasePlayer player, string[] args)
        {
            if (!DeathLocations.TryGetValue(player.userID, out var pos))
            {
                SendMessage(player, "No death location recorded.");
                return;
            }
            DoTeleport(player, pos);
            SendMessage(player, "Teleported to your last death location.");
        }

        private static readonly Dictionary<ulong, Vector3> _lastTeleport = new Dictionary<ulong, Vector3>();
        private static readonly Dictionary<ulong, Vector3> _lastHome = new Dictionary<ulong, Vector3>();
        private static readonly Dictionary<ulong, Vector3> _lastWarp = new Dictionary<ulong, Vector3>();

        // --- Player-to-player teleport requests (tpr/tpa/tpd/tpc/tprhere) ---
        private sealed class PendingRequest
        {
            public BasePlayer From;
            public BasePlayer To;
            public bool TpHere;
            public int TimeRemaining;
            public Coroutine Timer;
            public PaymentReceipt Receipt = PaymentReceipt.None;
        }
        private readonly Dictionary<ulong, PendingRequest> _outgoingRequests = new Dictionary<ulong, PendingRequest>();
        private readonly Dictionary<ulong, PendingRequest> _incomingRequests = new Dictionary<ulong, PendingRequest>();

        /// <summary>
        /// Full source teleport sequence: dismount, unparent, sleep + ReceivingSnapshot, deep-sea
        /// enter/leave RPC, collider/server-fall toggling, MovePosition + ForcePositionTo, and network
        /// snapshot refresh. Mirrors TeleportGUI.cs Teleport(BasePlayer, Vector3) using current game APIs.
        /// </summary>
        private static void DoTeleport(BasePlayer player, Vector3 position)
        {
            if (player == null || player.IsDestroyed) return;

            Vector3 from = player.transform.position;
            _lastTeleport[player.userID] = from;

            bool isPlayerInDeepSea = IsInsideDeepSea(from);
            bool isTargetInDeepSea = IsInsideDeepSea(position);

            try
            {
                if (player.isMounted)
                    player.GetMounted()?.DismountPlayer(player, true);

                player.SetParent(null, true, true);

                player.StartSleeping();
                player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);

                if (isPlayerInDeepSea != isTargetInDeepSea)
                {
                    try
                    {
                        var mgr = PointEntity<DeepSeaManager>.ServerInstance;
                        if (mgr != null)
                            mgr.ClientRPC(RpcTarget.Player("CLIENT_PlayerEnterOrLeaveDeepSea", player), isTargetInDeepSea);
                    }
                    catch { }
                }

                player.EnablePlayerCollider();
                player.SetServerFall(true);

                player.MovePosition(position);
                player.ClientRPC(RpcTarget.Player("ForcePositionTo", player), position);

                if (player.IsConnected)
                {
                    player.UpdateNetworkGroup();
                    player.SendNetworkUpdateImmediate();
                    player.ClearEntityQueue();
                    player.SendSubscribedGroupsSnapshot();
                }
            }
            finally
            {
                if (isPlayerInDeepSea != isTargetInDeepSea)
                {
                    try
                    {
                        if (isTargetInDeepSea) player.OnEnterDeepSea();
                        else player.OnExitDeepSea();
                    }
                    catch { }
                }
                player.EnablePlayerCollider();
                player.SetServerFall(false);
            }
        }

        private static bool IsInsideDeepSea(Vector3 position)
        {
            try { return DeepSeaManager.IsInsideDeepSea(position); }
            catch { return false; }
        }

        private System.Collections.IEnumerator DelayedTeleport(BasePlayer player, Vector3 position, int delaySeconds, Action onDone, string mode = "teleport", PaymentReceipt receipt = default, BasePlayer[] clearPendingPopupsFor = null)
        {
            if (player == null) yield break;
            _playersInDelayedTeleport[player.userID] = mode;
            try
            {
                var from = player.transform.position;
                for (int i = delaySeconds; i > 0; i--)
                {
                    yield return new WaitForSeconds(1f);
                    if (player == null || player.IsDestroyed) yield break;
                    if (_cancelTeleportRequested.Remove(player.userID))
                    {
                        RefundPayment(player, receipt);
                        if (player.IsConnected)
                            SendMessage(player, "Teleport cancelled (hurt or death).");
                        yield break;
                    }
                    if ((player.transform.position - from).sqrMagnitude > 4f)
                    {
                        RefundPayment(player, receipt);
                        if (player.IsConnected)
                            SendMessage(player, "Teleport cancelled (you moved).");
                        yield break;
                    }
                }
                if (_cancelTeleportRequested.Remove(player.userID))
                {
                    RefundPayment(player, receipt);
                    if (player != null && player.IsConnected)
                        SendMessage(player, "Teleport cancelled (hurt or death).");
                    yield break;
                }
                if (player != null && !player.IsDestroyed)
                {
                    bool ok = mode == "warp"
                        ? MeetsPositionConditions(player, position, true)
                        : mode == "home"
                            ? MeetsPositionConditions(player, position, false)
                            : true;
                    if (!ok)
                    {
                        RefundPayment(player, receipt);
                        yield break;
                    }
                    DoTeleport(player, position);
                    onDone?.Invoke();
                }
            }
            finally
            {
                if (player != null)
                {
                    _playersInDelayedTeleport.Remove(player.userID);
                    _cancelTeleportRequested.Remove(player.userID);
                }
                DestroyPendingPopups(clearPendingPopupsFor ?? new[] { player });
            }
        }

        private void DestroyPendingPopups(IEnumerable<BasePlayer> players)
        {
            if (players == null) return;
            foreach (var p in players)
            {
                if (p == null) continue;
                StopPopupDestroy(p);
                ChaosUI.Destroy(p, TPP_POPUP);
            }
        }

        public void OnPlayerTakeDamage(BasePlayer player)
        {
            if (player == null || player.IsNpc) return;
            if (!_playersInDelayedTeleport.TryGetValue(player.userID, out var mode)) return;
            bool cancel = (mode == "teleport" && _config.Teleport?.CancelOnDamage == true) ||
                         (mode == "home" && _config.Home?.CancelOnDamage == true) ||
                         (mode == "warp" && _config.Warp?.CancelOnDamage == true);
            if (cancel) _cancelTeleportRequested.Add(player.userID);
        }

        private static BasePlayer FindPlayer(string nameOrId)
        {
            if (string.IsNullOrEmpty(nameOrId)) return null;
            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p == null || p.IsNpc) continue;
                if (p.UserIDString == nameOrId || p.displayName?.IndexOf(nameOrId, StringComparison.OrdinalIgnoreCase) >= 0)
                    return p;
            }
            return null;
        }

        private void SendMessage(BasePlayer player, string message)
        {
            if (player?.IsConnected != true) return;
            var chat = _config?.Chat;
            if (chat != null && chat.UsePrefix && !string.IsNullOrEmpty(chat.Prefix))
                message = chat.Prefix + message;
            if (chat != null && chat.Icon != 0UL)
                player.SendConsoleCommand("chat.add", 2, chat.Icon, message);
            else
                player.ChatMessage(message);
        }

        private void SendLang(BasePlayer player, string key, params object[] args)
        {
            SendMessage(player, TeleportGUILanguage.Get(key, player, args));
        }

        public void OnPlayerDie(BasePlayer player)
        {
            if (player == null || player.IsNpc) return;
            if (_config != null && ShouldRecordDeath)
                DeathLocations[player.userID] = player.transform.position;
            if (_playersInDelayedTeleport.TryGetValue(player.userID, out var mode))
            {
                bool cancel = (mode == "teleport" && _config.Teleport?.CancelOnDeath == true) ||
                             (mode == "home" && _config.Home?.CancelOnDeath == true) ||
                             (mode == "warp" && _config.Warp?.CancelOnDeath == true);
                if (cancel) _cancelTeleportRequested.Add(player.userID);
            }
        }

        public static void RecordDeathPosition(ulong userId, Vector3 position)
        {
            if (Instance != null && Instance.ShouldRecordDeath)
                DeathLocations[userId] = position;
        }

        #region Extended source commands (locations / grid / marker / requests / back)

        private void CmdTpSave(BasePlayer player, string[] args)
        {
            if (!HasPerm(player, "teleportgui.tp.location")) { SendMessage(player, "You don't have permission."); return; }
            if (args == null || args.Length == 0) { SendMessage(player, "You must specify a name."); return; }
            var user = GetOrCreateUser(player);
            var name = args[0];
            user.Locations[name] = player.transform.position;
            SaveData();
            SendMessage(player, "Saved your current position as '" + name + "'.");
        }

        private void CmdTpl(BasePlayer player, string[] args)
        {
            if (!HasPerm(player, "teleportgui.tp.location")) { SendMessage(player, "You don't have permission."); return; }
            if (args == null || args.Length == 0) { SendMessage(player, "You must specify a name."); return; }
            var user = GetOrCreateUser(player);
            if (user.Locations.Count == 0) { SendMessage(player, "You have no saved locations."); return; }
            if (!user.Locations.TryGetValue(args[0], out var position)) { SendMessage(player, "You have no saved location called '" + args[0] + "'."); return; }
            DoTeleport(player, position);
            SendMessage(player, "Teleported to '" + args[0] + "'.");
        }

        private void CmdTplList(BasePlayer player)
        {
            if (!HasPerm(player, "teleportgui.tp.location")) { SendMessage(player, "You don't have permission."); return; }
            var user = GetOrCreateUser(player);
            if (user.Locations.Count == 0) { SendMessage(player, "You have no saved locations."); return; }
            SendMessage(player, "Your locations: " + string.Join(", ", user.Locations.Keys));
        }

        private void CmdListHomes(BasePlayer player, string[] args)
        {
            if (!HasPerm(player, "teleportgui.homes.use")) { SendMessage(player, "You don't have permission."); return; }
            if (args != null && args.Length > 0)
            {
                if (!HasPerm(player, "teleportgui.homes.viewothershomes")) { SendMessage(player, "You don't have permission."); return; }
                var target = FindPlayer(string.Join(" ", args));
                if (target == null) { SendMessage(player, "Player not found."); return; }
                if (!_data.Users.TryGetValue(target.userID, out var tu) || tu.Homes.Count == 0)
                { SendMessage(player, target.displayName + " has no homes."); return; }
                SendMessage(player, target.displayName + "'s homes: " + string.Join(", ", tu.Homes.Keys));
                return;
            }
            var user = GetOrCreateUser(player);
            if (user.Homes.Count == 0) { SendMessage(player, "You have no homes."); return; }
            SendMessage(player, "Your homes: " + string.Join(", ", user.Homes.Keys));
        }

        private void CmdHomeCancel(BasePlayer player)
        {
            if (_playersInDelayedTeleport.TryGetValue(player.userID, out var mode) && mode == "home")
            {
                _cancelTeleportRequested.Add(player.userID);
                return;
            }
            SendMessage(player, "You have no pending home teleport.");
        }

        private void CmdHomeBack(BasePlayer player, string[] args)
        {
            if (HasPerm(player, "teleportgui.homes.back.admin") && args != null && args.Length > 0)
            {
                var target = FindPlayer(args[0]);
                if (target == null) { SendMessage(player, "Player not found."); return; }
                if (!_lastHome.TryGetValue(target.userID, out var tp)) { SendMessage(player, target.displayName + " has no home back location."); return; }
                DoTeleport(target, tp);
                SendMessage(target, "You were teleported back to your previous location.");
                SendMessage(player, "Teleported " + target.displayName + " back.");
                return;
            }
            if (!HasPerm(player, "teleportgui.homes.back") && !HasPerm(player, "teleportgui.homes.back.bypass"))
            { SendMessage(player, "You don't have permission."); return; }
            if (!_lastHome.TryGetValue(player.userID, out var pos)) { SendMessage(player, "No previous home location stored."); return; }
            DoTeleport(player, pos);
            SendMessage(player, "Returned to your previous location.");
        }

        private void CmdWarpBack(BasePlayer player, string[] args)
        {
            if (HasPerm(player, "teleportgui.warps.back.admin") && args != null && args.Length > 0)
            {
                var target = FindPlayer(args[0]);
                if (target == null) { SendMessage(player, "Player not found."); return; }
                if (!_lastWarp.TryGetValue(target.userID, out var tp)) { SendMessage(player, target.displayName + " has no warp back location."); return; }
                DoTeleport(target, tp);
                SendMessage(target, "You were teleported back to your previous location.");
                SendMessage(player, "Teleported " + target.displayName + " back.");
                return;
            }
            if (!HasPerm(player, "teleportgui.warps.back") && !HasPerm(player, "teleportgui.warps.back.bypass"))
            { SendMessage(player, "You don't have permission."); return; }
            if (!_lastWarp.TryGetValue(player.userID, out var pos)) { SendMessage(player, "No previous warp location stored."); return; }
            DoTeleport(player, pos);
            SendMessage(player, "Returned to your previous location.");
        }

        private void CmdTpGrid(BasePlayer player, string[] args)
        {
            if (!HasPerm(player, "teleportgui.tp.grid")) { SendMessage(player, "You don't have permission."); return; }
            string grid = args != null && args.Length > 0 ? args[0] : string.Empty;
            if (string.IsNullOrEmpty(grid)) { SendMessage(player, "Usage: /tpgrid <grid> (e.g. G14)"); return; }
            Vector3? value;
            try { value = MapHelper.StringToPosition(grid); }
            catch { value = null; }
            if (value == null) { SendMessage(player, "Invalid grid reference."); return; }
            Vector3 position = value.Value;
            position = SnapToGround(position);
            DoTeleport(player, position);
            SendMessage(player, "Teleported to grid " + grid + ".");
        }

        private void CmdTpMarker(BasePlayer player, string[] args)
        {
            if (!HasPerm(player, "teleportgui.tp.marker")) { SendMessage(player, "You don't have permission."); return; }
            var poi = player.State?.pointsOfInterest;
            if (poi == null || poi.Count == 0) { SendMessage(player, "You have no map markers."); return; }
            ProtoBuf.MapNote mapNote = null;
            if (args == null || args.Length == 0)
                mapNote = poi[poi.Count - 1];
            else
            {
                foreach (var marker in poi)
                    if (marker.label != null && marker.label.Equals(args[0], StringComparison.InvariantCultureIgnoreCase)) { mapNote = marker; break; }
                if (mapNote == null && int.TryParse(args[0], out int id))
                {
                    if (id < 0) mapNote = poi[poi.Count - 1];
                    else if (id < poi.Count) mapNote = poi[id];
                    else { SendMessage(player, "Invalid marker id."); return; }
                }
            }
            if (mapNote == null) { SendMessage(player, "Marker not found."); return; }
            Vector3 position = SnapToGround(mapNote.worldPosition);
            DoTeleport(player, position);
            SendMessage(player, "Teleported to map marker.");
        }

        private static Vector3 SnapToGround(Vector3 position)
        {
            try
            {
                position.y = WaterLevel.GetWaterOrTerrainSurface(position, true, true, null) + 0.5f;
            }
            catch { }
            return position;
        }

        // --- Player-to-player request system ---

        private void CmdTpr(BasePlayer player, string[] args, bool tpHere)
        {
            if (!HasPerm(player, tpHere ? "teleportgui.tp.tphere" : "teleportgui.tp.use"))
            { SendMessage(player, "You don't have permission."); return; }
            if (args == null || args.Length == 0)
            { SendMessage(player, tpHere ? "Usage: /tprhere <player>" : "Usage: /tpr <player>"); return; }
            var target = FindPlayer(string.Join(" ", args));
            if (target == null) { SendMessage(player, "Player not found."); return; }
            if (target == player) { SendMessage(player, "You cannot teleport to yourself."); return; }

            if (_config.Admin?.Instant == true && player.IsAdmin)
            {
                if (tpHere) DoTeleport(target, player.transform.position);
                else DoTeleport(player, target.transform.position);
                SendMessage(player, tpHere ? ("Teleported " + target.displayName + " to you.") : ("Teleported to " + target.displayName + "."));
                return;
            }

            if (_outgoingRequests.ContainsKey(player.userID) || _incomingRequests.ContainsKey(player.userID))
            { SendMessage(player, "You already have a pending request."); return; }
            if (_outgoingRequests.ContainsKey(target.userID) || _incomingRequests.ContainsKey(target.userID))
            { SendMessage(player, target.displayName + " already has a pending request."); return; }

            if (!MeetsPlayerConditions(player, target))
                return;

            var user = GetOrCreateUser(player);
            if (!AdminsBypassLimits || !player.IsAdmin)
            {
                if (GetTPCooldown(player) > 0 && user.IsOnTPCooldown(CurrentTime()))
                {
                    SendMessage(player, "TP is on cooldown.");
                    return;
                }
            }
            if (!TryAuthorizePayment(player, user, TeleportPaymentKind.Teleport, out var receipt, out _))
                return;

            var req = new PendingRequest
            {
                From = player,
                To = target,
                TpHere = tpHere,
                TimeRemaining = Math.Max(5, _config.Teleport?.RequestTimeout ?? 30),
                Receipt = receipt
            };

            // Auto-accept based on the target's preferences.
            if (ShouldAutoAccept(player, target))
            {
                SendMessage(player, "Request auto-accepted by " + target.displayName + ".");
                AcceptRequest(req);
                return;
            }

            _outgoingRequests[player.userID] = req;
            _incomingRequests[target.userID] = req;
            req.Timer = ServerMgr.Instance?.StartCoroutine(RequestTimeout(req));
            ShowRequestPopups(req);

            if (tpHere)
            {
                SendMessage(player, "You requested " + target.displayName + " to teleport to you.");
                SendMessage(target, player.displayName + " requested you teleport to them. /tpa to accept, /tpd to decline.");
            }
            else
            {
                SendMessage(player, "Teleport request sent to " + target.displayName + ".");
                SendMessage(target, player.displayName + " requested to teleport to you. /tpa to accept, /tpd to decline.");
            }
        }

        private bool ShouldAutoAccept(BasePlayer from, BasePlayer to)
        {
            if (!_data.Users.TryGetValue(to.userID, out var toUser)) return false;
            var mode = toUser.AutoAccept;
            if ((mode & TeleportGUIData.UserData.AutoAcceptEnum.All) != 0) return true;
            if ((mode & TeleportGUIData.UserData.AutoAcceptEnum.Teams) != 0 && from.currentTeam != 0UL && from.currentTeam == to.currentTeam) return true;
            if ((mode & TeleportGUIData.UserData.AutoAcceptEnum.Clans) != 0 && TeleportGUIIntegrations.Clans.IsClanMember(from.userID, to.userID)) return true;
            if ((mode & TeleportGUIData.UserData.AutoAcceptEnum.Friends) != 0 && TeleportGUIIntegrations.Friends.AreFriends(from.userID, to.userID)) return true;
            return false;
        }

        private void CmdTpAccept(BasePlayer player)
        {
            if (!HasPerm(player, "teleportgui.tp.use")) { SendMessage(player, "You don't have permission."); return; }
            if (!_incomingRequests.TryGetValue(player.userID, out var req)) { SendMessage(player, "You have no pending requests."); return; }
            AcceptRequest(req);
        }

        private void CmdTpDecline(BasePlayer player)
        {
            if (!HasPerm(player, "teleportgui.tp.use")) { SendMessage(player, "You don't have permission."); return; }
            if (!_incomingRequests.TryGetValue(player.userID, out var req)) { SendMessage(player, "You have no pending requests."); return; }
            ClearRequest(req, refund: true);
            if (req.From != null) SendMessage(req.From, (req.To != null ? req.To.displayName : "The player") + " declined your teleport request.");
            if (req.To != null) SendMessage(req.To, "You declined the teleport request.");
        }

        private void CmdTpCancel(BasePlayer player)
        {
            if (!HasPerm(player, "teleportgui.tp.tpcancel")) { SendMessage(player, "You don't have permission."); return; }
            // Cancel an outgoing request, otherwise cancel a delayed teleport.
            if (_outgoingRequests.TryGetValue(player.userID, out var req))
            {
                ClearRequest(req, refund: true);
                if (req.From != null) SendMessage(req.From, "You cancelled your teleport request.");
                if (req.To != null) SendMessage(req.To, (req.From != null ? req.From.displayName : "The player") + " cancelled their teleport request.");
                return;
            }
            if (_playersInDelayedTeleport.ContainsKey(player.userID))
            {
                _cancelTeleportRequested.Add(player.userID);
                return;
            }
            SendMessage(player, "You have no pending teleport.");
        }

        private void AcceptRequest(PendingRequest req)
        {
            if (req == null) return;
            var from = req.From; var to = req.To;
            var receipt = req.Receipt;
            ClearRequest(req, refund: false);

            if (from == null || to == null || !from.IsConnected || !to.IsConnected)
            {
                RefundPayment(from, receipt);
                return;
            }

            if (!MeetsPlayerConditions(from, to))
            {
                RefundPayment(from, receipt);
                return;
            }

            int delay = (_config.Admin?.Instant == true && from.IsAdmin) ? 0 : Math.Max(0, GetTPDelay(from));
            var mover = req.TpHere ? to : from;
            var dest = req.TpHere ? from.transform.position : to.transform.position;

            SendMessage(from, "Teleport request accepted.");
            SendMessage(to, "Teleport request accepted.");

            var user = GetOrCreateUser(from);
            if (delay > 0)
            {
                SendMessage(mover, "Teleporting in " + delay + " seconds...");
                ShowPendingTeleportPopups(from, to, delay, req.TpHere);
                ServerMgr.Instance?.StartCoroutine(DelayedTeleport(mover, dest, delay, () =>
                {
                    user.TPUsesToday++;
                    user.TPCooldownUntil = CurrentTime() + GetTPCooldown(from);
                    SaveData();
                }, "teleport", receipt, clearPendingPopupsFor: new[] { from, to }));
            }
            else
            {
                DoTeleport(mover, dest);
                user.TPUsesToday++;
                user.TPCooldownUntil = CurrentTime() + GetTPCooldown(from);
                SaveData();
            }
        }

        private void ClearAllRequests()
        {
            var all = new List<PendingRequest>(_outgoingRequests.Values);
            foreach (var req in all)
                ClearRequest(req, refund: true);
            _outgoingRequests.Clear();
            _incomingRequests.Clear();
        }

        private void ClearRequest(PendingRequest req, bool refund = false)
        {
            if (req == null) return;
            if (req.Timer != null) { try { ServerMgr.Instance?.StopCoroutine(req.Timer); } catch { } req.Timer = null; }
            if (req.From != null) _outgoingRequests.Remove(req.From.userID);
            if (req.To != null) _incomingRequests.Remove(req.To.userID);
            if (req.From != null)
            {
                StopPopupDestroy(req.From);
                ChaosUI.Destroy(req.From, TPR_POPUP);
            }
            if (req.To != null)
            {
                StopPopupDestroy(req.To);
                ChaosUI.Destroy(req.To, TPR_POPUP);
            }
            if (refund && req.Receipt.WasCharged)
            {
                RefundPayment(req.From, req.Receipt);
                req.Receipt = PaymentReceipt.None;
            }
        }

        private void ShowRequestPopups(PendingRequest req)
        {
            if (req?.From == null || req.To == null) return;
            string toName = req.To.displayName?.StripTags() ?? "player";
            string fromName = req.From.displayName?.StripTags() ?? "player";
            string incomingKey = req.TpHere ? "Popup.Incoming.TPHere" : "Popup.Incoming.TPR";
            string outgoingKey = req.TpHere ? "Popup.Outgoing.TPHere" : "Popup.Outgoing.TPR";

            CreateTeleportRequestPopup(req.To, TPR_POPUP, req.TimeRemaining, incomingKey, fromName,
                isReceiver: true, canAccept: true, canCancel: true,
                onAccept: () =>
                {
                    if (_incomingRequests.TryGetValue(req.To.userID, out var current) && ReferenceEquals(current, req))
                        AcceptRequest(req);
                },
                onDeclineOrCancel: () =>
                {
                    if (_incomingRequests.TryGetValue(req.To.userID, out var current) && ReferenceEquals(current, req))
                        CmdTpDecline(req.To);
                });

            CreateTeleportRequestPopup(req.From, TPR_POPUP, req.TimeRemaining, outgoingKey, toName,
                isReceiver: false, canAccept: false, canCancel: true,
                onAccept: null,
                onDeclineOrCancel: () =>
                {
                    if (_outgoingRequests.TryGetValue(req.From.userID, out var current) && ReferenceEquals(current, req))
                        CmdTpCancel(req.From);
                });
        }

        private void ShowPendingTeleportPopups(BasePlayer from, BasePlayer to, int delay, bool tpHere)
        {
            if (from == null || to == null || delay <= 0) return;
            string fromName = from.displayName?.StripTags() ?? "player";
            string toName = to.displayName?.StripTags() ?? "player";
            bool fromCanCancel = HasPerm(from, "teleportgui.tp.tpcancel");
            BasePlayer mover = tpHere ? to : from;

            CreateTeleportRequestPopup(to, TPP_POPUP, delay, "Popup.Incoming.TP", fromName,
                isReceiver: true, canAccept: false, canCancel: false,
                onAccept: null, onDeclineOrCancel: null);

            CreateTeleportRequestPopup(from, TPP_POPUP, delay, "Popup.Outgoing.TP", toName,
                isReceiver: false, canAccept: false, canCancel: fromCanCancel,
                onAccept: null,
                onDeclineOrCancel: () =>
                {
                    if (mover != null && _playersInDelayedTeleport.ContainsKey(mover.userID))
                        _cancelTeleportRequested.Add(mover.userID);
                    DestroyPendingPopups(new[] { from, to });
                });
        }

        private void ShowPendingPositionPopup(BasePlayer player, int delay, string langKey, string displayName)
        {
            if (player == null || delay <= 0) return;
            CreateTeleportRequestPopup(player, TPP_POPUP, delay, langKey, displayName ?? string.Empty,
                isReceiver: false, canAccept: false, canCancel: true,
                onAccept: null,
                onDeclineOrCancel: () =>
                {
                    if (_playersInDelayedTeleport.ContainsKey(player.userID))
                        _cancelTeleportRequested.Add(player.userID);
                });
        }

        private System.Collections.IEnumerator RequestTimeout(PendingRequest req)
        {
            while (req.TimeRemaining > 0)
            {
                yield return new WaitForSeconds(1f);
                if (req.From == null || !req.From.IsConnected || req.To == null || !req.To.IsConnected)
                {
                    ClearRequest(req, refund: true);
                    yield break;
                }
                req.TimeRemaining--;
            }
            ClearRequest(req, refund: true);
            if (req.From != null && req.From.IsConnected) SendMessage(req.From, "Teleport request to " + (req.To != null ? req.To.displayName : "player") + " timed out.");
            if (req.To != null && req.To.IsConnected) SendMessage(req.To, "Teleport request from " + (req.From != null ? req.From.displayName : "player") + " timed out.");
        }

        private void CmdWarpAdd(BasePlayer player, string[] args)
        {
            if (!HasPerm(player, "teleportgui.warps.admin")) { SendMessage(player, "You don't have permission."); return; }
            if (args == null || args.Length == 0) { SendMessage(player, "Usage: /warpadd <name> [permission] [command]"); return; }
            string warpName = args[0];
            if (_warpData.ContainsKey(warpName)) { SendMessage(player, "A warp called '" + warpName + "' already exists."); return; }
            string perm = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
            string command = args.Length > 2 ? NormalizeWarpChatCommand(args[2]) : string.Empty;
            var wp = new TeleportGUIData.WarpPoint { Position = player.transform.position, Permission = perm, Command = command };
            _warpData[warpName] = wp;
            _data.WarpPoints = _warpData;
            if (!string.IsNullOrEmpty(perm)) PermissionsBridge.RegisterPermission(EnsureWarpPermission(perm));
            SaveData();
            RegisterWarpChatCommands();
            string response = "Warp '" + warpName + "' added.";
            if (!string.IsNullOrEmpty(perm)) response += " Permission: " + perm + ".";
            if (!string.IsNullOrEmpty(command)) response += " Command: /" + command + ".";
            SendMessage(player, response);
        }

        private void CmdWarpRemove(BasePlayer player, string[] args)
        {
            if (!HasPerm(player, "teleportgui.warps.admin")) { SendMessage(player, "You don't have permission."); return; }
            if (args == null || args.Length == 0) { SendMessage(player, "Usage: /warpremove <name>"); return; }
            if (!_warpData.ContainsKey(args[0])) { SendMessage(player, "Warp '" + args[0] + "' does not exist."); return; }
            _warpData.Remove(args[0]);
            _data.WarpPoints = _warpData;
            SaveData();
            SendMessage(player, "Warp '" + args[0] + "' removed.");
        }

        private void HandleTpAdmin(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection?.player as BasePlayer;
            HandleTpAdmin(player, arg.Args.AsStringArray(), message => arg.ReplyWith(message));
        }

        private void HandleTpAdmin(BasePlayer player, string[] args, Action<string> reply)
        {
            if (player != null && !HasPerm(player, "teleportgui.admin"))
            {
                reply("You don't have permission.");
                return;
            }
            if (args == null || args.Length != 2)
            {
                reply("tpadmin wipehomes|wipelocations|wipetpusage|wipehomeusage|wipewarpusage <user_id|*>");
                return;
            }

            bool all = args[1] == "*";
            TeleportGUIData.UserData specified = null;
            if (!all)
            {
                if (!ulong.TryParse(args[1], out ulong userId))
                {
                    reply("Invalid user ID entered.");
                    return;
                }
                if (!_data.Users.TryGetValue(userId, out specified))
                {
                    reply("No user data found with the specified user ID.");
                    return;
                }
            }

            IEnumerable<TeleportGUIData.UserData> users = all ? _data.Users.Values : new[] { specified };
            switch (args[0].ToLowerInvariant())
            {
                case "wipehomes":
                    foreach (var user in users)
                    {
                        user.Homes.Clear();
                        user.HomeUsage.Reset();
                    }
                    break;
                case "wipelocations":
                    foreach (var user in users) user.Locations.Clear();
                    break;
                case "wipetpusage":
                    foreach (var user in users) user.TPUsage.Reset();
                    break;
                case "wipehomeusage":
                    foreach (var user in users) user.HomeUsage.Reset();
                    break;
                case "wipewarpusage":
                    foreach (var user in users) user.WarpUsage.Reset();
                    break;
                default:
                    reply("Incorrect syntax.");
                    return;
            }
            SaveData();
            reply(all ? "TeleportGUI data wiped for all users." : "TeleportGUI data wiped for " + args[1] + ".");
        }

        #endregion

        #region Public API (exposed via AppDomain "TeleportGUI_ApiType")
        // These static getters mirror the Oxide TeleportGUI plugin API. Consumers resolve
        // typeof(TeleportGUIMod) from AppDomain.CurrentDomain.GetData("TeleportGUI_ApiType")
        // and invoke them reflectively. All are null-safe when the mod is unloaded.

        public static Dictionary<string, Vector3> GetPlayerHomes(ulong userID)
        {
            var self = Instance;
            if (self?._data?.Users == null || !self._data.Users.TryGetValue(userID, out var user) || user?.Homes == null)
                return null;
            var homes = new Dictionary<string, Vector3>();
            foreach (var kvp in user.Homes)
            {
                if (kvp.Value != null && kvp.Value.TryGetPosition(out Vector3 position))
                    homes[kvp.Key] = position;
            }
            return homes;
        }

        public static Dictionary<string, Vector3> GetPlayerLocations(ulong userID)
        {
            var self = Instance;
            if (self?._data?.Users == null || !self._data.Users.TryGetValue(userID, out var user) || user?.Locations == null)
                return null;
            return new Dictionary<string, Vector3>(user.Locations);
        }

        public static Dictionary<string, Vector3> GetWarpPoints()
        {
            var self = Instance;
            var result = new Dictionary<string, Vector3>();
            if (self?._warpData != null)
                foreach (var kvp in self._warpData)
                    result[kvp.Key] = kvp.Value != null ? kvp.Value.Position : Vector3.zero;
            return result;
        }

        public static double GetPlayerHomeCooldown(ulong userID) =>
            (Instance?._data?.Users != null && Instance._data.Users.TryGetValue(userID, out var u)) ? u.HomeCooldownUntil : 0;

        public static int GetPlayerHomeUses(ulong userID) =>
            (Instance?._data?.Users != null && Instance._data.Users.TryGetValue(userID, out var u)) ? u.HomeUsesToday : 0;

        public static double GetPlayerTPCooldown(ulong userID) =>
            (Instance?._data?.Users != null && Instance._data.Users.TryGetValue(userID, out var u)) ? u.TPCooldownUntil : 0;

        public static int GetPlayerTPUses(ulong userID) =>
            (Instance?._data?.Users != null && Instance._data.Users.TryGetValue(userID, out var u)) ? u.TPUsesToday : 0;

        public static double GetPlayerWarpCooldown(ulong userID) =>
            (Instance?._data?.Users != null && Instance._data.Users.TryGetValue(userID, out var u)) ? u.WarpCooldownUntil : 0;

        public static int GetPlayerWarpUses(ulong userID) =>
            (Instance?._data?.Users != null && Instance._data.Users.TryGetValue(userID, out var u)) ? u.WarpUsesToday : 0;
        #endregion
    }
}
