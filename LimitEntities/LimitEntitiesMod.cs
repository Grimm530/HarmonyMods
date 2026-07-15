using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LimitEntities
{
    /// <summary>
    /// Harmony entry for LimitEntities 2.3.10 port.
    /// Load order: 0Permissions -> LimitEntities.
    /// </summary>
    public class LimitEntitiesMod : IHarmonyModHooks
    {
        public static LimitEntitiesMod Instance { get; private set; }
        public static LimitEntitiesService Service { get; private set; }

        public const int VersionMajor = 2;
        public const int VersionMinor = 3;
        public const int VersionPatch = 10;

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
                Service = new LimitEntitiesService(root);
                Service.LoadConfig();
            }
            catch (Exception ex)
            {
                Debug.LogError("[LimitEntities] FAIL: construct/config: " + ex);
                return;
            }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            EnsureRunner();
            _runner.GetComponent<LimitEntitiesRunner>().Begin(this);

            RefreshChatCommands();
            RegisterConsoleCommands();

            Debug.Log($"[LimitEntities] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[LimitEntities] -> Config: HarmonyConfig/LimitEntities.json");
            Debug.Log("[LimitEntities] -> Data: HarmonyData/LimitEntities.json");
            Debug.Log("[LimitEntities] -> Load order: 0Permissions -> LimitEntities");
        }

        private void OnPermissionsReady()
        {
            try
            {
                Service?.RegisterPermissions();
                Service?.RefreshAllPlayerPerms();
                Debug.Log("[LimitEntities] OK: Permissions ready — perms re-registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LimitEntities] FAIL: Permissions ready: " + ex.Message);
            }
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || Service == null) return;
            _serverReady = true;
            try
            {
                Service.RegisterPermissions();
                Service.InitializeCaches();
                Service.StoredDataLoad();
                Service.CacheEntities();
                RefreshChatCommands();

                foreach (BasePlayer player in BasePlayer.activePlayerList)
                {
                    if (player != null && player.userID.IsSteamId())
                        Service.GetPlayerData(player.userID);
                }

                Debug.Log("[LimitEntities] OK: Server initialized.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[LimitEntities] FAIL: OnServerInitialized: " + ex);
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
            try { Service?.StoredDataSave(); } catch { }
            Service?.Shutdown();
            Service = null;

            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }

            Instance = null;
            Debug.Log("[LimitEntities] OK: Unloaded.");
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("LimitEntities_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<LimitEntitiesRunner>();
        }

        public void RefreshChatCommands()
        {
            _chatCommands.Clear();
            if (Service?.Config?.Commands == null) return;
            foreach (string cmd in Service.Config.Commands)
            {
                if (!string.IsNullOrWhiteSpace(cmd))
                    _chatCommands.Add(cmd.Trim());
            }
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || Service == null || string.IsNullOrEmpty(command)) return false;
            if (!_chatCommands.Contains(command)) return false;
            Service.CmdLimitEntities(player, args);
            return true;
        }

        private void RegisterConsoleCommands()
        {
            UnregisterConsoleCommands();
            RegisterConsole("limitentities.list", arg =>
            {
                Service?.CmdLimitEntitiesList(arg);
            });
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler)
        {
            try
            {
                var cmd = new ConsoleSystem.Command
                {
                    Name = name.Contains(".") ? name.Substring(name.IndexOf('.') + 1) : name,
                    FullName = name.Contains(".") ? name : "global." + name,
                    Variable = false,
                    ServerAdmin = true,
                    ServerUser = true,
                    AllowRunFromServer = true,
                    Call = a => handler(a)
                };
                // Facepunch indexes by short name in GlobalDict and full name in Dict
                string shortName = cmd.Name;
                string fullName = cmd.FullName;
                if (!fullName.StartsWith("global.", StringComparison.OrdinalIgnoreCase) && !fullName.Contains("."))
                    fullName = "global." + shortName;
                cmd.FullName = fullName;
                cmd.Name = shortName.Contains(".") ? shortName : (name.Contains(".") ? name : shortName);

                // Prefer registering as limitentities.list style
                if (name.Contains("."))
                {
                    cmd.Name = name;
                    cmd.FullName = name;
                }

                _commands.Add(cmd);
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                if (dict != null) dict[cmd.FullName] = cmd;
                if (globalDict != null) globalDict[cmd.Name] = cmd;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LimitEntities] FAIL: RegisterConsole(" + name + "): " + ex.Message);
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

        public static void NextTick(Action action)
        {
            if (action == null) return;
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(action, 0f);
            else
                action();
        }
    }

    internal sealed class LimitEntitiesRunner : MonoBehaviour
    {
        private LimitEntitiesMod _mod;
        private bool _started;

        public void Begin(LimitEntitiesMod mod)
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

            // Wait for ItemManager + world entities to exist
            int attempts = 0;
            while (attempts < 120)
            {
                bool itemsReady = false;
                try { itemsReady = ItemManager.itemList != null && ItemManager.itemList.Count > 0; } catch { }
                if (itemsReady && BaseNetworkable.serverEntities != null)
                    break;
                attempts++;
                yield return new WaitForSeconds(attempts < 10 ? 0.5f : 1f);
            }

            // Small delay so Permissions OnLoaded finishes if loading alphabetically
            yield return new WaitForSeconds(1f);
            _mod?.OnServerInitialized();
        }
    }
}
