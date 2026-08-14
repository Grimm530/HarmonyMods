using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using OxidePlugin = Oxide.Plugins.ZoneManager;

namespace ZoneManagerHarmony
{
    public class ModRunner : MonoBehaviour
    {
        public static ModRunner Instance { get; private set; }
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static GameObject _go;

        public static void Ensure()
        {
            if (Instance != null) return;
            _go = new GameObject("ZoneManager_Runner");
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
                catch (Exception ex) { Debug.LogWarning("[ZoneManager] NextTick: " + ex.Message); }
            }
        }
    }

    public class ZoneManagerMod : IHarmonyModHooks
    {
        public static ZoneManagerMod Instance { get; private set; }
        public static OxidePlugin Plugin => OxidePlugin.GetModInstance();

        public const string AppDomainApiKey = "ZoneManager_ApiType";
        public const string AppDomainPluginKey = "ZoneManager_Plugin";
        public const string CuiMarker = "ZONEMANAGER";

        public const int VersionMajor = 3;
        public const int VersionMinor = 1;
        public const int VersionPatch = 11;

        private Coroutine _initCoroutine;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _chatMethodMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Action _permissionsReadyCallback;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            ModRunner.Ensure();

            OxidePlugin plugin;
            try
            {
                plugin = new OxidePlugin();
                OxidePlugin.SetInstance(plugin);
                plugin.HarmonyLoadDefaultMessages();
                plugin.HarmonyLoadConfig();
            }
            catch (Exception ex)
            {
                Debug.LogError("[ZoneManager] Failed to construct/config plugin: " + ex);
                return;
            }

            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(ZoneManagerMod));
                AppDomain.CurrentDomain.SetData(AppDomainPluginKey, this);
            }
            catch { }

            ScanChatCommands(plugin);
            RegisterAttributedConsoleCommands();

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            _initCoroutine = ModRunner.Instance.StartCoroutine(WaitForServerThenInit());

            Debug.Log($"[ZoneManager] Harmony mod loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}. Config: HarmonyConfig/ZoneManager.json. Data: HarmonyData/ZoneManager/");
        }

        private void OnPermissionsReady()
        {
            try
            {
                var plugin = Plugin;
                if (plugin == null) return;
                plugin.HarmonyRegisterPermissions();
                plugin.ResolvePluginReferences();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ZoneManager] Permissions ready: " + ex.Message);
            }
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

            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, null);
                AppDomain.CurrentDomain.SetData(AppDomainPluginKey, null);
            }
            catch { }

            ModRunner.Destroy();
            OxidePlugin.ClearInstance();
            Instance = null;
            Debug.Log("[ZoneManager] Harmony mod unloaded.");
        }

        private IEnumerator WaitForServerThenInit(int attempt = 0)
        {
            while (ServerMgr.Instance == null) yield return null;
            while (ItemManager.itemList == null || ItemManager.itemList.Count == 0)
            {
                if (attempt > 120)
                {
                    Debug.LogWarning("[ZoneManager] ItemManager timeout; proceeding.");
                    break;
                }
                yield return new WaitForSeconds(attempt < 10 ? 0.5f : 1f);
                attempt++;
            }

            yield return new WaitForSeconds(1f);

            var plugin = Plugin;
            if (plugin == null) yield break;

            try { plugin.CallInit(); }
            catch (Exception ex) { Debug.LogWarning("[ZoneManager] Init: " + ex.Message); }

            try { plugin.CallOnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[ZoneManager] OnServerInitialized: " + ex); }

            plugin.ResolvePluginReferences();
            _initCoroutine = null;
            Debug.Log("[ZoneManager] Server initialized.");
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || Plugin == null || string.IsNullOrEmpty(command)) return false;
            if (!_chatCommandNames.Contains(command)) return false;
            if (!_chatMethodMap.TryGetValue(command, out string method)) return false;
            InvokeChatMethod(Plugin, method, player, command, args ?? Array.Empty<string>());
            return true;
        }

        /// <summary>TruePVE / SkillTree Plugin.Call entry. Aliases CreateZone → CreateOrUpdateZone.</summary>
        public object Call(string method, params object[] args)
        {
            var plugin = Plugin;
            if (plugin == null || string.IsNullOrEmpty(method)) return null;
            args ??= Array.Empty<object>();
            try
            {
                if (string.Equals(method, "CreateZone", StringComparison.OrdinalIgnoreCase))
                    method = "CreateOrUpdateZone";
                if (string.Equals(method, "erase", StringComparison.OrdinalIgnoreCase))
                    method = "EraseZone";

                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                Type[] types = new Type[args.Length];
                for (int i = 0; i < args.Length; i++)
                    types[i] = args[i]?.GetType() ?? typeof(object);

                var mi = typeof(OxidePlugin).GetMethod(method, bf, null, types, null);
                if (mi == null)
                {
                    foreach (var m in typeof(OxidePlugin).GetMethods(bf))
                    {
                        if (!string.Equals(m.Name, method, StringComparison.OrdinalIgnoreCase)) continue;
                        if (m.GetParameters().Length == args.Length) { mi = m; break; }
                        if (mi == null && m.GetParameters().Length >= args.Length) mi = m;
                    }
                }
                if (mi == null) return null;

                var pars = mi.GetParameters();
                if (pars.Length != args.Length)
                {
                    var adapted = new object[pars.Length];
                    for (int i = 0; i < pars.Length; i++)
                    {
                        if (i < args.Length) adapted[i] = args[i];
                        else adapted[i] = pars[i].HasDefaultValue ? pars[i].DefaultValue
                            : (pars[i].ParameterType.IsValueType ? Activator.CreateInstance(pars[i].ParameterType) : null);
                    }
                    args = adapted;
                }
                return mi.Invoke(plugin, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ZoneManager] Call(" + method + "): " + (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }

        public void HandleCuiEndtest(ConsoleSystem.Arg args, Array a)
        {
            var player = args?.Connection?.player as BasePlayer ?? args?.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return;

            var sb = new StringBuilder();
            for (int i = 1; i < a.Length; i++)
            {
                if (sb.Length > 0) sb.Append(' ');
                string s = a.GetValue(i)?.ToString() ?? string.Empty;
                if (s.IndexOfAny(new[] { ' ', '"' }) >= 0)
                    sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
                else
                    sb.Append(s);
            }

            try
            {
                var opt = ConsoleSystem.Option.Server.Quiet();
                if (args.Connection != null)
                    opt = opt.FromConnection(args.Connection);
                var uiArg = new ConsoleSystem.Arg(opt, sb.ToString());
                Plugin?.HarmonyCcmdEditFlag(uiArg);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ZoneManager] cui.endtest: " + ex);
            }
        }

        private void ScanChatCommands(OxidePlugin plugin)
        {
            _chatCommandNames.Clear();
            _chatMethodMap.Clear();
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var mi in typeof(OxidePlugin).GetMethods(bf))
            {
                var attrs = mi.GetCustomAttributes(typeof(Oxide.Plugins.ChatCommandAttribute), inherit: false);
                if (attrs == null || attrs.Length == 0) continue;
                foreach (Oxide.Plugins.ChatCommandAttribute attr in attrs)
                {
                    if (string.IsNullOrWhiteSpace(attr.Command)) continue;
                    string name = attr.Command.Trim();
                    _chatCommandNames.Add(name);
                    _chatMethodMap[name] = mi.Name;
                    plugin.cmd.AddChatCommand(name, plugin, mi.Name);
                }
            }
        }

        private static void InvokeChatMethod(OxidePlugin plugin, string methodName, BasePlayer player, string command, string[] args)
        {
            if (string.IsNullOrEmpty(methodName) || plugin == null || player == null) return;
            try
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var mi = typeof(OxidePlugin).GetMethod(methodName, bf, null, new[] { typeof(BasePlayer), typeof(string), typeof(string[]) }, null);
                if (mi != null) { mi.Invoke(plugin, new object[] { player, command, args }); return; }

                mi = typeof(OxidePlugin).GetMethod(methodName, bf, null, new[] { typeof(BasePlayer) }, null);
                if (mi != null) { mi.Invoke(plugin, new object[] { player }); return; }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ZoneManager] InvokeChatMethod " + methodName + ": " + ex.Message);
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
                        if (_registeredCommands.Any(c =>
                                string.Equals(c.Name, cmdName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(c.FullName, "global." + cmdName, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        var methodName = mi.Name;
                        RegisterConsole(cmdName, arg => InvokeConsoleMethod(methodName, arg));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ZoneManager] RegisterAttributedConsoleCommands: " + ex.Message);
            }
        }

        private void InvokeConsoleMethod(string methodName, ConsoleSystem.Arg arg)
        {
            var plugin = Plugin;
            if (plugin == null || string.IsNullOrEmpty(methodName) || arg == null) return;
            try
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var mi = typeof(OxidePlugin).GetMethod(methodName, bf, null, new[] { typeof(ConsoleSystem.Arg) }, null);
                mi?.Invoke(plugin, new object[] { arg });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ZoneManager] InvokeConsoleMethod " + methodName + ": " + ex.Message);
            }
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler)
        {
            try
            {
                var cmd = new ConsoleSystem.Command
                {
                    Name = name,
                    FullName = name.IndexOf('.') >= 0 ? name : "global." + name,
                    Variable = false,
                    ServerAdmin = true,
                    ServerUser = true,
                    AllowRunFromServer = true,
                    Call = a => handler(a)
                };
                _registeredCommands.Add(cmd);
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                if (dict != null) dict[cmd.FullName] = cmd;
                if (globalDict != null && name.IndexOf('.') < 0) globalDict[cmd.Name] = cmd;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ZoneManager] RegisterConsole(" + name + "): " + ex.Message);
            }
        }

        private void UnregisterConsoleCommands()
        {
            try
            {
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                foreach (var cmd in _registeredCommands)
                {
                    dict?.Remove(cmd.FullName);
                    globalDict?.Remove(cmd.Name);
                }
            }
            catch { }
            _registeredCommands.Clear();
        }
    }
}
