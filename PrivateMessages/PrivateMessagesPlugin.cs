using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace PrivateMessagesHarmony
{
    public sealed class PrivateMessagesPlugin
    {
        public const string PermAllow = "privatemessages.allow";
        public const string PermBlock = "privatemessages.block";
        private const string AdminGroup = "admin";

        private readonly string _serverRoot;
        private readonly string _configPath;
        private readonly LangStore _lang = new LangStore();
        private ConfigData _config;

        private readonly Dictionary<string, string> _pmHistory = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _cooldown = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<MessageHistory> _lastFivePms = new List<MessageHistory>();

        public ConfigData Config => _config;

        public class MessageHistory
        {
            public string Sender;
            public string Target;
            public List<string> Messages = new List<string>();
        }

        public class ConfigData
        {
            public bool UseUFilter;
            public bool UseIgnore;
            public bool UseCooldown = true;
            public bool CallCanChat = true;
            public bool CallOnPMProcessed = true;
            public bool UseBetterChatMute = true;
            public bool EnableLogging = true;
            public bool EnableHistory;
            public bool UsePermission;
            public bool ToastInfo = true;
            public int CooldownTime = 1;
            public string PmCommand = "pm";
        }

        public PrivateMessagesPlugin(string serverRoot)
        {
            _serverRoot = serverRoot;
            _configPath = Path.Combine(serverRoot, "HarmonyConfig", "PrivateMessages.json");
        }

        public void LoadConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(_configPath))
                    _config = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(_configPath));

                if (_config == null)
                {
                    Debug.LogWarning("[PrivateMessages] Creating new configuration file.");
                    _config = new ConfigData();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PrivateMessages] FAIL: load config — using defaults. " + ex.Message);
                _config = new ConfigData();
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
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(_config ?? new ConfigData(), Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PrivateMessages] FAIL: save config: " + ex.Message);
            }
        }

        public void LoadDefaultMessages()
        {
            _lang.RegisterMessages(new Dictionary<string, string>
            {
                ["PMTo"] = "[#00FFFF]PM to {0}[/#]: {1}",
                ["PMFrom"] = "[#00FFFF]PM from {0}[/#]: {1}",
                ["PlayerNotOnline"] = "{0} was not found.",
                ["NotOnlineAnymore"] = "The last person you were talking to is not online anymore.",
                ["NotMessaged"] = "You haven't messaged anyone or they haven't messaged you.",
                ["IgnoreYou"] = "[#FF0000]{0} is ignoring you and cannot receive your PM[/#]",
                ["SyntaxReply"] = "Incorrect Syntax use: /{0} <msg>",
                ["HistorySyntax"] = "Incorrect Syntax use: /{0} <name>",
                ["SyntaxPM"] = "Incorrect Syntax use: /{0} <name> <msg>",
                ["NotAllowedToChat"] = "You are not allowed to chat here",
                ["History"] = "Your History:\n{0}",
                ["CooldownMessage"] = "You will be able to send a private message in {0} seconds",
                ["NoHistory"] = "This player has no saved history",
                ["CannotFindUser"] = "This player cannot be found",
                ["CommandDisabled"] = "This command has been disabled",
                ["IsMuted"] = "You are currently muted & cannot send private messages",
                ["TargetMuted"] = "This person is muted & cannot receive your private message",
                ["NoPermission"] = "You don't have the correct permissions to run this command",
                ["HistoryPM"] = "[#00FFFF]{0}[/#]: {1}",
                ["Logging"] = "[PM] {0}->{1}: {2}",
                ["Blocked"] = "This user cannot be messaged."
            }, "en");
            _lang.LoadHarmonyLanguageOverrides(_serverRoot, "PrivateMessages");
        }

        public void RegisterPermissions()
        {
            PermissionsBridge.RegisterPermission(PermAllow);
            PermissionsBridge.RegisterPermission(PermBlock);
            if (!PermissionsBridge.IsAvailable) return;
            if (!PermissionsBridge.GroupExists(AdminGroup))
                PermissionsBridge.CreateGroup(AdminGroup, "Administrators", 0);
            PermissionsBridge.GrantGroupPermission(AdminGroup, PermAllow);
        }

        public void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;
            _pmHistory.Remove(player.UserIDString);
        }

        public void CommandPrivateMessage(BasePlayer sender, string command, string[] args)
        {
            if (sender == null) return;
            if (_config.UsePermission && !PermissionsBridge.UserHasPermission(sender.UserIDString, PermAllow))
            {
                Toast(sender, GameTip.Styles.Red_Normal, "NoPermission");
                return;
            }

            if (args == null || args.Length == 0)
            {
                Toast(sender, GameTip.Styles.Error, "SyntaxPM", command);
                return;
            }

            string name = args[0];
            BasePlayer target = FindPlayer(name);
            if (target == null)
            {
                Toast(sender, GameTip.Styles.Red_Normal, "PlayerNotOnline", name);
                return;
            }

            if (target.userID == sender.userID)
            {
                string self = FilterMessage(args);
                Message(target, "PMFrom", sender.displayName, self);
                if (target.IsAdmin)
                    AddHistoryAndLogging(sender, target, self);
                return;
            }

            // CallCanChat / CallOnPMProcessed Oxide hooks are no-ops under Harmony.

            if (CheckMuteStatus(sender, target))
                return;
            if (HasCooldown(sender) || IsIgnored(sender, target))
                return;

            if (PermissionsBridge.UserHasPermission(target.UserIDString, PermBlock))
            {
                Toast(sender, GameTip.Styles.Error, "Blocked", target.displayName);
                return;
            }

            string message = FilterMessage(args);
            AddHistoryAndLogging(sender, target, message);
            AddPmHistory(sender.UserIDString, target.UserIDString);

            Message(sender, "PMTo", target.displayName, message);
            if (!IsShadowMuted(sender, target))
                Message(target, "PMFrom", sender.displayName, message);
        }

        public void CommandReply(BasePlayer sender, string command, string[] args)
        {
            if (sender == null) return;
            if (_config.UsePermission && !PermissionsBridge.UserHasPermission(sender.UserIDString, PermAllow))
            {
                Toast(sender, GameTip.Styles.Red_Normal, "NoPermission");
                return;
            }

            if (args == null || args.Length == 0)
            {
                Toast(sender, GameTip.Styles.Error, "SyntaxReply", command);
                return;
            }

            if (!_pmHistory.TryGetValue(sender.UserIDString, out string steamid))
            {
                Toast(sender, GameTip.Styles.Blue_Short, "NotMessaged");
                return;
            }

            BasePlayer target = FindPlayer(steamid);
            if (target == null)
            {
                Toast(sender, GameTip.Styles.Blue_Short, "NotOnlineAnymore");
                return;
            }

            if (CheckMuteStatus(sender, target))
                return;
            if (HasCooldown(sender) || IsIgnored(sender, target))
                return;

            string message = FilterMessage(args, true);
            if (string.IsNullOrEmpty(message))
                return;

            AddHistoryAndLogging(sender, target, message);
            AddPmHistory(sender.UserIDString, target.UserIDString);

            Message(sender, "PMTo", target.displayName, message);
            if (!IsShadowMuted(sender, target))
                Message(target, "PMFrom", sender.displayName, message);
        }

        public void CommandHistory(BasePlayer sender, string command, string[] args)
        {
            if (sender == null) return;
            if (!_config.EnableHistory)
            {
                Toast(sender, GameTip.Styles.Error, "CommandDisabled");
                return;
            }

            if (args == null || args.Length == 0)
            {
                Toast(sender, GameTip.Styles.Error, "HistorySyntax", command);
                return;
            }

            BasePlayer target = FindPlayer(args[0]);
            if (target == null)
            {
                Toast(sender, GameTip.Styles.Error, "CannotFindUser");
                return;
            }

            var history = GetLastFivePms(sender.UserIDString, target.UserIDString);
            if (history == null)
            {
                Toast(sender, GameTip.Styles.Blue_Short, "NoHistory");
                return;
            }

            Message(sender, "History", string.Join(Environment.NewLine, history.Messages.ToArray()));
        }

        private void AddHistoryAndLogging(BasePlayer player, BasePlayer target, string message)
        {
            if (_config.EnableHistory)
                AddToHistory(player.UserIDString, target.UserIDString, Lang("HistoryPM", player.displayName, message));
            if (_config.EnableLogging)
                Debug.Log("[PrivateMessages] " + Lang("Logging", player.displayName, target.displayName, message));
        }

        private void AddPmHistory(string initiatorId, string targetId)
        {
            _pmHistory[initiatorId] = targetId;
            _pmHistory[targetId] = initiatorId;
        }

        private static bool IsShadowMuted(BasePlayer sender, BasePlayer target)
        {
            // GTFO plugin does not exist in this Harmony stack.
            return false;
        }

        private bool CheckMuteStatus(BasePlayer sender, BasePlayer target)
        {
            if (sender.HasPlayerFlag(BasePlayer.PlayerFlags.ChatMute))
            {
                Toast(sender, GameTip.Styles.Error, "IsMuted");
                return true;
            }

            if (!sender.IsAdmin && target.HasPlayerFlag(BasePlayer.PlayerFlags.ChatMute))
            {
                Toast(sender, GameTip.Styles.Error, "TargetMuted");
                return true;
            }

            if (_config.UseBetterChatMute)
            {
                if (!sender.IsAdmin && IsBetterChatMuted(sender))
                {
                    Toast(sender, GameTip.Styles.Error, "IsMuted");
                    return true;
                }
                if (IsBetterChatMuted(target))
                {
                    Toast(sender, GameTip.Styles.Error, "TargetMuted");
                    return true;
                }
            }

            return false;
        }

        private static bool IsBetterChatMuted(BasePlayer player)
        {
            if (player == null) return false;
            try
            {
                var apiType = AppDomain.CurrentDomain.GetData("BetterChatMute_ApiType") as Type
                              ?? AppDomain.CurrentDomain.GetData("BetterChat_ApiType") as Type;
                if (apiType == null) return false;
                MethodInfo mi = apiType.GetMethod("API_IsMuted", BindingFlags.Public | BindingFlags.Static)
                                ?? apiType.GetMethod("IsMuted", BindingFlags.Public | BindingFlags.Static);
                if (mi == null) return false;
                object result = mi.Invoke(null, new object[] { player.UserIDString });
                return result is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        private static BasePlayer FindPlayer(string nameOrId)
        {
            if (string.IsNullOrEmpty(nameOrId)) return null;
            foreach (BasePlayer target in BasePlayer.activePlayerList)
            {
                if (target == null || !target.IsConnected) continue;
                if (string.Equals(target.UserIDString, nameOrId, StringComparison.Ordinal))
                    return target;
                if (string.Equals(target.displayName, nameOrId, StringComparison.OrdinalIgnoreCase))
                    return target;
            }
            foreach (BasePlayer target in BasePlayer.activePlayerList)
            {
                if (target == null || !target.IsConnected) continue;
                if (target.displayName != null && target.displayName.IndexOf(nameOrId, StringComparison.OrdinalIgnoreCase) >= 0)
                    return target;
            }
            return null;
        }

        private void AddToHistory(string sender, string target, string message)
        {
            var history = GetLastFivePms(sender, target);
            if (history == null)
            {
                _lastFivePms.Add(new MessageHistory
                {
                    Sender = sender,
                    Target = target,
                    Messages = new List<string> { message }
                });
            }
            else
            {
                history.Messages.Add(message);
                if (history.Messages.Count > 5)
                    history.Messages.RemoveAt(0);
            }
        }

        private MessageHistory GetLastFivePms(string sender, string target)
        {
            for (int i = 0; i < _lastFivePms.Count; i++)
            {
                var h = _lastFivePms[i];
                if (h == null) continue;
                if ((h.Sender == sender && h.Target == target) || (h.Sender == target && h.Target == sender))
                    return h;
            }
            return null;
        }

        private bool IsIgnored(BasePlayer sender, BasePlayer target)
        {
            // Ignore plugin does not exist in this Harmony stack.
            return false;
        }

        private static string FilterMessage(string[] args, bool isReply = false)
        {
            var sb = new StringBuilder();
            int start = isReply ? 0 : 1;
            bool insideTag = false;
            for (int i = start; i < args.Length; i++)
            {
                if (i > start)
                    sb.Append(' ');
                string arg = args[i];
                for (int c = 0; c < arg.Length; c++)
                {
                    char ch = arg[c];
                    if (ch == '<')
                    {
                        insideTag = true;
                        continue;
                    }
                    if (ch == '>')
                    {
                        insideTag = false;
                        continue;
                    }
                    if (!insideTag)
                        sb.Append(ch);
                }
            }
            // UFilter plugin does not exist — return stripped text.
            return sb.ToString();
        }

        private bool HasCooldown(BasePlayer sender)
        {
            if (!_config.UseCooldown) return false;
            int now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (_cooldown.TryGetValue(sender.UserIDString, out int time))
            {
                if (time > now)
                {
                    Toast(sender, GameTip.Styles.Blue_Short, "CooldownMessage", time - now);
                    return true;
                }
                _cooldown.Remove(sender.UserIDString);
            }
            _cooldown[sender.UserIDString] = now + _config.CooldownTime;
            return false;
        }

        private string Lang(string key, params object[] args)
        {
            string raw = _lang.GetMessage(key);
            if (args == null || args.Length == 0) return raw;
            try { return string.Format(raw, args); }
            catch { return raw; }
        }

        private void Message(BasePlayer user, string key, params object[] args)
        {
            string message = Lang(key, args);
            if (string.IsNullOrEmpty(message) || user == null) return;
            user.ChatMessage(message);
        }

        private void Toast(BasePlayer user, GameTip.Styles style, string key, params object[] args)
        {
            string message = Lang(key, args);
            if (string.IsNullOrEmpty(message) || user == null) return;
            if (!_config.ToastInfo)
            {
                user.ChatMessage(message);
                return;
            }
            try
            {
                user.SendConsoleCommand("gametip.showtoast_translated", (int)style, "privatemessages." + key, message, false, Array.Empty<string>());
            }
            catch
            {
                user.ChatMessage(message);
            }
        }
    }

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
                Debug.Log($"[PrivateMessages] Loaded {map.Count} language strings from HarmonyLanguage/{modName}.json");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PrivateMessages] HarmonyLanguage load failed: " + ex.Message);
            }
        }

        public string GetMessage(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            return _en.TryGetValue(key, out string msg) && !string.IsNullOrEmpty(msg) ? msg : key;
        }
    }
}
