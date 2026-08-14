using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyChat;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;
using OxidePlugin = Oxide.Plugins.UberTool;

namespace UberToolHarmony
{
    public class ModRunner : MonoBehaviour
    {
        public static ModRunner Instance { get; private set; }
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static GameObject _go;

        public static void Ensure()
        {
            if (Instance != null) return;
            _go = new GameObject("UberTool_Runner");
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
                catch (Exception ex) { Debug.LogWarning("[UberTool] NextTick: " + ex.Message); }
            }
        }
    }

    public class UberToolMod : IHarmonyModHooks
    {
        public static UberToolMod Instance { get; private set; }
        public static OxidePlugin Plugin => OxidePlugin.GetModInstance();

        private Coroutine _initCoroutine;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();
        private readonly List<ConsoleSystem.Command> _chatAliasCommands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _uiConsoleCommands = new List<string>();
        private Action _permissionsReadyCallback;

        public const string AppDomainApiKey = "UberTool_ApiType";
        public const string CuiMarker = "UBERTOOL";

        public IReadOnlyList<string> UiConsoleCommands => _uiConsoleCommands;

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
                Debug.LogError("[UberTool] Failed to construct/config plugin: " + ex);
                return;
            }

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(UberToolMod)); }
            catch { }

            RegisterAttributedChatCommands();
            ChatSayBridge.Register("UberTool", OnChatCommand);
            RegisterAttributedConsoleCommands();

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            _initCoroutine = ModRunner.Instance.StartCoroutine(WaitForServerThenInit());
            Debug.Log("[UberTool] Harmony mod loaded. Config: HarmonyConfig/UberTool.json");
        }

        private void OnPermissionsReady()
        {
            try
            {
                var plugin = OxidePlugin.GetModInstance();
                if (plugin == null) return;
                plugin.HarmonyResolvePluginReferences();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UberTool] Permissions ready: " + ex.Message);
            }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            ChatSayBridge.Unregister("UberTool");
            if (_permissionsReadyCallback != null)
            {
                PermissionsBridge.UnregisterReadyCallback(_permissionsReadyCallback);
                _permissionsReadyCallback = null;
            }
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
            Debug.Log("[UberTool] Harmony mod unloaded.");
        }

        private IEnumerator WaitForServerThenInit(int attempt = 0)
        {
            while (ServerMgr.Instance == null) yield return null;
            while (ItemManager.itemList == null || ItemManager.itemList.Count == 0)
            {
                if (attempt > 120)
                {
                    Debug.LogWarning("[UberTool] ItemManager timeout; proceeding.");
                    break;
                }
                yield return new WaitForSeconds(attempt < 10 ? 0.5f : 1f);
                attempt++;
            }
            yield return new WaitForSeconds(1f);
            var plugin = Plugin;
            if (plugin == null) yield break;
            try { plugin.HarmonyResolvePluginReferences(); }
            catch (Exception ex) { Debug.LogWarning("[UberTool] ResolvePluginReferences: " + ex.Message); }
            try { plugin.CallInit(); }
            catch (Exception ex) { Debug.LogWarning("[UberTool] Init: " + ex.Message); }
            try { plugin.CallLoaded(); }
            catch (Exception ex) { Debug.LogWarning("[UberTool] Loaded: " + ex.Message); }
            try { plugin.CallOnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[UberTool] OnServerInitialized: " + ex); }
            RefreshDynamicCommands();
            _initCoroutine = null;
            Debug.Log("[UberTool] Server initialized.");
        }

        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrWhiteSpace(message)) return false;
            message = message.Trim();
            if (message.StartsWith("/") || message.StartsWith("\\")) message = message.Substring(1).Trim();
            string[] parts = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            string commandName = parts[0].ToLowerInvariant();
            if (!_chatCommandNames.Contains(commandName)) return false;
            string[] args = parts.Skip(1).ToArray();
            var plugin = Plugin;
            if (plugin == null) return false;
            foreach (var reg in plugin.cmd.RegisteredChatCommands)
            {
                if (!string.Equals(reg.name, commandName, StringComparison.OrdinalIgnoreCase)) continue;
                InvokeChatMethod(plugin, reg.method, player, commandName, args);
                return true;
            }
            InvokeChatMethod(plugin, null, player, commandName, args, attributedOnly: true);
            return true;
        }

        private void RegisterAttributedChatCommands()
        {
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var mi in typeof(OxidePlugin).GetMethods(bf))
            {
                var attrs = mi.GetCustomAttributes(typeof(Oxide.Plugins.ChatCommandAttribute), inherit: false);
                if (attrs == null || attrs.Length == 0) continue;
                foreach (Oxide.Plugins.ChatCommandAttribute attr in attrs)
                {
                    if (string.IsNullOrWhiteSpace(attr.Command)) continue;
                    var cmdName = attr.Command.Trim().ToLowerInvariant();
                    _chatCommandNames.Add(cmdName);
                    Plugin?.cmd.AddChatCommand(cmdName, Plugin, mi.Name);
                    RegisterChatAliasConsole(cmdName);
                }
            }
        }

        private static void InvokeChatMethod(OxidePlugin plugin, string methodName, BasePlayer player, string command, string[] args, bool attributedOnly = false)
        {
            if (plugin == null || player == null) return;
            try
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var type = typeof(OxidePlugin);
                if (string.IsNullOrEmpty(methodName) && attributedOnly)
                {
                    foreach (var miScan in type.GetMethods(bf))
                    {
                        var attrs = miScan.GetCustomAttributes(typeof(Oxide.Plugins.ChatCommandAttribute), false);
                        foreach (Oxide.Plugins.ChatCommandAttribute a in attrs)
                        {
                            if (string.Equals(a.Command, command, StringComparison.OrdinalIgnoreCase))
                            { methodName = miScan.Name; break; }
                        }
                        if (!string.IsNullOrEmpty(methodName)) break;
                    }
                }
                if (string.IsNullOrEmpty(methodName)) return;

                var mi = type.GetMethod(methodName, bf, null, new[] { typeof(BasePlayer), typeof(string), typeof(string[]) }, null);
                if (mi != null) { mi.Invoke(plugin, new object[] { player, command, args }); return; }

                mi = type.GetMethod(methodName, bf, null, new[] { typeof(IPlayer), typeof(string), typeof(string[]) }, null);
                if (mi != null)
                {
                    mi.Invoke(plugin, new object[] { new BasePlayerWrapper(player), command, args });
                    return;
                }

                mi = type.GetMethod(methodName, bf, null, new[] { typeof(BasePlayer) }, null);
                if (mi != null) { mi.Invoke(plugin, new object[] { player }); return; }

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
                Debug.LogWarning("[UberTool] InvokeChatMethod " + methodName + ": " + ex.Message);
            }
        }

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
                Debug.LogWarning("[UberTool] RegisterAttributedConsoleCommands: " + ex.Message);
            }
        }

        private void TrackUiConsoleCommand(string cmdName)
        {
            if (string.IsNullOrEmpty(cmdName)) return;
            for (int i = 0; i < _uiConsoleCommands.Count; i++)
                if (string.Equals(_uiConsoleCommands[i], cmdName, StringComparison.OrdinalIgnoreCase))
                    return;
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
                    if (_registeredCommands.Any(c =>
                            string.Equals(c.FullName, "global." + name, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(c.FullName, name, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var captured = reg;
                    RegisterConsole(name, arg => InvokeConsoleMethod(captured.method, arg), serverAdmin: false);
                }
                SortUiConsoleCommands();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UberTool] RefreshDynamicCommands: " + ex.Message);
            }
        }

        private void RegisterChatAliasConsole(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (name.IndexOf('.') >= 0) return;
            if (_chatAliasCommands.Any(c =>
                    string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.FullName, "global." + name, StringComparison.OrdinalIgnoreCase)))
                return;
            if (_registeredCommands.Any(c =>
                    string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.FullName, "global." + name, StringComparison.OrdinalIgnoreCase)))
                return;
            string localName = name;
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
                        Debug.LogWarning("[UberTool] chat alias " + localName + ": " + ex.Message);
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
                Debug.LogWarning("[UberTool] RegisterChatAliasConsole(" + localName + "): " + ex.Message);
            }
        }

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
                if (cmd?.Call != null)
                {
                    cmd.Call(uiArg);
                    return;
                }
                InvokeConsoleMethodByCommandName(cmdName, uiArg);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UberTool] cui.endtest UBERTOOL: " + ex.Message);
            }
        }

        private void InvokeConsoleMethodByCommandName(string cmdName, ConsoleSystem.Arg arg)
        {
            var plugin = Plugin;
            if (plugin == null) return;
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var mi in typeof(OxidePlugin).GetMethods(bf))
            {
                var attrs = mi.GetCustomAttributes(typeof(Oxide.Plugins.ConsoleCommandAttribute), false);
                foreach (Oxide.Plugins.ConsoleCommandAttribute a in attrs)
                {
                    if (string.Equals(a.Command, cmdName, StringComparison.OrdinalIgnoreCase))
                    {
                        InvokeConsoleMethod(mi.Name, arg);
                        return;
                    }
                }
            }
            foreach (var reg in plugin.cmd.RegisteredConsoleCommands)
            {
                if (string.Equals(reg.name, cmdName, StringComparison.OrdinalIgnoreCase))
                {
                    InvokeConsoleMethod(reg.method, arg);
                    return;
                }
            }
        }

        private void InvokeConsoleMethod(string methodName, ConsoleSystem.Arg arg)
        {
            var plugin = Plugin;
            if (plugin == null || arg == null) return;
            try
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var type = typeof(OxidePlugin);
                var mi = type.GetMethod(methodName, bf, null, new[] { typeof(ConsoleSystem.Arg) }, null);
                if (mi != null) { mi.Invoke(plugin, new object[] { arg }); return; }

                var player = arg.Player();
                string[] args = arg.Args != null ? arg.Args.Select(x => x.ToString()).ToArray() : Array.Empty<string>();
                string cmd = arg.cmd?.Name ?? methodName;

                mi = type.GetMethod(methodName, bf, null, new[] { typeof(BasePlayer), typeof(string), typeof(string[]) }, null);
                if (mi != null && player != null) { mi.Invoke(plugin, new object[] { player, cmd, args }); return; }

                mi = type.GetMethod(methodName, bf, null, new[] { typeof(IPlayer), typeof(string), typeof(string[]) }, null);
                if (mi != null)
                {
                    IPlayer ip = player != null ? (IPlayer)new BasePlayerWrapper(player) : new RustConsolePlayer();
                    mi.Invoke(plugin, new object[] { ip, cmd, args });
                    return;
                }

                mi = type.GetMethod(methodName, bf);
                mi?.Invoke(plugin, new object[] { arg });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UberTool] " + methodName + ": " + (ex.InnerException ?? ex).Message);
            }
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler, bool serverAdmin = false)
        {
            bool hasDot = name.Contains(".");
            string parent = hasDot ? name.Split('.')[0] : "";
            string cmdName = hasDot ? name.Split(new[] { '.' }, 2)[1] : name;
            string fullName = hasDot ? name : "global." + name;
            string dictKey = hasDot ? name : fullName;
            var captured = name;
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
                Call = a =>
                {
                    try { handler(a); }
                    catch (Exception ex) { Debug.LogWarning("[UberTool] cmd " + captured + ": " + ex.Message); }
                }
            };
            ConsoleSystem.Index.Server.Dict[dictKey] = cmd;
            if (!hasDot && ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict[cmdName] = cmd;
            _registeredCommands.Add(cmd);
        }

        private void UnregisterConsoleCommands()
        {
            _chatAliasCommands.Clear();
            try
            {
                var dict = ConsoleSystem.Index.Server.Dict;
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
