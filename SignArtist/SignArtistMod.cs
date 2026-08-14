using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Oxide.Core.Libraries.Covalence;
using Oxide.Plugins;
using OxidePlugin = Oxide.Plugins.SignArtist;

namespace SignArtistHarmony
{
    public class ModRunner : MonoBehaviour
    {
        public static ModRunner Instance { get; private set; }
        private static GameObject _go;

        public static void Ensure()
        {
            if (Instance != null) return;
            _go = new GameObject("SignArtist_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _go.hideFlags = HideFlags.HideAndDontSave;
            Instance = _go.AddComponent<ModRunner>();
        }

        public static void Destroy()
        {
            if (_go != null) { UnityEngine.Object.Destroy(_go); _go = null; Instance = null; }
        }
    }

    public class SignArtistMod : IHarmonyModHooks
    {
        public static SignArtistMod Instance { get; private set; }
        internal static SignArtist Plugin => OxidePlugin.GetModInstance();
        public const string AppDomainApiKey = "SignArtist_ApiType";

        private Coroutine _initCoroutine;
        private readonly List<ConsoleSystem.Command> _registeredCommands = new List<ConsoleSystem.Command>();
        private readonly List<ConsoleSystem.Command> _chatAliasCommands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Action _permissionsReadyCallback;

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
                Debug.LogError("[SignArtist] Failed to construct/config plugin: " + ex);
                return;
            }

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(SignArtistMod)); }
            catch { }

            RegisterAttributedChatCommands();
            _permissionsReadyCallback = () =>
            {
                try { Plugin?.CallInit(); }
                catch (Exception ex) { Debug.LogWarning("[SignArtist] Permissions ready: " + ex.Message); }
            };
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);

            _initCoroutine = ModRunner.Instance.StartCoroutine(WaitForServerThenInit());
            Debug.Log("[SignArtist] Harmony mod loaded. Chat: /sil /silt /sili /silrestore. Config: HarmonyConfig/SignArtist.json");
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
            Debug.Log("[SignArtist] Harmony mod unloaded.");
        }

        private IEnumerator WaitForServerThenInit()
        {
            while (ServerMgr.Instance == null) yield return null;
            yield return new WaitForSeconds(1f);
            var plugin = Plugin;
            if (plugin == null) yield break;
            try { plugin.CallInit(); }
            catch (Exception ex) { Debug.LogWarning("[SignArtist] Init: " + ex.Message); }
            try { plugin.CallOnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[SignArtist] OnServerInitialized: " + ex); }
            RefreshDynamicCommands();
            _initCoroutine = null;
            Debug.Log("[SignArtist] Server initialized.");
        }

        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrWhiteSpace(message)) return false;
            message = message.Trim();
            if (message.StartsWith("/") || message.StartsWith("\\"))
                message = message.Substring(1).Trim();

            string[] parts = message.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            string commandName = parts[0];
            if (!_chatCommandNames.Contains(commandName)) return false;

            string[] args = parts.Length > 1 ? parts.Skip(1).ToArray() : Array.Empty<string>();
            var plugin = Plugin;
            if (plugin == null) return false;

            foreach (var reg in plugin.cmd.RegisteredChatCommands)
            {
                if (!string.Equals(reg.name, commandName, StringComparison.OrdinalIgnoreCase)) continue;
                InvokeChatMethod(plugin, reg.method, player, commandName, args);
                return true;
            }
            return InvokeAttributedChat(plugin, commandName, player, args);
        }

        private bool InvokeAttributedChat(OxidePlugin plugin, string commandName, BasePlayer player, string[] args)
        {
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var mi in typeof(OxidePlugin).GetMethods(bf))
            {
                foreach (CommandAttribute attr in mi.GetCustomAttributes(typeof(CommandAttribute), false))
                {
                    if (!string.Equals(attr.Command, commandName, StringComparison.OrdinalIgnoreCase)) continue;
                    InvokeMethod(plugin, mi, player, commandName, args);
                    return true;
                }
                foreach (ChatCommandAttribute attr in mi.GetCustomAttributes(typeof(ChatCommandAttribute), false))
                {
                    if (!string.Equals(attr.Command, commandName, StringComparison.OrdinalIgnoreCase)) continue;
                    InvokeMethod(plugin, mi, player, commandName, args);
                    return true;
                }
            }
            return false;
        }

        private static void InvokeChatMethod(OxidePlugin plugin, string methodName, BasePlayer player, string command, string[] args)
        {
            if (string.IsNullOrEmpty(methodName) || plugin == null) return;
            var mi = typeof(OxidePlugin).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi == null) return;
            InvokeMethod(plugin, mi, player, command, args);
        }

        private static void InvokeMethod(OxidePlugin plugin, MethodInfo mi, BasePlayer player, string command, string[] args)
        {
            try
            {
                var ps = mi.GetParameters();
                if (ps.Length == 3 && ps[0].ParameterType == typeof(IPlayer))
                {
                    mi.Invoke(plugin, new object[] { new BasePlayerWrapper(player), command, args });
                    return;
                }
                if (ps.Length == 3 && ps[0].ParameterType == typeof(BasePlayer))
                    mi.Invoke(plugin, new object[] { player, command, args });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SignArtist] Invoke " + mi.Name + ": " + ex);
            }
        }

        private void RegisterAttributedChatCommands()
        {
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var mi in typeof(OxidePlugin).GetMethods(bf))
            {
                foreach (CommandAttribute attr in mi.GetCustomAttributes(typeof(CommandAttribute), false))
                {
                    if (string.IsNullOrWhiteSpace(attr.Command)) continue;
                    _chatCommandNames.Add(attr.Command);
                    RegisterChatAliasConsole(attr.Command);
                }
                foreach (ChatCommandAttribute attr in mi.GetCustomAttributes(typeof(ChatCommandAttribute), false))
                {
                    if (string.IsNullOrWhiteSpace(attr.Command)) continue;
                    _chatCommandNames.Add(attr.Command);
                    RegisterChatAliasConsole(attr.Command);
                }
            }
        }

        private void RefreshDynamicCommands()
        {
            var plugin = Plugin;
            if (plugin == null) return;
            foreach (var reg in plugin.cmd.RegisteredChatCommands)
            {
                if (string.IsNullOrEmpty(reg.name)) continue;
                _chatCommandNames.Add(reg.name);
                RegisterChatAliasConsole(reg.name);
            }
        }

        private void RegisterChatAliasConsole(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOf('.') >= 0) return;
            name = name.Trim();
            if (_chatAliasCommands.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) return;
            if (ConsoleSystem.Index.Server.Dict != null &&
                ConsoleSystem.Index.Server.Dict.ContainsKey("global." + name))
                return;

            string localName = name;
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
                        var player = a?.Connection?.player as BasePlayer;
                        if (player == null) return;
                        var sb = new StringBuilder(localName);
                        if (a.Args != null)
                            for (int i = 0; i < a.Args.Length; i++)
                                sb.Append(' ').Append(a.Args[i].ToString());
                        OnChatCommand(player, sb.ToString());
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[SignArtist] chat alias " + localName + ": " + ex.Message);
                    }
                }
            };
            try
            {
                ConsoleSystem.Index.Server.Dict["global." + localName] = cmd;
                ConsoleSystem.Index.Server.GlobalDict[localName] = cmd;
                _chatAliasCommands.Add(cmd);
                _registeredCommands.Add(cmd);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SignArtist] RegisterChatAliasConsole(" + name + "): " + ex.Message);
            }
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
                    globalDict?.Remove(cmd.Name);
                }
            }
            catch { }
            _registeredCommands.Clear();
            _chatCommandNames.Clear();
        }
    }
}
