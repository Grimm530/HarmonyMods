using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace InfiniteVendingStock
{
    /// <summary>
    /// Harmony entry for Infinite Vending Stock (Oxide 1.0.2 port). No Oxide runtime.
    /// </summary>
    public class InfiniteVendingStockMod : IHarmonyModHooks
    {
        public static InfiniteVendingStockMod Instance { get; private set; }
        public static InfiniteVendingStockService Service { get; private set; }

        public const int VersionMajor = 1;
        public const int VersionMinor = 0;
        public const int VersionPatch = 2;

        private GameObject _runner;
        private bool _serverReady;

        internal InfiniteVendingStockRunner Runner =>
            _runner != null ? _runner.GetComponent<InfiniteVendingStockRunner>() : null;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            try
            {
                Service = new InfiniteVendingStockService(root);
                Service.LoadConfig();
            }
            catch (Exception ex)
            {
                Debug.LogError("[InfiniteVendingStock] FAIL: construct/config: " + ex);
                return;
            }

            EnsureRunner();
            _runner.GetComponent<InfiniteVendingStockRunner>().Begin(this);

            Debug.Log($"[InfiniteVendingStock] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[InfiniteVendingStock] -> Config: HarmonyConfig/InfiniteVendingStock.json");
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || Service == null) return;
            _serverReady = true;
            try
            {
                Service.RestockAllNpcVendors();
                Debug.Log("[InfiniteVendingStock] OK: Server initialized — NPC vendors restocked.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[InfiniteVendingStock] FAIL: OnServerInitialized: " + ex);
            }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            Service = null;

            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }

            Instance = null;
            Debug.Log("[InfiniteVendingStock] OK: Unloaded.");
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("InfiniteVendingStock_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<InfiniteVendingStockRunner>();
        }
    }

    public sealed class InfiniteVendingStockRunner : MonoBehaviour
    {
        private InfiniteVendingStockMod _mod;
        private bool _started;

        public void Begin(InfiniteVendingStockMod mod)
        {
            _mod = mod;
            if (_started) return;
            _started = true;
            StartCoroutine(WaitForServer());
        }

        private IEnumerator WaitForServer()
        {
            while (ServerMgr.Instance == null)
                yield return null;

            yield return new WaitForSeconds(1f);
            _mod?.OnServerInitialized();
        }

        public void NextTick(Action action)
        {
            if (action == null) return;
            StartCoroutine(NextTickCo(action));
        }

        private IEnumerator NextTickCo(Action action)
        {
            yield return null;
            try { action(); }
            catch (Exception ex) { Debug.LogWarning("[InfiniteVendingStock] next tick: " + ex.Message); }
        }
    }
}
