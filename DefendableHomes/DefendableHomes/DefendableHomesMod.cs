using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using OxidePlugin = Oxide.Plugins.DefendableHomes;

namespace DefendableHomes
{
    /// <summary>
    /// Harmony entry point for Defendable Homes. Instantiates the ported Oxide plugin body,
    /// loads config from HarmonyConfig/DefendableHomes.json, registers commands, and drives
    /// Init / OnServerInitialized / Unload.
    /// </summary>
    public class DefendableHomesMod : IHarmonyModHooks
    {
        public static DefendableHomesMod Instance { get; private set; }
        public static OxidePlugin Plugin { get; private set; }

        private HarmonyLib.Harmony _harmony;
        private Coroutine _initCoroutine;
        private readonly List<ConsoleSystem.Command> _commands = new List<ConsoleSystem.Command>();
        private Func<string, object[], object> _grimmHookHandler;

        private static readonly string[] StartCommands = { "giveflare", "defstop" };

        public const string AppDomainApiKey = "DefendableHomes_ApiType";

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            ModRunner.Ensure();

            try
            {
                Plugin = new OxidePlugin();
                Plugin.HarmonyLoadConfig();
            }
            catch (Exception ex)
            {
                Debug.LogError("[DefendableHomes] Failed to construct/load plugin: " + ex);
                return;
            }

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(DefendableHomesMod)); }
            catch { }

            DefendableHomesGrimmNpc.Bind();
            DefendableHomesDamageApi.Publish();
            RegisterGrimmHookHandler();

            try
            {
                _harmony = new HarmonyLib.Harmony("com.facepunch.rust_dedicated.DefendableHomes.find");
                if (!Patches.Patch_ConsoleSystem_Server_Find.TryApply(_harmony))
                    Debug.LogWarning("[DefendableHomes] Could not patch ConsoleSystem.Find; relying on command dictionary registration.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DefendableHomes] Find patch failed (non-fatal): " + ex.Message);
            }

            RegisterCommands();

            _initCoroutine = ModRunner.Instance.StartCoroutine(WaitForServerThenInit());
            Debug.Log("[DefendableHomes] Harmony mod loaded. Commands: giveflare, defstop, plus config CheckCommand. Config: HarmonyConfig/DefendableHomes.json. Requires 0GrimmNPC.");
        }

        private IEnumerator WaitForServerThenInit()
        {
            while (ServerMgr.Instance == null)
                yield return null;
            yield return new WaitForSeconds(2f);

            DefendableHomesGrimmNpc.Bind();

            try { Plugin?.CallInit(); }
            catch (Exception ex) { Debug.LogWarning("[DefendableHomes] Init failed: " + ex.Message); }

            try { Plugin?.CallOnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[DefendableHomes] OnServerInitialized failed: " + ex); }

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
            catch (Exception ex) { Debug.LogWarning("[DefendableHomes] Unload failed: " + ex.Message); }

            UnregisterGrimmHookHandler();
            DefendableHomesDamageApi.Unpublish();
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); } catch { }
            UnregisterCommands();

            try { _harmony?.UnpatchAll(_harmony.Id); }
            catch { }
            _harmony = null;

            ModRunner.Destroy();
            Plugin = null;
            Instance = null;
            Debug.Log("[DefendableHomes] Harmony mod unloaded.");
        }

        private void RegisterGrimmHookHandler()
        {
            _grimmHookHandler = OxidePlugin.Dispatch_GrimmHook;
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
                Debug.LogWarning("[DefendableHomes] Failed to register GrimmNPC hook handler: " + ex.Message);
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
                RegisterNamed("giveflare", HandleGiveFlare);
                RegisterNamed("defstop", HandleDefStop);
                Debug.Log("[DefendableHomes] Commands registered (server console / F1 / chat).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DefendableHomes] Command registration failed: " + ex.Message);
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
                    arg.ReplyWith("[DefendableHomes] " + name + " must be run by a player.");
                    return;
                }
                string[] args = ArgStrings(arg);
                callback(player, name, args);
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

        /// <summary>
        /// Shop Command products use <c>giveflare 2888602635 %steamid%</c> (skin or EASY/MEDIUM/HARD).
        /// ConsoleSystem.Run often cannot see Harmony Dict-only commands, so Shop calls this directly.
        /// </summary>
        public bool TryRunServerCommand(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine) || Plugin == null) return false;
            commandLine = commandLine.Trim();
            if (commandLine.StartsWith("/") || commandLine.StartsWith("\\"))
                commandLine = commandLine.Substring(1).Trim();

            string[] parts = commandLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return false;
            if (!string.Equals(parts[0], "giveflare", StringComparison.OrdinalIgnoreCase)) return false;
            if (!ulong.TryParse(parts[2], out ulong steamId) || steamId == 0) return false;
            int amount = 1;
            if (parts.Length > 3) int.TryParse(parts[3], out amount);
            return OxidePlugin.TryGiveFlare(parts[1], steamId, amount);
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

        private static void HandleGiveFlare(ConsoleSystem.Arg arg)
        {
            var player = PlayerOf(arg);
            if (player == null)
            {
                OxidePlugin.CmdGiveFlareConsole(arg);
                return;
            }
            if (!player.IsAdmin)
            {
                arg.ReplyWith("[DefendableHomes] Only admins can use giveflare.");
                return;
            }
            OxidePlugin.CmdGiveFlareChat(player, ArgStrings(arg));
        }

        private static void HandleDefStop(ConsoleSystem.Arg arg)
        {
            var player = PlayerOf(arg);
            if (player == null)
            {
                arg.ReplyWith("[DefendableHomes] defstop must be run by a player.");
                return;
            }
            OxidePlugin.CmdDefStop(player);
        }
        #endregion
    }

    /// <summary>Persistent MonoBehaviour for NextTick queueing and coroutines.</summary>
    public class ModRunner : MonoBehaviour
    {
        public static ModRunner Instance { get; private set; }
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static GameObject _go;

        public static void Ensure()
        {
            if (Instance != null) return;
            _go = new GameObject("DefendableHomes_Runner");
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
                catch (Exception ex) { Debug.LogWarning("[DefendableHomes] NextTick action failed: " + ex.Message); }
            }
        }
    }
}
