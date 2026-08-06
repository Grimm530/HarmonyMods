using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using OxidePlugin = Oxide.Plugins.GrimmBoss;

namespace GrimmBoss
{
    /// <summary>
    /// Harmony entry for GrimmBoss. Instantiates the ported Oxide plugin body, loads
    /// HarmonyConfig/GrimmBoss.json + HarmonyData/GrimmBoss/, registers admin commands,
    /// and drives Init → OnServerInitialized → Unload. Requires 0GrimmNPC (NpcSpawn API).
    /// </summary>
    public class GrimmBossMod : IHarmonyModHooks
    {
        public static GrimmBossMod Instance { get; private set; }
        public static OxidePlugin Plugin { get; private set; }

        private Coroutine _initCoroutine;
        private readonly List<ConsoleSystem.Command> _commands = new List<ConsoleSystem.Command>();

        private static readonly string[] CommandNames =
        {
            "worldpos", "savepos", "custompos", "spawnboss", "killboss"
        };

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            ModRunner.Ensure();

            try
            {
                Plugin = new OxidePlugin();
                Plugin.HarmonyLoadConfig();
                Plugin.HarmonyLoadDefaultMessages();
            }
            catch (Exception ex)
            {
                Debug.LogError("[GrimmBoss] Failed to construct/load plugin: " + ex);
                return;
            }

            GrimmBossGrimmNpc.Bind();
            RegisterCommands();

            _initCoroutine = ModRunner.Instance.StartCoroutine(WaitForServerThenInit());
            Debug.Log("[GrimmBoss] Harmony mod loaded. Commands: " + string.Join(", ", CommandNames)
                + ". Config: HarmonyConfig/GrimmBoss.json. Data: HarmonyData/GrimmBoss/. Requires 0GrimmNPC.");
        }

        private IEnumerator WaitForServerThenInit()
        {
            while (ServerMgr.Instance == null)
                yield return null;
            yield return new WaitForSeconds(2f);

            GrimmBossGrimmNpc.Bind();

            try { Plugin?.CallInit(); }
            catch (Exception ex) { Debug.LogWarning("[GrimmBoss] Init failed: " + ex.Message); }

            try { Plugin?.CallOnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[GrimmBoss] OnServerInitialized failed: " + ex); }

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
            catch (Exception ex) { Debug.LogWarning("[GrimmBoss] Unload failed: " + ex.Message); }

            UnregisterCommands();
            ModRunner.Destroy();
            Plugin = null;
            Instance = null;
            Debug.Log("[GrimmBoss] Harmony mod unloaded.");
        }

        #region Commands
        private void RegisterCommands()
        {
            try
            {
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                foreach (string name in CommandNames)
                {
                    var cmd = new ConsoleSystem.Command
                    {
                        Name = name,
                        FullName = "global." + name,
                        Variable = false,
                        ServerAdmin = true,
                        ServerUser = true,
                        AllowRunFromServer = true,
                        Call = MakeHandler(name)
                    };
                    _commands.Add(cmd);
                    if (dict != null) dict["global." + name] = cmd;
                    if (globalDict != null) globalDict[name] = cmd;
                }
                Debug.Log("[GrimmBoss] Commands registered (server console / F1 / chat).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmBoss] Command registration failed: " + ex.Message);
            }
        }

        private void UnregisterCommands()
        {
            try
            {
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                foreach (string name in CommandNames)
                {
                    dict?.Remove("global." + name);
                    globalDict?.Remove(name);
                }
            }
            catch { }
            _commands.Clear();
        }

        private static Action<ConsoleSystem.Arg> MakeHandler(string name)
        {
            switch (name)
            {
                case "worldpos": return HandleWorldPos;
                case "savepos": return HandleSavePos;
                case "custompos": return HandleCustomPos;
                case "spawnboss": return HandleSpawnBoss;
                case "killboss": return HandleKillBoss;
                default: return arg => { };
            }
        }

        private static BasePlayer PlayerOf(ConsoleSystem.Arg arg) => arg?.Connection?.player as BasePlayer;

        private static bool DenyIfNotAdmin(ConsoleSystem.Arg arg, BasePlayer player)
        {
            // Console (no player) is allowed for SpawnBoss/KillBoss.
            if (player != null && !player.IsAdmin)
            {
                arg.ReplyWith("[GrimmBoss] Only admins can use this command.");
                return true;
            }
            return false;
        }

        private static string[] ArgsOf(ConsoleSystem.Arg arg)
        {
            if (arg?.Args == null || arg.Args.Length == 0) return Array.Empty<string>();
            string[] result = new string[arg.Args.Length];
            for (int i = 0; i < arg.Args.Length; i++)
                result[i] = arg.Args[i].ToString();
            return result;
        }

        private static void HandleWorldPos(ConsoleSystem.Arg arg)
        {
            var player = PlayerOf(arg);
            if (player == null) { arg.ReplyWith("[GrimmBoss] worldpos must be run by a player."); return; }
            if (DenyIfNotAdmin(arg, player)) return;
            OxidePlugin.CmdWorldPos(player);
        }

        private static void HandleSavePos(ConsoleSystem.Arg arg)
        {
            var player = PlayerOf(arg);
            if (player == null) { arg.ReplyWith("[GrimmBoss] savepos must be run by a player."); return; }
            if (DenyIfNotAdmin(arg, player)) return;
            OxidePlugin.CmdSavePos(player, ArgsOf(arg));
        }

        private static void HandleCustomPos(ConsoleSystem.Arg arg)
        {
            var player = PlayerOf(arg);
            if (player == null) { arg.ReplyWith("[GrimmBoss] custompos must be run by a player."); return; }
            if (DenyIfNotAdmin(arg, player)) return;
            OxidePlugin.CmdCustomPos(player, ArgsOf(arg));
        }

        private static void HandleSpawnBoss(ConsoleSystem.Arg arg)
        {
            var player = PlayerOf(arg);
            if (DenyIfNotAdmin(arg, player)) return;
            if (player != null)
                OxidePlugin.CmdSpawnBossChat(player, ArgsOf(arg));
            else
                OxidePlugin.CmdSpawnBossConsole(ArgsOf(arg));
        }

        private static void HandleKillBoss(ConsoleSystem.Arg arg)
        {
            var player = PlayerOf(arg);
            if (player != null)
            {
                arg.ReplyWith("[GrimmBoss] killboss is a server-console command.");
                return;
            }
            OxidePlugin.CmdKillBossConsole(ArgsOf(arg));
        }
        #endregion
    }

    /// <summary>Persistent MonoBehaviour for NextTick queueing and timer coroutines.</summary>
    public class ModRunner : MonoBehaviour
    {
        public static ModRunner Instance { get; private set; }
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static GameObject _go;

        public static void Ensure()
        {
            if (Instance != null) return;
            _go = new GameObject("GrimmBoss_Runner");
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

        public static void StartCoroutineStatic(IEnumerator routine)
        {
            Ensure();
            if (Instance != null && routine != null)
                Instance.StartCoroutine(routine);
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
                catch (Exception ex) { Debug.LogWarning("[GrimmBoss] NextTick action failed: " + ex.Message); }
            }
        }
    }
}
