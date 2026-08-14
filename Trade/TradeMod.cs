using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Trade
{
    public class TradeMod : IHarmonyModHooks
    {
        public static TradeMod Instance { get; private set; }
        public const string AppDomainApiKey = "Trade_ApiType";
        public const int VersionMajor = 1;
        public const int VersionMinor = 2;
        public const int VersionPatch = 15;

        private TradePlugin _plugin;
        private Action _permissionsReadyCallback;
        private readonly HashSet<string> _chatCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "trade" };
        private readonly List<ConsoleSystem.Command> _commands = new List<ConsoleSystem.Command>();
        private GameObject _runner;
        private bool _serverReady;

        public TradePlugin Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            try
            {
                _plugin = new TradePlugin(root);
                _plugin.Load();
            }
            catch (Exception ex)
            {
                Debug.LogError("[Trade] FAIL: construct/config: " + ex);
                return;
            }

            AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(TradeMod));
            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);
            EnsureRunner();
            _runner.GetComponent<TradeRunner>().Begin(this);
            RegisterConsoleCommands();
            Debug.Log($"[Trade] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
        }

        private void OnPermissionsReady()
        {
            try { _plugin?.RegisterPermissions(); }
            catch (Exception ex) { Debug.LogWarning("[Trade] Permissions: " + ex.Message); }
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || _plugin == null) return;
            _serverReady = true;
            try
            {
                _plugin.RegisterPermissions();
                Debug.Log("[Trade] OK: Server initialized.");
            }
            catch (Exception ex) { Debug.LogError("[Trade] FAIL: OnServerInitialized: " + ex); }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            try
            {
                if (_permissionsReadyCallback != null)
                    PermissionsBridge.UnregisterReadyCallback(_permissionsReadyCallback);
            }
            catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); } catch { }
            UnregisterConsoleCommands();
            try { _plugin?.Unload(); } catch { }
            _plugin = null;
            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }
            Instance = null;
            Debug.Log("[Trade] OK: Unloaded.");
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || _plugin == null || string.IsNullOrEmpty(command)) return false;
            if (!_chatCommands.Contains(command)) return false;
            _plugin.CmdChatTrade(player, command, args);
            return true;
        }

        /// <summary>API for other mods: detect Trade shop-front boxes.</summary>
        public static bool IsTradeBox(BaseNetworkable bn) =>
            Instance?.Plugin != null && Instance.Plugin.IsTradeBox(bn);

        internal void Delay(Action action, float delay) =>
            _runner?.GetComponent<TradeRunner>()?.Delay(action, delay);

        private void RegisterConsoleCommands()
        {
            try
            {
                var cmd = new ConsoleSystem.Command
                {
                    Name = "trade",
                    FullName = "global.trade",
                    Variable = false,
                    ServerAdmin = true,
                    ServerUser = true,
                    AllowRunFromServer = true,
                    Call = a =>
                    {
                        var player = a?.Player();
                        if (player == null || a.Args == null || a.Args.Length == 0) return;
                        _plugin?.CmdChatTrade(player, "trade", new[] { a.Args[0].ToString() });
                    }
                };
                _commands.Add(cmd);
                ConsoleSystem.Index.Server.Dict[cmd.FullName] = cmd;
                ConsoleSystem.Index.Server.GlobalDict[cmd.Name] = cmd;
            }
            catch (Exception ex) { Debug.LogWarning("[Trade] RegisterConsole: " + ex.Message); }
        }

        private void UnregisterConsoleCommands()
        {
            try
            {
                foreach (var cmd in _commands)
                {
                    ConsoleSystem.Index.Server.Dict?.Remove(cmd.FullName);
                    ConsoleSystem.Index.Server.GlobalDict?.Remove(cmd.Name);
                }
            }
            catch { }
            _commands.Clear();
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("Trade_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<TradeRunner>();
        }
    }

    internal sealed class TradeRunner : MonoBehaviour
    {
        private TradeMod _mod;
        private bool _started;

        public void Begin(TradeMod mod)
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

        public void Delay(Action action, float delay)
        {
            if (action == null) return;
            StartCoroutine(DelayCo(action, delay));
        }

        private IEnumerator DelayCo(Action action, float delay)
        {
            if (delay <= 0f) yield return null;
            else yield return new WaitForSeconds(delay);
            try { action(); } catch (Exception ex) { Debug.LogWarning("[Trade] delayed: " + ex.Message); }
        }
    }
}
