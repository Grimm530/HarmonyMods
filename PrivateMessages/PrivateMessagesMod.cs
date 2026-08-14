using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PrivateMessagesHarmony
{
    public class PrivateMessagesMod : IHarmonyModHooks
    {
        public static PrivateMessagesMod Instance { get; private set; }
        public static PrivateMessagesPlugin Plugin { get; private set; }

        public const int VersionMajor = 1;
        public const int VersionMinor = 1;
        public const int VersionPatch = 12;

        private Action _permissionsReadyCallback;
        private readonly HashSet<string> _pmCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _replyCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _historyCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private GameObject _runner;
        private bool _serverReady;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            try
            {
                Plugin = new PrivateMessagesPlugin(root);
                Plugin.LoadConfig();
                Plugin.LoadDefaultMessages();
            }
            catch (Exception ex)
            {
                Debug.LogError("[PrivateMessages] FAIL: construct/config: " + ex);
                return;
            }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            RefreshChatCommands();
            EnsureRunner();
            _runner.GetComponent<PrivateMessagesRunner>().Begin(this);

            Debug.Log($"[PrivateMessages] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[PrivateMessages] -> Config: HarmonyConfig/PrivateMessages.json");
            Debug.Log("[PrivateMessages] -> Lang: HarmonyLanguage/PrivateMessages.json");
            Debug.Log("[PrivateMessages] Chat: /pm /r /reply /msg /tell /send /pmhistory");
        }

        private void OnPermissionsReady()
        {
            try
            {
                Plugin?.RegisterPermissions();
                Debug.Log("[PrivateMessages] OK: Permissions ready — perms re-registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PrivateMessages] FAIL: Permissions ready: " + ex.Message);
            }
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || Plugin == null) return;
            _serverReady = true;
            try
            {
                Plugin.RegisterPermissions();
                RefreshChatCommands();
                Debug.Log("[PrivateMessages] OK: Server initialized.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[PrivateMessages] FAIL: OnServerInitialized: " + ex);
            }
        }

        public void RefreshChatCommands()
        {
            _pmCommands.Clear();
            _replyCommands.Clear();
            _historyCommands.Clear();

            foreach (string cmd in new[] { "pm", "send", "msg", "tell" })
                _pmCommands.Add(cmd);
            if (Plugin?.Config != null && !string.IsNullOrWhiteSpace(Plugin.Config.PmCommand))
                _pmCommands.Add(Plugin.Config.PmCommand.Trim());

            _replyCommands.Add("r");
            _replyCommands.Add("reply");

            if (Plugin?.Config == null || Plugin.Config.EnableHistory)
                _historyCommands.Add("pmhistory");
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || Plugin == null || string.IsNullOrEmpty(command)) return false;
            if (_pmCommands.Contains(command))
            {
                Plugin.CommandPrivateMessage(player, command, args);
                return true;
            }
            if (_replyCommands.Contains(command))
            {
                Plugin.CommandReply(player, command, args);
                return true;
            }
            if (_historyCommands.Contains(command))
            {
                Plugin.CommandHistory(player, command, args);
                return true;
            }
            return false;
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
            Debug.Log("[PrivateMessages] OK: Unloaded.");
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("PrivateMessages_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<PrivateMessagesRunner>();
        }
    }

    internal sealed class PrivateMessagesRunner : MonoBehaviour
    {
        private PrivateMessagesMod _mod;
        private bool _started;

        public void Begin(PrivateMessagesMod mod)
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
