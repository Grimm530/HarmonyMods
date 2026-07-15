using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace BackpacksHarmony
{
    /// <summary>
    /// Harmony entry point for Backpacks 3.17.41. Hosts the ported plugin and registers commands.
    /// </summary>
    public class BackpacksHarmonyMod : IHarmonyModHooks
    {
        public static BackpacksHarmonyMod Instance { get; private set; }

        public const int VersionMajor = 3;
        public const int VersionMinor = 17;
        public const int VersionPatch = 41;

        public static readonly VersionNumber Version = new VersionNumber(VersionMajor, VersionMinor, VersionPatch);

        public const string AppDomainApiKey = "Backpacks_ApiType";

        private Backpacks _plugin;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _chatToMethod =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Backpacks Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            BackpacksHost.Init(root);
            _plugin = new Backpacks();
            BackpacksHost.Instance.Plugin = _plugin;
            RegisterApiType();
            _plugin.HarmonyInit();
            BindItemRetriever();
            ScrubBackpacksFromReplicatedList();
            RegisterCommands();
            ScheduleServerInitialized();
            Debug.Log($"[Backpacks Harmony] Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[Backpacks Harmony] Config: HarmonyConfig/Backpacks.json");
            Debug.Log("[Backpacks Harmony] Chat: /backpack  Console: backpack.open / viewbackpack / ...");
        }

        private void BindItemRetriever()
        {
            try
            {
                ItemRetrieverBinder.RegisterReadyCallback(() =>
                {
                    try
                    {
                        Debug.Log("[Backpacks] ItemRetriever ready - registering retrieve supplier.");
                        _plugin?.MaybeRegisterItemRetriever();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[Backpacks] ItemRetriever ready callback: " + ex.Message);
                    }
                });
                if (ItemRetrieverBinder.TryResolveBridge() == null)
                    Debug.Log("[Backpacks] ItemRetriever not loaded yet (normal if DLL order is alphabetical). Will bind when it loads.");
                else
                    _plugin?.MaybeRegisterItemRetriever();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Backpacks] BindItemRetriever: " + ex.Message);
            }
        }

        private void ScheduleServerInitialized(int attempt = 0)
        {
            if (_plugin == null) return;
            try
            {
                bool itemsReady = ItemManager.itemList != null && ItemManager.itemList.Count > 0;
                if (itemsReady)
                {
                    try
                    {
                        _plugin.HarmonyServerInitialized();
                        RefreshChatCommandsFromConfig();
                        Debug.Log($"[Backpacks Harmony] Server initialized (v{VersionMajor}.{VersionMinor}.{VersionPatch})");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("[Backpacks Harmony] HarmonyServerInitialized failed: " + ex);
                    }
                    return;
                }
            }
            catch { }

            if (attempt > 120)
            {
                Debug.LogWarning("[Backpacks Harmony] Timed out waiting for ItemManager; initializing anyway");
                try
                {
                    _plugin.HarmonyServerInitialized();
                    RefreshChatCommandsFromConfig();
                }
                catch (Exception ex) { Debug.LogError("[Backpacks Harmony] Init failed: " + ex); }
                return;
            }

            float delay = attempt < 10 ? 0.5f : 1f;
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(() => ScheduleServerInitialized(attempt + 1), delay);
            else
            {
                try
                {
                    var go = new GameObject("BackpacksHarmony_InitWait");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<InitWaitBehaviour>().Begin(this, attempt);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Backpacks Harmony] ScheduleServerInitialized: " + ex.Message);
                }
            }
        }

        private class InitWaitBehaviour : MonoBehaviour
        {
            private BackpacksHarmonyMod _mod;
            private int _attempt;
            public void Begin(BackpacksHarmonyMod mod, int attempt)
            {
                _mod = mod;
                _attempt = attempt;
                StartCoroutine(Wait());
            }
            private IEnumerator Wait()
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
            UnregisterCommands();
            _plugin?.HarmonyUnload();
            UnregisterApiType();
            BackpacksHost.Shutdown();
            _plugin = null;
            Instance = null;
        }

        private static void RegisterApiType()
        {
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(BackpacksHarmonyMod)); }
            catch (Exception ex) { Debug.LogWarning("[Backpacks Harmony] RegisterApiType: " + ex.Message); }
        }

        private static void UnregisterApiType()
        {
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); }
            catch { }
        }

        // ---- Commands ----

        private void RegisterCommands()
        {
            // Root + dotted commands (IPlayer covalence style)
            RegisterIPlayerCommand("backpack", "BackpackOpenCommand");
            RegisterIPlayerCommand("backpack.open", "BackpackOpenCommand");
            RegisterIPlayerCommand("backpack.next", "BackpackNextCommand");
            RegisterIPlayerCommand("backpack.prev", "BackpackPreviousCommand");
            RegisterIPlayerCommand("backpack.previous", "BackpackPreviousCommand");
            RegisterIPlayerCommand("backpack.fetch", "BackpackFetchCommand");
            RegisterIPlayerCommand("backpack.erase", "EraseBackpackCommand", serverAdmin: true);
            RegisterIPlayerCommand("viewbackpack", "ViewBackpackCommand");
            RegisterIPlayerCommand("backpack.addsize", "AddBackpackCapacityCommand", serverAdmin: true);
            RegisterIPlayerCommand("backpack.setsize", "SetBackpackCapacityCommand", serverAdmin: true);
            RegisterIPlayerCommand("backpack.resetgui", "ResetGuiCommand", serverAdmin: true);
            RegisterIPlayerCommand("backpackgui", "ToggleBackpackGUICommand");
            RegisterIPlayerCommand("backpack.setgathermode", "SetGatherCommand");
            RegisterIPlayerCommand("backpack.ui.togglegather", "ToggleGatherUICommand");
            RegisterIPlayerCommand("backpack.ui.toggleretrieve", "ToggleRetrieveUICommand");
            RegisterIPlayerCommand("backpack.debug.size", "DebugSizeCommand", serverAdmin: true);
            RegisterIPlayerCommand("backpack.debug.capacity", "DebugSizeCommand", serverAdmin: true);
            RegisterIPlayerCommand("backpack.debug.gather", "DebugGatherCommand", serverAdmin: true);

            // Chat aliases
            MapChat("backpack", "BackpackOpenCommand");
            MapChat("backpack.open", "BackpackOpenCommand");
            MapChat("backpack.next", "BackpackNextCommand");
            MapChat("backpack.prev", "BackpackPreviousCommand");
            MapChat("backpack.previous", "BackpackPreviousCommand");
            MapChat("backpack.fetch", "BackpackFetchCommand");
            MapChat("viewbackpack", "ViewBackpackCommand");
            MapChat("backpackgui", "ToggleBackpackGUICommand");
            MapChat("backpack.setgathermode", "SetGatherCommand");
            MapChat("backpack.addsize", "AddBackpackCapacityCommand");
            MapChat("backpack.setsize", "SetBackpackCapacityCommand");
            MapChat("backpack.resetgui", "ResetGuiCommand");
            MapChat("backpack.debug.size", "DebugSizeCommand");
            MapChat("backpack.debug.capacity", "DebugSizeCommand");
            MapChat("backpack.debug.gather", "DebugGatherCommand");
        }

        private void MapChat(string name, string method)
        {
            if (string.IsNullOrEmpty(name)) return;
            _chatCommandNames.Add(name);
            _chatToMethod[name] = method;
        }

        private void RegisterIPlayerCommand(string name, string methodName, bool serverAdmin = false)
        {
            RegisterConsole(name, arg => InvokeIPlayerMethod(methodName, name, arg), serverAdmin);
        }

        private void RefreshChatCommandsFromConfig()
        {
            if (_plugin == null) return;
            try
            {
                foreach (var entry in _plugin.RegisteredCovalenceCommands)
                {
                    if (entry.commands == null) continue;
                    foreach (var c in entry.commands)
                    {
                        if (string.IsNullOrEmpty(c)) continue;
                        _chatCommandNames.Add(c);
                        if (!_chatToMethod.ContainsKey(c))
                            _chatToMethod[c] = entry.methodName;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Backpacks Harmony] RefreshChatCommandsFromConfig: " + ex.Message);
            }
        }

        private void InvokeIPlayerMethod(string methodName, string cmdName, ConsoleSystem.Arg arg)
        {
            if (_plugin == null || arg == null) return;
            try
            {
                IPlayer player;
                var bp = arg.Player();
                if (bp != null)
                    player = bp.ToIPlayer();
                else
                    player = new RustConsolePlayer();

                string[] args;
                try
                {
                    var raw = arg.Args;
                    if (raw == null || raw.Length == 0) args = Array.Empty<string>();
                    else
                    {
                        args = new string[raw.Length];
                        for (int i = 0; i < raw.Length; i++)
                            args[i] = raw[i].ToString() ?? "";
                    }
                }
                catch { args = Array.Empty<string>(); }

                var mi = typeof(Backpacks).GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                mi?.Invoke(_plugin, new object[] { player, cmdName, args });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Backpacks Harmony] {methodName}: " + ex.Message);
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
                    catch (Exception ex) { Debug.LogWarning($"[Backpacks] command {localName}: " + ex.Message); }
                }
            };

            ConsoleSystem.Index.Server.Dict[dictKey] = cmd;
            if (!hasDot && ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict[cmdName] = cmd;

            _registeredCommands.Add(cmd);
        }

        private static void ScrubBackpacksFromReplicatedList()
        {
            try
            {
                var replicated = typeof(ConsoleSystem.Index.Server)
                    .GetField("Replicated", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IList;
                if (replicated == null)
                {
                    replicated = typeof(ConsoleSystem.Index.Server)
                        .GetProperty("Replicated", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IList;
                }
                if (replicated == null) return;

                for (int i = replicated.Count - 1; i >= 0; i--)
                {
                    if (replicated[i] is not ConsoleSystem.Command cmd) continue;
                    string full = cmd.FullName ?? string.Empty;
                    string parent = cmd.Parent ?? string.Empty;
                    string name = cmd.Name ?? string.Empty;

                    bool isBp =
                        full.StartsWith("backpack.", StringComparison.OrdinalIgnoreCase) ||
                        full.Equals("global.backpack", StringComparison.OrdinalIgnoreCase) ||
                        full.Equals("global.backpackgui", StringComparison.OrdinalIgnoreCase) ||
                        full.Equals("global.viewbackpack", StringComparison.OrdinalIgnoreCase) ||
                        parent.Equals("backpack", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("backpack", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("backpackgui", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("viewbackpack", StringComparison.OrdinalIgnoreCase);

                    if (isBp)
                    {
                        cmd.Replicated = false;
                        cmd.Variable = false;
                        replicated.RemoveAt(i);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Backpacks] ScrubBackpacksFromReplicatedList: " + ex.Message);
            }
        }

        private void UnregisterCommands()
        {
            ScrubBackpacksFromReplicatedList();

            string[] names =
            {
                "global.backpack", "global.backpackgui", "global.viewbackpack",
                "backpack.open", "backpack.next", "backpack.prev", "backpack.previous",
                "backpack.fetch", "backpack.erase", "backpack.addsize", "backpack.setsize",
                "backpack.resetgui", "backpack.setgathermode",
                "backpack.ui.togglegather", "backpack.ui.toggleretrieve",
                "backpack.debug.size", "backpack.debug.capacity", "backpack.debug.gather"
            };
            foreach (var name in names)
            {
                ConsoleSystem.Index.Server.Dict?.Remove(name);
                if (name.StartsWith("global."))
                    ConsoleSystem.Index.Server.GlobalDict?.Remove(name.Substring("global.".Length));
            }
            _registeredCommands.Clear();
            _chatCommandNames.Clear();
            _chatToMethod.Clear();
        }

        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || _plugin == null || string.IsNullOrWhiteSpace(message)) return false;
            message = message.Trim();
            if (message.StartsWith("/")) message = message.Substring(1).Trim();
            string[] parts = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            // Support dotted chat forms: backpack.open, backpack.next, etc.
            string name = parts[0];
            if (!_chatCommandNames.Contains(name)) return false;
            if (!_chatToMethod.TryGetValue(name, out var methodName)) return false;

            var args = parts.Skip(1).ToArray();
            try
            {
                var mi = typeof(Backpacks).GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                mi?.Invoke(_plugin, new object[] { player.ToIPlayer(), name, args });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Backpacks] chat " + name + ": " + ex.Message);
            }
            return true;
        }
    }
}
