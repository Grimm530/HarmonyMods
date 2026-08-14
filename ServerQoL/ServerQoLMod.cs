using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace ServerQoL
{
    /// <summary>
    /// Harmony entry combining UnlockInventory, InfiniteBurn, ElectricGeneratorTweaker, InfiniteVendingStock.
    /// Load order: 0Permissions -> ServerQoL (ready-callback safe).
    /// </summary>
    public class ServerQoLMod : IHarmonyModHooks
    {
        public static ServerQoLMod Instance { get; private set; }
        public static ServerQoLService Service { get; private set; }

        public const int VersionMajor = 1;
        public const int VersionMinor = 0;
        public const int VersionPatch = 0;

        private Action _permissionsReadyCallback;
        private GameObject _runner;
        private bool _serverReady;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            try
            {
                Service = new ServerQoLService(root);
                Service.LoadConfig();
            }
            catch (Exception ex)
            {
                Debug.LogError("[ServerQoL] FAIL: construct/config: " + ex);
                return;
            }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            EnsureRunner();
            _runner.GetComponent<ServerQoLRunner>().Begin(this);

            Debug.Log($"[ServerQoL] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[ServerQoL] -> Config: HarmonyConfig/ServerQoL.json");
            Debug.Log("[ServerQoL] -> Load order: 0Permissions -> ServerQoL");
        }

        private void OnPermissionsReady()
        {
            try
            {
                Service?.RegisterPermissions();
                Debug.Log("[ServerQoL] OK: Permissions ready — perms re-registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerQoL] FAIL: Permissions ready: " + ex.Message);
            }
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || Service == null) return;
            _serverReady = true;
            try
            {
                Service.RegisterPermissions();
                Service.ApplyExistingWorld();
                Debug.Log("[ServerQoL] OK: Server initialized.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[ServerQoL] FAIL: OnServerInitialized: " + ex);
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

            Service = null;

            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }

            Instance = null;
            Debug.Log("[ServerQoL] OK: Unloaded.");
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("ServerQoL_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<ServerQoLRunner>();
        }
    }

    internal sealed class ServerQoLRunner : MonoBehaviour
    {
        private ServerQoLMod _mod;
        private bool _started;

        public void Begin(ServerQoLMod mod)
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
