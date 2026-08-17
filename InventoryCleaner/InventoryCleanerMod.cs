using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace InventoryCleaner
{
    /// <summary>
    /// Harmony entry for Inventory Cleaner 2.1.2 port.
    /// Load order: 0Permissions -> InventoryCleaner (ready-callback safe).
    /// </summary>
    public class InventoryCleanerMod : IHarmonyModHooks
    {
        public static InventoryCleanerMod Instance { get; private set; }
        public static InventoryCleanerService Service { get; private set; }

        public const int VersionMajor = 2;
        public const int VersionMinor = 1;
        public const int VersionPatch = 2;

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
                Service = new InventoryCleanerService(root);
                Service.LoadConfig();
                Service.LoadDefaultMessages();
            }
            catch (Exception ex)
            {
                Debug.LogError("[InventoryCleaner] FAIL: construct/config: " + ex);
                return;
            }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            EnsureRunner();
            _runner.GetComponent<InventoryCleanerRunner>().Begin(this);

            RefreshChatCommands();
            RegisterConsoleCommands();

            Debug.Log($"[InventoryCleaner] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[InventoryCleaner] -> Config: HarmonyConfig/InventoryCleaner.json");
            Debug.Log("[InventoryCleaner] -> Lang: HarmonyLanguage/InventoryCleaner.json (optional)");
            Debug.Log("[InventoryCleaner] -> Load order: 0Permissions -> InventoryCleaner");
        }

        private void OnPermissionsReady()
        {
            try
            {
                Service?.RegisterPermissions();
                Debug.Log("[InventoryCleaner] OK: Permissions ready — perms re-registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[InventoryCleaner] FAIL: Permissions ready: " + ex.Message);
            }
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || Service == null) return;
            _serverReady = true;
            try
            {
                Service.RegisterPermissions();
                RefreshChatCommands();
                Debug.Log("[InventoryCleaner] OK: Server initialized.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[InventoryCleaner] FAIL: OnServerInitialized: " + ex);
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
            Debug.Log("[InventoryCleaner] OK: Unloaded.");
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("InventoryCleaner_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<InventoryCleanerRunner>();
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
            Service.HandleClearCommand(player, args);
            return true;
        }

        private void RegisterConsoleCommands()
        {
            UnregisterConsoleCommands();
            // Client F1 / console aliases (server console ignored in service — needs a player).
            foreach (string name in new[] { "clearinv", "cleaninv", "invclear", "invclean" })
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
                    Service?.HandleClearCommand(player, args);
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
                Debug.LogWarning("[InventoryCleaner] FAIL: RegisterConsole(" + name + "): " + ex.Message);
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

    internal sealed class InventoryCleanerRunner : MonoBehaviour
    {
        private InventoryCleanerMod _mod;
        private bool _started;

        public void Begin(InventoryCleanerMod mod)
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
