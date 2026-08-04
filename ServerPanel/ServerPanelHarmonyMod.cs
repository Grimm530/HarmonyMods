using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ServerPanelHarmony
{
    /// <summary>
    /// Harmony entry point for ServerPanel 2.0.20 with ServerPanelPopUps 2.0.20 consolidated into the
    /// same assembly. Hosts both ported plugins, registers their commands and exposes the ServerPanel
    /// API to other Harmony mods through AppDomain data.
    /// </summary>
    public class ServerPanelHarmonyMod : IHarmonyModHooks
    {
        public static ServerPanelHarmonyMod Instance { get; private set; }

        public const int VersionMajor = 2;
        public const int VersionMinor = 0;
        public const int VersionPatch = 20;

        public static readonly VersionNumber Version = new VersionNumber(VersionMajor, VersionMinor, VersionPatch);

        public const string AppDomainApiKey = "ServerPanel_ApiType";
        public const string AppDomainPluginKey = "ServerPanel_Plugin";
        public const string AppDomainPopUpsPluginKey = "ServerPanelPopUps_Plugin";

        private ServerPanel _plugin;
        private ServerPanelPopUps _popUps;
        private ServerPanelPluginWrapper _pluginWrapper;
        private PopUpsPluginWrapper _popUpsWrapper;

        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();

        private readonly Dictionary<string, ChatRoute> _chatCommands =
            new Dictionary<string, ChatRoute>(StringComparer.OrdinalIgnoreCase);

        public ServerPanel Plugin => _plugin;
        public ServerPanelPopUps PopUps => _popUps;

        private enum ChatOwner
        {
            Panel,
            PopUps
        }

        private readonly struct ChatRoute
        {
            public readonly ChatOwner Owner;
            public readonly string Method;

            public ChatRoute(ChatOwner owner, string method)
            {
                Owner = owner;
                Method = method;
            }
        }

        #region Lifecycle

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            ServerPanelHost.Init(root);

            _plugin = new ServerPanel();
            _popUps = new ServerPanelPopUps();
            ServerPanelHost.Instance.Panel = _plugin;
            ServerPanelHost.Instance.PopUps = _popUps;

            _pluginWrapper = new ServerPanelPluginWrapper(this);
            _popUpsWrapper = new PopUpsPluginWrapper(this);
            RegisterApiType();

            try { _plugin.HarmonyInit(); }
            catch (Exception ex) { Debug.LogError("[ServerPanel Harmony] ServerPanel init failed: " + ex); }

            try { _popUps.HarmonyInit(); }
            catch (Exception ex) { Debug.LogError("[ServerPanel Harmony] PopUps init failed: " + ex); }

            ScrubFromReplicatedList();
            RegisterFixedCommands();
            ScheduleServerInitialized();

            Debug.Log($"[ServerPanel Harmony] Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch} (ServerPanel + PopUps)");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            UnregisterCommands();

            try { _popUps?.HarmonyUnload(); }
            catch (Exception ex) { Debug.LogWarning("[ServerPanel Harmony] PopUps unload: " + ex.Message); }

            try { _plugin?.HarmonyUnload(); }
            catch (Exception ex) { Debug.LogWarning("[ServerPanel Harmony] ServerPanel unload: " + ex.Message); }

            UnregisterApiType();
            ServerPanelHost.Shutdown();
            _plugin = null;
            _popUps = null;
            _pluginWrapper = null;
            _popUpsWrapper = null;
            Instance = null;
        }

        private void ScheduleServerInitialized(int attempt = 0)
        {
            if (_plugin == null) return;
            try
            {
                bool itemsReady = ItemManager.itemList != null && ItemManager.itemList.Count > 0;
                if (itemsReady && ServerMgr.Instance != null && attempt >= 2)
                {
                    RunServerInitialized();
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerPanel Harmony] ScheduleServerInitialized: " + ex.Message);
            }

            if (attempt > 120)
            {
                Debug.LogWarning("[ServerPanel Harmony] Timed out waiting for ItemManager; initializing anyway");
                RunServerInitialized();
                return;
            }

            float delay = attempt < 10 ? 0.5f : 1f;
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(() => ScheduleServerInitialized(attempt + 1), delay);
            else
            {
                try
                {
                    var go = new GameObject("ServerPanelHarmony_InitWait");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<InitWaitBehaviour>().Begin(this, attempt);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[ServerPanel Harmony] ScheduleServerInitialized: " + ex.Message);
                }
            }
        }

        private void RunServerInitialized()
        {
            try { _plugin?.HarmonyServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[ServerPanel Harmony] ServerPanel OnServerInitialized: " + ex); }

            try { _popUps?.HarmonyServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[ServerPanel Harmony] PopUps OnServerInitialized: " + ex); }

            RegisterDynamicCommands();
            NotifyConsumersServerPanelReady();

            Debug.Log($"[ServerPanel Harmony] Server initialized. Chat commands: {string.Join(", ", _chatCommands.Keys.OrderBy(k => k))}");
        }

        /// <summary>
        /// Consumers that loaded before ServerPanel keep a null stub until they re-run LoadServerPanel.
        /// Mirror Oxide OnPluginLoaded(ServerPanel) by calling their LoadServerPanel / ProcessCategory path.
        /// </summary>
        private void NotifyConsumersServerPanelReady()
        {
            try
            {
                ServerPanelHost.Instance?.Plugins?.Clear();
                string[] names =
                {
                    "Shop", "Kits", "WipeSchedule", "RustVehicles", "RustVehiclesGUI",
                    "Leaderboard", "RaidableBasesUI", "RaidableBasesBuyableUI"
                };
                for (int i = 0; i < names.Length; i++)
                {
                    try
                    {
                        var plugin = ServerPanelHost.Instance?.Plugins?.Find(names[i]);
                        if (plugin is not { IsLoaded: true }) continue;
                        // Prefer LoadServerPanel (Shop/Kits); else ProcessCategory via our API.
                        if (plugin.Call("LoadServerPanel") == null)
                            Call("API_OnServerPanelProcessCategory", names[i]);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[ServerPanel Harmony] Notify " + names[i] + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerPanel Harmony] NotifyConsumersServerPanelReady: " + ex.Message);
            }
        }

        private class InitWaitBehaviour : MonoBehaviour
        {
            private ServerPanelHarmonyMod _mod;
            private int _attempt;

            public void Begin(ServerPanelHarmonyMod mod, int attempt)
            {
                _mod = mod;
                _attempt = attempt;
                StartCoroutine(Wait());
            }

            private System.Collections.IEnumerator Wait()
            {
                yield return new WaitForSeconds(0.5f);
                var mod = _mod;
                var attempt = _attempt;
                Destroy(gameObject);
                mod?.ScheduleServerInitialized(attempt + 1);
            }
        }

        #endregion

        #region AppDomain API

        private void RegisterApiType()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(ServerPanelHarmonyMod));
                AppDomain.CurrentDomain.SetData(AppDomainPluginKey, _pluginWrapper);
                AppDomain.CurrentDomain.SetData(AppDomainPopUpsPluginKey, _popUpsWrapper);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerPanel Harmony] RegisterApiType: " + ex.Message);
            }
        }

        private void UnregisterApiType()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, null);
                AppDomain.CurrentDomain.SetData(AppDomainPluginKey, null);
                AppDomain.CurrentDomain.SetData(AppDomainPopUpsPluginKey, null);
            }
            catch { }
        }

        /// <summary>Reflective dispatch onto the ServerPanel plugin (API_* surface used by Shop / Kits).</summary>
        public object Call(string method, params object[] args) => CallOn(_plugin, typeof(ServerPanel), method, args);

        public object CallPopUps(string method, params object[] args) =>
            CallOn(_popUps, typeof(ServerPanelPopUps), method, args);

        private static object CallOn(object target, Type type, string method, object[] args)
        {
            if (target == null || string.IsNullOrEmpty(method)) return null;
            try
            {
                int count = args?.Length ?? 0;
                var candidates = type
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == method)
                    .ToArray();

                var mi = candidates.FirstOrDefault(m => m.GetParameters().Length == count)
                         ?? candidates.FirstOrDefault(m =>
                             m.GetParameters().Length >= count &&
                             m.GetParameters().Skip(count).All(p => p.IsOptional));
                if (mi == null) return null;

                var parameters = mi.GetParameters();
                object[] call = args ?? Array.Empty<object>();
                if (parameters.Length != call.Length)
                {
                    var padded = new object[parameters.Length];
                    for (int i = 0; i < parameters.Length; i++)
                        padded[i] = i < call.Length ? call[i] : Type.Missing;
                    call = padded;
                }

                return mi.Invoke(target, call);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ServerPanel Harmony] Call({method}): " + (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }

        #endregion

        #region Commands

        private void RegisterFixedCommands()
        {
            RegisterConsole("UI_ServerPanel", arg => InvokePanel("CmdServerPanel", arg));
            RegisterConsole("UI_ServerPanel_Close", arg => InvokePanel("CmdConsoleServerPanelClose", arg));
            RegisterConsole("UI_ServerPanel_Send_Command", arg => InvokePanel("CmdConsoleServerPanelSendCmd", arg));
            RegisterConsole("serverpanel_broadcastvideo",
                arg => InvokePanel("CmdConsoleServerPanelBroadcastVideo", arg));

            RegisterConsole("UI_ServerPanel_PopUps", arg => InvokePopUps("CmdConsolePopUps", arg));
            RegisterConsole("serverpanelpopups_broadcastvideo", arg => InvokePopUps("CmdBroadcastVideo", arg));

            _chatCommands["popupid"] = new ChatRoute(ChatOwner.PopUps, "CmdOpenPopUpByID");
        }

        /// <summary>
        /// Category commands (ServerPanel data) and pop-up commands (PopUps config) are only known
        /// once both plugins have loaded their data.
        /// </summary>
        private void RegisterDynamicCommands()
        {
            if (_plugin != null)
            {
                try
                {
                    foreach (var entry in _plugin.cmd.RegisteredChatCommands)
                    {
                        if (string.IsNullOrEmpty(entry.name)) continue;
                        _chatCommands[entry.name] = new ChatRoute(ChatOwner.Panel, entry.method);
                    }

                    foreach (var entry in _plugin.cmd.RegisteredConsoleCommands)
                    {
                        if (string.IsNullOrEmpty(entry.name)) continue;
                        var command = entry.name;

                        // Category commands resolve by name, and ConsoleSystem.Arg.RawCommand is not a
                        // reliable source for it here, so dispatch the chat handler with an explicit name.
                        if (_chatCommands.TryGetValue(command, out var chatRoute) &&
                            chatRoute.Owner == ChatOwner.Panel)
                        {
                            var chatMethod = chatRoute.Method;
                            RegisterConsole(command, arg =>
                            {
                                var player = arg?.Player();
                                if (player == null) return;
                                DispatchCovalence(_plugin, typeof(ServerPanel), chatMethod, player, command,
                                    ArgsOf(arg));
                            });
                            continue;
                        }

                        var method = entry.method;
                        RegisterConsole(command, arg => InvokePanel(method, arg));
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[ServerPanel Harmony] Category command registration: " + ex.Message);
                }
            }

            if (_popUps != null)
            {
                try
                {
                    foreach (var entry in _popUps.RegisteredCovalenceCommands)
                    {
                        if (entry.commands == null || string.IsNullOrEmpty(entry.methodName)) continue;
                        foreach (var name in entry.commands)
                        {
                            if (string.IsNullOrEmpty(name)) continue;
                            _chatCommands[name] = new ChatRoute(ChatOwner.PopUps, entry.methodName);
                            var method = entry.methodName;
                            RegisterConsole(name, arg => InvokePopUpsCovalence(method, arg));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[ServerPanel Harmony] PopUp command registration: " + ex.Message);
                }
            }

            ScrubFromReplicatedList();
        }

        private void InvokePanel(string methodName, ConsoleSystem.Arg arg)
        {
            if (_plugin == null || arg == null || string.IsNullOrEmpty(methodName)) return;
            try
            {
                var mi = typeof(ServerPanel).GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                    new[] { typeof(ConsoleSystem.Arg) }, null);
                if (mi == null)
                {
                    Debug.LogWarning("[ServerPanel Harmony] Method not found: " + methodName);
                    return;
                }

                mi.Invoke(_plugin, new object[] { arg });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ServerPanel Harmony] {methodName}: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        private void InvokePopUps(string methodName, ConsoleSystem.Arg arg)
        {
            if (_popUps == null || arg == null || string.IsNullOrEmpty(methodName)) return;
            try
            {
                var mi = typeof(ServerPanelPopUps).GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                    new[] { typeof(ConsoleSystem.Arg) }, null);
                if (mi == null)
                {
                    Debug.LogWarning("[ServerPanel Harmony] PopUps method not found: " + methodName);
                    return;
                }

                mi.Invoke(_popUps, new object[] { arg });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ServerPanel Harmony] PopUps {methodName}: " +
                                 (ex.InnerException?.Message ?? ex.Message));
            }
        }

        /// <summary>Console entry for a pop-up command registered as a covalence command.</summary>
        private void InvokePopUpsCovalence(string methodName, ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null) return;
            var name = arg.cmd?.FullName ?? "";
            int dot = name.IndexOf('.');
            if (dot >= 0) name = name.Substring(dot + 1);
            DispatchCovalence(_popUps, typeof(ServerPanelPopUps), methodName, player, name, ArgsOf(arg));
        }

        private static string[] ArgsOf(ConsoleSystem.Arg arg)
        {
            if (arg?.Args == null) return Array.Empty<string>();
            var result = new string[arg.Args.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = arg.GetString(i);
            return result;
        }

        private static void DispatchCovalence(object target, Type type, string methodName, BasePlayer player,
            string command, string[] args)
        {
            if (target == null || string.IsNullOrEmpty(methodName)) return;
            try
            {
                var mi = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public |
                                                    BindingFlags.NonPublic);
                if (mi == null)
                {
                    Debug.LogWarning("[ServerPanel Harmony] Method not found: " + methodName);
                    return;
                }

                var parameters = mi.GetParameters();
                if (parameters.Length == 3 && parameters[0].ParameterType == typeof(IPlayer))
                    mi.Invoke(target, new object[] { player.ToIPlayer(), command, args ?? Array.Empty<string>() });
                else if (parameters.Length == 3 && parameters[0].ParameterType == typeof(BasePlayer))
                    mi.Invoke(target, new object[] { player, command, args ?? Array.Empty<string>() });
                else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(BasePlayer))
                    mi.Invoke(target, new object[] { player });
                else
                    Debug.LogWarning("[ServerPanel Harmony] Unsupported signature for " + methodName);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ServerPanel Harmony] {methodName}: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler, bool serverAdmin = false)
        {
            if (string.IsNullOrEmpty(name)) return;

            var localName = name;
            bool hasDot = localName.Contains(".");
            string cmdParent = "";
            string cmdName = localName;
            string fullName;
            string dictKey;

            if (hasDot)
            {
                var parts = localName.Split(new[] { '.' }, 2);
                cmdParent = parts[0];
                cmdName = parts[1];
                fullName = localName;
                dictKey = localName;
            }
            else
            {
                fullName = "global." + localName;
                dictKey = fullName;
            }

            if (_registeredCommands.Any(c =>
                    string.Equals(c.FullName, fullName, StringComparison.OrdinalIgnoreCase)))
                return;

            var cmd = new ConsoleSystem.Command
            {
                Name = cmdName,
                Parent = cmdParent,
                FullName = fullName,
                Variable = false,
                ServerAdmin = serverAdmin,
                AllowRunFromServer = true,
                Replicated = false,
                Call = a =>
                {
                    try { handler(a); }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ServerPanel] command {localName}: " + ex.Message);
                    }
                }
            };

            ConsoleSystem.Index.Server.Dict[dictKey] = cmd;
            if (hasDot)
            {
                if (!string.IsNullOrEmpty(fullName) &&
                    !string.Equals(dictKey, fullName, StringComparison.OrdinalIgnoreCase))
                    ConsoleSystem.Index.Server.Dict[fullName] = cmd;
            }
            else if (ConsoleSystem.Index.Server.GlobalDict != null)
            {
                ConsoleSystem.Index.Server.GlobalDict[cmdName] = cmd;
            }

            _registeredCommands.Add(cmd);
        }

        private void UnregisterCommands()
        {
            ScrubFromReplicatedList();
            foreach (var cmd in _registeredCommands)
            {
                try
                {
                    string dictKey = string.IsNullOrEmpty(cmd.Parent) ? "global." + cmd.Name : cmd.FullName;
                    ConsoleSystem.Index.Server.Dict?.Remove(dictKey);
                    if (string.IsNullOrEmpty(cmd.Parent))
                        ConsoleSystem.Index.Server.GlobalDict?.Remove(cmd.Name);
                }
                catch { }
            }

            _registeredCommands.Clear();
            _chatCommands.Clear();
        }

        /// <summary>Keep ServerPanel commands out of the replicated ConVar list sent to clients.</summary>
        private void ScrubFromReplicatedList()
        {
            try
            {
                var replicated = typeof(ConsoleSystem.Index.Server)
                        .GetField("Replicated", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                    as System.Collections.IList;
                if (replicated == null)
                {
                    replicated = typeof(ConsoleSystem.Index.Server)
                            .GetProperty("Replicated", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                        as System.Collections.IList;
                }

                if (replicated == null) return;

                var owned = new HashSet<string>(_registeredCommands.Select(c => c.FullName ?? ""),
                    StringComparer.OrdinalIgnoreCase);

                for (int i = replicated.Count - 1; i >= 0; i--)
                {
                    if (replicated[i] is not ConsoleSystem.Command cmd) continue;
                    string full = cmd.FullName ?? string.Empty;
                    string name = cmd.Name ?? string.Empty;

                    bool isOurs = owned.Contains(full) ||
                                  full.StartsWith("global.UI_ServerPanel", StringComparison.OrdinalIgnoreCase) ||
                                  name.StartsWith("UI_ServerPanel", StringComparison.OrdinalIgnoreCase) ||
                                  name.StartsWith("serverpanel", StringComparison.OrdinalIgnoreCase);

                    if (!isOurs) continue;
                    cmd.Replicated = false;
                    cmd.Variable = false;
                    replicated.RemoveAt(i);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerPanel] ScrubFromReplicatedList: " + ex.Message);
            }
        }

        /// <summary>Chat entry point, called from the ConVar.Chat.say prefix.</summary>
        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrWhiteSpace(message)) return false;
            message = message.Trim();
            if (message.StartsWith("/")) message = message.Substring(1).Trim();
            var parts = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            string name = parts[0];
            if (!_chatCommands.TryGetValue(name, out var route)) return false;

            var args = parts.Skip(1).ToArray();
            if (route.Owner == ChatOwner.Panel)
                DispatchCovalence(_plugin, typeof(ServerPanel), route.Method, player, name, args);
            else
                DispatchCovalence(_popUps, typeof(ServerPanelPopUps), route.Method, player, name, args);
            return true;
        }

        /// <summary>Rebuild a console command line from a cui.endtest payload and run it on a plugin.</summary>
        internal void HandleCuiMarker(ConsoleSystem.Arg args, Array a, string command, bool popUps)
        {
            if (a == null || a.Length < 1) return;
            var player = args?.Connection?.player as BasePlayer ?? args?.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return;

            var sb = new StringBuilder(command);
            for (int i = 1; i < a.Length; i++)
            {
                sb.Append(' ');
                string s = a.GetValue(i)?.ToString() ?? string.Empty;
                if (s.IndexOfAny(new[] { ' ', '"' }) >= 0)
                    sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
                else
                    sb.Append(s);
            }

            try
            {
                var opt = ConsoleSystem.Option.Server.Quiet();
                if (args?.Connection != null)
                    opt = opt.FromConnection(args.Connection);
                var uiArg = new ConsoleSystem.Arg(opt, sb.ToString());

                switch (command)
                {
                    case "UI_ServerPanel":
                        InvokePanel("CmdServerPanel", uiArg);
                        break;
                    case "UI_ServerPanel_Close":
                        InvokePanel("CmdConsoleServerPanelClose", uiArg);
                        break;
                    case "UI_ServerPanel_Send_Command":
                        InvokePanel("CmdConsoleServerPanelSendCmd", uiArg);
                        break;
                    case "serverpanel_broadcastvideo":
                        InvokePanel("CmdConsoleServerPanelBroadcastVideo", uiArg);
                        break;
                    case "UI_ServerPanel_PopUps":
                        InvokePopUps("CmdConsolePopUps", uiArg);
                        break;
                    case "serverpanelpopups_broadcastvideo":
                        InvokePopUps("CmdBroadcastVideo", uiArg);
                        break;
                    default:
                        Debug.LogWarning("[ServerPanel] Unmapped cui marker command: " + command);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerPanel] cui.endtest " + command + ": " + ex);
            }
        }

        #endregion

        #region Player hooks (called from patches)

        public void OnPlayerConnected(BasePlayer player)
        {
            if (_plugin == null || player == null) return;
            try { _plugin.OnPlayerConnected(player); }
            catch (Exception ex) { Debug.LogWarning("[ServerPanel] OnPlayerConnected: " + ex.Message); }
        }

        public void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;
            try { _plugin?.OnPlayerDisconnected(player); }
            catch (Exception ex) { Debug.LogWarning("[ServerPanel] OnPlayerDisconnected: " + ex.Message); }

            try { _popUps?.OnPlayerDisconnected(player); }
            catch (Exception ex) { Debug.LogWarning("[ServerPanel] PopUps OnPlayerDisconnected: " + ex.Message); }
        }

        #endregion

        #region API wrappers exposed through AppDomain

        /// <summary>
        /// Consumer-facing wrapper (AppDomain key ServerPanel_Plugin). Mirrors the Oxide Plugin surface
        /// that Shop / Kits / WipeSchedule call: IsLoaded plus Call(string, object[]).
        /// </summary>
        public sealed class ServerPanelPluginWrapper
        {
            private readonly ServerPanelHarmonyMod _mod;
            public ServerPanelPluginWrapper(ServerPanelHarmonyMod mod) => _mod = mod;

            public bool IsLoaded => _mod?._plugin != null;
            public string Name => "ServerPanel";
            public string Version => $"{VersionMajor}.{VersionMinor}.{VersionPatch}";

            public object Call(string method, params object[] args) => _mod?.Call(method, args);

            // Convenience direct entry points for strongly typed consumers.
            public void API_OnServerPanelProcessCategory(string pluginName) =>
                _mod?.Call("API_OnServerPanelProcessCategory", pluginName);

            public void API_OnServerPanelOpenCategoryByID(BasePlayer player, int categoryId) =>
                _mod?.Call("API_OnServerPanelOpenCategoryByID", player, categoryId);

            public void API_OnServerPanelCallClose(BasePlayer player) =>
                _mod?.Call("API_OnServerPanelCallClose", player);

            public void API_OnServerPanelClosed(BasePlayer player) =>
                _mod?.Call("API_OnServerPanelClosed", player);

            public object API_OnServerPanelGetCategoryInfo(string pluginName) =>
                _mod?.Call("API_OnServerPanelGetCategoryInfo", pluginName);

            public object API_OnServerPanelGetPagedCategoryInfo(string pluginName) =>
                _mod?.Call("API_OnServerPanelGetPagedCategoryInfo", pluginName);

            public string API_GetBackgroundParentLayer() =>
                _mod?.Call("API_GetBackgroundParentLayer") as string;

            public string API_GetCurrentTemplate() => _mod?.Call("API_GetCurrentTemplate") as string;

            public object API_GetEditorPosition(BasePlayer player) => _mod?.Call("API_GetEditorPosition", player);

            public object API_GetEditorShowStatus(BasePlayer player) => _mod?.Call("API_GetEditorShowStatus", player);

            public object API_OnServerPanelEditorGetPosition(BasePlayer player) =>
                _mod?.Call("API_OnServerPanelEditorGetPosition", player);
        }

        public sealed class PopUpsPluginWrapper
        {
            private readonly ServerPanelHarmonyMod _mod;
            public PopUpsPluginWrapper(ServerPanelHarmonyMod mod) => _mod = mod;

            public bool IsLoaded => _mod?._popUps != null;
            public string Name => "ServerPanelPopUps";
            public string Version => $"{VersionMajor}.{VersionMinor}.{VersionPatch}";

            public object Call(string method, params object[] args) => _mod?.CallPopUps(method, args);
        }

        #endregion
    }
}
