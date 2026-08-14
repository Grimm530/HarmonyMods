using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace InventoryViewer
{
    public class InventoryViewerMod : IHarmonyModHooks
    {
        public static InventoryViewerMod Instance { get; private set; }
        public const int VersionMajor = 4;
        public const int VersionMinor = 1;
        public const int VersionPatch = 3;

        private InventoryViewerPlugin _plugin;
        private Action _permissionsReadyCallback;
        private readonly HashSet<string> _chatCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "viewinv", "viewinventory", "inspect"
        };
        private GameObject _runner;
        private bool _serverReady;

        public InventoryViewerPlugin Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            try
            {
                _plugin = new InventoryViewerPlugin(root);
                _plugin.Load();
            }
            catch (Exception ex)
            {
                Debug.LogError("[InventoryViewer] FAIL: construct/config: " + ex);
                return;
            }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);
            EnsureRunner();
            _runner.GetComponent<InventoryViewerRunner>().Begin(this);
            Debug.Log($"[InventoryViewer] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
        }

        private void OnPermissionsReady()
        {
            try { _plugin?.RegisterPermissions(); }
            catch (Exception ex) { Debug.LogWarning("[InventoryViewer] Permissions: " + ex.Message); }
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || _plugin == null) return;
            _serverReady = true;
            try
            {
                _plugin.RegisterPermissions();
                Debug.Log("[InventoryViewer] OK: Server initialized.");
            }
            catch (Exception ex) { Debug.LogError("[InventoryViewer] FAIL: OnServerInitialized: " + ex); }
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
            Debug.Log("[InventoryViewer] OK: Unloaded.");
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || _plugin == null || string.IsNullOrEmpty(command)) return false;
            if (!_chatCommands.Contains(command)) return false;
            _plugin.ViewInvCmd(player, args);
            return true;
        }

        internal void Delay(Action action, float delay) =>
            _runner?.GetComponent<InventoryViewerRunner>()?.Delay(action, delay);

        public void ViewInventory(BasePlayer viewer, BasePlayer target) =>
            _plugin?._ViewInventory(viewer, target);

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("InventoryViewer_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<InventoryViewerRunner>();
        }
    }

    internal sealed class InventoryViewerRunner : MonoBehaviour
    {
        private InventoryViewerMod _mod;
        private bool _started;

        public void Begin(InventoryViewerMod mod)
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
            try { action(); } catch (Exception ex) { Debug.LogWarning("[InventoryViewer] delayed: " + ex.Message); }
        }
    }
}
