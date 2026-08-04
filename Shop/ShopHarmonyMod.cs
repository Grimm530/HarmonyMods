using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ShopHarmony
{
    /// <summary>
    /// Harmony entry point for Shop 2.4.201. Hosts the ported plugin and registers commands.
    /// </summary>
    public class ShopHarmonyMod : IHarmonyModHooks
    {
        public static ShopHarmonyMod Instance { get; private set; }

        public const int VersionMajor = 2;
        public const int VersionMinor = 4;
        public const int VersionPatch = 201;

        public static readonly VersionNumber Version = new VersionNumber(VersionMajor, VersionMinor, VersionPatch);

        public const string AppDomainApiKey = "Shop_ApiType";
        public const string AppDomainPluginKey = "Shop_Plugin";

        private Shop _plugin;
        private ShopPluginWrapper _pluginWrapper;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _commandMethodMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Shop Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            ShopHost.Init(root);
            _plugin = new Shop();
            ShopHost.Instance.Plugin = _plugin;
            _pluginWrapper = new ShopPluginWrapper(this);
            RegisterApiType();
            _plugin.HarmonyInit();
            ScrubShopFromReplicatedList();
            RegisterCommands();
            ScheduleServerInitialized();
            Debug.Log($"[Shop Harmony] Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[Shop Harmony] Chat: /s or /shops (from HarmonyConfig/Shop.json Commands)");
        }

        private void ScheduleServerInitialized(int attempt = 0)
        {
            if (_plugin == null) return;
            try
            {
                bool itemsReady = ItemManager.itemList != null && ItemManager.itemList.Count > 0;
                if (itemsReady && ServerMgr.Instance != null && attempt >= 2)
                {
                    _plugin.HarmonyServerInitialized();
                    RefreshChatCommandsFromConfig();
                    RegisterDeferredConsoleFromPlugin();
                    Debug.Log($"[Shop Harmony] Server initialized (v{VersionMajor}.{VersionMinor}.{VersionPatch})");
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Shop Harmony] ScheduleServerInitialized: " + ex.Message);
            }

            if (attempt > 120)
            {
                Debug.LogWarning("[Shop Harmony] Timed out waiting for ItemManager; initializing anyway");
                try
                {
                    _plugin.HarmonyServerInitialized();
                    RefreshChatCommandsFromConfig();
                    RegisterDeferredConsoleFromPlugin();
                }
                catch (Exception ex) { Debug.LogError("[Shop Harmony] Init failed: " + ex); }
                return;
            }

            float delay = attempt < 10 ? 0.5f : 1f;
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(() => ScheduleServerInitialized(attempt + 1), delay);
            else
            {
                try
                {
                    var go = new GameObject("ShopHarmony_InitWait");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<InitWaitBehaviour>().Begin(this, attempt);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Shop Harmony] ScheduleServerInitialized: " + ex.Message);
                }
            }
        }

        private class InitWaitBehaviour : MonoBehaviour
        {
            private ShopHarmonyMod _mod;
            private int _attempt;
            public void Begin(ShopHarmonyMod mod, int attempt)
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
            ShopHost.Shutdown();
            _plugin = null;
            _pluginWrapper = null;
            Instance = null;
        }

        private void RegisterApiType()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(ShopHarmonyMod));
                AppDomain.CurrentDomain.SetData(AppDomainPluginKey, _pluginWrapper);
            }
            catch (Exception ex) { Debug.LogWarning("[Shop Harmony] RegisterApiType: " + ex.Message); }
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

        public object Call(string method, params object[] args)
        {
            if (_plugin == null || string.IsNullOrEmpty(method)) return null;
            try
            {
                args ??= Array.Empty<object>();
                var count = args.Length;
                var mi = typeof(Shop).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == method && m.GetParameters().Length == count);
                if (mi == null)
                {
                    Debug.LogWarning($"[Shop Harmony] Call({method}): method not found for {count} arg(s)");
                    return null;
                }
                return mi.Invoke(_plugin, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Shop Harmony] Call({method}): " + (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }

        // ---- Commands ----

        private void RegisterCommands()
        {
            // Main CUI console entry (also reached via cui.endtest SHOP)
            RegisterConsole("UI_Shop", arg => InvokeConsoleMethod(nameof(Shop.CmdConsoleShop), arg));
            RegisterConsole("UI_Shop_Installer", arg => InvokeConsoleMethod(nameof(Shop.CmdConsoleShopInstaller), arg));
            RegisterConsole("openshopUI", arg => InvokeConsoleMethod(nameof(Shop.OpenShopUI), arg));

            RegisterConsole("shop.item", arg => InvokeConsoleMethod(nameof(Shop.CmdShopItem), arg), serverAdmin: true);
            RegisterConsole("shop.change", arg => InvokeConsoleMethod(nameof(Shop.CmdChangeItemCategory), arg), serverAdmin: true);
            RegisterConsole("shop.refill", arg => InvokeConsoleMethod(nameof(Shop.CmdConsoleRefill), arg), serverAdmin: true);
            RegisterConsole("shop.wipe", arg => InvokeConsoleMethod(nameof(Shop.CmdConsoleWipe), arg), serverAdmin: true);
            RegisterConsole("shop.reset", arg => InvokeConsoleMethod(nameof(Shop.CmdConsoleReset), arg), serverAdmin: true);
            RegisterConsole("shop.remove", arg => InvokeConsoleMethod(nameof(Shop.CmdConsoleRemoveItem), arg), serverAdmin: true);
            RegisterConsole("shop.fill.icc", arg => InvokeConsoleMethod(nameof(Shop.CmdConsoleFillICC), arg), serverAdmin: true);
            RegisterConsole("shop.wipesummary", arg => InvokeConsoleMethod(nameof(Shop.CmdShopWipeSummary), arg), serverAdmin: true);
            RegisterConsole("shop.setwipestart", arg => InvokeConsoleMethod(nameof(Shop.CmdSetWipeStart), arg), serverAdmin: true);
            RegisterConsole("shop.install", arg => InvokeConsoleMethod(nameof(Shop.CmdConsoleShopInstall), arg), serverAdmin: true);
            RegisterConsole("shop.manage", arg => InvokeConsoleMethod(nameof(Shop.CmdConsoleShopManage), arg), serverAdmin: true);
            RegisterConsole("shop.discordtest", arg => InvokeConsoleMethod(nameof(Shop.CmdDiscordTest), arg), serverAdmin: true);
            Action<ConsoleSystem.Arg> horseCmd = arg =>
            {
                try
                {
                    _plugin?.CmdShopHorse(arg);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Shop Harmony] shop.horse: " + ex.Message);
                }
            };
            RegisterConsole("shop.horse", horseCmd);
            // Backward-compatible alias for old Shop data still using animalspawn.horse
            RegisterConsole("animalspawn.horse", horseCmd);

            // Default chat aliases until config RegisterCommands runs
            foreach (var name in new[] { "s", "shop", "shops", "shop.setvm", "shop.setnpc", "shop.install" })
                _chatCommandNames.Add(name);

            _commandMethodMap["shop.setvm"] = nameof(Shop.CmdSetCustomVM);
            _commandMethodMap["shop.setnpc"] = nameof(Shop.CmdSetShopNPC);
            _commandMethodMap["shop.install"] = nameof(Shop.CmdChatShopInstaller);
        }

        private void RegisterDeferredConsoleFromPlugin()
        {
            if (_plugin == null) return;
            try
            {
                foreach (var entry in _plugin.cmd.RegisteredConsoleCommands)
                {
                    if (string.IsNullOrEmpty(entry.name) || string.IsNullOrEmpty(entry.method)) continue;
                    if (_registeredCommands.Any(c =>
                            string.Equals(c.FullName, entry.name, StringComparison.OrdinalIgnoreCase) ||
                            (string.IsNullOrEmpty(c.Parent) &&
                             string.Equals(c.Name, entry.name, StringComparison.OrdinalIgnoreCase))))
                        continue;
                    var method = entry.method;
                    RegisterConsole(entry.name, arg => InvokeConsoleMethod(method, arg));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Shop Harmony] RegisterDeferredConsoleFromPlugin: " + ex.Message);
            }
        }

        private void RefreshChatCommandsFromConfig()
        {
            if (_plugin == null) return;
            try
            {
                foreach (var entry in _plugin.RegisteredCovalenceCommands)
                {
                    if (entry.commands == null || string.IsNullOrEmpty(entry.methodName)) continue;
                    foreach (var c in entry.commands)
                    {
                        if (string.IsNullOrEmpty(c)) continue;
                        _chatCommandNames.Add(c);
                        _commandMethodMap[c] = entry.methodName;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Shop Harmony] RefreshChatCommandsFromConfig: " + ex.Message);
            }

            _chatCommandNames.Add("shop.setvm");
            _chatCommandNames.Add("shop.setnpc");
            _chatCommandNames.Add("shop.install");
            if (!_commandMethodMap.ContainsKey("shop.setvm"))
                _commandMethodMap["shop.setvm"] = nameof(Shop.CmdSetCustomVM);
            if (!_commandMethodMap.ContainsKey("shop.setnpc"))
                _commandMethodMap["shop.setnpc"] = nameof(Shop.CmdSetShopNPC);
            if (!_commandMethodMap.ContainsKey("shop.install"))
                _commandMethodMap["shop.install"] = nameof(Shop.CmdChatShopInstaller);
        }

        private void InvokeConsoleMethod(string methodName, ConsoleSystem.Arg arg)
        {
            if (_plugin == null || arg == null || string.IsNullOrEmpty(methodName)) return;
            try
            {
                var mi = typeof(Shop).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null)
                {
                    Debug.LogWarning($"[Shop Harmony] Method not found: {methodName}");
                    return;
                }
                mi.Invoke(_plugin, new object[] { arg });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Shop Harmony] {methodName}: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        private void DispatchCovalenceCommand(string methodName, BasePlayer player, string command, string[] args)
        {
            if (_plugin == null || string.IsNullOrEmpty(methodName)) return;
            try
            {
                var mi = typeof(Shop).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null)
                {
                    Debug.LogWarning($"[Shop Harmony] Method not found: {methodName}");
                    return;
                }

                var parameters = mi.GetParameters();
                if (parameters.Length == 3 && parameters[0].ParameterType == typeof(IPlayer))
                {
                    mi.Invoke(_plugin, new object[] { player.ToIPlayer(), command, args ?? Array.Empty<string>() });
                }
                else if (parameters.Length == 3 && parameters[0].ParameterType == typeof(BasePlayer))
                {
                    mi.Invoke(_plugin, new object[] { player, command, args ?? Array.Empty<string>() });
                }
                else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(ConsoleSystem.Arg))
                {
                    var sb = new StringBuilder(command);
                    if (args != null)
                    {
                        for (int i = 0; i < args.Length; i++)
                        {
                            sb.Append(' ');
                            string s = args[i] ?? "";
                            if (s.IndexOfAny(new[] { ' ', '"' }) >= 0)
                                sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
                            else
                                sb.Append(s);
                        }
                    }
                    var opt = ConsoleSystem.Option.Server.Quiet();
                    if (player?.net?.connection != null)
                        opt = opt.FromConnection(player.net.connection);
                    mi.Invoke(_plugin, new object[] { new ConsoleSystem.Arg(opt, sb.ToString()) });
                }
                else
                {
                    Debug.LogWarning($"[Shop Harmony] Unsupported signature for {methodName}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Shop Harmony] {methodName}: " + (ex.InnerException?.Message ?? ex.Message));
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
                    catch (Exception ex) { Debug.LogWarning($"[Shop] command {localName}: " + ex.Message); }
                }
            };

            ConsoleSystem.Index.Server.Dict[dictKey] = cmd;
            if (hasDot)
            {
                // Facepunch may resolve Parent.Name or FullName depending on client
                if (!string.IsNullOrEmpty(fullName) &&
                    !string.Equals(dictKey, fullName, StringComparison.OrdinalIgnoreCase))
                    ConsoleSystem.Index.Server.Dict[fullName] = cmd;
            }
            else if (ConsoleSystem.Index.Server.GlobalDict != null)
            {
                ConsoleSystem.Index.Server.GlobalDict[cmdName] = cmd;
            }

            _registeredCommands.Add(cmd);
        }

        private static void ScrubShopFromReplicatedList()
        {
            try
            {
                var replicated = typeof(ConsoleSystem.Index.Server)
                    .GetField("Replicated", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as System.Collections.IList;
                if (replicated == null)
                {
                    replicated = typeof(ConsoleSystem.Index.Server)
                        .GetProperty("Replicated", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as System.Collections.IList;
                }
                if (replicated == null) return;

                for (int i = replicated.Count - 1; i >= 0; i--)
                {
                    if (replicated[i] is not ConsoleSystem.Command cmd) continue;
                    string full = cmd.FullName ?? string.Empty;
                    string parent = cmd.Parent ?? string.Empty;
                    string name = cmd.Name ?? string.Empty;

                    bool isShop =
                        full.StartsWith("shop.", StringComparison.OrdinalIgnoreCase) ||
                        full.Equals("global.UI_Shop", StringComparison.OrdinalIgnoreCase) ||
                        full.Equals("global.UI_Shop_Installer", StringComparison.OrdinalIgnoreCase) ||
                        full.Equals("global.openshopUI", StringComparison.OrdinalIgnoreCase) ||
                        parent.Equals("shop", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("UI_Shop", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("UI_Shop_Installer", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("openshopUI", StringComparison.OrdinalIgnoreCase);

                    if (isShop)
                    {
                        cmd.Replicated = false;
                        cmd.Variable = false;
                        replicated.RemoveAt(i);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Shop] ScrubShopFromReplicatedList: " + ex.Message);
            }
        }

        private void UnregisterCommands()
        {
            ScrubShopFromReplicatedList();
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
            _chatCommandNames.Clear();
            _commandMethodMap.Clear();
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

            if (!_commandMethodMap.TryGetValue(name, out var methodName))
                methodName = nameof(Shop.CmdShopOpen);

            // shop.install chat route uses BasePlayer signature
            if (name.Equals("shop.install", StringComparison.OrdinalIgnoreCase))
                methodName = nameof(Shop.CmdChatShopInstaller);

            DispatchCovalenceCommand(methodName, player, name, args);
            return true;
        }

        /// <summary>Route cui.endtest SHOP … to CmdConsoleShop.</summary>
        public void HandleCuiShop(ConsoleSystem.Arg args, string[] a)
        {
            if (_plugin == null || a == null || a.Length < 1) return;
            var player = args.Connection?.player as BasePlayer ?? args.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return;

            var sb = new StringBuilder("UI_Shop");
            for (int i = 1; i < a.Length; i++)
            {
                sb.Append(' ');
                string s = a[i].ToString() ?? string.Empty;
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
                _plugin.CmdConsoleShop(new ConsoleSystem.Arg(opt, sb.ToString()));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Shop] cui.endtest SHOP: " + ex);
            }
        }

        /// <summary>Route cui.endtest SHOPINST … to CmdConsoleShopInstaller.</summary>
        public void HandleCuiShopInstaller(ConsoleSystem.Arg args, string[] a)
        {
            if (_plugin == null || a == null || a.Length < 1) return;
            var player = args.Connection?.player as BasePlayer ?? args.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return;

            var sb = new StringBuilder("UI_Shop_Installer");
            for (int i = 1; i < a.Length; i++)
            {
                sb.Append(' ');
                string s = a[i].ToString() ?? string.Empty;
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
                _plugin.CmdConsoleShopInstaller(new ConsoleSystem.Arg(opt, sb.ToString()));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Shop] cui.endtest SHOPINST: " + ex);
            }
        }

        public sealed class ShopPluginWrapper
        {
            private readonly ShopHarmonyMod _mod;
            public ShopPluginWrapper(ShopHarmonyMod mod) => _mod = mod;
            public bool IsLoaded => _mod?._plugin != null;
            public string Name => "Shop";
            public string Version => $"{VersionMajor}.{VersionMinor}.{VersionPatch}";
            public object Call(string method, params object[] args) => _mod?.Call(method, args);
        }
    }
}
