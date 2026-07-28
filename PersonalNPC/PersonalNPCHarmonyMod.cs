using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace PersonalNPCHarmony
{
    /// <summary>
    /// Harmony entry point. Hosts all three ported plugins (PersonalNPC, PersonalNPCHelper,
    /// PNPCAddonBuilder) in one DLL and wires the cross-plugin bridges that replace Oxide's
    /// [PluginReference] plumbing.
    /// </summary>
    public class PersonalNPCHarmonyMod : IHarmonyModHooks
    {
        public static PersonalNPCHarmonyMod Instance { get; private set; }

        public const string AppDomainApiKey = "PersonalNPC_ApiType";

        private PersonalNPC _plugin;
        private PersonalNPCHelper _helper;
        private PNPCAddonBuilder _builder;

        private readonly List<string> _registeredCommandKeys = new List<string>();
        private readonly HashSet<string> _chatCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _serverInitialized;

        public PersonalNPC Plugin => _plugin;
        public PersonalNPCHelper Helper => _helper;
        public PNPCAddonBuilder Builder => _builder;

        /// <summary>Required by Rust.Harmony.</summary>
        public PersonalNPCHarmonyMod() { }

        // ---- Lifecycle ----

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;

            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            PersonalNPCHost.Init(root);

            _plugin = new PersonalNPC();
            _helper = new PersonalNPCHelper();
            _builder = new PNPCAddonBuilder();

            var host = PersonalNPCHost.Instance;
            _plugin.Config = host.Config;
            _builder.Config = host.BuilderConfig;
            _helper.Config = host.Config;

            WireBridges(host);
            RegisterApiType();

            try { _plugin.HarmonyInit(); }
            catch (Exception ex) { Debug.LogError("[PersonalNPC] Init failed: " + ex); }

            try { _builder.HarmonyInit(); }
            catch (Exception ex) { Debug.LogError("[PersonalNPC] Builder init failed: " + ex); }

            try { _helper.HarmonyInit(); }
            catch (Exception ex) { Debug.LogError("[PersonalNPC] Helper init failed: " + ex); }

            RegisterCommands();
            StartImagePump();
            ScheduleServerInitialized();

            Debug.Log("[PersonalNPC Harmony] Loaded PersonalNPC 2.0.7 + Helper 1.3.0 + Builder 1.0.0");
            Debug.Log("[PersonalNPC Harmony] Chat: /pnpc  /bw  /botwheel");
            Debug.Log("[PersonalNPC Harmony] Console: pnpc, pnpc.info, pnpc.item, pnpc.deposit, pnpchelper.reset, pnpchelper.grant");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            UnregisterCommands();

            try { _helper?.HarmonyUnload(); } catch (Exception ex) { Debug.LogWarning("[PersonalNPC] Helper unload: " + ex.Message); }
            try { _builder?.HarmonyUnload(); } catch (Exception ex) { Debug.LogWarning("[PersonalNPC] Builder unload: " + ex.Message); }
            try { _plugin?.HarmonyUnload(); } catch (Exception ex) { Debug.LogWarning("[PersonalNPC] Unload: " + ex.Message); }

            UnregisterApiType();
            PersonalNPCHost.Shutdown();

            _plugin = null;
            _helper = null;
            _builder = null;
            _serverInitialized = false;
            Instance = null;
        }

        /// <summary>
        /// Replaces Oxide's [PluginReference] injection. Merged plugins get a LocalPluginBridge;
        /// soft dependencies that are not ported stay null, matching the original null checks.
        /// </summary>
        private void WireBridges(PersonalNPCHost host)
        {
            var pnpcBridge = new LocalPluginBridge("PersonalNPC", _plugin);
            var helperBridge = new LocalPluginBridge("PersonalNPCHelper", _helper);
            var builderBridge = new LocalPluginBridge("PNPCAddonBuilder", _builder);

            host.Plugins.Register("PersonalNPC", pnpcBridge);
            host.Plugins.Register("PersonalNPCHelper", helperBridge);
            host.Plugins.Register("PNPCAddonBuilder", builderBridge);
            host.Plugins.Register("ImageLibrary", host.ImageLibrary);

            _plugin.ImageLibrary = host.ImageLibrary;
            _plugin.PersonalNPCHelper = helperBridge;
            _plugin.PNPCAddonBuilder = builderBridge;
            _plugin.VehicleDeployedLocks = null;
            _plugin.PNPCAddonHeli = null;
            _plugin.PNPCAddonHunter = null;
            _plugin.ZoneManager = null;
            _plugin.Friends = null;
            _plugin.Clans = null;
            _plugin.DeployableNature = null;

            _helper.PersonalNPC = pnpcBridge;
            _helper.PNPCAddonBuilder = builderBridge;
            _helper.RaidableBasesBuyableUI = null;
        }

        private void ScheduleServerInitialized(int attempt = 0)
        {
            if (_plugin == null || _serverInitialized) return;

            bool ready = false;
            try { ready = ItemManager.itemList != null && ItemManager.itemList.Count > 0; }
            catch { }

            if (ready || attempt > 120)
            {
                if (!ready)
                    Debug.LogWarning("[PersonalNPC Harmony] Timed out waiting for ItemManager; initializing anyway");

                _serverInitialized = true;
                try { _plugin.HarmonyServerInitialized(); }
                catch (Exception ex) { Debug.LogError("[PersonalNPC] OnServerInitialized failed: " + ex); }
                try { _builder.HarmonyServerInitialized(); }
                catch (Exception ex) { Debug.LogError("[PersonalNPC] Builder OnServerInitialized failed: " + ex); }
                try { _helper.HarmonyServerInitialized(); }
                catch (Exception ex) { Debug.LogError("[PersonalNPC] Helper OnServerInitialized failed: " + ex); }

                Debug.Log("[PersonalNPC Harmony] Server initialized");
                return;
            }

            float delay = attempt < 10 ? 0.5f : 1f;
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(() => ScheduleServerInitialized(attempt + 1), delay);
            else
            {
                try
                {
                    var go = new GameObject("PersonalNPCHarmony_InitWait");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<InitWaitBehaviour>().Begin(this, attempt);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[PersonalNPC Harmony] ScheduleServerInitialized: " + ex.Message);
                }
            }
        }

        private class InitWaitBehaviour : MonoBehaviour
        {
            private PersonalNPCHarmonyMod _mod;
            private int _attempt;

            public void Begin(PersonalNPCHarmonyMod mod, int attempt)
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

        /// <summary>Image downloads finish on worker threads; FileStorage must be touched on the main thread.</summary>
        private void StartImagePump()
        {
            try { ServerMgr.Instance?.StartCoroutine(ImagePump()); }
            catch (Exception ex) { Debug.LogWarning("[PersonalNPC] image pump: " + ex.Message); }
        }

        private static IEnumerator ImagePump()
        {
            var wait = new WaitForSeconds(2f);
            while (PersonalNPCHost.Instance != null)
            {
                try { PersonalNPCHost.Instance.ImageLibrary?.FlushPendingStores(); }
                catch { }
                yield return wait;
            }
        }

        private static void RegisterApiType()
        {
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(PersonalNPCHarmonyMod)); }
            catch (Exception ex) { Debug.LogWarning("[PersonalNPC Harmony] RegisterApiType: " + ex.Message); }
        }

        private static void UnregisterApiType()
        {
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); }
            catch { }
        }

        // ---- Static API for other Harmony mods ----

        public static bool HasBot(BasePlayer player) =>
            Instance?._plugin != null && Instance._plugin.Call<bool>("HasBot", player);

        public static object GetBotController(BasePlayer player) =>
            Instance?._plugin?.Call("GetBotController", player);

        public static bool IsPersonalNPC(BasePlayer player) =>
            Instance?._plugin != null && Instance._plugin.Call<bool>("IsPersonalNPC", player);

        // ---- Console commands ----

        private void RegisterCommands()
        {
            RegisterConsole("pnpc", arg => _plugin?.cnslCommand(arg));
            RegisterConsole("pnpc.deposit", arg => _plugin?.ConsoleDepositCommand(arg));
            RegisterConsole("pnpc.info", arg => _plugin?.cnslCommandInfo(arg));
            RegisterConsole("pnpc.item", arg => _plugin?.cnslCommandItem(arg), serverAdmin: true);

            RegisterConsole("pnpchelper.wheel", arg => _helper?.CmdWheel(arg));
            RegisterConsole("pnpchelper.build", arg => _helper?.CmdBuildUi(arg));
            RegisterConsole("pnpchelper.reset", arg => _helper?.CmdReset(arg), serverAdmin: true);
            RegisterConsole("pnpchelper.grant", arg => _helper?.CmdGrant(arg), serverAdmin: true);

            _chatCommandNames.Add("pnpc");
            _chatCommandNames.Add("bw");
            _chatCommandNames.Add("botwheel");
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

            // Never replicate: clients have no ConsoleGen entry for these and would log
            // "Replicated convar not found on client".
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
                    catch (Exception ex) { Debug.LogWarning("[PersonalNPC] command " + localName + ": " + ex.Message); }
                }
            };

            ConsoleSystem.Index.Server.Dict[dictKey] = cmd;
            if (!hasDot && ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict[cmdName] = cmd;

            _registeredCommandKeys.Add(dictKey);
        }

        private void UnregisterCommands()
        {
            foreach (var key in _registeredCommandKeys)
            {
                ConsoleSystem.Index.Server.Dict?.Remove(key);
                if (key.StartsWith("global.", StringComparison.Ordinal))
                    ConsoleSystem.Index.Server.GlobalDict?.Remove(key.Substring("global.".Length));
            }
            _registeredCommandKeys.Clear();
            _chatCommandNames.Clear();
        }

        /// <summary>
        /// Runs one of our console commands locally. Used by Cui_Endtest_Patch and by GUI shortcut
        /// buttons, which under Oxide would bounce off the client console.
        /// </summary>
        public void DispatchConsole(string command, IReadOnlyList<string> args, Network.Connection connection)
        {
            if (string.IsNullOrEmpty(command)) return;

            var sb = new StringBuilder(command);
            if (args != null)
            {
                for (int i = 0; i < args.Count; i++)
                {
                    sb.Append(' ');
                    string s = args[i] ?? string.Empty;
                    if (s.IndexOfAny(new[] { ' ', '"' }) >= 0)
                        sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
                    else
                        sb.Append(s);
                }
            }

            try
            {
                var opt = ConsoleSystem.Option.Server.Quiet();
                if (connection != null)
                    opt = opt.FromConnection(connection);

                var arg = new ConsoleSystem.Arg(opt, sb.ToString());

                switch (command)
                {
                    case "pnpc": _plugin?.cnslCommand(arg); break;
                    case "pnpc.deposit": _plugin?.ConsoleDepositCommand(arg); break;
                    case "pnpc.info": _plugin?.cnslCommandInfo(arg); break;
                    case "pnpc.item": _plugin?.cnslCommandItem(arg); break;
                    case "pnpchelper.wheel": _helper?.CmdWheel(arg); break;
                    case "pnpchelper.build": _helper?.CmdBuildUi(arg); break;
                    case "pnpchelper.reset": _helper?.CmdReset(arg); break;
                    case "pnpchelper.grant": _helper?.CmdGrant(arg); break;
                    default:
                        Debug.LogWarning("[PersonalNPC] Unknown CUI command: " + command);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PersonalNPC] DispatchConsole " + command + ": " + ex.Message);
            }
        }

        /// <summary>Runs a chat command straight through the plugin (no client round trip).</summary>
        public static void RunChatCommand(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrWhiteSpace(message)) return;
            Instance?.OnChatCommand(player, message);
        }

        // ---- Chat commands ----

        /// <summary>Returns true when the message was a PersonalNPC command and should not be broadcast.</summary>
        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrWhiteSpace(message)) return false;

            message = message.Trim();
            if (!message.StartsWith("/", StringComparison.Ordinal)) return false;
            message = message.Substring(1).Trim();

            var parts = SplitArgs(message);
            if (parts.Length == 0) return false;

            string name = parts[0];
            if (!_chatCommandNames.Contains(name)) return false;

            var args = parts.Skip(1).ToArray();

            if (name.Equals("pnpc", StringComparison.OrdinalIgnoreCase))
            {
                // Helper's Frankenstein unlock gate runs first (Oxide OnPlayerCommand).
                try
                {
                    if (_helper?.OnPlayerCommand(player, "pnpc", args) != null)
                        return true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[PersonalNPC] unlock gate: " + ex.Message);
                }

                try { _plugin?.chatCommand(player, "pnpc", args); }
                catch (Exception ex) { Debug.LogWarning("[PersonalNPC] /pnpc: " + ex.Message); }
                return true;
            }

            try { _helper?.CmdBotWheel(player, name, args); }
            catch (Exception ex) { Debug.LogWarning("[PersonalNPC] /" + name + ": " + ex.Message); }
            return true;
        }

        /// <summary>Whitespace split that honours double quotes, matching Oxide's chat parser.</summary>
        internal static string[] SplitArgs(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return Array.Empty<string>();

            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            foreach (char c in input)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (!inQuotes && char.IsWhiteSpace(c))
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }
                current.Append(c);
            }

            if (current.Length > 0) result.Add(current.ToString());
            return result.ToArray();
        }
    }
}
