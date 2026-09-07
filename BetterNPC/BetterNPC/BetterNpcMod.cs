using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using BetterNpc.Patches;
using Harmony.Plugins;
using HarmonyPlugin = Harmony.Plugins.BetterNpc;

namespace BetterNpc
{
    /// <summary>
    /// Harmony entry point for BetterNPC. Instantiates the ported Harmony mod body,
    /// loads config from HarmonyConfig/BetterNpc.json, registers commands, and drives
    /// Init / OnServerInitialized / Unload.
    /// </summary>
    public class BetterNpcMod : IHarmonyModHooks
    {
        public static BetterNpcMod Instance { get; private set; }
        public static HarmonyPlugin Plugin { get; private set; }

        private HarmonyLib.Harmony _harmony;
        private Coroutine _initCoroutine;
        private readonly List<ConsoleSystem.Command> _commands = new List<ConsoleSystem.Command>();
        private Func<string, object[], object> _grimmHookHandler;

        public const string AppDomainApiKey = "BetterNpc_ApiType";

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            ModRunner.Ensure();

            try
            {
                Plugin = new HarmonyPlugin();
                Plugin.HarmonyLoadConfig();
            }
            catch (Exception ex)
            {
                Debug.LogError("[BetterNPC] Failed to construct/load plugin: " + ex);
                return;
            }

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(BetterNpcMod)); }
            catch { }

            BetterNpcGrimmNpc.Bind();
            RegisterGrimmHookHandler();

            try
            {
                _harmony = new HarmonyLib.Harmony("com.facepunch.rust_dedicated.BetterNpc.find");
                if (!Patch_ConsoleSystem_Server_Find.TryApply(_harmony))
                    Debug.LogWarning("[BetterNPC] Could not patch ConsoleSystem.Find; relying on command dictionary registration.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterNPC] Find patch failed (non-fatal): " + ex.Message);
            }

            RegisterCommands();

            _initCoroutine = ModRunner.Instance.StartCoroutine(WaitForServerThenInit());
            Debug.Log("[BetterNPC] Harmony mod loaded. Commands registered from plugin attributes. Config: HarmonyConfig/BetterNpc.json. Requires 0GrimmNPC (soft-fail if absent).");
        }

        private IEnumerator WaitForServerThenInit()
        {
            while (ServerMgr.Instance == null)
                yield return null;
            // Soft-start: wait for 0GrimmNPC position gen before monument soft-spawn begins.
            yield return new WaitForSeconds(5f);

            BetterNpcGrimmNpc.Bind();

            try { Plugin?.CallInit(); }
            catch (Exception ex) { Debug.LogWarning("[BetterNPC] Init failed: " + ex.Message); }

            yield return new WaitForSeconds(0.5f);

            try { Plugin?.CallOnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[BetterNPC] OnServerInitialized failed: " + ex); }

            _initCoroutine = null;
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            if (_initCoroutine != null && ModRunner.Instance != null)
            {
                ModRunner.Instance.StopCoroutine(_initCoroutine);
                _initCoroutine = null;
            }

            try { Plugin?.CallUnload(); }
            catch (Exception ex) { Debug.LogWarning("[BetterNPC] Unload failed: " + ex.Message); }

            UnregisterGrimmHookHandler();
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); } catch { }
            UnregisterCommands();

            try { _harmony?.UnpatchAll(_harmony.Id); }
            catch { }
            _harmony = null;

            ModRunner.Destroy();
            Plugin = null;
            Instance = null;
            Debug.Log("[BetterNPC] Harmony mod unloaded.");
        }

        private void RegisterGrimmHookHandler()
        {
            _grimmHookHandler = HarmonyPlugin.Dispatch_GrimmHook;
            try
            {
                var list = AppDomain.CurrentDomain.GetData("Harmony_CallHookList") as List<Func<string, object[], object>>;
                if (list == null)
                {
                    list = new List<Func<string, object[], object>>();
                    AppDomain.CurrentDomain.SetData("Harmony_CallHookList", list);
                }
                if (!list.Contains(_grimmHookHandler))
                    list.Add(_grimmHookHandler);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterNPC] Failed to register GrimmNPC hook handler: " + ex.Message);
            }
        }

        private void UnregisterGrimmHookHandler()
        {
            if (_grimmHookHandler == null) return;
            try
            {
                var list = AppDomain.CurrentDomain.GetData("Harmony_CallHookList") as List<Func<string, object[], object>>;
                list?.Remove(_grimmHookHandler);
            }
            catch { }
            _grimmHookHandler = null;
        }

        #region Commands
        private void RegisterCommands()
        {
            try
            {
                RegisterCommandsFromAttributes();
                Debug.Log("[BetterNPC] Commands registered (server console / F1 / chat).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterNPC] Command registration failed: " + ex.Message);
            }
        }

        private void RegisterCommandsFromAttributes()
        {
            if (Plugin == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var mi in Plugin.GetType().GetMethods(flags))
            {
                foreach (ChatCommandAttribute attr in mi.GetCustomAttributes(typeof(ChatCommandAttribute), false))
                {
                    var method = mi;
                    RegisterNamed(attr.Command, arg =>
                    {
                        var player = PlayerOf(arg);
                        if (player == null)
                        {
                            arg.ReplyWith("[BetterNPC] " + attr.Command + " must be run by a player.");
                            return;
                        }
                        InvokeChatCommand(method, player, attr.Command, ArgStrings(arg));
                    });
                }

                foreach (ConsoleCommandAttribute attr in mi.GetCustomAttributes(typeof(ConsoleCommandAttribute), false))
                {
                    var method = mi;
                    RegisterNamed(attr.Command, arg =>
                    {
                        try { method.Invoke(Plugin, new object[] { arg }); }
                        catch (Exception ex) { Debug.LogWarning("[BetterNPC] Console command " + attr.Command + " failed: " + ex.Message); }
                    });
                }
            }
        }

        private void InvokeChatCommand(MethodInfo method, BasePlayer player, string command, string[] args)
        {
            if (method == null || Plugin == null || player == null) return;
            var ps = method.GetParameters();
            try
            {
                if (ps.Length == 1)
                    method.Invoke(Plugin, new object[] { player });
                else if (ps.Length == 2)
                    method.Invoke(Plugin, new object[] { player, command });
                else
                    method.Invoke(Plugin, new object[] { player, command, args ?? Array.Empty<string>() });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterNPC] Chat command " + command + " failed: " + ex.Message);
            }
        }

        public void RegisterChatCommand(string name, Action<BasePlayer, string, string[]> callback)
        {
            if (string.IsNullOrEmpty(name) || callback == null) return;
            RegisterNamed(name, arg =>
            {
                var player = PlayerOf(arg);
                if (player == null)
                {
                    arg.ReplyWith("[BetterNPC] " + name + " must be run by a player.");
                    return;
                }
                callback(player, name, ArgStrings(arg));
            });
        }

        private void RegisterNamed(string name, Action<ConsoleSystem.Arg> handler)
        {
            var dict = ConsoleSystem.Index.Server.Dict;
            var globalDict = ConsoleSystem.Index.Server.GlobalDict;
            var cmd = new ConsoleSystem.Command
            {
                Name = name,
                FullName = "global." + name,
                Variable = false,
                ServerAdmin = true,
                ServerUser = true,
                AllowRunFromServer = true,
                Call = handler
            };
            _commands.Add(cmd);
            if (dict != null) dict["global." + name] = cmd;
            if (globalDict != null) globalDict[name] = cmd;
        }

        private void UnregisterCommands()
        {
            try
            {
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                foreach (var cmd in _commands)
                {
                    dict?.Remove("global." + cmd.Name);
                    globalDict?.Remove(cmd.Name);
                }
            }
            catch { }
            _commands.Clear();
        }

        public ConsoleSystem.Command GetCommand(string strName)
        {
            if (string.IsNullOrEmpty(strName)) return null;
            string n = strName.Trim().ToLowerInvariant();
            if (n.StartsWith("global.")) n = n.Substring(7);
            foreach (var cmd in _commands)
                if (string.Equals(cmd.Name, n, StringComparison.OrdinalIgnoreCase))
                    return cmd;
            return null;
        }

        private static BasePlayer PlayerOf(ConsoleSystem.Arg arg) => arg?.Connection?.player as BasePlayer;

        private static string[] ArgStrings(ConsoleSystem.Arg arg)
        {
            if (arg?.Args == null || arg.Args.Length == 0) return Array.Empty<string>();
            var result = new string[arg.Args.Length];
            for (int i = 0; i < arg.Args.Length; i++)
                result[i] = arg.GetString(i);
            return result;
        }
        #endregion
    }

    /// <summary>Persistent MonoBehaviour for NextTick queueing, timers, and coroutines.</summary>
    public class ModRunner : MonoBehaviour
    {
        public static ModRunner Instance { get; private set; }
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static GameObject _go;

        public static void Ensure()
        {
            if (Instance != null) return;
            _go = new GameObject("BetterNpc_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _go.hideFlags = HideFlags.HideAndDontSave;
            Instance = _go.AddComponent<ModRunner>();
        }

        public static void Destroy()
        {
            lock (_queue) _queue.Clear();
            if (_go != null)
            {
                UnityEngine.Object.Destroy(_go);
                _go = null;
                Instance = null;
            }
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
                Action action;
                lock (_queue)
                {
                    if (_queue.Count == 0) break;
                    action = _queue.Dequeue();
                }
                try { action(); }
                catch (Exception ex) { Debug.LogWarning("[BetterNPC] NextTick action failed: " + ex.Message); }
            }
        }
    }
}
