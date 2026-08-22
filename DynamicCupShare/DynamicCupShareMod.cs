using System;
using System.Collections;
using System.IO;
using System.Text;
using HarmonyLib;
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
        private Harmony _oxideObserver;

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

            PatchOxidePluginHooks();

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
            try
            {
                if (_permissionsReadyCallback != null)
                    PermissionsBridge.UnregisterReadyCallback(_permissionsReadyCallback);
            }
            catch { }

            try { _oxideObserver?.UnpatchAll(_oxideObserver.Id); }
            catch { }
            _oxideObserver = null;

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

        private void PatchOxidePluginHooks()
        {
            try
            {
                var t = AccessTools.TypeByName("Oxide.Core.Interface");
                var m = t != null ? AccessTools.Method(t, "CallHook", new[] { typeof(string), typeof(object[]) }) : null;
                if (m == null)
                    return;

                _oxideObserver = new Harmony("com.grimm.dynamiccupshare.oxideobserver");
                _oxideObserver.Patch(m, postfix: new HarmonyMethod(typeof(DynamicCupShareMod), nameof(OxideCallHookPostfix)));
                Debug.Log("[DynamicCupShare] Observing Oxide plugin hooks (Clans/Friends).");
            }
            catch (Exception ex)
            {
                Debug.Log("[DynamicCupShare] Oxide hook observer not attached: " + ex.Message);
            }
        }

        public static void OxideCallHookPostfix(string hook, object[] args)
        {
            var plugin = Instance?._plugin;
            if (plugin == null || string.IsNullOrEmpty(hook) || args == null) return;

            try
            {
                switch (hook)
                {
                    case "OnFriendAdded":
                        if (args.Length >= 1)
                            plugin.OnFriendAdded(args[0]?.ToString(), args.Length > 1 ? args[1]?.ToString() : null);
                        break;
                    case "OnFriendRemoved":
                        if (args.Length >= 1)
                            plugin.OnFriendRemoved(args[0]?.ToString(), args.Length > 1 ? args[1]?.ToString() : null);
                        break;
                    case "OnClanMemberJoined":
                        HandleClanMemberHook(plugin, args, joined: true);
                        break;
                    case "OnClanMemberGone":
                        HandleClanMemberHook(plugin, args, joined: false);
                        break;
                    case "OnClanDisbanded":
                        HandleClanDisbanded(plugin, args);
                        break;
                    case "OnClanAllianceCreated":
                    case "OnClanAllianceDissolved":
                        if (args.Length >= 2)
                            plugin.OnClanAllianceDissolved(args[0]?.ToString(), args[1]?.ToString());
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DynamicCupShare] Oxide hook observer: " + ex.Message);
            }
        }

        private static void HandleClanMemberHook(DynamicCupSharePlugin plugin, object[] args, bool joined)
        {
            if (plugin == null || args == null || args.Length == 0) return;

            string tag = null;
            ulong playerId = 0UL;
            System.Collections.Generic.List<ulong> members = null;

            // Clans Reborn / DCS: (string tag, ulong playerId, List<ulong> members)
            if (args.Length >= 2 && args[0] is string tagStr && TryToUlong(args[1], out playerId))
            {
                tag = tagStr;
                if (args.Length >= 3)
                    members = args[2] as System.Collections.Generic.List<ulong>;
            }
            // BlueprintShare / some Clans: (ulong playerId, string clanName)
            else if (TryToUlong(args[0], out playerId))
            {
                tag = args.Length > 1 ? args[1]?.ToString() : null;
                if (args.Length > 2)
                    members = args[2] as System.Collections.Generic.List<ulong>;
            }

            if (playerId == 0UL) return;

            if (joined)
                plugin.OnClanMemberJoined(tag, playerId, members);
            else
                plugin.OnClanMemberGone(tag, playerId, members);
        }

        private static void HandleClanDisbanded(DynamicCupSharePlugin plugin, object[] args)
        {
            if (plugin == null || args == null || args.Length == 0) return;

            if (args.Length >= 2 && args[1] is System.Collections.Generic.List<ulong> disbanded)
            {
                plugin.OnClanDisbanded(args[0]?.ToString(), disbanded);
                return;
            }

            if (args[0] is System.Collections.Generic.List<ulong> memberUlongs)
            {
                plugin.OnClanDisbanded(null, memberUlongs);
                return;
            }

            if (args[0] is System.Collections.Generic.List<string> memberStrings)
            {
                var ids = new System.Collections.Generic.List<ulong>(memberStrings.Count);
                for (int i = 0; i < memberStrings.Count; i++)
                {
                    if (ulong.TryParse(memberStrings[i], out ulong id))
                        ids.Add(id);
                }
                plugin.OnClanDisbanded(null, ids);
            }
        }

        private static bool TryToUlong(object value, out ulong id)
        {
            id = 0UL;
            if (value == null) return false;
            if (value is ulong u)
            {
                id = u;
                return id != 0UL;
            }
            return ulong.TryParse(value.ToString(), out id) && id != 0UL;
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
