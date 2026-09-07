using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ArmoredTrain.Patches;
using HarmonyPlugin = Harmony.Plugins.ArmoredTrain;

namespace ArmoredTrain
{
    /// <summary>
    /// Harmony entry point for the Armored Train event. Instantiates the ported Harmony mod body
    /// (Harmony.Plugins.ArmoredTrain), loads config from HarmonyConfig/ArmoredTrain.json, registers the
    /// atrain* console commands, and drives the compat-style Init/OnServerInitialized/Unload lifecycle.
    /// </summary>
    public class ArmoredTrainMod : IHarmonyModHooks
    {
        public static ArmoredTrainMod Instance { get; private set; }
        public static HarmonyPlugin Plugin { get; private set; }

        private HarmonyLib.Harmony _harmony;
        private Coroutine _initCoroutine;
        private readonly List<ConsoleSystem.Command> _commands = new List<ConsoleSystem.Command>();

        private static readonly string[] StartCommands =
        {
            "atrainstart", "atrainstop", "atrainstartunderground", "atrainstartaboveground", "atrainpoint", "savecustomwagon"
        };

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            // Keep OnLoaded minimal — PatchAll already ran on this frame.
            // Config, Find patch, commands, and plugin init run in SoftStartCoroutine
            // so harmony.load does not stall the server (RaidableBases-style soft-start).
            Instance = this;
            GrimmCoreHurtRegistration.Register();
            ModRunner.Ensure();
            _initCoroutine = ModRunner.Instance.StartCoroutine(SoftStartCoroutine());
            Debug.Log("[ArmoredTrain] Harmony mod loaded (soft-start pending). Requires 0GrimmNPC.");
        }

        private IEnumerator SoftStartCoroutine()
        {
            while (ServerMgr.Instance == null)
                yield return null;

            // Leave the harmony.load / PatchAll frame before doing heavy work.
            yield return null;
            yield return null;

            if (Instance != this)
                yield break;

            try
            {
                Plugin = new HarmonyPlugin();
                Plugin.HarmonyLoadConfig();
            }
            catch (Exception ex)
            {
                Debug.LogError("[ArmoredTrain] Failed to construct/load plugin: " + ex);
                _initCoroutine = null;
                yield break;
            }

            yield return null;
            if (Instance != this)
                yield break;

            ArmoredTrainGrimmNpc.Bind();
            ArmoredTrainDamageApi.Publish();
            yield return null;
            if (Instance != this)
                yield break;

            // Facepunch HarmonyLoader already PatchAll's this assembly before OnLoaded.
            // Only apply the Find fallback with a separate ID (same pattern as Convoy).
            try
            {
                _harmony = new HarmonyLib.Harmony("com.facepunch.rust_dedicated.ArmoredTrain.find");
                if (!Patches.Patch_ConsoleSystem_Server_Find.TryApply(_harmony))
                    Debug.LogWarning("[ArmoredTrain] Could not patch ConsoleSystem.Find; relying on command dictionary registration.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ArmoredTrain] Find patch failed (non-fatal): " + ex.Message);
            }

            yield return null;
            if (Instance != this)
                yield break;

            RegisterCommands();
            Debug.Log("[ArmoredTrain] Soft-start: config + commands ready. Config: HarmonyConfig/ArmoredTrain.json. Data root: " + Harmony.Core.HarmonyModInterface.Mods.DataDirectory + ".");

            // Brief settle so other mods finishing soft-start are not stacked on the same frames.
            yield return CoroutineEx.waitForSeconds(2f);
            if (Instance != this)
                yield break;

            ArmoredTrainGrimmNpc.Bind();

            try { Plugin?.CallInit(); }
            catch (Exception ex) { Debug.LogWarning("[ArmoredTrain] Init failed: " + ex.Message); }

            yield return CoroutineEx.waitForSeconds(0.5f);
            if (Instance != this)
                yield break;

            try { Plugin?.CallOnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[ArmoredTrain] OnServerInitialized failed: " + ex); }

            Debug.Log("[ArmoredTrain] Soft-start complete.");
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
            catch (Exception ex) { Debug.LogWarning("[ArmoredTrain] Unload failed: " + ex.Message); }

            ArmoredTrainDamageApi.Unpublish();
            UnregisterCommands();

            try { _harmony?.UnpatchAll(_harmony.Id); }
            catch { }
            _harmony = null;

            ModRunner.Destroy();
            Plugin = null;
            GrimmCoreHurtRegistration.Unregister();
            Instance = null;
            Debug.Log("[ArmoredTrain] Harmony mod unloaded.");
        }

        #region Commands
        private void RegisterCommands()
        {
            try
            {
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                foreach (string name in StartCommands)
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
                Debug.Log("[ArmoredTrain] Commands registered (server console / F1 / chat).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ArmoredTrain] Command registration failed: " + ex.Message);
            }
        }

        private void UnregisterCommands()
        {
            try
            {
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                foreach (string name in StartCommands)
                {
                    dict?.Remove("global." + name);
                    globalDict?.Remove(name);
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

        private static Action<ConsoleSystem.Arg> MakeHandler(string name)
        {
            switch (name)
            {
                case "atrainstart": return arg => HandleStart(arg, null);
                case "atrainstop": return arg => HandleStop(arg);
                case "atrainstartunderground": return arg => HandleStart(arg, 100);
                case "atrainstartaboveground": return arg => HandleStart(arg, 0);
                case "atrainpoint": return arg => HandlePoint(arg);
                case "savecustomwagon": return arg => HandleSaveWagon(arg);
                default: return arg => { };
            }
        }

        private static BasePlayer PlayerOf(ConsoleSystem.Arg arg) => arg?.Connection?.player as BasePlayer;

        private static bool DenyIfNotAdmin(ConsoleSystem.Arg arg, BasePlayer player)
        {
            if (player != null && !player.IsAdmin)
            {
                arg.ReplyWith("[ArmoredTrain] Only admins can use this command.");
                return true;
            }
            return false;
        }

        private static void HandleStart(ConsoleSystem.Arg arg, int? overrideUnderground)
        {
            var player = PlayerOf(arg);
            if (DenyIfNotAdmin(arg, player)) return;
            string preset = null;
            try { if (arg.HasArgs(1)) preset = arg.GetString(0); } catch { }
            HarmonyPlugin.CmdStart(player, preset, overrideUnderground);
        }

        private static void HandleStop(ConsoleSystem.Arg arg)
        {
            var player = PlayerOf(arg);
            if (DenyIfNotAdmin(arg, player)) return;
            HarmonyPlugin.CmdStop();
            arg.ReplyWith("[ArmoredTrain] Stop requested.");
        }

        private static void HandlePoint(ConsoleSystem.Arg arg)
        {
            var player = PlayerOf(arg);
            if (player == null)
            {
                arg.ReplyWith("[ArmoredTrain] atrainpoint must be run by a player (F1 console).");
                return;
            }
            if (DenyIfNotAdmin(arg, player)) return;
            HarmonyPlugin.CmdPoint(player);
        }

        private static void HandleSaveWagon(ConsoleSystem.Arg arg)
        {
            var player = PlayerOf(arg);
            if (DenyIfNotAdmin(arg, player)) return;
            if (!arg.HasArgs(2))
            {
                arg.ReplyWith("[ArmoredTrain] Usage: savecustomwagon <presetName> <wagonShortPrefabName>");
                return;
            }
            HarmonyPlugin.CmdSaveCustomWagon(arg.GetString(0), arg.GetString(1));
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
            _go = new GameObject("ArmoredTrain_Runner");
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
                catch (Exception ex) { Debug.LogWarning("[ArmoredTrain] NextTick action failed: " + ex.Message); }
            }
        }
    }
}
