using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace GrimmNPC
{
    /// <summary>
    /// Minimal Oxide API stand-ins so NpcSpawn logic can run as a Harmony mod without Oxide.Core references.
    /// </summary>
    public static class OxideCompat
    {
        private static readonly string ServerRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static readonly string ConfigPath = Path.Combine(ServerRoot, "HarmonyConfig", "GrimmNPC.json");
        private static readonly string DataRoot = Path.Combine(ServerRoot, "HarmonyConfig", "GrimmNPC");

        /// <summary>Replaces Interface.Oxide.DataDirectory for NpcSpawn folder layout under HarmonyConfig.</summary>
        public static string DataDirectory => Path.Combine(ServerRoot, "HarmonyConfig");

        public static readonly TimerHelper Timer = new TimerHelper();

        private static MethodInfo _callHook;
        private static readonly List<ConsoleSystem.Command> _commands = new List<ConsoleSystem.Command>();

        public static void EnsureDataFolders()
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(ServerRoot, "HarmonyConfig"));
                Directory.CreateDirectory(DataRoot);
                Directory.CreateDirectory(Path.Combine(DataRoot, "Preset"));
                Directory.CreateDirectory(Path.Combine(DataRoot, "NavMesh"));
                string npcSpawnData = Path.Combine(DataDirectory, "NpcSpawn");
                Directory.CreateDirectory(npcSpawnData);
                Directory.CreateDirectory(Path.Combine(npcSpawnData, "Preset"));
                Directory.CreateDirectory(Path.Combine(npcSpawnData, "NavMesh"));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmNPC] EnsureDataFolders: " + ex.Message);
            }
        }

        public static T ReadConfig<T>() where T : class, new()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    T cfg = JsonConvert.DeserializeObject<T>(json);
                    if (cfg != null) return cfg;
                }

                string oxideCfg = Path.Combine(ServerRoot, "oxide", "config", "NpcSpawn.json");
                if (File.Exists(oxideCfg))
                {
                    string json = File.ReadAllText(oxideCfg);
                    T cfg = JsonConvert.DeserializeObject<T>(json);
                    if (cfg != null)
                    {
                        WriteConfig(cfg);
                        Debug.Log("[GrimmNPC] Migrated config from oxide/config/NpcSpawn.json");
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmNPC] ReadConfig failed: " + ex.Message);
            }
            return new T();
        }

        public static void WriteConfig(object config)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath) ?? ServerRoot);
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmNPC] WriteConfig failed: " + ex.Message);
            }
        }

        public static object CallHook(string hook, params object[] args)
        {
            try
            {
                if (_callHook == null)
                {
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type t = asm.GetType("Oxide.Core.Interface");
                        if (t == null) continue;
                        _callHook = t.GetMethod("CallHook", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(object[]) }, null);
                        if (_callHook != null) break;
                    }
                }
                if (_callHook == null) return null;
                return _callHook.Invoke(null, new object[] { hook, args ?? Array.Empty<object>() });
            }
            catch
            {
                return null;
            }
        }

        public static void LogWarning(string message) => Debug.LogWarning("[GrimmNPC] " + message);

        public static void RunWhenServerInitialized(Action action)
        {
            if (action == null) return;
            // Harmony mods load BeforeSceneLoad — ServerMgr is often null here.
            // Never call Timer.Once when ServerMgr is null (it used to invoke sync and recurse).
            BootstrapRunner.Start(WaitForServer(action));
        }

        private static IEnumerator WaitForServer(Action action)
        {
            while (ServerMgr.Instance == null)
                yield return null;
            while (TerrainMeta.Size.x <= 0f)
                yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(0.5f);
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[GrimmNPC] RunWhenServerInitialized: " + ex); }
        }

        /// <summary>Runs a coroutine even when ServerMgr does not exist yet (Harmony BeforeSceneLoad).</summary>
        private sealed class BootstrapRunner : MonoBehaviour
        {
            public static void Start(IEnumerator routine)
            {
                if (routine == null) return;
                if (ServerMgr.Instance != null)
                {
                    ServerMgr.Instance.StartCoroutine(routine);
                    return;
                }
                var go = new GameObject("GrimmNPC_Bootstrap");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<BootstrapRunner>()._routine = routine;
            }

            private IEnumerator _routine;

            private void Start()
            {
                if (_routine != null)
                    StartCoroutine(RunAndDestroy(_routine));
            }

            private IEnumerator RunAndDestroy(IEnumerator routine)
            {
                yield return StartCoroutine(routine);
                if (this != null && gameObject != null)
                    UnityEngine.Object.Destroy(gameObject);
            }
        }

        public static void RegisterCommands(GrimmNPC mod)
        {
            UnregisterCommands();
            try
            {
                AddCommand("npccount", arg =>
                {
                    BasePlayer ply = arg?.Player();
                    if (ply != null && arg?.Connection != null)
                        mod.CmdNpcCount(ply, "npccount", ToStringArgs(arg));
                    else
                        mod.ConNpcCount(arg);
                });
                AddCommand("npcdiag", arg =>
                {
                    BasePlayer ply = arg?.Player();
                    if (ply != null && arg?.Connection != null && (arg.Args == null || arg.Args.Length == 0))
                        mod.CmdNpcDiag(ply, "npcdiag", ToStringArgs(arg));
                    else
                        mod.ConNpcDiag(arg);
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmNPC] RegisterCommands: " + ex.Message);
            }
        }

        private static string[] ToStringArgs(ConsoleSystem.Arg arg)
        {
            if (arg?.Args == null || arg.Args.Length == 0) return Array.Empty<string>();
            string[] result = new string[arg.Args.Length];
            for (int i = 0; i < arg.Args.Length; i++)
                result[i] = arg.Args[i].ToString();
            return result;
        }

        private static void AddCommand(string name, Action<ConsoleSystem.Arg> handler)
        {
            var cmd = new ConsoleSystem.Command
            {
                Name = name,
                FullName = "global." + name,
                Variable = false,
                ServerAdmin = true,
                ServerUser = true,
                AllowRunFromServer = true,
                Call = arg => handler(arg)
            };
            _commands.Add(cmd);
            try
            {
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                if (dict != null) dict["global." + name] = cmd;
                if (globalDict != null) globalDict[name] = cmd;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmNPC] AddCommand(" + name + "): " + ex.Message);
            }
        }

        public static void UnregisterCommands()
        {
            try
            {
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                foreach (var cmd in _commands)
                {
                    if (dict != null) dict.Remove(cmd.FullName);
                    if (globalDict != null) globalDict.Remove(cmd.Name);
                }
            }
            catch { }
            _commands.Clear();
        }

        public sealed class PluginRef
        {
            private readonly string _name;
            private object _plugin;
            private MethodInfo _call;

            private PluginRef(string name) { _name = name; }

            public static PluginRef Find(string name) => new PluginRef(name);

            public bool Exists
            {
                get
                {
                    Resolve();
                    return _plugin != null;
                }
            }

            public string Author
            {
                get
                {
                    Resolve();
                    if (_plugin == null) return null;
                    var p = _plugin.GetType().GetProperty("Author");
                    return p?.GetValue(_plugin) as string;
                }
            }

            public object Call(string method, params object[] args)
            {
                Resolve();
                if (_plugin == null) return null;
                try
                {
                    if (_call == null)
                        _call = _plugin.GetType().GetMethod("Call", new[] { typeof(string), typeof(object[]) });
                    return _call?.Invoke(_plugin, new object[] { method, args ?? Array.Empty<object>() });
                }
                catch
                {
                    return null;
                }
            }

            private void Resolve()
            {
                if (_plugin != null) return;
                try
                {
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type iface = asm.GetType("Oxide.Core.Interface");
                        if (iface == null) continue;
                        object oxide = iface.GetProperty("Oxide", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                        if (oxide == null) continue;
                        object rpm = oxide.GetType().GetProperty("RootPluginManager")?.GetValue(oxide);
                        if (rpm == null) continue;
                        MethodInfo get = rpm.GetType().GetMethod("GetPlugin", new[] { typeof(string) });
                        _plugin = get?.Invoke(rpm, new object[] { _name });
                        return;
                    }
                }
                catch { }
            }
        }

        public sealed class TimerHelper
        {
            public void Once(float delay, Action action)
            {
                if (action == null) return;
                BootstrapRunner.Start(OnceCo(delay, action));
            }

            private static IEnumerator OnceCo(float delay, Action action)
            {
                if (delay > 0f) yield return new WaitForSeconds(delay);
                try { action(); } catch (Exception ex) { Debug.LogWarning("[GrimmNPC] timer.Once: " + ex.Message); }
            }
        }
    }

    /// <summary>Oxide VersionNumber stand-in used by NpcSpawn config.</summary>
    [Serializable]
    public struct VersionNumber : IComparable<VersionNumber>
    {
        public int Major;
        public int Minor;
        public int Patch;

        public VersionNumber(int major, int minor, int patch)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public int CompareTo(VersionNumber other)
        {
            int c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            return Patch.CompareTo(other.Patch);
        }

        public static bool operator <(VersionNumber a, VersionNumber b) => a.CompareTo(b) < 0;
        public static bool operator >(VersionNumber a, VersionNumber b) => a.CompareTo(b) > 0;
        public static bool operator <=(VersionNumber a, VersionNumber b) => a.CompareTo(b) <= 0;
        public static bool operator >=(VersionNumber a, VersionNumber b) => a.CompareTo(b) >= 0;

        public override string ToString() => Major + "." + Minor + "." + Patch;
    }
}
