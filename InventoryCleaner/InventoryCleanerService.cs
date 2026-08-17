using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace InventoryCleaner
{
    /// <summary>
    /// Inventory Cleaner 2.1.2 logic (Oxide port). Config / lang / clear / chat helpers.
    /// </summary>
    public sealed class InventoryCleanerService
    {
        public const string Author = "Joao Pster";
        public const string VersionString = "2.1.2";

        public static class Perms
        {
            public const string Clear = "inventorycleaner.allowed";
            public const string ClearOthers = "inventorycleaner.cleaneveryone";
            public const string ClearOnDeath = "inventorycleaner.cleanondeath";
            public const string ClearOnLogout = "inventorycleaner.cleanonexit";
        }

        private static readonly string[] AllPermissions =
        {
            Perms.Clear,
            Perms.ClearOthers,
            Perms.ClearOnDeath,
            Perms.ClearOnLogout
        };

        /// <summary>Command perms only. Wipe-on-death / wipe-on-logout are opt-in flags, not admin tools.</summary>
        private static readonly string[] AdminCommandPermissions =
        {
            Perms.Clear,
            Perms.ClearOthers
        };

        private static readonly string[] OptInWipePermissions =
        {
            Perms.ClearOnDeath,
            Perms.ClearOnLogout
        };

        private static readonly string[] ChatCommandNames =
        {
            "clearinv", "cleaninv", "clear.inv", "clean.inv",
            "inv.clear", "invclear", "inv.clean", "invclean"
        };

        private readonly string _serverRoot;
        private readonly string _configPath;
        private readonly LangStore _lang = new LangStore();
        private Configuration _config;

        public Configuration Config => _config;

        public InventoryCleanerService(string serverRoot)
        {
            _serverRoot = serverRoot;
            _configPath = Path.Combine(serverRoot, "HarmonyConfig", "InventoryCleaner.json");
        }

        public IEnumerable<string> GetChatCommandNames() => ChatCommandNames;

        #region Config

        public class Configuration
        {
            [JsonProperty(PropertyName = "[Message Image]")]
            public ulong MessageImage { get; set; }

            [JsonProperty(PropertyName = "[Message Prefix]")]
            public string MessagePrefix { get; set; } = "[Clear Inventory]";
        }

        public void LoadConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    _config = JsonConvert.DeserializeObject<Configuration>(json);
                }

                if (_config == null)
                {
                    Debug.LogWarning("[InventoryCleaner] Creating new configuration file.");
                    _config = new Configuration();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[InventoryCleaner] FAIL: load config — using defaults. " + ex.Message);
                _config = new Configuration();
            }

            SaveConfig();
        }

        public void SaveConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(_config ?? new Configuration(), Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[InventoryCleaner] FAIL: save config: " + ex.Message);
            }
        }

        #endregion

        #region Lang

        public void LoadDefaultMessages()
        {
            _lang.RegisterMessages(CreateDefaultMessages(), "en");
            _lang.LoadHarmonyLanguageOverrides(_serverRoot, "InventoryCleaner");
        }

        private static Dictionary<string, string> CreateDefaultMessages() => new Dictionary<string, string>
        {
            [MessageKey.NoPermission] = "You don't have the permission <color=#FF0000>{0}</color> to do that!",
            [MessageKey.NotFound] = "Command <color=red>{0}</color> not found!",
            [MessageKey.OptNotFound] = "Option <color=red>/clearinv {0}</color> not found!",
            [MessageKey.CorrectUse] = "The correct use is: <color=green>/clearinv [command]</color>",
            [MessageKey.BeltCleaned] = "{0}, your belt has just been cleaned!",
            [MessageKey.EveryBeltCleaned] = "The Belt of all players logged into the server has just been removed!",
            [MessageKey.InvCleaned] = "{0}, your inventory has just been cleaned!",
            [MessageKey.EveryInvCleaned] = "The Inventory of all players logged into the server has just been removed!",
            [MessageKey.WearCleaned] = "{0}, your clothing slots has just been cleaned!",
            [MessageKey.EveryWearCleaned] = "The Clothing Slots of all players logged into the server has just been removed!",
            [MessageKey.AllCleaned] = "{0}, everything you have has just been cleaned!",
            [MessageKey.EveryAllCleaned] = "All Items of all players logged into the server has just been removed!",
            [MessageKey.OnDeath] = "{0}, you died and everything you had was deleted before your death!",
            [MessageKey.Header] = "<size=16><color=green>Clear Inventory by {0}</color></size> v{1} \n",
            [MessageKey.Gone] = "<color=#ff0000>Warning:</color> Once items removed they are GONE ! \n\n",
            [MessageKey.Opts] = "Hi, the base commands is <color=green>/clearinv [opts]</color>, see the opts:\n\n",
            [MessageKey.Perms] = "Hi <color=green>{0}</color>, this is your permissions: \n",
            [MessageKey.OptAll] = "<color=yellow>main</color>: remove all your items \n",
            [MessageKey.OptInv] = "<color=yellow>inv</color>: remove all items from your inventory \n",
            [MessageKey.OptBelt] = "<color=yellow>belt</color>: remove all items from your belt \n",
            [MessageKey.OptWear] = "<color=yellow>wear</color>: remove all items from your clothing slots \n\n",
            [MessageKey.OptEvery] = "And, if you have permission, you can do <color=red>/clearinv [opts] everyone</color> to remove the items from everyone who is logged on to the server!",
            [MessageKey.PermUse] = "<color=yellow>Use Clear:</color> {0} \n",
            [MessageKey.PermEvery] = "<color=yellow>Clear Everyone:</color> {0} \n",
            [MessageKey.PermDeath] = "<color=yellow>Clear on Death:</color> {0} \n",
            [MessageKey.PermLogout] = "<color=yellow>Clear on logout:</color> {0} \n\n",
            [MessageKey.InvComands] = "Use <color=green>/clearinv cmds</color> to see the comands.",
        };

        private static class MessageKey
        {
            public const string NoPermission = "[No Permission]";
            public const string NotFound = "[Not Found]";
            public const string OptNotFound = "[Option Not Found]";
            public const string CorrectUse = "[Correct Use]";
            public const string BeltCleaned = "[Belt Cleaned]";
            public const string EveryBeltCleaned = "[Every Belt Cleaned]";
            public const string InvCleaned = "[Inventory Cleaned]";
            public const string EveryInvCleaned = "InventoryCleaner.EveryInvCleaned";
            public const string WearCleaned = "[Wear Cleaned]";
            public const string EveryWearCleaned = "InventoryCleaner.EveryWearCleaned";
            public const string AllCleaned = "[All Cleaned]";
            public const string EveryAllCleaned = "InventoryCleaner.EveryAllCleaned";
            public const string OnDeath = "[On Death]";
            public const string Header = "[Interface Header]";
            public const string Gone = "[Interface Gome]";
            public const string Opts = "[Interface Options]";
            public const string Perms = "[Interface Perms]";
            public const string OptAll = "[Interface Opt All]";
            public const string OptInv = "[Interface Opt Inv]";
            public const string OptBelt = "[Interface Opt Belt]";
            public const string OptWear = "[Interface Opt Wear]";
            public const string OptEvery = "[Interface Opt Every]";
            public const string PermUse = "[Interface Perm Use]";
            public const string PermEvery = "[Interface Perm Every]";
            public const string PermDeath = "[Interface Perm Death]";
            public const string PermLogout = "[Interface Perm Logout]";
            public const string InvComands = "[Interface Comands]";
        }

        private string GetMessage(string key, string playerId = null, params object[] args)
        {
            string raw = _lang.GetMessage(key);
            if (args == null || args.Length == 0) return raw;
            try { return string.Format(raw, args); }
            catch (Exception ex)
            {
                Debug.LogWarning("[InventoryCleaner] FAIL: GetMessage format: " + ex.Message);
                return raw;
            }
        }

        #endregion

        #region Permissions

        private const string AdminGroup = "admin";

        public void RegisterPermissions()
        {
            foreach (string perm in AllPermissions)
                PermissionsBridge.RegisterPermission(perm);
            EnsureAdminGroupPermissions();
        }

        /// <summary>
        /// Grants command perms to Permissions group "admin". Does not grant wipe-on-death /
        /// wipe-on-logout — those mean "strip this player", so giving them to admin was wiping
        /// staff inventory on disconnect.
        /// </summary>
        private void EnsureAdminGroupPermissions()
        {
            if (!PermissionsBridge.IsAvailable)
            {
                Debug.LogWarning("[InventoryCleaner] Permissions not available — cannot grant inventorycleaner command perms to admin group.");
                return;
            }

            if (!PermissionsBridge.GroupExists(AdminGroup))
                PermissionsBridge.CreateGroup(AdminGroup, "Administrators", 0);

            int granted = 0;
            foreach (string perm in AdminCommandPermissions)
            {
                if (PermissionsBridge.GrantGroupPermission(AdminGroup, perm))
                    granted++;
            }

            int revoked = 0;
            foreach (string perm in OptInWipePermissions)
            {
                if (PermissionsBridge.RevokeGroupPermission(AdminGroup, perm))
                    revoked++;
            }

            Debug.Log($"[InventoryCleaner] Ensured Permissions group '{AdminGroup}' has command perms ({granted}/{AdminCommandPermissions.Length} grants); revoked opt-in wipe perms ({revoked}/{OptInWipePermissions.Length}).");
        }

        private bool HasPermission(BasePlayer player, string permissionName, bool send = true)
        {
            if (player == null) return false;
            bool has = PermissionsBridge.UserHasPermission(player.UserIDString, permissionName);
            if (!has && send)
            {
                string message = GenerateMessage(GetMessage(MessageKey.NoPermission, player.UserIDString, permissionName));
                SendChatMessage(player, message);
            }
            return has;
        }

        private bool[] HasAllPermissions(BasePlayer player)
        {
            var result = new bool[AllPermissions.Length];
            string id = player?.UserIDString ?? "";
            for (int i = 0; i < AllPermissions.Length; i++)
                result[i] = PermissionsBridge.UserHasPermission(id, AllPermissions[i]);
            return result;
        }

        #endregion

        #region Messaging

        private string GenerateMessage(string message, string color = "#ffffff", int size = 14, bool italic = false)
        {
            string prefix = _config?.MessagePrefix ?? "[Clear Inventory]";
            if (italic)
                return $"<size={size}>{prefix} <color={color}><i>{message}</i></color></size>";
            return $"<size={size}>{prefix} <color={color}>{message}</color></size>";
        }

        private void SendChatMessage(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrEmpty(message)) return;
            ulong icon = _config?.MessageImage ?? 0UL;
            if (player.net?.connection == null)
            {
                player.ChatMessage(message);
                return;
            }
            try
            {
                ConsoleNetwork.SendClientCommand(player.net.connection, "chat.add", 2, icon, message);
            }
            catch
            {
                player.ChatMessage(message);
            }
        }

        private void ReplyPlayer(BasePlayer player, string message)
        {
            if (player == null) return;
            SendChatMessage(player, message);
        }

        private void SendMessageToAll(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected) continue;
                SendChatMessage(player, message);
            }
        }

        #endregion

        #region Lifecycle hooks

        /// <summary>Oxide OnPlayerDeath — call from BasePlayer.Die prefix (before base.Die / loot).</summary>
        public void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (player == null) return;
            if (!HasPermission(player, Perms.ClearOnDeath, false)) return;

            player.inventory?.Strip();
            string msg = GenerateMessage(GetMessage(MessageKey.OnDeath, player.UserIDString, player.displayName), "green", 14);
            SendChatMessage(player, msg);
        }

        /// <summary>Oxide OnPlayerDisconnected.</summary>
        public void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;
            if (player.IsDead()) return;
            if (!HasPermission(player, Perms.ClearOnLogout, false)) return;

            Debug.Log($"[InventoryCleaner] Stripping inventory on logout for {player.displayName} ({player.UserIDString}) — has {Perms.ClearOnLogout}.");
            player.inventory?.Strip();
        }

        #endregion

        #region Clear helpers

        private void DeleteFromEveryone(BasePlayer actor, string opt)
        {
            if (actor == null) return;
            Debug.LogWarning($"[InventoryCleaner] {actor.displayName} is trying to run Delete Everyone!");
            if (!PermissionsBridge.UserHasPermission(actor.UserIDString, Perms.ClearOthers)) return;
            Debug.LogWarning($"[InventoryCleaner] {actor.displayName}: Running Delete Everyone Started!");

            // Parity with Oxide: broadcast inside the loop (message repeats per player).
            foreach (BasePlayer p in BasePlayer.allPlayerList)
            {
                if (p?.inventory == null) continue;
                var inv = p.inventory;
                switch (opt)
                {
                    case "main":
                        inv.Strip();
                        SendMessageToAll(GenerateMessage(GetMessage(MessageKey.EveryAllCleaned, actor.UserIDString), "red", 18));
                        break;
                    case "inv":
                        inv.containerMain?.Clear();
                        SendMessageToAll(GenerateMessage(GetMessage(MessageKey.EveryInvCleaned, actor.UserIDString), "red", 18));
                        break;
                    case "belt":
                        inv.containerBelt?.Clear();
                        SendMessageToAll(GenerateMessage(GetMessage(MessageKey.EveryBeltCleaned, actor.UserIDString), "red", 18));
                        break;
                    case "wear":
                        inv.containerWear?.Clear();
                        SendMessageToAll(GenerateMessage(GetMessage(MessageKey.EveryWearCleaned, actor.UserIDString), "red", 18));
                        break;
                    default:
                        ReplyPlayer(actor, GenerateMessage(GetMessage(MessageKey.OptNotFound, actor.UserIDString), "red", 14));
                        break;
                }
            }

            ItemManager.DoRemoves();
            Debug.LogWarning($"[InventoryCleaner] {actor.displayName}: Running Delete Everyone Finished!");
        }

        private void ClearOneContainer(BasePlayer player, ItemContainer container, string msgKey, string option = "main", bool every = false)
        {
            if (every)
            {
                DeleteFromEveryone(player, option);
                return;
            }

            container?.Clear();
            ItemManager.DoRemoves();

            string msg = GenerateMessage(GetMessage(msgKey, player.UserIDString, player.displayName), "green", 14);
            ReplyPlayer(player, msg);
        }

        private void ClearAllContainers(BasePlayer player, string msgKey, string option = "main", bool every = false)
        {
            if (every)
            {
                DeleteFromEveryone(player, option);
                return;
            }

            player.inventory?.Strip();
            ItemManager.DoRemoves();

            string msg = GenerateMessage(GetMessage(msgKey, player.UserIDString, player.displayName), "green", 14);
            ReplyPlayer(player, msg);
        }

        #endregion

        #region Panels + command

        private void CommandsPanel(BasePlayer player)
        {
            var sb = new StringBuilder();
            sb.Append(GetMessage(MessageKey.Header, player.UserIDString, Author, VersionString));
            sb.Append(GetMessage(MessageKey.Gone, player.UserIDString));
            sb.Append(GetMessage(MessageKey.Opts, player.UserIDString));
            sb.Append(GetMessage(MessageKey.OptAll, player.UserIDString));
            sb.Append(GetMessage(MessageKey.OptInv, player.UserIDString));
            sb.Append(GetMessage(MessageKey.OptBelt, player.UserIDString));
            sb.Append(GetMessage(MessageKey.OptWear, player.UserIDString));
            sb.Append(GetMessage(MessageKey.OptEvery, player.UserIDString));
            ReplyPlayer(player, sb.ToString());
        }

        private void HelpPanel(BasePlayer player)
        {
            bool[] mCp = HasAllPermissions(player);
            var sb = new StringBuilder();
            sb.Append(GetMessage(MessageKey.Header, player.UserIDString, Author, VersionString));
            sb.Append(GetMessage(MessageKey.Gone, player.UserIDString));
            sb.Append(GetMessage(MessageKey.Perms, player.UserIDString, player.displayName));
            sb.Append(GetMessage(MessageKey.PermUse, player.UserIDString, mCp[0]));
            sb.Append(GetMessage(MessageKey.PermEvery, player.UserIDString, mCp[1]));
            sb.Append(GetMessage(MessageKey.PermDeath, player.UserIDString, mCp[2]));
            sb.Append(GetMessage(MessageKey.PermLogout, player.UserIDString, mCp[3]));
            sb.Append(GetMessage(MessageKey.InvComands, player.UserIDString));
            ReplyPlayer(player, sb.ToString());
        }

        public void HandleClearCommand(BasePlayer player, string[] args)
        {
            if (player == null) return;
            if (!PermissionsBridge.UserHasPermission(player.UserIDString, Perms.Clear))
                return;

            if (args == null || args.Length == 0)
            {
                ClearAllContainers(player, MessageKey.AllCleaned);
                return;
            }

            bool every = args.Length > 1 && string.Equals(args[1], "everyone", StringComparison.OrdinalIgnoreCase);
            string opt = args[0].ToLowerInvariant();

            switch (opt)
            {
                case "main":
                    ClearAllContainers(player, MessageKey.AllCleaned, opt, every);
                    break;
                case "inv":
                    ClearOneContainer(player, player.inventory?.containerMain, MessageKey.InvCleaned, opt, every);
                    break;
                case "belt":
                    ClearOneContainer(player, player.inventory?.containerBelt, MessageKey.InvCleaned, opt, every);
                    break;
                case "wear":
                    ClearOneContainer(player, player.inventory?.containerWear, MessageKey.InvCleaned, opt, every);
                    break;
                case "help":
                    HelpPanel(player);
                    break;
                case "cmds":
                    CommandsPanel(player);
                    break;
                default:
                    ReplyPlayer(player, GenerateMessage(GetMessage(MessageKey.OptNotFound, player.UserIDString, opt)));
                    break;
            }
        }

        #endregion
    }

    /// <summary>Embedded messages + optional HarmonyLanguage/InventoryCleaner.json overrides.</summary>
    internal sealed class LangStore
    {
        private readonly Dictionary<string, string> _en = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool _fileLoaded;

        public void RegisterMessages(Dictionary<string, string> messages, string language)
        {
            if (messages == null) return;
            if (!string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)) return;
            foreach (var kv in messages)
                _en[kv.Key] = kv.Value ?? "";
        }

        public void LoadHarmonyLanguageOverrides(string serverRoot, string modName)
        {
            if (_fileLoaded) return;
            _fileLoaded = true;
            try
            {
                string path = Path.Combine(serverRoot, "HarmonyLanguage", modName + ".json");
                if (!File.Exists(path)) return;
                var map = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (map == null || map.Count == 0) return;
                foreach (var kv in map)
                    _en[kv.Key] = kv.Value ?? "";
                Debug.Log($"[InventoryCleaner] Loaded {map.Count} language strings from HarmonyLanguage/{modName}.json");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[InventoryCleaner] HarmonyLanguage load failed: " + ex.Message);
            }
        }

        public string GetMessage(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            return _en.TryGetValue(key, out string msg) && !string.IsNullOrEmpty(msg) ? msg : key;
        }
    }
}
