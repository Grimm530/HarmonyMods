using System;
using System.Linq;
using System.Text;
using System.IO;
using Oxide.Ext.Chaos.UIFramework;
using UnityEngine;

namespace StackManagerHarmony
{
    /// <summary>
    /// Harmony entry point for StackManager (StacksExtended 2.0.24 Chaos UI port).
    /// </summary>
    public class StackManagerHarmonyMod : IHarmonyModHooks
    {
        public static StackManagerHarmonyMod Instance { get; private set; }

        public const int VersionMajor = 2;
        public const int VersionMinor = 0;
        public const int VersionPatch = 24;

        public static readonly VersionNumber Version = new VersionNumber(VersionMajor, VersionMinor, VersionPatch);

        public const string AppDomainApiKey = "StackManager_ApiType";
        public const string AppDomainPluginKey = "StackManager_Plugin";

        private StacksExtended _plugin;
        private Action _permissionsReadyCallback;

        public StacksExtended Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            StackManagerHost.Init(root);
            _plugin = new StacksExtended();
            if (StackManagerHost.Instance != null)
                StackManagerHost.Instance.PluginRef = new Plugin
                {
                    Name = "StackManager",
                    Title = "StackManager",
                    Version = Version,
                    IsLoaded = true
                };
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(StackManagerHarmonyMod)); }
            catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainPluginKey, _plugin); }
            catch { }

            _plugin.HarmonyInit();
            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);
            ScheduleServerInitialized();
            Debug.Log($"[StackManager Harmony] Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[StackManager Harmony] Chat: /stacks");
        }

        private void OnPermissionsReady()
        {
            try
            {
                _plugin?.RegisterPermissionsWithPermissionsMod();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StackManager] Permissions ready re-register: " + ex.Message);
            }
        }

        private void ScheduleServerInitialized(int attempt = 0)
        {
            if (_plugin == null) return;
            try
            {
                var identity = ConVar.Server.identity;
                bool identityReady = !string.IsNullOrEmpty(identity) &&
                    !string.Equals(identity, "my_server_identity", StringComparison.OrdinalIgnoreCase);
                bool ready = ServerMgr.Instance != null && attempt >= 2 && identityReady;
                if (ready)
                {
                    _plugin.HarmonyServerInitialized();
                    Debug.Log($"[StackManager Harmony] Server initialized (v{VersionMajor}.{VersionMinor}.{VersionPatch})");
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StackManager Harmony] ScheduleServerInitialized: " + ex.Message);
            }

            if (attempt > 60)
            {
                try
                {
                    _plugin.HarmonyServerInitialized();
                }
                catch (Exception ex) { Debug.LogError("[StackManager Harmony] Init failed: " + ex); }
                return;
            }

            float delay = attempt < 10 ? 0.5f : 1f;
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(() => ScheduleServerInitialized(attempt + 1), delay);
            else
            {
                try
                {
                    var go = new GameObject("StackManagerHarmony_InitWait");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<InitWaitBehaviour>().Begin(this, attempt);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[StackManager Harmony] ScheduleServerInitialized: " + ex.Message);
                }
            }
        }

        private class InitWaitBehaviour : MonoBehaviour
        {
            private StackManagerHarmonyMod _mod;
            private int _attempt;

            public void Begin(StackManagerHarmonyMod mod, int attempt)
            {
                _mod = mod;
                _attempt = attempt;
                StartCoroutine(Wait());
            }

            private System.Collections.IEnumerator Wait()
            {
                yield return new WaitForSeconds(0.5f);
                var mod = _mod;
                var attempt = _attempt;
                Destroy(gameObject);
                mod?.ScheduleServerInitialized(attempt + 1);
            }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            if (_permissionsReadyCallback != null)
            {
                PermissionsBridge.UnregisterReadyCallback(_permissionsReadyCallback);
                _permissionsReadyCallback = null;
            }

            try
            {
                _plugin?.ClearAllUis();
                _plugin?.CallbackHandler?.Clear();
                _plugin?.CallbackHandler?.Unregister();
                _plugin?.HarmonyUnload();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StackManager Harmony] Unload: " + ex.Message);
            }

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); } catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainPluginKey, null); } catch { }

            StackManagerHost.Shutdown();
            _plugin = null;
            Instance = null;
        }

        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || _plugin == null || string.IsNullOrWhiteSpace(message)) return false;
            message = message.Trim();
            if (message.StartsWith("/")) message = message.Substring(1).Trim();
            string[] parts = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            if (!string.Equals(parts[0], "stacks", StringComparison.OrdinalIgnoreCase))
                return false;

            var args = parts.Skip(1).ToArray();
            try
            {
                _plugin.CmdStacks(player, "stacks", args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StackManager] /stacks: " + (ex.InnerException?.Message ?? ex.Message));
            }
            return true;
        }

        /// <summary>Route cui.endtest STACKMANAGER … to CommandCallbackHandler.</summary>
        public void HandleCuiCallback(ConsoleSystem.Arg args, Array a)
        {
            if (_plugin?.CallbackHandler == null || a == null || a.Length < 1) return;
            var player = args.Connection?.player as BasePlayer ?? args.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return;

            var sb = new StringBuilder("stackmanager.callback");
            int start = 1;
            if (a.Length >= 2)
            {
                string second = a.GetValue(1)?.ToString() ?? "";
                if (second.Equals("stackmanager.callback", StringComparison.OrdinalIgnoreCase) ||
                    second.StartsWith("stackmanager.callback", StringComparison.OrdinalIgnoreCase) ||
                    second.Equals("stacksextended.callback", StringComparison.OrdinalIgnoreCase) ||
                    second.StartsWith("stacksextended.callback", StringComparison.OrdinalIgnoreCase))
                {
                    start = 2;
                    string prefix = second.StartsWith("stacksextended", StringComparison.OrdinalIgnoreCase)
                        ? "stacksextended.callback"
                        : "stackmanager.callback";
                    if (second.Length > prefix.Length)
                    {
                        var rest = second.Substring(prefix.Length).Trim();
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
                Debug.LogWarning("[StackManager] cui.endtest STACKMANAGER: " + ex);
            }
        }
    }
}
