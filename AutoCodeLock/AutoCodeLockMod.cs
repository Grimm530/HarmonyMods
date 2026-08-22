using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyChat;
using Oxide.Ext.Chaos.UIFramework;
using UnityEngine;

namespace AutoCodeLockHarmony
{
    /// <summary>
    /// Harmony entry for AutoCodeLock 3.0.12 (Chaos UI port).
    /// Load order: 0Permissions -> AutoCodeLock (ready-callback safe).
    /// </summary>
    public class AutoCodeLockMod : IHarmonyModHooks
    {
        public static AutoCodeLockMod Instance { get; private set; }

        public const int VersionMajor = 3;
        public const int VersionMinor = 0;
        public const int VersionPatch = 12;

        private AutoCodeLockPlugin _plugin;
        private Action _permissionsReadyCallback;
        private GameObject _runner;
        private bool _serverReady;

        public AutoCodeLockPlugin Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            try
            {
                AutoCodeLockHost.Init(root);
                _plugin = new AutoCodeLockPlugin();
                _plugin.HarmonyInit();
            }
            catch (Exception ex)
            {
                Debug.LogError("[AutoCodeLock] FAIL: construct/config: " + ex);
                return;
            }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);
            ChatSayBridge.Register("AutoCodeLock", OnChatCommand);

            EnsureRunner();
            _runner.GetComponent<AutoCodeLockRunner>().Begin(this);

            Debug.Log($"[AutoCodeLock] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[AutoCodeLock] -> Config: HarmonyConfig/AutoCodeLock.json");
            Debug.Log("[AutoCodeLock] -> Data: HarmonyData/AutoCodeLock/user_data.json");
            Debug.Log("[AutoCodeLock] -> Lang: HarmonyLanguage/AutoCodeLock.json (optional)");
            Debug.Log("[AutoCodeLock] -> Load order: 0Permissions -> AutoCodeLock");
        }

        private void OnPermissionsReady()
        {
            try
            {
                _plugin?.RegisterPermissions();
                Debug.Log("[AutoCodeLock] OK: Permissions ready - perms re-registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AutoCodeLock] FAIL: Permissions ready: " + ex.Message);
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
                Debug.Log("[AutoCodeLock] OK: Server initialized.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[AutoCodeLock] FAIL: OnServerInitialized: " + ex);
            }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            try { ChatSayBridge.Unregister("AutoCodeLock"); } catch { }

            try
            {
                if (_permissionsReadyCallback != null)
                    PermissionsBridge.UnregisterReadyCallback(_permissionsReadyCallback);
            }
            catch { }

            try
            {
                _plugin?.HarmonyUnload(shuttingDown: false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AutoCodeLock] Unload: " + ex.Message);
            }

            _plugin = null;
            AutoCodeLockHost.Shutdown();

            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }

            Instance = null;
            Debug.Log("[AutoCodeLock] OK: Unloaded.");
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("AutoCodeLock_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<AutoCodeLockRunner>();
        }

        /// <summary>ChatSayBridge entry: full message including leading slash.</summary>
        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrWhiteSpace(message)) return false;
            message = message.Trim();
            if (message.StartsWith("/") || message.StartsWith("\\"))
                message = message.Substring(1).Trim();
            if (message.Length == 0) return false;

            string[] parts = message.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            string[] args = parts.Length > 1 ? new string[parts.Length - 1] : Array.Empty<string>();
            for (int i = 1; i < parts.Length; i++)
                args[i - 1] = parts[i];
            return TryHandleChat(player, parts[0], args);
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (_plugin == null) return false;
            return _plugin.TryHandleChat(player, command, args);
        }

        /// <summary>Route cui.endtest AUTOCODELOCK … to CommandCallbackHandler.</summary>
        public void HandleCuiCallback(ConsoleSystem.Arg args, Array a)
        {
            if (_plugin?.CallbackHandler == null || a == null || a.Length < 1) return;
            var player = args.Connection?.player as BasePlayer ?? args.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return;

            var sb = new StringBuilder("autocodelock.callback");
            int start = 1;
            if (a.Length >= 2)
            {
                string second = a.GetValue(1)?.ToString() ?? "";
                if (second.Equals("autocodelock.callback", StringComparison.OrdinalIgnoreCase) ||
                    second.StartsWith("autocodelock.callback", StringComparison.OrdinalIgnoreCase))
                {
                    start = 2;
                    if (second.Length > "autocodelock.callback".Length)
                    {
                        var rest = second.Substring("autocodelock.callback".Length).Trim();
                        if (!string.IsNullOrEmpty(rest))
                        {
                            sb.Append(' ');
                            sb.Append(rest);
                        }
                    }
                }
            }

            for (int i = start; i < a.Length; i++)
            {
                sb.Append(' ');
                string s = a.GetValue(i)?.ToString() ?? string.Empty;
                if (s.IndexOfAny(new[] { ' ', '"' }) >= 0)
                    sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
                else
                    sb.Append(s);
            }

            try
            {
                var opt = ConsoleSystem.Option.Server.Quiet();
                if (args.Connection != null)
                    opt = opt.FromConnection(args.Connection);
                var uiArg = new ConsoleSystem.Arg(opt, sb.ToString());
                _plugin.CallbackHandler.HandleCallback(uiArg);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AutoCodeLock] cui.endtest AUTOCODELOCK: " + ex);
            }
        }
    }

    internal sealed class AutoCodeLockRunner : MonoBehaviour
    {
        private AutoCodeLockMod _mod;
        private bool _started;

        public void Begin(AutoCodeLockMod mod)
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
