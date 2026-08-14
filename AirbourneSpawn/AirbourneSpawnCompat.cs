using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace AirbourneSpawnHarmony
{
    public class Hash<TKey, TValue> : Dictionary<TKey, TValue>
    {
        public new TValue this[TKey key]
        {
            get => TryGetValue(key, out var value) ? value : default;
            set => base[key] = value;
        }
    }

    public static class PlayerExtensions
    {
        public static bool HasPermission(this BasePlayer player, string perm)
        {
            if (player == null || string.IsNullOrEmpty(perm)) return false;
            return PermissionsBridge.UserHasPermission(player.UserIDString, perm);
        }

        public static ulong GetUserId(this BasePlayer player)
        {
            if (player == null) return 0UL;
            try { return player.userID.Get(); }
            catch { return (ulong)player.userID; }
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
                Debug.LogWarning("[AirbourneSpawn] Lang file load failed: " + ex.Message);
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

    public class AirbourneSpawnHost
    {
        public static AirbourneSpawnHost Instance { get; private set; }

        public string ServerRoot { get; private set; }
        public string ConfigDirectory { get; private set; }
        public string LangDirectory { get; private set; }
        public string ConfigPath { get; private set; }
        public string LangPath { get; private set; }
        public LangHelper Lang { get; } = new LangHelper();

        public static void Init(string serverRoot)
        {
            Instance = new AirbourneSpawnHost();
            Instance.ServerRoot = serverRoot ?? ".";
            Instance.ConfigDirectory = Path.Combine(Instance.ServerRoot, "HarmonyConfig");
            Instance.LangDirectory = Path.Combine(Instance.ServerRoot, "HarmonyLanguage");
            Instance.ConfigPath = Path.Combine(Instance.ConfigDirectory, "AirbourneSpawn.json");
            Instance.LangPath = Path.Combine(Instance.LangDirectory, "AirbourneSpawn.json");

            Directory.CreateDirectory(Instance.ConfigDirectory);
            Directory.CreateDirectory(Instance.LangDirectory);

            Debug.Log("[AirbourneSpawn] Config: " + Instance.ConfigPath);
            Debug.Log("[AirbourneSpawn] Lang:   " + Instance.LangPath);
        }

        public void ReloadLanguage()
        {
            if (Lang.LoadLanguageFile(LangPath))
                Debug.Log($"[AirbourneSpawn] OK: Loaded {Lang.FileMessageCount} lang entries from HarmonyLanguage/AirbourneSpawn.json");
            else
                Debug.Log("[AirbourneSpawn] HarmonyLanguage/AirbourneSpawn.json missing or empty — using embedded defaults");
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

        public Timer Repeat(float seconds, int times, Action callback)
        {
            if (callback == null) return new Timer();
            var t = new Timer();
            lock (_active) _active.Add(t);
            try { ServerMgr.Instance?.StartCoroutine(RunRepeat(seconds, times, t, callback)); }
            catch { lock (_active) _active.Remove(t); }
            return t;
        }

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
            try { cb(); } catch (Exception ex) { Debug.LogWarning("[AirbourneSpawn] Timer: " + ex.Message); }
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
                try { cb(); } catch (Exception ex) { Debug.LogWarning("[AirbourneSpawn] Timer: " + ex.Message); }
                count++;
            }
            t.Destroy();
            lock (_active) _active.Remove(t);
        }
    }

    public static class KitsBridge
    {
        public static bool IsLoaded => Resolve() != null;

        public static bool IsKit(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var t = Resolve();
            if (t == null) return false;
            try
            {
                var mi = t.GetMethod("IsKit", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string) }, null);
                if (mi != null && mi.Invoke(null, new object[] { name }) is bool b)
                    return b;
            }
            catch { }
            return false;
        }

        public static void GiveKit(BasePlayer player, string name)
        {
            if (player == null || string.IsNullOrEmpty(name)) return;
            var t = Resolve();
            if (t == null)
            {
                Debug.LogWarning("[AirbourneSpawn] Kits Harmony mod is not loaded — cannot give kit '" + name + "'");
                return;
            }
            try
            {
                var mi = t.GetMethod("GiveKit", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(BasePlayer), typeof(string) }, null);
                if (mi != null)
                    mi.Invoke(null, new object[] { player, name });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AirbourneSpawn] GiveKit: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        private static Type Resolve()
        {
            try
            {
                var fromDomain = AppDomain.CurrentDomain.GetData("Kits_ApiType") as Type;
                if (fromDomain != null) return fromDomain;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var t = asm.GetType("KitsHarmony.KitsHarmonyMod");
                        if (t != null) return t;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }
    }

    public static class CuiUtil
    {
        public static void AddUi(BasePlayer player, string json)
        {
            try
            {
                if (player == null || player.IsDestroyed || !player.IsConnected || player.net?.connection == null)
                    return;
                if (string.IsNullOrEmpty(json)) return;
                CommunityEntity.ServerInstance?.ClientRPC(RpcTarget.Player("AddUI", player.net.connection), json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AirbourneSpawn] AddUI: " + ex.Message);
            }
        }

        public static void DestroyUi(BasePlayer player, string name)
        {
            try
            {
                if (player == null || player.IsDestroyed || !player.IsConnected || player.net?.connection == null)
                    return;
                if (string.IsNullOrEmpty(name)) return;
                CommunityEntity.ServerInstance?.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), name);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AirbourneSpawn] DestroyUI: " + ex.Message);
            }
        }
    }
}
