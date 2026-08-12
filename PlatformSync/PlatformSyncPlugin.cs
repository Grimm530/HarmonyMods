using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PlatformSync
{
    /// <summary>
    /// Near-identical port of Oxide PlatformSync 1.1.01 (PlatformSync | Grimm530).
    /// Oxide APIs are replaced by Compat shims; hooks arrive via Harmony patches.
    /// </summary>
    public class PlatformSyncPlugin
    {
        public static PlatformSyncPlugin Instance { get; private set; }

        private Compat.PluginRef Rustcord;
        private List<LinkLogEntry> _linkEntries;
        private int _pendingFlushCount;
        private bool _linkLogFlushScheduled;
        private int _nextRequestId;
        private Dictionary<string, int> _pendingLinkRequests = new Dictionary<string, int>();
        private int _nextDebugRequestId;
        private Dictionary<int, string> _pendingDebugRequests = new Dictionary<int, string>();
        private const int LogBufferFlushCount = 10;
        private const float LogBufferFlushDelay = 5f;
        private const float ValidateRequestTimeout = 15f;
        private const float RecheckRequestDelay = 0.75f;
        private const int DataVersion = 1;
        private const string LocalLinkedAction = "local-linked";
        private const string LocalUnlinkedAction = "local-unlinked";

        private bool _recheckRunning;
        private bool _recheckCancel;
        private readonly Queue<string> _recheckQueue = new Queue<string>();
        private int _recheckTotal;
        private int _recheckDone;
        private int _recheckFailed;
        private int _recheckVerifiedRemoved;
        private int _recheckNitroRemoved;
        private int _recheckVerifiedAdded;
        private int _recheckNitroAdded;
        private string _recheckScope = "all";
        private Action<string> _recheckReply;

        private static string LinksDataPath => Compat.LinksDataPath;
        private static string LinksLogLegacyPath => Compat.LinksLogLegacyPath;

        private Compat.TimerHelper timer => Compat.Timer;
        private Compat.WebRequestHelper webrequest => Compat.Webrequest;
        private Compat.PermissionHelper permission => Compat.Permission;
        private Compat.LangHelper lang => Compat.Lang;
        private Compat.PlayersHelper covalencePlayers => Compat.Players;

        class LinkLogEntry
        {
            [JsonProperty("timestamp")] public string Timestamp;
            [JsonProperty("steam_id")] public string SteamId;
            [JsonProperty("steam_name")] public string SteamName;
            [JsonProperty("discord_id")] public string DiscordId;
            [JsonProperty("discord_name")] public string DiscordName;
            [JsonProperty("action")] public string Action;
            [JsonProperty("local_only")] public bool LocalOnly;
            [JsonProperty("discord_role")] public string DiscordRole;
            [JsonProperty("oxide_group")] public string OxideGroup;
        }

        class StoredLinksData
        {
            [JsonProperty("version")] public int Version = DataVersion;
            [JsonProperty("updated")] public string Updated;
            [JsonProperty("entries")] public List<LinkLogEntry> Entries;
        }

        #region Config accessors (Oxide Config[key] parity)

        private object ConfigGet(string key) => PlatformSyncConfig.Get(key);

        private void ConfigSet(string key, object value) => PlatformSyncConfig.Set(key, value);

        private void SaveConfig() => PlatformSyncConfig.SaveConfig();

        private void Puts(string message) => Compat.Puts(message);

        private Compat.PlayerWrapper GetIPlayer(BasePlayer player) =>
            player == null ? null : new Compat.PlayerWrapper(player);

        #endregion

        #region Lifecycle

        public void Init()
        {
            Instance = this;
            Compat.EnsureDataFolders();
            PlatformSyncConfig.LoadConfig();
            Compat.Permission.EnsureLinked();
            Rustcord = Compat.PluginRef.Find("Rustcord");
            LoadDefaultMessages();
            LoadLinksData();
            RegisterCommands();
            Puts("Loaded. Config: " + (PlatformSyncConfig.ConfigPath ?? "n/a")
                 + " LocalVerifyGroup=" + LocalVerifyOxideGroup
                 + " PermissionsBound=" + Compat.Permission.IsPermissionsBound);
        }

        public void Shutdown()
        {
            _recheckCancel = true;
            _recheckQueue.Clear();
            _recheckRunning = false;
            FlushLinkLog();
            Compat.UnregisterCommands();
            if (Instance == this) Instance = null;
        }

        private void RegisterCommands()
        {
            Compat.RegisterConsoleCommand("ps.testlink", TestLinkConsoleCommand, adminOnly: true);
            Compat.RegisterConsoleCommand("ps.testurl", TestUrlConsoleCommand, adminOnly: true);
            Compat.RegisterConsoleCommand("ps.recheck", RecheckConsoleCommand, adminOnly: true);
            Compat.RegisterConsoleCommand("localverify", LocalVerifyConsoleCommand, adminOnly: true);
            Compat.RegisterConsoleCommand("localverifycheck", LocalVerifyCheckConsoleCommand, adminOnly: true);
            Compat.RegisterConsoleCommand("localverifyroles", LocalVerifyRolesConsoleCommand, adminOnly: true);
            // Chat commands also work as console: link / testlink / testurl (from chat patch + console)
            Compat.RegisterConsoleCommand("link", arg =>
            {
                var player = Compat.GetPlayer(arg);
                if (player != null) LinkCommand(player, "link", ToArgs(arg));
            }, adminOnly: false);
            Compat.RegisterConsoleCommand("testlink", arg =>
            {
                var player = Compat.GetPlayer(arg);
                if (player != null) TestLinkCommand(player, "testlink", ToArgs(arg));
                else TestLinkConsoleCommand(arg);
            }, adminOnly: false);
            Compat.RegisterConsoleCommand("testurl", arg =>
            {
                var player = Compat.GetPlayer(arg);
                if (player != null) TestUrlCommand(player, "testurl", ToArgs(arg));
                else TestUrlConsoleCommand(arg);
            }, adminOnly: false);
        }

        private static string[] ToArgs(ConsoleSystem.Arg arg)
        {
            if (arg?.Args == null || arg.Args.Length == 0) return Array.Empty<string>();
            var result = new string[arg.Args.Length];
            for (int i = 0; i < arg.Args.Length; i++)
                result[i] = arg.Args[i].ToString() ?? "";
            return result;
        }

        /// <summary>Chat command dispatcher from Chat.say Harmony patch. Returns true if handled.</summary>
        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrEmpty(message)) return false;
            string text = message.Trim();
            if (text.Length == 0) return false;
            if (text[0] == '/' || text[0] == '!')
                text = text.Substring(1).Trim();
            if (text.Length == 0) return false;

            string[] parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts[0];
            string[] args = parts.Length > 1 ? new string[parts.Length - 1] : Array.Empty<string>();
            for (int i = 1; i < parts.Length; i++)
                args[i - 1] = parts[i];

            if (string.Equals(cmd, "link", StringComparison.OrdinalIgnoreCase))
            {
                LinkCommand(player, "link", args);
                return true;
            }
            if (string.Equals(cmd, "testlink", StringComparison.OrdinalIgnoreCase))
            {
                TestLinkCommand(player, "testlink", args);
                return true;
            }
            if (string.Equals(cmd, "testurl", StringComparison.OrdinalIgnoreCase))
            {
                TestUrlCommand(player, "testurl", args);
                return true;
            }
            return false;
        }

        #endregion

        #region Data load/save and migration
        private void LoadLinksData()
        {
            _linkEntries = new List<LinkLogEntry>();
            string dir = Path.GetDirectoryName(LinksDataPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // Prefer Harmony data; migrate from Oxide data if needed
            string path = LinksDataPath;
            if (!File.Exists(path) && File.Exists(Compat.OxideLinksDataPath))
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    File.Copy(Compat.OxideLinksDataPath, path);
                    Puts("Migrated links.json from oxide/data/PlatformSync/");
                }
                catch (Exception ex) { Puts("Failed to migrate links.json: " + ex.Message); }
            }

            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var data = JsonConvert.DeserializeObject<StoredLinksData>(json);
                    if (data?.Entries != null)
                        _linkEntries = data.Entries;
                }
                catch (Exception ex) { Puts("PlatformSync failed to load links.json: " + ex.Message); }
                return;
            }

            string legacy = LinksLogLegacyPath;
            if (!File.Exists(legacy) && File.Exists(Compat.OxideLinksLogLegacyPath))
                legacy = Compat.OxideLinksLogLegacyPath;

            if (File.Exists(legacy))
                MigrateFromLegacyLog(legacy);
        }

        private void MigrateFromLegacyLog(string legacyPath = null)
        {
            try
            {
                string[] lines = File.ReadAllLines(legacyPath ?? LinksLogLegacyPath);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var obj = JObject.Parse(line);
                        _linkEntries.Add(new LinkLogEntry
                        {
                            Timestamp = obj["timestamp"]?.ToString() ?? "",
                            SteamId = obj["steam_id"]?.ToString() ?? "",
                            SteamName = obj["steam_name"]?.ToString() ?? "",
                            DiscordId = obj["discord_id"]?.ToString() ?? "",
                            DiscordName = obj["discord_name"]?.ToString() ?? "",
                            Action = obj["action"]?.ToString() ?? ""
                        });
                    }
                    catch { /* skip bad lines */ }
                }
                SaveLinksData();
                Puts("PlatformSync migrated " + _linkEntries.Count + " entries from links.log to links.json");
            }
            catch (Exception ex) { Puts("PlatformSync migration failed: " + ex.Message); }
        }

        private void SaveLinksData()
        {
            try
            {
                var data = new StoredLinksData
                {
                    Version = DataVersion,
                    Updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    Entries = _linkEntries ?? new List<LinkLogEntry>()
                };
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(LinksDataPath, json);
            }
            catch (Exception ex) { Puts("PlatformSync failed to save links.json: " + ex.Message); }
        }
        #endregion

        #region Helpers
        private void AppendLinkLog(string steamId, string steamName, string discordId, string discordName, string action, bool localOnly = false, string discordRole = "", string oxideGroup = "")
        {
            var logLinks = ConfigGet("LogLinks");
            if (logLinks != null && logLinks is bool bl && bl == false) return;
            var entry = new LinkLogEntry
            {
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "Z",
                SteamId = steamId ?? "",
                SteamName = steamName ?? "",
                DiscordId = discordId ?? "",
                DiscordName = discordName ?? "",
                Action = action ?? "",
                LocalOnly = localOnly,
                DiscordRole = discordRole ?? "",
                OxideGroup = oxideGroup ?? ""
            };
            if (_linkEntries == null) _linkEntries = new List<LinkLogEntry>();
            _linkEntries.Add(entry);
            _pendingFlushCount++;
            if (_pendingFlushCount >= LogBufferFlushCount)
            {
                _pendingFlushCount = 0;
                FlushLinkLog();
            }
            else if (!_linkLogFlushScheduled)
            {
                _linkLogFlushScheduled = true;
                timer.Once(LogBufferFlushDelay, () => { _linkLogFlushScheduled = false; _pendingFlushCount = 0; FlushLinkLog(); });
            }
        }

        private string GetConfigString(string key, string defaultValue)
        {
            if (ConfigGet(key) == null)
            {
                ConfigSet(key, defaultValue);
                SaveConfig();
            }

            string value = ConfigGet(key)?.ToString();
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
        }

        private string LocalVerifyDiscordRole => GetConfigString("LocalVerifyDiscordRole", "Verified");
        private string LocalVerifyOxideGroup => GetConfigString("LocalVerifyOxideGroup", "verified");

        private bool IsLocalLinkedAction(string action)
        {
            return string.Equals(action, LocalLinkedAction, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsLocalUnlinkedAction(string action)
        {
            return string.Equals(action, LocalUnlinkedAction, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsValidSnowflake(string value)
        {
            ulong parsed;
            return !string.IsNullOrWhiteSpace(value) && ulong.TryParse(value, out parsed) && parsed != 0;
        }

        private bool IsValidSteamId(string value)
        {
            ulong parsed;
            return !string.IsNullOrWhiteSpace(value) && ulong.TryParse(value, out parsed) && parsed != 0;
        }

        private string GetKnownSteamName(string steamId)
        {
            var player = covalencePlayers.FindPlayerById(steamId);
            return player?.Name ?? "";
        }

        private void EnsureGroup(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName)) return;
            if (!permission.GroupExists(groupName)) permission.CreateGroup(groupName, groupName, 0);
        }

        private bool TryDiscordUserHasRole(string discordId, string roleName, out bool hasRole, out string error)
        {
            hasRole = false;
            error = "";

            if (Rustcord == null || !Rustcord.IsLoaded)
            {
                error = "Rustcord is not loaded.";
                return false;
            }

            object result = Rustcord.Call("DiscordUserHasRole", discordId, roleName);
            if (result is bool)
            {
                hasRole = (bool)result;
                return true;
            }

            error = "Rustcord did not return a Discord role check result.";
            return false;
        }

        private string[] GetDiscordUserRoleNames(string discordId, out string error)
        {
            error = "";
            if (Rustcord == null || !Rustcord.IsLoaded)
            {
                error = "Rustcord is not loaded.";
                return new string[0];
            }

            object result = Rustcord.Call("GetDiscordUserRoleNames", discordId);
            var roles = result as string[];
            if (roles != null) return roles;

            error = "Rustcord did not return Discord roles for that user.";
            return new string[0];
        }

        private Dictionary<string, LinkLogEntry> GetActiveLocalLinks()
        {
            var activeLinks = new Dictionary<string, LinkLogEntry>();
            if (_linkEntries == null) return activeLinks;

            foreach (var entry in _linkEntries)
            {
                if (entry == null || !entry.LocalOnly) continue;
                if (string.IsNullOrWhiteSpace(entry.SteamId) || string.IsNullOrWhiteSpace(entry.DiscordId)) continue;

                string key = entry.SteamId;
                if (IsLocalLinkedAction(entry.Action))
                    activeLinks[key] = entry;
                else if (IsLocalUnlinkedAction(entry.Action))
                    activeLinks.Remove(key);
            }

            return activeLinks;
        }

        private bool HasActiveLocalLinkForGroup(string steamId, string groupName)
        {
            if (string.IsNullOrWhiteSpace(steamId) || string.IsNullOrWhiteSpace(groupName)) return false;
            foreach (var entry in GetActiveLocalLinks().Values)
            {
                string entryGroup = string.IsNullOrWhiteSpace(entry.OxideGroup) ? LocalVerifyOxideGroup : entry.OxideGroup;
                if (entry.SteamId == steamId && string.Equals(entryGroup, groupName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private void LocalVerify(string steamId, string discordId, Action<string> reply)
        {
            if (!IsValidSteamId(steamId))
            {
                reply("Usage: localverify <steamid> <discordid>");
                return;
            }

            if (!IsValidSnowflake(discordId))
            {
                reply("Usage: localverify <steamid> <discordid>");
                return;
            }

            string roleName = LocalVerifyDiscordRole;
            string oxideGroup = LocalVerifyOxideGroup;

            bool hasRole;
            string error;
            if (!TryDiscordUserHasRole(discordId, roleName, out hasRole, out error))
            {
                reply(error);
                return;
            }

            if (!hasRole)
            {
                reply("Discord user " + discordId + " does not have the " + roleName + " role.");
                return;
            }

            EnsureGroup(oxideGroup);
            if (!permission.UserHasGroup(steamId, oxideGroup))
                permission.AddUserGroup(steamId, oxideGroup);

            AppendLinkLog(steamId, GetKnownSteamName(steamId), discordId, "", LocalLinkedAction, true, roleName, oxideGroup);
            FlushLinkLog();
            reply("Locally verified " + steamId + " with Discord " + discordId + " and added group " + oxideGroup + ".");
        }

        private void LocalVerifyCheck(Action<string> reply)
        {
            var activeLinks = new List<LinkLogEntry>(GetActiveLocalLinks().Values);
            if (activeLinks.Count == 0)
            {
                reply("No local PlatformSync links found.");
                return;
            }

            int kept = 0;
            int removed = 0;
            int failed = 0;

            foreach (var entry in activeLinks)
            {
                string roleName = string.IsNullOrWhiteSpace(entry.DiscordRole) ? LocalVerifyDiscordRole : entry.DiscordRole;
                string oxideGroup = string.IsNullOrWhiteSpace(entry.OxideGroup) ? LocalVerifyOxideGroup : entry.OxideGroup;

                bool hasRole;
                string error;
                if (!TryDiscordUserHasRole(entry.DiscordId, roleName, out hasRole, out error))
                {
                    failed++;
                    Puts("PlatformSync localverifycheck failed for " + entry.SteamId + "/" + entry.DiscordId + ": " + error);
                    continue;
                }

                if (hasRole)
                {
                    EnsureGroup(oxideGroup);
                    if (!permission.UserHasGroup(entry.SteamId, oxideGroup))
                        permission.AddUserGroup(entry.SteamId, oxideGroup);
                    kept++;
                    continue;
                }

                if (permission.UserHasGroup(entry.SteamId, oxideGroup))
                    permission.RemoveUserGroup(entry.SteamId, oxideGroup);

                AppendLinkLog(entry.SteamId, entry.SteamName, entry.DiscordId, entry.DiscordName, LocalUnlinkedAction, true, roleName, oxideGroup);
                removed++;
            }

            FlushLinkLog();
            reply("Local verify check complete. Kept: " + kept + ", removed: " + removed + ", failed: " + failed + ".");
        }

        private void FlushLinkLog()
        {
            if (_linkEntries == null || _linkEntries.Count == 0) return;
            try { SaveLinksData(); }
            catch (Exception ex) { Puts("PlatformSync links write failed: " + ex.Message); }
        }

        private static string JsonString(JObject obj, params string[] keys)
        {
            if (obj == null) return "";
            foreach (var key in keys)
            {
                var t = obj[key];
                if (t != null && t.Type != JTokenType.Null && t.Type != JTokenType.Undefined) return t.ToString();
            }
            return "";
        }

        private int BeginLinkRequest(BasePlayer player)
        {
            if (player == null) return 0;
            int requestId = ++_nextRequestId;
            _pendingLinkRequests[player.UserIDString] = requestId;
            return requestId;
        }

        private bool CompleteLinkRequest(BasePlayer player, int requestId)
        {
            if (player == null) return true;
            int activeRequestId;
            if (!_pendingLinkRequests.TryGetValue(player.UserIDString, out activeRequestId)) return false;
            if (activeRequestId != requestId) return false;
            _pendingLinkRequests.Remove(player.UserIDString);
            return true;
        }

        private void StartDebugRequest(string label, string url, Action<string> reply = null)
        {
            int requestId = ++_nextDebugRequestId;
            _pendingDebugRequests[requestId] = label;
            Puts("PlatformSync debug request " + requestId + " started: " + label + " -> " + url);

            timer.Once(ValidateRequestTimeout + 1f, () =>
            {
                string activeLabel;
                if (!_pendingDebugRequests.TryGetValue(requestId, out activeLabel)) return;
                _pendingDebugRequests.Remove(requestId);
                Puts("PlatformSync debug request " + requestId + " timed out locally: " + activeLabel);
                reply?.Invoke("Debug request timed out locally after " + (ValidateRequestTimeout + 1f) + "s: " + activeLabel);
            });

            Dictionary<string, string> headers = new Dictionary<string, string> { { "Accept", "*/*" } };
            webrequest.Enqueue(url, null, (code, response) =>
            {
                string activeLabel;
                if (!_pendingDebugRequests.TryGetValue(requestId, out activeLabel))
                {
                    Puts("PlatformSync debug request " + requestId + " callback arrived after local timeout.");
                    return;
                }

                _pendingDebugRequests.Remove(requestId);
                int responseLength = response != null ? response.Length : 0;
                string responsePreview = string.IsNullOrWhiteSpace(response) ? "<empty>" : response.Substring(0, Math.Min(200, response.Length));
                Puts("PlatformSync debug request " + requestId + " callback: code=" + code + ", responseLength=" + responseLength + ", label=" + activeLabel);
                Puts("PlatformSync debug request " + requestId + " body preview: " + responsePreview);
                reply?.Invoke("Debug callback for " + activeLabel + ": code=" + code + ", len=" + responseLength + ", body=" + responsePreview);
            }, this, RequestMethod.GET, headers, ValidateRequestTimeout);
        }
        #endregion

        #region CheckPlayer
        public void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;
            float timeout = ValidateRequestTimeout;
            Dictionary<string, string> headers = new Dictionary<string, string> { { "Accept", "*/*" } };

            webrequest.Enqueue("https://link.platformsync.io/validate.php?action=validate&steamid=" + player.UserIDString + "&guildid=" + ConfigGet("GuildID") + "&auth=" + ConfigGet("APIToken"), null, (code, response) =>
                GetCallback(code, response, player, false), this, RequestMethod.GET, headers, timeout);
        }

        private void LinkCommand(BasePlayer player, string command, string[] args)
        {
            float timeout = ValidateRequestTimeout;
            Dictionary<string, string> headers = new Dictionary<string, string> { { "Accept", "*/*" } };
            int requestId = BeginLinkRequest(player);
            Puts("PlatformSync /link requested by " + player.UserIDString + " (" + (player?.displayName ?? "unknown") + ")");
            player.ChatMessage(Lang("CheckingLink"));
            timer.Once(ValidateRequestTimeout + 1f, () =>
            {
                if (player == null) return;
                if (!CompleteLinkRequest(player, requestId)) return;
                Puts("PlatformSync local timeout fallback triggered for " + player.UserIDString + " request " + requestId + ".");
                player.ChatMessage(Lang("ApiTimeout"));
            });

            webrequest.Enqueue("https://link.platformsync.io/validate.php?action=validate&steamid=" + player.UserIDString + "&guildid=" + ConfigGet("GuildID") + "&auth=" + ConfigGet("APIToken"), null, (code, response) =>
                GetCallback(code, response, player, true, requestId), this, RequestMethod.GET, headers, timeout);
        }

        private void TestLinkCommand(BasePlayer player, string command, string[] args)
        {
            if (player != null && !player.IsAdmin)
            {
                player.ChatMessage(Lang("NoPermission"));
                return;
            }

            string steamId = args != null && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
                ? args[0]
                : player?.UserIDString ?? "";

            if (string.IsNullOrWhiteSpace(steamId))
            {
                player?.ChatMessage("Usage: /testlink <steamid>");
                return;
            }

            player?.ChatMessage("Running PlatformSync debug checks for " + steamId + "...");
            StartDebugRequest("root", "https://link.platformsync.io/", message => player?.ChatMessage(message));
            StartDebugRequest("validate:" + steamId,
                "https://link.platformsync.io/validate.php?action=validate&steamid=" + steamId + "&guildid=" + ConfigGet("GuildID") + "&auth=" + ConfigGet("APIToken"),
                message => player?.ChatMessage(message));
        }

        private void TestLinkConsoleCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || !arg.IsAdmin)
            {
                arg?.ReplyWith("You do not have permission to use this command.");
                return;
            }

            string steamId = arg.Args != null && arg.Args.Length > 0 && !string.IsNullOrWhiteSpace(arg.GetString(0))
                ? arg.GetString(0)
                : "76561197967147516";

            arg.ReplyWith("Running PlatformSync debug checks for " + steamId + "...");
            StartDebugRequest("root", "https://link.platformsync.io/", message => arg.ReplyWith(message));
            StartDebugRequest("validate:" + steamId,
                "https://link.platformsync.io/validate.php?action=validate&steamid=" + steamId + "&guildid=" + ConfigGet("GuildID") + "&auth=" + ConfigGet("APIToken"),
                message => arg.ReplyWith(message));
        }

        private void TestUrlCommand(BasePlayer player, string command, string[] args)
        {
            if (player != null && !player.IsAdmin)
            {
                player.ChatMessage(Lang("NoPermission"));
                return;
            }

            player?.ChatMessage("Running PlatformSync network tests...");
            StartDebugRequest("http-example", "http://example.com/", message => player?.ChatMessage(message));
            StartDebugRequest("https-example", "https://example.com/", message => player?.ChatMessage(message));
            StartDebugRequest("https-platformsync", "https://link.platformsync.io/", message => player?.ChatMessage(message));
        }

        private void TestUrlConsoleCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || !arg.IsAdmin)
            {
                arg?.ReplyWith("You do not have permission to use this command.");
                return;
            }

            arg.ReplyWith("Running PlatformSync network tests...");
            StartDebugRequest("http-example", "http://example.com/", message => arg.ReplyWith(message));
            StartDebugRequest("https-example", "https://example.com/", message => arg.ReplyWith(message));
            StartDebugRequest("https-platformsync", "https://link.platformsync.io/", message => arg.ReplyWith(message));
        }

        private void LocalVerifyConsoleCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || !arg.IsAdmin)
            {
                arg?.ReplyWith("You do not have permission to use this command.");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 2)
            {
                arg.ReplyWith("Usage: localverify <steamid> <discordid>");
                return;
            }

            LocalVerify(arg.GetString(0), arg.GetString(1), message => arg.ReplyWith(message));
        }

        private void LocalVerifyCheckConsoleCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || !arg.IsAdmin)
            {
                arg?.ReplyWith("You do not have permission to use this command.");
                return;
            }

            LocalVerifyCheck(message => arg.ReplyWith(message));
        }

        private void LocalVerifyRolesConsoleCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || !arg.IsAdmin)
            {
                arg?.ReplyWith("You do not have permission to use this command.");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1 || !IsValidSnowflake(arg.GetString(0)))
            {
                arg.ReplyWith("Usage: localverifyroles <discordid>");
                return;
            }

            string error;
            string[] roles = GetDiscordUserRoleNames(arg.GetString(0), out error);
            if (!string.IsNullOrWhiteSpace(error))
            {
                arg.ReplyWith(error);
                return;
            }

            arg.ReplyWith(roles.Length == 0
                ? "No cached Discord roles found for " + arg.GetString(0) + "."
                : "Cached Discord roles for " + arg.GetString(0) + ": " + string.Join(", ", roles));
        }

        private void RecheckConsoleCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || !arg.IsAdmin)
            {
                arg?.ReplyWith("You do not have permission to use this command.");
                return;
            }

            string scope = "all";
            if (arg.Args != null && arg.Args.Length > 0 && !string.IsNullOrWhiteSpace(arg.GetString(0)))
                scope = arg.GetString(0).Trim();

            StartGroupRecheck(scope, msg =>
            {
                Puts(msg);
                arg.ReplyWith(msg);
            });
        }

        /// <summary>
        /// Queue Platform Sync API checks for members of verified and/or nitro.
        /// Only adds/removes group membership — never deletes permission user data.
        /// Usage: ps.recheck [all|verified|nitro|cancel|status]
        /// </summary>
        private void StartGroupRecheck(string scope, Action<string> reply)
        {
            reply = reply ?? (msg => Puts(msg));
            scope = string.IsNullOrWhiteSpace(scope) ? "all" : scope.Trim().ToLowerInvariant();

            if (scope == "cancel" || scope == "stop")
            {
                if (!_recheckRunning)
                {
                    reply("No PlatformSync recheck is running.");
                    return;
                }
                _recheckCancel = true;
                reply("Cancelling PlatformSync recheck after the current request...");
                return;
            }

            if (scope == "status")
            {
                if (!_recheckRunning)
                {
                    reply("No PlatformSync recheck is running.");
                    return;
                }
                reply("PlatformSync recheck in progress: " + _recheckDone + "/" + _recheckTotal
                      + " (scope=" + _recheckScope + ", queue=" + _recheckQueue.Count + ").");
                return;
            }

            if (_recheckRunning)
            {
                reply("A recheck is already running (" + _recheckDone + "/" + _recheckTotal
                      + "). Use: ps.recheck cancel");
                return;
            }

            bool wantVerified = scope == "all" || scope == "verified" || scope == "link" || scope == "discord";
            bool wantNitro = scope == "all" || scope == "nitro";
            if (!wantVerified && !wantNitro)
            {
                reply("Usage: ps.recheck [all|verified|nitro|status|cancel]");
                return;
            }

            Compat.Permission.EnsureLinked();
            string verifiedGroup = LocalVerifyOxideGroup;
            if (string.IsNullOrWhiteSpace(verifiedGroup)) verifiedGroup = "verified";
            const string nitroGroup = "nitro";

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (wantVerified)
            {
                EnsureGroup(verifiedGroup);
                foreach (var id in permission.GetUsersInGroup(verifiedGroup))
                    if (!string.IsNullOrWhiteSpace(id)) ids.Add(id.Trim());
            }
            if (wantNitro)
            {
                EnsureGroup(nitroGroup);
                foreach (var id in permission.GetUsersInGroup(nitroGroup))
                    if (!string.IsNullOrWhiteSpace(id)) ids.Add(id.Trim());
            }

            if (ids.Count == 0)
            {
                reply("No users found in selected group(s) for scope '" + scope + "'.");
                return;
            }

            _recheckQueue.Clear();
            foreach (var id in ids)
                _recheckQueue.Enqueue(id);

            _recheckScope = scope;
            _recheckTotal = _recheckQueue.Count;
            _recheckDone = 0;
            _recheckFailed = 0;
            _recheckVerifiedRemoved = 0;
            _recheckNitroRemoved = 0;
            _recheckVerifiedAdded = 0;
            _recheckNitroAdded = 0;
            _recheckCancel = false;
            _recheckRunning = true;
            _recheckReply = reply;

            reply("Starting PlatformSync recheck for " + _recheckTotal + " user(s) (scope=" + scope
                  + "). Only group membership will change; user data is kept. Delay=" + RecheckRequestDelay + "s.");
            ProcessNextRecheck();
        }

        private void ProcessNextRecheck()
        {
            if (!_recheckRunning) return;

            if (_recheckCancel || _recheckQueue.Count == 0)
            {
                FinishRecheck(_recheckCancel ? "cancelled" : "complete");
                return;
            }

            string steamId = _recheckQueue.Dequeue();
            string url = "https://link.platformsync.io/validate.php?action=validate&steamid=" + steamId
                         + "&guildid=" + ConfigGet("GuildID") + "&auth=" + ConfigGet("APIToken");
            var headers = new Dictionary<string, string> { { "Accept", "*/*" } };

            webrequest.Enqueue(url, null, (code, response) =>
            {
                if (!_recheckRunning) return;

                _recheckDone++;
                string steamName = GetKnownSteamName(steamId);
                if (code != 200 || string.IsNullOrWhiteSpace(response))
                {
                    _recheckFailed++;
                    Puts("PlatformSync recheck failed for " + steamId + " (code " + code + ").");
                }
                else
                {
                    try
                    {
                        var linkDetails = JObject.Parse(response);
                        ApplyValidateResult(steamId, steamName, linkDetails, trackRecheckStats: true);
                    }
                    catch (Exception ex)
                    {
                        _recheckFailed++;
                        Puts("PlatformSync recheck parse failed for " + steamId + ": " + ex.Message);
                    }
                }

                if (_recheckDone % 25 == 0 || _recheckQueue.Count == 0)
                {
                    Puts("PlatformSync recheck progress: " + _recheckDone + "/" + _recheckTotal
                         + " failed=" + _recheckFailed
                         + " verified-=" + _recheckVerifiedRemoved + " nitro-=" + _recheckNitroRemoved
                         + " verified+=" + _recheckVerifiedAdded + " nitro+=" + _recheckNitroAdded);
                }

                timer.Once(RecheckRequestDelay, ProcessNextRecheck);
            }, this, RequestMethod.GET, headers, ValidateRequestTimeout);
        }

        private void FinishRecheck(string status)
        {
            FlushLinkLog();
            string summary = "PlatformSync recheck " + status + ". Checked: " + _recheckDone + "/" + _recheckTotal
                             + ", failed: " + _recheckFailed
                             + ", verified removed: " + _recheckVerifiedRemoved
                             + ", nitro removed: " + _recheckNitroRemoved
                             + ", verified added: " + _recheckVerifiedAdded
                             + ", nitro added: " + _recheckNitroAdded
                             + ".";
            _recheckRunning = false;
            _recheckCancel = false;
            _recheckQueue.Clear();
            var reply = _recheckReply;
            _recheckReply = null;
            Puts(summary);
            reply?.Invoke(summary);
        }

        /// <summary>
        /// Apply Platform Sync validate payload for any Steam ID (online or offline).
        /// Only mutates group membership — never deletes permission user records.
        /// </summary>
        private void ApplyValidateResult(string steamId, string steamName, JObject linkDetails, bool trackRecheckStats = false, BasePlayer notifyPlayer = null)
        {
            if (string.IsNullOrWhiteSpace(steamId) || linkDetails == null) return;
            if (string.IsNullOrWhiteSpace(steamName))
                steamName = GetKnownSteamName(steamId);

            bool notify = notifyPlayer != null;

            if (Convert.ToBoolean(ConfigGet("EnableDiscordLink")) == true)
            {
                bool linked = linkDetails["linked"] != null && (bool)linkDetails["linked"];
                var discordOxideGroup = linkDetails["discord_oxide_group"]?.ToString();
                if (string.IsNullOrWhiteSpace(discordOxideGroup))
                    discordOxideGroup = LocalVerifyOxideGroup;

                if (linked)
                {
                    EnsureGroup(discordOxideGroup);
                    if (!permission.UserHasGroup(steamId, discordOxideGroup))
                    {
                        permission.AddUserGroup(steamId, discordOxideGroup);
                        AppendLinkLog(
                            steamId,
                            steamName,
                            JsonString(linkDetails, "discord_id", "discordId"),
                            JsonString(linkDetails, "discord_username", "discord_name", "discordUsername", "discordName"),
                            "linked");
                        if (trackRecheckStats) _recheckVerifiedAdded++;
                        if (notify) notifyPlayer.ChatMessage(Lang("ConfirmLink"));
                        Puts(steamId + " has linked with the discord adding them to the " + discordOxideGroup + " group.");
                    }
                    else if (notify)
                    {
                        notifyPlayer.ChatMessage(Lang("AlreadyLinked"));
                    }
                }
                else
                {
                    if (notify)
                        notifyPlayer.ChatMessage(Lang("ErrorLink"));

                    if (HasActiveLocalLinkForGroup(steamId, discordOxideGroup))
                    {
                        Puts(steamId + " is locally verified; keeping " + discordOxideGroup + " group.");
                    }
                    else if (permission.UserHasGroup(steamId, discordOxideGroup))
                    {
                        permission.RemoveUserGroup(steamId, discordOxideGroup);
                        AppendLinkLog(
                            steamId,
                            steamName,
                            JsonString(linkDetails, "discord_id", "discordId"),
                            JsonString(linkDetails, "discord_username", "discord_name", "discordUsername", "discordName"),
                            "unlinked");
                        if (trackRecheckStats) _recheckVerifiedRemoved++;
                        Puts(steamId + " has stop linking with the discord removing them from the " + discordOxideGroup + " group.");
                    }
                }
            }

            if (Convert.ToBoolean(ConfigGet("EnableNitro")) == true)
            {
                bool nitro = linkDetails["nitro"] != null && (bool)linkDetails["nitro"];
                var nitroOxideGroup = linkDetails["nitro_oxide_group"]?.ToString();
                if (string.IsNullOrWhiteSpace(nitroOxideGroup))
                    nitroOxideGroup = "nitro";

                if (nitro)
                {
                    EnsureGroup(nitroOxideGroup);
                    if (!permission.UserHasGroup(steamId, nitroOxideGroup))
                    {
                        permission.AddUserGroup(steamId, nitroOxideGroup);
                        if (trackRecheckStats) _recheckNitroAdded++;
                        if (notify) notifyPlayer.ChatMessage(Lang("ConfirmNitro"));
                        Puts(steamId + " has boosted the discord adding them to the " + nitroOxideGroup + " group.");
                    }
                }
                else if (permission.UserHasGroup(steamId, nitroOxideGroup))
                {
                    permission.RemoveUserGroup(steamId, nitroOxideGroup);
                    if (trackRecheckStats) _recheckNitroRemoved++;
                    Puts(steamId + " has stop boosting the discord removing them from the " + nitroOxideGroup + " group.");
                }
            }
        }

        private void GetCallback(int code, string response, BasePlayer player, bool inGameCommand = false, int requestId = 0)
        {
            if (inGameCommand && !CompleteLinkRequest(player, requestId))
            {
                Puts("PlatformSync ignoring stale or already-timed-out callback for " + (player != null ? player.UserIDString : "?") + " request " + requestId + ".");
                return;
            }
            Puts("PlatformSync validate callback for " + (player != null ? player.UserIDString : "?") + ": code=" + code + ", responseLength=" + (response != null ? response.Length.ToString() : "null"));
            if (code != 200 || string.IsNullOrWhiteSpace(response))
            {
                Puts("PlatformSync API request failed for " + (player != null ? player.UserIDString : "?") + " (code " + code + ").");
                if (inGameCommand && player != null)
                {
                    player.ChatMessage(code == 0 ? Lang("ApiTimeout") : Lang("ApiError"));
                }
                return;
            }

            JObject linkDetails;
            try { linkDetails = JObject.Parse(response); }
            catch
            {
                Puts("PlatformSync API returned invalid response for " + (player != null ? player.UserIDString : "?"));
                if (inGameCommand && player != null)
                {
                    player.ChatMessage(Lang("ApiError"));
                }
                return;
            }

            if (player == null) return;
            ApplyValidateResult(
                player.UserIDString,
                player.displayName ?? "",
                linkDetails,
                trackRecheckStats: false,
                notifyPlayer: inGameCommand ? player : null);
        }
        #endregion

        #region LangFile
        private void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CheckingLink"] = "Checking your Discord link status...",
                ["ConfirmLink"] = "You have succsfully linked your Steam Account with your Discord Account.",
                ["AlreadyLinked"] = "Your Steam Account is already linked with your Discord Account.",
                ["ErrorLink"] = "There was an error connecting your Steam Account with your Discord Account please contact support.",
                ["ApiError"] = "We couldn't verify your Discord link right now. Please try again in a moment or contact support.",
                ["ApiTimeout"] = "The Discord link check timed out. Please try again in a few seconds.",
                ["ConfirmNitro"] = "You have successfully boosted our discord server and the perks have been added to your account!",
                ["NoPermission"] = "You do not have permission to use this command.",
            }, this);
        }

        private string Lang(string key, string id = null, params object[] args) =>
            string.Format(lang.GetMessage(key, this, id), args);

        #endregion LangFile
    }
}
