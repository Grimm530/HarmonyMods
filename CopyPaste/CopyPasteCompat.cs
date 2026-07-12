/*
 * Harmony shims so the ported CopyPaste 4.2.81 logic can run without Oxide/Carbon.
 * No Oxide assemblies are referenced or loaded.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CopyPasteHarmony
{
    public interface IPlayer
    {
        string Id { get; }
        object Object { get; }
        string Name { get; }
        bool IsAdmin { get; }
        bool IsServer { get; }
        bool IsConnected { get; }
        void Reply(string message);
        void Message(string msg);
        bool HasPermission(string perm);
    }

    public class RustConsolePlayer : IPlayer
    {
        public string Id => "0";
        public object Object => null;
        public string Name => "Server";
        public bool IsAdmin => true;
        public bool IsServer => true;
        public bool IsConnected => true;
        public void Reply(string message) => Debug.Log("[CopyPaste] " + message);
        public void Message(string msg) => Reply(msg);
        public bool HasPermission(string perm) => true;
    }

    public class BasePlayerWrapper : IPlayer
    {
        private readonly BasePlayer _player;
        public BasePlayerWrapper(BasePlayer player) => _player = player;
        public string Id => _player?.UserIDString ?? "0";
        public object Object => _player;
        public string Name => _player?.displayName ?? "";
        public bool IsAdmin => _player != null && _player.IsAdmin;
        public bool IsServer => false;
        public bool IsConnected => _player != null && _player.IsConnected;
        public void Reply(string message)
        {
            if (_player == null || !_player.IsConnected || _player.net?.connection == null) return;
            ConsoleNetwork.SendClientCommand(_player.net.connection, "chat.add", 0, 0, message ?? "");
        }
        public void Message(string msg) => Reply(msg);
        public bool HasPermission(string perm)
        {
            if (_player == null) return false;
            if (_player.IsAdmin) return true;
            return CopyPasteHost.Instance?.Permission?.UserHasPermission(Id, perm) == true;
        }
    }

    public static class PlayerExtensions
    {
        /// <summary>Oxide uses BasePlayer.IPlayer property; Harmony uses this extension.</summary>
        public static IPlayer ToIPlayer(this BasePlayer player) =>
            player == null ? null : new BasePlayerWrapper(player);

        /// <summary>Oxide Core extension used by CopyPaste for ownership checks.</summary>
        public static bool IsSteamId(this ulong id) => id > 76561197960265728UL;
    }

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
            if (Major != other.Major) return Major.CompareTo(other.Major);
            if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
            return Patch.CompareTo(other.Patch);
        }

        public static bool operator >=(VersionNumber a, VersionNumber b) => a.CompareTo(b) >= 0;
        public static bool operator <=(VersionNumber a, VersionNumber b) => a.CompareTo(b) <= 0;
        public static bool operator >(VersionNumber a, VersionNumber b) => a.CompareTo(b) > 0;
        public static bool operator <(VersionNumber a, VersionNumber b) => a.CompareTo(b) < 0;

        public override string ToString() => $"{Major}.{Minor}.{Patch}";
    }

    public class Timer
    {
        public Action Callback;
        public bool Destroyed { get; private set; }
        public void Destroy() => Destroyed = true;
    }

    public class HarmonyTimerRunner
    {
        private readonly List<Timer> _timers = new List<Timer>();

        public void DestroyAll()
        {
            List<Timer> copy;
            lock (_timers) { copy = new List<Timer>(_timers); _timers.Clear(); }
            foreach (var t in copy) t?.Destroy();
        }

        public Timer Once(float seconds, Action callback)
        {
            if (callback == null) return new Timer();
            var t = new Timer { Callback = callback };
            lock (_timers) _timers.Add(t);
            try
            {
                ServerMgr.Instance?.StartCoroutine(WaitAndRun(seconds, t, () =>
                {
                    if (!t.Destroyed)
                    {
                        try { callback(); }
                        catch (Exception ex) { Debug.LogWarning("[CopyPaste] Timer: " + ex.Message); }
                    }
                    t.Destroy();
                    lock (_timers) _timers.Remove(t);
                }));
            }
            catch { lock (_timers) _timers.Remove(t); }
            return t;
        }

        public Timer In(float seconds, Action callback) => Once(seconds, callback);

        private static IEnumerator WaitAndRun(float seconds, Timer timer, Action callback)
        {
            if (seconds > 0f)
                yield return new WaitForSeconds(seconds);
            else
                yield return null;
            try { callback?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[CopyPaste] Timer: " + ex.Message); }
        }
    }

    public class HarmonyPermissionHelper
    {
        private readonly HashSet<string> _granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public void RegisterPermission(string perm, object plugin) { }
        public bool UserHasPermission(string userId, string perm)
        {
            if (string.IsNullOrEmpty(userId)) return false;
            if (_granted.Contains(userId + ":" + perm)) return true;
            // Admins are handled on IPlayer.HasPermission; default deny for non-admins.
            return false;
        }
        public void GrantUserPermission(string userId, string perm) => _granted.Add(userId + ":" + perm);
    }

    public class LangHelper
    {
        private readonly Dictionary<string, Dictionary<string, string>> _byLang =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public void RegisterMessages(Dictionary<string, string> messages, object plugin, string language)
        {
            if (messages == null) return;
            language = string.IsNullOrEmpty(language) ? "en" : language;
            if (!_byLang.TryGetValue(language, out var dict))
                _byLang[language] = dict = new Dictionary<string, string>();
            foreach (var kv in messages)
                dict[kv.Key] = kv.Value;
        }

        public string GetMessage(string key, object plugin, string userId)
        {
            if (_byLang.TryGetValue("en", out var en) && en.TryGetValue(key, out var msg))
                return msg;
            foreach (var dict in _byLang.Values)
            {
                if (dict.TryGetValue(key, out msg))
                    return msg;
            }
            return key ?? "";
        }
    }

    public class PlayersHelper
    {
        public IPlayer FindPlayerById(string id)
        {
            if (string.IsNullOrEmpty(id) || !ulong.TryParse(id, out var uid)) return null;
            var p = BasePlayer.FindByID(uid) ?? BasePlayer.FindSleeping(uid);
            return p != null ? new BasePlayerWrapper(p) : null;
        }
    }

    /// <summary>Dynamic JSON data file compatible with Oxide DynamicConfigFile indexing.</summary>
    public class DynamicConfigFile
    {
        private JObject _data;
        private readonly string _path;
        public JsonSerializerSettings Settings { get; } = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Populate
        };

        public DynamicConfigFile(string path = null, JObject data = null)
        {
            _path = path;
            _data = data ?? new JObject();
        }

        public void Clear() => _data = new JObject();

        public object this[string key]
        {
            get => ConvertToken(_data?[key]);
            set => _data[key] = value == null ? JValue.CreateNull() : JToken.FromObject(value, JsonSerializer.Create(Settings));
        }

        public T ReadObject<T>() where T : class, new()
        {
            try
            {
                if (_data == null || !_data.HasValues)
                    return new T();
                return _data.ToObject<T>(JsonSerializer.Create(Settings)) ?? new T();
            }
            catch
            {
                return new T();
            }
        }

        public void WriteObject(object obj, bool sync = false)
        {
            if (obj == null) return;
            _data = JObject.FromObject(obj, JsonSerializer.Create(Settings));
            if (sync) Save();
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(_path)) return;
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_path, _data.ToString(Formatting.Indented));
        }

        internal JObject Raw => _data;

        public static object ConvertToken(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            switch (token.Type)
            {
                case JTokenType.Object:
                {
                    var dict = new Dictionary<string, object>();
                    foreach (var prop in ((JObject)token).Properties())
                        dict[prop.Name] = ConvertToken(prop.Value);
                    return dict;
                }
                case JTokenType.Array:
                {
                    var list = new List<object>();
                    foreach (var item in (JArray)token)
                        list.Add(ConvertToken(item));
                    return list;
                }
                case JTokenType.Integer:
                {
                    var lng = token.ToObject<long>();
                    if (lng >= int.MinValue && lng <= int.MaxValue)
                        return (int)lng;
                    return lng;
                }
                case JTokenType.Float:
                    return token.ToObject<double>();
                case JTokenType.Boolean:
                    return token.ToObject<bool>();
                case JTokenType.String:
                    return token.ToObject<string>();
                default:
                    return ((JValue)token).Value;
            }
        }
    }

    public class DataFileSystem
    {
        private readonly string _root;

        public DataFileSystem(string root)
        {
            _root = root;
            if (!Directory.Exists(_root))
                Directory.CreateDirectory(_root);
        }

        private string ResolvePath(string relativePath)
        {
            // Oxide paths look like "copypaste/mybase". Our data root is already HarmonyData/copypaste,
            // so strip a leading "copypaste/" segment.
            if (string.IsNullOrEmpty(relativePath)) return Path.Combine(_root, "data.json");
            relativePath = relativePath.Replace('\\', '/').Trim('/');
            if (relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                relativePath = relativePath.Substring(0, relativePath.Length - 5);
            if (relativePath.StartsWith("copypaste/", StringComparison.OrdinalIgnoreCase))
                relativePath = relativePath.Substring("copypaste/".Length);
            else if (relativePath.Equals("copypaste", StringComparison.OrdinalIgnoreCase))
                relativePath = "";
            var segments = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) return Path.Combine(_root, "data.json");
            var dir = _root;
            for (int i = 0; i < segments.Length - 1; i++)
                dir = Path.Combine(dir, segments[i]);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return Path.Combine(dir, segments[segments.Length - 1] + ".json");
        }

        private readonly Dictionary<string, DynamicConfigFile> _cache =
            new Dictionary<string, DynamicConfigFile>(StringComparer.OrdinalIgnoreCase);

        public DynamicConfigFile GetFile(string relativePath)
        {
            var path = ResolvePath(relativePath);
            if (_cache.TryGetValue(path, out var cached))
                return cached;
            JObject data = new JObject();
            if (File.Exists(path))
            {
                try { data = JObject.Parse(File.ReadAllText(path)); }
                catch { data = new JObject(); }
            }
            var file = new DynamicConfigFile(path, data);
            _cache[path] = file;
            return file;
        }

        public DynamicConfigFile GetDatafile(string relativePath) => GetFile(relativePath);

        public void SaveDatafile(string relativePath)
        {
            var path = ResolvePath(relativePath);
            if (_cache.TryGetValue(path, out var file))
                file.Save();
            else
                GetFile(relativePath).Save();
        }

        public bool ExistsDatafile(string relativePath)
        {
            var path = ResolvePath(relativePath);
            return File.Exists(path);
        }

        public string[] GetFiles(string relativePath)
        {
            relativePath = (relativePath ?? "").Replace('\\', '/').Trim('/');
            var dir = string.IsNullOrEmpty(relativePath) ? _root : Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            // Oxide stores under oxide/data/copypaste/ — our root IS HarmonyData/copypaste,
            // so "copypaste/" means the root itself.
            if (relativePath.Equals("copypaste", StringComparison.OrdinalIgnoreCase) ||
                relativePath.Equals("copypaste/", StringComparison.OrdinalIgnoreCase))
                dir = _root;
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            return Directory.GetFiles(dir, "*.json")
                .Select(f => "copypaste/" + Path.GetFileNameWithoutExtension(f))
                .ToArray();
        }
    }

    /// <summary>Harmony stand-in for plugin hooks / data IO. No Oxide assembly is loaded or referenced.</summary>
    public static class Interface
    {
        public static DataFileSystem DataFileSystem { get; set; }

        public static object CallHook(string name, params object[] args) => null;

        public static void NextTick(Action action)
        {
            try { ServerMgr.Instance?.StartCoroutine(NextTickCoroutine(action)); }
            catch { }
        }

        private static IEnumerator NextTickCoroutine(Action action)
        {
            yield return null;
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[CopyPaste] NextTick: " + ex.Message); }
        }
    }

    public class CopyPasteHost
    {
        public static CopyPasteHost Instance { get; private set; }
        public HarmonyPermissionHelper Permission { get; } = new HarmonyPermissionHelper();
        public HarmonyTimerRunner Timer { get; } = new HarmonyTimerRunner();
        public LangHelper Lang { get; } = new LangHelper();
        public PlayersHelper Players { get; } = new PlayersHelper();
        public DynamicConfigFile Config { get; private set; }
        public CopyPaste Plugin { get; set; }

        public static void Init(string serverRoot)
        {
            Instance = new CopyPasteHost();
            var dataDir = Path.Combine(serverRoot, "HarmonyData", "copypaste");
            var configDir = Path.Combine(serverRoot, "HarmonyConfig");
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
            if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);
            Interface.DataFileSystem = new DataFileSystem(dataDir);
            var configPath = Path.Combine(configDir, "CopyPaste.json");
            JObject data = null;
            if (File.Exists(configPath))
            {
                try { data = JObject.Parse(File.ReadAllText(configPath)); }
                catch { data = null; }
            }
            Instance.Config = new DynamicConfigFile(configPath, data ?? new JObject());
        }

        public static void Shutdown()
        {
            Instance?.Timer?.DestroyAll();
            Instance = null;
        }

        public void Puts(string message) => Debug.Log("[CopyPaste] " + message);
        public void Puts(string format, params object[] args) =>
            Debug.Log("[CopyPaste] " + (args == null || args.Length == 0 ? format : string.Format(format, args)));
        public void PrintWarning(string message) => Debug.LogWarning("[CopyPaste] " + message);
        public void PrintError(string message) => Debug.LogError("[CopyPaste] " + message);
    }

    /// <summary>Base class replacing Oxide CovalencePlugin for CopyPaste.</summary>
    public abstract class CopyPasteBase
    {
        public VersionNumber Version { get; protected set; } = new VersionNumber(4, 2, 81);

        protected CopyPasteHost Host => CopyPasteHost.Instance;
        protected HarmonyPermissionHelper permission => Host?.Permission;
        protected HarmonyTimerRunner timer => Host?.Timer;
        protected LangHelper lang => Host?.Lang;
        protected PlayersHelper players => Host?.Players;
        protected DynamicConfigFile Config => Host?.Config;

        protected void Puts(string message) => Host?.Puts(message);
        protected void Puts(string format, params object[] args) => Host?.Puts(format, args);
        protected void PrintWarning(string message) => Host?.PrintWarning(message);
        protected void PrintError(string message) => Host?.PrintError(message);

        protected void NextTick(Action action)
        {
            try { ServerMgr.Instance?.StartCoroutine(NextTickCoroutine(action)); }
            catch { }
        }

        private static IEnumerator NextTickCoroutine(Action action)
        {
            yield return null;
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[CopyPaste] NextTick: " + ex.Message); }
        }

        protected virtual void LoadDefaultConfig() { }
        public abstract void HarmonyInit();
        public abstract void HarmonyServerInitialized();
        public abstract void HarmonyUnload();
    }
}
