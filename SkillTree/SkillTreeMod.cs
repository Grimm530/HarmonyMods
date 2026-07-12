// SkillTreeMod.cs  --  Harmony entry point for SkillTree 1.7.x
// Hosts Oxide.Plugins.SkillTree, drives Init/Loaded/OnServerInitialized/Unload
// lifecycle, registers chat + console commands, routes /st chat, etc.
// Pattern follows KitsHarmonyMod and ArmoredTrainMod.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using OxidePlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony
{
    /// <summary>Persistent MonoBehaviour: NextTick queue + StartCoroutine for timers.</summary>
    public class ModRunner : MonoBehaviour
    {
        public static ModRunner Instance { get; private set; }
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static GameObject _go;

        public static void Ensure()
        {
            if (Instance != null) return;
            _go = new GameObject("SkillTree_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _go.hideFlags = HideFlags.HideAndDontSave;
            Instance = _go.AddComponent<ModRunner>();
        }

        public static void Destroy()
        {
            lock (_queue) _queue.Clear();
            if (_go != null) { UnityEngine.Object.Destroy(_go); _go = null; Instance = null; }
        }

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            lock (_queue) _queue.Enqueue(action);
        }

        private void Update()
        {
            while (true)
            {
                Action a;
                lock (_queue) { if (_queue.Count == 0) break; a = _queue.Dequeue(); }
                try { a(); }
                catch (Exception ex) { Debug.LogWarning("[SkillTree] NextTick: " + ex.Message); }
            }
        }
    }

    /// <summary>Harmony mod entry point for SkillTree.</summary>
    public class SkillTreeMod : IHarmonyModHooks
    {
        public static SkillTreeMod Instance { get; private set; }

        // The live plugin instance (accessed via the partial-class helper to avoid private access).
        public static OxidePlugin Plugin => OxidePlugin.GetModInstance();

        private Coroutine _initCoroutine;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Action _permissionsReadyCallback;

        public const string AppDomainApiKey = "SkillTree_ApiType";

        // ---- IHarmonyModHooks ---------------------------------------------

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            ModRunner.Ensure();

            OxidePlugin plugin;
            try
            {
                plugin = new OxidePlugin();
                OxidePlugin.SetInstance(plugin);
                plugin.HarmonyLoadConfig();
            }
            catch (Exception ex)
            {
                Debug.LogError("[SkillTree] Failed to construct/config plugin: " + ex);
                return;
            }

            // Expose via AppDomain for other mods that call SkillTree hooks.
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(SkillTreeMod)); }
            catch { }

            // Seed basic chat commands (config-independent defaults).
            foreach (var cmd in new[] { "st", "skilltree", "skills" })
                _chatCommandNames.Add(cmd);
            foreach (var cmd in new[] { "score", "scoreboard" })
                _chatCommandNames.Add(cmd);

            // Register the UI console commands immediately.
            RegisterConsoleCommands();

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            // Start init coroutine (waits for ServerMgr + ItemManager).
            _initCoroutine = ModRunner.Instance.StartCoroutine(WaitForServerThenInit());

            Debug.Log("[SkillTree] Harmony mod loaded. Chat: /st /skilltree /skills. Config: HarmonyConfig/SkillTree.json. Data: HarmonyData/. Custom player data: see config CustomSkillTreeDataDirectory.");
        }

        private void OnPermissionsReady()
        {
            try
            {
                var plugin = OxidePlugin.GetModInstance();
                if (plugin == null) return;
                var mi = typeof(OxidePlugin).GetMethod("HandlePermissions", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                mi?.Invoke(plugin, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkillTree] Permissions ready re-register: " + ex.Message);
            }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            if (_permissionsReadyCallback != null)
            {
                PermissionsBridge.UnregisterReadyCallback(_permissionsReadyCallback);
                _permissionsReadyCallback = null;
            }

            if (_initCoroutine != null && ModRunner.Instance != null)
            {
                ModRunner.Instance.StopCoroutine(_initCoroutine);
                _initCoroutine = null;
            }

            OxidePlugin.GetModInstance()?.timer?.DestroyAll();
            OxidePlugin.GetModInstance()?.CallUnload();

            UnregisterConsoleCommands();

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); }
            catch { }

            ModRunner.Destroy();
            OxidePlugin.ClearInstance();
            Instance = null;
            Debug.Log("[SkillTree] Harmony mod unloaded.");
        }

        // ---- Init coroutine ---------------------------------------------

        private IEnumerator WaitForServerThenInit(int attempt = 0)
        {
            while (ServerMgr.Instance == null) yield return null;
            while (ItemManager.itemList == null || ItemManager.itemList.Count == 0)
            {
                if (attempt > 120)
                {
                    Debug.LogWarning("[SkillTree] ItemManager timeout; proceeding.");
                    break;
                }
                yield return new WaitForSeconds(attempt < 10 ? 0.5f : 1f);
                attempt++;
            }

            yield return new WaitForSeconds(1f); // let other mods finish loading

            var plugin = Plugin;
            if (plugin == null) yield break;

            // Oxide order: Init -> Loaded -> OnServerInitialized.
            try { plugin.CallInit(); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] Init: " + ex.Message); }

            try { plugin.ResolvePluginReferences(); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] PluginReference bind: " + ex.Message); }

            try { plugin.CallLoaded(); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] Loaded: " + ex.Message); }

            try { plugin.CallOnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[SkillTree] OnServerInitialized: " + ex); }

            // After Init/OnServerInitialized, cmd may have new registrations.
            RefreshDynamicCommands();

            _initCoroutine = null;
            Debug.Log("[SkillTree] Server initialized.");
        }

        // ---- Chat command routing ---------------------------------------

        /// <summary>Called by Chat_Say_Patch for messages starting with /.</summary>
        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrWhiteSpace(message)) return false;
            message = message.Trim();
            if (message.StartsWith("/")) message = message.Substring(1).Trim();

            string[] parts = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            string commandName = parts[0].ToLowerInvariant();
            if (!_chatCommandNames.Contains(commandName)) return false;

            string[] args = parts.Skip(1).ToArray();

            // Check if it's a dynamic cmd-registered command.
            var plugin = Plugin;
            if (plugin == null) return false;

            // Try registered commands first (from cmd.AddChatCommand).
            foreach (var reg in plugin.cmd.RegisteredChatCommands)
            {
                if (!string.Equals(reg.name, commandName, StringComparison.OrdinalIgnoreCase)) continue;
                InvokeChatMethod(plugin, reg.method, player, commandName, args);
                return true;
            }

            // Default: the "st"/"skilltree"/"skills" and "score"/"scoreboard" commands.
            if (new[] { "st", "skilltree", "skills" }.Contains(commandName, StringComparer.OrdinalIgnoreCase))
            {
                InvokeChatMethod(plugin, "SendMenuCMD", player, commandName, args);
                return true;
            }
            if (new[] { "score", "scoreboard" }.Contains(commandName, StringComparer.OrdinalIgnoreCase))
            {
                InvokeChatMethod(plugin, "CheckScoreBoard", player, commandName, args);
                return true;
            }

            return false;
        }

        private static void InvokeChatMethod(OxidePlugin plugin, string methodName, BasePlayer player, string command, string[] args)
        {
            if (string.IsNullOrEmpty(methodName) || plugin == null || player == null) return;
            try
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var type = typeof(OxidePlugin);

                // Try (BasePlayer, string, string[]) — standard Oxide signature.
                var mi = type.GetMethod(methodName, bf, null, new[] { typeof(BasePlayer), typeof(string), typeof(string[]) }, null);
                if (mi != null) { mi.Invoke(plugin, new object[] { player, command, args }); return; }

                // Try (BasePlayer) — SkillTree-specific compact signature.
                mi = type.GetMethod(methodName, bf, null, new[] { typeof(BasePlayer) }, null);
                if (mi != null) { mi.Invoke(plugin, new object[] { player }); return; }

                // Try (ConsoleSystem.Arg) — some chat commands share a console handler.
                mi = type.GetMethod(methodName, bf, null, new[] { typeof(ConsoleSystem.Arg) }, null);
                if (mi != null)
                {
                    var sb = new StringBuilder(command);
                    foreach (var a in args) sb.Append(' ').Append(a);
                    var opt = ConsoleSystem.Option.Server;
                    if (player.net?.connection != null) opt = opt.FromConnection(player.net.connection);
                    mi.Invoke(plugin, new object[] { new ConsoleSystem.Arg(opt, sb.ToString()) });
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkillTree] InvokeChatMethod " + methodName + ": " + ex.Message);
            }
        }

        // ---- Console command registration --------------------------------

        private void RegisterConsoleCommands()
        {
            // Core UI console command (sent by CUI buttons).
            RegisterConsole("ST_UI", arg => InvokeConsoleMethod("UI_SkillTree", arg), serverAdmin: false);
        }

        private void RefreshDynamicCommands()
        {
            var plugin = Plugin;
            if (plugin == null) return;
            try
            {
                foreach (var reg in plugin.cmd.RegisteredChatCommands)
                {
                    if (!string.IsNullOrEmpty(reg.name))
                        _chatCommandNames.Add(reg.name.ToLowerInvariant());
                }

                foreach (var reg in plugin.cmd.RegisteredConsoleCommands)
                {
                    if (string.IsNullOrEmpty(reg.name)) continue;
                    var name = reg.name.ToLowerInvariant();
                    // Avoid double-registration.
                    if (_registeredCommands.Any(c => string.Equals(c.FullName, "global." + name, StringComparison.OrdinalIgnoreCase) ||
                                                     string.Equals(c.FullName, name, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var captured = reg;
                    RegisterConsole(name, arg => InvokeConsoleMethod(captured.method, arg), serverAdmin: false);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkillTree] RefreshDynamicCommands: " + ex.Message);
            }
        }

        private void InvokeConsoleMethod(string methodName, ConsoleSystem.Arg arg)
        {
            var plugin = Plugin;
            if (plugin == null || arg == null) return;
            try
            {
                var mi = typeof(OxidePlugin).GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                mi?.Invoke(plugin, new object[] { arg });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkillTree] " + methodName + ": " + ex.Message);
            }
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler, bool serverAdmin = false)
        {
            bool hasDot  = name.Contains(".");
            string parent  = hasDot ? name.Split('.')[0] : "";
            string cmdName = hasDot ? name.Split(new[] { '.' }, 2)[1] : name;
            string fullName = hasDot ? name : "global." + name;
            string dictKey  = hasDot ? name : fullName;

            var captured = name;
            var cmd = new ConsoleSystem.Command
            {
                Name              = cmdName,
                Parent            = parent,
                FullName          = fullName,
                Variable          = false,
                ServerAdmin       = serverAdmin,
                AllowRunFromServer= true,
                Replicated        = false,
                Call              = a =>
                {
                    try { handler(a); }
                    catch (Exception ex) { Debug.LogWarning("[SkillTree] cmd " + captured + ": " + ex.Message); }
                }
            };

            ConsoleSystem.Index.Server.Dict[dictKey] = cmd;
            if (!hasDot && ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict[cmdName] = cmd;

            _registeredCommands.Add(cmd);
        }

        private void UnregisterConsoleCommands()
        {
            try
            {
                var dict       = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                foreach (var cmd in _registeredCommands)
                {
                    dict?.Remove(cmd.FullName);
                    dict?.Remove(cmd.Parent + "." + cmd.Name);
                    if (string.Equals(cmd.Parent, "global", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(cmd.Parent))
                        globalDict?.Remove(cmd.Name);
                }
            }
            catch { }
            _registeredCommands.Clear();
            _chatCommandNames.Clear();
        }
    }
}
