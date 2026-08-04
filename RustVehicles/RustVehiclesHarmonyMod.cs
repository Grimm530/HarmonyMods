using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RustVehiclesHarmony
{
    /// <summary>
    /// Harmony entry point for RustVehicles 2.0.5. Hosts the ported plugin, registers
    /// [ConsoleCommand] / [ChatCommand] methods, routes chat, and exposes Call() for HookMethod APIs.
    /// </summary>
    public class RustVehiclesHarmonyMod : IHarmonyModHooks
    {
        public static RustVehiclesHarmonyMod Instance { get; private set; }

        public const int VersionMajor = 2;
        public const int VersionMinor = 0;
        public const int VersionPatch = 5;

        public static readonly VersionNumber Version = new VersionNumber(VersionMajor, VersionMinor, VersionPatch);

        public const string AppDomainApiKey = "RustVehicles_ApiType";
        public const string AppDomainPluginKey = "RustVehicles_Plugin";

        /// <summary>True until the first successful HarmonyServerInitialized; then false for reloads.</summary>
        public static bool IsFirstServerInit { get; private set; } = true;

        private RustVehicles _plugin;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();
        private readonly Dictionary<string, MethodInfo> _chatMethods =
            new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);

        public RustVehicles Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
            RustVehiclesHost.Init(root);
            _plugin = new RustVehicles();
            RustVehiclesHost.Instance.Plugin = _plugin;
            PluginBridges.Wire(_plugin);
            RegisterApiType();
            try { _plugin.HarmonyInit(); }
            catch (Exception ex) { Debug.LogError("[RustVehicles Harmony] FAIL: HarmonyInit -> " + ex); }
            PluginBridges.Wire(_plugin);
            RegisterConsoleCommands();
            RegisterChatCommandsFromAttributes();
            RegisterChatCommandsFromCmdHelper();
            ScheduleServerInitialized();
            Debug.Log($"[RustVehicles Harmony] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            UnregisterCommands();
            try { _plugin?.HarmonyUnload(); }
            catch (Exception ex) { Debug.LogWarning("[RustVehicles Harmony] HarmonyUnload: " + ex.Message); }
            UnregisterApiType();
            RustVehiclesHost.Shutdown();
            _plugin = null;
            _chatMethods.Clear();
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
                    RegisterChatCommandsFromCmdHelper();
                    IsFirstServerInit = false;
                    Debug.Log($"[RustVehicles Harmony] OK: Server initialized (v{VersionMajor}.{VersionMinor}.{VersionPatch})");
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RustVehicles Harmony] ScheduleServerInitialized: " + ex.Message);
            }

            if (attempt > 120)
            {
                try
                {
                    _plugin.HarmonyServerInitialized();
                    RegisterChatCommandsFromCmdHelper();
                    IsFirstServerInit = false;
                }
                catch (Exception ex) { Debug.LogError("[RustVehicles Harmony] FAIL: Init -> " + ex); }
                return;
            }

            float delay = attempt < 10 ? 0.5f : 1f;
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(() => ScheduleServerInitialized(attempt + 1), delay);
            else
            {
                try
                {
                    var go = new GameObject("RustVehiclesHarmony_InitWait");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<InitWaitBehaviour>().Begin(this, attempt);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[RustVehicles Harmony] ScheduleServerInitialized: " + ex.Message);
                }
            }
        }

        private class InitWaitBehaviour : MonoBehaviour
        {
            private RustVehiclesHarmonyMod _mod;
            private int _attempt;
            public void Begin(RustVehiclesHarmonyMod mod, int attempt)
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
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(RustVehiclesHarmonyMod));
                // Expose the mod (has Call) so other mods can do Economics-style Plugin.Call bridges.
                AppDomain.CurrentDomain.SetData(AppDomainPluginKey, this);
            }
            catch (Exception ex) { Debug.LogWarning("[RustVehicles Harmony] RegisterApiType: " + ex.Message); }
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

        /// <summary>Oxide-style Call / HookMethod dispatcher for other mods.</summary>
        public object Call(string method, params object[] args)
        {
            if (_plugin == null || string.IsNullOrEmpty(method)) return null;
            try
            {
                args ??= Array.Empty<object>();
                var candidates = typeof(RustVehicles)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => string.Equals(m.Name, method, StringComparison.Ordinal))
                    .ToList();

                MethodInfo mi = candidates.FirstOrDefault(m => m.GetParameters().Length == args.Length);
                if (mi == null)
                {
                    // Prefer HookMethod attribute name match.
                    mi = typeof(RustVehicles)
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m =>
                        {
                            var attr = m.GetCustomAttribute<HookMethodAttribute>();
                            return attr != null &&
                                   string.Equals(attr.Name, method, StringComparison.Ordinal) &&
                                   m.GetParameters().Length == args.Length;
                        });
                }
                if (mi == null) return null;
                return mi.Invoke(_plugin, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RustVehicles Harmony] Call({method}): " + (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }

        #endregion

        #region Console commands ([ConsoleCommand] discovery)

        private void RegisterConsoleCommands()
        {
            if (_plugin == null) return;
            var methods = typeof(RustVehicles).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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
                        Debug.LogWarning($"[RustVehicles] console {attr.Command}: " + (ex.InnerException?.Message ?? ex.Message));
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
                    catch (Exception ex) { Debug.LogWarning($"[RustVehicles] command {localName}: " + ex.Message); }
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

        private void RegisterChatCommandsFromAttributes()
        {
            if (_plugin == null) return;
            var methods = typeof(RustVehicles).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var mi in methods)
            {
                var attr = mi.GetCustomAttribute<ChatCommandAttribute>();
                if (attr == null || string.IsNullOrEmpty(attr.Command)) continue;
                _chatMethods[attr.Command] = mi;
            }
        }

        private void RegisterChatCommandsFromCmdHelper()
        {
            var regs = RustVehiclesHost.Instance?.Cmd?.ChatCommands;
            if (regs == null || _plugin == null) return;
            foreach (var reg in regs)
            {
                if (string.IsNullOrEmpty(reg.Command) || string.IsNullOrEmpty(reg.MethodName)) continue;
                if (_chatMethods.ContainsKey(reg.Command)) continue;
                var mi = typeof(RustVehicles).GetMethod(reg.MethodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null)
                    _chatMethods[reg.Command] = mi;
            }
        }

        /// <summary>
        /// Routes a chat command to the matching [ChatCommand] / cmd.AddChatCommand method.
        /// Signature: (BasePlayer, string, string[]). Returns true if handled.
        /// </summary>
        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || _plugin == null || string.IsNullOrEmpty(command)) return false;
            if (!_chatMethods.TryGetValue(command, out var mi) || mi == null) return false;

            try
            {
                mi.Invoke(_plugin, new object[] { player, command, args ?? Array.Empty<string>() });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RustVehicles] chat " + command + ": " + (ex.InnerException?.Message ?? ex.Message));
                try { player.ChatMessage("[RustVehicles] Error: " + (ex.InnerException?.Message ?? ex.Message)); } catch { }
            }
            return true;
        }

        #endregion
    }
}
