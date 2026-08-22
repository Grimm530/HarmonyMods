using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyChat;
using UnityEngine;

namespace RemoverToolHarmony
{
    /// <summary>
    /// Harmony entry point for Remover Tool 4.3.431. Hosts the ported plugin, registers its
    /// console commands ([ConsoleCommand]) and the chat command (from config), and routes chat.
    /// </summary>
    public class RemoverToolHarmonyMod : IHarmonyModHooks
    {
        public static RemoverToolHarmonyMod Instance { get; private set; }

        public const int VersionMajor = 4;
        public const int VersionMinor = 3;
        public const int VersionPatch = 431;

        public static readonly VersionNumber Version = new VersionNumber(VersionMajor, VersionMinor, VersionPatch);

        public const string AppDomainApiKey = "RemoverTool_ApiType";
        public const string AppDomainPluginKey = "RemoverTool_Plugin";

        private RemoverTool _plugin;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();

        public RemoverTool Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
            RemoverToolHost.Init(root);
            _plugin = new RemoverTool();
            RemoverToolHost.Instance.Plugin = _plugin;
            RegisterApiType();
            try { _plugin.HarmonyInit(); }
            catch (Exception ex) { Debug.LogError("[RemoverTool Harmony] FAIL: HarmonyInit -> " + ex); }
            RegisterConsoleCommands();
            ChatSayBridge.Register("RemoverTool", OnChatCommand);
            ScheduleServerInitialized();
            Debug.Log($"[RemoverTool Harmony] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            try { ChatSayBridge.Unregister("RemoverTool"); } catch { }
            UnregisterCommands();
            try { _plugin?.HarmonyUnload(); }
            catch (Exception ex) { Debug.LogWarning("[RemoverTool Harmony] HarmonyUnload: " + ex.Message); }
            UnregisterApiType();
            RemoverToolHost.Shutdown();
            _plugin = null;
            Instance = null;
        }

        #region ServerInitialized scheduling

        private void ScheduleServerInitialized(int attempt = 0)
        {
            if (_plugin == null) return;
            try
            {
                bool itemsReady = ItemManager.itemList != null && ItemManager.itemList.Count > 0;
                if (itemsReady && ServerMgr.Instance != null)
                {
                    _plugin.HarmonyServerInitialized();
                    Debug.Log($"[RemoverTool Harmony] OK: Server initialized (v{VersionMajor}.{VersionMinor}.{VersionPatch})");
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RemoverTool Harmony] ScheduleServerInitialized: " + ex.Message);
            }

            if (attempt > 120)
            {
                try { _plugin.HarmonyServerInitialized(); }
                catch (Exception ex) { Debug.LogError("[RemoverTool Harmony] FAIL: Init -> " + ex); }
                return;
            }

            float delay = attempt < 10 ? 0.5f : 1f;
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(() => ScheduleServerInitialized(attempt + 1), delay);
            else
            {
                try
                {
                    var go = new GameObject("RemoverToolHarmony_InitWait");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<InitWaitBehaviour>().Begin(this, attempt);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[RemoverTool Harmony] ScheduleServerInitialized: " + ex.Message);
                }
            }
        }

        private class InitWaitBehaviour : MonoBehaviour
        {
            private RemoverToolHarmonyMod _mod;
            private int _attempt;
            public void Begin(RemoverToolHarmonyMod mod, int attempt)
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

        #endregion

        #region AppDomain API

        private void RegisterApiType()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(RemoverToolHarmonyMod));
                AppDomain.CurrentDomain.SetData(AppDomainPluginKey, _plugin);
            }
            catch (Exception ex) { Debug.LogWarning("[RemoverTool Harmony] RegisterApiType: " + ex.Message); }
        }

        private void UnregisterApiType()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, null);
                AppDomain.CurrentDomain.SetData(AppDomainPluginKey, null);
            }
            catch { }
        }

        #endregion

        #region Console commands ([ConsoleCommand] discovery)

        private void RegisterConsoleCommands()
        {
            if (_plugin == null) return;
            var methods = typeof(RemoverTool).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var mi in methods)
            {
                var attr = mi.GetCustomAttribute<ConsoleCommandAttribute>();
                if (attr == null || string.IsNullOrEmpty(attr.Command)) continue;
                var method = mi;
                RegisterConsole(attr.Command, arg =>
                {
                    try { method.Invoke(_plugin, new object[] { arg }); }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[RemoverTool] console {attr.Command}: " + (ex.InnerException?.Message ?? ex.Message));
                    }
                });
            }
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler, bool serverAdmin = false)
        {
            var localName = name;
            bool hasDot = localName.Contains(".");
            string cmdParent = "";
            string cmdName = localName;
            string fullName;
            string dictKey;

            if (hasDot)
            {
                var parts = localName.Split(new[] { '.' }, 2);
                cmdParent = parts[0];
                cmdName = parts[1];
                fullName = localName;
                dictKey = localName;
            }
            else
            {
                fullName = "global." + localName;
                dictKey = fullName;
            }

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
                    catch (Exception ex) { Debug.LogWarning($"[RemoverTool] command {localName}: " + ex.Message); }
                }
            };

            ConsoleSystem.Index.Server.Dict[dictKey] = cmd;
            if (!hasDot && ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict[cmdName] = cmd;

            _registeredCommands.Add(cmd);
        }

        private void UnregisterCommands()
        {
            foreach (var cmd in _registeredCommands)
            {
                try
                {
                    string dictKey = string.IsNullOrEmpty(cmd.Parent) ? "global." + cmd.Name : cmd.FullName;
                    ConsoleSystem.Index.Server.Dict?.Remove(dictKey);
                    if (string.IsNullOrEmpty(cmd.Parent))
                        ConsoleSystem.Index.Server.GlobalDict?.Remove(cmd.Name);
                }
                catch { }
            }
            _registeredCommands.Clear();
        }

        #endregion

        #region Chat routing

        /// <summary>
        /// ChatSayBridge entry: full message including leading slash.
        /// </summary>
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

        /// <summary>
        /// Routes a chat command to CmdRemove. Returns true if handled (suppress chat broadcast).
        /// Always accepts "remove" plus whatever was registered from config.
        /// </summary>
        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || _plugin == null || string.IsNullOrEmpty(command)) return false;

            bool matched = string.Equals(command, "remove", StringComparison.OrdinalIgnoreCase);
            if (!matched)
            {
                var registrations = RemoverToolHost.Instance?.ChatCommands;
                if (registrations != null)
                {
                    foreach (var reg in registrations)
                    {
                        if (string.Equals(reg.Command, command, StringComparison.OrdinalIgnoreCase))
                        {
                            matched = true;
                            break;
                        }
                    }
                }
            }

            // Config may use a custom chat command name
            if (!matched)
            {
                try
                {
                    string cfgCmd = _plugin.GetChatCommandName();
                    if (!string.IsNullOrEmpty(cfgCmd) &&
                        string.Equals(cfgCmd, command, StringComparison.OrdinalIgnoreCase))
                        matched = true;
                }
                catch { }
            }

            if (!matched) return false;

            try
            {
                _plugin.CmdRemove(player, command, args ?? Array.Empty<string>());
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RemoverTool] chat " + command + ": " + (ex.InnerException?.Message ?? ex.Message));
                try { player.ChatMessage("[RemoverTool] Error: " + (ex.InnerException?.Message ?? ex.Message)); } catch { }
            }
            return true;
        }

        #endregion
    }
}
