using System;
using System.Collections;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace AirbourneSpawnHarmony
{
    /// <summary>
    /// Harmony entry for AirbourneSpawn 1.0.191 (Oxide-free).
    /// Load order: 0Permissions -> Kits (optional autokit) -> AirbourneSpawn.
    /// </summary>
    public class AirbourneSpawnMod : IHarmonyModHooks
    {
        public static AirbourneSpawnMod Instance { get; private set; }

        public const int VersionMajor = 1;
        public const int VersionMinor = 0;
        public const int VersionPatch = 191;

        public const string AppDomainApiKey = "AirbourneSpawn_ApiType";

        private AirbourneSpawnPlugin _plugin;
        private Action _permissionsReadyCallback;
        private GameObject _runner;
        private bool _serverReady;
        private Harmony _kitsHarmony;
        private bool _kitsPatched;

        public AirbourneSpawnPlugin Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            try
            {
                AirbourneSpawnHost.Init(root);
                _plugin = new AirbourneSpawnPlugin();
                _plugin.HarmonyInit();
            }
            catch (Exception ex)
            {
                Debug.LogError("[AirbourneSpawn] FAIL: construct/config: " + ex);
                return;
            }

            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(AirbourneSpawnMod));
            }
            catch { }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            EnsureRunner();
            _runner.GetComponent<AirbourneSpawnRunner>().Begin(this);

            Debug.Log($"[AirbourneSpawn] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[AirbourneSpawn] -> Config: HarmonyConfig/AirbourneSpawn.json");
            Debug.Log("[AirbourneSpawn] -> Lang: HarmonyLanguage/AirbourneSpawn.json (optional)");
            Debug.Log("[AirbourneSpawn] -> Load order: 0Permissions -> Kits (optional) -> AirbourneSpawn");
        }

        private void OnPermissionsReady()
        {
            try
            {
                _plugin?.RegisterPermissions();
                Debug.Log("[AirbourneSpawn] OK: Permissions ready - perms re-registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AirbourneSpawn] FAIL: Permissions ready: " + ex.Message);
            }
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || _plugin == null) return;
            _serverReady = true;
            try
            {
                _plugin.RegisterPermissions();
                _plugin.HarmonyServerInitialized();
                TryPatchKitsAutoKit();
                Debug.Log("[AirbourneSpawn] OK: Server initialized.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[AirbourneSpawn] FAIL: OnServerInitialized: " + ex);
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

            try
            {
                if (_kitsHarmony != null)
                    _kitsHarmony.UnpatchAll(_kitsHarmony.Id);
            }
            catch { }
            _kitsHarmony = null;
            _kitsPatched = false;

            try
            {
                _plugin?.HarmonyUnload();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AirbourneSpawn] Unload: " + ex.Message);
            }

            _plugin = null;
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); } catch { }
            AirbourneSpawnHost.Shutdown();

            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }

            Instance = null;
            Debug.Log("[AirbourneSpawn] OK: Unloaded.");
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("AirbourneSpawn_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<AirbourneSpawnRunner>();
        }

        /// <summary>Route cui.endtest AIRBOURNESPAWN beach to the beach spawn command.</summary>
        public void HandleCuiCallback(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 2) return;
            string action = a[1].ToString();
            if (!string.Equals(action, "beach", StringComparison.OrdinalIgnoreCase))
                return;

            var player = args.Connection?.player as BasePlayer ?? ArgEx.Player(args);
            _plugin?.CmdBeach(player);
        }

        /// <summary>
        /// Skip Harmony Kits autokit when we are giving a plane-spawn kit.
        /// Applied dynamically so Kits can load before or after this mod.
        /// </summary>
        internal void TryPatchKitsAutoKit()
        {
            if (_kitsPatched) return;
            Type kitsType = null;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        kitsType = asm.GetType("KitsHarmony.Kits");
                        if (kitsType != null) break;
                    }
                    catch { }
                }
            }
            catch { }

            if (kitsType == null) return;

            var method = AccessTools.Method(kitsType, "OnPlayerRespawned", new[] { typeof(BasePlayer) });
            if (method == null) return;

            try
            {
                _kitsHarmony = new Harmony("AirbourneSpawn.KitsAutoKit");
                var prefix = new HarmonyMethod(typeof(Patches.Kits_OnPlayerRespawned_Patch), nameof(Patches.Kits_OnPlayerRespawned_Patch.Prefix));
                _kitsHarmony.Patch(method, prefix: prefix);
                _kitsPatched = true;
                Debug.Log("[AirbourneSpawn] OK: Patched Kits.OnPlayerRespawned (plane autokit skip).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AirbourneSpawn] Kits autokit patch: " + ex.Message);
            }
        }
    }

    internal sealed class AirbourneSpawnRunner : MonoBehaviour
    {
        private AirbourneSpawnMod _mod;
        private bool _started;

        public void Begin(AirbourneSpawnMod mod)
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
