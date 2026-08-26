using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RustLeagueHarmony
{
    public class RustLeagueMod : IHarmonyModHooks
    {
        public static RustLeagueMod Instance { get; private set; }

        public const int VersionMajor = 1;
        public const int VersionMinor = 3;
        public const int VersionPatch = 28;

        public const string AppDomainApiKey = "RustLeague_ApiType";

        private RustLeaguePlugin _plugin;
        private Action _permissionsReadyCallback;
        private GameObject _runner;
        private bool _serverReady;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();

        public RustLeaguePlugin Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            try
            {
                RustLeagueHost.Init(root);
                _plugin = new RustLeaguePlugin();
                _plugin.HarmonyInit();
            }
            catch (Exception ex)
            {
                Debug.LogError("[RustLeague] FAIL: construct/config: " + ex);
                return;
            }

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(RustLeagueMod)); }
            catch { }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            RegisterCommands();
            RegisterRlAlias("rl.spawn", "spawn");
            RegisterRlAlias("rl.tp", "tp");
            RegisterRlAlias("rl.open", "open");
            RegisterRlAlias("rl.close", "close");
            RegisterRlAlias("rl.test", "test");
            EnsureRunner();
            _runner.GetComponent<RustLeagueRunner>().Begin(this);

            Debug.Log($"[RustLeague] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[RustLeague] -> Config: HarmonyConfig/RustLeague.json");
            Debug.Log("[RustLeague] -> Load order: 0Permissions -> RustLeague");
            Debug.Log("[RustLeague] -> Chat: /rl   Console: rl.spawn / rl.tp / rl.open / rl.close / rl.test");
        }

        private void OnPermissionsReady()
        {
            try
            {
                _plugin?.RegisterPermissions();
                Debug.Log("[RustLeague] OK: Permissions ready - perms re-registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RustLeague] FAIL: Permissions ready: " + ex.Message);
            }
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || _plugin == null) return;
            _serverReady = true;
            try
            {
                _plugin.RegisterPermissions();
                _plugin.HarmonyServerInitialized();
                Debug.Log("[RustLeague] OK: Server initialized.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[RustLeague] FAIL: OnServerInitialized: " + ex);
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

            UnregisterCommands();

            try { _plugin?.HarmonyUnload(); }
            catch (Exception ex) { Debug.LogWarning("[RustLeague] Unload: " + ex.Message); }

            _plugin = null;
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); } catch { }
            RustLeagueHost.Shutdown();

            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }

            Instance = null;
            Debug.Log("[RustLeague] OK: Unloaded.");
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("RustLeague_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<RustLeagueRunner>();
        }

        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || _plugin == null || string.IsNullOrWhiteSpace(message)) return false;
            message = message.Trim();
            if (message.StartsWith("/") || message.StartsWith("\\"))
                message = message.Substring(1).Trim();
            if (!message.StartsWith("rl", StringComparison.OrdinalIgnoreCase))
                return false;
            if (message.Length > 2 && message[2] != ' ')
                return false;

            string[] parts = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] args = parts.Length <= 1 ? Array.Empty<string>() : new string[parts.Length - 1];
            for (int i = 1; i < parts.Length; i++)
                args[i - 1] = parts[i];

            try { _plugin.CmdChatRl(player, args); }
            catch (Exception ex) { Debug.LogWarning("[RustLeague] /rl: " + ex); }
            return true;
        }

        public void HandleCuiCallback(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 2) return;
            string action = a[1].ToString();
            var player = args.Connection?.player as BasePlayer ?? ArgEx.Player(args);
            _plugin?.HandleJoinUi(player, action);
        }

        private void RegisterCommands()
        {
            RegisterConsole("rl", arg =>
            {
                if (arg == null) return;
                var player = arg.Connection?.player as BasePlayer ?? ArgEx.Player(arg);
                string[] args;
                try
                {
                    var raw = arg.Args;
                    if (raw == null || raw.Length == 0) args = Array.Empty<string>();
                    else
                    {
                        args = new string[raw.Length];
                        for (int i = 0; i < raw.Length; i++) args[i] = raw[i].ToString();
                    }
                }
                catch { args = Array.Empty<string>(); }

                if (player == null)
                    _plugin?.CmdConsoleRl(arg, args);
                else
                    _plugin?.CmdChatRl(player, args);
            }, serverAdmin: false);
        }

        private void RegisterRlAlias(string name, string subcommand)
        {
            RegisterConsole(name, arg =>
            {
                if (arg == null) return;
                var player = arg.Connection?.player as BasePlayer ?? ArgEx.Player(arg);
                string[] args = new[] { subcommand };
                if (player == null)
                    _plugin?.CmdConsoleRl(arg, args);
                else
                    _plugin?.CmdChatRl(player, args);
            }, serverAdmin: false);
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler, bool serverAdmin = false)
        {
            bool hasDot = name.IndexOf('.') >= 0;
            string cmdParent = "";
            string cmdName = name;
            string fullName;
            string dictKey;
            if (hasDot)
            {
                int dot = name.IndexOf('.');
                cmdParent = name.Substring(0, dot);
                cmdName = name.Substring(dot + 1);
                fullName = name;
                dictKey = name;
            }
            else
            {
                fullName = "global." + name;
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
                    catch (Exception ex) { Debug.LogWarning("[RustLeague] command " + name + ": " + ex.Message); }
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
                string full = cmd.FullName ?? ("global." + cmd.Name);
                ConsoleSystem.Index.Server.Dict?.Remove(full);
                if (!string.IsNullOrEmpty(cmd.Parent))
                    ConsoleSystem.Index.Server.Dict?.Remove(cmd.Parent + "." + cmd.Name);
                ConsoleSystem.Index.Server.GlobalDict?.Remove(cmd.Name);
            }
            _registeredCommands.Clear();
        }
    }

    internal sealed class RustLeagueRunner : MonoBehaviour
    {
        private RustLeagueMod _mod;
        private bool _started;

        public void Begin(RustLeagueMod mod)
        {
            _mod = mod;
            if (!_started)
            {
                _started = true;
                StartCoroutine(WaitForServer());
            }
        }

        private IEnumerator WaitForServer()
        {
            while (ServerMgr.Instance == null)
                yield return null;
            yield return new WaitForSeconds(2f);
            _mod?.OnServerInitialized();
        }
    }
}
