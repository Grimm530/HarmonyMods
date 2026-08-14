using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CustomMagazineHarmony
{
    public class CustomMagazineMod : IHarmonyModHooks
    {
        public static CustomMagazineMod Instance { get; private set; }
        public static CustomMagazinePlugin Plugin { get; private set; }

        public const int VersionMajor = 1;
        public const int VersionMinor = 0;
        public const int VersionPatch = 9;

        private readonly List<ConsoleSystem.Command> _commands = new List<ConsoleSystem.Command>();
        private GameObject _runner;
        private bool _serverReady;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            try
            {
                Plugin = new CustomMagazinePlugin(root);
                Plugin.LoadConfig();
            }
            catch (Exception ex)
            {
                Debug.LogError("[CustomMagazine] FAIL: construct/config: " + ex);
                return;
            }

            RegisterConsoleCommands();
            EnsureRunner();
            _runner.GetComponent<CustomMagazineRunner>().Begin(this);

            Debug.Log($"[CustomMagazine] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[CustomMagazine] -> Config: HarmonyConfig/CustomMagazine.json");
            Debug.Log("[CustomMagazine] Console: givemagazine <skinid> <steamid>");
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || Plugin == null) return;
            _serverReady = true;
            try
            {
                Plugin.OnServerInitialized();
                Debug.Log("[CustomMagazine] OK: Server initialized.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[CustomMagazine] FAIL: OnServerInitialized: " + ex);
            }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            UnregisterConsoleCommands();
            Plugin = null;
            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }
            Instance = null;
            Debug.Log("[CustomMagazine] OK: Unloaded.");
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("CustomMagazine_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<CustomMagazineRunner>();
        }

        private void RegisterConsoleCommands()
        {
            UnregisterConsoleCommands();
            try
            {
                var cmd = new ConsoleSystem.Command
                {
                    Name = "givemagazine",
                    FullName = "global.givemagazine",
                    Variable = false,
                    ServerAdmin = true,
                    ServerUser = false,
                    AllowRunFromServer = true,
                    Call = a => Plugin?.ConsoleGiveMagazine(a)
                };
                _commands.Add(cmd);
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                if (dict != null) dict[cmd.FullName] = cmd;
                if (globalDict != null) globalDict[cmd.Name] = cmd;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CustomMagazine] FAIL: RegisterConsole(givemagazine): " + ex.Message);
            }
        }

        private void UnregisterConsoleCommands()
        {
            try
            {
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                foreach (var cmd in _commands)
                {
                    dict?.Remove(cmd.FullName);
                    globalDict?.Remove(cmd.Name);
                }
            }
            catch { }
            _commands.Clear();
        }
    }

    internal sealed class CustomMagazineRunner : MonoBehaviour
    {
        private CustomMagazineMod _mod;
        private bool _started;

        public void Begin(CustomMagazineMod mod)
        {
            _mod = mod;
            if (!_started)
            {
                _started = true;
                StartCoroutine(WaitForServer());
            }
        }

        private IEnumerator WaitForServer()
        {
            while (ServerMgr.Instance == null)
                yield return null;
            yield return new WaitForSeconds(1f);
            _mod?.OnServerInitialized();
        }
    }
}
