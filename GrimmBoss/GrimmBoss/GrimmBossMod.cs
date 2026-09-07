using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GrimmBoss.Patches;
using HarmonyPlugin = Harmony.Plugins.GrimmBoss;

namespace GrimmBoss
{
    /// <summary>
    /// Harmony entry for GrimmBoss. Instantiates the ported Harmony mod body, loads
    /// HarmonyConfig/GrimmBoss.json + HarmonyData/GrimmBoss/, registers admin commands,
    /// and drives Init → OnServerInitialized → Unload. Requires 0GrimmNPC (NpcSpawn API).
    /// </summary>
    public class GrimmBossMod : IHarmonyModHooks
    {
        public static GrimmBossMod Instance { get; private set; }
        public static HarmonyPlugin Plugin { get; private set; }

        private Coroutine _initCoroutine;
        private readonly List<ConsoleSystem.Command> _commands = new List<ConsoleSystem.Command>();

        private static readonly string[] CommandNames =
        {
            "worldpos", "savepos", "custompos", "spawnboss", "killboss"
        };

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            GrimmCoreHurtRegistration.Register();
            ModRunner.Ensure();

            try
            {
                Plugin = new HarmonyPlugin();
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
            int attempts = 0;
            while (ServerMgr.Instance == null)
                yield return null;

            while (attempts < 360)
            {
                if (IsWorldReadyForBossInit())
                    break;
                attempts++;
                yield return new WaitForSeconds(attempts < 24 ? 2f : 5f);
            }

            yield return new WaitForSeconds(4f);

            GrimmBossGrimmNpc.Bind();

            try { Plugin?.CallInit(); }
            catch (Exception ex) { Debug.LogWarning("[GrimmBoss] Init failed: " + ex.Message); }

            yield return new WaitForSeconds(1f);

            try { Plugin?.CallOnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[GrimmBoss] OnServerInitialized failed: " + ex); }

            _initCoroutine = null;
        }

        private static bool IsWorldReadyForBossInit()
        {
            try
            {
                if (TerrainMeta.HeightMap == null || !TerrainMeta.HeightMap.isInitialized || World.Size <= 0)
                    return false;
                if (TerrainMeta.Path?.Monuments == null)
                    return false;
                if (!ConVar.AI.move || Rust.Ai.AiManager.nav_disable)
                    return false;

                if (TerrainMeta.Path.Monuments != null)
                {
                    int tested = 0;
                    foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
                    {
                        if (monument == null) continue;
                        Vector3 p = monument.transform.position;
                        if (UnityEngine.AI.NavMesh.SamplePosition(p, out _, 80f, UnityEngine.AI.NavMesh.AllAreas))
                            return true;
                        if (++tested >= 12) break;
                    }
                }

                Vector3 probe = Vector3.zero;
                probe.y = TerrainMeta.HeightMap.GetHeight(probe);
                return UnityEngine.AI.NavMesh.SamplePosition(probe, out _, 500f, UnityEngine.AI.NavMesh.AllAreas);
            }
            catch
            {
                return false;
            }
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
            GrimmCoreHurtRegistration.Unregister();
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
            HarmonyPlugin.CmdWorldPos(player);
        }

        private static void HandleSavePos(ConsoleSystem.Arg arg)
        {
            var player = PlayerOf(arg);
            if (player == null) { arg.ReplyWith("[GrimmBoss] savepos must be run by a player."); return; }
            if (DenyIfNotAdmin(arg, player)) return;
            HarmonyPlugin.CmdSavePos(player, ArgsOf(arg));
        }

        private static void HandleCustomPos(ConsoleSystem.Arg arg)
        {
            var player = PlayerOf(arg);
            if (player == null) { arg.ReplyWith("[GrimmBoss] custompos must be run by a player."); return; }
            if (DenyIfNotAdmin(arg, player)) return;
            HarmonyPlugin.CmdCustomPos(player, ArgsOf(arg));
        }

        private static void HandleSpawnBoss(ConsoleSystem.Arg arg)
        {
            var player = PlayerOf(arg);
            if (DenyIfNotAdmin(arg, player)) return;
            if (player != null)
                HarmonyPlugin.CmdSpawnBossChat(player, ArgsOf(arg));
            else
                HarmonyPlugin.CmdSpawnBossConsole(ArgsOf(arg));
        }

        private static void HandleKillBoss(ConsoleSystem.Arg arg)
        {
            var player = PlayerOf(arg);
            if (player != null)
            {
                arg.ReplyWith("[GrimmBoss] killboss is a server-console command.");
                return;
            }
            HarmonyPlugin.CmdKillBossConsole(ArgsOf(arg));
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
