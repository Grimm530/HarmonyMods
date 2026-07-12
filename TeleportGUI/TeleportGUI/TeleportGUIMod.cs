using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConVar;
using Newtonsoft.Json;
using UnityEngine;
using Network;

namespace TeleportGUI
{
    public class TeleportGUIMod : IHarmonyModHooks
    {
        public static TeleportGUIMod Instance { get; private set; }

        private TeleportGUIConfig _config;
        private TeleportGUIData _data;
        private string _dataPath;
        private string _configPath;
        private readonly Dictionary<string, ConsoleSystem.Command> _registeredCommands = new Dictionary<string, ConsoleSystem.Command>();
        private static readonly Dictionary<ulong, Vector3> DeathLocations = new Dictionary<ulong, Vector3>();
        private readonly Dictionary<ulong, (string mode, int page, string search)> _uiState = new Dictionary<ulong, (string, int, string)>();
        private readonly Dictionary<ulong, Vector3> _pendingWarpPosition = new Dictionary<ulong, Vector3>();
        /// <summary>When set, OpenTeleportUI shows Create Home or Add Warp modal instead of main panel.</summary>
        private readonly Dictionary<ulong, string> _showingModal = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> _playersInDelayedTeleport = new Dictionary<ulong, string>();
        private readonly HashSet<ulong> _cancelTeleportRequested = new HashSet<ulong>();
        private const int UI_PER_PAGE = 8;

        private static double CurrentTime() => (double)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            LoadConfig();
            LoadData();
            MergeConfigWarpsIntoData();
            PurgeOldUsers();
            RegisterCommands();
            RegisterCuiCommand();
            UnityEngine.Debug.Log("[TeleportGUI] Harmony mod loaded. Commands: /tp, /home, /warp, /tpback, /death. Use /tp with no args to open GUI.");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            UnregisterCuiCommand();
            UnregisterCommands();
            foreach (var p in BasePlayer.activePlayerList)
                if (p != null && p.IsConnected) TeleportGUIUI.Destroy(p);
            _uiState.Clear();
            SaveData();
            Instance = null;
            UnityEngine.Debug.Log("[TeleportGUI] Harmony mod unloaded.");
        }

        private void LoadConfig()
        {
            var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var paths = new[]
            {
                Path.Combine(serverRoot, "oxide", "config", "TeleportGUI.json"),
                Path.Combine(serverRoot, "HarmonyConfig", "TeleportGUI.json"),
                Path.Combine(serverRoot, "Config", "TeleportGUI.json"),
                Path.Combine(serverRoot, "TeleportGUI.json"),
            };
            foreach (var p in paths)
            {
                if (File.Exists(p))
                {
                    _configPath = p;
                    try
                    {
                        var json = File.ReadAllText(p);
                        _config = JsonConvert.DeserializeObject<TeleportGUIConfig>(json);
                        if (_config == null) _config = new TeleportGUIConfig();
                        EnsureConfigDefaults();
                        EnsureDefaultWarpsInConfig();
                        return;
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogWarning("[TeleportGUI] Config load failed: " + ex.Message);
                    }
                    break;
                }
            }
            _config = new TeleportGUIConfig();
            EnsureConfigDefaults();
            EnsureDefaultWarpsInConfig();
            try
            {
                var dir = Path.GetDirectoryName(Path.Combine(serverRoot, "HarmonyConfig", "TeleportGUI.json"));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var defaultPath = Path.Combine(serverRoot, "HarmonyConfig", "TeleportGUI.json");
                File.WriteAllText(defaultPath, JsonConvert.SerializeObject(_config, Formatting.Indented));
                _configPath = defaultPath;
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
        }

        /// <summary>Ensures Outpost and Bandit exist in config warps (for existing configs that don't have them).</summary>
        private void EnsureDefaultWarpsInConfig()
        {
            if (_config?.WarpPoints == null) return;
            if (!_config.WarpPoints.ContainsKey("Outpost"))
                _config.WarpPoints["Outpost"] = new TeleportGUIConfig.WarpPointConfig { X = 0, Y = 0, Z = 0 };
            if (!_config.WarpPoints.ContainsKey("Bandit"))
                _config.WarpPoints["Bandit"] = new TeleportGUIConfig.WarpPointConfig { X = 0, Y = 0, Z = 0 };
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
            _dataPath = Path.Combine(GetDataFolder(), "TeleportGUI_Data.json");
            _data = new TeleportGUIData();
            try
            {
                var dir = Path.GetDirectoryName(_dataPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(_dataPath))
                {
                    var json = File.ReadAllText(_dataPath);
                    var loaded = JsonConvert.DeserializeObject<TeleportGUIData>(json);
                    if (loaded != null) _data = loaded;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[TeleportGUI] Data load failed: " + ex.Message);
            }

            if (_data.Users == null) _data.Users = new Dictionary<ulong, TeleportGUIData.UserData>();
            if (_data.WarpPoints == null) _data.WarpPoints = new Dictionary<string, TeleportGUIData.Vector3Data>();

            if (_data.ShouldResetDailyUses())
            {
                foreach (var u in _data.Users.Values) u.ResetDailyUses();
                _data.LastResetDate = DateTime.UtcNow.ToString("o");
                SaveData();
            }
        }

        private void MergeConfigWarpsIntoData()
        {
            if (_config?.WarpPoints == null) return;
            foreach (var kv in _config.WarpPoints)
            {
                if (_data.WarpPoints.ContainsKey(kv.Key)) continue;
                _data.WarpPoints[kv.Key] = new TeleportGUIData.Vector3Data
                {
                    X = kv.Value.X,
                    Y = kv.Value.Y,
                    Z = kv.Value.Z
                };
            }
        }

        public void SaveData()
        {
            if (_data == null || string.IsNullOrEmpty(_dataPath)) return;
            try
            {
                var dir = Path.GetDirectoryName(_dataPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(_data, Formatting.Indented);
                File.WriteAllText(_dataPath, json);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[TeleportGUI] Save failed: " + ex.Message);
            }
        }

        private bool CanUse(BasePlayer player)
        {
            if (player == null || player.IsNpc) return false;
            if (_config.AdminsBypass && player.IsAdmin) return true;
            if (_config.AllowedSteamIds == null || _config.AllowedSteamIds.Count == 0) return true;
            return _config.AllowedSteamIds.Contains(player.UserIDString);
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

        private int GetTPDelay() => _config.Teleport?.Delay?.Default ?? 5;
        private int GetTPCooldown() => _config.Teleport?.Cooldown?.Default ?? 300;
        private int GetTPDailyLimit() => _config.Teleport?.DailyLimit?.Default ?? 10;
        private int GetMaxHomes() => _config.Home?.MaxHomes?.Default ?? 5;
        private int GetHomeDelay() => _config.Home?.Delay?.Default ?? 5;
        private int GetHomeCooldown() => _config.Home?.Cooldown?.Default ?? 60;
        private int GetHomeDailyLimit() => _config.Home?.DailyLimit?.Default ?? 0;
        private int GetWarpDelay() => _config.Warp?.Delay?.Default ?? 5;
        private int GetWarpCooldown() => _config.Warp?.Cooldown?.Default ?? 120;
        private int GetWarpDailyLimit() => _config.Warp?.DailyLimit?.Default ?? 0;

        private void RegisterCommands()
        {
            var allAliases = new List<string>();
            allAliases.AddRange(GetTPAliases());
            allAliases.AddRange(GetHomeAliases());
            allAliases.AddRange(GetWarpAliases());
            if (_config.TpBackCommandAliases != null) allAliases.AddRange(_config.TpBackCommandAliases);
            if (_config.DeathCommandAliases != null) allAliases.AddRange(_config.DeathCommandAliases);

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

        private void HandleCuiCommand(BasePlayer player, string cmd, string[] args)
        {
            if (string.IsNullOrEmpty(cmd)) return;
            if (cmd == "close")
            {
                TeleportGUIUI.Destroy(player);
                _uiState.Remove(player.userID);
                _pendingWarpPosition.Remove(player.userID);
                _showingModal.Remove(player.userID);
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
                if (!player.IsAdmin) return;
                _pendingWarpPosition[player.userID] = player.transform.position;
                _showingModal[player.userID] = "addwarp";
                OpenTeleportUI(player, "warp");
                return;
            }
            if (cmd == "addwarp.cancel")
            {
                _showingModal.Remove(player.userID);
                _pendingWarpPosition.Remove(player.userID);
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
            if (cmd.StartsWith("tp."))
            {
                var userIdStr = cmd.Substring(3).Trim();
                TeleportGUIUI.Destroy(player);
                _uiState.Remove(player.userID);
                CmdTP(player, new[] { userIdStr });
                return;
            }
            if (cmd.StartsWith("home."))
            {
                var name = cmd.Substring(5).Replace("_", " ").Trim();
                TeleportGUIUI.Destroy(player);
                _uiState.Remove(player.userID);
                CmdHome(player, "home", new[] { name });
                return;
            }
            if (cmd.StartsWith("warp."))
            {
                var name = cmd.Substring(5).Replace("_", " ").Trim();
                TeleportGUIUI.Destroy(player);
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
                if (_data.WarpPoints == null) _data.WarpPoints = new Dictionary<string, TeleportGUIData.Vector3Data>();
                _data.WarpPoints[name] = TeleportGUIData.Vector3Data.FromVector3(pos);
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
            // Show Create Home or Add Warp modal if requested
            if (_showingModal.TryGetValue(player.userID, out var modal))
            {
                if (modal == "addhome")
                {
                    string modalJson = TeleportGUIUI.BuildCreateHomeModal(_config.UI?.Colors);
                    TeleportGUIUI.Show(player, modalJson);
                    return;
                }
                if (modal == "addwarp")
                {
                    string modalJson = TeleportGUIUI.BuildAddWarpModal(_config.UI?.Colors);
                    TeleportGUIUI.Show(player, modalJson);
                    return;
                }
            }
            if (!_uiState.TryGetValue(player.userID, out var state))
                state = (mode, 0, "");
            if (string.IsNullOrEmpty(state.mode)) state = (mode, state.page, state.search);
            mode = state.mode;
            int page = state.page;
            string search = state.search ?? "";

            List<TeleportGUIUI.PlayerEntry> playerEntries = null;
            List<TeleportGUIUI.HomeEntry> homeEntries = null;
            List<TeleportGUIUI.WarpEntry> warpEntries = null;
            bool hasNext = false;

            if (mode == "teleport")
            {
                var players = BasePlayer.activePlayerList
                    .Where(p => p != null && !p.IsNpc && p != player && p.IsConnected)
                    .ToList();
                if (_config.UI?.HideAdminsInUI == true)
                    players = players.Where(p => !p.IsAdmin).ToList();
                if (!string.IsNullOrEmpty(search))
                    players = players.Where(p => (p.displayName ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 || (p.UserIDString ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                int total = players.Count;
                playerEntries = players.Skip(page * UI_PER_PAGE).Take(UI_PER_PAGE).Select(p => new TeleportGUIUI.PlayerEntry { UserId = p.UserIDString, DisplayName = p.displayName }).ToList();
                hasNext = (page + 1) * UI_PER_PAGE < total;
            }
            else if (mode == "home")
            {
                var user = GetOrCreateUser(player);
                var homes = user.Homes.Keys.AsEnumerable();
                if (!string.IsNullOrEmpty(search))
                    homes = homes.Where(k => k.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
                var homeList = homes.ToList();
                int total = homeList.Count;
                homeEntries = homeList.Skip(page * UI_PER_PAGE).Take(UI_PER_PAGE).Select(n => new TeleportGUIUI.HomeEntry { Name = n }).ToList();
                hasNext = (page + 1) * UI_PER_PAGE < total;
            }
            else if (mode == "warp")
            {
                var warps = _data.WarpPoints?.Keys.AsEnumerable() ?? Enumerable.Empty<string>();
                if (!string.IsNullOrEmpty(search))
                    warps = warps.Where(k => k.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
                var warpList = warps.ToList();
                int total = warpList.Count;
                warpEntries = warpList.Skip(page * UI_PER_PAGE).Take(UI_PER_PAGE).Select(n => new TeleportGUIUI.WarpEntry { Name = n }).ToList();
                hasNext = (page + 1) * UI_PER_PAGE < total;
            }

            _uiState[player.userID] = (mode, page, search);
            bool isAdmin = player.IsAdmin;
            var uiColors = _config.UI?.Colors;
            string dailyLimitText = GetDailyLimitOrCooldownText(player, mode);
            string json = TeleportGUIUI.BuildUI(player, mode, page, search, playerEntries, homeEntries, warpEntries, hasNext, UI_PER_PAGE, isAdmin, dailyLimitText, uiColors);
            TeleportGUIUI.Show(player, json);
        }

        private string GetDailyLimitOrCooldownText(BasePlayer player, string mode)
        {
            var user = GetOrCreateUser(player);
            int limit = 0;
            int used = 0;
            double cooldownEnd = 0;
            if (mode == "teleport")
            {
                limit = GetTPDailyLimit();
                used = user.TPUsesToday;
                cooldownEnd = user.TPCooldownUntil;
            }
            else if (mode == "home")
            {
                limit = GetHomeDailyLimit();
                used = user.HomeUsesToday;
                cooldownEnd = user.HomeCooldownUntil;
            }
            else if (mode == "warp")
            {
                limit = GetWarpDailyLimit();
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
            var player = arg.Connection?.player as BasePlayer;
            if (player == null || player.IsNpc) return;
            var cmd = arg.cmd?.Name?.Trim().ToLowerInvariant();
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
            if (args == null || args.Length == 0)
            {
                SendMessage(player, "Usage: /tp <player name or id>");
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
            if (!_config.AdminsBypass || !player.IsAdmin)
            {
                if (GetTPCooldown() > 0 && user.IsOnTPCooldown(now))
                {
                    SendMessage(player, "TP is on cooldown.");
                    return;
                }
                if (GetTPDailyLimit() > 0 && user.TPUsesToday >= GetTPDailyLimit())
                {
                    SendMessage(player, "Daily TP limit reached.");
                    return;
                }
            }

            var dest = target.transform.position;
            int delaySec = (_config.Admin?.Instant == true && player.IsAdmin) ? 0 : Math.Max(0, GetTPDelay());
            var delay = (_config.AdminsBypass && player.IsAdmin) ? 0 : delaySec;
            if (delay > 0)
            {
                SendMessage(player, "Teleporting in " + delay + " seconds...");
                ServerMgr.Instance?.StartCoroutine(DelayedTeleport(player, dest, delay, () =>
                {
                    user.TPUsesToday++;
                    user.TPCooldownUntil = CurrentTime() + GetTPCooldown();
                }, "teleport"));
            }
            else
            {
                DoTeleport(player, dest);
                user.TPUsesToday++;
                user.TPCooldownUntil = CurrentTime() + GetTPCooldown();
            }
            SaveData();
        }

        private void CmdHome(BasePlayer player, string subCmd, string[] args)
        {
            var user = GetOrCreateUser(player);
            var isSet = string.Equals(subCmd, "sethome", StringComparison.OrdinalIgnoreCase);
            var isDelete = string.Equals(subCmd, "deletehome", StringComparison.OrdinalIgnoreCase);
            if (args == null) args = Array.Empty<string>();

            if (isSet)
            {
                var name = args.Length > 0 ? string.Join(" ", args).Trim() : "home";
                if (string.IsNullOrEmpty(name)) name = "home";
                var max = _config.AdminsBypass && player.IsAdmin ? 999 : GetMaxHomes();
                if (user.Homes.Count >= max && !user.Homes.ContainsKey(name))
                {
                    SendMessage(player, "Max homes (" + max + ") reached.");
                    return;
                }
                user.Homes[name] = TeleportGUIData.Vector3Data.FromVector3(player.transform.position);
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

            var dest = homeData.ToVector3();
            var now = CurrentTime();
            if (!_config.AdminsBypass || !player.IsAdmin)
            {
                if (GetHomeCooldown() > 0 && user.IsOnHomeCooldown(now))
                {
                    SendMessage(player, "Home teleport is on cooldown.");
                    return;
                }
                if (GetHomeDailyLimit() > 0 && user.HomeUsesToday >= GetHomeDailyLimit())
                {
                    SendMessage(player, "Daily home limit reached.");
                    return;
                }
            }

            int delaySec = (_config.Admin?.Instant == true && player.IsAdmin) ? 0 : Math.Max(0, GetHomeDelay());
            var delay = (_config.AdminsBypass && player.IsAdmin) ? 0 : delaySec;
            if (delay > 0)
            {
                SendMessage(player, "Teleporting home in " + delay + " seconds...");
                ServerMgr.Instance?.StartCoroutine(DelayedTeleport(player, dest, delay, () =>
                {
                    user.HomeUsesToday++;
                    user.HomeCooldownUntil = CurrentTime() + GetHomeCooldown();
                }, "home"));
            }
            else
            {
                DoTeleport(player, dest);
                user.HomeUsesToday++;
                user.HomeCooldownUntil = CurrentTime() + GetHomeCooldown();
            }
            SaveData();
        }

        private void CmdWarp(BasePlayer player, string[] args)
        {
            if (_data.WarpPoints == null || _data.WarpPoints.Count == 0)
            {
                SendMessage(player, "No warp points configured.");
                return;
            }
            if (args == null || args.Length == 0)
            {
                SendMessage(player, "Warps: " + string.Join(", ", _data.WarpPoints.Keys));
                return;
            }

            var name = string.Join(" ", args).Trim();
            if (!_data.WarpPoints.TryGetValue(name, out var wp))
            {
                var key = _data.WarpPoints.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
                if (key == null || !_data.WarpPoints.TryGetValue(key, out wp))
                {
                    SendMessage(player, "Warp '" + name + "' not found.");
                    return;
                }
            }

            var user = GetOrCreateUser(player);
            var now = CurrentTime();
            if (!_config.AdminsBypass || !player.IsAdmin)
            {
                if (GetWarpCooldown() > 0 && user.IsOnWarpCooldown(now))
                {
                    SendMessage(player, "Warp is on cooldown.");
                    return;
                }
                if (GetWarpDailyLimit() > 0 && user.WarpUsesToday >= GetWarpDailyLimit())
                {
                    SendMessage(player, "Daily warp limit reached.");
                    return;
                }
            }

            var dest = wp.ToVector3();
            if (dest.sqrMagnitude < 1f)
            {
                SendMessage(player, "Warp '" + name + "' has no position set. Admin: set X,Y,Z in TeleportGUI config (e.g. Outpost, Bandit).");
                return;
            }
            int delaySec = (_config.Admin?.Instant == true && player.IsAdmin) ? 0 : Math.Max(0, GetWarpDelay());
            var delay = (_config.AdminsBypass && player.IsAdmin) ? 0 : delaySec;
            if (delay > 0)
            {
                SendMessage(player, "Warping in " + delay + " seconds...");
                ServerMgr.Instance?.StartCoroutine(DelayedTeleport(player, dest, delay, () =>
                {
                    user.WarpUsesToday++;
                    user.WarpCooldownUntil = CurrentTime() + GetWarpCooldown();
                }, "warp"));
            }
            else
            {
                DoTeleport(player, dest);
                user.WarpUsesToday++;
                user.WarpCooldownUntil = CurrentTime() + GetWarpCooldown();
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

        private static void DoTeleport(BasePlayer player, Vector3 position)
        {
            if (player == null || player.IsDestroyed) return;
            _lastTeleport[player.userID] = player.transform.position;
            player.MovePosition(position);
            player.ClientRPC(RpcTarget.Player("ForcePositionTo", player), position);
        }

        private System.Collections.IEnumerator DelayedTeleport(BasePlayer player, Vector3 position, int delaySeconds, Action onDone, string mode = "teleport")
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
                        if (player.IsConnected)
                            SendMessage(player, "Teleport cancelled (hurt or death).");
                        yield break;
                    }
                    if ((player.transform.position - from).sqrMagnitude > 4f)
                    {
                        if (player.IsConnected)
                            SendMessage(player, "Teleport cancelled (you moved).");
                        yield break;
                    }
                }
                if (_cancelTeleportRequested.Remove(player.userID))
                {
                    if (player != null && player.IsConnected)
                        SendMessage(player, "Teleport cancelled (hurt or death).");
                    yield break;
                }
                if (player != null && !player.IsDestroyed)
                {
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

        public void OnPlayerDie(BasePlayer player)
        {
            if (player == null || player.IsNpc) return;
            if (_config != null && _config.RecordDeathLocation)
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
            if (Instance?._config?.RecordDeathLocation == true)
                DeathLocations[userId] = position;
        }
    }
}
