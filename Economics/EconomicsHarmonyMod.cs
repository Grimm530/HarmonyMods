using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace EconomicsHarmony
{
    /// <summary>
    /// Harmony entry point for Economics Extended 3.10.4.
    /// Exposes Balance/Deposit/SetBalance/Transfer/Withdraw via AppDomain + Call dispatcher.
    /// </summary>
    public class EconomicsHarmonyMod : IHarmonyModHooks
    {
        public static EconomicsHarmonyMod Instance { get; private set; }

        public const int VersionMajor = 3;
        public const int VersionMinor = 10;
        public const int VersionPatch = 4;

        public static readonly VersionNumber Version = new VersionNumber(VersionMajor, VersionMinor, VersionPatch);

        public const string AppDomainApiKey = "Economics_ApiType";
        public const string AppDomainPluginKey = "Economics_Plugin";

        private Economics _plugin;
        private EconomicsPluginWrapper _pluginWrapper;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _commandMethodMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Economics Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            EconomicsHost.Init(root);
            _plugin = new Economics();
            EconomicsHost.Instance.Plugin = _plugin;
            _pluginWrapper = new EconomicsPluginWrapper(this);
            RegisterApiType();
            _plugin.HarmonyInit();
            RegisterCommandsFromPlugin();
            RegisterBuiltinConsoleCommands();
            ScheduleServerInitialized();
            Debug.Log($"[Economics Harmony] Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
        }

        private void ScheduleServerInitialized(int attempt = 0)
        {
            if (_plugin == null) return;
            try
            {
                if (ServerMgr.Instance != null && attempt >= 2)
                {
                    _plugin.HarmonyServerInitialized();
                    Debug.Log($"[Economics Harmony] Server initialized (v{VersionMajor}.{VersionMinor}.{VersionPatch})");
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Economics Harmony] ScheduleServerInitialized: " + ex.Message);
            }

            if (attempt > 60)
            {
                try { _plugin.HarmonyServerInitialized(); }
                catch (Exception ex) { Debug.LogError("[Economics Harmony] Init failed: " + ex); }
                return;
            }

            float delay = 0.5f;
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(() => ScheduleServerInitialized(attempt + 1), delay);
            else
            {
                try
                {
                    var go = new GameObject("EconomicsHarmony_InitWait");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<InitWaitBehaviour>().Begin(this, attempt);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Economics Harmony] ScheduleServerInitialized: " + ex.Message);
                }
            }
        }

        private class InitWaitBehaviour : MonoBehaviour
        {
            private EconomicsHarmonyMod _mod;
            private int _attempt;
            public void Begin(EconomicsHarmonyMod mod, int attempt)
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
            EconomicsHost.Shutdown();
            _plugin = null;
            _pluginWrapper = null;
            Instance = null;
        }

        private void RegisterApiType()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(EconomicsHarmonyMod));
                AppDomain.CurrentDomain.SetData(AppDomainPluginKey, _pluginWrapper);
            }
            catch (Exception ex) { Debug.LogWarning("[Economics Harmony] RegisterApiType: " + ex.Message); }
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

        // ---- Static API (Oxide Call surface) ----

        public static double Balance(string playerId) => Instance?._plugin?.Balance(playerId) ?? 0d;
        public static double Balance(object playerId) => Instance?._plugin?.Balance(playerId) ?? 0d;
        public static bool Deposit(string playerId, double amount) => Instance?._plugin != null && Instance._plugin.Deposit(playerId, amount);
        public static bool Deposit(object playerId, double amount) => Instance?._plugin != null && Instance._plugin.Deposit(playerId, amount);
        public static bool SetBalance(string playerId, double amount) => Instance?._plugin != null && Instance._plugin.SetBalance(playerId, amount);
        public static bool SetBalance(object playerId, double amount) => Instance?._plugin != null && Instance._plugin.SetBalance(playerId, amount);
        public static bool Transfer(string playerId, string targetId, double amount) =>
            Instance?._plugin != null && Instance._plugin.Transfer(playerId, targetId, amount);
        public static bool Transfer(object playerId, ulong targetId, double amount) =>
            Instance?._plugin != null && Instance._plugin.Transfer(playerId, targetId, amount);
        public static bool Withdraw(string playerId, double amount) => Instance?._plugin != null && Instance._plugin.Withdraw(playerId, amount);
        public static bool Withdraw(object playerId, double amount) => Instance?._plugin != null && Instance._plugin.Withdraw(playerId, amount);

        /// <summary>RaidableBases-style Economics.Call("Deposit", playerId, amount) dispatcher.</summary>
        public object Call(string method, params object[] args)
        {
            if (_plugin == null || string.IsNullOrEmpty(method)) return null;
            try
            {
                switch (method)
                {
                    case "Balance":
                        if (args == null || args.Length < 1) return 0d;
                        return args[0] is string sBal ? _plugin.Balance(sBal) : _plugin.Balance(args[0]);
                    case "Deposit":
                        if (args == null || args.Length < 2) return false;
                        double depAmt = Convert.ToDouble(args[1]);
                        return args[0] is string sDep ? _plugin.Deposit(sDep, depAmt) : _plugin.Deposit(args[0], depAmt);
                    case "SetBalance":
                        if (args == null || args.Length < 2) return false;
                        double setAmt = Convert.ToDouble(args[1]);
                        return args[0] is string sSet ? _plugin.SetBalance(sSet, setAmt) : _plugin.SetBalance(args[0], setAmt);
                    case "Withdraw":
                        if (args == null || args.Length < 2) return false;
                        double wAmt = Convert.ToDouble(args[1]);
                        return args[0] is string sW ? _plugin.Withdraw(sW, wAmt) : _plugin.Withdraw(args[0], wAmt);
                    case "Transfer":
                        if (args == null || args.Length < 3) return false;
                        double tAmt = Convert.ToDouble(args[2]);
                        if (args[0] is string sFrom && args[1] is string sTo)
                            return _plugin.Transfer(sFrom, sTo, tAmt);
                        if (args[1] is ulong tUid)
                            return _plugin.Transfer(args[0], tUid, tAmt);
                        return _plugin.Transfer(GetId(args[0]), GetId(args[1]), tAmt);
                    default:
                        var mi = typeof(Economics).GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (mi == null) return null;
                        return mi.Invoke(_plugin, args);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Economics Harmony] Call({method}): " + ex.Message);
                return null;
            }
        }

        private static string GetId(object o)
        {
            if (o == null) return "";
            if (o is string s) return s;
            if (o is ulong u) return u.ToString();
            if (o is BasePlayer bp) return bp.UserIDString;
            return o.ToString();
        }

        // ---- Commands ----

        private void RegisterCommandsFromPlugin()
        {
            if (_plugin == null) return;
            foreach (var entry in _plugin.RegisteredCovalenceCommands)
            {
                if (entry.commands == null || string.IsNullOrEmpty(entry.methodName)) continue;
                foreach (var cmd in entry.commands)
                {
                    if (string.IsNullOrEmpty(cmd)) continue;
                    _chatCommandNames.Add(cmd);
                    _commandMethodMap[cmd] = entry.methodName;
                    RegisterConsole(cmd, arg => DispatchCovalenceCommand(entry.methodName, arg), serverAdmin: false);
                }
            }
        }

        private void RegisterBuiltinConsoleCommands()
        {
            // Ensure admin/console aliases exist even if lang registration missed them
            string[] builtins =
            {
                "balance", "deposit", "SetBalance", "transfer", "withdraw",
                "ecowipe", "ecopurge", "ecostats",
                "testdiscord", "testdiscorddirect", "testwebhook",
                "ecodailysummary", "ecoperiodicreport"
            };

            var methodByCmd = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["balance"] = nameof(Economics.CommandBalance),
                ["deposit"] = nameof(Economics.CommandDeposit),
                ["SetBalance"] = nameof(Economics.CommandSetBalance),
                ["transfer"] = nameof(Economics.CommandTransfer),
                ["withdraw"] = nameof(Economics.CommandWithdraw),
                ["ecowipe"] = nameof(Economics.CommandWipe),
                ["ecopurge"] = nameof(Economics.CommandPurge),
                ["ecostats"] = nameof(Economics.CommandStats),
                ["testdiscord"] = nameof(Economics.CommandTestDiscord),
                ["testdiscorddirect"] = nameof(Economics.CommandTestDiscordDirect),
                ["testwebhook"] = nameof(Economics.CommandTestWebhook),
                ["ecodailysummary"] = nameof(Economics.CommandDailySummary),
                ["ecoperiodicreport"] = nameof(Economics.CommandPeriodicReport),
            };

            foreach (var cmd in builtins)
            {
                _chatCommandNames.Add(cmd);
                if (!_commandMethodMap.ContainsKey(cmd) && methodByCmd.TryGetValue(cmd, out var method))
                    _commandMethodMap[cmd] = method;

                if (!_registeredCommands.Any(c =>
                        string.Equals(c.Name, cmd, StringComparison.OrdinalIgnoreCase) &&
                        string.IsNullOrEmpty(c.Parent)))
                {
                    var methodName = _commandMethodMap.TryGetValue(cmd, out var m) ? m : methodByCmd[cmd];
                    RegisterConsole(cmd, arg => DispatchCovalenceCommand(methodName, arg), serverAdmin: false);
                }
            }
        }

        private void DispatchCovalenceCommand(string methodName, ConsoleSystem.Arg arg)
        {
            if (_plugin == null || string.IsNullOrEmpty(methodName) || arg == null) return;
            try
            {
                IPlayer player = null;
                var bp = arg.Player();
                if (bp != null)
                    player = bp.ToIPlayer();
                else
                    player = new RustConsolePlayer();

                string[] args;
                try
                {
                    var raw = arg.Args;
                    if (raw == null || raw.Length == 0) args = Array.Empty<string>();
                    else
                    {
                        args = new string[raw.Length];
                        for (int i = 0; i < raw.Length; i++) args[i] = raw[i].ToString() ?? "";
                    }
                }
                catch { args = Array.Empty<string>(); }

                var mi = typeof(Economics).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null)
                {
                    Debug.LogWarning($"[Economics Harmony] Method not found: {methodName}");
                    return;
                }
                mi.Invoke(_plugin, new object[] { player, methodName, args });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Economics Harmony] {methodName}: " + (ex.InnerException?.Message ?? ex.Message));
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
                    catch (Exception ex) { Debug.LogWarning($"[Economics] command {localName}: " + ex.Message); }
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
            if (!_commandMethodMap.TryGetValue(name, out var methodName)) return false;

            var args = parts.Skip(1).ToArray();
            try
            {
                var mi = typeof(Economics).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                mi?.Invoke(_plugin, new object[] { player.ToIPlayer(), name, args });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Economics] chat {name}: " + (ex.InnerException?.Message ?? ex.Message));
            }
            return true;
        }

        /// <summary>Plugin-shaped wrapper for AppDomain consumers (IsLoaded + Call).</summary>
        public sealed class EconomicsPluginWrapper
        {
            private readonly EconomicsHarmonyMod _mod;
            public EconomicsPluginWrapper(EconomicsHarmonyMod mod) => _mod = mod;
            public bool IsLoaded => _mod?._plugin != null;
            public string Name => "Economics";
            public string Version => $"{VersionMajor}.{VersionMinor}.{VersionPatch}";
            public object Call(string method, params object[] args) => _mod?.Call(method, args);
        }
    }
}
