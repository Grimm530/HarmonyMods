using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace PlaytimeTrackerHarmony
{
    public class Hash<TKey, TValue> : Dictionary<TKey, TValue>
    {
        public new TValue this[TKey key]
        {
            get => TryGetValue(key, out var value) ? value : default;
            set => base[key] = value;
        }
    }

    public class PlaytimeTrackerMod : IHarmonyModHooks
    {
        public static PlaytimeTrackerMod Instance { get; private set; }
        public const string AppDomainApiKey = "PlaytimeTracker_ApiType";
        public const int VersionMajor = 0;
        public const int VersionMinor = 2;
        public const int VersionPatch = 21;

        internal ConfigData Configuration;
        internal StoredData storedData;

        private readonly Dictionary<string, PlayerTracker> _activeTrackers = new Dictionary<string, PlayerTracker>();
        private readonly Dictionary<string, string> _lang = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ConsoleSystem.Command> _commands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "playtime", "refer" };

        private string _root;
        private string _configPath;
        private string _dataPath;
        private string _dataDir;
        private Action _permissionsReadyCallback;
        private GameObject _runner;
        private double _nextTopUpdate;
        private readonly List<StoredData.UserData> _topList = new List<StoredData.UserData>();

        internal Action<string, double> TimeReward;
        internal Action<string, string> ReferralReward;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            _root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            _configPath = Path.Combine(_root, "HarmonyConfig", "PlaytimeTracker.json");
            _dataDir = Path.Combine(_root, "HarmonyData", "PlaytimeTracker");
            Directory.CreateDirectory(_dataDir);
            _dataPath = Path.Combine(_dataDir, "user_data.json");

            LoadLang();
            LoadConfig();
            LoadData();

            TimeReward = IssueReward;
            ReferralReward = IssueReward;

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(PlaytimeTrackerMod)); }
            catch { }

            _permissionsReadyCallback = RegisterPermissions;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            EnsureRunner();
            RegisterConsoleCommands();

            Debug.Log($"[PlaytimeTracker] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[PlaytimeTracker] -> Config: HarmonyConfig/PlaytimeTracker.json");
            Debug.Log("[PlaytimeTracker] -> Data: HarmonyData/PlaytimeTracker/user_data.json");
        }

        internal void OnServerInitialized()
        {
            RegisterPermissions();
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null && player.IsConnected)
                    OnUserConnected(player);
            }
            Debug.Log("[PlaytimeTracker] OK: Server initialized.");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            try
            {
                if (_permissionsReadyCallback != null)
                    PermissionsBridge.UnregisterReadyCallback(_permissionsReadyCallback);
            }
            catch { }

            foreach (var tracker in _activeTrackers.Values)
                tracker.OnUserDisconnected();
            _activeTrackers.Clear();
            SaveData();

            UnregisterConsoleCommands();
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); }
            catch { }

            TimeReward = null;
            ReferralReward = null;
            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }
            Instance = null;
            Debug.Log("[PlaytimeTracker] OK: Unloaded.");
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("PlaytimeTracker_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<PlaytimeTrackerRunner>().Begin(this);
        }

        private void RegisterPermissions()
        {
            if (Configuration?.Reward?.CustomMultipliers == null) return;
            foreach (string perm in Configuration.Reward.CustomMultipliers.Keys)
                PermissionsBridge.RegisterPermission(perm);
        }

        public void OnUserConnected(BasePlayer player)
        {
            if (player == null) return;
            string id = player.UserIDString;
            if (_activeTrackers.TryGetValue(id, out var existing))
                existing.OnUserConnected(player);
            else
            {
                var tracker = new PlayerTracker(this, player);
                _activeTrackers[id] = tracker;
                storedData.OnUserConnected(player);
            }
        }

        public void OnUserDisconnected(BasePlayer player)
        {
            if (player == null) return;
            string id = player.UserIDString;
            if (_activeTrackers.TryGetValue(id, out var tracker))
            {
                tracker.OnUserDisconnected();
                _activeTrackers.Remove(id);
            }
            storedData.OnUserDisconnected(player);
        }

        public void OnNewSave()
        {
            const string groupName = "topfive";
            const int topAmount = 5;
            foreach (var p in BasePlayer.allPlayerList)
            {
                if (p == null) continue;
                PermissionsBridge.RemoveUserGroup(p.UserIDString, groupName);
            }
            var topPlayers = storedData.GetTopPlayers(topAmount);
            foreach (var player in topPlayers)
            {
                PermissionsBridge.AddUserGroup(player.Key, groupName);
                Debug.Log($"[PlaytimeTracker] Added {player.Value.displayName} ({player.Key}) to group {groupName}");
            }
        }

        internal void ProcessRewards()
        {
            foreach (var tracker in _activeTrackers.Values)
                tracker.ProcessReward();
        }

        internal void TickAllPositions(float interval)
        {
            foreach (var tracker in _activeTrackers.Values)
                tracker.TickPosition(interval);
        }

        internal static double CurrentTime => DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds;

        private static string FormatTime(double time)
        {
            TimeSpan dateDifference = TimeSpan.FromSeconds((float)time);
            int hours = dateDifference.Hours + (dateDifference.Days * 24);
            return string.Format("{0:00}h:{1:00}m:{2:00}s", hours, dateDifference.Minutes, dateDifference.Seconds);
        }

        private static string FormatLastSeen(double lastSeen)
        {
            if (lastSeen <= 0) return "Never";
            DateTime lastSeenDate = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(lastSeen);
            TimeSpan timeDiff = DateTime.UtcNow - lastSeenDate;
            if (timeDiff.TotalDays >= 1) return $"{(int)timeDiff.TotalDays} days ago";
            if (timeDiff.TotalHours >= 1) return $"{(int)timeDiff.TotalHours} hours ago";
            if (timeDiff.TotalMinutes >= 1) return $"{(int)timeDiff.TotalMinutes} minutes ago";
            return "Just now";
        }

        private void IssueReward(string id, double amount)
        {
            float multiplier = 1f;
            if (Configuration.Reward.CustomMultipliers != null)
            {
                foreach (var kvp in Configuration.Reward.CustomMultipliers)
                {
                    if (PermissionsBridge.UserHasPermission(id, kvp.Key) && kvp.Value > multiplier)
                        multiplier = kvp.Value;
                }
            }
            amount *= multiplier;
            RewardPluginCall(id, amount);
            var user = BasePlayer.FindAwakeOrSleeping(id);
            if (user != null && user.IsConnected)
                Message(user, $"Reward.Given.{Configuration.Reward.Plugin}", (int)amount);
        }

        private void IssueReward(string referrer, string referee)
        {
            IssueReward(referrer, Configuration.Reward.Referral.InviteReward);
            IssueReward(referee, Configuration.Reward.Referral.JoinReward);
        }

        private void RewardPluginCall(string id, double amount)
        {
            switch (Configuration.Reward.Plugin)
            {
                case "ServerRewards":
                    CallModApi("ServerRewards_ApiType", "AddPoints", ulong.Parse(id), (int)amount);
                    break;
                case "Economics":
                    CallModApi("Economics_ApiType", "Deposit", id, amount);
                    break;
            }
        }

        private static void CallModApi(string key, string method, params object[] args)
        {
            try
            {
                var api = AppDomain.CurrentDomain.GetData(key) as Type;
                if (api == null) return;
                var types = args.Select(a => a?.GetType()).ToArray();
                var mi = api.GetMethod(method, BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic, null, types, null)
                    ?? api.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic)
                        .FirstOrDefault(m => m.Name == method && m.GetParameters().Length == args.Length);
                mi?.Invoke(null, args);
            }
            catch (Exception ex) { Debug.LogWarning("[PlaytimeTracker] Reward API: " + ex.Message); }
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || string.IsNullOrEmpty(command) || !_chatCommands.Contains(command))
                return false;
            if (command.Equals("playtime", StringComparison.OrdinalIgnoreCase))
                CmdPlaytime(player, args ?? Array.Empty<string>());
            else if (command.Equals("refer", StringComparison.OrdinalIgnoreCase))
                CmdRefer(player, args ?? Array.Empty<string>());
            return true;
        }

        private void CmdPlaytime(BasePlayer user, string[] args)
        {
            if (args.Length == 0)
            {
                double time = storedData.GetPlayTimeForPlayer(user.UserIDString);
                double afkTime = storedData.GetAFKTimeForPlayer(user.UserIDString);
                if (time == 0 && afkTime == 0)
                    Message(user, "Error.NoPlaytimeStored");
                else if (Configuration.General.TrackAFK)
                    Message(user, "Playtime.Both", FormatTime(time), FormatTime(afkTime));
                else
                    Message(user, "Playtime.Single", FormatTime(time));
                Message(user, "Playtime.Help");
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "wipe":
                    if (!user.IsAdmin) return;
                    storedData = new StoredData();
                    SaveData();
                    user.ChatMessage("Wiped data");
                    return;
                case "grant":
                    if (!user.IsAdmin) return;
                    if (storedData._userData.Count > 0)
                    {
                        user.ChatMessage("Granting top 5.");
                        OnNewSave();
                    }
                    else user.ChatMessage("No data exist");
                    return;
                case "lastseen":
                    if (!user.IsAdmin)
                    {
                        user.ChatMessage("You don't have permission to use this command");
                        return;
                    }
                    if (args.Length < 2)
                    {
                        user.ChatMessage("Usage: /playtime lastseen <playername>");
                        return;
                    }
                    var target = FindPlayer(args[1]);
                    if (target == null)
                    {
                        Message(user, "Error.NoPlayerFound", args[1]);
                        return;
                    }
                    if (storedData._userData.TryGetValue(target.UserIDString, out var userData))
                        user.ChatMessage($"{target.displayName} was last seen: {FormatLastSeen(userData.lastSeen)}");
                    else
                        user.ChatMessage("No data found for this player");
                    return;
                case "top":
                    string str = Msg("Top.Title");
                    if (CurrentTime > _nextTopUpdate)
                    {
                        storedData.GetTopPlayTime(_topList);
                        _nextTopUpdate = CurrentTime + 60f;
                    }
                    int count = Math.Min(Configuration.General.TopCount, _topList.Count);
                    for (int i = 0; i < count; i++)
                        str += string.Format(Msg("Top.Format"), _topList[i].displayName, FormatTime(_topList[i].playtime));
                    user.ChatMessage(str);
                    return;
                default:
                    if (user.IsAdmin)
                    {
                        var other = FindPlayer(args[0]);
                        if (other == null)
                        {
                            Message(user, "Error.NoPlayerFound", args[0]);
                            return;
                        }
                        double time = storedData.GetPlayTimeForPlayer(other.UserIDString);
                        if (time == 0)
                        {
                            Message(user, "Error.NoTimeStored");
                            return;
                        }
                        user.ChatMessage($"{other.displayName} - {FormatTime(time)}");
                    }
                    else Message(user, "Error.InvalidSyntax");
                    break;
            }
        }

        private void CmdRefer(BasePlayer user, string[] args)
        {
            if (!Configuration.Reward.Referral.Enabled)
            {
                Message(user, "Referral.Disabled");
                return;
            }
            if (args.Length == 0)
            {
                Message(user, "Referral.Help");
                return;
            }
            if (storedData.HasBeenReferred(user.UserIDString))
            {
                Message(user, "Referral.Submitted");
                return;
            }
            var referrer = FindPlayer(args[0]);
            if (referrer == null)
            {
                Message(user, "Error.NoPlayerFound", args[0]);
                return;
            }
            if (referrer.UserIDString.Equals(user.UserIDString))
            {
                Message(user, "Referral.Self");
                return;
            }
            storedData.ReferPlayer(referrer.UserIDString, user.UserIDString);
            Message(user, "Referral.Accepted");
            if (referrer.IsConnected)
                Message(referrer, "Referral.Acknowledged", user.displayName);
        }

        internal void CmdRestoreNames(BasePlayer player)
        {
            if (player != null && !player.IsAdmin) return;
            int missing = 0, restored = 0;
            foreach (var kvp in storedData._userData)
            {
                if (!string.IsNullOrEmpty(kvp.Value.displayName)) continue;
                missing++;
                var found = BasePlayer.FindAwakeOrSleeping(kvp.Key);
                if (found != null && !string.IsNullOrEmpty(found.displayName))
                {
                    restored++;
                    kvp.Value.displayName = found.displayName;
                }
                else kvp.Value.displayName = "Unnamed";
            }
            Reply(player, $"Restored {restored}/{missing} names");
        }

        internal void CmdCleanup(BasePlayer player)
        {
            if (player != null && !player.IsAdmin) return;
            int cleaned = storedData._userData.Count;
            SaveData();
            Reply(player, $"Data cleanup completed for {cleaned} players. Duplicate fields will be removed on next save.");
        }

        private static BasePlayer FindPlayer(string nameOrId)
        {
            if (string.IsNullOrEmpty(nameOrId)) return null;
            var byId = BasePlayer.FindAwakeOrSleeping(nameOrId);
            if (byId != null) return byId;
            BasePlayer exact = null, partial = null;
            foreach (var list in new[] { BasePlayer.activePlayerList, BasePlayer.sleepingPlayerList })
            {
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (p == null) continue;
                    if (string.Equals(p.displayName, nameOrId, StringComparison.OrdinalIgnoreCase))
                        exact ??= p;
                    else if (partial == null && p.displayName != null &&
                             p.displayName.IndexOf(nameOrId, StringComparison.OrdinalIgnoreCase) >= 0)
                        partial = p;
                }
            }
            return exact ?? partial;
        }

        // ---- API ----
        public static object GetPlayTime(string id)
        {
            double time = Instance?.storedData?.GetPlayTimeForPlayer(id) ?? 0;
            return time == 0 ? null : (object)time;
        }

        public static object GetAFKTime(string id)
        {
            double time = Instance?.storedData?.GetAFKTimeForPlayer(id) ?? 0;
            return time == 0 ? null : (object)time;
        }

        public static object GetReferrals(string id)
        {
            int amount = Instance?.storedData?.GetReferralsForPlayer(id) ?? 0;
            return amount == 0 ? null : (object)amount;
        }

        public static object GetLastSeen(string id)
        {
            if (Instance?.storedData?._userData == null) return null;
            return Instance.storedData._userData.TryGetValue(id, out var d) ? (object)d.lastSeen : null;
        }

        public static object GetDisplayName(string id)
        {
            if (Instance?.storedData?._userData == null) return null;
            if (!Instance.storedData._userData.TryGetValue(id, out var d) || string.IsNullOrEmpty(d.displayName))
                return null;
            return d.displayName;
        }

        public static object Call(string method, params object[] args)
        {
            if (string.IsNullOrEmpty(method)) return null;
            try
            {
                int count = args?.Length ?? 0;
                var mi = typeof(PlaytimeTrackerMod).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == method && m.GetParameters().Length == count);
                return mi?.Invoke(null, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlaytimeTracker] Call(" + method + "): " + (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }

        private void RegisterConsoleCommands()
        {
            UnregisterConsoleCommands();
            RegisterConsole("ptt.restorenames", arg => CmdRestoreNames(arg?.Player()), true);
            RegisterConsole("ptt.cleanup", arg => CmdCleanup(arg?.Player()), true);
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler, bool serverAdmin)
        {
            bool hasDot = name.Contains(".");
            string parent = "", cmdName = name, fullName = hasDot ? name : "global." + name;
            if (hasDot)
            {
                var parts = name.Split(new[] { '.' }, 2);
                parent = parts[0];
                cmdName = parts[1];
            }
            var cmd = new ConsoleSystem.Command
            {
                Name = cmdName,
                Parent = parent,
                FullName = fullName,
                Variable = false,
                ServerAdmin = serverAdmin,
                ServerUser = true,
                AllowRunFromServer = true,
                Replicated = false,
                Call = a => { try { handler(a); } catch (Exception ex) { Debug.LogWarning("[PlaytimeTracker] " + name + ": " + ex.Message); } }
            };
            try
            {
                ConsoleSystem.Index.Server.Dict[fullName] = cmd;
                if (!hasDot) ConsoleSystem.Index.Server.GlobalDict[cmdName] = cmd;
                _commands.Add(cmd);
            }
            catch { }
        }

        private void UnregisterConsoleCommands()
        {
            try
            {
                foreach (var cmd in _commands)
                {
                    ConsoleSystem.Index.Server.Dict?.Remove(cmd.FullName);
                    if (string.IsNullOrEmpty(cmd.Parent))
                        ConsoleSystem.Index.Server.GlobalDict?.Remove(cmd.Name);
                }
            }
            catch { }
            _commands.Clear();
        }

        private void LoadLang()
        {
            foreach (var kv in DefaultMessages)
                _lang[kv.Key] = kv.Value;
            try
            {
                var path = Path.Combine(_root, "HarmonyLanguage", "PlaytimeTracker.json");
                if (!File.Exists(path)) return;
                var extra = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (extra == null) return;
                foreach (var kv in extra)
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                        _lang[kv.Key] = kv.Value;
            }
            catch { }
        }

        private string Msg(string key) => _lang.TryGetValue(key, out var v) ? v : key;

        private void Message(BasePlayer user, string key, params object[] args)
        {
            string text = Msg(key);
            if (args != null && args.Length > 0)
            {
                try { text = string.Format(text, args); } catch { }
            }
            user?.ChatMessage(text);
        }

        private static void Reply(BasePlayer player, string msg)
        {
            if (player != null && player.IsConnected) player.ChatMessage(msg);
            else Debug.Log("[PlaytimeTracker] " + msg);
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                    Configuration = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(_configPath));
            }
            catch (Exception ex) { Debug.LogWarning("[PlaytimeTracker] Config load: " + ex.Message); }

            if (Configuration?.General == null || Configuration.Reward == null)
                Configuration = ConfigData.Default();
            SaveConfig();
        }

        private void SaveConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(Configuration, Formatting.Indented));
            }
            catch (Exception ex) { Debug.LogWarning("[PlaytimeTracker] Config save: " + ex.Message); }
        }

        internal void SaveData()
        {
            try
            {
                File.WriteAllText(_dataPath, JsonConvert.SerializeObject(storedData, Formatting.Indented));
            }
            catch (Exception ex) { Debug.LogWarning("[PlaytimeTracker] SaveData: " + ex.Message); }
        }

        private void LoadData()
        {
            if (File.Exists(_dataPath))
            {
                try
                {
                    storedData = JsonConvert.DeserializeObject<StoredData>(File.ReadAllText(_dataPath)) ?? new StoredData();
                    return;
                }
                catch (Exception ex) { Debug.LogWarning("[PlaytimeTracker] Data load: " + ex.Message); }
            }

            string oxideNew = Path.Combine(_root, "oxide", "data", "PlaytimeTracker", "user_data.json");
            if (File.Exists(oxideNew))
            {
                try
                {
                    storedData = JsonConvert.DeserializeObject<StoredData>(File.ReadAllText(oxideNew)) ?? new StoredData();
                    SaveData();
                    Debug.Log("[PlaytimeTracker] Migrated oxide/data/PlaytimeTracker/user_data.json");
                    return;
                }
                catch (Exception ex) { Debug.LogWarning("[PlaytimeTracker] Oxide data migrate: " + ex.Message); }
            }

            string oldPlay = Path.Combine(_root, "oxide", "data", "PTTracker", "playtime_data.json");
            string harmonyOld = Path.Combine(_root, "HarmonyData", "PTTracker", "playtime_data.json");
            if (File.Exists(oldPlay) || File.Exists(harmonyOld))
            {
                RestoreOldData(File.Exists(oldPlay) ? Path.Combine(_root, "oxide", "data", "PTTracker") : Path.Combine(_root, "HarmonyData", "PTTracker"));
                return;
            }

            storedData = new StoredData();
            SaveData();
        }

        private void RestoreOldData(string dir)
        {
            Debug.Log("[PlaytimeTracker] Migrating data from PTTracker format...");
            storedData = new StoredData();
            try
            {
                var playPath = Path.Combine(dir, "playtime_data.json");
                var referPath = Path.Combine(dir, "referral_data.json");
                var permPath = Path.Combine(dir, "permission_data.json");

                PlayData playData = File.Exists(playPath)
                    ? JsonConvert.DeserializeObject<PlayData>(File.ReadAllText(playPath)) ?? new PlayData()
                    : new PlayData();
                RefData referData = File.Exists(referPath)
                    ? JsonConvert.DeserializeObject<RefData>(File.ReadAllText(referPath)) ?? new RefData()
                    : new RefData();
                PermData permData = File.Exists(permPath)
                    ? JsonConvert.DeserializeObject<PermData>(File.ReadAllText(permPath)) ?? new PermData()
                    : new PermData();

                foreach (var kvp in playData.timeData)
                {
                    var p = BasePlayer.FindAwakeOrSleeping(kvp.Key);
                    storedData.InsertData(kvp.Key, p?.displayName ?? "Unnamed", kvp.Value.playTime, kvp.Value.afkTime, kvp.Value.lastReward, kvp.Value.referrals);
                }
                if (referData.referrals != null)
                {
                    foreach (string str in referData.referrals)
                        storedData.InsertReferral(str);
                }
                if (permData.permissions != null && permData.permissions.Count > 0)
                {
                    foreach (var kvp in permData.permissions)
                        Configuration.Reward.CustomMultipliers[kvp.Key] = kvp.Value;
                    RegisterPermissions();
                    SaveConfig();
                }
                SaveData();
                Debug.Log("[PlaytimeTracker] Data migration completed successfully!");
            }
            catch (Exception ex)
            {
                Debug.LogError("[PlaytimeTracker] PTTracker migration failed: " + ex);
                storedData ??= new StoredData();
            }
        }

        internal class PlayerTracker
        {
            private readonly PlaytimeTrackerMod _plugin;
            private BasePlayer _user;
            private Vector3 _lastPosition;
            private const float POSITION_CHECK_INTERVAL = 30f;
            private const float AFK_DISTANCE_THRESHOLD = 0.1f;

            public PlayerTracker(PlaytimeTrackerMod plugin, BasePlayer user)
            {
                _plugin = plugin;
                OnUserConnected(user);
            }

            public void OnUserConnected(BasePlayer user)
            {
                _user = user;
                _lastPosition = user != null ? user.transform.position : Vector3.zero;
            }

            public void OnUserDisconnected() => _user = null;

            public void TickPosition(float interval)
            {
                if (_user == null || !_user.IsConnected)
                {
                    OnUserDisconnected();
                    return;
                }
                var currentPosition = _user.transform.position;
                var distance = Vector3.Distance(_lastPosition, currentPosition);
                var userData = _plugin.storedData.GetOrCreateUserData(_user.UserIDString);
                userData.lastSeen = CurrentTime;
                if (_plugin.Configuration.General.TrackAFK)
                {
                    if (distance < AFK_DISTANCE_THRESHOLD) userData.afkTime += interval;
                    else userData.playtime += interval;
                }
                else userData.playtime += interval;
                _lastPosition = currentPosition;
            }

            public void ProcessReward()
            {
                if (_user == null || !_user.IsConnected || !_plugin.Configuration.Reward.Playtime.Enabled)
                    return;
                var userData = _plugin.storedData.GetOrCreateUserData(_user.UserIDString);
                double rewardMultiplier = (userData.playtime - userData.lastRewardTime) / _plugin.Configuration.Reward.Playtime.Interval;
                if (rewardMultiplier >= 1f)
                {
                    _plugin.TimeReward?.Invoke(_user.UserIDString, _plugin.Configuration.Reward.Playtime.Reward * rewardMultiplier);
                    userData.lastRewardTime = userData.playtime;
                }
            }
        }

        public class ConfigData
        {
            [JsonProperty("General Options")]
            public GeneralOptions General { get; set; }

            [JsonProperty("Reward Options")]
            public RewardOptions Reward { get; set; }

            [JsonProperty("Version")]
            public VersionInfo Version { get; set; }

            public class GeneralOptions
            {
                [JsonProperty("Data save interval (seconds)")]
                public int SaveInterval { get; set; }
                [JsonProperty("Track player AFK time")]
                public bool TrackAFK { get; set; }
                [JsonProperty("Number of entries to display in the top playtime list")]
                public int TopCount { get; set; }
            }

            public class RewardOptions
            {
                [JsonProperty("Reward plugin (ServerRewards, Economics)")]
                public string Plugin { get; set; }
                [JsonProperty("Playtime rewards")]
                public PlaytimeRewards Playtime { get; set; }
                [JsonProperty("Referral rewards")]
                public ReferralRewards Referral { get; set; }
                [JsonProperty("Custom reward multipliers (permission / multiplier)")]
                public Hash<string, float> CustomMultipliers { get; set; }

                public class PlaytimeRewards
                {
                    [JsonProperty("Issue rewards for playtime")]
                    public bool Enabled { get; set; }
                    [JsonProperty("Reward interval (seconds)")]
                    public int Interval { get; set; }
                    [JsonProperty("Reward amount")]
                    public int Reward { get; set; }
                }

                public class ReferralRewards
                {
                    [JsonProperty("Issue rewards for player referrals")]
                    public bool Enabled { get; set; }
                    [JsonProperty("Referrer reward amount")]
                    public int InviteReward { get; set; }
                    [JsonProperty("Referee reward amount")]
                    public int JoinReward { get; set; }
                }
            }

            public class VersionInfo
            {
                public int Major { get; set; }
                public int Minor { get; set; }
                public int Patch { get; set; }
            }

            public static ConfigData Default() => new ConfigData
            {
                General = new GeneralOptions { SaveInterval = 900, TrackAFK = true, TopCount = 10 },
                Reward = new RewardOptions
                {
                    Plugin = "Economics",
                    Playtime = new RewardOptions.PlaytimeRewards { Enabled = true, Interval = 3600, Reward = 5 },
                    Referral = new RewardOptions.ReferralRewards { Enabled = true, InviteReward = 5, JoinReward = 3 },
                    CustomMultipliers = new Hash<string, float>
                    {
                        ["playtimetracker.examplevip1"] = 1.5f,
                        ["playtimetracker.examplevip2"] = 2.0f
                    }
                },
                Version = new VersionInfo { Major = VersionMajor, Minor = VersionMinor, Patch = VersionPatch }
            };
        }

        public class StoredData
        {
            [JsonProperty]
            internal Dictionary<string, UserData> _userData = new Dictionary<string, UserData>();
            [JsonProperty]
            internal HashSet<string> _referredUsers = new HashSet<string>();

            public bool HasBeenReferred(string id) => _referredUsers.Contains(id);

            public void ReferPlayer(string referrer, string referree)
            {
                _referredUsers.Add(referree);
                GetOrCreateUserData(referrer).referrals += 1;
                PlaytimeTrackerMod.Instance?.ReferralReward?.Invoke(referrer, referree);
            }

            public void OnUserConnected(BasePlayer user)
            {
                var d = GetOrCreateUserData(user.UserIDString);
                d.displayName = user.displayName;
                d.lastSeen = CurrentTime;
            }

            public void OnUserDisconnected(BasePlayer user)
            {
                if (_userData.TryGetValue(user.UserIDString, out var d))
                    d.lastSeen = CurrentTime;
            }

            public UserData GetOrCreateUserData(string id)
            {
                if (!_userData.TryGetValue(id, out var userData))
                    userData = _userData[id] = new UserData();
                return userData;
            }

            public double GetPlayTimeForPlayer(string id) => _userData.TryGetValue(id, out var d) ? d.playtime : 0;
            public double GetAFKTimeForPlayer(string id) => _userData.TryGetValue(id, out var d) ? d.afkTime : 0;
            public int GetReferralsForPlayer(string id) => _userData.TryGetValue(id, out var d) ? d.referrals : 0;

            public void GetTopPlayTime(List<UserData> list)
            {
                list.Clear();
                list.AddRange(_userData.Values);
                list.Sort((a, b) => b.playtime.CompareTo(a.playtime));
            }

            public Dictionary<string, UserData> GetTopPlayers(int count)
            {
                return _userData
                    .OrderByDescending(x => x.Value.playtime)
                    .Take(count)
                    .ToDictionary(x => x.Key, x => x.Value);
            }

            internal void InsertData(string id, string displayName, double playTime, double afkTime, double lastReward, int referrals)
            {
                var userData = GetOrCreateUserData(id);
                userData.displayName = displayName;
                userData.playtime = playTime;
                userData.afkTime = afkTime;
                userData.lastRewardTime = playTime;
                userData.referrals = referrals;
            }

            internal void InsertReferral(string id) => _referredUsers.Add(id);

            public class UserData
            {
                public double playtime;
                public double afkTime;
                public double lastRewardTime;
                public int referrals;
                public string displayName;
                public double lastSeen;
            }
        }

        private class PlayData
        {
            public Dictionary<string, TimeInfo> timeData = new Dictionary<string, TimeInfo>();
            public class TimeInfo
            {
                public double playTime;
                public double afkTime;
                public double lastReward;
                public int referrals;
            }
        }
        private class PermData { public Dictionary<string, float> permissions = new Dictionary<string, float>(); }
        private class RefData { public List<string> referrals = new List<string>(); }

        private static readonly Dictionary<string, string> DefaultMessages = new Dictionary<string, string>
        {
            ["Playtime.Both"] = "[#45b6fe]Playtime[/#] : [#ffd479]{0}[/#]\n[#45b6fe]AFK Time[/#] : [#ffd479]{1}[/#]",
            ["Playtime.Single"] = "[#45b6fe]Playtime[/#] : [#ffd479]{0}[/#]",
            ["Playtime.Help"] = "You can see the top scoring playtimes by typing [#a1ff46]/playtime top[/#]\nAdmins can check when a player was last seen with [#a1ff46]/playtime lastseen <playername>[/#]",
            ["Top.Title"] = "[#45b6fe]Top Playtimes:[/#]",
            ["Top.Format"] = "\n[#a1ff46]{0}[/#] - [#ffd479]{1}[/#]",
            ["Referral.Disabled"] = "The referral system is disabled",
            ["Referral.Help"] = "[#ffd479]/refer <name or ID>[/#] - Add a referral for the specified player",
            ["Referral.Submitted"] = "You have already submitted your referral",
            ["Referral.Self"] = "You can not refer yourself",
            ["Referral.Accepted"] = "Your referral has been accepted",
            ["Referral.Acknowledged"] = "[#a1ff46]{0}[/#] has acknowledged a referral from you",
            ["Reward.Given.ServerRewards"] = "You have received [#a1ff46]{0} RP[/#] for playing on our server!",
            ["Reward.Given.Economics"] = "You have received [#a1ff46]{0}[/#] coins for playing on our server!",
            ["Error.NoPlaytimeStored"] = "No playtime has been stored for you yet",
            ["Error.NoPlayerFound"] = "No player found with the name [#a1ff46]{0}[/#]",
            ["Error.NoTimeStored"] = "No time stored for the specified player",
            ["Error.InvalidSyntax"] = "Invalid syntax",
        };
    }

    internal sealed class PlaytimeTrackerRunner : MonoBehaviour
    {
        private PlaytimeTrackerMod _mod;
        private float _posAccum;
        private float _saveAccum;
        private float _rewardAccum;
        private bool _started;

        public void Begin(PlaytimeTrackerMod mod)
        {
            _mod = mod;
            if (!_started)
            {
                _started = true;
                StartCoroutine(WaitForServer());
            }
        }

        private IEnumerator WaitForServer()
        {
            while (ServerMgr.Instance == null)
                yield return null;
            yield return new WaitForSeconds(1f);
            _mod?.OnServerInitialized();
        }

        private void Update()
        {
            if (_mod?.Configuration == null) return;
            float dt = Time.deltaTime;
            _posAccum += dt;
            _saveAccum += dt;
            _rewardAccum += dt;

            if (_posAccum >= 30f)
            {
                float interval = _posAccum;
                _posAccum = 0f;
                try { _mod.TickAllPositions(interval); }
                catch { }
            }

            int saveInterval = Math.Max(30, _mod.Configuration.General.SaveInterval);
            if (_saveAccum >= saveInterval)
            {
                _saveAccum = 0f;
                _mod.SaveData();
            }

            if (_mod.Configuration.Reward.Playtime.Enabled)
            {
                int rewardInterval = Math.Max(10, _mod.Configuration.Reward.Playtime.Interval);
                if (_rewardAccum >= rewardInterval)
                {
                    _rewardAccum = 0f;
                    _mod.ProcessRewards();
                }
            }
        }
    }
}
