using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Oxide.Ext.Chaos.UIFramework;
using UnityEngine;

namespace PlayerSkinsHarmony
{
    /// <summary>
    /// Harmony entry for PlayerSkins 3.0.141 (Chaos UI port).
    /// Load order: 0Permissions -> Economics (optional) -> PlayerSkins
    /// </summary>
    public class PlayerSkinsMod : IHarmonyModHooks
    {
        public static PlayerSkinsMod Instance { get; private set; }

        public const int VersionMajor = 3;
        public const int VersionMinor = 0;
        public const int VersionPatch = 141;

        private PlayerSkinsPlugin _plugin;
        private Action _permissionsReadyCallback;
        private GameObject _runner;
        private bool _serverReady;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();

        public PlayerSkinsPlugin Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            try
            {
                PlayerSkinsHost.Init(root);
                _plugin = new PlayerSkinsPlugin();
                _plugin.HarmonyInit();
            }
            catch (Exception ex)
            {
                Debug.LogError("[PlayerSkins] FAIL: construct/config: " + ex);
                return;
            }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            RegisterConsoleCommands();
            EnsureRunner();
            _runner.GetComponent<PlayerSkinsRunner>().Begin(this);

            Debug.Log($"[PlayerSkins] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[PlayerSkins] -> Config: HarmonyConfig/PlayerSkins.json");
            Debug.Log("[PlayerSkins] -> Data: HarmonyData/PlayerSkins/");
            Debug.Log("[PlayerSkins] -> Lang: HarmonyLanguage/PlayerSkins.json (optional)");
            Debug.Log("[PlayerSkins] -> Load order: 0Permissions -> PlayerSkins");
        }

        private void OnPermissionsReady()
        {
            try
            {
                RegisterPermissions();
                Debug.Log("[PlayerSkins] OK: Permissions ready - perms re-registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerSkins] FAIL: Permissions ready: " + ex.Message);
            }
        }

        private void RegisterPermissions()
        {
            PermissionsBridge.RegisterPermission("playerskins.shop");
            PermissionsBridge.RegisterPermission("playerskins.reskin");
            PermissionsBridge.RegisterPermission("playerskins.nocharge");
            PermissionsBridge.RegisterPermission("playerskins.admin");
            PermissionsBridge.RegisterPermission("playerskins.addskin");

            var cfg = _plugin?.Configuration;
            if (cfg?.Shop?.Permissions != null)
            {
                foreach (string perm in cfg.Shop.Permissions)
                {
                    if (!string.IsNullOrEmpty(perm))
                        PermissionsBridge.RegisterPermission(perm);
                }
            }
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || _plugin == null) return;
            _serverReady = true;
            try
            {
                RegisterPermissions();
                _plugin.HarmonyServerInitialized();
                RefreshChatCommandsFromConfig();
                Debug.Log("[PlayerSkins] OK: Server initialized.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[PlayerSkins] FAIL: OnServerInitialized: " + ex);
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

            UnregisterConsoleCommands();

            try
            {
                _plugin?.HarmonyUnload();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerSkins] Unload: " + ex.Message);
            }

            _plugin = null;
            PlayerSkinsHost.Shutdown();

            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }

            Instance = null;
            Debug.Log("[PlayerSkins] OK: Unloaded.");
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("PlayerSkins_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<PlayerSkinsRunner>();
        }

        private void RefreshChatCommandsFromConfig()
        {
            // Chat commands are resolved dynamically in TryHandleChat from config.
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (_plugin == null || player == null) return false;
            var cfg = _plugin.Configuration;
            if (cfg?.Commands == null) return false;

            if (string.Equals(command, cfg.Commands.DefaultCommand, StringComparison.OrdinalIgnoreCase))
            {
                _plugin.cmdSkin(player, command, args);
                return true;
            }
            if (string.Equals(command, cfg.Commands.ReskinCommand, StringComparison.OrdinalIgnoreCase))
            {
                _plugin.cmdReSkin(player, command, args);
                return true;
            }
            if (string.Equals(command, cfg.Commands.ShopCommand, StringComparison.OrdinalIgnoreCase))
            {
                _plugin.cmdSkinShop(player, command, args);
                return true;
            }
            string addSkin = cfg.Commands.AddSkinCommand ?? "addskin";
            if (!string.IsNullOrEmpty(addSkin) &&
                string.Equals(command, addSkin, StringComparison.OrdinalIgnoreCase))
            {
                _plugin.cmdAddSkin(player, command, args);
                return true;
            }
            return false;
        }

        private void RegisterConsoleCommands()
        {
            RegisterConsole("playerskins.skins", arg => _plugin?.ccmdSkinManager(arg));
            RegisterConsole("playerskins.setprice", arg => _plugin?.ccmdSetSkinPrice(arg));
            RegisterConsole("playerskins.giveskin", arg => _plugin?.ccmdGiveSkin(arg));
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler, bool serverAdmin = true)
        {
            if (string.IsNullOrEmpty(name) || handler == null) return;
            bool hasDot = name.Contains(".");
            string cmdParent = "";
            string cmdName = name;
            string fullName = name;
            string dictKey = name;
            if (hasDot)
            {
                var parts = name.Split(new[] { '.' }, 2);
                cmdParent = parts[0];
                cmdName = parts[1];
            }

            try
            {
                var cmd = new ConsoleSystem.Command
                {
                    Name = cmdName,
                    Parent = cmdParent,
                    FullName = fullName,
                    Variable = false,
                    ServerAdmin = serverAdmin,
                    AllowRunFromServer = true,
                    Replicated = false,
                    Call = a =>
                    {
                        try { handler(a); }
                        catch (Exception ex) { Debug.LogWarning("[PlayerSkins] command " + name + ": " + ex.Message); }
                    }
                };
                ConsoleSystem.Index.Server.Dict[dictKey] = cmd;
                _registeredCommands.Add(cmd);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerSkins] RegisterConsole " + name + ": " + ex.Message);
            }
        }

        private void UnregisterConsoleCommands()
        {
            foreach (var cmd in _registeredCommands)
            {
                try
                {
                    if (cmd?.FullName != null)
                        ConsoleSystem.Index.Server.Dict.Remove(cmd.FullName);
                }
                catch { }
            }
            _registeredCommands.Clear();
        }

        /// <summary>Route cui.endtest PLAYERSKINS to CommandCallbackHandler.</summary>
        public void HandleCuiCallback(ConsoleSystem.Arg args, Array a)
        {
            if (_plugin?.CallbackHandler == null || a == null || a.Length < 1) return;
            var player = args.Connection?.player as BasePlayer ?? args.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return;

            var sb = new StringBuilder("playerskins.callback");
            int start = 1;
            if (a.Length >= 2)
            {
                string second = a.GetValue(1)?.ToString() ?? "";
                if (second.Equals("playerskins.callback", StringComparison.OrdinalIgnoreCase) ||
                    second.StartsWith("playerskins.callback", StringComparison.OrdinalIgnoreCase))
                {
                    start = 2;
                    if (second.Length > "playerskins.callback".Length)
                    {
                        var rest = second.Substring("playerskins.callback".Length).Trim();
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
                Debug.LogWarning("[PlayerSkins] cui.endtest PLAYERSKINS: " + ex);
            }
        }

        internal void OnServerSave()
        {
            try { _plugin?.OnServerSave(); }
            catch (Exception ex) { Debug.LogWarning("[PlayerSkins] OnServerSave: " + ex.Message); }
        }
    }

    internal sealed class PlayerSkinsRunner : MonoBehaviour
    {
        private PlayerSkinsMod _mod;
        private bool _started;

        public void Begin(PlayerSkinsMod mod)
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

            float wait = 0f;
            while (wait < 120f)
            {
                if (ItemManager.itemList != null && ItemManager.itemList.Count > 0)
                    break;
                yield return new WaitForSeconds(0.5f);
                wait += 0.5f;
            }

            yield return new WaitForSeconds(1f);
            _mod?.OnServerInitialized();
        }
    }
}
