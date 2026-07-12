using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace RustRewardsHarmony
{
    /// <summary>
    /// Harmony entry point for RustRewards 3.2.5. Hosts the ported plugin and registers commands.
    /// </summary>
    public class RustRewardsHarmonyMod : IHarmonyModHooks
    {
        public static RustRewardsHarmonyMod Instance { get; private set; }

        public const int VersionMajor = 3;
        public const int VersionMinor = 2;
        public const int VersionPatch = 5;

        public static readonly VersionNumber Version = new VersionNumber(VersionMajor, VersionMinor, VersionPatch);

        public const string AppDomainApiKey = "RustRewards_ApiType";
        public const string AppDomainPluginKey = "RustRewards_Plugin";

        private RustRewards _plugin;
        private RustRewardsPluginWrapper _pluginWrapper;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _commandMethodMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public RustRewards Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            RustRewardsHost.Init(root);
            _plugin = new RustRewards();
            RustRewardsHost.Instance.Plugin = _plugin;
            _pluginWrapper = new RustRewardsPluginWrapper(this);
            RegisterApiType();
            _plugin.HarmonyInit();
            RegisterCommands();
            ScheduleServerInitialized();
            Debug.Log($"[GrimmRewards Harmony] Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[GrimmRewards Harmony] Chat: /rr or MainCommandAlias from HarmonyConfig/RustRewards.json");
        }

        private void ScheduleServerInitialized(int attempt = 0)
        {
            if (_plugin == null) return;
            try
            {
                bool itemsReady = ItemManager.itemList != null && ItemManager.itemList.Count > 0;
                if (itemsReady && ServerMgr.Instance != null && attempt >= 2)
                {
                    _plugin.HarmonyServerInitialized();
                    RefreshChatCommandsFromConfig();
                    Debug.Log($"[GrimmRewards Harmony] Server initialized (v{VersionMajor}.{VersionMinor}.{VersionPatch})");
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmRewards Harmony] ScheduleServerInitialized: " + ex.Message);
            }

            if (attempt > 120)
            {
                Debug.LogWarning("[GrimmRewards Harmony] Timed out waiting for ItemManager; initializing anyway");
                try
                {
                    _plugin.HarmonyServerInitialized();
                    RefreshChatCommandsFromConfig();
                }
                catch (Exception ex) { Debug.LogError("[GrimmRewards Harmony] Init failed: " + ex); }
                return;
            }

            float delay = attempt < 10 ? 0.5f : 1f;
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(() => ScheduleServerInitialized(attempt + 1), delay);
            else
            {
                try
                {
                    var go = new GameObject("RustRewardsHarmony_InitWait");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<InitWaitBehaviour>().Begin(this, attempt);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[GrimmRewards Harmony] ScheduleServerInitialized: " + ex.Message);
                }
            }
        }

        private class InitWaitBehaviour : MonoBehaviour
        {
            private RustRewardsHarmonyMod _mod;
            private int _attempt;
            public void Begin(RustRewardsHarmonyMod mod, int attempt)
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

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            UnregisterCommands();
            _plugin?.HarmonyUnload();
            UnregisterApiType();
            RustRewardsHost.Shutdown();
            _plugin = null;
            _pluginWrapper = null;
            Instance = null;
        }

        private void RegisterApiType()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(RustRewardsHarmonyMod));
                AppDomain.CurrentDomain.SetData(AppDomainPluginKey, _pluginWrapper);
            }
            catch (Exception ex) { Debug.LogWarning("[GrimmRewards Harmony] RegisterApiType: " + ex.Message); }
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

        /// <summary>Oxide-style Call surface (IsBotReSpawn, GetNPCType, IsNight, HappyHour, etc.).</summary>
        public object Call(string method, params object[] args)
        {
            if (_plugin == null || string.IsNullOrEmpty(method)) return null;
            try
            {
                var mi = typeof(RustRewards).GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null) return null;
                var pars = mi.GetParameters();
                if (args == null) args = Array.Empty<object>();
                if (pars.Length == args.Length)
                    return mi.Invoke(_plugin, args);
                if (pars.Length > args.Length)
                {
                    var full = new object[pars.Length];
                    for (int i = 0; i < args.Length; i++) full[i] = args[i];
                    for (int i = args.Length; i < pars.Length; i++)
                        full[i] = pars[i].HasDefaultValue ? pars[i].DefaultValue
                            : (pars[i].ParameterType.IsValueType ? Activator.CreateInstance(pars[i].ParameterType) : null);
                    return mi.Invoke(_plugin, full);
                }
                return mi.Invoke(_plugin, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GrimmRewards Harmony] Call({method}): " + ex.Message);
                return null;
            }
        }

        // ---- Commands ----

        private void RegisterCommands()
        {
            // UI console commands (also reached via cui.endtest RR)
            RegisterConsole("CloseRR", arg => InvokeConsoleMethod(nameof(RustRewards.CloseRR), arg));
            RegisterConsole("RRChangePref", arg => InvokeConsoleMethod(nameof(RustRewards.RRChangePref), arg));
            RegisterConsole("RRChangePos", arg => InvokeConsoleMethod(nameof(RustRewards.RRChangePos), arg));
            RegisterConsole("RRChangeType", arg => InvokeConsoleMethod(nameof(RustRewards.RRChangeType), arg));
            RegisterConsole("rrv", arg => InvokeConsoleMethod(nameof(RustRewards.rrv), arg));
            RegisterConsole("RRUI", arg => InvokeConsoleMethod(nameof(RustRewards.RRUI), arg));
            RegisterConsole("RRChangeNum", arg => InvokeConsoleMethod(nameof(RustRewards.RRChangeNum), arg));
            RegisterConsole("RRChangeAll", arg => InvokeConsoleMethod(nameof(RustRewards.RRChangeAll), arg));
            RegisterConsole("RRChangeMult", arg => InvokeConsoleMethod(nameof(RustRewards.RRChangeMult), arg));
            RegisterConsole("RRChangeAllMult", arg => InvokeConsoleMethod(nameof(RustRewards.RRChangeAllMult), arg));
            RegisterConsole("RRZone", arg => InvokeConsoleMethod(nameof(RustRewards.RRZone), arg));
            RegisterConsole("RRChangeZoneMult", arg => InvokeConsoleMethod(nameof(RustRewards.RRChangeZoneMult), arg));
            RegisterConsole("RRChangeAllZoneMult", arg => InvokeConsoleMethod(nameof(RustRewards.RRChangeAllZoneMult), arg));

            RegisterConsole("rustrewards.wipesummary", arg => InvokeConsoleMethod(nameof(RustRewards.CmdRustRewardsWipeSummary), arg), serverAdmin: true);
            RegisterConsole("rustrewards.setwipebaseline", arg => InvokeConsoleMethod(nameof(RustRewards.CmdRustRewardsSetWipeBaseline), arg), serverAdmin: true);
            RegisterConsole("rustrewards.sendreport", arg => InvokeConsoleMethod(nameof(RustRewards.CmdSendDiscordReport), arg), serverAdmin: true);

            // Default chat aliases until config MainCommandAlias is known
            foreach (var name in new[] { "rr", "rustrewards", "GrimmRewards" })
            {
                _chatCommandNames.Add(name);
                _commandMethodMap[name] = nameof(RustRewards.RustRewardsUI);
            }
        }

        private void RefreshChatCommandsFromConfig()
        {
            if (_plugin == null) return;
            try
            {
                foreach (var entry in _plugin.cmd.RegisteredChatCommands)
                {
                    if (string.IsNullOrEmpty(entry.name) || string.IsNullOrEmpty(entry.method)) continue;
                    _chatCommandNames.Add(entry.name);
                    _commandMethodMap[entry.name] = entry.method;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmRewards Harmony] RefreshChatCommandsFromConfig: " + ex.Message);
            }

            _chatCommandNames.Add("rr");
            _chatCommandNames.Add("rustrewards");
            if (!_commandMethodMap.ContainsKey("rr"))
                _commandMethodMap["rr"] = nameof(RustRewards.RustRewardsUI);
            if (!_commandMethodMap.ContainsKey("rustrewards"))
                _commandMethodMap["rustrewards"] = nameof(RustRewards.RustRewardsUI);

            // Config MainCommandAlias (e.g. GrimmRewards) — also registered via cmd.AddChatCommand during OnServerInitialized
            try
            {
                var confField = typeof(RustRewards).GetField("conf", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var conf = confField?.GetValue(_plugin);
                var settings = conf?.GetType().GetProperty("Settings")?.GetValue(conf)
                               ?? conf?.GetType().GetField("Settings")?.GetValue(conf);
                var ui = settings?.GetType().GetProperty("UI")?.GetValue(settings)
                         ?? settings?.GetType().GetField("UI")?.GetValue(settings);
                var alias = ui?.GetType().GetProperty("MainCommandAlias")?.GetValue(ui)?.ToString()
                            ?? ui?.GetType().GetField("MainCommandAlias")?.GetValue(ui)?.ToString();
                if (!string.IsNullOrEmpty(alias))
                {
                    _chatCommandNames.Add(alias);
                    _commandMethodMap[alias] = nameof(RustRewards.RustRewardsUI);
                }
            }
            catch { }
        }

        private void InvokeConsoleMethod(string methodName, ConsoleSystem.Arg arg)
        {
            if (_plugin == null || arg == null || string.IsNullOrEmpty(methodName)) return;
            try
            {
                var mi = typeof(RustRewards).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null)
                {
                    Debug.LogWarning($"[GrimmRewards Harmony] Method not found: {methodName}");
                    return;
                }
                mi.Invoke(_plugin, new object[] { arg });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GrimmRewards Harmony] {methodName}: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        /// <summary>Route cui.endtest RR &lt;cmd&gt; … to the matching console handler.</summary>
        public void HandleCuiEndtest(ConsoleSystem.Arg args, Array a)
        {
            if (_plugin == null || a == null || a.Length < 2) return;
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
            string method = MapUiCommandToMethod(cmdName);
            if (string.IsNullOrEmpty(method))
            {
                Debug.LogWarning("[GrimmRewards] Unknown cui.endtest RR command: " + cmdName);
                return;
            }

            try
            {
                var opt = ConsoleSystem.Option.Server.Quiet();
                if (args.Connection != null)
                    opt = opt.FromConnection(args.Connection);
                InvokeConsoleMethod(method, new ConsoleSystem.Arg(opt, full));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmRewards] cui.endtest RR: " + ex.Message);
            }
        }

        private static string MapUiCommandToMethod(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return null;
            switch (cmd)
            {
                case "CloseRR": return nameof(RustRewards.CloseRR);
                case "RRChangePref": return nameof(RustRewards.RRChangePref);
                case "RRChangePos": return nameof(RustRewards.RRChangePos);
                case "RRChangeType": return nameof(RustRewards.RRChangeType);
                case "rrv": return nameof(RustRewards.rrv);
                case "RRUI": return nameof(RustRewards.RRUI);
                case "RRChangeNum": return nameof(RustRewards.RRChangeNum);
                case "RRChangeAll": return nameof(RustRewards.RRChangeAll);
                case "RRChangeMult": return nameof(RustRewards.RRChangeMult);
                case "RRChangeAllMult": return nameof(RustRewards.RRChangeAllMult);
                case "RRZone": return nameof(RustRewards.RRZone);
                case "RRChangeZoneMult": return nameof(RustRewards.RRChangeZoneMult);
                case "RRChangeAllZoneMult": return nameof(RustRewards.RRChangeAllZoneMult);
                default: return null;
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
                    catch (Exception ex) { Debug.LogWarning($"[GrimmRewards] command {localName}: " + ex.Message); }
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
            _chatCommandNames.Clear();
            _commandMethodMap.Clear();
        }

        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || _plugin == null || string.IsNullOrWhiteSpace(message)) return false;
            message = message.Trim();
            if (message.StartsWith("/")) message = message.Substring(1).Trim();
            string[] parts = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            string name = parts[0];
            if (!_chatCommandNames.Contains(name)) return false;

            var args = parts.Skip(1).ToArray();
            if (!_commandMethodMap.TryGetValue(name, out var methodName))
                methodName = nameof(RustRewards.RustRewardsUI);

            try
            {
                var mi = typeof(RustRewards).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null) return false;
                var parameters = mi.GetParameters();
                if (parameters.Length == 3 && parameters[0].ParameterType == typeof(BasePlayer))
                    mi.Invoke(_plugin, new object[] { player, name, args });
                else
                    mi.Invoke(_plugin, new object[] { player, name, args });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GrimmRewards] chat {name}: " + (ex.InnerException?.Message ?? ex.Message));
            }
            return true;
        }

        /// <summary>Plugin-shaped wrapper for AppDomain consumers (IsLoaded + Call).</summary>
        public sealed class RustRewardsPluginWrapper
        {
            private readonly RustRewardsHarmonyMod _mod;
            public RustRewardsPluginWrapper(RustRewardsHarmonyMod mod) => _mod = mod;
            public bool IsLoaded => _mod?._plugin != null;
            public string Name => "GrimmRewards";
            public string Version => $"{VersionMajor}.{VersionMinor}.{VersionPatch}";
            public object Call(string method, params object[] args) => _mod?.Call(method, args);
        }
    }
}
