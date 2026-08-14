using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rust;
using UnityEngine;

namespace BGrade
{
    public class BGradePlugin
    {
        public static BGradePlugin Instance { get; private set; }

        private readonly string _configPath;
        private readonly string _langPath;
        private readonly Dictionary<string, string> _lang = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _registeredPermissions = new List<string>();
        private readonly Dictionary<Vector3, int> _lastAttacked = new Dictionary<Vector3, int>();

        public bool AllowTimer;
        public int MaxTimer;
        public int DefaultTimer;
        public bool CheckLastAttack;
        public int UpgradeCooldown;
        public List<string> ChatCommands;
        public List<string> ConsoleCommands;
        public bool RefundOnBlock;
        public bool DestroyOnDisconnect;

        public BGradePlugin(string serverRoot)
        {
            Instance = this;
            _configPath = Path.Combine(serverRoot, "HarmonyConfig", "BGrade.json");
            _langPath = Path.Combine(serverRoot, "HarmonyLanguage", "BGrade.json");
        }

        public void Load()
        {
            LoadDefaultMessages();
            LoadLangFile();
            LoadConfig();
        }

        public void Unload()
        {
            DestroyAllPlayers();
            BGradePlayer.Players.Clear();
            Instance = null;
        }

        public void RegisterPermissions()
        {
            _registeredPermissions.Clear();
            for (int i = 1; i < 5; i++)
                RegisterPermission("bgrade." + i);
            RegisterPermission("bgrade.nores");
            RegisterPermission("bgrade.all");
        }

        private void RegisterPermission(string permissionName)
        {
            if (!_registeredPermissions.Contains(permissionName))
                _registeredPermissions.Add(permissionName);
            PermissionsBridge.RegisterPermission(permissionName);
        }

        public void OnServerSave()
        {
            if (!CheckLastAttack) return;
            CheckLastAttacked();
        }

        public void OnEntityBuilt(Planner plan, GameObject gameObject)
        {
            var player = plan?.GetOwnerPlayer();
            if (player == null) return;
            if (plan.isTypeDeployable) return;

            var buildingBlock = gameObject.GetComponent<BuildingBlock>();
            if (buildingBlock == null) return;
            if (!player.CanBuild()) return;
            if (!HasAnyPermission(player)) return;

            BGradePlayer bgradePlayer;
            if (!BGradePlayer.Players.TryGetValue(player, out bgradePlayer))
                return;

            int playerGrade = bgradePlayer.GetGrade();
            if (playerGrade == 0) return;
            if (!HasPluginPerm(player, "all") && !HasPluginPerm(player, playerGrade.ToString()))
                return;

            if (playerGrade < (int)buildingBlock.grade || buildingBlock.blockDefinition == null
                || buildingBlock.blockDefinition.grades == null
                || playerGrade >= buildingBlock.blockDefinition.grades.Length
                || buildingBlock.blockDefinition.grades[playerGrade] == null)
                return;

            if (CheckLastAttack && WasAttackedRecently(buildingBlock.transform.position))
                return;

            if (!HasPluginPerm(player, "nores"))
            {
                Dictionary<int, int> itemsToTake;
                string resourceResponse = TakeResources(player, playerGrade, buildingBlock, out itemsToTake);
                if (!string.IsNullOrEmpty(resourceResponse))
                {
                    player.ChatMessage(resourceResponse);
                    return;
                }

                foreach (var itemToTake in itemsToTake)
                    TakeItem(player, itemToTake.Key, itemToTake.Value);
            }

            if (AllowTimer)
                bgradePlayer.UpdateTime();

            buildingBlock.SetGrade((BuildingGrade.Enum)playerGrade);
            buildingBlock.SetHealthToMax();
            buildingBlock.StartBeingRotatable();
            buildingBlock.SendNetworkUpdate();
            buildingBlock.UpdateSkin();
            buildingBlock.ResetUpkeepTime();
            buildingBlock.GetBuilding()?.Dirty();
        }

        public object OnPayForPlacement(BasePlayer player, Planner planner, Construction component)
        {
            if (planner.isTypeDeployable) return null;
            if (!BGradePlayer.Players.ContainsKey(player)) return null;
            if (!HasPluginPerm(player, "nores")) return null;
            var bgradePlayer = BGradePlayer.Players[player];
            if (bgradePlayer.GetGrade() == 0) return null;
            return false;
        }

        public void OnEntityDeath(BuildingBlock buildingBlock, HitInfo info)
        {
            if (!CheckLastAttack) return;
            var attacker = info?.InitiatorPlayer;
            if (attacker == null) return;
            if (info.damageTypes != null && info.damageTypes.GetMajorityDamageType() == DamageType.Explosion)
                _lastAttacked[buildingBlock.transform.position] = UnixNow() + UpgradeCooldown;
        }

        public void OnPlayerDisconnected(BasePlayer player)
        {
            if (!DestroyOnDisconnect) return;
            BGradePlayer bgradePlayer;
            if (!BGradePlayer.Players.TryGetValue(player, out bgradePlayer))
                return;
            bgradePlayer.Destroy();
        }

        public void BGradeCommand(BasePlayer player, string command, string[] args)
        {
            if (!HasAnyPermission(player))
            {
                player.ChatMessage(Lang("Permission", player.UserIDString));
                return;
            }

            if (args == null || args.Length == 0)
            {
                player.ChatMessage(Lang("Error.InvalidArgs", player.UserIDString, command));
                return;
            }

            var chatMsgs = new List<string>();

            switch (args[0].ToLowerInvariant())
            {
                case "0":
                    {
                        player.ChatMessage(Lang("Notice.Disabled", player.UserIDString));
                        BGradePlayer bgradePlayer;
                        if (BGradePlayer.Players.TryGetValue(player, out bgradePlayer))
                        {
                            bgradePlayer.DestroyTimer();
                            bgradePlayer.SetGrade(0);
                        }
                        return;
                    }
                case "1":
                case "2":
                case "3":
                case "4":
                    {
                        if (!HasPluginPerm(player, "all") && !HasPluginPerm(player, args[0]))
                        {
                            player.ChatMessage(Lang("Permission", player.UserIDString));
                            return;
                        }

                        int grade = Convert.ToInt32(args[0]);
                        BGradePlayer bgradePlayer;
                        if (!BGradePlayer.Players.TryGetValue(player, out bgradePlayer))
                            bgradePlayer = player.gameObject.AddComponent<BGradePlayer>();

                        bgradePlayer.SetGrade(grade);
                        int time = bgradePlayer.GetTime();
                        chatMsgs.Add(Lang("Notice.SetGrade", player.UserIDString, grade));
                        if (AllowTimer && time > 0)
                            chatMsgs.Add(Lang("Notice.Time", player.UserIDString, time));
                        player.ChatMessage(string.Join("\n", chatMsgs.ToArray()));
                        return;
                    }
                case "t":
                    {
                        if (!AllowTimer) return;
                        if (args.Length == 1)
                        {
                            player.ChatMessage(Lang("Error.InvalidArgs", player.UserIDString, command));
                            return;
                        }

                        int time;
                        if (!int.TryParse(args[1], out time) || time <= 0)
                        {
                            player.ChatMessage(Lang("Error.InvalidTime", player.UserIDString, args[1]));
                            return;
                        }

                        if (time > MaxTimer)
                        {
                            player.ChatMessage(Lang("Error.TimerTooLong", player.UserIDString, MaxTimer));
                            return;
                        }

                        BGradePlayer bgradePlayer;
                        if (!BGradePlayer.Players.TryGetValue(player, out bgradePlayer))
                            bgradePlayer = player.gameObject.AddComponent<BGradePlayer>();

                        player.ChatMessage(Lang("Notice.SetTime", player.UserIDString, time));
                        bgradePlayer.SetTime(time);
                        return;
                    }
                case "help":
                    {
                        chatMsgs.Add(Lang("Command.Help", player.UserIDString));
                        if (AllowTimer)
                        {
                            chatMsgs.Add(Lang("Command.Help.T", player.UserIDString, command));
                            chatMsgs.Add(Lang("Command.Help.0", player.UserIDString, command));
                        }

                        for (int i = 1; i < 5; i++)
                        {
                            if (HasPluginPerm(player, i.ToString()) || HasPluginPerm(player, "all"))
                                chatMsgs.Add(Lang("Command.Help." + i, player.UserIDString, command));
                        }

                        if (chatMsgs.Count <= 3 && !HasPluginPerm(player, "all"))
                        {
                            player.ChatMessage(Lang("Permission", player.UserIDString));
                            return;
                        }

                        BGradePlayer bgradePlayer;
                        if (BGradePlayer.Players.TryGetValue(player, out bgradePlayer))
                        {
                            chatMsgs.Add(Lang("Command.Settings", player.UserIDString));
                            if (AllowTimer)
                                chatMsgs.Add(Lang("Command.Settings.Timer", player.UserIDString, bgradePlayer.GetTime(false)));
                            int fetchedGrade = bgradePlayer.GetGrade();
                            chatMsgs.Add(Lang("Command.Settings.Grade", player.UserIDString,
                                fetchedGrade == 0 ? Lang("Words.Disabled", player.UserIDString) : fetchedGrade.ToString()));
                        }

                        player.ChatMessage(string.Join("\n", chatMsgs.ToArray()));
                        return;
                    }
                default:
                    player.ChatMessage(Lang("Error.InvalidArgs", player.UserIDString, command));
                    return;
            }
        }

        public void BGradeUpCommand(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null) return;
            if (!HasAnyPermission(player))
            {
                player.ChatMessage(Lang("Permission", player.UserIDString));
                return;
            }

            BGradePlayer bgradePlayer;
            if (!BGradePlayer.Players.TryGetValue(player, out bgradePlayer))
                bgradePlayer = player.gameObject.AddComponent<BGradePlayer>();

            int grade = bgradePlayer.GetGrade() + 1;
            int count = 0;

            if (!HasPluginPerm(player, "all"))
            {
                while (!HasPluginPerm(player, grade.ToString()))
                {
                    grade++;
                    if (grade > 4)
                        grade = 1;
                    count++;
                    if (count > bgradePlayer.GetGrade() + 4)
                    {
                        player.ChatMessage(Lang("Permission", player.UserIDString));
                        return;
                    }
                }
            }
            else if (grade > 4) grade = 1;

            var chatMsgs = new List<string>();
            bgradePlayer.SetGrade(grade);
            int time = bgradePlayer.GetTime();
            chatMsgs.Add(Lang("Notice.SetGrade", player.UserIDString, grade));
            if (AllowTimer && time > 0)
                chatMsgs.Add(Lang("Notice.Time", player.UserIDString, time));
            player.ChatMessage(string.Join("\n", chatMsgs.ToArray()));
        }

        private string TakeResources(BasePlayer player, int playerGrade, BuildingBlock buildingBlock, out Dictionary<int, int> items)
        {
            var itemsToTake = new Dictionary<int, int>();
            List<ItemAmount> costToBuild = null;
            var grades = buildingBlock.blockDefinition.grades;
            if (grades != null)
            {
                for (int i = 0; i < grades.Length; i++)
                {
                    var grade = grades[i];
                    if (grade == null || grade.gradeBase == null) continue;
                    if (grade.gradeBase.type == (BuildingGrade.Enum)playerGrade)
                    {
                        costToBuild = grade.CostToBuild();
                        break;
                    }
                }
            }

            if (costToBuild == null)
            {
                Debug.LogError($"[BGrade] COULDN'T FIND COST TO BUILD WITH GRADE: {playerGrade} FOR {buildingBlock.PrefabName}");
                items = itemsToTake;
                return Lang("Error.Resources", player.UserIDString);
            }

            for (int i = 0; i < costToBuild.Count; i++)
            {
                var itemAmount = costToBuild[i];
                if (!itemsToTake.ContainsKey(itemAmount.itemid))
                    itemsToTake.Add(itemAmount.itemid, 0);
                itemsToTake[itemAmount.itemid] += (int)itemAmount.amount;
            }

            bool canAfford = true;
            foreach (var itemToTake in itemsToTake)
            {
                if (!HasItemAmount(player, itemToTake.Key, itemToTake.Value))
                    canAfford = false;
            }

            items = itemsToTake;
            return canAfford ? null : Lang("Error.Resources", player.UserIDString);
        }

        private void CheckLastAttacked()
        {
            var toRemove = new List<Vector3>();
            foreach (var lastAttackEntry in _lastAttacked)
            {
                if (!WasAttackedRecently(lastAttackEntry.Key))
                    toRemove.Add(lastAttackEntry.Key);
            }
            for (int i = 0; i < toRemove.Count; i++)
                _lastAttacked.Remove(toRemove[i]);
        }

        private bool WasAttackedRecently(Vector3 position)
        {
            int time;
            if (!_lastAttacked.TryGetValue(position, out time))
                return false;
            if (time < UnixNow())
                return true;
            return false;
        }

        private static int UnixNow() => (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private void DestroyAllPlayers()
        {
            var toDestroy = new List<BGradePlayer>();
            foreach (var kvp in BGradePlayer.Players)
            {
                if (kvp.Value != null)
                    toDestroy.Add(kvp.Value);
            }
            for (int i = 0; i < toDestroy.Count; i++)
            {
                if (toDestroy[i] != null)
                    UnityEngine.Object.Destroy(toDestroy[i]);
            }
        }

        private bool HasAnyPermission(BasePlayer player)
        {
            for (int i = 0; i < _registeredPermissions.Count; i++)
            {
                if (PermissionsBridge.UserHasPermission(player.UserIDString, _registeredPermissions[i]))
                    return true;
            }
            return false;
        }

        private static bool HasPluginPerm(BasePlayer player, string perm) =>
            PermissionsBridge.UserHasPermission(player.UserIDString, "bgrade." + perm);

        private static bool HasItemAmount(BasePlayer player, int itemId, int itemAmount)
        {
            int count = 0;
            var all = new List<Item>();
            player.inventory.GetAllItems(all);
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].info.itemid == itemId)
                    count += all[i].amount;
            }
            return count >= itemAmount;
        }

        private static void TakeItem(BasePlayer player, int itemId, int itemAmount)
        {
            if (player.inventory.Take(null, itemId, itemAmount) > 0)
                player.SendConsoleCommand("note.inv", itemId, itemAmount * -1);
        }

        internal string Lang(string key, string id = null, params object[] args)
        {
            string msg;
            if (!_lang.TryGetValue(key, out msg) || msg == null)
                msg = key;
            if (args == null || args.Length == 0) return msg;
            try { return string.Format(msg, args); }
            catch { return msg; }
        }

        private void LoadDefaultMessages()
        {
            void Add(string k, string v) { if (!_lang.ContainsKey(k)) _lang[k] = v; }
            Add("Permission", "You don't have permission to use that command");
            Add("Error.InvalidArgs", "Invalid arguments, please use /{0} help");
            Add("Error.Resources", "You don't have enough resources to upgrade.");
            Add("Error.InvalidTime", "Please enter a valid time. '<color=orange>{0}</color>' is not recognised as a number.");
            Add("Error.TimerTooLong", "Please enter a time that is below the value of <color=orange>{0}</color>.");
            Add("Notice.SetGrade", "Automatic upgrading is now set to grade <color=orange>{0}</color>.");
            Add("Notice.SetTime", "The disable timer is now set to <color=orange>{0}</color>.");
            Add("Notice.Disabled", "Automatic upgrading is now disabled.");
            Add("Notice.Disabled.Auto", "Automatic upgrading has been automatically disabled.");
            Add("Notice.Time", "It'll automatically disable in <color=orange>{0}</color> seconds.");
            Add("Command.Help", "<color=orange><size=16>BGrade Command Usages</size></color>");
            Add("Command.Help.0", "/{0} 0 - Disables BGrade");
            Add("Command.Help.1", "/{0} 1 - Upgrades to Wood upon placement");
            Add("Command.Help.2", "/{0} 2 - Upgrades to Stone upon placement");
            Add("Command.Help.3", "/{0} 3 - Upgrades to Metal upon placement");
            Add("Command.Help.4", "/{0} 4 - Upgrades to Armoured upon placement");
            Add("Command.Help.T", "/{0} t <seconds> - Time until BGrade is disabled");
            Add("Command.Settings", "<color=orange><size=16>Your current settings</size></color>");
            Add("Command.Settings.Timer", "Timer: <color=orange>{0}</color> seconds");
            Add("Command.Settings.Grade", "Grade: <color=orange>{0}</color>");
            Add("Words.Disabled", "disabled");
        }

        private void LoadLangFile()
        {
            if (!File.Exists(_langPath)) return;
            try
            {
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(_langPath));
                if (loaded == null) return;
                foreach (var kv in loaded)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                        _lang[kv.Key] = kv.Value;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BGrade] Lang file load failed: " + ex.Message);
            }
        }

        private void LoadConfig()
        {
            JObject obj = null;
            if (File.Exists(_configPath))
            {
                try { obj = JObject.Parse(File.ReadAllText(_configPath)); }
                catch (Exception ex) { Debug.LogWarning("[BGrade] Config parse failed: " + ex.Message); }
            }
            if (obj == null) obj = new JObject();

            AllowTimer = Get(obj, true, "Timer Settings", "Enabled");
            DefaultTimer = Get(obj, 30, "Timer Settings", "Default Timer");
            MaxTimer = Get(obj, 180, "Timer Settings", "Max Timer");
            ChatCommands = GetList(obj, new List<string> { "bgrade", "grade" }, "Command Settings", "Chat Commands");
            ConsoleCommands = GetList(obj, new List<string> { "bgrade.up" }, "Command Settings", "Console Commands");
            CheckLastAttack = Get(obj, true, "Building Attack Settings", "Enabled");
            UpgradeCooldown = Get(obj, 30, "Building Attack Settings", "Cooldown Time");
            RefundOnBlock = Get(obj, true, "Refund Settings", "Refund on Block");
            DestroyOnDisconnect = Get(obj, false, "Destroy Data on Player Disconnect (for high pop servers)");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
                File.WriteAllText(_configPath, obj.ToString(Formatting.Indented));
            }
            catch (Exception ex) { Debug.LogWarning("[BGrade] Config save failed: " + ex.Message); }
        }

        private static T Get<T>(JObject obj, T defaultVal, params string[] path)
        {
            JToken cur = obj;
            for (int i = 0; i < path.Length; i++)
            {
                if (cur is JObject jo && jo.TryGetValue(path[i], out var next))
                    cur = next;
                else
                {
                    SetPath(obj, defaultVal, path);
                    return defaultVal;
                }
            }
            try { return cur.ToObject<T>(); }
            catch { return defaultVal; }
        }

        private static List<string> GetList(JObject obj, List<string> defaultVal, params string[] path)
        {
            var list = Get(obj, defaultVal, path);
            return list ?? defaultVal;
        }

        private static void SetPath(JObject obj, object value, string[] path)
        {
            JObject cur = obj;
            for (int i = 0; i < path.Length - 1; i++)
            {
                if (!(cur[path[i]] is JObject next))
                {
                    next = new JObject();
                    cur[path[i]] = next;
                }
                cur = next;
            }
            cur[path[path.Length - 1]] = value == null ? JValue.CreateNull() : JToken.FromObject(value);
        }
    }

    public class BGradePlayer : FacepunchBehaviour
    {
        public static readonly Dictionary<BasePlayer, BGradePlayer> Players = new Dictionary<BasePlayer, BGradePlayer>();

        private BasePlayer _player;
        private bool _timerPending;
        private int _grade;
        private int _time;

        public void Awake()
        {
            var attachedPlayer = GetComponent<BasePlayer>();
            if (attachedPlayer == null || !attachedPlayer.IsConnected)
                return;
            _player = attachedPlayer;
            Players[_player] = this;
            _time = GetTime(false);
        }

        public int GetTime(bool updateTime = true)
        {
            var inst = BGradePlugin.Instance;
            if (inst == null || !inst.AllowTimer)
                return 0;
            if (updateTime)
                UpdateTime();
            return _time != 0 ? _time : inst.DefaultTimer;
        }

        public void UpdateTime()
        {
            if (_time <= 0) return;
            DestroyTimer();
            _timerPending = true;
            Invoke(OnTimerElapsed, _time);
        }

        private void OnTimerElapsed()
        {
            _grade = 0;
            DestroyTimer();
            if (_player != null && _player.IsConnected)
                _player.ChatMessage(BGradePlugin.Instance?.Lang("Notice.Disabled.Auto", _player.UserIDString) ?? "Automatic upgrading has been automatically disabled.");
        }

        public int GetGrade() => _grade;

        public void SetGrade(int newGrade) => _grade = newGrade;
        public void SetTime(int newTime) => _time = newTime;

        public void DestroyTimer()
        {
            if (_timerPending)
            {
                CancelInvoke(OnTimerElapsed);
                _timerPending = false;
            }
        }

        public void Destroy() => Destroy(this);

        public void OnDestroy()
        {
            if (_player != null && Players.ContainsKey(_player))
                Players.Remove(_player);
        }
    }
}
