using System;
using System.Collections.Generic;
using System.Reflection;
using Facepunch;
using UnityEngine;

namespace AdminTime
{
    /// <summary>
    /// Harmony mod: per-player admin time and weather (mytime, myweather, storm).
    /// All players can use /mytime and /myweather from chat or F1. /storm remains admin-only per config.
    /// </summary>
    public class AdminTimeMod : IHarmonyModHooks
    {
        public static AdminTimeMod Instance { get; private set; }

        private static readonly HashSet<string> AdminScalarArgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "clouds", "fog", "rain", "wind"
        };

        private static readonly HashSet<string> ReplicatedScalarArgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "brightness"
        };

        private readonly Dictionary<ulong, Dictionary<string, float>> _players = new Dictionary<ulong, Dictionary<string, float>>();

        private ConsoleSystem.Command _mytimeCmd;
        private ConsoleSystem.Command _myweatherCmd;
        private ConsoleSystem.Command _stormCmd;
        private ConsoleSystem.Command _myweatherClearCmd;
        private static object _replicatedList;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            AdminTimeConfig.LoadConfig();
            RegisterCommands();
            UnityEngine.Debug.Log("[AdminTime] Harmony mod loaded. Use /mytime and /myweather to set personal time/weather (stored until disconnect or server restart).");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            UnregisterCommands();
            foreach (var entry in _players)
            {
                var player = BasePlayer.FindByID(entry.Key);
                if (player != null) Toggle(player, false);
            }
            _players.Clear();
            Instance = null;
            UnityEngine.Debug.Log("[AdminTime] Harmony mod unloaded.");
        }

        /// <summary>Called when a player connects. We do not apply any overrides here; they see server time/weather until they use /mytime or /myweather.</summary>
        public void OnPlayerConnected(BasePlayer player)
        {
            // No-op: new connections see server time/weather. Overrides are applied only when they set them via /mytime or /myweather.
        }

        /// <summary>Clear this player's cached time/weather overrides when they disconnect so they must set them again on next connect.</summary>
        public void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;
            _players.Remove(player.userID);
        }

        private bool EventTerritory(Vector3 position)
        {
            if (AdminTimeConfig.Config?.BlockInEventTerritory != true) return false;
            return AdminTimeConfig.IsInBlockedPosition(position);
        }

        private void RegisterCommands()
        {
            try
            {
                _mytimeCmd = new ConsoleSystem.Command
                {
                    Name = "mytime",
                    FullName = "global.mytime",
                    Variable = true,
                    ServerAdmin = false,
                    ServerUser = true,
                    Replicated = true,
                    Call = CmdMytime
                };
                _myweatherCmd = new ConsoleSystem.Command
                {
                    Name = "myweather",
                    FullName = "global.myweather",
                    Variable = true,
                    ServerAdmin = false,
                    ServerUser = true,
                    Replicated = true,
                    Call = CmdMyweather
                };
                _stormCmd = new ConsoleSystem.Command
                {
                    Name = "storm",
                    FullName = "global.storm",
                    Variable = true,
                    ServerAdmin = false,
                    ServerUser = true,
                    Replicated = true,
                    Call = CmdStorm
                };
                _myweatherClearCmd = new ConsoleSystem.Command
                {
                    Name = "myweather.clear",
                    FullName = "global.myweather.clear",
                    Variable = false,
                    ServerAdmin = false,
                    ServerUser = true,
                    Replicated = true,
                    Call = CmdMyweatherClear
                };

                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                if (dict != null)
                {
                    if (!dict.ContainsKey("global.mytime")) dict["global.mytime"] = _mytimeCmd;
                    if (!dict.ContainsKey("global.myweather")) dict["global.myweather"] = _myweatherCmd;
                    if (!dict.ContainsKey("global.storm")) dict["global.storm"] = _stormCmd;
                    if (!dict.ContainsKey("global.myweather.clear")) dict["global.myweather.clear"] = _myweatherClearCmd;
                }
                if (globalDict != null)
                {
                    if (!globalDict.ContainsKey("mytime")) globalDict["mytime"] = _mytimeCmd;
                    if (!globalDict.ContainsKey("myweather")) globalDict["myweather"] = _myweatherCmd;
                    if (!globalDict.ContainsKey("storm")) globalDict["storm"] = _stormCmd;
                    if (!globalDict.ContainsKey("myweather.clear")) globalDict["myweather.clear"] = _myweatherClearCmd;
                }

                // Add to replicated list so clients who join after server start receive commands (fixes "unknown command" until reload).
                var serverType = typeof(ConsoleSystem.Index.Server);
                var prop = serverType.GetProperty("Replicated", BindingFlags.Public | BindingFlags.Static);
                if (prop != null)
                {
                    var list = prop.GetValue(null) as System.Collections.IList;
                    if (list != null)
                    {
                        if (!list.Contains(_mytimeCmd)) list.Add(_mytimeCmd);
                        if (!list.Contains(_myweatherCmd)) list.Add(_myweatherCmd);
                        if (!list.Contains(_stormCmd)) list.Add(_stormCmd);
                        if (!list.Contains(_myweatherClearCmd)) list.Add(_myweatherClearCmd);
                        _replicatedList = list;
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[AdminTime] Command registration failed: " + ex.Message);
            }
        }

        private void UnregisterCommands()
        {
            try
            {
                if (_replicatedList is System.Collections.IList list)
                {
                    if (_mytimeCmd != null) list.Remove(_mytimeCmd);
                    if (_myweatherCmd != null) list.Remove(_myweatherCmd);
                    if (_stormCmd != null) list.Remove(_stormCmd);
                    if (_myweatherClearCmd != null) list.Remove(_myweatherClearCmd);
                }
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                if (dict != null)
                {
                    dict.Remove("global.mytime");
                    dict.Remove("global.myweather");
                    dict.Remove("global.storm");
                    dict.Remove("global.myweather.clear");
                }
                if (globalDict != null)
                {
                    globalDict.Remove("mytime");
                    globalDict.Remove("myweather");
                    globalDict.Remove("storm");
                    globalDict.Remove("myweather.clear");
                }
            }
            catch { }
            _replicatedList = null;
        }

        private void Reply(BasePlayer player, string message)
        {
            if (player != null && player.IsConnected) player.ChatMessage(message);
        }

        private static string[] ToStringArray(StringView[] args)
        {
            if (args == null || args.Length == 0) return Array.Empty<string>();

            var result = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
                result[i] = args[i].ToString();
            return result;
        }

        private void CmdMytime(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null || player.IsNpc) return;
            RunMytime(player, ToStringArray(arg.Args));
        }

        /// <summary>Runs mytime logic. Used by both console command and chat /mytime.</summary>
        public void RunMytime(BasePlayer player, string[] args)
        {
            if (player == null || player.IsNpc) return;

            float value = 12f;
            if (args != null && args.Length >= 1 && float.TryParse(args[0], out float argVal))
                value = argVal >= 0f ? Mathf.Clamp(argVal, 0f, 24f) : -1f;

            if (!_players.TryGetValue(player.userID, out var dict))
                _players[player.userID] = dict = new Dictionary<string, float>();

            if (value < 0f)
            {
                if (dict.Remove("time") && dict.Count == 0) _players.Remove(player.userID);
            }
            else
                dict["time"] = value;

            ChangeTime(player, value);
            if (value < 0f)
                Reply(player, "Time override cleared. You now see server time.");
            else
                Reply(player, "Time set to " + value.ToString("0.##") + " (hours).");
        }

        private void CmdMyweather(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null || player.IsNpc) return;
            RunMyweather(player, ToStringArray(arg.Args));
        }

        /// <summary>Runs myweather logic. Used by both console command and chat /myweather.</summary>
        public void RunMyweather(BasePlayer player, string[] args)
        {
            if (player == null || player.IsNpc) return;

            string key = (args != null && args.Length > 0) ? args[0].ToLowerInvariant() : string.Empty;
            if (string.IsNullOrEmpty(key))
            {
                Reply(player, "Use: /myweather <clouds|fog|rain|wind|brightness|clear> <value(s)>");
                return;
            }

            if (key == "clear")
            {
                ClearAllWeather(player);
                Reply(player, "Your personal weather overrides have been cleared.");
                return;
            }

            if (ReplicatedScalarArgs.Contains(key))
            {
                if (!(args != null && args.Length >= 2 && float.TryParse(args[1], out float argVal)))
                {
                    Reply(player, "Use: /myweather brightness <0..1> or /myweather brightness -1 to reset");
                    return;
                }
                if (argVal < 0f)
                {
                    if (_players.TryGetValue(player.userID, out var d) && d.Remove(key) && d.Count == 0)
                        _players.Remove(player.userID);
                    SendVar(player, "weather.atmosphere_brightness", "-1");
                    Reply(player, "Brightness override cleared.");
                    return;
                }
                float val = Mathf.Clamp01(argVal);
                if (EventTerritory(player.transform.position)) return;
                if (!_players.TryGetValue(player.userID, out var d2))
                    _players[player.userID] = d2 = new Dictionary<string, float>();
                d2[key] = val;
                SendVar(player, "weather.atmosphere_brightness", val.ToString("0.###"));
                Reply(player, "Set brightness to " + val.ToString("0.###"));
                return;
            }

            if (!AdminScalarArgs.Contains(key))
            {
                Reply(player, "Use: /myweather <clouds|fog|rain|wind|brightness|clear> <value(s)>");
                return;
            }

            float a = -1f;
            if (args != null && args.Length >= 2 && float.TryParse(args[1], out float parsed)) a = parsed;
            float scalar = a >= 0f ? Mathf.Clamp(a, 0f, 1f) : -1f;

            if (!_players.TryGetValue(player.userID, out var pdict))
                _players[player.userID] = pdict = new Dictionary<string, float>();
            if (scalar < 0f)
            {
                if (pdict.Remove(key) && pdict.Count == 0) _players.Remove(player.userID);
            }
            else
                pdict[key] = scalar;
            ChangeWeather(player, key, scalar);
        }

        private void CmdStorm(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            RunStorm(player, ToStringArray(arg.Args));
        }

        /// <summary>Runs storm logic. Used by both console command and chat /storm. Admin-only when StormAdminOnly is true.</summary>
        public void RunStorm(BasePlayer player, string[] args)
        {
            if (AdminTimeConfig.Config?.StormAdminOnly == true && (player == null || !player.IsAdmin))
            {
                Reply(player, "You must be an admin to use /storm (global).");
                return;
            }
            if (args == null || args.Length == 0)
            {
                Reply(player, "Use: /storm <0-1|off|default> - Sets thunder/lightning intensity (0=off, 1=max, default=natural)");
                return;
            }

            string input = args[0].ToLowerInvariant();
            float intensity;
            if (input == "off" || input == "0") intensity = 0f;
            else if (input == "on") intensity = 1f;
            else if (input == "default" || input == "reset") intensity = -1f;
            else if (!float.TryParse(input, out intensity))
            {
                Reply(player, "Use: /storm <0-1|off|default>");
                return;
            }
            if (intensity >= 0f) intensity = Mathf.Clamp01(intensity);

            ConsoleSystem.Run(ConsoleSystem.Option.Server, "weather.thunder", intensity.ToString("0.###"));

            if (intensity == 0f) Reply(player, "Storm/thunder disabled (global).");
            else if (intensity < 0f) Reply(player, "Storm/thunder reset to natural weather (global).");
            else Reply(player, "Storm/thunder intensity set to " + intensity.ToString("0.###") + " (global).");
        }

        private void CmdMyweatherClear(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null || player.IsNpc) return;
            RunMyweatherClear(player);
        }

        /// <summary>Clears all weather overrides. Used by console and chat /myweather.clear.</summary>
        public void RunMyweatherClear(BasePlayer player)
        {
            if (player == null || player.IsNpc) return;
            ClearAllWeather(player);
            Reply(player, "Your personal weather overrides have been cleared.");
        }

        /// <summary>Returns true if the command was handled (caller should suppress chat).</summary>
        public bool RunChatCommand(BasePlayer player, string cmd, string[] args)
        {
            if (player == null || player.IsNpc || string.IsNullOrEmpty(cmd)) return false;
            cmd = cmd.ToLowerInvariant();
            if (cmd == "mytime")
            {
                RunMytime(player, args ?? Array.Empty<string>());
                return true;
            }
            if (cmd == "myweather")
            {
                RunMyweather(player, args ?? Array.Empty<string>());
                return true;
            }
            if (cmd == "myweather.clear")
            {
                RunMyweatherClear(player);
                return true;
            }
            if (cmd == "storm")
            {
                RunStorm(player, args ?? Array.Empty<string>());
                return true;
            }
            return false;
        }

        private void ChangeWeather(BasePlayer player, string arg, float value)
        {
            if (value != -1f && EventTerritory(player.transform.position)) return;
            if (player.IsAdmin)
            {
                player.SendConsoleCommand("admin" + arg, value.ToString("0.###"));
                return;
            }
            if (player.IsFlying)
            {
                Reply(player, "You cannot use this command while flying.");
                return;
            }
            player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
            player.SendNetworkUpdateImmediate();
            player.SendConsoleCommand("admin" + arg, value.ToString("0.###"));
            player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
            player.SendNetworkUpdateImmediate();
        }

        private void ChangeTime(BasePlayer player, float time)
        {
            if (time != -1f && EventTerritory(player.transform.position)) return;
            if (player.IsAdmin)
            {
                player.SendConsoleCommand("admintime", time.ToString("0.##"));
                return;
            }
            if (player.IsFlying)
            {
                Reply(player, "You cannot use this command while flying.");
                return;
            }
            player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
            player.SendNetworkUpdateImmediate();
            player.SendConsoleCommand("admintime", time.ToString("0.##"));
            player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
            player.SendNetworkUpdateImmediate();
        }

        private void ChangeTimeForAPI(BasePlayer player, float time)
        {
            if (player.IsAdmin)
            {
                player.SendConsoleCommand("admintime", time.ToString("0.##"));
                return;
            }
            if (player.IsFlying)
            {
                Reply(player, "You cannot use this command while flying.");
                return;
            }
            player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
            player.SendNetworkUpdateImmediate();
            player.SendConsoleCommand("admintime", time.ToString("0.##"));
            player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
            player.SendNetworkUpdateImmediate();
        }

        private void Toggle(BasePlayer player, bool enable)
        {
            if (player == null || !player.IsValid()) return;
            if (!_players.TryGetValue(player.userID, out var dict)) return;
            foreach (var kvp in dict)
            {
                if (kvp.Key == "time")
                    ChangeTime(player, enable ? kvp.Value : -1f);
                else if (ReplicatedScalarArgs.Contains(kvp.Key))
                {
                    if (enable) SendVar(player, "weather.atmosphere_brightness", kvp.Value.ToString("0.###"));
                    else SendVar(player, "weather.atmosphere_brightness", "-1");
                }
                else if (AdminScalarArgs.Contains(kvp.Key))
                    ChangeWeather(player, kvp.Key, enable ? kvp.Value : -1f);
            }
        }

        private void ClearAllWeather(BasePlayer player)
        {
            foreach (string k in AdminScalarArgs)
                ChangeWeather(player, k, -1f);
            SendVar(player, "weather.atmosphere_brightness", "-1");
            if (_players.TryGetValue(player.userID, out var dict))
            {
                foreach (string k in AdminScalarArgs) dict.Remove(k);
                foreach (string k in ReplicatedScalarArgs) dict.Remove(k);
                if (dict.Count == 0 || (dict.Count == 1 && !dict.ContainsKey("time")))
                    _players.Remove(player.userID);
            }
        }

        private void SendVar(BasePlayer player, string command, string value)
        {
            if (value != "-1" && EventTerritory(player.transform.position)) return;
            try
            {
                Type netType = Type.GetType("Network.Net, Assembly-CSharp");
                if (netType == null)
                {
                    foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        netType = a.GetType("Network.Net");
                        if (netType != null) break;
                    }
                }
                if (netType == null) return;
                PropertyInfo svProp = netType.GetProperty("sv", BindingFlags.Public | BindingFlags.Static);
                if (svProp?.GetValue(null) == null) return;
                object sv = svProp.GetValue(null);
                MethodInfo isConnected = sv.GetType().GetMethod("IsConnected");
                if (isConnected != null && !(bool)isConnected.Invoke(sv, null)) return;
                MethodInfo startWrite = sv.GetType().GetMethod("StartWrite");
                if (startWrite == null) return;
                object netWrite = startWrite.Invoke(sv, null);
                if (netWrite == null) return;
                Type msgType = Type.GetType("Network.Message, Assembly-CSharp") ?? sv.GetType().Assembly.GetType("Network.Message");
                if (msgType == null) return;
                Type messageTypeEnum = msgType.GetNestedType("Type", BindingFlags.Public | BindingFlags.Static);
                if (messageTypeEnum == null) return;
                FieldInfo consoleReplicatedVars = messageTypeEnum.GetField("ConsoleReplicatedVars", BindingFlags.Public | BindingFlags.Static);
                if (consoleReplicatedVars == null) return;
                object packetId = consoleReplicatedVars.GetValue(null);
                netWrite.GetType().GetMethod("PacketID")?.Invoke(netWrite, new[] { packetId });
                netWrite.GetType().GetMethod("Int32", new[] { typeof(int) })?.Invoke(netWrite, new object[] { 1 });
                netWrite.GetType().GetMethod("String", new[] { typeof(string) })?.Invoke(netWrite, new object[] { command });
                netWrite.GetType().GetMethod("String", new[] { typeof(string) })?.Invoke(netWrite, new object[] { value });
                object sendInfo = Activator.CreateInstance(Type.GetType("Network.SendInfo, Assembly-CSharp") ?? sv.GetType().Assembly.GetType("Network.SendInfo"), player.net.connection);
                netWrite.GetType().GetMethod("Send").Invoke(netWrite, new[] { sendInfo });
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[AdminTime] SendVar failed: " + ex.Message);
            }
        }

        private static bool IsSteamId(ulong id) => id >= 76561197960265728UL;

        #region Public API (for Oxide plugins via reflection)

        public static bool SetPlayerTime(BasePlayer player, float time)
        {
            if (player == null || !IsSteamId((ulong)player.userID)) return false;
            try
            {
                var mod = Instance;
                if (mod == null) return false;
                if (!mod._players.TryGetValue(player.userID, out var dict))
                    mod._players[player.userID] = dict = new Dictionary<string, float>();
                if (time < 0f)
                {
                    if (dict.Remove("time") && dict.Count == 0) mod._players.Remove(player.userID);
                }
                else
                    dict["time"] = Mathf.Clamp(time, 0f, 24f);
                mod.ChangeTimeForAPI(player, time);
                return true;
            }
            catch { return false; }
        }

        public static float GetPlayerTime(BasePlayer player)
        {
            if (player == null || !IsSteamId((ulong)player.userID)) return -1f;
            if (Instance?._players.TryGetValue(player.userID, out var dict) == true && dict.TryGetValue("time", out float time))
                return time;
            return -1f;
        }

        public static bool HasTimeOverride(BasePlayer player)
        {
            if (player == null || !IsSteamId((ulong)player.userID)) return false;
            return Instance?._players.TryGetValue(player.userID, out var dict) == true && dict.ContainsKey("time");
        }

        public static bool ResetPlayerTime(BasePlayer player)
        {
            return SetPlayerTime(player, -1f);
        }

        #endregion
    }
}
