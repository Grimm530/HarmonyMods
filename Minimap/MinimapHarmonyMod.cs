using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyChat;
using MinimapHarmony.Patches;
using Oxide.Ext.Chaos.UIFramework;
using UnityEngine;

namespace MinimapHarmony
{
    public class MinimapHarmonyMod : IHarmonyModHooks
    {
        public static MinimapHarmonyMod Instance { get; private set; }

        public const int VersionMajor = 1;
        public const int VersionMinor = 3;
        public const int VersionPatch = 1;

        public static readonly VersionNumber Version = new VersionNumber(VersionMajor, VersionMinor, VersionPatch);

        public const string AppDomainApiKey = "Minimap_ApiType";
        public const string AppDomainPluginKey = "Minimap_Plugin";

        private Minimap _plugin;
        private Action _permissionsReadyCallback;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();

        public Minimap Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            MinimapHost.Init(root);
            _plugin = new Minimap();

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(MinimapHarmonyMod)); }
            catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainPluginKey, _plugin); }
            catch { }

            _plugin.HarmonyInit();
            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);
            ChatSayBridge.Register("Minimap", OnChatCommand);
            RegisterConsoleCommands();
            ScheduleServerInitialized();
            Debug.Log($"[Minimap Harmony] Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[Minimap Harmony] Chat: /map");
        }

        private void OnPermissionsReady()
        {
            try
            {
                _plugin?.RegisterPermissions();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] Permissions ready re-register: " + ex.Message);
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
                    DeferredFogPatches.Apply();
                    try
                    {
                        _plugin.HarmonyServerInitialized();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("[Minimap Harmony] HarmonyServerInitialized: " + ex);
                    }
                    Debug.Log($"[Minimap Harmony] Server initialized (v{VersionMajor}.{VersionMinor}.{VersionPatch})");
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap Harmony] ScheduleServerInitialized: " + ex.Message);
            }

            if (attempt > 60)
            {
                try
                {
                    DeferredFogPatches.Apply();
                    _plugin.HarmonyServerInitialized();
                    Debug.Log($"[Minimap Harmony] Server initialized (v{VersionMajor}.{VersionMinor}.{VersionPatch})");
                }
                catch (Exception ex) { Debug.LogError("[Minimap Harmony] Init failed: " + ex); }
                return;
            }

            float delay = attempt < 10 ? 0.5f : 1f;
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(() => ScheduleServerInitialized(attempt + 1), delay);
            else
            {
                try
                {
                    var go = new GameObject("MinimapHarmony_InitWait");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<InitWaitBehaviour>().Begin(this, attempt);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Minimap Harmony] ScheduleServerInitialized: " + ex.Message);
                }
            }
        }

        private class InitWaitBehaviour : MonoBehaviour
        {
            private MinimapHarmonyMod _mod;
            private int _attempt;

            public void Begin(MinimapHarmonyMod mod, int attempt)
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

            UnregisterConsoleCommands();
            ChatSayBridge.Unregister("Minimap");

            try
            {
                _plugin?.CallbackHandler?.Clear();
                _plugin?.CallbackHandler?.Unregister();
                _plugin?.HarmonyUnload();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap Harmony] Unload: " + ex.Message);
            }

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); } catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainPluginKey, null); } catch { }

            MinimapHost.Shutdown();
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
            if (!string.Equals(parts[0], "map", StringComparison.OrdinalIgnoreCase))
                return false;

            var args = parts.Skip(1).ToArray();
            try
            {
                _plugin.cmdMinimap(player, "map", args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] /map: " + (ex.InnerException?.Message ?? ex.Message));
            }
            return true;
        }

        public void HandleCuiCallback(ConsoleSystem.Arg args, Array a)
        {
            if (_plugin?.CallbackHandler == null || a == null || a.Length < 1) return;
            var player = args.Connection?.player as BasePlayer ?? args.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return;

            var sb = new StringBuilder("minimap.callback");
            int start = 1;
            if (a.Length >= 2)
            {
                string second = a.GetValue(1)?.ToString() ?? "";
                if (second.Equals("minimap.callback", StringComparison.OrdinalIgnoreCase) ||
                    second.StartsWith("minimap.callback", StringComparison.OrdinalIgnoreCase))
                {
                    start = 2;
                    if (second.Length > "minimap.callback".Length)
                    {
                        var rest = second.Substring("minimap.callback".Length).Trim();
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
                Debug.LogWarning("[Minimap] cui.endtest MINIMAP: " + ex);
            }
        }

        private void RegisterConsoleCommands()
        {
            RegisterConsole("minimap.regenerate", arg => _plugin?.ccmdRegenerate(arg), serverAdmin: true);
            RegisterConsole("minimap.render", arg => _plugin?.ccmdMinimapRender(arg), serverAdmin: true);
            RegisterConsole("minimap.reset", arg => _plugin?.ccmdMinimapReset(arg), serverAdmin: false);
            RegisterConsole("minimap.toggle", arg => _plugin?.ccmdMinimapToggle(arg), serverAdmin: false);
            RegisterConsole("minimap.zoom.in", arg => _plugin?.ccmdMinimapZoomIn(arg), serverAdmin: false);
            RegisterConsole("minimap.zoom.out", arg => _plugin?.ccmdMinimapZoomOut(arg), serverAdmin: false);
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler, bool serverAdmin)
        {
            if (string.IsNullOrEmpty(name) || handler == null) return;
            string cmdParent = "";
            string cmdName = name;
            if (name.Contains("."))
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
                    FullName = name,
                    Variable = false,
                    ServerAdmin = serverAdmin,
                    ServerUser = true,
                    AllowRunFromServer = true,
                    Replicated = false,
                    Call = a =>
                    {
                        try { handler(a); }
                        catch (Exception ex) { Debug.LogWarning("[Minimap] command " + name + ": " + ex.Message); }
                    }
                };
                ConsoleSystem.Index.Server.Dict[name] = cmd;
                _registeredCommands.Add(cmd);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] RegisterConsole " + name + ": " + ex.Message);
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
    }
}
