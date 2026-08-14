using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SortButton
{
    public class SortButtonMod : IHarmonyModHooks
    {
        public static SortButtonMod Instance { get; private set; }
        public const int VersionMajor = 2;
        public const int VersionMinor = 7;
        public const int VersionPatch = 0;

        private SortButtonPlugin _plugin;
        private Action _permissionsReadyCallback;
        private readonly HashSet<string> _chatCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private GameObject _runner;
        private bool _serverReady;

        public SortButtonPlugin Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            try
            {
                _plugin = new SortButtonPlugin(root);
                _plugin.Load();
            }
            catch (Exception ex)
            {
                Debug.LogError("[SortButton] FAIL: construct/config: " + ex);
                return;
            }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);
            EnsureRunner();
            _runner.GetComponent<SortButtonRunner>().Begin(this);
            RefreshChatCommands();
            Debug.Log($"[SortButton] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
        }

        private void OnPermissionsReady()
        {
            try
            {
                _plugin?.RegisterPermissions();
                Debug.Log("[SortButton] OK: Permissions ready.");
            }
            catch (Exception ex) { Debug.LogWarning("[SortButton] Permissions ready: " + ex.Message); }
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || _plugin == null) return;
            _serverReady = true;
            try
            {
                _plugin.RegisterPermissions();
                _plugin.OnServerInitialized();
                RefreshChatCommands();
                Debug.Log("[SortButton] OK: Server initialized.");
            }
            catch (Exception ex) { Debug.LogError("[SortButton] FAIL: OnServerInitialized: " + ex); }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            try
            {
                if (_permissionsReadyCallback != null)
                    PermissionsBridge.UnregisterReadyCallback(_permissionsReadyCallback);
            }
            catch { }
            try { _plugin?.Unload(); } catch { }
            _plugin = null;
            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }
            Instance = null;
            Debug.Log("[SortButton] OK: Unloaded.");
        }

        public void RefreshChatCommands()
        {
            _chatCommands.Clear();
            if (_plugin?.Commands == null) return;
            for (int i = 0; i < _plugin.Commands.Count; i++)
            {
                string cmd = _plugin.Commands[i];
                if (!string.IsNullOrWhiteSpace(cmd))
                    _chatCommands.Add(cmd.Trim());
            }
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || _plugin == null || string.IsNullOrEmpty(command)) return false;
            if (!_chatCommands.Contains(command)) return false;
            _plugin.CmdSortButton(player, command, args);
            return true;
        }

        public void HandleCui(BasePlayer player, string action)
        {
            if (_plugin == null || player == null) return;
            if (string.Equals(action, "order", StringComparison.OrdinalIgnoreCase))
                _plugin.Command_SortType(player);
            else if (string.Equals(action, "sort", StringComparison.OrdinalIgnoreCase))
                _plugin.Command_Sort(player);
        }

        internal void NextTick(Action action)
        {
            _runner?.GetComponent<SortButtonRunner>()?.Delay(action, 0f);
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("SortButton_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<SortButtonRunner>();
        }
    }

    internal sealed class SortButtonRunner : MonoBehaviour
    {
        private SortButtonMod _mod;
        private bool _started;

        public void Begin(SortButtonMod mod)
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

        public void Delay(Action action, float delay)
        {
            if (action == null) return;
            StartCoroutine(DelayCo(action, delay));
        }

        private IEnumerator DelayCo(Action action, float delay)
        {
            if (delay <= 0f) yield return null;
            else yield return new WaitForSeconds(delay);
            try { action(); } catch (Exception ex) { Debug.LogWarning("[SortButton] delayed: " + ex.Message); }
        }
    }
}
