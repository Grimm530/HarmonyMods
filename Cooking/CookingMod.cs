// CookingMod.cs — Harmony entry point for Cooking 2.0.35
// Hosts Oxide.Plugins.Cooking, lifecycle, chat + console commands, SkillTree API.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyChat;
using UnityEngine;
using OxidePlugin = Oxide.Plugins.Cooking;

namespace CookingHarmony
{
    public class ModRunner : MonoBehaviour
    {
        public static ModRunner Instance { get; private set; }
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static GameObject _go;

        public static void Ensure()
        {
            if (Instance != null) return;
            _go = new GameObject("Cooking_Runner");
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
                catch (Exception ex) { Debug.LogWarning("[Cooking] NextTick: " + ex.Message); }
            }
        }
    }

    public class CookingMod : IHarmonyModHooks
    {
        public static CookingMod Instance { get; private set; }
        public static OxidePlugin Plugin => OxidePlugin.GetModInstance();

        private Coroutine _initCoroutine;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();
        private readonly List<ConsoleSystem.Command> _chatAliasCommands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _uiConsoleCommands = new List<string>();
        private Action _permissionsReadyCallback;

        public const string AppDomainApiKey = "Cooking_ApiType";
        public const string CuiMarker = "COOKING";

        public IReadOnlyList<string> UiConsoleCommands => _uiConsoleCommands;

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
                Debug.LogError("[Cooking] Failed to construct/config plugin: " + ex);
                return;
            }

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(CookingMod)); }
            catch { }

            foreach (var cmd in new[] { "recipemenu", "cook", "market" })
            {
                _chatCommandNames.Add(cmd);
                RegisterChatAliasConsole(cmd);
            }

            ChatSayBridge.Register("Cooking", OnChatCommand);

            RegisterAttributedConsoleCommands();
            RegisterAttributedChatCommands();

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            _initCoroutine = ModRunner.Instance.StartCoroutine(WaitForServerThenInit());

            Debug.Log("[Cooking] Harmony mod loaded. Chat: /recipemenu /cook. Config: HarmonyConfig/Cooking.json.");
        }

        private void OnPermissionsReady()
        {
            try
            {
                var plugin = OxidePlugin.GetModInstance();
                plugin?.HarmonyReregisterPermissions();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Cooking] Permissions ready re-register: " + ex.Message);
            }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            ChatSayBridge.Unregister("Cooking");

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
            Debug.Log("[Cooking] Harmony mod unloaded.");
        }

        private IEnumerator WaitForServerThenInit(int attempt = 0)
        {
            while (ServerMgr.Instance == null) yield return null;
            while (ItemManager.itemList == null || ItemManager.itemList.Count == 0)
            {
                if (attempt > 120)
                {
                    Debug.LogWarning("[Cooking] ItemManager timeout; proceeding.");
                    break;
                }
                yield return new WaitForSeconds(attempt < 10 ? 0.5f : 1f);
                attempt++;
            }

            yield return new WaitForSeconds(1f);

            var plugin = Plugin;
            if (plugin == null) yield break;

            try { plugin.CallInit(); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] Init: " + ex.Message); }

            try { plugin.CallOnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[Cooking] OnServerInitialized: " + ex); }

            plugin.ResolvePluginReferences();
            RefreshDynamicCommands();

            _initCoroutine = null;
            Debug.Log("[Cooking] Server initialized.");
        }

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
            var plugin = Plugin;
            if (plugin == null) return false;

            foreach (var reg in plugin.cmd.RegisteredChatCommands)
            {
                if (!string.Equals(reg.name, commandName, StringComparison.OrdinalIgnoreCase)) continue;
                InvokeChatMethod(plugin, reg.method, player, commandName, args);
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

                var mi = type.GetMethod(methodName, bf, null, new[] { typeof(BasePlayer), typeof(string), typeof(string[]) }, null);
                if (mi != null) { mi.Invoke(plugin, new object[] { player, command, args }); return; }

                mi = type.GetMethod(methodName, bf, null, new[] { typeof(BasePlayer) }, null);
                if (mi != null) { mi.Invoke(plugin, new object[] { player }); return; }

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
                Debug.LogWarning("[Cooking] InvokeChatMethod " + methodName + ": " + ex.Message);
            }
        }

        private void RegisterAttributedConsoleCommands()
        {
            var plugin = Plugin;
            if (plugin == null) return;
            try
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var mi in typeof(OxidePlugin).GetMethods(bf))
                {
                    var attrs = mi.GetCustomAttributes(typeof(Oxide.Plugins.ConsoleCommandAttribute), inherit: false);
                    if (attrs == null || attrs.Length == 0) continue;
                    foreach (Oxide.Plugins.ConsoleCommandAttribute attr in attrs)
                    {
                        if (string.IsNullOrWhiteSpace(attr.Command)) continue;
                        var cmdName = attr.Command.Trim();
                        TrackUiConsoleCommand(cmdName);

                        if (_registeredCommands.Any(c =>
                                string.Equals(c.Name, cmdName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(c.FullName, "global." + cmdName, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        var methodName = mi.Name;
                        RegisterConsole(cmdName, arg => InvokeConsoleMethod(methodName, arg), serverAdmin: false);
                    }
                }
                SortUiConsoleCommands();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Cooking] RegisterAttributedConsoleCommands: " + ex.Message);
            }
        }

        private void RegisterAttributedChatCommands()
        {
            var plugin = Plugin;
            if (plugin == null) return;
            try
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var mi in typeof(OxidePlugin).GetMethods(bf))
                {
                    var attrs = mi.GetCustomAttributes(typeof(Oxide.Plugins.ChatCommandAttribute), inherit: false);
                    if (attrs == null || attrs.Length == 0) continue;
                    foreach (Oxide.Plugins.ChatCommandAttribute attr in attrs)
                    {
                        if (string.IsNullOrWhiteSpace(attr.Command)) continue;
                        var chatName = attr.Command.Trim().ToLowerInvariant();
                        plugin.cmd.AddChatCommand(chatName, plugin, mi.Name);
                        _chatCommandNames.Add(chatName);
                        RegisterChatAliasConsole(chatName);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Cooking] RegisterAttributedChatCommands: " + ex.Message);
            }
        }

        private void TrackUiConsoleCommand(string cmdName)
        {
            if (string.IsNullOrEmpty(cmdName)) return;
            for (int i = 0; i < _uiConsoleCommands.Count; i++)
            {
                if (string.Equals(_uiConsoleCommands[i], cmdName, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            _uiConsoleCommands.Add(cmdName);
        }

        private void SortUiConsoleCommands()
        {
            _uiConsoleCommands.Sort((a, b) => b.Length.CompareTo(a.Length));
        }

        private void RefreshDynamicCommands()
        {
            var plugin = Plugin;
            if (plugin == null) return;
            try
            {
                foreach (var reg in plugin.cmd.RegisteredChatCommands)
                {
                    if (string.IsNullOrEmpty(reg.name)) continue;
                    var chatName = reg.name.ToLowerInvariant();
                    _chatCommandNames.Add(chatName);
                    RegisterChatAliasConsole(chatName);
                }

                foreach (var reg in plugin.cmd.RegisteredConsoleCommands)
                {
                    if (string.IsNullOrEmpty(reg.name)) continue;
                    var name = reg.name.Trim();
                    TrackUiConsoleCommand(name);
                    if (_registeredCommands.Any(c => string.Equals(c.FullName, "global." + name, StringComparison.OrdinalIgnoreCase) ||
                                                     string.Equals(c.FullName, name, StringComparison.OrdinalIgnoreCase) ||
                                                     string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var captured = reg;
                    RegisterConsole(name, arg => InvokeConsoleMethod(captured.method, arg), serverAdmin: false);
                }
                SortUiConsoleCommands();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Cooking] RefreshDynamicCommands: " + ex.Message);
            }
        }

        private void RegisterChatAliasConsole(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (name.IndexOf('.') >= 0) return;

            if (_chatAliasCommands.Any(c =>
                    string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.FullName, "global." + name, StringComparison.OrdinalIgnoreCase)))
                return;

            if (_registeredCommands.Any(c =>
                    string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.FullName, "global." + name, StringComparison.OrdinalIgnoreCase)))
                return;

            string localName = name;
            if (ConsoleSystem.Index.Server.Dict != null &&
                ConsoleSystem.Index.Server.Dict.ContainsKey("global." + localName))
                return;

            var cmd = new ConsoleSystem.Command
            {
                Name = localName,
                Parent = string.Empty,
                FullName = "global." + localName,
                Variable = false,
                ServerAdmin = false,
                ServerUser = true,
                AllowRunFromServer = true,
                Replicated = false,
                Call = a =>
                {
                    try
                    {
                        var player = a?.Player();
                        if (player == null) return;
                        var sb = new StringBuilder(localName);
                        var raw = a.Args;
                        if (raw != null)
                        {
                            for (int i = 0; i < raw.Length; i++)
                            {
                                sb.Append(' ');
                                sb.Append(raw[i].ToString() ?? string.Empty);
                            }
                        }
                        OnChatCommand(player, sb.ToString());
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[Cooking] chat alias " + localName + ": " + ex.Message);
                    }
                }
            };

            try
            {
                ConsoleSystem.Index.Server.Dict["global." + localName] = cmd;
                if (ConsoleSystem.Index.Server.GlobalDict != null)
                    ConsoleSystem.Index.Server.GlobalDict[localName] = cmd;
                _chatAliasCommands.Add(cmd);
                _registeredCommands.Add(cmd);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Cooking] RegisterChatAliasConsole(" + localName + "): " + ex.Message);
            }
        }

        public void HandleCuiEndtest(ConsoleSystem.Arg args, Array a)
        {
            if (Plugin == null || a == null || a.Length < 2) return;
            var player = args.Connection?.player as BasePlayer ?? args.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return;

            var sb = new StringBuilder();
            for (int i = 1; i < a.Length; i++)
            {
                if (i > 1) sb.Append(' ');
                string s = a.GetValue(i)?.ToString() ?? string.Empty;
                if (s.IndexOfAny(new[] { ' ', '"' }) >= 0)
                    sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
                else
                    sb.Append(s);
            }

            string full = sb.ToString();
            string cmdName = a.GetValue(1)?.ToString() ?? "";
            if (string.IsNullOrEmpty(cmdName)) return;

            try
            {
                var opt = ConsoleSystem.Option.Server.Quiet();
                if (args.Connection != null)
                    opt = opt.FromConnection(args.Connection);
                var uiArg = new ConsoleSystem.Arg(opt, full);

                ConsoleSystem.Command cmd = null;
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                if (dict != null)
                {
                    if (!dict.TryGetValue("global." + cmdName, out cmd))
                        dict.TryGetValue(cmdName, out cmd);
                }
                if (cmd == null && globalDict != null)
                    globalDict.TryGetValue(cmdName, out cmd);
                if (cmd == null && globalDict != null)
                {
                    foreach (var kvp in globalDict)
                    {
                        if (string.Equals(kvp.Key.ToString(), cmdName, StringComparison.OrdinalIgnoreCase))
                        {
                            cmd = kvp.Value;
                            break;
                        }
                    }
                }

                if (cmd?.Call != null)
                {
                    cmd.Call(uiArg);
                    return;
                }

                Debug.LogWarning("[Cooking] cui.endtest COOKING: command not registered: " + cmdName);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Cooking] cui.endtest COOKING: " + ex.Message);
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
                Debug.LogWarning("[Cooking] " + methodName + ": " + ex.Message);
            }
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler, bool serverAdmin = false)
        {
            bool hasDot = name.Contains(".");
            string parent = hasDot ? name.Split('.')[0] : "";
            string cmdName = hasDot ? name.Split(new[] { '.' }, 2)[1] : name;
            string fullName = hasDot ? name : "global." + name;
            string dictKey = hasDot ? name : fullName;

            var captured = name;
            var cmd = new ConsoleSystem.Command
            {
                Name = cmdName,
                Parent = parent,
                FullName = fullName,
                Variable = false,
                ServerAdmin = serverAdmin,
                AllowRunFromServer = true,
                Replicated = false,
                Call = a =>
                {
                    try { handler(a); }
                    catch (Exception ex) { Debug.LogWarning("[Cooking] cmd " + captured + ": " + ex.Message); }
                }
            };

            ConsoleSystem.Index.Server.Dict[dictKey] = cmd;
            if (!hasDot && ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict[cmdName] = cmd;

            _registeredCommands.Add(cmd);
        }

        private void UnregisterConsoleCommands()
        {
            _chatAliasCommands.Clear();
            try
            {
                var dict = ConsoleSystem.Index.Server.Dict;
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
            _uiConsoleCommands.Clear();
        }

        // ---- SkillTree Plugin.Call surface (static methods on Cooking_ApiType) ----

        public static object Call(string method, params object[] args)
        {
            if (string.IsNullOrEmpty(method)) return null;
            var plugin = Plugin;
            if (plugin == null) return null;
            try
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var types = args?.Select(a => a?.GetType() ?? typeof(object)).ToArray() ?? Type.EmptyTypes;
                var mi = typeof(OxidePlugin).GetMethod(method, bf, null, types, null)
                    ?? typeof(OxidePlugin).GetMethods(bf).FirstOrDefault(m =>
                        m.Name == method && m.GetParameters().Length == (args?.Length ?? 0));
                return mi?.Invoke(plugin, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Cooking] Call(" + method + "): " + ex.Message);
                return null;
            }
        }

        public static bool IsCookingMeal(Item item) => Plugin != null && Plugin.IsCookingMeal(item);
        public static bool IsCustomIngredient(Item item) => Plugin != null && Plugin.IsCustomIngredient(item);
        public static bool IsHorseBuffed(RidableHorse horse) => Plugin != null && Plugin.IsHorseBuffed(horse);
        public static object GetCookingMealsAndIngredients() => Plugin?.GetCookingMealsAndIngredients();
        public static int API_GetBagItemCount(ulong playerId, string shortname, ulong skin)
            => Plugin?.API_GetBagItemCount(playerId, shortname, skin) ?? 0;
        public static bool API_TakeBagItems(ulong playerId, string shortname, ulong skin, int amount)
            => Plugin != null && Plugin.API_TakeBagItems(playerId, shortname, skin, amount);
    }
}
