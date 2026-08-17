// TruePVEMod.cs -- Harmony entry point for the TruePVE Oxide port (2.4.21).
// Hosts Oxide.Plugins.TruePVE, drives LoadConfig/Init/OnServerInitialized/Unload,
// registers chat + console commands (covalence bridge), and re-registers permissions
// when the Permissions Harmony mod becomes ready. Pattern follows SkillTreeMod.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Oxide.Core.Libraries.Covalence;
using OxidePlugin = Oxide.Plugins.TruePVE;

namespace TruePVEHarmony
{
    /// <summary>Persistent MonoBehaviour: NextTick queue + StartCoroutine host for timers.</summary>
    public class ModRunner : MonoBehaviour
    {
        public static ModRunner Instance { get; private set; }
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static GameObject _go;

        public static void Ensure()
        {
            if (Instance != null) return;
            _go = new GameObject("TruePVE_Runner");
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
                catch (Exception ex) { Debug.LogWarning("[TruePVE] NextTick: " + ex.Message); }
            }
        }
    }

    /// <summary>Harmony mod entry point for TruePVE.</summary>
    public class TruePVEMod : IHarmonyModHooks
    {
        public static TruePVEMod Instance { get; private set; }

        public static OxidePlugin Plugin => OxidePlugin.GetModInstance();

        private Coroutine _initCoroutine;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Action _permissionsReadyCallback;

        public const string AppDomainApiKey = "TruePVE_ApiType";

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
                plugin.HarmonyLoadDefaultMessages();
                plugin.HarmonyLoadConfig();
                // Hooks default to subscribed under Harmony; keep damage gated until Init re-subscribes after ruleset is ready.
                plugin.GateDamageHookUntilInit();
            }
            catch (Exception ex)
            {
                Debug.LogError("[TruePVE] Failed to construct/config plugin: " + ex);
                return;
            }

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(TruePVEMod)); }
            catch { }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            _initCoroutine = ModRunner.Instance.StartCoroutine(WaitForServerThenInit());

            Debug.Log("[TruePVE] Harmony mod loaded (Oxide port 2.4.22). Config: HarmonyConfig/TruePVE.json. server.pve browser tag supported (RuleSets own damage).");
        }

        private void OnPermissionsReady()
        {
            try
            {
                var plugin = Plugin;
                if (plugin == null) return;
                ReRegisterPermissions(plugin);
                plugin.ResolvePluginReferences();
            }
            catch (Exception ex) { Debug.LogWarning("[TruePVE] Permissions ready: " + ex.Message); }
        }

        private static void ReRegisterPermissions(OxidePlugin plugin)
        {
            // The plugin registers all permissions in Init(); re-invoking Init is unsafe,
            // so re-register the known static permission set directly through the bridge.
            string[] perms =
            {
                "truepve.canmap",
                "truepve.preventlooting.use", "truepve.preventlooting.admin",
                "truepve.preventlooting.player", "truepve.preventlooting.corpse",
                "truepve.preventlooting.backpack", "truepve.preventlooting.storage",
                "truepve.lootdefender.bypassbradleylock", "truepve.lootdefender.bypasshelilock",
                "truepve.lootdefender.bypassnpclock", "truepve.lootdefender.bypass.loot",
                "truepve.lootdefender.bypass.damage", "truepve.lootdefender.bypass.lockouts"
            };
            foreach (var p in perms) PermissionsBridge.RegisterPermission(p);
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
            Debug.Log("[TruePVE] Harmony mod unloaded.");
        }

        // ---- Init coroutine ----------------------------------------------

        private IEnumerator WaitForServerThenInit()
        {
            while (ServerMgr.Instance == null) yield return null;
            yield return new WaitForSeconds(1f); // let other mods finish loading

            var plugin = Plugin;
            if (plugin == null) yield break;

            // Oxide order: Init -> OnServerInitialized.
            try { plugin.CallInit(); }
            catch (Exception ex) { Debug.LogWarning("[TruePVE] Init: " + ex.Message); }

            try { plugin.ResolvePluginReferences(); }
            catch (Exception ex) { Debug.LogWarning("[TruePVE] PluginReference bind: " + ex.Message); }

            // Register commands added during Init.
            RegisterDynamicCommands();

            try { plugin.CallOnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[TruePVE] OnServerInitialized: " + ex); }

            // OnServerInitialized may add more commands (lockout UI etc.).
            RegisterDynamicCommands();

            _initCoroutine = null;
            Debug.Log("[TruePVE] Server initialized.");
        }

        // ---- Chat command routing ----------------------------------------

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

            // DynamicCupShare owns /share when loaded. PreventLooting still has /plshare.
            if ((commandName == "share" || commandName == "unshare") && DynamicCupShareLoaded())
                return false;

            var plugin = Plugin;
            if (plugin == null) return false;

            string[] cmdArgs = parts.Skip(1).ToArray();
            foreach (var reg in plugin.cmd.RegisteredChatCommands)
            {
                if (!string.Equals(reg.name, commandName, StringComparison.OrdinalIgnoreCase)) continue;
                InvokeCovalenceMethod(plugin, reg.method, WrapPlayer(player), commandName, cmdArgs);
                return true;
            }
            return false;
        }

        private static bool DynamicCupShareLoaded()
        {
            try { return AppDomain.CurrentDomain.GetData("DynamicCupShare_Plugin") != null; }
            catch { return false; }
        }

        private static IPlayer WrapPlayer(BasePlayer player)
            => player == null ? (IPlayer)new RustConsolePlayer() : new BasePlayerWrapper(player);

        private static void InvokeCovalenceMethod(OxidePlugin plugin, string methodName, IPlayer user, string command, string[] args)
        {
            if (string.IsNullOrEmpty(methodName) || plugin == null) return;
            try
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var mi = typeof(OxidePlugin).GetMethod(methodName, bf, null,
                    new[] { typeof(IPlayer), typeof(string), typeof(string[]) }, null);
                mi?.Invoke(plugin, new object[] { user, command, args ?? Array.Empty<string>() });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TruePVE] command " + methodName + ": " + ex.Message);
            }
        }

        // ---- Console command registration --------------------------------

        private void RegisterDynamicCommands()
        {
            var plugin = Plugin;
            if (plugin == null) return;
            try
            {
                foreach (var reg in plugin.cmd.RegisteredChatCommands)
                    if (!string.IsNullOrEmpty(reg.name))
                        _chatCommandNames.Add(reg.name.ToLowerInvariant());

                foreach (var reg in plugin.cmd.RegisteredConsoleCommands)
                {
                    if (string.IsNullOrEmpty(reg.name)) continue;
                    var name = reg.name.ToLowerInvariant();
                    if (_registeredCommands.Any(c =>
                            string.Equals(c.FullName, "global." + name, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(c.FullName, name, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var captured = reg;
                    RegisterConsole(name, arg => InvokeConsole(plugin, captured.method, arg));
                }
            }
            catch (Exception ex) { Debug.LogWarning("[TruePVE] RegisterDynamicCommands: " + ex.Message); }
        }

        private static void InvokeConsole(OxidePlugin plugin, string methodName, ConsoleSystem.Arg arg)
        {
            if (arg == null || plugin == null) return;
            var basePlayer = arg.Player();
            IPlayer user = basePlayer != null ? new BasePlayerWrapper(basePlayer) : (IPlayer)new RustConsolePlayer();
            string command = arg.cmd?.FullName ?? arg.cmd?.Name ?? "";
            var rawArgs = arg.Args;
            string[] args = rawArgs == null ? Array.Empty<string>() : Array.ConvertAll(rawArgs, a => a.ToString());
            InvokeCovalenceMethod(plugin, methodName, user, command, args);
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler)
        {
            bool hasDot   = name.Contains(".");
            string parent = hasDot ? name.Split('.')[0] : "";
            string cmdName = hasDot ? name.Split(new[] { '.' }, 2)[1] : name;
            string fullName = hasDot ? name : "global." + name;
            string dictKey  = hasDot ? name : fullName;

            var captured = name;
            var cmd = new ConsoleSystem.Command
            {
                Name               = cmdName,
                Parent             = parent,
                FullName           = fullName,
                Variable           = false,
                ServerAdmin        = false,
                AllowRunFromServer = true,
                Replicated         = false,
                Call = a =>
                {
                    try { handler(a); }
                    catch (Exception ex) { Debug.LogWarning("[TruePVE] cmd " + captured + ": " + ex.Message); }
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
