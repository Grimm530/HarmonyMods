using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace KitsHarmony
{
    /// <summary>
    /// Harmony entry point for Kits 2.3.8. Hosts the ported plugin and exposes GiveKit via AppDomain.
    /// </summary>
    public class KitsHarmonyMod : IHarmonyModHooks
    {
        public static KitsHarmonyMod Instance { get; private set; }

        public const int VersionMajor = 2;
        public const int VersionMinor = 3;
        public const int VersionPatch = 8;

        public static readonly VersionNumber Version = new VersionNumber(VersionMajor, VersionMinor, VersionPatch);

        public const string AppDomainApiKey = "Kits_ApiType";
        public const string AppDomainPluginKey = "Kits_Plugin";

        private Kits _plugin;
        private KitsPluginWrapper _pluginWrapper;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public Kits Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            KitsHost.Init(root);
            _plugin = new Kits();
            KitsHost.Instance.Plugin = _plugin;
            _pluginWrapper = new KitsPluginWrapper(this);
            RegisterApiType();
            _plugin.HarmonyInit();
            // Drop any stale replicated kits.* entries from older builds (causes client ERRORS overlay).
            ScrubKitsFromReplicatedList();
            RegisterCommands();
            ScheduleServerInitialized();
            Debug.Log($"[Kits Harmony] Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch} (waiting for ItemManager if needed)");
            Debug.Log("[Kits Harmony] Console (server): kits template fullscreen rust categories");
            Debug.Log("[Kits Harmony] Chat (admin): /kits template fullscreen rust categories");
        }

        private void ScheduleServerInitialized(int attempt = 0)
        {
            if (_plugin == null) return;
            try
            {
                bool itemsReady = ItemManager.itemList != null && ItemManager.itemList.Count > 0;
                if (itemsReady)
                {
                    _plugin.HarmonyServerInitialized();
                    RefreshChatCommandsFromConfig();
                    Debug.Log($"[Kits Harmony] Server initialized (v{VersionMajor}.{VersionMinor}.{VersionPatch})");
                    return;
                }
            }
            catch { }

            if (attempt > 120)
            {
                Debug.LogWarning("[Kits Harmony] Timed out waiting for ItemManager; initializing anyway");
                try
                {
                    _plugin.HarmonyServerInitialized();
                    RefreshChatCommandsFromConfig();
                }
                catch (Exception ex) { Debug.LogError("[Kits Harmony] Init failed: " + ex); }
                return;
            }

            float delay = attempt < 10 ? 0.5f : 1f;
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(() => ScheduleServerInitialized(attempt + 1), delay);
            else
            {
                try
                {
                    var go = new GameObject("KitsHarmony_InitWait");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<InitWaitBehaviour>().Begin(this, attempt);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Kits Harmony] ScheduleServerInitialized: " + ex.Message);
                }
            }
        }

        private class InitWaitBehaviour : MonoBehaviour
        {
            private KitsHarmonyMod _mod;
            private int _attempt;
            public void Begin(KitsHarmonyMod mod, int attempt)
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
            UnregisterCommands();
            _plugin?.HarmonyUnload();
            UnregisterApiType();
            KitsHost.Shutdown();
            _plugin = null;
            _pluginWrapper = null;
            Instance = null;
        }

        private void RegisterApiType()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(KitsHarmonyMod));
                AppDomain.CurrentDomain.SetData(AppDomainPluginKey, _pluginWrapper);
            }
            catch (Exception ex) { Debug.LogWarning("[Kits Harmony] RegisterApiType: " + ex.Message); }
        }

        private static void UnregisterApiType()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, null);
                AppDomain.CurrentDomain.SetData(AppDomainPluginKey, null);
            }
            catch { }
        }

        public object Call(string method, params object[] args)
        {
            if (_plugin == null || string.IsNullOrEmpty(method)) return null;
            try
            {
                var count = args?.Length ?? 0;
                var mi = typeof(Kits).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == method && m.GetParameters().Length == count);
                return mi?.Invoke(_plugin, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Kits Harmony] Call({method}): " + (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }

        // ---- Static API ----

        public static object GiveKit(BasePlayer player, string name)
        {
            return Instance?._plugin?.GiveKit(player, name);
        }

        public static bool GiveKit(BasePlayer player, string name, bool usingUI)
        {
            return Instance?._plugin != null && Instance._plugin.GiveKit(player, name, usingUI);
        }

        public static bool IsKit(string name)
        {
            return Instance?._plugin != null && Instance._plugin.IsKit(name);
        }

        public static bool isKit(string name) => IsKit(name);

        public sealed class KitsPluginWrapper
        {
            private readonly KitsHarmonyMod _mod;
            public KitsPluginWrapper(KitsHarmonyMod mod) => _mod = mod;
            public bool IsLoaded => _mod?._plugin != null;
            public string Name => "Kits";
            public string Version => $"{VersionMajor}.{VersionMinor}.{VersionPatch}";
            public object Call(string method, params object[] args) => _mod?.Call(method, args);
        }

        // ---- Commands ----

        private void RegisterCommands()
        {
            // Space-form root for server console / RCON. Do NOT replicate — clients log
            // "Replicated convar not found" for kits.* / editkit when these are replicated.
            RegisterConsole("kits", HandleKitsRoot, serverAdmin: false);

            RegisterConsole("kits.reset", arg => InvokeConsoleMethod("CmdKitsReset", arg), serverAdmin: true);
            RegisterConsole("kits.give", arg => InvokeConsoleMethod("CmdKitsGive", arg), serverAdmin: true);
            RegisterConsole("kits.givekit", arg => InvokeConsoleMethod("CmdKitsGiveKit", arg), serverAdmin: true);
            RegisterConsole("kits.template", arg => InvokeConsoleMethod("CmdKitsSetTemplate", arg), serverAdmin: true);
            RegisterConsole("kits.convert", arg => InvokeConsoleMethod("OldKitsConvert", arg), serverAdmin: true);

            RegisterConsole("UI_Kits", arg => _plugin?.CmdKitsConsole(arg));

            RegisterConsole("editkit", arg =>
            {
                var player = arg?.Player();
                if (player == null || _plugin == null) return;
                string[] args;
                try
                {
                    var raw = arg.Args;
                    if (raw == null || raw.Length == 0) args = Array.Empty<string>();
                    else
                    {
                        args = new string[raw.Length];
                        for (int i = 0; i < raw.Length; i++) args[i] = raw[i].ToString();
                    }
                }
                catch { args = Array.Empty<string>(); }
                _plugin.editKitCommand(player, "editkit", args);
            });

            foreach (var name in new[] { "kit", "kits", "editkit" })
                _chatCommandNames.Add(name);
        }

        /// <summary>
        /// kits template|reset|give|givekit|convert ...
        /// </summary>
        private void HandleKitsRoot(ConsoleSystem.Arg arg)
        {
            if (arg == null) return;

            string sub = arg.GetString(0, string.Empty);
            if (string.IsNullOrEmpty(sub))
            {
                arg.ReplyWith(
                    "Usage:\n" +
                    "  kits template [fullscreen|inmenu] [old|rust] [normal|content|categories|content_categories] [1|2|4]\n" +
                    "  kits reset|give|givekit|convert ...\n" +
                    "Chat (admin): /kits template fullscreen rust categories");
                return;
            }

            string method = sub.ToLowerInvariant() switch
            {
                "template" => "CmdKitsSetTemplate",
                "reset" => "CmdKitsReset",
                "give" => "CmdKitsGive",
                "givekit" => "CmdKitsGiveKit",
                "convert" => "OldKitsConvert",
                _ => null
            };

            if (method == null)
            {
                arg.ReplyWith($"Unknown kits subcommand '{sub}'. Try: template, reset, give, givekit, convert");
                return;
            }

            try
            {
                var sb = new StringBuilder("kits.").Append(sub.ToLowerInvariant());
                var raw = arg.Args;
                if (raw != null)
                {
                    for (int i = 1; i < raw.Length; i++)
                    {
                        sb.Append(' ');
                        string s = raw[i].ToString() ?? string.Empty;
                        if (s.IndexOfAny(new[] { ' ', '"' }) >= 0)
                            sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
                        else
                            sb.Append(s);
                    }
                }

                var opt = ConsoleSystem.Option.Server.Quiet();
                if (arg.Connection != null)
                    opt = opt.FromConnection(arg.Connection);

                InvokeConsoleMethod(method, new ConsoleSystem.Arg(opt, sb.ToString()));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Kits] kits root: " + ex.Message);
                arg.ReplyWith("Error: " + ex.Message);
            }
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
                        if (!string.IsNullOrEmpty(c))
                            _chatCommandNames.Add(c);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Kits Harmony] RefreshChatCommandsFromConfig: " + ex.Message);
            }
            _chatCommandNames.Add("editkit");
        }

        private void InvokeConsoleMethod(string methodName, ConsoleSystem.Arg arg)
        {
            if (_plugin == null || arg == null) return;
            try
            {
                var mi = typeof(Kits).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                mi?.Invoke(_plugin, new object[] { arg });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Kits Harmony] {methodName}: " + ex.Message);
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

            // Never set Replicated/Variable — clients do not have these ConsoleGen entries and
            // spam "Replicated convar not found on client: kits.*" / global.editkit.
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
                    catch (Exception ex) { Debug.LogWarning($"[Kits] command {localName}: " + ex.Message); }
                }
            };

            ConsoleSystem.Index.Server.Dict[dictKey] = cmd;
            if (!hasDot && ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict[cmdName] = cmd;

            _registeredCommands.Add(cmd);
        }

        /// <summary>
        /// Remove any kits-related commands from Index.Server.Replicated (including leftovers
        /// from older DLL builds that incorrectly set Replicated=true).
        /// Note: Replicated is a public static Field (not a Property).
        /// </summary>
        private static void ScrubKitsFromReplicatedList()
        {
            try
            {
                var replicated = typeof(ConsoleSystem.Index.Server)
                    .GetField("Replicated", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IList;
                if (replicated == null)
                {
                    // Fallback if Facepunch ever exposes it as a property
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

                    bool isKits =
                        full.StartsWith("kits.", StringComparison.OrdinalIgnoreCase) ||
                        full.Equals("global.kits", StringComparison.OrdinalIgnoreCase) ||
                        full.Equals("global.editkit", StringComparison.OrdinalIgnoreCase) ||
                        full.Equals("global.UI_Kits", StringComparison.OrdinalIgnoreCase) ||
                        parent.Equals("kits", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("kits", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("editkit", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("UI_Kits", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("reset", StringComparison.OrdinalIgnoreCase) && parent.Equals("kits", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("give", StringComparison.OrdinalIgnoreCase) && parent.Equals("kits", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("givekit", StringComparison.OrdinalIgnoreCase) && parent.Equals("kits", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("template", StringComparison.OrdinalIgnoreCase) && parent.Equals("kits", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("convert", StringComparison.OrdinalIgnoreCase) && parent.Equals("kits", StringComparison.OrdinalIgnoreCase);

                    if (isKits)
                    {
                        cmd.Replicated = false;
                        cmd.Variable = false;
                        replicated.RemoveAt(i);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Kits] ScrubKitsFromReplicatedList: " + ex.Message);
            }
        }

        private void UnregisterCommands()
        {
            ScrubKitsFromReplicatedList();

            string[] names =
            {
                "global.kits", "global.UI_Kits", "global.editkit",
                "kits.reset", "kits.give", "kits.givekit", "kits.template", "kits.convert"
            };
            foreach (var name in names)
            {
                ConsoleSystem.Index.Server.Dict?.Remove(name);
                if (name.StartsWith("global."))
                    ConsoleSystem.Index.Server.GlobalDict?.Remove(name.Substring("global.".Length));
            }
            _registeredCommands.Clear();
            _chatCommandNames.Clear();
        }

        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || _plugin == null || string.IsNullOrWhiteSpace(message)) return false;
            message = message.Trim();
            if (message.StartsWith("/")) message = message.Substring(1).Trim();
            string[] parts = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            string name = parts[0];
            if (!_chatCommandNames.Contains(name)) return false;

            var args = parts.Skip(1).ToArray();

            // Admin: /kits template fullscreen rust categories
            if (args.Length > 0 &&
                name.Equals("kits", StringComparison.OrdinalIgnoreCase) &&
                args[0].Equals("template", StringComparison.OrdinalIgnoreCase) &&
                _plugin.IsAdmin(player))
            {
                try
                {
                    var sb = new StringBuilder("kits.template");
                    for (int i = 1; i < args.Length; i++)
                        sb.Append(' ').Append(args[i]);
                    var opt = ConsoleSystem.Option.Server.Quiet();
                    if (player.net?.connection != null)
                        opt = opt.FromConnection(player.net.connection);
                    InvokeConsoleMethod("CmdKitsSetTemplate", new ConsoleSystem.Arg(opt, sb.ToString()));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Kits] chat template: " + ex.Message);
                }
                return true;
            }

            if (name.Equals("editkit", StringComparison.OrdinalIgnoreCase))
            {
                _plugin.editKitCommand(player, name, args);
                return true;
            }

            _plugin.CmdOpenKits(player.ToIPlayer(), name, args);
            return true;
        }
    }
}
