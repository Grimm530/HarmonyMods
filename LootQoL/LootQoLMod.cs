using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace LootQoLHarmony
{
    /// <summary>
    /// Harmony entry for LootQoL (FastLoot + LootBouncer + SortButton).
    /// Load order: 0Permissions -> LootQoL (ready-callback safe).
    /// </summary>
    public class LootQoLMod : IHarmonyModHooks
    {
        public static LootQoLMod Instance { get; private set; }
        public static LootQoLPlugin Plugin { get; private set; }

        public const int VersionMajor = 1;
        public const int VersionMinor = 2;
        public const int VersionPatch = 0;

        private Action _permissionsReadyCallback;
        private GameObject _runner;
        private bool _serverReady;

        public LootQoLRunner Runner => _runner != null ? _runner.GetComponent<LootQoLRunner>() : null;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            try
            {
                Plugin = new LootQoLPlugin(root);
                Plugin.LoadConfig();
                Plugin.LoadDefaultMessages();
            }
            catch (Exception ex)
            {
                Debug.LogError("[LootQoL] FAIL: construct/config: " + ex);
                return;
            }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            EnsureRunner();
            _runner.GetComponent<LootQoLRunner>().Begin(this);

            Debug.Log($"[LootQoL] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch} (FastLoot + LootBouncer + SortButton)");
            Debug.Log("[LootQoL] -> Config: HarmonyConfig/LootQoL.json");
            Debug.Log("[LootQoL] -> Lang: HarmonyLanguage/LootQoL.json");
            Debug.Log("[LootQoL] -> Data: HarmonyData/LootQoL/");
            Debug.Log("[LootQoL] -> Load order: 0Permissions -> LootQoL");
        }

        private void OnPermissionsReady()
        {
            try
            {
                Plugin?.RegisterPermissions();
                Debug.Log("[LootQoL] OK: Permissions ready — perms re-registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LootQoL] FAIL: Permissions ready: " + ex.Message);
            }
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || Plugin == null) return;
            _serverReady = true;
            try
            {
                Plugin.RegisterPermissions();
                Plugin.OnServerInitialized();
                Debug.Log("[LootQoL] OK: Server initialized.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[LootQoL] FAIL: OnServerInitialized: " + ex);
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

            try { Plugin?.Unload(); }
            catch { }

            Plugin = null;

            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }

            Instance = null;
            Debug.Log("[LootQoL] OK: Unloaded.");
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("LootQoL_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<LootQoLRunner>();
        }

        public void HandleCuiCallback(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 2) return;
            string action = a.GetValue(1)?.ToString() ?? string.Empty;
            BasePlayer player = args.Connection?.player as BasePlayer;
            if (player == null) return;
            if (string.Equals(action, "take", StringComparison.OrdinalIgnoreCase))
                Plugin?.FastLootTakeAll(player);
            else if (string.Equals(action, "sort", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(action, "order", StringComparison.OrdinalIgnoreCase))
                Plugin?.SortButton?.HandleCui(player, action);
        }

        public bool TryHandleChat(BasePlayer player, string message)
        {
            return Plugin?.SortButton != null && Plugin.SortButton.TryHandleChat(player, message);
        }

        internal void NextTick(Action action)
        {
            Runner?.Delay(action, 0f);
        }
    }

    public sealed class LootQoLRunner : MonoBehaviour
    {
        private LootQoLMod _mod;
        private bool _started;
        private readonly System.Collections.Generic.Dictionary<ulong, Coroutine> _timers =
            new System.Collections.Generic.Dictionary<ulong, Coroutine>();

        public void Begin(LootQoLMod mod)
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

        public void Once(ulong id, float delay, Action action)
        {
            Cancel(id);
            _timers[id] = StartCoroutine(OnceCo(id, delay, action));
        }

        public void Cancel(ulong id)
        {
            if (_timers.TryGetValue(id, out Coroutine co) && co != null)
            {
                StopCoroutine(co);
                _timers.Remove(id);
            }
        }

        public bool HasTimer(ulong id) => _timers.ContainsKey(id);

        public void Delay(Action action, float delay)
        {
            if (action == null) return;
            StartCoroutine(DelayCo(action, delay));
        }

        private IEnumerator DelayCo(Action action, float delay)
        {
            if (delay <= 0f) yield return null;
            else yield return new WaitForSeconds(delay);
            try { action(); }
            catch (Exception ex) { Debug.LogWarning("[LootQoL] delayed: " + ex.Message); }
        }

        public void CancelAll()
        {
            foreach (var kv in _timers)
            {
                if (kv.Value != null)
                    StopCoroutine(kv.Value);
            }
            _timers.Clear();
        }

        private IEnumerator OnceCo(ulong id, float delay, Action action)
        {
            yield return new WaitForSeconds(delay);
            _timers.Remove(id);
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[LootQoL] timer: " + ex.Message); }
        }
    }
}
