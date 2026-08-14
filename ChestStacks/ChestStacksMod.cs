using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ChestStacks
{
    public class ChestStacksMod : IHarmonyModHooks
    {
        public static ChestStacksMod Instance { get; private set; }
        public const int VersionMajor = 1;
        public const int VersionMinor = 4;
        public const int VersionPatch = 6;

        private ChestStacksPlugin _plugin;
        private Action _permissionsReadyCallback;
        private GameObject _runner;
        private bool _serverReady;

        public ChestStacksPlugin Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            try
            {
                _plugin = new ChestStacksPlugin(root);
                _plugin.Load();
            }
            catch (Exception ex)
            {
                Debug.LogError("[ChestStacks] FAIL: construct/config: " + ex);
                return;
            }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);
            EnsureRunner();
            _runner.GetComponent<ChestStacksRunner>().Begin(this);
            Debug.Log($"[ChestStacks] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[ChestStacks] -> Config: HarmonyConfig/ChestStacks.json");
            Debug.Log("[ChestStacks] -> Data: HarmonyData/ChestStacks/");
        }

        private void OnPermissionsReady()
        {
            try
            {
                _plugin?.RegisterPermissions();
                Debug.Log("[ChestStacks] OK: Permissions ready — perms re-registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ChestStacks] FAIL: Permissions ready: " + ex.Message);
            }
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || _plugin == null) return;
            _serverReady = true;
            try
            {
                _plugin.RegisterPermissions();
                _plugin.OnServerInitialized();
                Debug.Log("[ChestStacks] OK: Server initialized.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[ChestStacks] FAIL: OnServerInitialized: " + ex);
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
            try { _plugin?.Unload(); } catch { }
            _plugin = null;
            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }
            Instance = null;
            Debug.Log("[ChestStacks] OK: Unloaded.");
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("ChestStacks_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<ChestStacksRunner>();
        }
    }

    internal sealed class ChestStacksRunner : MonoBehaviour
    {
        private ChestStacksMod _mod;
        private bool _started;

        public void Begin(ChestStacksMod mod)
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
            if (delay <= 0f)
                yield return null;
            else
                yield return new WaitForSeconds(delay);
            try { action(); } catch (Exception ex) { Debug.LogWarning("[ChestStacks] delayed: " + ex.Message); }
        }
    }
}
