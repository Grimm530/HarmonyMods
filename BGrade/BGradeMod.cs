using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BGrade
{
    public class BGradeMod : IHarmonyModHooks
    {
        public static BGradeMod Instance { get; private set; }
        public const int VersionMajor = 1;
        public const int VersionMinor = 1;
        public const int VersionPatch = 6;

        private BGradePlugin _plugin;
        private Action _permissionsReadyCallback;
        private readonly List<ConsoleSystem.Command> _commands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private GameObject _runner;
        private bool _serverReady;

        public BGradePlugin Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            try
            {
                _plugin = new BGradePlugin(root);
                _plugin.Load();
            }
            catch (Exception ex)
            {
                Debug.LogError("[BGrade] FAIL: construct/config: " + ex);
                return;
            }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            EnsureRunner();
            _runner.GetComponent<BGradeRunner>().Begin(this);
            RefreshChatCommands();
            RegisterConsoleCommands();

            Debug.Log($"[BGrade] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[BGrade] -> Config: HarmonyConfig/BGrade.json");
            Debug.Log("[BGrade] -> Lang: HarmonyLanguage/BGrade.json");
        }

        private void OnPermissionsReady()
        {
            try
            {
                _plugin?.RegisterPermissions();
                Debug.Log("[BGrade] OK: Permissions ready — perms re-registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BGrade] FAIL: Permissions ready: " + ex.Message);
            }
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || _plugin == null) return;
            _serverReady = true;
            try
            {
                _plugin.RegisterPermissions();
                RefreshChatCommands();
                RegisterConsoleCommands();
                Debug.Log("[BGrade] OK: Server initialized.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[BGrade] FAIL: OnServerInitialized: " + ex);
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

            UnregisterConsoleCommands();
            try { _plugin?.Unload(); } catch { }
            _plugin = null;

            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }

            Instance = null;
            Debug.Log("[BGrade] OK: Unloaded.");
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("BGrade_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<BGradeRunner>();
        }

        public void RefreshChatCommands()
        {
            _chatCommands.Clear();
            if (_plugin == null) return;
            List<string> cmds = _plugin.ChatCommands;
            if (cmds == null) return;
            for (int i = 0; i < cmds.Count; i++)
            {
                string cmd = cmds[i];
                if (!string.IsNullOrWhiteSpace(cmd))
                    _chatCommands.Add(cmd.Trim());
            }
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || _plugin == null || string.IsNullOrEmpty(command)) return false;
            if (!_chatCommands.Contains(command)) return false;
            _plugin.BGradeCommand(player, command, args);
            return true;
        }

        private void RegisterConsoleCommands()
        {
            UnregisterConsoleCommands();
            if (_plugin?.ConsoleCommands == null) return;
            for (int i = 0; i < _plugin.ConsoleCommands.Count; i++)
            {
                string name = _plugin.ConsoleCommands[i];
                if (string.IsNullOrWhiteSpace(name)) continue;
                RegisterConsole(name, arg =>
                {
                    BasePlayer player = arg?.Player();
                    if (player == null) return;
                    _plugin?.BGradeUpCommand(arg);
                });
            }
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler)
        {
            try
            {
                string full = name.IndexOf('.') >= 0 ? name : "global." + name;
                string shortName = name.IndexOf('.') >= 0 ? name.Substring(name.IndexOf('.') + 1) : name;
                var cmd = new ConsoleSystem.Command
                {
                    Name = shortName,
                    FullName = full,
                    Variable = false,
                    ServerAdmin = true,
                    ServerUser = true,
                    AllowRunFromServer = true,
                    Call = a => handler(a)
                };
                _commands.Add(cmd);
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                if (dict != null) dict[cmd.FullName] = cmd;
                if (globalDict != null) globalDict[cmd.Name] = cmd;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BGrade] FAIL: RegisterConsole(" + name + "): " + ex.Message);
            }
        }

        private void UnregisterConsoleCommands()
        {
            try
            {
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                foreach (var cmd in _commands)
                {
                    dict?.Remove(cmd.FullName);
                    globalDict?.Remove(cmd.Name);
                }
            }
            catch { }
            _commands.Clear();
        }
    }

    internal sealed class BGradeRunner : MonoBehaviour
    {
        private BGradeMod _mod;
        private bool _started;

        public void Begin(BGradeMod mod)
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
            yield return new WaitForSeconds(1f);
            _mod?.OnServerInitialized();
        }
    }
}
