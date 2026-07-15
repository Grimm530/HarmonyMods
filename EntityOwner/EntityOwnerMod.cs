using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EntityOwner
{
    /// <summary>
    /// Harmony entry for Entity Owner 3.4.3 (Calytic) port.
    /// Load order: 0Permissions -> EntityOwner (ready-callback safe).
    /// </summary>
    public class EntityOwnerMod : IHarmonyModHooks
    {
        public static EntityOwnerMod Instance { get; private set; }
        public static EntityOwnerService Service { get; private set; }

        public const int VersionMajor = 3;
        public const int VersionMinor = 4;
        public const int VersionPatch = 3;

        private Action _permissionsReadyCallback;
        private readonly List<ConsoleSystem.Command> _commands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private GameObject _runner;
        private bool _serverReady;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            try
            {
                Service = new EntityOwnerService(root);
                Service.LoadConfig();
                Service.LoadDefaultMessages();
            }
            catch (Exception ex)
            {
                Debug.LogError("[EntityOwner] FAIL: construct/config: " + ex);
                return;
            }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            EnsureRunner();
            _runner.GetComponent<EntityOwnerRunner>().Begin(this);

            RefreshChatCommands();
            RegisterConsoleCommands();

            Debug.Log($"[EntityOwner] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[EntityOwner] -> Config: HarmonyConfig/EntityOwner.json");
            Debug.Log("[EntityOwner] -> Lang: HarmonyLanguage/EntityOwner.json (optional)");
            Debug.Log("[EntityOwner] -> Load order: 0Permissions -> EntityOwner");
        }

        private void OnPermissionsReady()
        {
            try
            {
                Service?.RegisterPermissions();
                Debug.Log("[EntityOwner] OK: Permissions ready — perms re-registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[EntityOwner] FAIL: Permissions ready: " + ex.Message);
            }
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || Service == null) return;
            _serverReady = true;
            try
            {
                Service.OnServerInitialized();
                RefreshChatCommands();
                Debug.Log("[EntityOwner] OK: Server initialized.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[EntityOwner] FAIL: OnServerInitialized: " + ex);
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
            Service = null;

            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }

            Instance = null;
            Debug.Log("[EntityOwner] OK: Unloaded.");
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("EntityOwner_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<EntityOwnerRunner>();
        }

        public void RefreshChatCommands()
        {
            _chatCommands.Clear();
            if (Service == null) return;
            foreach (string cmd in Service.GetChatCommandNames())
            {
                if (!string.IsNullOrWhiteSpace(cmd))
                    _chatCommands.Add(cmd.Trim());
            }
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || Service == null || string.IsNullOrEmpty(command)) return false;
            if (!_chatCommands.Contains(command)) return false;
            return Service.HandleChatCommand(player, command, args ?? Array.Empty<string>());
        }

        private void RegisterConsoleCommands()
        {
            UnregisterConsoleCommands();
            foreach (string name in Service.GetChatCommandNames())
            {
                string cmdName = name;
                RegisterConsole(cmdName, arg =>
                {
                    BasePlayer player = arg?.Player();
                    if (player == null) return;
                    string[] args;
                    if (arg.Args == null || arg.Args.Length == 0)
                    {
                        args = Array.Empty<string>();
                    }
                    else
                    {
                        args = new string[arg.Args.Length];
                        for (int i = 0; i < arg.Args.Length; i++)
                            args[i] = arg.Args[i].ToString();
                    }
                    Service?.HandleChatCommand(player, cmdName, args);
                });
            }
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler)
        {
            try
            {
                var cmd = new ConsoleSystem.Command
                {
                    Name = name,
                    FullName = "global." + name,
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
                Debug.LogWarning("[EntityOwner] FAIL: RegisterConsole(" + name + "): " + ex.Message);
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

    internal sealed class EntityOwnerRunner : MonoBehaviour
    {
        private EntityOwnerMod _mod;
        private bool _started;

        public void Begin(EntityOwnerMod mod)
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
