using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Oxide.Core.Libraries.Covalence;
using Oxide.Plugins;
using UnityEngine;

namespace WaterBasesHarmony
{
    public class ModRunner : MonoBehaviour
    {
        public static ModRunner Instance { get; private set; }
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static GameObject _go;

        public static void Ensure()
        {
            if (Instance != null) return;
            _go = new GameObject("WaterBases_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _go.hideFlags = HideFlags.HideAndDontSave;
            Instance = _go.AddComponent<ModRunner>();
        }

        public static void DestroyRunner()
        {
            lock (_queue) _queue.Clear();
            if (_go != null)
            {
                UnityEngine.Object.Destroy(_go);
                _go = null;
                Instance = null;
            }
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
                lock (_queue)
                {
                    if (_queue.Count == 0) break;
                    a = _queue.Dequeue();
                }
                try { a(); }
                catch (Exception ex) { Debug.LogWarning("[WaterBases] NextTick: " + ex.Message); }
            }
        }
    }

    public class WaterBasesMod : IHarmonyModHooks
    {
        public static WaterBasesMod Instance { get; private set; }
        public const string AppDomainApiKey = "WaterBases_ApiType";
        public const string CuiMarker = "WB";
        public const int VersionMajor = 1;
        public const int VersionMinor = 0;
        public const int VersionPatch = 26;

        public WaterBases Plugin { get; private set; }

        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MethodInfo> _chatHandlers = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MethodInfo> _consoleHandlers = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _uiConsoleCommands = new List<string>();
        private Action _permissionsReadyCallback;
        private Coroutine _initCoroutine;

        public IReadOnlyList<string> UiConsoleCommands => _uiConsoleCommands;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            ModRunner.Ensure();

            try
            {
                Plugin = new WaterBases();
            }
            catch (Exception ex)
            {
                Debug.LogError("[WaterBases] FAIL: construct: " + ex);
                return;
            }

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(WaterBasesMod)); }
            catch { }

            DiscoverCommands();
            RegisterConsoleCommands();

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            if (ModRunner.Instance != null)
                _initCoroutine = ModRunner.Instance.StartCoroutine(WaitForServerThenInit());

            Debug.Log($"[WaterBases] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[WaterBases] -> Config: HarmonyConfig/WaterBases.json");
            Debug.Log("[WaterBases] -> Lang: HarmonyLanguage/WaterBases.json");
            Debug.Log("[WaterBases] -> Load order: 0Permissions -> WaterBases");
        }

        private IEnumerator WaitForServerThenInit()
        {
            while (ServerMgr.Instance == null)
                yield return null;
            yield return new WaitForSeconds(1f);
            try
            {
                Plugin?.HarmonyOnServerInitialized();
                DiscoverCovalenceCommands();
                RegisterConsoleCommands();
                Debug.Log("[WaterBases] OK: Server initialized.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[WaterBases] FAIL: OnServerInitialized: " + ex);
            }
        }

        private void OnPermissionsReady()
        {
            try
            {
                Plugin?.permission.RegisterPermission(WaterBases.PERM_ADMIN, Plugin);
                Plugin?.permission.RegisterPermission(WaterBases.PERM_VIP1, Plugin);
                Plugin?.permission.RegisterPermission(WaterBases.PERM_VIP2, Plugin);
                Plugin?.permission.RegisterPermission(WaterBases.PERM_VIP3, Plugin);
                Plugin?.permission.RegisterPermission(WaterBases.PERM_VIP4, Plugin);
                Plugin?.permission.RegisterPermission(WaterBases.PERM_VIP5, Plugin);
                Debug.Log("[WaterBases] OK: Permissions ready — perms re-registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WaterBases] FAIL: Permissions ready: " + ex.Message);
            }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            try
            {
                if (_permissionsReadyCallback != null)
                    PermissionsBridge.UnregisterReadyCallback(_permissionsReadyCallback);
            }
            catch { }

            try { Plugin?.HarmonyUnload(); }
            catch (Exception ex) { Debug.LogWarning("[WaterBases] Unload: " + ex.Message); }

            UnregisterConsoleCommands();
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); }
            catch { }

            Plugin = null;
            Instance = null;
            ModRunner.DestroyRunner();
            Debug.Log("[WaterBases] OK: Unloaded.");
        }

        private void DiscoverCommands()
        {
            _chatHandlers.Clear();
            _consoleHandlers.Clear();
            _chatCommandNames.Clear();
            _uiConsoleCommands.Clear();

            foreach (var mi in typeof(WaterBases).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (var attr in mi.GetCustomAttributes(typeof(Oxide.Plugins.ChatCommandAttribute), false).Cast<Oxide.Plugins.ChatCommandAttribute>())
                {
                    if (string.IsNullOrEmpty(attr.Command)) continue;
                    _chatHandlers[attr.Command] = mi;
                    _chatCommandNames.Add(attr.Command);
                }
                foreach (var attr in mi.GetCustomAttributes(typeof(Oxide.Plugins.ConsoleCommandAttribute), false).Cast<Oxide.Plugins.ConsoleCommandAttribute>())
                {
                    if (string.IsNullOrEmpty(attr.Command)) continue;
                    _consoleHandlers[attr.Command] = mi;
                    _uiConsoleCommands.Add(attr.Command);
                }
            }

            _uiConsoleCommands.Sort((a, b) => b.Length.CompareTo(a.Length));
        }

        private void DiscoverCovalenceCommands()
        {
            if (Plugin?.cmd == null) return;
            foreach (var entry in Plugin.cmd.RegisteredChatCommands)
            {
                if (string.IsNullOrEmpty(entry.name) || string.IsNullOrEmpty(entry.method)) continue;
                var mi = typeof(WaterBases).GetMethod(entry.method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null) continue;
                _chatHandlers[entry.name] = mi;
                _chatCommandNames.Add(entry.name);
                if (!_consoleHandlers.ContainsKey(entry.name))
                    _consoleHandlers[entry.name] = mi;
            }
            foreach (var entry in Plugin.cmd.RegisteredConsoleCommands)
            {
                if (string.IsNullOrEmpty(entry.name) || string.IsNullOrEmpty(entry.method)) continue;
                var mi = typeof(WaterBases).GetMethod(entry.method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null) continue;
                if (!_consoleHandlers.ContainsKey(entry.name))
                    _consoleHandlers[entry.name] = mi;
            }
        }

        private void RegisterConsoleCommands()
        {
            UnregisterConsoleCommands();
            foreach (var kv in _consoleHandlers)
                RegisterConsole(kv.Key, kv.Value);
        }

        private void RegisterConsole(string name, MethodInfo handler)
        {
            bool hasDot = name.Contains(".");
            string cmdParent = "";
            string cmdName = name;
            string fullName = hasDot ? name : "global." + name;
            if (hasDot)
            {
                var parts = name.Split(new[] { '.' }, 2);
                cmdParent = parts[0];
                cmdName = parts[1];
            }

            var cmd = new ConsoleSystem.Command
            {
                Name = cmdName,
                Parent = cmdParent,
                FullName = fullName,
                Variable = false,
                ServerAdmin = false,
                ServerUser = true,
                AllowRunFromServer = true,
                Replicated = false,
                Call = a => InvokeConsole(handler, a)
            };

            try
            {
                ConsoleSystem.Index.Server.Dict[fullName] = cmd;
                if (!hasDot && ConsoleSystem.Index.Server.GlobalDict != null)
                    ConsoleSystem.Index.Server.GlobalDict[cmdName] = cmd;
                _registeredCommands.Add(cmd);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WaterBases] RegisterConsole(" + name + "): " + ex.Message);
            }
        }

        private void InvokeConsole(MethodInfo handler, ConsoleSystem.Arg arg)
        {
            if (Plugin == null || handler == null || arg == null) return;
            try
            {
                var ps = handler.GetParameters();
                if (ps.Length == 1 && typeof(ConsoleSystem.Arg).IsAssignableFrom(ps[0].ParameterType))
                {
                    handler.Invoke(Plugin, new object[] { arg });
                    return;
                }
                if (ps.Length == 3 && typeof(IPlayer).IsAssignableFrom(ps[0].ParameterType))
                {
                    var player = arg.Player();
                    IPlayer iplayer = player != null
                        ? new BasePlayerWrapper(player)
                        : (IPlayer)new RustConsolePlayer();
                    string[] args = ArgStrings(arg);
                    handler.Invoke(Plugin, new object[] { iplayer, arg.cmd?.Name ?? "", args });
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WaterBases] console: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        private static string[] ArgStrings(ConsoleSystem.Arg arg)
        {
            try
            {
                var raw = arg.Args;
                if (raw == null || raw.Length == 0) return Array.Empty<string>();
                var args = new string[raw.Length];
                for (int i = 0; i < raw.Length; i++) args[i] = raw[i].ToString() ?? "";
                return args;
            }
            catch { return Array.Empty<string>(); }
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
                    if (string.IsNullOrEmpty(cmd.Parent))
                        globalDict?.Remove(cmd.Name);
                }
            }
            catch { }
            _registeredCommands.Clear();
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || Plugin == null || string.IsNullOrEmpty(command)) return false;
            if (!_chatHandlers.TryGetValue(command, out var mi)) return false;
            try
            {
                var ps = mi.GetParameters();
                if (ps.Length >= 3 && typeof(IPlayer).IsAssignableFrom(ps[0].ParameterType))
                    mi.Invoke(Plugin, new object[] { new BasePlayerWrapper(player), command, args ?? Array.Empty<string>() });
                else
                    mi.Invoke(Plugin, new object[] { player, command, args ?? Array.Empty<string>() });
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WaterBases] chat /" + command + ": " + (ex.InnerException?.Message ?? ex.Message));
                return true;
            }
        }

        public void HandleCuiEndtest(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 2 || Plugin == null) return;
            string cmdName = a[1].ToString();
            if (string.IsNullOrEmpty(cmdName) || !_consoleHandlers.TryGetValue(cmdName, out var handler))
                return;

            var sb = new StringBuilder(cmdName);
            for (int i = 2; i < a.Length; i++)
            {
                sb.Append(' ');
                string s = a[i].ToString() ?? "";
                if (s.IndexOfAny(new[] { ' ', '"' }) >= 0)
                    sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
                else
                    sb.Append(s);
            }

            var opt = ConsoleSystem.Option.Server.Quiet();
            if (args.Connection != null)
                opt = opt.FromConnection(args.Connection);
            InvokeConsole(handler, new ConsoleSystem.Arg(opt, sb.ToString()));
        }

        public static object CheckIfInsideWaterBase(DecayEntity entity)
            => Instance?.Plugin?.CheckIfInsideWaterBase(entity);

        public static bool CheckWaterFoundation(BuildingBlock block)
            => Instance?.Plugin != null && Instance.Plugin.CheckWaterFoundation(block);

        public static object Call(string method, params object[] args)
        {
            var plugin = Instance?.Plugin;
            if (plugin == null || string.IsNullOrEmpty(method)) return null;
            try
            {
                int count = args?.Length ?? 0;
                var mi = typeof(WaterBases).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == method && m.GetParameters().Length == count);
                return mi?.Invoke(plugin, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WaterBases] Call(" + method + "): " + (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }
    }
}
