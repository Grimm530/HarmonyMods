using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ScaleHarmony
{
    public class ScaleMod : IHarmonyModHooks
    {
        public static ScaleMod Instance { get; private set; }
        public static ScalePlugin Plugin { get; private set; }

        public const int VersionMajor = 1;
        public const int VersionMinor = 0;
        public const int VersionPatch = 0;

        private Action _permissionsReadyCallback;
        private readonly HashSet<string> _chatCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private GameObject _runner;
        private bool _serverReady;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            try
            {
                Plugin = new ScalePlugin(root);
                Plugin.LoadConfig();
            }
            catch (Exception ex)
            {
                Debug.LogError("[Scale] FAIL: construct/config: " + ex);
                return;
            }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            _chatCommands.Clear();
            _chatCommands.Add("scale");
            _chatCommands.Add("scaleid");

            EnsureRunner();
            _runner.GetComponent<ScaleRunner>().Begin(this);

            Debug.Log($"[Scale] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[Scale] -> Config: HarmonyConfig/Scale.json");
            Debug.Log("[Scale] Chat: /scale /scaleid");
        }

        private void OnPermissionsReady()
        {
            try
            {
                Plugin?.RegisterPermissions();
                Debug.Log("[Scale] OK: Permissions ready — perms re-registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Scale] FAIL: Permissions ready: " + ex.Message);
            }
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || Plugin == null) return;
            _serverReady = true;
            try
            {
                Plugin.RegisterPermissions();
                Debug.Log("[Scale] OK: Server initialized.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Scale] FAIL: OnServerInitialized: " + ex);
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
            Debug.Log("[Scale] OK: Unloaded.");
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || Plugin == null || string.IsNullOrEmpty(command)) return false;
            if (!_chatCommands.Contains(command)) return false;
            if (string.Equals(command, "scaleid", StringComparison.OrdinalIgnoreCase))
                Plugin.CmdScaleId(player, args);
            else
                Plugin.CmdScale(player, args);
            return true;
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("Scale_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<ScaleRunner>();
        }
    }

    internal sealed class ScaleRunner : MonoBehaviour
    {
        private ScaleMod _mod;
        private bool _started;

        public void Begin(ScaleMod mod)
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
