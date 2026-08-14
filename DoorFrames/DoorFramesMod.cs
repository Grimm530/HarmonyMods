using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DoorFramesHarmony
{
    public class DoorFramesMod : IHarmonyModHooks
    {
        public static DoorFramesMod Instance { get; private set; }
        public static DoorFramesPlugin Plugin { get; private set; }

        public const int VersionMajor = 2;
        public const int VersionMinor = 2;
        public const int VersionPatch = 0;

        private Action _permissionsReadyCallback;
        private readonly HashSet<string> _chatCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private GameObject _runner;
        private bool _serverReady;

        public DoorFramesRunner Runner => _runner != null ? _runner.GetComponent<DoorFramesRunner>() : null;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            try
            {
                Plugin = new DoorFramesPlugin();
            }
            catch (Exception ex)
            {
                Debug.LogError("[DoorFrames] FAIL: construct: " + ex);
                return;
            }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            _chatCommands.Clear();
            _chatCommands.Add("df.rotate");

            EnsureRunner();
            _runner.GetComponent<DoorFramesRunner>().Begin(this);

            Debug.Log($"[DoorFrames] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[DoorFrames] Chat: /df.rotate");
            Debug.Log("[DoorFrames] -> Load order: 0Permissions -> DoorFrames");
        }

        private void OnPermissionsReady()
        {
            try
            {
                Plugin?.RegisterPermissions();
                Debug.Log("[DoorFrames] OK: Permissions ready — perms re-registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DoorFrames] FAIL: Permissions ready: " + ex.Message);
            }
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || Plugin == null) return;
            _serverReady = true;
            try
            {
                Plugin.RegisterPermissions();
                Debug.Log("[DoorFrames] OK: Server initialized.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[DoorFrames] FAIL: OnServerInitialized: " + ex);
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

            Plugin = null;
            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }
            Instance = null;
            Debug.Log("[DoorFrames] OK: Unloaded.");
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || Plugin == null || string.IsNullOrEmpty(command)) return false;
            if (!_chatCommands.Contains(command)) return false;
            Plugin.RotateDoor(player);
            return true;
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("DoorFrames_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<DoorFramesRunner>();
        }
    }

    public sealed class DoorFramesRunner : MonoBehaviour
    {
        private DoorFramesMod _mod;
        private bool _started;
        private float _nextCleanup;

        public void Begin(DoorFramesMod mod)
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

        private void Update()
        {
            var plugin = DoorFramesMod.Plugin;
            if (plugin == null) return;
            if (Time.realtimeSinceStartup < _nextCleanup) return;
            _nextCleanup = Time.realtimeSinceStartup + 10f;
            plugin.CleanCooldowns();
        }
    }
}
