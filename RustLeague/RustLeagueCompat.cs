using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace RustLeagueHarmony
{
    public static class PlayerExtensions
    {
        public static bool HasPermission(this BasePlayer player, string perm)
        {
            if (player == null || string.IsNullOrEmpty(perm)) return false;
            if (player.IsAdmin || player.IsDeveloper) return true;
            return PermissionsBridge.UserHasPermission(player.UserIDString, perm);
        }

        public static ulong GetUserId(this BasePlayer player)
        {
            if (player == null) return 0UL;
            try { return player.userID.Get(); }
            catch { return (ulong)player.userID; }
        }

        public static T GetOrAdd<T>(this BaseEntity entity) where T : Component
        {
            if (entity == null) return null;
            var existing = entity.GetComponent<T>();
            return existing != null ? existing : entity.gameObject.AddComponent<T>();
        }
    }

    public class LangHelper
    {
        private readonly Dictionary<string, string> _embedded =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _file =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int FileMessageCount => _file.Count;

        public void RegisterMessages(Dictionary<string, string> messages)
        {
            if (messages == null) return;
            foreach (var kv in messages)
                _embedded[kv.Key] = kv.Value;
        }

        public bool LoadLanguageFile(string path)
        {
            _file = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            try
            {
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (loaded == null) return false;
                foreach (var kv in loaded)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                        _file[kv.Key] = kv.Value;
                }
                return _file.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RustLeague] Lang file load failed: " + ex.Message);
                return false;
            }
        }

        public string GetMessage(string key, string userId = null)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (_file.TryGetValue(key, out var fromFile) && !string.IsNullOrEmpty(fromFile))
                return fromFile;
            if (_embedded.TryGetValue(key, out var msg))
                return msg;
            return key;
        }
    }

    public class RustLeagueHost
    {
        public static RustLeagueHost Instance { get; private set; }

        public string ServerRoot { get; private set; }
        public string ConfigDirectory { get; private set; }
        public string LangDirectory { get; private set; }
        public string DataDirectory { get; private set; }
        public string ConfigPath { get; private set; }
        public string LangPath { get; private set; }
        public LangHelper Lang { get; } = new LangHelper();

        public static void Init(string serverRoot)
        {
            Instance = new RustLeagueHost();
            Instance.ServerRoot = serverRoot ?? ".";
            Instance.ConfigDirectory = Path.Combine(Instance.ServerRoot, "HarmonyConfig");
            Instance.LangDirectory = Path.Combine(Instance.ServerRoot, "HarmonyLanguage");
            Instance.DataDirectory = Path.Combine(Instance.ServerRoot, "HarmonyData", "RustLeague");
            Instance.ConfigPath = Path.Combine(Instance.ConfigDirectory, "RustLeague.json");
            Instance.LangPath = Path.Combine(Instance.LangDirectory, "RustLeague.json");

            Directory.CreateDirectory(Instance.ConfigDirectory);
            Directory.CreateDirectory(Instance.LangDirectory);
            Directory.CreateDirectory(Instance.DataDirectory);

            TryMigrateOxideConfig(Instance);
            Debug.Log("[RustLeague] Config: " + Instance.ConfigPath);
            Debug.Log("[RustLeague] Lang:   " + Instance.LangPath);
        }

        private static void TryMigrateOxideConfig(RustLeagueHost host)
        {
            if (File.Exists(host.ConfigPath)) return;
            string oxide = Path.Combine(host.ServerRoot, "oxide", "config", "RustLeague.json");
            if (!File.Exists(oxide)) return;
            try
            {
                File.Copy(oxide, host.ConfigPath, false);
                Debug.Log("[RustLeague] Migrated oxide/config/RustLeague.json -> HarmonyConfig/RustLeague.json");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RustLeague] Config migrate failed: " + ex.Message);
            }
        }

        public void ReloadLanguage()
        {
            if (Lang.LoadLanguageFile(LangPath))
                Debug.Log($"[RustLeague] OK: Loaded {Lang.FileMessageCount} lang entries from HarmonyLanguage/RustLeague.json");
            else
                Debug.Log("[RustLeague] HarmonyLanguage/RustLeague.json missing or empty — using embedded defaults");
        }

        public static void Shutdown() => Instance = null;
    }

    public class Timer
    {
        public bool Destroyed { get; private set; }
        public void Destroy() => Destroyed = true;
    }

    public class TimerLib
    {
        private readonly List<Timer> _active = new List<Timer>();

        public Timer Once(float seconds, Action callback) => In(seconds, callback);

        public Timer In(float seconds, Action callback)
        {
            if (callback == null) return new Timer();
            var t = new Timer();
            lock (_active) _active.Add(t);
            try
            {
                ServerMgr.Instance?.StartCoroutine(RunOnce(seconds, t, callback));
            }
            catch { lock (_active) _active.Remove(t); }
            return t;
        }

        public Timer Every(float seconds, Action callback) => Repeat(seconds, -1, callback);

        public Timer Repeat(float seconds, int times, Action callback)
        {
            if (callback == null) return new Timer();
            var t = new Timer();
            lock (_active) _active.Add(t);
            try { ServerMgr.Instance?.StartCoroutine(RunRepeat(seconds, times, t, callback)); }
            catch { lock (_active) _active.Remove(t); }
            return t;
        }

        public void NextTick(Action callback) => Once(0f, callback);

        public void DestroyAll()
        {
            List<Timer> copy;
            lock (_active) { copy = new List<Timer>(_active); _active.Clear(); }
            foreach (var t in copy) t?.Destroy();
        }

        private IEnumerator RunOnce(float secs, Timer t, Action cb)
        {
            if (secs > 0f) yield return new WaitForSeconds(secs); else yield return null;
            if (t.Destroyed) yield break;
            try { cb(); } catch (Exception ex) { Debug.LogWarning("[RustLeague] Timer: " + ex.Message); }
            t.Destroy();
            lock (_active) _active.Remove(t);
        }

        private IEnumerator RunRepeat(float secs, int times, Timer t, Action cb)
        {
            int count = 0;
            while (!t.Destroyed && (times < 0 || count < times))
            {
                if (secs > 0f) yield return new WaitForSeconds(secs); else yield return null;
                if (t.Destroyed) break;
                try { cb(); } catch (Exception ex) { Debug.LogWarning("[RustLeague] Timer: " + ex.Message); }
                count++;
            }
            t.Destroy();
            lock (_active) _active.Remove(t);
        }
    }
}
