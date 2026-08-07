// SkillTreeMod.cs  --  Harmony entry point for SkillTree 1.7.x
// Hosts Oxide.Plugins.SkillTree, drives Init/Loaded/OnServerInitialized/Unload
// lifecycle, registers chat + console commands, routes /st chat, etc.
// Pattern follows KitsHarmonyMod and ArmoredTrainMod.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyChat;
using UnityEngine;
using OxidePlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony
{
    /// <summary>Persistent MonoBehaviour: NextTick queue + StartCoroutine for timers.</summary>
    public class ModRunner : MonoBehaviour
    {
        public static ModRunner Instance { get; private set; }
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static GameObject _go;

        public static void Ensure()
        {
            if (Instance != null) return;
            _go = new GameObject("SkillTree_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _go.hideFlags = HideFlags.HideAndDontSave;
            Instance = _go.AddComponent<ModRunner>();
        }

        public static void Destroy()
        {
            lock (_queue) _queue.Clear();
            if (_go != null) { UnityEngine.Object.Destroy(_go); _go = null; Instance = null; }
        }

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            lock (_queue) _queue.Enqueue(action);
        }

        private void Update()
        {
            while (true)
            {
                Action a;
                lock (_queue) { if (_queue.Count == 0) break; a = _queue.Dequeue(); }
                try { a(); }
                catch (Exception ex) { Debug.LogWarning("[SkillTree] NextTick: " + ex.Message); }
            }
        }
    }

    /// <summary>Harmony mod entry point for SkillTree.</summary>
    public class SkillTreeMod : IHarmonyModHooks
    {
        public static SkillTreeMod Instance { get; private set; }

        // The live plugin instance (accessed via the partial-class helper to avoid private access).
        public static OxidePlugin Plugin => OxidePlugin.GetModInstance();

        private Coroutine _initCoroutine;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();
        /// <summary>
        /// Chat open aliases (st/skills/score…) registered as unreplicated server console commands.
        /// Chat /st is handled by ChatSayBridge — do not put these on Index.Server.Replicated
        /// (clients spam "Replicated convar not found on client: global.setgenes" etc.). Same as Kits.
        /// </summary>
        private readonly List<ConsoleSystem.Command> _chatAliasCommands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Exact CUI command names (original casing) for cui.endtest rewrite. Longer names first.</summary>
        private readonly List<string> _uiConsoleCommands = new List<string>();
        private Action _permissionsReadyCallback;
        private Action _movementSpeedReadyCallback;

        public const string AppDomainApiKey = "SkillTree_ApiType";
        public const string CuiMarker = "ST";

        /// <summary>CUI button command names sorted longest-first for RustCui rewrite.</summary>
        public IReadOnlyList<string> UiConsoleCommands => _uiConsoleCommands;

        // ---- IHarmonyModHooks ---------------------------------------------

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            ModRunner.Ensure();

            OxidePlugin plugin;
            try
            {
                plugin = new OxidePlugin();
                OxidePlugin.SetInstance(plugin);
                plugin.HarmonyLoadConfig();
            }
            catch (Exception ex)
            {
                Debug.LogError("[SkillTree] Failed to construct/config plugin: " + ex);
                return;
            }

            // Expose via AppDomain for other mods that call SkillTree hooks.
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(SkillTreeMod)); }
            catch { }

            // Drop stale replicated SkillTree chat aliases from older builds (client ERRORS overlay).
            ScrubSkillTreeFromReplicatedList();

            // Seed basic chat commands (config-independent defaults). Chat routing = ChatSayBridge.
            foreach (var cmd in new[] { "st", "skilltree", "skills", "score", "scoreboard" })
            {
                _chatCommandNames.Add(cmd);
                RegisterChatAliasConsole(cmd);
            }

            // Shared chat.say bridge — coexist with Shop (/s) regardless of Harmony prefix order.
            ChatSayBridge.Register("SkillTree", OnChatCommand);

            // Register [ConsoleCommand] handlers + ST_UI immediately (CUI needs them before players open /st).
            RegisterConsoleCommands();
            RegisterAttributedConsoleCommands();

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            // MovementSpeed loads before SkillTree alphabetically, but harmony.load order can differ.
            _movementSpeedReadyCallback = OnMovementSpeedReady;
            RegisterMovementSpeedReadyCallback(_movementSpeedReadyCallback);

            // Start init coroutine (waits for ServerMgr + ItemManager).
            _initCoroutine = ModRunner.Instance.StartCoroutine(WaitForServerThenInit());

            Debug.Log("[SkillTree] Harmony mod loaded. Chat: /st /skilltree /skills. Config: HarmonyConfig/SkillTree.json. No forced DLL load order (Permissions + MovementSpeed bind via ready callbacks).");
        }

        private void OnPermissionsReady()
        {
            try
            {
                var plugin = OxidePlugin.GetModInstance();
                if (plugin == null) return;
                var mi = typeof(OxidePlugin).GetMethod("HandlePermissions", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                mi?.Invoke(plugin, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkillTree] Permissions ready re-register: " + ex.Message);
            }
        }

        private void OnMovementSpeedReady()
        {
            try
            {
                var plugin = OxidePlugin.GetModInstance();
                if (plugin == null) return;
                plugin.ResolvePluginReferences();
                Debug.Log("[SkillTree] MovementSpeed ready — RoadRunner/swim PluginReference rebound.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkillTree] MovementSpeed ready: " + ex.Message);
            }
        }

        private static void RegisterMovementSpeedReadyCallback(Action callback)
        {
            if (callback == null) return;
            try
            {
                var t = AppDomain.CurrentDomain.GetData("MovementSpeed_ApiType") as Type;
                var mi = t?.GetMethod("RegisterReadyCallback", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Action) }, null);
                if (mi != null)
                {
                    mi.Invoke(null, new object[] { callback });
                    return;
                }
            }
            catch { }

            try
            {
                var list = AppDomain.CurrentDomain.GetData("MovementSpeed_ReadyCallbacks") as System.Collections.IList;
                if (list == null)
                {
                    list = new List<Action>();
                    AppDomain.CurrentDomain.SetData("MovementSpeed_ReadyCallbacks", list);
                }
                lock (list)
                {
                    if (!list.Contains(callback))
                        list.Add(callback);
                }
            }
            catch { }

            // Already up
            if (AppDomain.CurrentDomain.GetData("MovementSpeed_ApiType") is Type)
            {
                try { callback(); } catch { }
            }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            ChatSayBridge.Unregister("SkillTree");

            if (_permissionsReadyCallback != null)
            {
                PermissionsBridge.UnregisterReadyCallback(_permissionsReadyCallback);
                _permissionsReadyCallback = null;
            }
            _movementSpeedReadyCallback = null;

            if (_initCoroutine != null && ModRunner.Instance != null)
            {
                ModRunner.Instance.StopCoroutine(_initCoroutine);
                _initCoroutine = null;
            }

            OxidePlugin.GetModInstance()?.timer?.DestroyAll();
            OxidePlugin.GetModInstance()?.CallUnload();

            UnregisterConsoleCommands();

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); }
            catch { }

            ModRunner.Destroy();
            OxidePlugin.ClearInstance();
            Instance = null;
            Debug.Log("[SkillTree] Harmony mod unloaded.");
        }

        // ---- Init coroutine ---------------------------------------------

        private IEnumerator WaitForServerThenInit(int attempt = 0)
        {
            while (ServerMgr.Instance == null) yield return null;
            while (ItemManager.itemList == null || ItemManager.itemList.Count == 0)
            {
                if (attempt > 120)
                {
                    Debug.LogWarning("[SkillTree] ItemManager timeout; proceeding.");
                    break;
                }
                yield return new WaitForSeconds(attempt < 10 ? 0.5f : 1f);
                attempt++;
            }

            yield return new WaitForSeconds(1f); // let other mods finish loading

            var plugin = Plugin;
            if (plugin == null) yield break;

            // Oxide order: Init -> Loaded -> OnServerInitialized.
            try { plugin.CallInit(); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] Init: " + ex.Message); }

            try { plugin.ResolvePluginReferences(); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] PluginReference bind: " + ex.Message); }

            try { plugin.CallLoaded(); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] Loaded: " + ex.Message); }

            try { plugin.CallOnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[SkillTree] OnServerInitialized: " + ex); }

            // After Init/OnServerInitialized, cmd may have new registrations.
            RefreshDynamicCommands();

            _initCoroutine = null;
            Debug.Log("[SkillTree] Server initialized.");
        }

        // ---- Chat command routing ---------------------------------------

        /// <summary>Called by Chat_Say_Patch for messages starting with /.</summary>
        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrWhiteSpace(message)) return false;
            message = message.Trim();
            if (message.StartsWith("/")) message = message.Substring(1).Trim();

            string[] parts = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            string commandName = parts[0].ToLowerInvariant();
            if (!_chatCommandNames.Contains(commandName)) return false;

            string[] args = parts.Skip(1).ToArray();

            // Check if it's a dynamic cmd-registered command.
            var plugin = Plugin;
            if (plugin == null) return false;

            // Try registered commands first (from cmd.AddChatCommand).
            foreach (var reg in plugin.cmd.RegisteredChatCommands)
            {
                if (!string.Equals(reg.name, commandName, StringComparison.OrdinalIgnoreCase)) continue;
                InvokeChatMethod(plugin, reg.method, player, commandName, args);
                return true;
            }

            // Default: the "st"/"skilltree"/"skills" and "score"/"scoreboard" commands.
            if (new[] { "st", "skilltree", "skills" }.Contains(commandName, StringComparer.OrdinalIgnoreCase))
            {
                InvokeChatMethod(plugin, "SendMenuCMD", player, commandName, args);
                return true;
            }
            if (new[] { "score", "scoreboard" }.Contains(commandName, StringComparer.OrdinalIgnoreCase))
            {
                InvokeChatMethod(plugin, "CheckScoreBoard", player, commandName, args);
                return true;
            }

            return false;
        }

        private static void InvokeChatMethod(OxidePlugin plugin, string methodName, BasePlayer player, string command, string[] args)
        {
            if (string.IsNullOrEmpty(methodName) || plugin == null || player == null) return;
            try
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var type = typeof(OxidePlugin);

                // Try (BasePlayer, string, string[]) — standard Oxide signature.
                var mi = type.GetMethod(methodName, bf, null, new[] { typeof(BasePlayer), typeof(string), typeof(string[]) }, null);
                if (mi != null) { mi.Invoke(plugin, new object[] { player, command, args }); return; }

                // Try (BasePlayer) — SkillTree-specific compact signature.
                mi = type.GetMethod(methodName, bf, null, new[] { typeof(BasePlayer) }, null);
                if (mi != null) { mi.Invoke(plugin, new object[] { player }); return; }

                // Try (ConsoleSystem.Arg) — some chat commands share a console handler.
                mi = type.GetMethod(methodName, bf, null, new[] { typeof(ConsoleSystem.Arg) }, null);
                if (mi != null)
                {
                    var sb = new StringBuilder(command);
                    foreach (var a in args) sb.Append(' ').Append(a);
                    var opt = ConsoleSystem.Option.Server;
                    if (player.net?.connection != null) opt = opt.FromConnection(player.net.connection);
                    mi.Invoke(plugin, new object[] { new ConsoleSystem.Arg(opt, sb.ToString()) });
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkillTree] InvokeChatMethod " + methodName + ": " + ex.Message);
            }
        }

        // ---- Console command registration --------------------------------

        private void RegisterConsoleCommands()
        {
            // Legacy name kept for compatibility; SkillTree UI uses many discrete [ConsoleCommand]s.
            RegisterConsole("ST_UI", arg => InvokeConsoleMethod("UI_SkillTree", arg), serverAdmin: false);
        }

        /// <summary>
        /// Oxide registers [ConsoleCommand] automatically; Harmony must scan and bind them.
        /// Without this, /st opens but every button command is a no-op.
        /// </summary>
        private void RegisterAttributedConsoleCommands()
        {
            var plugin = Plugin;
            if (plugin == null) return;
            try
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var mi in typeof(OxidePlugin).GetMethods(bf))
                {
                    var attrs = mi.GetCustomAttributes(typeof(Oxide.Plugins.ConsoleCommandAttribute), inherit: false);
                    if (attrs == null || attrs.Length == 0) continue;
                    foreach (Oxide.Plugins.ConsoleCommandAttribute attr in attrs)
                    {
                        if (string.IsNullOrWhiteSpace(attr.Command)) continue;
                        var cmdName = attr.Command.Trim();
                        TrackUiConsoleCommand(cmdName);

                        // Avoid double-registration (case-insensitive).
                        if (_registeredCommands.Any(c =>
                                string.Equals(c.Name, cmdName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(c.FullName, "global." + cmdName, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        var methodName = mi.Name;
                        RegisterConsole(cmdName, arg => InvokeConsoleMethod(methodName, arg), serverAdmin: false);
                    }
                }
                SortUiConsoleCommands();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkillTree] RegisterAttributedConsoleCommands: " + ex.Message);
            }
        }

        private void TrackUiConsoleCommand(string cmdName)
        {
            if (string.IsNullOrEmpty(cmdName)) return;
            for (int i = 0; i < _uiConsoleCommands.Count; i++)
            {
                if (string.Equals(_uiConsoleCommands[i], cmdName, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            _uiConsoleCommands.Add(cmdName);
        }

        private void SortUiConsoleCommands()
        {
            _uiConsoleCommands.Sort((a, b) => b.Length.CompareTo(a.Length));
        }

        private void RefreshDynamicCommands()
        {
            var plugin = Plugin;
            if (plugin == null) return;
            try
            {
                foreach (var reg in plugin.cmd.RegisteredChatCommands)
                {
                    if (string.IsNullOrEmpty(reg.name)) continue;
                    var chatName = reg.name.ToLowerInvariant();
                    _chatCommandNames.Add(chatName);
                    RegisterChatAliasConsole(chatName);
                }

                foreach (var reg in plugin.cmd.RegisteredConsoleCommands)
                {
                    if (string.IsNullOrEmpty(reg.name)) continue;
                    var name = reg.name.Trim();
                    TrackUiConsoleCommand(name);
                    // Avoid double-registration (including chat aliases already registered).
                    if (_registeredCommands.Any(c => string.Equals(c.FullName, "global." + name, StringComparison.OrdinalIgnoreCase) ||
                                                     string.Equals(c.FullName, name, StringComparison.OrdinalIgnoreCase) ||
                                                     string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var captured = reg;
                    RegisterConsole(name, arg => InvokeConsoleMethod(captured.method, arg), serverAdmin: false);
                }
                SortUiConsoleCommands();
                // Config-driven aliases may have been left on Replicated by older DLLs.
                ScrubSkillTreeFromReplicatedList();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkillTree] RefreshDynamicCommands: " + ex.Message);
            }
        }

        /// <summary>
        /// Register an undotted chat alias (e.g. st, skills) as a server console command for F1/RCON.
        /// Do NOT set Variable/Replicated or add to Index.Server.Replicated — clients have no ConsoleGen
        /// entry and spam "Replicated convar not found on client: global.setgenes" (etc.) on join.
        /// Player chat /st is handled by ChatSayBridge (same pattern as Kits).
        /// </summary>
        private void RegisterChatAliasConsole(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (name.IndexOf('.') >= 0) return;

            if (_chatAliasCommands.Any(c =>
                    string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.FullName, "global." + name, StringComparison.OrdinalIgnoreCase)))
                return;

            // Do not overwrite an existing UI/console registration for the same short name.
            if (_registeredCommands.Any(c =>
                    string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.FullName, "global." + name, StringComparison.OrdinalIgnoreCase)))
                return;

            string localName = name;
            // Never overwrite another mod's console command (e.g. Shop /s).
            if (ConsoleSystem.Index.Server.Dict != null &&
                ConsoleSystem.Index.Server.Dict.ContainsKey("global." + localName))
                return;

            var cmd = new ConsoleSystem.Command
            {
                Name = localName,
                Parent = string.Empty,
                FullName = "global." + localName,
                Variable = false,
                ServerAdmin = false,
                ServerUser = true,
                AllowRunFromServer = true,
                Replicated = false,
                Call = a =>
                {
                    try
                    {
                        var player = a?.Player();
                        if (player == null) return;
                        var sb = new StringBuilder(localName);
                        var raw = a.Args;
                        if (raw != null)
                        {
                            for (int i = 0; i < raw.Length; i++)
                            {
                                sb.Append(' ');
                                sb.Append(raw[i].ToString() ?? string.Empty);
                            }
                        }
                        OnChatCommand(player, sb.ToString());
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[SkillTree] chat alias " + localName + ": " + ex.Message);
                    }
                }
            };

            try
            {
                ConsoleSystem.Index.Server.Dict["global." + localName] = cmd;
                if (ConsoleSystem.Index.Server.GlobalDict != null)
                    ConsoleSystem.Index.Server.GlobalDict[localName] = cmd;
                _chatAliasCommands.Add(cmd);
                _registeredCommands.Add(cmd);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkillTree] RegisterChatAliasConsole(" + localName + "): " + ex.Message);
            }
        }

        private static System.Collections.IList GetReplicatedList()
        {
            try
            {
                var serverType = typeof(ConsoleSystem.Index.Server);
                var list = serverType.GetField("Replicated", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) as System.Collections.IList;
                if (list != null) return list;
                return serverType.GetProperty("Replicated", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) as System.Collections.IList;
            }
            catch
            {
                return null;
            }
        }

        private static void RemoveFromReplicatedList(ConsoleSystem.Command cmd)
        {
            if (cmd == null) return;
            try
            {
                GetReplicatedList()?.Remove(cmd);
            }
            catch { }
        }

        /// <summary>
        /// Remove SkillTree chat aliases from Index.Server.Replicated (including leftovers from
        /// older DLL builds that incorrectly set Replicated=true / Variable=true).
        /// </summary>
        private void ScrubSkillTreeFromReplicatedList()
        {
            try
            {
                var replicated = GetReplicatedList();
                if (replicated == null) return;

                for (int i = replicated.Count - 1; i >= 0; i--)
                {
                    if (replicated[i] is not ConsoleSystem.Command cmd) continue;
                    string full = cmd.FullName ?? string.Empty;
                    string name = cmd.Name ?? string.Empty;

                    bool isOurs = false;
                    if (full.StartsWith("global.", StringComparison.OrdinalIgnoreCase))
                    {
                        var shortName = full.Substring("global.".Length);
                        if (_chatCommandNames.Contains(shortName) ||
                            _chatAliasCommands.Any(c =>
                                string.Equals(c.FullName, full, StringComparison.OrdinalIgnoreCase)))
                            isOurs = true;
                    }
                    if (!isOurs && _chatCommandNames.Contains(name))
                        isOurs = true;
                    // Defaults always scrubbed even before _chatCommandNames is fully seeded.
                    if (!isOurs && IsDefaultSkillTreeChatAlias(name))
                        isOurs = true;
                    if (!isOurs && full.StartsWith("global.", StringComparison.OrdinalIgnoreCase) &&
                        IsDefaultSkillTreeChatAlias(full.Substring("global.".Length)))
                        isOurs = true;

                    if (!isOurs) continue;
                    cmd.Replicated = false;
                    cmd.Variable = false;
                    replicated.RemoveAt(i);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkillTree] ScrubSkillTreeFromReplicatedList: " + ex.Message);
            }
        }

        private static bool IsDefaultSkillTreeChatAlias(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            switch (name.ToLowerInvariant())
            {
                case "st":
                case "skilltree":
                case "skills":
                case "score":
                case "scoreboard":
                case "setgenes":
                case "locatenodes":
                case "turbo":
                case "crates":
                case "traps":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Route cui.endtest ST &lt;cmd&gt; … to the matching console handler.</summary>
        public void HandleCuiEndtest(ConsoleSystem.Arg args, Array a)
        {
            if (Plugin == null || a == null || a.Length < 2) return;
            var player = args.Connection?.player as BasePlayer ?? args.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return;

            var sb = new StringBuilder();
            for (int i = 1; i < a.Length; i++)
            {
                if (i > 1) sb.Append(' ');
                string s = a.GetValue(i)?.ToString() ?? string.Empty;
                if (s.IndexOfAny(new[] { ' ', '"' }) >= 0)
                    sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
                else
                    sb.Append(s);
            }

            string full = sb.ToString();
            string cmdName = a.GetValue(1)?.ToString() ?? "";
            if (string.IsNullOrEmpty(cmdName)) return;

            try
            {
                var opt = ConsoleSystem.Option.Server.Quiet();
                if (args.Connection != null)
                    opt = opt.FromConnection(args.Connection);
                var uiArg = new ConsoleSystem.Arg(opt, full);

                // Prefer the ConsoleSystem binding we registered.
                ConsoleSystem.Command cmd = null;
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                if (dict != null)
                {
                    if (!dict.TryGetValue("global." + cmdName, out cmd))
                        dict.TryGetValue(cmdName, out cmd);
                }
                if (cmd == null && globalDict != null)
                    globalDict.TryGetValue(cmdName, out cmd);
                // Case-insensitive fallback (CUI casing can differ from registration).
                if (cmd == null && globalDict != null)
                {
                    foreach (var kvp in globalDict)
                    {
                        if (string.Equals(kvp.Key.ToString(), cmdName, StringComparison.OrdinalIgnoreCase))
                        {
                            cmd = kvp.Value;
                            break;
                        }
                    }
                }

                if (cmd?.Call != null)
                {
                    cmd.Call(uiArg);
                    return;
                }

                Debug.LogWarning("[SkillTree] cui.endtest ST: command not registered: " + cmdName);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkillTree] cui.endtest ST: " + ex.Message);
            }
        }

        private void InvokeConsoleMethod(string methodName, ConsoleSystem.Arg arg)
        {
            var plugin = Plugin;
            if (plugin == null || arg == null) return;
            try
            {
                var mi = typeof(OxidePlugin).GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                mi?.Invoke(plugin, new object[] { arg });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkillTree] " + methodName + ": " + ex.Message);
            }
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler, bool serverAdmin = false)
        {
            bool hasDot  = name.Contains(".");
            string parent  = hasDot ? name.Split('.')[0] : "";
            string cmdName = hasDot ? name.Split(new[] { '.' }, 2)[1] : name;
            string fullName = hasDot ? name : "global." + name;
            string dictKey  = hasDot ? name : fullName;

            var captured = name;
            var cmd = new ConsoleSystem.Command
            {
                Name              = cmdName,
                Parent            = parent,
                FullName          = fullName,
                Variable          = false,
                ServerAdmin       = serverAdmin,
                AllowRunFromServer= true,
                Replicated        = false,
                Call              = a =>
                {
                    try { handler(a); }
                    catch (Exception ex) { Debug.LogWarning("[SkillTree] cmd " + captured + ": " + ex.Message); }
                }
            };

            ConsoleSystem.Index.Server.Dict[dictKey] = cmd;
            if (!hasDot && ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict[cmdName] = cmd;

            _registeredCommands.Add(cmd);
        }

        private void UnregisterConsoleCommands()
        {
            foreach (var cmd in _chatAliasCommands)
            {
                try
                {
                    cmd.Replicated = false;
                    cmd.Variable = false;
                    RemoveFromReplicatedList(cmd);
                }
                catch { }
            }
            _chatAliasCommands.Clear();
            ScrubSkillTreeFromReplicatedList();

            try
            {
                var dict       = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                foreach (var cmd in _registeredCommands)
                {
                    dict?.Remove(cmd.FullName);
                    dict?.Remove(cmd.Parent + "." + cmd.Name);
                    if (string.Equals(cmd.Parent, "global", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(cmd.Parent))
                        globalDict?.Remove(cmd.Name);
                }
            }
            catch { }
            _registeredCommands.Clear();
            _chatCommandNames.Clear();
            _uiConsoleCommands.Clear();
        }
    }
}
