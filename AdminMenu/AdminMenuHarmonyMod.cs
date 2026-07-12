using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oxide.Ext.Chaos.UIFramework;
using UnityEngine;

namespace AdminMenuHarmony
{
    /// <summary>
    /// Harmony entry point for AdminMenu 2.1.13 (Chaos UI). Hosts the plugin and routes /admin + CUI callbacks.
    /// </summary>
    public class AdminMenuHarmonyMod : IHarmonyModHooks
    {
        public static AdminMenuHarmonyMod Instance { get; private set; }

        public const int VersionMajor = 2;
        public const int VersionMinor = 1;
        public const int VersionPatch = 13;

        public static readonly VersionNumber Version = new VersionNumber(VersionMajor, VersionMinor, VersionPatch);

        public const string AppDomainApiKey = "AdminMenu_ApiType";
        public const string AppDomainPluginKey = "AdminMenu_Plugin";

        private AdminMenu _plugin;
        private Action _permissionsReadyCallback;

        public AdminMenu Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            AdminMenuHost.Init(root);
            _plugin = new AdminMenu();
            if (AdminMenuHost.Instance != null)
                AdminMenuHost.Instance.PluginRef = new Plugin
                {
                    Name = "AdminMenu",
                    Title = "AdminMenu",
                    Version = Version,
                    IsLoaded = true
                };
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(AdminMenuHarmonyMod)); }
            catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainPluginKey, _plugin); }
            catch { }

            _plugin.HarmonyInit();
            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);
            ScheduleServerInitialized();
            Debug.Log($"[AdminMenu Harmony] Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[AdminMenu Harmony] Chat: /admin");
        }

        private void OnPermissionsReady()
        {
            try
            {
                _plugin?.RegisterPermissionsWithPermissionsMod();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AdminMenu] Permissions ready re-register: " + ex.Message);
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
                    AdminMenuImages.TryLoad(AdminMenuHost.Instance?.ImagesDirectory);
                    Debug.Log($"[AdminMenu Harmony] Server initialized (v{VersionMajor}.{VersionMinor}.{VersionPatch})");
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AdminMenu Harmony] ScheduleServerInitialized: " + ex.Message);
            }

            if (attempt > 60)
            {
                try
                {
                    _plugin.HarmonyServerInitialized();
                    AdminMenuImages.TryLoad(AdminMenuHost.Instance?.ImagesDirectory);
                }
                catch (Exception ex) { Debug.LogError("[AdminMenu Harmony] Init failed: " + ex); }
                return;
            }

            float delay = attempt < 10 ? 0.5f : 1f;
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(() => ScheduleServerInitialized(attempt + 1), delay);
            else
            {
                try
                {
                    var go = new GameObject("AdminMenuHarmony_InitWait");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<InitWaitBehaviour>().Begin(this, attempt);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[AdminMenu Harmony] ScheduleServerInitialized: " + ex.Message);
                }
            }
        }

        private class InitWaitBehaviour : MonoBehaviour
        {
            private AdminMenuHarmonyMod _mod;
            private int _attempt;

            public void Begin(AdminMenuHarmonyMod mod, int attempt)
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
                Debug.LogWarning("[AdminMenu Harmony] Unload: " + ex.Message);
            }

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); } catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainPluginKey, null); } catch { }

            AdminMenuHost.Shutdown();
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
            if (!string.Equals(parts[0], "admin", StringComparison.OrdinalIgnoreCase))
                return false;

            var args = parts.Skip(1).ToArray();
            try
            {
                _plugin.CmdAdmin(player, "admin", args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AdminMenu] /admin: " + (ex.InnerException?.Message ?? ex.Message));
            }
            return true;
        }

        /// <summary>Route cui.endtest ADMINMENU … to CommandCallbackHandler.</summary>
        public void HandleCuiCallback(ConsoleSystem.Arg args, Array a)
        {
            if (_plugin?.CallbackHandler == null || a == null || a.Length < 1) return;
            var player = args.Connection?.player as BasePlayer ?? args.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return;

            // Rebuild: adminmenu.callback {rest after marker}
            // Payload may be: ADMINMENU adminmenu.callback <id> …  OR  ADMINMENU <id> …
            var sb = new StringBuilder("adminmenu.callback");
            int start = 1;
            if (a.Length >= 2)
            {
                string second = a.GetValue(1)?.ToString() ?? "";
                if (second.Equals("adminmenu.callback", StringComparison.OrdinalIgnoreCase) ||
                    second.StartsWith("adminmenu.callback", StringComparison.OrdinalIgnoreCase))
                {
                    // Already includes command token — append remaining only
                    start = 2;
                    if (second.Length > "adminmenu.callback".Length)
                    {
                        var rest = second.Substring("adminmenu.callback".Length).Trim();
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
                Debug.LogWarning("[AdminMenu] cui.endtest ADMINMENU: " + ex);
            }
        }
    }
}
