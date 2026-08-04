using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RustVehiclesGUIHarmony
{
    /// <summary>
    /// Harmony entry point for RustVehiclesGUI 1.0.5. Hosts the ported plugin, registers its
    /// [ConsoleCommand] methods and config chat aliases, and exposes Call() so ServerPanel can
    /// reach API_OpenPlugin / OnServerPanelClosed / OnServerPanelCategoryPage.
    /// </summary>
    public class RustVehiclesGUIHarmonyMod : IHarmonyModHooks
    {
        public static RustVehiclesGUIHarmonyMod Instance { get; private set; }

        public const int VersionMajor = 1;
        public const int VersionMinor = 0;
        public const int VersionPatch = 5;

        public static readonly VersionNumber Version = new VersionNumber(VersionMajor, VersionMinor, VersionPatch);

        public const string AppDomainApiKey = "RustVehiclesGUI_ApiType";
        public const string AppDomainPluginKey = "RustVehiclesGUI_Plugin";

        private RustVehiclesGUI _plugin;
        private PluginWrapper _wrapper;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();
        private readonly Dictionary<string, MethodInfo> _chatMethods =
            new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);

        public RustVehiclesGUI Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
            RustVehiclesGUIHost.Init(root);
            PluginBridges.Clear();
            _plugin = new RustVehiclesGUI();
            RustVehiclesGUIHost.Instance.Plugin = _plugin;
            _wrapper = new PluginWrapper(this);
            RegisterApiType();

            try { _plugin.HarmonyInit(); }
            catch (Exception ex) { Debug.LogError("[RustVehiclesGUI Harmony] FAIL: HarmonyInit -> " + ex); }

            RegisterConsoleCommands();
            RegisterChatCommands();
            ScheduleServerInitialized();

            Debug.Log($"[RustVehiclesGUI Harmony] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[RustVehiclesGUI Harmony] Chat aliases from HarmonyConfig/RustVehiclesGUI.json (Chat Commands)");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            UnregisterCommands();
            try { _plugin?.HarmonyUnload(); }
            catch (Exception ex) { Debug.LogWarning("[RustVehiclesGUI Harmony] HarmonyUnload: " + ex.Message); }
            UnregisterApiType();
            RustVehiclesGUIHost.Shutdown();
            PluginBridges.Clear();
            _plugin = null;
            _wrapper = null;
            _chatMethods.Clear();
            Instance = null;
        }

        #region ServerInitialized scheduling

        private void ScheduleServerInitialized(int attempt = 0)
        {
            if (_plugin == null) return;
            try
            {
                bool itemsReady = ItemManager.itemList != null && ItemManager.itemList.Count > 0;
                if (itemsReady && ServerMgr.Instance != null)
                {
                    _plugin.HarmonyServerInitialized();
                    RegisterChatCommands();
                    Debug.Log($"[RustVehiclesGUI Harmony] OK: Server initialized (v{VersionMajor}.{VersionMinor}.{VersionPatch})");
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RustVehiclesGUI Harmony] ScheduleServerInitialized: " + ex.Message);
            }

            if (attempt > 120)
            {
                try
                {
                    _plugin.HarmonyServerInitialized();
                    RegisterChatCommands();
                }
                catch (Exception ex) { Debug.LogError("[RustVehiclesGUI Harmony] FAIL: Init -> " + ex); }
                return;
            }

            float delay = attempt < 10 ? 0.5f : 1f;
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(() => ScheduleServerInitialized(attempt + 1), delay);
            else
            {
                try
                {
                    var go = new GameObject("RustVehiclesGUIHarmony_InitWait");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<InitWaitBehaviour>().Begin(this, attempt);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[RustVehiclesGUI Harmony] ScheduleServerInitialized: " + ex.Message);
                }
            }
        }

        private class InitWaitBehaviour : MonoBehaviour
        {
            private RustVehiclesGUIHarmonyMod _mod;
            private int _attempt;
            public void Begin(RustVehiclesGUIHarmonyMod mod, int attempt)
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
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(RustVehiclesGUIHarmonyMod));
                AppDomain.CurrentDomain.SetData(AppDomainPluginKey, _wrapper);
            }
            catch (Exception ex) { Debug.LogWarning("[RustVehiclesGUI Harmony] RegisterApiType: " + ex.Message); }
        }

        private void UnregisterApiType()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, null);
                AppDomain.CurrentDomain.SetData(AppDomainPluginKey, null);
            }
            catch { }
        }

        /// <summary>Oxide-style Call dispatcher. ServerPanel uses this for API_OpenPlugin.</summary>
        public object Call(string method, params object[] args)
        {
            if (_plugin == null || string.IsNullOrEmpty(method)) return null;
            try
            {
                args ??= Array.Empty<object>();
                var mi = typeof(RustVehiclesGUI)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => string.Equals(m.Name, method, StringComparison.Ordinal) &&
                                         m.GetParameters().Length == args.Length);
                if (mi == null)
                {
                    mi = typeof(RustVehiclesGUI)
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m =>
                        {
                            var attr = m.GetCustomAttribute<HookMethodAttribute>();
                            return attr != null &&
                                   string.Equals(attr.Name, method, StringComparison.Ordinal) &&
                                   m.GetParameters().Length == args.Length;
                        });
                }
                if (mi == null) return null;
                return mi.Invoke(_plugin, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RustVehiclesGUI Harmony] Call({method}): " +
                                 (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }

        /// <summary>Published on the AppDomain so other mods can bridge without a type reference.</summary>
        public sealed class PluginWrapper
        {
            private readonly RustVehiclesGUIHarmonyMod _mod;
            public PluginWrapper(RustVehiclesGUIHarmonyMod mod) => _mod = mod;
            public bool IsLoaded => _mod?._plugin != null;
            public string Name => "RustVehiclesGUI";
            public string Version => $"{VersionMajor}.{VersionMinor}.{VersionPatch}";
            public object Call(string method, params object[] args) => _mod?.Call(method, args);
        }

        #endregion

        #region Console commands

        private void RegisterConsoleCommands()
        {
            if (_plugin == null) return;
            var methods = typeof(RustVehiclesGUI).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var mi in methods)
            {
                var attr = mi.GetCustomAttribute<ConsoleCommandAttribute>();
                if (attr == null || string.IsNullOrEmpty(attr.Command)) continue;
                var method = mi;
                var name = attr.Command;
                RegisterConsole(name, arg =>
                {
                    try { method.Invoke(_plugin, new object[] { arg }); }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[RustVehiclesGUI] console {name}: " + (ex.InnerException?.Message ?? ex.Message));
                    }
                });
            }
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler, bool serverAdmin = false)
        {
            var localName = name;
            bool hasDot = localName.Contains(".");
            string cmdParent = "";
            string cmdName = localName;
            string fullName;
            string dictKey;

            if (hasDot)
            {
                // vgui.serverpanel.manage.nextpage -> parent "vgui", name "serverpanel.manage.nextpage"
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
                    catch (Exception ex) { Debug.LogWarning($"[RustVehiclesGUI] command {localName}: " + ex.Message); }
                }
            };

            ConsoleSystem.Index.Server.Dict[dictKey] = cmd;
            if (!hasDot && ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict[cmdName] = cmd;

            _registeredCommands.Add(cmd);
        }

        private void UnregisterCommands()
        {
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
        }

        /// <summary>Runs a registered vgui.* console command for a player (used by the cui.endtest bridge).</summary>
        internal bool InvokeConsoleCommand(ConsoleSystem.Arg source, string command, string[] args)
        {
            if (string.IsNullOrEmpty(command)) return false;
            var cmd = _registeredCommands.FirstOrDefault(c =>
                string.Equals(c.FullName, command, StringComparison.OrdinalIgnoreCase));
            if (cmd == null) return false;

            var line = new System.Text.StringBuilder(command);
            if (args != null)
            {
                foreach (var a in args)
                {
                    line.Append(' ');
                    var s = a ?? "";
                    if (s.IndexOfAny(new[] { ' ', '"' }) >= 0)
                        line.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
                    else
                        line.Append(s);
                }
            }

            try
            {
                var opt = ConsoleSystem.Option.Server.Quiet();
                if (source?.Connection != null)
                    opt = opt.FromConnection(source.Connection);
                cmd.Call(new ConsoleSystem.Arg(opt, line.ToString()));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RustVehiclesGUI] InvokeConsoleCommand(" + command + "): " + ex.Message);
                return false;
            }
        }

        #endregion

        #region Chat routing

        private void RegisterChatCommands()
        {
            if (_plugin == null) return;

            foreach (var mi in typeof(RustVehiclesGUI).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var attr = mi.GetCustomAttribute<ChatCommandAttribute>();
                if (attr == null || string.IsNullOrEmpty(attr.Command)) continue;
                _chatMethods[attr.Command] = mi;
            }

            var regs = RustVehiclesGUIHost.Instance?.Cmd?.ChatCommands;
            if (regs == null) return;
            foreach (var reg in regs)
            {
                if (string.IsNullOrEmpty(reg.Command) || string.IsNullOrEmpty(reg.MethodName)) continue;
                if (_chatMethods.ContainsKey(reg.Command)) continue;
                var mi = typeof(RustVehiclesGUI).GetMethod(reg.MethodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null)
                    _chatMethods[reg.Command] = mi;
            }
        }

        /// <summary>Routes a chat command to the matching handler. Returns true if handled.</summary>
        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || _plugin == null || string.IsNullOrEmpty(command)) return false;
            if (!_chatMethods.TryGetValue(command, out var mi) || mi == null) return false;

            try
            {
                mi.Invoke(_plugin, new object[] { player, command, args ?? Array.Empty<string>() });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RustVehiclesGUI] chat " + command + ": " + (ex.InnerException?.Message ?? ex.Message));
            }
            return true;
        }

        #endregion
    }
}
