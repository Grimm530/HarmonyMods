using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Oxide.Core.Libraries.Covalence;
using Oxide.Plugins;
using OxidePlugin = Oxide.Plugins.HitMarkers;

namespace HitMarkersHarmony
{
    public class ModRunner : MonoBehaviour
    {
        public static ModRunner Instance { get; private set; }
        private static GameObject _go;

        public static void Ensure()
        {
            if (Instance != null) return;
            _go = new GameObject("HitMarkers_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _go.hideFlags = HideFlags.HideAndDontSave;
            Instance = _go.AddComponent<ModRunner>();
        }

        public static void Destroy()
        {
            if (_go != null) { UnityEngine.Object.Destroy(_go); _go = null; Instance = null; }
        }
    }

    public class HitMarkersMod : IHarmonyModHooks
    {
        public static HitMarkersMod Instance { get; private set; }
        public static OxidePlugin Plugin => OxidePlugin.GetModInstance();

        public const string AppDomainApiKey = "HitMarkers_ApiType";
        public const string CuiMarker = "HITMARKERS";

        private Coroutine _initCoroutine;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();
        private readonly List<ConsoleSystem.Command> _chatAliasCommands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _uiConsoleCommands = new List<string>();
        private Action _permissionsReadyCallback;

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
                Debug.LogError("[HitMarkers] Failed to construct/config plugin: " + ex);
                return;
            }

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(HitMarkersMod)); }
            catch { }

            RegisterAttributedConsoleCommands();
            RegisterAttributedChatCommands();

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            _initCoroutine = ModRunner.Instance.StartCoroutine(WaitForServerThenInit());
            Debug.Log("[HitMarkers] Harmony mod loaded. Chat: /marker /hits /hit. Config: HarmonyConfig/HitMarkers.json");
        }

        private void OnPermissionsReady()
        {
            try { Plugin?.CallInit(); }
            catch (Exception ex) { Debug.LogWarning("[HitMarkers] Permissions ready: " + ex.Message); }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
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
            Debug.Log("[HitMarkers] Harmony mod unloaded.");
        }

        private IEnumerator WaitForServerThenInit(int attempt = 0)
        {
            while (ServerMgr.Instance == null) yield return null;
            yield return new WaitForSeconds(1f);

            var plugin = Plugin;
            if (plugin == null) yield break;

            try { plugin.CallInit(); }
            catch (Exception ex) { Debug.LogWarning("[HitMarkers] Init: " + ex.Message); }

            try { plugin.CallOnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[HitMarkers] OnServerInitialized: " + ex); }

            RefreshDynamicCommands();
            _initCoroutine = null;
            Debug.Log("[HitMarkers] Server initialized.");
        }

        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrWhiteSpace(message)) return false;
            message = message.Trim();
            if (message.StartsWith("/") || message.StartsWith("\\"))
                message = message.Substring(1).Trim();

            string[] parts = message.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            string commandName = parts[0];
            if (!_chatCommandNames.Contains(commandName)) return false;

            string[] args = parts.Length > 1 ? parts.Skip(1).ToArray() : Array.Empty<string>();
            var plugin = Plugin;
            if (plugin == null) return false;

            foreach (var reg in plugin.cmd.RegisteredChatCommands)
            {
                if (!string.Equals(reg.name, commandName, StringComparison.OrdinalIgnoreCase)) continue;
                InvokeChatMethod(plugin, reg.method, player, commandName, args);
                return true;
            }

            return InvokeAttributedChat(plugin, commandName, player, args);
        }

        private bool InvokeAttributedChat(OxidePlugin plugin, string commandName, BasePlayer player, string[] args)
        {
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var mi in typeof(OxidePlugin).GetMethods(bf))
            {
                foreach (ChatCommandAttribute attr in mi.GetCustomAttributes(typeof(ChatCommandAttribute), false))
                {
                    if (!string.Equals(attr.Command, commandName, StringComparison.OrdinalIgnoreCase)) continue;
                    InvokeMethod(plugin, mi, player, commandName, args);
                    return true;
                }
                foreach (Oxide.Plugins.CommandAttribute attr in mi.GetCustomAttributes(typeof(Oxide.Plugins.CommandAttribute), false))
                {
                    if (!string.Equals(attr.Command, commandName, StringComparison.OrdinalIgnoreCase)) continue;
                    InvokeMethod(plugin, mi, player, commandName, args);
                    return true;
                }
            }
            return false;
        }

        private static void InvokeChatMethod(OxidePlugin plugin, string methodName, BasePlayer player, string command, string[] args)
        {
            if (string.IsNullOrEmpty(methodName) || plugin == null) return;
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var mi = typeof(OxidePlugin).GetMethod(methodName, bf);
            if (mi == null) return;
            InvokeMethod(plugin, mi, player, command, args);
        }

        private static void InvokeMethod(OxidePlugin plugin, MethodInfo mi, BasePlayer player, string command, string[] args)
        {
            try
            {
                var ps = mi.GetParameters();
                if (ps.Length == 3 && ps[0].ParameterType == typeof(IPlayer))
                {
                    mi.Invoke(plugin, new object[] { new BasePlayerWrapper(player), command, args });
                    return;
                }
                if (ps.Length == 3 && ps[0].ParameterType == typeof(BasePlayer))
                {
                    mi.Invoke(plugin, new object[] { player, command, args });
                    return;
                }
                if (ps.Length == 1 && ps[0].ParameterType == typeof(BasePlayer))
                {
                    mi.Invoke(plugin, new object[] { player });
                    return;
                }
                if (ps.Length == 1 && ps[0].ParameterType == typeof(ConsoleSystem.Arg))
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
                Debug.LogWarning("[HitMarkers] Invoke " + mi.Name + ": " + ex);
            }
        }

        private void RegisterAttributedChatCommands()
        {
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var mi in typeof(OxidePlugin).GetMethods(bf))
            {
                foreach (ChatCommandAttribute attr in mi.GetCustomAttributes(typeof(ChatCommandAttribute), false))
                {
                    if (string.IsNullOrWhiteSpace(attr.Command)) continue;
                    _chatCommandNames.Add(attr.Command);
                    RegisterChatAliasConsole(attr.Command);
                }
                foreach (Oxide.Plugins.CommandAttribute attr in mi.GetCustomAttributes(typeof(Oxide.Plugins.CommandAttribute), false))
                {
                    if (string.IsNullOrWhiteSpace(attr.Command)) continue;
                    _chatCommandNames.Add(attr.Command);
                    RegisterChatAliasConsole(attr.Command);
                }
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
                        RegisterConsole(cmdName, arg => InvokeConsoleMethod(methodName, arg));
                    }
                }
                SortUiConsoleCommands();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HitMarkers] RegisterAttributedConsoleCommands: " + ex.Message);
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
            => _uiConsoleCommands.Sort((a, b) => b.Length.CompareTo(a.Length));

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
                            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var captured = reg;
                    RegisterConsole(name, arg => InvokeConsoleMethod(captured.method, arg));
                }
                SortUiConsoleCommands();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HitMarkers] RefreshDynamicCommands: " + ex.Message);
            }
        }

        private void RegisterChatAliasConsole(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOf('.') >= 0) return;
            name = name.Trim();
            if (_chatAliasCommands.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) return;
            if (_registeredCommands.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) return;
            if (ConsoleSystem.Index.Server.Dict != null &&
                ConsoleSystem.Index.Server.Dict.ContainsKey("global." + name))
                return;

            string localName = name;
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
                        var player = a?.Connection?.player as BasePlayer;
                        if (player == null) return;
                        var sb = new StringBuilder(localName);
                        if (a.Args != null)
                        {
                            for (int i = 0; i < a.Args.Length; i++)
                                sb.Append(' ').Append(a.Args[i].ToString());
                        }
                        OnChatCommand(player, sb.ToString());
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[HitMarkers] chat alias " + localName + ": " + ex.Message);
                    }
                }
            };
            try
            {
                ConsoleSystem.Index.Server.Dict["global." + localName] = cmd;
                ConsoleSystem.Index.Server.GlobalDict[localName] = cmd;
                _chatAliasCommands.Add(cmd);
                _registeredCommands.Add(cmd);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HitMarkers] RegisterChatAliasConsole(" + localName + "): " + ex.Message);
            }
        }

        public void HandleCuiEndtest(ConsoleSystem.Arg args, Array a)
        {
            if (Plugin == null || a == null || a.Length < 2) return;
            var player = args.Connection?.player as BasePlayer;
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

            string cmdName = a.GetValue(1)?.ToString() ?? "";
            if (string.IsNullOrEmpty(cmdName)) return;

            try
            {
                var opt = ConsoleSystem.Option.Server.Quiet();
                if (args.Connection != null)
                    opt = opt.FromConnection(args.Connection);
                var uiArg = new ConsoleSystem.Arg(opt, sb.ToString());

                ConsoleSystem.Command cmd = null;
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                if (dict != null && !dict.TryGetValue("global." + cmdName, out cmd))
                    dict.TryGetValue(cmdName, out cmd);
                if (cmd == null)
                    globalDict?.TryGetValue(cmdName, out cmd);
                cmd?.Call?.Invoke(uiArg);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HitMarkers] cui.endtest HITMARKERS: " + ex.Message);
            }
        }

        private void InvokeConsoleMethod(string methodName, ConsoleSystem.Arg arg)
        {
            var plugin = Plugin;
            if (plugin == null || arg == null) return;
            try
            {
                var mi = typeof(OxidePlugin).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                mi?.Invoke(plugin, new object[] { arg });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HitMarkers] " + methodName + ": " + ex.Message);
            }
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler)
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
                ServerAdmin = false,
                AllowRunFromServer = true,
                Replicated = false,
                Call = a =>
                {
                    try { handler(a); }
                    catch (Exception ex) { Debug.LogWarning("[HitMarkers] cmd " + captured + ": " + ex.Message); }
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
                    if (string.IsNullOrEmpty(cmd.Parent) || string.Equals(cmd.Parent, "global", StringComparison.OrdinalIgnoreCase))
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
