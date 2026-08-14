using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace AnimalSpawn
{
    /// <summary>
    /// Oxide stand-ins so AnimalSpawn can run as a Harmony mod without Oxide.Core.
    /// Horse command/limit helpers are intentionally absent — Shop owns those.
    /// </summary>
    public static class OxideCompat
    {
        private static readonly string ServerRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public static readonly string ConfigPath = Path.Combine(ServerRoot, "HarmonyConfig", "AnimalSpawn.json");
        public static readonly string DataDirectory = Path.Combine(ServerRoot, "HarmonyData", "AnimalSpawn");
        public static readonly string NavMeshDirectory = Path.Combine(DataDirectory, "NavMesh");
        public static readonly string PresetDirectory = Path.Combine(DataDirectory, "Preset");

        public static readonly TimerHelper Timer = new TimerHelper();

        private static MethodInfo _callHook;

        public static void EnsureDataFolders()
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(ServerRoot, "HarmonyConfig"));
                Directory.CreateDirectory(DataDirectory);
                Directory.CreateDirectory(NavMeshDirectory);
                Directory.CreateDirectory(PresetDirectory);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AnimalSpawn] EnsureDataFolders: " + ex.Message);
            }
        }

        public static T ReadConfig<T>() where T : class, new()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    T cfg = JsonConvert.DeserializeObject<T>(File.ReadAllText(ConfigPath));
                    if (cfg != null) return cfg;
                }

                string oxideCfg = Path.Combine(ServerRoot, "oxide", "config", "AnimalSpawn.json");
                if (File.Exists(oxideCfg))
                {
                    T cfg = JsonConvert.DeserializeObject<T>(File.ReadAllText(oxideCfg));
                    if (cfg != null)
                    {
                        WriteConfig(cfg);
                        Debug.Log("[AnimalSpawn] Migrated config from oxide/config/AnimalSpawn.json");
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AnimalSpawn] ReadConfig failed: " + ex.Message);
            }
            return new T();
        }

        public static void WriteConfig(object config)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath) ?? ServerRoot);
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(config, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AnimalSpawn] WriteConfig failed: " + ex.Message);
            }
        }

        public static T ReadJson<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AnimalSpawn] ReadJson " + path + ": " + ex.Message);
                return null;
            }
        }

        public static IEnumerable<string> EnumerateJsonFiles(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                yield break;
            string[] files;
            try { files = Directory.GetFiles(directory, "*.json"); }
            catch { yield break; }
            for (int i = 0; i < files.Length; i++)
                yield return files[i];
        }

        public static void MigrateOxideDataIfNeeded()
        {
            try
            {
                string oxideData = Path.Combine(ServerRoot, "oxide", "data", "AnimalSpawn");
                if (!Directory.Exists(oxideData)) return;

                CopyJsonTree(oxideData, DataDirectory);
                string oxideNav = Path.Combine(oxideData, "NavMesh");
                if (Directory.Exists(oxideNav))
                    CopyJsonTree(oxideNav, NavMeshDirectory);
                string oxidePreset = Path.Combine(oxideData, "Preset");
                if (Directory.Exists(oxidePreset))
                    CopyJsonTree(oxidePreset, PresetDirectory);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AnimalSpawn] MigrateOxideDataIfNeeded: " + ex.Message);
            }
        }

        private static void CopyJsonTree(string fromDir, string toDir)
        {
            if (!Directory.Exists(fromDir) || string.IsNullOrEmpty(toDir)) return;
            Directory.CreateDirectory(toDir);
            foreach (string file in Directory.GetFiles(fromDir, "*.json"))
            {
                string dest = Path.Combine(toDir, Path.GetFileName(file));
                if (!File.Exists(dest))
                    File.Copy(file, dest, false);
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

        public static void RunWhenServerInitialized(Action action)
        {
            if (action == null) return;
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
            catch (Exception ex) { Debug.LogWarning("[AnimalSpawn] RunWhenServerInitialized: " + ex); }
        }

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
                var go = new GameObject("AnimalSpawn_Bootstrap");
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
                try { action(); } catch (Exception ex) { Debug.LogWarning("[AnimalSpawn] timer.Once: " + ex.Message); }
            }
        }
    }

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
