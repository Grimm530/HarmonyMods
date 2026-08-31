using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ConVar;
using CompanionServer;
using Facepunch;
using HarmonyChat;
using Network;
using UnityEngine;
using Pool = Facepunch.Pool;

namespace BetterChatHarmony
{
    public class BetterChatMod : IHarmonyModHooks
    {
        public static BetterChatMod Instance { get; private set; }

        public const string AppDomainApiKey = "BetterChat_ApiType";
        public const string AppDomainSkipTranslatorKey = "BetterChat_SkipTranslator";
        public const string AppDomainReadyCallbacksKey = "BetterChat_ReadyCallbacks";
        public const string PermAdmin = "betterchat.admin";

        public static readonly ChatGroup RustDeveloperGroup = new ChatGroup("rust_developer")
        {
            Priority = 100,
            Title = { Text = "[Rust Developer]", Color = "#ffaa55" }
        };

        private static readonly ChatGroup FallbackGroup = new ChatGroup("default");

        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();
        private readonly List<Func<Dictionary<string, object>, object>> _chatModifiers =
            new List<Func<Dictionary<string, object>, object>>();
        private readonly Dictionary<string, Func<BasePlayer, string>> _thirdPartyTitles =
            new Dictionary<string, Func<BasePlayer, string>>(StringComparer.OrdinalIgnoreCase);
        private readonly StringBuilder _helpSb = new StringBuilder();
        private Action _permissionsReady;
        private Action<string> _membershipChanged;
        private GameObject _runnerGo;

        public ColouredChatService Colours { get; private set; }
        public List<ChatGroup> Groups => BetterChatConfig.Config?.Groups;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            BetterChatConfig.Load(root);
            Colours = new ColouredChatService(root);

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(BetterChatMod)); } catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainSkipTranslatorKey, true); } catch { }

            _permissionsReady = RegisterPermissions;
            _membershipChanged = OnPermissionsMembershipChanged;
            PermissionsBridge.RegisterReadyCallback(_permissionsReady);
            PermissionsBridge.RegisterMembershipChangedCallback(_membershipChanged);
            EnsureChatGroupsInPermissions();
            RegisterConsoleCommands();
            ChatSayBridge.Register("BetterChat", OnChatCommand);
            InvokeReadyCallbacks();
            StartPeriodicSave();

            Debug.Log("[BetterChat] Loaded. Groups/titles + coloured names/messages. Config: HarmonyConfig/BetterChat.json");
            Debug.Log("[BetterChat] Chat: /chat  /colour  /colours  /mcolour  /mcolours   Console: betterchat");
            LogConfigSummary();
            LogPermissionsLink();
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            ChatSayBridge.Unregister("BetterChat");
            PermissionsBridge.UnregisterReadyCallback(_permissionsReady);
            PermissionsBridge.UnregisterMembershipChangedCallback(_membershipChanged);
            UnregisterConsoleCommands();
            Colours?.ClearUpData();
            Colours?.Save();
            // Do not BetterChatConfig.Save() here: in-memory groups are stale on harmony.load
            // and would overwrite HarmonyConfig/BetterChat.json (e.g. wipe 'verified').
            if (_runnerGo != null)
            {
                UnityEngine.Object.Destroy(_runnerGo);
                _runnerGo = null;
            }
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); } catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainSkipTranslatorKey, null); } catch { }
            _chatModifiers.Clear();
            _thirdPartyTitles.Clear();
            Instance = null;
        }

        private void StartPeriodicSave()
        {
            try
            {
                _runnerGo = new GameObject("BetterChat_Saver");
                UnityEngine.Object.DontDestroyOnLoad(_runnerGo);
                _runnerGo.hideFlags = HideFlags.HideAndDontSave;
                _runnerGo.AddComponent<SaveBehaviour>().Init(this);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterChat] Periodic save: " + ex.Message);
            }
        }

        private class SaveBehaviour : MonoBehaviour
        {
            private BetterChatMod _mod;
            public void Init(BetterChatMod mod)
            {
                _mod = mod;
                InvokeRepeating(nameof(Tick), 300f, 300f);
            }
            private void Tick()
            {
                try
                {
                    _mod?.Colours?.ClearUpData();
                    _mod?.Colours?.Save();
                    BetterChatConfig.Save();
                }
                catch { }
            }
        }

        private void RegisterPermissions()
        {
            PermissionsBridge.RegisterPermission(PermAdmin);
            var c = BetterChatConfig.Config?.Coloured;
            if (c == null) return;
            PermissionsBridge.RegisterPermission(c.NamePermShow);
            PermissionsBridge.RegisterPermission(c.NamePermUse);
            PermissionsBridge.RegisterPermission(c.NamePermGradient);
            PermissionsBridge.RegisterPermission(c.NamePermRainbow);
            PermissionsBridge.RegisterPermission(c.NamePermBypass);
            PermissionsBridge.RegisterPermission(c.NamePermSetOthers);
            PermissionsBridge.RegisterPermission(c.NamePermRandomColour);
            PermissionsBridge.RegisterPermission(c.MessagePermShow);
            PermissionsBridge.RegisterPermission(c.MessagePermUse);
            PermissionsBridge.RegisterPermission(c.MessagePermGradient);
            PermissionsBridge.RegisterPermission(c.MessagePermRainbow);
            PermissionsBridge.RegisterPermission(c.MessagePermBypass);
            PermissionsBridge.RegisterPermission(c.MessagePermSetOthers);
            PermissionsBridge.RegisterPermission(c.MessagePermRandomColour);
            EnsureChatGroupsInPermissions();
            LogPermissionsLink();
        }

        /// <summary>Called by 0Permissions when a player is added/removed from a group.</summary>
        public static void OnPermissionsMembershipChanged(string playerId)
        {
            var mod = Instance;
            if (mod == null) return;
            if (string.IsNullOrEmpty(playerId))
                mod.Colours?.ClearCache();
            else
                mod.Colours?.ClearCache(playerId);
        }

        private static void LogPermissionsLink()
        {
            string[] permGroups = PermissionsBridge.GetAllGroupNames();
            Debug.Log("[BetterChat] 0Permissions " + PermissionsBridge.DescribeBind() +
                      " permGroups=[" + string.Join(", ", permGroups) + "]");
        }

        private static void LogConfigSummary()
        {
            var groups = Instance?.Groups;
            if (groups == null || groups.Count == 0)
            {
                Debug.LogWarning("[BetterChat] HarmonyConfig/BetterChat.json has no Groups.");
                return;
            }
            var sb = new StringBuilder();
            sb.Append("[BetterChat] Using HarmonyConfig/BetterChat.json (").Append(groups.Count).Append(" groups): ");
            for (int i = 0; i < groups.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var g = groups[i];
                if (g == null) continue;
                sb.Append(g.GroupName);
                if (g.Title != null && !g.Title.Hidden && !string.IsNullOrWhiteSpace(g.Title.Text))
                {
                    sb.Append('=').Append(g.Title.Text);
                    if (g.Title.AttachToUsername) sb.Append("(name)");
                }
            }
            Debug.Log(sb.ToString());
        }

        private void EnsureChatGroupsInPermissions()
        {
            var groups = Groups;
            if (groups == null) return;
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                if (g == null || string.IsNullOrEmpty(g.GroupName)) continue;
                if (!PermissionsBridge.GroupExists(g.GroupName))
                    PermissionsBridge.CreateGroup(g.GroupName, g.Title?.Text ?? g.GroupName, g.Priority);
            }
        }

        public static void RegisterReadyCallback(Action callback)
        {
            if (callback == null) return;
            var list = GetOrCreateReadyCallbacks();
            lock (list)
            {
                if (!list.Contains(callback))
                    list.Add(callback);
            }
            if (Instance != null)
            {
                try { callback(); } catch (Exception ex) { Debug.LogWarning("[BetterChat] Ready callback: " + ex.Message); }
            }
        }

        public static void UnregisterReadyCallback(Action callback)
        {
            if (callback == null) return;
            try
            {
                if (AppDomain.CurrentDomain.GetData(AppDomainReadyCallbacksKey) is List<Action> list)
                {
                    lock (list) list.Remove(callback);
                }
            }
            catch { }
        }

        public static void RegisterOnBetterChat(Func<Dictionary<string, object>, object> modifier)
        {
            if (modifier == null || Instance == null) return;
            if (!Instance._chatModifiers.Contains(modifier))
                Instance._chatModifiers.Add(modifier);
        }

        public static void UnregisterOnBetterChat(Func<Dictionary<string, object>, object> modifier)
        {
            Instance?._chatModifiers.Remove(modifier);
        }

        public static void RegisterThirdPartyTitle(string pluginName, Func<BasePlayer, string> titleGetter)
        {
            if (Instance == null || string.IsNullOrEmpty(pluginName) || titleGetter == null) return;
            Instance._thirdPartyTitles[pluginName] = titleGetter;
        }

        public static void UnregisterThirdPartyTitle(string pluginName)
        {
            if (Instance == null || string.IsNullOrEmpty(pluginName)) return;
            Instance._thirdPartyTitles.Remove(pluginName);
        }

        private static List<Action> GetOrCreateReadyCallbacks()
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData(AppDomainReadyCallbacksKey) is List<Action> existing)
                    return existing;
            }
            catch { }
            var created = new List<Action>();
            try { AppDomain.CurrentDomain.SetData(AppDomainReadyCallbacksKey, created); } catch { }
            return created;
        }

        private static void InvokeReadyCallbacks()
        {
            try
            {
                if (!(AppDomain.CurrentDomain.GetData(AppDomainReadyCallbacksKey) is List<Action> list) || list.Count == 0)
                    return;
                List<Action> snapshot;
                lock (list) snapshot = new List<Action>(list);
                foreach (var cb in snapshot)
                {
                    try { cb(); }
                    catch (Exception ex) { Debug.LogWarning("[BetterChat] Ready callback failed: " + ex.Message); }
                }
            }
            catch { }
        }

        #region Chat pipeline

        public bool HandleSayImpl(Chat.ChatChannel channel, ConsoleSystem.Arg arg)
        {
            if (!Chat.enabled)
            {
                arg?.ReplyWith("Chat is disabled.");
                return false;
            }

            var player = arg?.Player() ?? arg?.Connection?.player as BasePlayer;
            if (player == null || !player.IsValid()) return true;
            if (Chat.hideChatInTutorial && player.IsInTutorial) return false;
            if (player.HasPlayerFlag(BasePlayer.PlayerFlags.ChatMute)) return false;

            var raw = arg.GetString(0, "text");
            if (string.IsNullOrEmpty(raw)) return false;

            if (OnChatCommand(player, raw))
                return false;

            var trimmed = raw.TrimStart();
            if (trimmed.Length > 0 && (trimmed[0] == '/' || trimmed[0] == '\\'))
                return true;

            if (!player.IsAdmin && !player.IsDeveloper)
            {
                if (player.NextChatTime == 0f)
                    player.NextChatTime = UnityEngine.Time.realtimeSinceStartup - 30f;
                if (player.NextChatTime > UnityEngine.Time.realtimeSinceStartup)
                {
                    player.NextChatTime += 2f;
                    float remaining = player.NextChatTime - UnityEngine.Time.realtimeSinceStartup;
                    ConsoleNetwork.SendClientCommand(player.net.connection, "chat.add", 2, 0,
                        "You're chatting too fast - try again in " + (remaining + 0.5f).ToString("0") + " seconds");
                    if (remaining > 120f)
                        player.Kick("Chatting too fast");
                    return false;
                }
            }

            var cfg = BetterChatConfig.Config;
            int maxLen = cfg != null ? cfg.MaxMessageLength : 128;
            var message = raw.Replace("\n", "").Replace("\r", "").Trim();
            if (message.Length > maxLen) message = message.Substring(0, maxLen);
            if (message.Length <= 0) return false;

            message = ChatFormatter.StripRichText(message.EscapeRichText());

            var chatMessage = PrepareMessage(player, message);
            if (chatMessage == null) return true;

            var dict = chatMessage.ToDictionary();
            dict["ChatChannel"] = channel;
            var hookResult = InvokeModifiers(dict);
            if (hookResult is bool)
                return false;
            if (hookResult is Dictionary<string, object> modified)
            {
                try { chatMessage = BetterChatMessage.FromDictionary(modified); }
                catch (Exception ex)
                {
                    Debug.LogWarning("[BetterChat] OnBetterChat modifier produced invalid data: " + ex.Message);
                }
            }

            if (chatMessage.CancelOption != BetterChatMessage.CancelOptions.None)
                return false;

            SendFormatted(chatMessage, channel);
            Debug.Log("[BetterChat] " + player.displayName + " id=" + player.UserIDString +
                      " permGroups=[" + string.Join(",", PermissionsBridge.GetUserGroups(player.UserIDString)) + "]" +
                      " primary=" + chatMessage.PrimaryGroup +
                      " titles=" + (chatMessage.Titles == null ? "0" : chatMessage.Titles.Count.ToString()) +
                      " namePrefix=" + (string.IsNullOrEmpty(chatMessage.NamePrefix) ? "(none)" : chatMessage.NamePrefix));
            player.NextChatTime = UnityEngine.Time.realtimeSinceStartup + 1.5f;
            try { Facepunch.Rust.Analytics.Azure.OnChatMessage(player, message, (int)channel); } catch { }
            return false;
        }

        public BetterChatMessage PrepareMessage(BasePlayer player, string message)
        {
            var primary = GetUserPrimaryGroup(player);
            var userGroups = GetUserGroups(player);
            if (primary == null)
            {
                Debug.LogWarning("[BetterChat] " + player.displayName + " (" + player.UserIDString +
                                 ") has no BetterChat group — using internal default.");
                primary = FallbackGroup;
                userGroups.Add(primary);
            }

            userGroups.Sort((a, b) => a.Priority.CompareTo(b.Priority));

            var cfg = BetterChatConfig.Config;
            int maxTitles = cfg != null ? cfg.MaxTitles : 3;
            bool reverse = cfg != null && cfg.ReverseTitleOrder;

            var titles = new List<string>();
            var namePrefix = new StringBuilder();
            for (int i = 0; i < userGroups.Count; i++)
            {
                var g = userGroups[i];
                if (g?.Title == null) continue;
                if (g.Title.Hidden) continue;
                if (g.Title.HiddenIfNotPrimary && primary != g) continue;
                if (string.IsNullOrWhiteSpace(g.Title.Text)) continue;
                if (g.Title.AttachToUsername)
                {
                    namePrefix.Append(ChatFormatter.FormatTitle(g.Title));
                    continue;
                }
                titles.Add(ChatFormatter.FormatTitle(g.Title));
            }

            if (titles.Count > maxTitles)
                titles.RemoveRange(maxTitles, titles.Count - maxTitles);
            if (reverse) titles.Reverse();

            foreach (var kvp in _thirdPartyTitles)
            {
                try
                {
                    string title = kvp.Value(player);
                    if (!string.IsNullOrEmpty(title))
                        titles.Add(title);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[BetterChat] Third-party title '" + kvp.Key + "': " + ex.Message);
                }
            }

            var chatMessage = new BetterChatMessage
            {
                Player = player,
                Username = ChatFormatter.StripRichText(player.displayName.EscapeRichText()),
                Message = message,
                Titles = titles,
                NamePrefix = namePrefix.ToString(),
                PrimaryGroup = primary.GroupName,
                UsernameSettings = CloneUsername(primary.Username),
                MessageSettings = CloneMessage(primary.Message),
                FormatSettings = primary.Format ?? new ChatGroup.FormatSettings()
            };

            Colours?.ApplyToMessage(chatMessage);
            return chatMessage;
        }

        private object InvokeModifiers(Dictionary<string, object> dict)
        {
            for (int i = 0; i < _chatModifiers.Count; i++)
            {
                try
                {
                    var result = _chatModifiers[i](dict);
                    if (result is Dictionary<string, object> d)
                        dict = d;
                    else if (result != null)
                        return result;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[BetterChat] OnBetterChat modifier: " + ex.Message);
                }
            }
            return dict;
        }

        public ChatGroup FindGroup(string name)
        {
            var groups = Groups;
            if (groups == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] != null && string.Equals(groups[i].GroupName, name, StringComparison.OrdinalIgnoreCase))
                    return groups[i];
            }
            return null;
        }

        public List<ChatGroup> GetUserGroups(BasePlayer player)
        {
            var result = new List<ChatGroup>();
            if (player == null) return result;
            string id = player.UserIDString;
            var names = PermissionsBridge.GetUserGroups(id);
            if (names.Length == 0)
            {
                ulong uid = player.userID;
                string alt = uid.ToString();
                if (!string.IsNullOrEmpty(alt) && !string.Equals(alt, id, StringComparison.Ordinal))
                    names = PermissionsBridge.GetUserGroups(alt);
            }
            var groups = Groups;
            if (groups != null)
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    var g = groups[i];
                    if (g == null || string.IsNullOrEmpty(g.GroupName)) continue;
                    bool inGroup = false;
                    for (int n = 0; n < names.Length; n++)
                    {
                        if (string.Equals(g.GroupName, names[n], StringComparison.OrdinalIgnoreCase))
                        {
                            inGroup = true;
                            break;
                        }
                    }
                    if (!inGroup)
                        inGroup = PermissionsBridge.UserHasGroup(player.UserIDString, g.GroupName);
                    if (inGroup)
                        result.Add(g);
                }
            }

            if (player.IsValid() && DeveloperList.Contains(player.userID))
                result.Add(RustDeveloperGroup);

            return result;
        }

        public ChatGroup GetUserPrimaryGroup(BasePlayer player)
        {
            var groups = GetUserGroups(player);
            ChatGroup primary = null;
            for (int i = 0; i < groups.Count; i++)
            {
                if (primary == null || groups[i].Priority < primary.Priority)
                    primary = groups[i];
            }
            return primary;
        }

        private static ChatGroup.UsernameSettings CloneUsername(ChatGroup.UsernameSettings src)
        {
            if (src == null) return new ChatGroup.UsernameSettings();
            return new ChatGroup.UsernameSettings { Color = src.Color, Size = src.Size };
        }

        private static ChatGroup.MessageSettings CloneMessage(ChatGroup.MessageSettings src)
        {
            if (src == null) return new ChatGroup.MessageSettings();
            return new ChatGroup.MessageSettings { Color = src.Color, Size = src.Size };
        }

        public void SendFormatted(BetterChatMessage chatMessage, Chat.ChatChannel channel)
        {
            var player = chatMessage.Player;
            if (player == null) return;
            var output = chatMessage.GetOutput();
            ulong userId = player.userID;
            string userIdString = player.UserIDString;
            int ch = (int)channel;
            string name = (output.Username ?? player.displayName).EscapeRichText();
            string msg = output.Message ?? chatMessage.Message;
            string color = output.Color ?? "#55aaff";
            // chat.add2 name is overwritten by the client when steamid matches a connected
            // player. chat.add puts the whole formatted line in the message body (Oxide BetterChat).
            string line = output.Chat;
            if (string.IsNullOrEmpty(line))
                line = name + ": " + msg;

            switch (channel)
            {
                case Chat.ChatChannel.Team:
                {
                    var team = RelationshipManager.ServerInstance?.FindPlayersTeam(userId) ?? player.Team;
                    if (team == null) return;
                    team.BroadcastTeamChat(userId, name, msg, color);
                    var connections = team.GetOnlineMemberConnections();
                    if (connections != null)
                        ConsoleNetwork.SendClientCommand(connections, "chat.add", ch, userId, line);
                    break;
                }
                case Chat.ChatChannel.Cards:
                {
                    if (!player.isMounted) return;
                    var table = player.GetMountedVehicle() as BaseCardGameEntity;
                    if (table == null || !(table.GameController?.IsAtTable(player) ?? false)) return;
                    var list = Pool.Get<List<Network.Connection>>();
                    table.GameController?.GetConnectionsInGame(list);
                    if (list.Count > 0)
                        ConsoleNetwork.SendClientCommand(list, "chat.add", ch, userId, line);
                    Pool.FreeUnmanaged(ref list);
                    break;
                }
                case Chat.ChatChannel.Clan:
                {
                    if (player.clanId == 0 || ClanManager.ServerInstance == null) return;
                    if (ClanManager.ServerInstance.TryGetClanMemberConnections(player.clanId, out var clanConns) &&
                        clanConns != null && clanConns.Count > 0)
                    {
                        ConsoleNetwork.SendClientCommand(clanConns, "chat.add", ch, userId, line);
                    }
                    break;
                }
                case Chat.ChatChannel.Local:
                {
                    float rangeSq = Chat.localChatRange * Chat.localChatRange;
                    var blocked = chatMessage.BlockedReceivers;
                    foreach (var target in BasePlayer.activePlayerList)
                    {
                        if (target == null || !target.IsConnected) continue;
                        if (IsBlocked(blocked, target.UserIDString)) continue;
                        float sqr = (player.transform.position - target.transform.position).sqrMagnitude;
                        if (sqr > rangeSq) continue;
                        target.SendConsoleCommand("chat.add", ch, userId, line);
                    }
                    break;
                }
                default:
                {
                    var blocked = chatMessage.BlockedReceivers;
                    if (blocked == null || blocked.Count == 0)
                    {
                        ConsoleNetwork.BroadcastToAllClients("chat.add", ch, userId, line);
                    }
                    else
                    {
                        foreach (var target in BasePlayer.activePlayerList)
                        {
                            if (target == null || !target.IsConnected) continue;
                            if (IsBlocked(blocked, target.UserIDString)) continue;
                            target.SendConsoleCommand("chat.add", ch, userId, line);
                        }
                    }
                    break;
                }
            }

            Debug.Log("[" + channel + "] " + output.Console);
            int unixTime = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            Chat.Record(new Chat.ChatEntry
            {
                Channel = channel,
                Message = output.Console,
                UserId = userIdString,
                Username = player.displayName,
                Color = color,
                Time = unixTime
            });
        }

        private static bool IsBlocked(List<string> blocked, string id)
        {
            if (blocked == null || blocked.Count == 0) return false;
            for (int i = 0; i < blocked.Count; i++)
            {
                if (string.Equals(blocked[i], id, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        #endregion

        #region Commands

        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrWhiteSpace(message)) return false;
            message = message.Trim();
            if (message.StartsWith("/") || message.StartsWith("\\"))
                message = message.Substring(1).Trim();
            if (message.Length == 0) return false;

            string[] parts = SplitArgs(message);
            if (parts.Length == 0) return false;
            string cmd = parts[0];
            string[] args = parts.Length > 1 ? new string[parts.Length - 1] : Array.Empty<string>();
            if (parts.Length > 1)
                Array.Copy(parts, 1, args, 0, args.Length);

            var coloured = BetterChatConfig.Config?.Coloured;
            if (string.Equals(cmd, "chat", StringComparison.OrdinalIgnoreCase))
            {
                HandleChatCommand(player, args, fromChat: true);
                return true;
            }
            if (IsCommand(cmd, coloured?.NameColourCommands))
            {
                HandleColourCommand(player, cmd, args, isMessage: false);
                return true;
            }
            if (IsCommand(cmd, coloured?.NameColoursCommands))
            {
                HandleColoursHelp(player, isMessage: false);
                return true;
            }
            if (IsCommand(cmd, coloured?.MessageColourCommands))
            {
                HandleColourCommand(player, cmd, args, isMessage: true);
                return true;
            }
            if (IsCommand(cmd, coloured?.MessageColoursCommands))
            {
                HandleColoursHelp(player, isMessage: true);
                return true;
            }
            return false;
        }

        private static bool IsCommand(string cmd, string[] names)
        {
            if (names == null) return false;
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(cmd, names[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string[] SplitArgs(string text)
        {
            var list = new List<string>();
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0)
                    {
                        list.Add(sb.ToString());
                        sb.Clear();
                    }
                }
                else sb.Append(c);
            }
            if (sb.Length > 0) list.Add(sb.ToString());
            return list.ToArray();
        }

        private bool HasAdminPerm(BasePlayer player) =>
            player != null && (player.IsAdmin || PermissionsBridge.UserHasPermission(player.UserIDString, PermAdmin));

        private void HandleChatCommand(BasePlayer player, string[] args, bool fromChat)
        {
            if (!HasAdminPerm(player))
            {
                Reply(player, "You don't have permission to use this command.");
                return;
            }

            string prefix = fromChat ? "/chat" : "betterchat";
            if (args.Length == 0)
            {
                Reply(player, prefix + " group <add|remove|set|list>");
                Reply(player, prefix + " user <add|remove>");
                return;
            }

            string sub = args[0].ToLowerInvariant();
            if (sub == "group")
            {
                string action = args.Length > 1 ? args[1].ToLowerInvariant() : "";
                if (action == "add")
                {
                    if (args.Length != 3)
                    {
                        Reply(player, "Syntax: " + prefix + " group add <group>");
                        return;
                    }
                    string groupName = args[2].ToLowerInvariant();
                    if (FindGroup(groupName) != null)
                    {
                        Reply(player, Lang("Group Already Exists", "group", groupName));
                        return;
                    }
                    Groups.Add(new ChatGroup(groupName));
                    if (!PermissionsBridge.GroupExists(groupName))
                        PermissionsBridge.CreateGroup(groupName, string.Empty, 0);
                    BetterChatConfig.Save();
                    Reply(player, Lang("Group Added", "group", groupName));
                    return;
                }
                if (action == "remove")
                {
                    if (args.Length != 3)
                    {
                        Reply(player, "Syntax: " + prefix + " group remove <group>");
                        return;
                    }
                    string groupName = args[2].ToLowerInvariant();
                    var group = FindGroup(groupName);
                    if (group == null)
                    {
                        Reply(player, Lang("Group Does Not Exist", "group", groupName));
                        return;
                    }
                    Groups.Remove(group);
                    BetterChatConfig.Save();
                    Reply(player, Lang("Group Removed", "group", groupName));
                    return;
                }
                if (action == "set")
                {
                    if (args.Length != 5)
                    {
                        Reply(player, "Syntax: " + prefix + " group set <group> <field> <value>");
                        Reply(player, "Fields:\n" + ChatGroupFields.FieldList());
                        return;
                    }
                    string groupName = args[2].ToLowerInvariant();
                    var group = FindGroup(groupName);
                    if (group == null)
                    {
                        Reply(player, Lang("Group Does Not Exist", "group", groupName));
                        return;
                    }
                    string field = args[3];
                    string value = args[4];
                    switch (ChatGroupFields.SetField(group, field, value))
                    {
                        case ChatGroupFields.SetValueResult.Success:
                            BetterChatConfig.Save();
                            Reply(player, Lang("Group Field Changed", "group", group.GroupName, "field", field, "value", value));
                            break;
                        case ChatGroupFields.SetValueResult.InvalidField:
                            Reply(player, Lang("Invalid Field", "field", field));
                            break;
                        case ChatGroupFields.SetValueResult.InvalidValue:
                            string type = ChatGroupFields.Fields.TryGetValue(field, out var f) ? f.UserFriendlyType : "?";
                            Reply(player, Lang("Invalid Value", "field", field, "value", value, "type", type));
                            break;
                    }
                    return;
                }
                if (action == "list")
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < Groups.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(Groups[i].GroupName);
                    }
                    Reply(player, sb.ToString());
                    return;
                }
                Reply(player, "Syntax: " + prefix + " group <add|remove|set|list>");
                return;
            }

            if (sub == "user")
            {
                string action = args.Length > 1 ? args[1].ToLowerInvariant() : "";
                if (action != "add" && action != "remove")
                {
                    Reply(player, "Syntax: " + prefix + " user <add|remove>");
                    return;
                }
                if (args.Length != 4)
                {
                    Reply(player, "Syntax: " + prefix + " user " + action + " <username|id> <group>");
                    return;
                }
                var target = FindPlayer(args[2], out string err);
                if (target == null)
                {
                    Reply(player, err);
                    return;
                }
                string groupName = args[3].ToLowerInvariant();
                var group = FindGroup(groupName);
                if (group == null)
                {
                    Reply(player, Lang("Group Does Not Exist", "group", groupName));
                    return;
                }
                if (action == "add")
                {
                    if (PermissionsBridge.UserHasGroup(target.UserIDString, groupName))
                    {
                        Reply(player, Lang("Player Already In Group", "player", target.displayName, "group", groupName));
                        return;
                    }
                    PermissionsBridge.AddUserGroup(target.UserIDString, groupName);
                    Colours?.ClearCache(target.UserIDString);
                    Reply(player, Lang("Added To Group", "player", target.displayName, "group", groupName));
                }
                else
                {
                    if (!PermissionsBridge.UserHasGroup(target.UserIDString, groupName))
                    {
                        Reply(player, Lang("Player Not In Group", "player", target.displayName, "group", groupName));
                        return;
                    }
                    PermissionsBridge.RemoveUserGroup(target.UserIDString, groupName);
                    Colours?.ClearCache(target.UserIDString);
                    Reply(player, Lang("Removed From Group", "player", target.displayName, "group", groupName));
                }
            }
        }

        private void HandleColourCommand(BasePlayer player, string cmd, string[] args, bool isMessage)
        {
            var cfg = BetterChatConfig.Config.Coloured;
            if (args.Length < 1)
            {
                Reply(player, Lang("IncorrectUsage",
                    "0", isMessage ? cfg.MessageColourCommands[0] : cfg.NameColourCommands[0],
                    "1", isMessage ? cfg.MessageColoursCommands[0] : cfg.NameColoursCommands[0]));
                return;
            }

            if (string.Equals(args[0], "set", StringComparison.OrdinalIgnoreCase))
            {
                if ((!isMessage && !ColouredChatService.CanNameSetOthers(player, cfg)) ||
                    (isMessage && !ColouredChatService.CanMessageSetOthers(player, cfg)))
                {
                    Reply(player, Lang("NoPermissionSetOthers", "0", isMessage ? "message" : "name"));
                    return;
                }
                if (args.Length < 3)
                {
                    Reply(player, Lang("IncorrectSetUsage", "0", isMessage ? cfg.MessageColourCommands[0] : cfg.NameColourCommands[0]));
                    return;
                }
                var target = FindPlayer(args[1], out string err);
                if (target == null)
                {
                    Reply(player, Lang("PlayerNotFound", "0", args[1]));
                    return;
                }
                string[] rest = Slice(args, 2);
                ProcessColour(player, target, rest[0].ToLowerInvariant(), Slice(rest, 1), isMessage);
                return;
            }

            if (string.Equals(args[0], "group", StringComparison.OrdinalIgnoreCase))
            {
                if (!player.IsAdmin)
                {
                    Reply(player, Lang("NoPermission"));
                    return;
                }
                if (args.Length < 3)
                {
                    Reply(player, Lang("IncorrectGroupUsage", "0", isMessage ? cfg.MessageColourCommands[0] : cfg.NameColourCommands[0]));
                    return;
                }
                if (!PermissionsBridge.GroupExists(args[1]))
                    PermissionsBridge.CreateGroup(args[1], string.Empty, 0);
                ProcessColour(player, player, args[2].ToLowerInvariant(), Slice(args, 3), isMessage, args[1]);
                return;
            }

            if ((!isMessage && !ColouredChatService.HasNamePerm(player, cfg)) ||
                (isMessage && !ColouredChatService.HasMessagePerm(player, cfg)))
            {
                Reply(player, Lang("NoPermission"));
                return;
            }
            ProcessColour(player, player, args[0].ToLowerInvariant(), Slice(args, 1), isMessage);
        }

        private void ProcessColour(BasePlayer player, BasePlayer target, string colLower, string[] colours, bool isMessage, string groupName = "")
        {
            var cfg = BetterChatConfig.Config.Coloured;
            bool isGroup = !string.IsNullOrEmpty(groupName);
            bool isCalledOnto = player != target && !isGroup;
            string key = isGroup ? groupName : target.UserIDString;
            Colours.GetOrCreate(key, isGroup);

            if (colLower == "gradient")
            {
                if ((!isMessage && !ColouredChatService.CanNameGradient(player, cfg)) ||
                    (isMessage && !ColouredChatService.CanMessageGradient(player, cfg)))
                {
                    Reply(player, Lang("NoPermissionGradient", "0", isMessage ? "message" : "name"));
                    return;
                }
                var valid = new List<string>();
                for (int i = 0; i < colours.Length; i++)
                {
                    var col = colours[i];
                    bool ok = isMessage ? Colours.IsValidMessageColour(col, player) : Colours.IsValidNameColour(col, player);
                    if (ok && Colours.IsValidColour(col) && Colours.IsInvalidCharacter(col) == null)
                        valid.Add(col);
                }
                if (valid.Count < 2)
                {
                    Reply(player, Lang("IncorrectGradientUsageArgs", "0", isMessage ? cfg.MessageColourCommands[0] : cfg.NameColourCommands[0]));
                    return;
                }
                string[] validArr = valid.ToArray();
                string gradientName = Colours.ProcessGradient(isMessage ? "Example Message" : target.displayName, validArr);
                if (string.IsNullOrEmpty(gradientName))
                {
                    Reply(player, Lang("IncorrectGradientUsage", "0", isMessage ? cfg.MessageColourCommands[0] : cfg.NameColourCommands[0]));
                    return;
                }
                var data = Colours.GetOrCreate(key, isGroup);
                if (isMessage)
                {
                    data.MessageColour = string.Empty;
                    data.MessageGradientArgs = validArr;
                }
                else
                {
                    data.NameColour = string.Empty;
                    data.NameGradientArgs = validArr;
                    if (!isGroup) Colours.ClearCache(key);
                }
                if (isGroup) Colours.ClearCache();
                Colours.Save();
                if (target.IsConnected) Reply(target, Lang("GradientChanged", "0", GetCorrectLang(isGroup, isMessage, key), "1", gradientName));
                if (isCalledOnto) Reply(player, Lang("GradientChangedFor", "0", target.displayName, "1", isMessage ? "message" : "name", "2", gradientName));
                return;
            }

            if (colLower == "reset" || colLower == "clear" || colLower == "remove")
            {
                var data = Colours.GetOrCreate(key, isGroup);
                if (isMessage)
                {
                    data.MessageColour = string.Empty;
                    data.MessageGradientArgs = null;
                }
                else
                {
                    data.NameColour = string.Empty;
                    data.NameGradientArgs = null;
                    Colours.ClearCache(key);
                }
                Colours.TryRemoveEmpty(key);
                if (isGroup) Colours.ClearCache();
                Colours.Save();
                if (target.IsConnected) Reply(target, Lang("ColourRemoved", "0", GetCorrectLang(isGroup, isMessage, key)));
                if (isCalledOnto) Reply(player, Lang("ColourRemovedFor", "0", target.displayName, "1", isMessage ? "message" : "name"));
                return;
            }

            if (colLower == "random")
            {
                if (!isMessage && !ColouredChatService.CanNameRandomColour(player, cfg) ||
                    isMessage && !ColouredChatService.CanMessageRandomColour(player, cfg))
                {
                    Reply(player, Lang("NoPermissionRandom", "0", isMessage ? "message" : "name"));
                    return;
                }
                colLower = Colours.GetRndColour();
                if (isMessage) Colours.ChangeMessageColour(key, colLower, null);
                else Colours.ChangeNameColour(key, colLower, null);
                if (isGroup) Colours.ClearCache();
                Colours.Save();
                if (target.IsConnected) Reply(target, Lang("RndColour", "0", GetCorrectLang(isGroup, isMessage, key), "1", colLower));
                if (isCalledOnto) Reply(player, Lang("RndColourFor", "0", isMessage ? "Message" : "Name", "1", target.displayName, "2", colLower));
                return;
            }

            if (colLower == "rainbow")
            {
                if ((isMessage && !ColouredChatService.HasMessageRainbow(player, cfg)) ||
                    (!isMessage && !ColouredChatService.HasNameRainbow(player, cfg)))
                {
                    Reply(player, Lang("NoPermissionRainbow"));
                    return;
                }
                if (isMessage) Colours.ChangeMessageColour(key, string.Empty, cfg.RainbowColours);
                else Colours.ChangeNameColour(key, string.Empty, cfg.RainbowColours);
                if (isGroup) Colours.ClearCache();
                Colours.Save();
                if (target.IsConnected) Reply(target, Lang("RainbowColour", "0", GetCorrectLang(isGroup, isMessage, key)));
                if (isCalledOnto) Reply(player, Lang("RainbowColourFor", "0", isMessage ? "Message" : "Name", "1", target.displayName));
                return;
            }

            string invalidChar = Colours.IsInvalidCharacter(colLower);
            if (invalidChar != null)
            {
                Reply(player, Lang("InvalidCharacters", "0", invalidChar));
                return;
            }
            if (!Colours.IsValidColour(colLower))
            {
                Reply(player, Lang("InvalidColour"));
                return;
            }
            if (isMessage ? !Colours.IsValidMessageColour(colLower, player) : !Colours.IsValidNameColour(colLower, player))
            {
                Reply(player, Lang("InvalidColour"));
                return;
            }

            if (isMessage) Colours.ChangeMessageColour(key, colLower, null);
            else Colours.ChangeNameColour(key, colLower, null);
            Colours.Save();
            if (isGroup) Colours.ClearCache();

            if (isCalledOnto) Reply(player, Lang("ColourChangedFor", "0", target.displayName, "1", isMessage ? "message" : "name", "2", colLower));
            else if (isGroup && target.IsConnected) Reply(target, Lang("ColourChangedFor", "0", key, "1", isMessage ? "message" : "name", "2", colLower));
            else if (target.IsConnected) Reply(target, Lang("ColourChanged", "0", isMessage ? "Message" : "Name", "1", colLower));
        }

        private void HandleColoursHelp(BasePlayer player, bool isMessage)
        {
            var cfg = BetterChatConfig.Config.Coloured;
            if ((!isMessage && !ColouredChatService.HasNamePerm(player, cfg)) ||
                (isMessage && !ColouredChatService.HasMessagePerm(player, cfg)))
            {
                Reply(player, Lang("NoPermission"));
                return;
            }

            _helpSb.Clear();
            string commandName = isMessage ? cfg.MessageColourCommands[0] : cfg.NameColourCommands[0];
            _helpSb.AppendLine().Append('/').Append(commandName).Append(" <color=#ff6666>#ff6666</color>");
            if (isMessage ? ColouredChatService.CanMessageRandomColour(player, cfg) : ColouredChatService.CanNameRandomColour(player, cfg))
                _helpSb.AppendLine().Append('/').Append(commandName).Append(" random");
            if (isMessage ? ColouredChatService.CanMessageGradient(player, cfg) : ColouredChatService.CanNameGradient(player, cfg))
            {
                _helpSb.AppendLine().Append('/').Append(commandName).Append(" gradient <color=#ff6666>#ff6666</color> <color=#ff6666>#ff6666</color>");
                _helpSb.AppendLine().Append('/').Append(commandName).Append(" gradient <color=#ff6666>#ff6666</color> <color=#ffff94>#ffff94</color> <color=#90ee90>#90ee90</color>");
            }
            if (isMessage ? ColouredChatService.CanMessageSetOthers(player, cfg) : ColouredChatService.CanNameSetOthers(player, cfg))
            {
                _helpSb.AppendLine().Append('/').Append(commandName).Append(" set <color=#a8a8a8>playerIdOrName</color> <color=#ff6666>#ff6666</color>");
                _helpSb.AppendLine().Append('/').Append(commandName).Append(" set <color=#a8a8a8>playerIdOrName</color> gradient <color=#ff6666>#ff6666</color> <color=#ffff94>#ffff94</color>");
            }
            if (player.IsAdmin)
            {
                _helpSb.AppendLine().Append('/').Append(commandName).Append(" group <color=#a8a8a8>groupName</color> <color=#ff6666>#ff6666</color>");
                _helpSb.AppendLine().Append('/').Append(commandName).Append(" group <color=#a8a8a8>groupName</color> gradient <color=#ff6666>#ff6666</color> <color=#ffff94>#ffff94</color>");
            }

            string available = _helpSb.ToString();
            _helpSb.Clear();
            var useWhite = isMessage ? cfg.MessageUseWhitelist : cfg.NameUseWhitelist;
            var useBlack = isMessage ? cfg.MessageUseBlacklist : cfg.NameUseBlacklist;
            var hexList = isMessage
                ? (useWhite ? cfg.MessageWhitelistedColoursHex : cfg.MessageBlockColoursHex)
                : (useWhite ? cfg.NameWhitelistedColoursHex : cfg.NameBlockColoursHex);
            var ranges = isMessage
                ? (useWhite ? cfg.MessageWhitelistedRangeColoursHex : cfg.MessageBlacklistedRangeColoursHex)
                : (useWhite ? cfg.NameWhitelistedRangeColoursHex : cfg.NameBlacklistedRangeColoursHex);

            if (useWhite || useBlack)
            {
                _helpSb.AppendLine(useWhite ? "Whitelisted Colours:" : "Blacklisted Colours:");
                if (hexList != null)
                {
                    for (int i = 0; i < hexList.Count; i++)
                        _helpSb.Append("- <color=").Append(hexList[i]).Append('>').Append(hexList[i]).AppendLine("</color>");
                }
                if (ranges != null)
                {
                    for (int i = 0; i < ranges.Count; i++)
                    {
                        var r = ranges[i];
                        if (r == null) continue;
                        _helpSb.Append("- From <color=").Append(r.From).Append('>').Append(r.From)
                            .Append("</color> to <color=").Append(r.To).Append('>').Append(r.To).AppendLine("</color>");
                    }
                }
            }

            Reply(player, Lang("ColoursInfo", "0", available, "1", _helpSb.ToString()));
        }

        private static string GetCorrectLang(bool isGroup, bool isMessage, string key) =>
            isGroup
                ? (isMessage ? "Group " + key + " message" : "Group " + key + " name")
                : (isMessage ? "Message" : "Name");

        private static string[] Slice(string[] arr, int start)
        {
            if (arr == null || start >= arr.Length) return Array.Empty<string>();
            var result = new string[arr.Length - start];
            Array.Copy(arr, start, result, 0, result.Length);
            return result;
        }

        private BasePlayer FindPlayer(string nameOrId, out string response)
        {
            response = null;
            if (nameOrId.Length == 17 && nameOrId.StartsWith("7656119"))
            {
                if (ulong.TryParse(nameOrId, out ulong id))
                {
                    var byId = BasePlayer.FindByID(id) ?? BasePlayer.FindSleeping(id);
                    if (byId != null) return byId;
                }
                response = "Could not find player with ID '" + nameOrId + "'";
                return null;
            }

            string lower = nameOrId.ToLowerInvariant();
            BasePlayer exact = null;
            var partial = new List<BasePlayer>();
            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p == null) continue;
                string n = p.displayName ?? "";
                if (n.Equals(nameOrId, StringComparison.OrdinalIgnoreCase))
                {
                    exact = p;
                    break;
                }
                if (n.ToLowerInvariant().IndexOf(lower, StringComparison.Ordinal) >= 0)
                    partial.Add(p);
            }
            if (exact != null) return exact;
            if (partial.Count == 1) return partial[0];
            if (partial.Count == 0)
            {
                response = "Could not find player with name '" + nameOrId + "'";
                return null;
            }
            var sb = new StringBuilder("Multiple matching players found: \n");
            for (int i = 0; i < partial.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(partial[i].displayName);
            }
            response = sb.ToString();
            return null;
        }

        private string DescribeWho(string query)
        {
            var sb = new StringBuilder();
            sb.AppendLine(PermissionsBridge.DescribeBind());
            string[] permGroups = PermissionsBridge.GetAllGroupNames();
            sb.Append("0Permissions groups: ");
            sb.AppendLine(permGroups.Length == 0 ? "(none)" : string.Join(", ", permGroups));

            string id = query;
            BasePlayer player = null;
            if (!string.IsNullOrWhiteSpace(query))
            {
                player = FindPlayer(query, out _);
                if (player != null) id = player.UserIDString;
            }
            else
            {
                sb.Append("Usage: betterchat who <steamid|name>");
                return sb.ToString();
            }

            var names = PermissionsBridge.GetUserGroups(id);
            sb.Append(id);
            if (player != null) sb.Append(" (").Append(player.displayName).Append(')');
            sb.Append(" permGroups=[").Append(string.Join(", ", names)).AppendLine("]");

            var matched = new List<string>();
            var titles = new List<string>();
            var groups = Groups;
            if (groups != null)
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    var g = groups[i];
                    if (g == null) continue;
                    bool inGroup = false;
                    for (int n = 0; n < names.Length; n++)
                    {
                        if (string.Equals(g.GroupName, names[n], StringComparison.OrdinalIgnoreCase))
                        {
                            inGroup = true;
                            break;
                        }
                    }
                    if (!inGroup) continue;
                    matched.Add(g.GroupName);
                    if (g.Title != null && !g.Title.Hidden && !string.IsNullOrWhiteSpace(g.Title.Text))
                        titles.Add(g.Title.Text);
                }
            }
            sb.Append("BetterChat match=[").Append(string.Join(", ", matched)).Append("] titles=[").Append(string.Join(" ", titles)).Append(']');
            return sb.ToString();
        }

        private static void Reply(BasePlayer player, string message)
        {
            if (player == null || !player.IsConnected) return;
            player.ChatMessage(message);
        }

        #endregion

        #region Console

        private void RegisterConsoleCommands()
        {
            RegisterConsole("betterchat", arg =>
            {
                string[] args = ArgToStrings(arg);
                if (args.Length > 0 && args[0].Equals("who", StringComparison.OrdinalIgnoreCase))
                {
                    arg.ReplyWith(DescribeWho(args.Length > 1 ? args[1] : null));
                    return;
                }
                var player = arg?.Player();
                if (player != null)
                    HandleChatCommand(player, args, fromChat: false);
                else
                    HandleChatCommandConsole(arg, args);
            }, serverAdmin: true);
        }

        private static string[] ArgToStrings(ConsoleSystem.Arg arg)
        {
            if (arg == null) return Array.Empty<string>();
            try
            {
                var raw = arg.Args;
                if (raw == null || raw.Length == 0) return Array.Empty<string>();
                var args = new string[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                    args[i] = raw[i].ToString() ?? string.Empty;
                return args;
            }
            catch { return Array.Empty<string>(); }
        }

        private void HandleChatCommandConsole(ConsoleSystem.Arg arg, string[] args)
        {
            if (arg == null) return;
            var dummy = arg.Player();
            if (dummy != null)
            {
                HandleChatCommand(dummy, args, fromChat: false);
                return;
            }
            if (args.Length == 0)
            {
                arg.ReplyWith("betterchat group <add|remove|set|list>\nbetterchat user <add|remove>\nbetterchat who <steamid|name>");
                return;
            }
            Debug.Log("[BetterChat] RCON: " + string.Join(" ", args));
            string sub = args[0].ToLowerInvariant();
            if (sub == "who")
            {
                arg.ReplyWith(DescribeWho(args.Length > 1 ? args[1] : null));
                return;
            }
            if (sub == "group" && args.Length > 1 && args[1].ToLowerInvariant() == "list")
            {
                var sb = new StringBuilder();
                for (int i = 0; i < Groups.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(Groups[i].GroupName);
                }
                arg.ReplyWith(sb.ToString());
                return;
            }
            arg.ReplyWith("Use in-game /chat or: betterchat group list");
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler, bool serverAdmin = false)
        {
            string fullName = "global." + name;
            var cmd = new ConsoleSystem.Command
            {
                Name = name,
                Parent = "",
                FullName = fullName,
                Variable = false,
                ServerAdmin = serverAdmin,
                AllowRunFromServer = true,
                Replicated = false,
                Call = a =>
                {
                    try { handler(a); }
                    catch (Exception ex) { Debug.LogWarning("[BetterChat] command " + name + ": " + ex.Message); }
                }
            };
            ConsoleSystem.Index.Server.Dict[fullName] = cmd;
            if (ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict[name] = cmd;
            _registeredCommands.Add(cmd);
        }

        private void UnregisterConsoleCommands()
        {
            try
            {
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                for (int i = 0; i < _registeredCommands.Count; i++)
                {
                    var cmd = _registeredCommands[i];
                    dict?.Remove(cmd.FullName);
                    globalDict?.Remove(cmd.Name);
                }
            }
            catch { }
            _registeredCommands.Clear();
        }

        #endregion

        #region Lang / API

        private static readonly Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            ["Group Already Exists"] = "Group '{group}' already exists.",
            ["Group Does Not Exist"] = "Group '{group}' doesn't exist.",
            ["Group Field Changed"] = "Changed {field} to {value} for group '{group}'.",
            ["Group Added"] = "Successfully added group '{group}'.",
            ["Group Removed"] = "Successfully removed group '{group}'.",
            ["Invalid Field"] = "{field} is not a valid field. Type 'chat group set' to list all existing fields.",
            ["Invalid Value"] = "'{value}' is not a correct value for field '{field}'! Should be a '{type}'.",
            ["Player Already In Group"] = "{player} already is in group '{group}'.",
            ["Added To Group"] = "{player} was added to group '{group}'.",
            ["Player Not In Group"] = "{player} is not in group '{group}'.",
            ["Removed From Group"] = "{player} was removed from group '{group}'.",
            ["NoPermission"] = "You don't have permission to use this command.",
            ["NoPermissionSetOthers"] = "You don't have permission to set other players {0} colours.",
            ["NoPermissionGradient"] = "You don't have permission to use {0} gradients.",
            ["NoPermissionRandom"] = "You don't have permission to use random {0} colours.",
            ["NoPermissionRainbow"] = "You don't have permission to use the rainbow colours.",
            ["IncorrectGradientUsage"] = "Incorrect usage! To use gradients please use /{0} gradient hexCode1 hexCode2 ...",
            ["IncorrectGradientUsageArgs"] = "Incorrect usage! A gradient requires at least two different valid colours!",
            ["GradientChanged"] = "{0} gradient changed to {1}!",
            ["GradientChangedFor"] = "{0}'s gradient {1} colour changed to {2}!",
            ["IncorrectUsage"] = "Incorrect usage! /{0} <colour>\nFor detailed help do /{1}",
            ["IncorrectSetUsage"] = "Incorrect set usage! /{0} set <playerIdOrName> <colourOrColourArgument>\nFor a list of colours do /colours",
            ["PlayerNotFound"] = "Player {0} was not found.",
            ["InvalidCharacters"] = "The character '{0}' is not allowed in colours. Please remove it.",
            ["ColourRemoved"] = "{0} colour removed!",
            ["ColourRemovedFor"] = "{0}'s {1} colour was removed!",
            ["ColourChanged"] = "{0} colour changed to <color={1}>{1}</color>!",
            ["ColourChangedFor"] = "{0}'s {1} colour changed to <color={2}>{2}</color>!",
            ["ColoursInfo"] = "You can only use hexcodes, eg '<color=#ffff94>#ffff94</color>'\nTo remove your colour, use 'clear', 'reset' or 'remove'\n\nAvailable Commands: {0}\n\n{1}",
            ["InvalidColour"] = "That colour is not valid. Do /colours for more information on valid colours.",
            ["RndColour"] = "{0} colour was randomized to <color={1}>{1}</color>",
            ["RndColourFor"] = "{0} colour of {1} randomized to <color={2}>{2}</color>.",
            ["RainbowColour"] = "{0} colour was set to rainbow.",
            ["RainbowColourFor"] = "{0} colour of {1} set to rainbow.",
            ["IncorrectGroupUsage"] = "Incorrect group usage! /{0} group <groupName> <colourOrColourArgument>\nFor a list of colours do /colours",
        };

        private static string Lang(string key, params string[] kv)
        {
            if (!Messages.TryGetValue(key, out var msg)) return key;
            if (kv == null || kv.Length == 0) return msg;
            for (int i = 0; i + 1 < kv.Length; i += 2)
            {
                msg = msg.Replace("{" + kv[i] + "}", kv[i + 1] ?? "");
            }
            return msg;
        }

        public static string API_GetFormattedMessage(BasePlayer player, string message, bool console = false)
        {
            if (Instance == null || player == null) return message;
            var output = Instance.PrepareMessage(player, message).GetOutput();
            return console ? output.Console : output.Chat;
        }

        public static Dictionary<string, object> API_GetMessageData(BasePlayer player, string message) =>
            Instance?.PrepareMessage(player, message)?.ToDictionary();

        public static bool API_AddGroup(string group)
        {
            if (Instance == null || string.IsNullOrEmpty(group)) return false;
            if (Instance.FindGroup(group) != null) return false;
            Instance.Groups.Add(new ChatGroup(group.ToLowerInvariant()));
            BetterChatConfig.Save();
            return true;
        }

        public static bool API_GroupExists(string group) => Instance?.FindGroup(group) != null;

        #endregion
    }
}
