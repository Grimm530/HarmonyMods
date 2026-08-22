using System;
using System.Collections;
using System.IO;
using System.Text;
using HarmonyChat;
using Oxide.Ext.Chaos.UIFramework;
using UnityEngine;

namespace DynamicCupShareHarmony
{
    /// <summary>
    /// Harmony entry for DynamicCupShare 3.1.23 (Chaos UI port).
    /// Load order: 0Permissions -> DynamicCupShare (ready-callback safe).
    /// </summary>
    public class DynamicCupShareMod : IHarmonyModHooks
    {
        public static DynamicCupShareMod Instance { get; private set; }

        public const int VersionMajor = 3;
        public const int VersionMinor = 1;
        public const int VersionPatch = 23;

        public const string AppDomainApiKey = "DynamicCupShare_ApiType";
        public const string AppDomainPluginKey = "DynamicCupShare_Plugin";

        private DynamicCupSharePlugin _plugin;
        private Action _permissionsReadyCallback;
        private GameObject _runner;
        private bool _serverReady;

        public DynamicCupSharePlugin Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            try
            {
                DynamicCupShareHost.Init(root);
                _plugin = new DynamicCupSharePlugin();
                _plugin.HarmonyInit();
            }
            catch (Exception ex)
            {
                Debug.LogError("[DynamicCupShare] FAIL: construct/config: " + ex);
                return;
            }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(DynamicCupShareMod)); }
            catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainPluginKey, _plugin); }
            catch { }

            ChatSayBridge.Register("DynamicCupShare", OnChatCommand);

            EnsureRunner();
            _runner.GetComponent<DynamicCupShareRunner>().Begin(this);

            Debug.Log($"[DynamicCupShare] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[DynamicCupShare] -> Config: HarmonyConfig/DynamicCupShare.json");
            Debug.Log("[DynamicCupShare] -> Data: HarmonyData/DynamicCupShare/");
            Debug.Log("[DynamicCupShare] -> Lang: HarmonyLanguage/DynamicCupShare.json (optional)");
            Debug.Log("[DynamicCupShare] -> Load order: 0Permissions -> DynamicCupShare");
            Debug.Log("[DynamicCupShare] -> Chat: /share  /shareplayer  /dcsadmin  /bs");
        }

        private void OnPermissionsReady()
        {
            try
            {
                _plugin?.RegisterPermissions();
                Debug.Log("[DynamicCupShare] OK: Permissions ready - perms re-registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DynamicCupShare] FAIL: Permissions ready: " + ex.Message);
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
                Debug.Log("[DynamicCupShare] OK: Server initialized.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[DynamicCupShare] FAIL: OnServerInitialized: " + ex);
            }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            try { ChatSayBridge.Unregister("DynamicCupShare"); } catch { }

            try
            {
                if (_permissionsReadyCallback != null)
                    PermissionsBridge.UnregisterReadyCallback(_permissionsReadyCallback);
            }
            catch { }

            try { _plugin?.HarmonyUnload(); }
            catch (Exception ex) { Debug.LogWarning("[DynamicCupShare] Unload: " + ex.Message); }

            _plugin = null;
            DynamicCupShareHost.Shutdown();

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); }
            catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainPluginKey, null); }
            catch { }

            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }

            Instance = null;
            Debug.Log("[DynamicCupShare] OK: Unloaded.");
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("DynamicCupShare_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<DynamicCupShareRunner>();
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

        /// <summary>
        /// Public API for Clans/Friends Harmony mods: rebuild shares after membership changes.
        /// </summary>
        public static void NotifyFriendChanged(string playerId)
        {
            Instance?._plugin?.OnFriendRemoved(playerId, null);
        }

        public static void NotifyFriendAdded(string playerId, string friendId)
        {
            Instance?._plugin?.OnFriendAdded(playerId, friendId);
        }

        public static void NotifyFriendRemoved(string playerId, string friendId)
        {
            Instance?._plugin?.OnFriendRemoved(playerId, friendId);
        }

        public static void NotifyClanMembersChanged(ulong playerId, System.Collections.Generic.List<ulong> clanMembers)
        {
            var plugin = Instance?._plugin;
            if (plugin == null) return;
            plugin.RebuildClanMemberEntities(playerId, clanMembers);
        }

        public static void NotifyClanMemberJoined(ulong playerId, System.Collections.Generic.List<ulong> clanMembers)
        {
            Instance?._plugin?.OnClanMemberJoined(null, playerId, clanMembers);
        }

        public static void NotifyClanMemberGone(ulong playerId, System.Collections.Generic.List<ulong> clanMembers)
        {
            Instance?._plugin?.OnClanMemberGone(null, playerId, clanMembers);
        }

        public static void NotifyClanDisbanded(System.Collections.Generic.List<ulong> clanMembers)
        {
            Instance?._plugin?.OnClanDisbanded(null, clanMembers);
        }

        public static void NotifyClanAllianceChanged(string tag, string alliedTag)
        {
            Instance?._plugin?.OnClanAllianceDissolved(tag, alliedTag);
        }

        /// <summary>Public API for AutoCodeLock: rebuild team/clan whitelist after a PIN/guest change.</summary>
        public static void NotifyCodeLockChanged(CodeLock codeLock)
        {
            Instance?._plugin?.RebuildCodeLockShares(codeLock);
        }

        /// <summary>Route cui.endtest DYNAMICCUPSHARE … to CommandCallbackHandler.</summary>
        public void HandleCuiCallback(ConsoleSystem.Arg args, Array a)
        {
            if (_plugin?.CallbackHandler == null || a == null || a.Length < 1) return;
            var player = args.Connection?.player as BasePlayer ?? args.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return;

            const string cmd = "dynamiccupshare.callback";
            var sb = new StringBuilder(cmd);
            int start = 1;
            if (a.Length >= 2)
            {
                string second = a.GetValue(1)?.ToString() ?? "";
                if (second.Equals(cmd, StringComparison.OrdinalIgnoreCase) ||
                    second.StartsWith(cmd, StringComparison.OrdinalIgnoreCase))
                {
                    start = 2;
                    if (second.Length > cmd.Length)
                    {
                        var rest = second.Substring(cmd.Length).Trim();
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
                Debug.LogWarning("[DynamicCupShare] cui.endtest DYNAMICCUPSHARE: " + ex);
            }
        }
    }

    internal sealed class DynamicCupShareRunner : MonoBehaviour
    {
        private DynamicCupShareMod _mod;
        private bool _started;

        public void Begin(DynamicCupShareMod mod)
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
