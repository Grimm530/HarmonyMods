/*
 * Harmony shims so the ported Remover Tool 4.3.431 can run without Oxide/Carbon.
 * No Oxide assemblies are referenced or loaded.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RemoverToolHarmony
{
    #region Attributes (stubs — commands registered manually)

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InfoAttribute : Attribute
    {
        public InfoAttribute(string title, string author, string version) { }
        public int ResourceId { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DescriptionAttribute : Attribute
    {
        public DescriptionAttribute(string description) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class PluginReferenceAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ChatCommandAttribute : Attribute
    {
        public ChatCommandAttribute(string command) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ConsoleCommandAttribute : Attribute
    {
        public string Command { get; }
        public ConsoleCommandAttribute(string command) { Command = command; }
    }

    #endregion

    #region Plugin stub

    public class Plugin
    {
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string Version { get; set; } = "0.0.0";
        public bool IsLoaded { get; set; } = true;
        public virtual object Call(string method, params object[] args) => null;
    }

    #endregion

    #region Hash (Oxide Hash<K,V> replacement)

    public class Hash<TKey, TValue> : Dictionary<TKey, TValue>
    {
        public Hash() { }
        public Hash(IDictionary<TKey, TValue> dictionary) : base(dictionary) { }

        public new TValue this[TKey key]
        {
            get
            {
                TValue value;
                if (TryGetValue(key, out value))
                    return value;
                return default(TValue);
            }
            set => base[key] = value;
        }
    }

    #endregion

    #region VersionNumber

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
        public static bool operator ==(VersionNumber a, VersionNumber b) => a.CompareTo(b) == 0;
        public static bool operator !=(VersionNumber a, VersionNumber b) => a.CompareTo(b) != 0;

        public override bool Equals(object obj) => obj is VersionNumber other && this == other;
        public override int GetHashCode() => Major * 397 ^ Minor * 31 ^ Patch;
        public override string ToString() => $"{Major}.{Minor}.{Patch}";
    }

    #endregion

    #region Player extensions

    public static class PlayerExtensions
    {
        public static bool IsSteamId(this ulong id) => id > 76561197960265728UL;
        public static bool IsSteamId(this string id) =>
            !string.IsNullOrEmpty(id) && ulong.TryParse(id, out var uid) && uid.IsSteamId();
        public static bool IsSteamId(this EncryptedValue<ulong> id)
        {
            try { return ((ulong)id) > 76561197960265728UL; }
            catch { return false; }
        }

        // Oxide UnityEngine extension replacement.
        public static T GetOrAddComponent<T>(this UnityEngine.Component component) where T : UnityEngine.Component
        {
            if (component == null) return null;
            return component.gameObject.GetComponent<T>() ?? component.gameObject.AddComponent<T>();
        }

        public static T GetOrAddComponent<T>(this UnityEngine.GameObject go) where T : UnityEngine.Component
        {
            if (go == null) return null;
            return go.GetComponent<T>() ?? go.AddComponent<T>();
        }
    }

    #endregion

    #region Timer

    public class Timer
    {
        public Action Callback;
        public bool Destroyed { get; private set; }
        public void Destroy() => Destroyed = true;
    }

    public class TimerHelper
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
                        catch (Exception ex) { Debug.LogWarning("[RemoverTool] Timer: " + ex.Message); }
                    }
                    t.Destroy();
                    lock (_timers) _timers.Remove(t);
                }));
            }
            catch { lock (_timers) _timers.Remove(t); }
            return t;
        }

        public Timer In(float seconds, Action callback) => Once(seconds, callback);

        public Timer Repeat(float seconds, int times, Action callback)
        {
            if (callback == null) return new Timer();
            var t = new Timer { Callback = callback };
            lock (_timers) _timers.Add(t);
            try
            {
                ServerMgr.Instance?.StartCoroutine(RepeatRun(seconds, times, t, callback));
            }
            catch { lock (_timers) _timers.Remove(t); }
            return t;
        }

        private static IEnumerator WaitAndRun(float seconds, Timer timer, Action callback)
        {
            if (seconds > 0f)
                yield return new WaitForSeconds(seconds);
            else
                yield return null;
            try { callback?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[RemoverTool] Timer: " + ex.Message); }
        }

        private IEnumerator RepeatRun(float seconds, int times, Timer timer, Action callback)
        {
            int count = 0;
            while (!timer.Destroyed && (times < 0 || count < times))
            {
                if (seconds > 0f)
                    yield return new WaitForSeconds(seconds);
                else
                    yield return null;
                if (timer.Destroyed) break;
                try { callback(); }
                catch (Exception ex) { Debug.LogWarning("[RemoverTool] Timer: " + ex.Message); }
                count++;
            }
            timer.Destroy();
            lock (_timers) _timers.Remove(timer);
        }
    }

    #endregion

    #region Permission (0Permissions bridge — see Framework §10a)

    public class HarmonyPermissionHelper
    {
        private readonly HashSet<string> _registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Action _readyCallback;

        private static Type _permType;
        private static MethodInfo _userHas;
        private static MethodInfo _register;
        private static MethodInfo _exists;
        private static MethodInfo _grantUser;
        private static MethodInfo _registerReady;
        private static int _boundGen = -1;
        private static bool _resolveAttempted;
        private static bool _loggedLink;

        private static int ReadGeneration()
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData("Permissions_Generation") is int g)
                    return g;
            }
            catch { }
            return 0;
        }

        private static object ReadLiveInstance(Type type)
        {
            if (type == null) return null;
            try
            {
                return type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            }
            catch { return null; }
        }

        private static Type ResolveLivePermType()
        {
            var fromDomain = AppDomain.CurrentDomain.GetData("Permissions_ApiType") as Type;
            if (fromDomain != null && ReadLiveInstance(fromDomain) != null)
                return fromDomain;

            Type fallback = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType("PermissionsHarmony.PermissionsMod");
                    if (t == null) continue;
                    if (ReadLiveInstance(t) != null)
                        return t;
                    fallback ??= t;
                }
                catch { }
            }
            return fromDomain ?? fallback;
        }

        private static void EnsureBound()
        {
            int gen = ReadGeneration();
            object live = ReadLiveInstance(_permType);
            if (_permType != null && _boundGen == gen && live != null) return;

            try
            {
                _permType = null;
                _userHas = _register = _exists = _grantUser = _registerReady = null;
                _permType = ResolveLivePermType();
                live = ReadLiveInstance(_permType);
                if (_permType == null || live == null)
                {
                    if (!_resolveAttempted)
                    {
                        _resolveAttempted = true;
                        Debug.LogWarning("[RemoverTool] Permissions mod not loaded — permission checks will fail until 0Permissions.dll is loaded.");
                    }
                    return;
                }

                _resolveAttempted = false;
                BindingFlags sf = BindingFlags.Public | BindingFlags.Static;
                _userHas = _permType.GetMethod("UserHasPermission", sf, null, new[] { typeof(string), typeof(string) }, null);
                _register = _permType.GetMethod("RegisterPermission", sf, null, new[] { typeof(string) }, null);
                _exists = _permType.GetMethod("PermissionExists", sf, null, new[] { typeof(string) }, null);
                _grantUser = _permType.GetMethod("GrantUserPermission", sf, null, new[] { typeof(string), typeof(string) }, null);
                _registerReady = _permType.GetMethod("RegisterReadyCallback", sf, null, new[] { typeof(Action) }, null);
                _boundGen = gen;

                if (!_loggedLink)
                {
                    _loggedLink = true;
                    Debug.Log("[RemoverTool] OK: Linked to Permissions Harmony mod.");
                }
                else
                    Debug.Log($"[RemoverTool] OK: Re-linked to Permissions Harmony mod (gen={gen}).");
            }
            catch (Exception ex)
            {
                _permType = null;
                Debug.LogWarning("[RemoverTool] Permissions bind failed: " + ex.Message);
            }
        }

        private void EnsureReadyCallback()
        {
            if (_readyCallback != null) return;
            _readyCallback = ReplayRegistered;
            EnsureBound();
            try
            {
                if (_registerReady != null)
                {
                    _registerReady.Invoke(null, new object[] { _readyCallback });
                    return;
                }
            }
            catch { }

            try
            {
                var list = AppDomain.CurrentDomain.GetData("Permissions_ReadyCallbacks") as System.Collections.IList;
                if (list == null)
                {
                    list = new List<Action>();
                    AppDomain.CurrentDomain.SetData("Permissions_ReadyCallbacks", list);
                }
                lock (list)
                {
                    if (!list.Contains(_readyCallback))
                        list.Add(_readyCallback);
                }
            }
            catch { }
        }

        private void ReplayRegistered()
        {
            EnsureBound();
            foreach (var perm in _registered)
            {
                try { _register?.Invoke(null, new object[] { perm }); } catch { }
            }
        }

        public void RegisterPermission(string perm, object plugin = null)
        {
            if (string.IsNullOrEmpty(perm)) return;
            _registered.Add(perm);
            EnsureBound();
            EnsureReadyCallback();
            try { _register?.Invoke(null, new object[] { perm }); } catch { }
        }

        public bool PermissionExists(string perm, object plugin = null)
        {
            if (string.IsNullOrEmpty(perm)) return false;
            if (_registered.Contains(perm)) return true;
            EnsureBound();
            try
            {
                if (_exists != null && _exists.Invoke(null, new object[] { perm }) is bool b)
                    return b;
            }
            catch { }
            return false;
        }

        public bool UserHasPermission(string userId, string perm)
        {
            if (string.IsNullOrEmpty(userId)) return false;
            if (string.IsNullOrEmpty(perm)) return true;
            if (_granted.Contains(userId + ":" + perm)) return true;

            EnsureBound();
            try
            {
                if (_userHas != null && _userHas.Invoke(null, new object[] { userId, perm }) is bool ok)
                    return ok;
            }
            catch { }

            return false;
        }

        public void GrantUserPermission(string userId, string perm)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(perm)) return;
            _granted.Add(userId + ":" + perm);
            EnsureBound();
            try { _grantUser?.Invoke(null, new object[] { userId, perm }); } catch { }
        }
    }

    #endregion

    #region Lang

    public class LangHelper
    {
        private readonly Dictionary<string, Dictionary<string, string>> _byLang =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, string> _override;

        public void SetOverride(Dictionary<string, string> messages)
        {
            _override = messages;
        }

        public void RegisterMessages(Dictionary<string, string> messages, object plugin, string language = "en")
        {
            if (messages == null) return;
            language = string.IsNullOrEmpty(language) ? "en" : language;
            if (!_byLang.TryGetValue(language, out var dict))
                _byLang[language] = dict = new Dictionary<string, string>();
            foreach (var kv in messages)
                dict[kv.Key] = kv.Value;

            // Merge optional HarmonyLanguage/RemoverTool.json override into the default (en) table.
            if (_override != null && language.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var kv in _override)
                    dict[kv.Key] = kv.Value;
            }
        }

        public string GetMessage(string key, object plugin, string userId)
        {
            var lang = GetLanguage(userId);
            if (_byLang.TryGetValue(lang, out var dict) && dict.TryGetValue(key, out var msg))
                return msg;
            if (_byLang.TryGetValue("en", out var en) && en.TryGetValue(key, out msg))
                return msg;
            foreach (var d in _byLang.Values)
            {
                if (d.TryGetValue(key, out msg))
                    return msg;
            }
            return key ?? "";
        }

        public string GetLanguage(string userId) => "en";
    }

    #endregion

    #region Plugins / Players / Covalence helpers

    public class PluginsHelper
    {
        public Plugin Find(string name) => PluginBridges.Resolve(name);
    }

    public class PlayersHelper
    {
        public IEnumerable<BasePlayer> Connected
        {
            get
            {
                var list = new List<BasePlayer>();
                try
                {
                    foreach (var p in BasePlayer.activePlayerList)
                        if (p != null && p.IsConnected) list.Add(p);
                }
                catch { }
                return list;
            }
        }
    }

    public class CovalenceHelper
    {
        public PlayersHelper Players => RemoverToolHost.Instance?.Players ?? new PlayersHelper();
    }

    /// <summary>Oxide Oxide.Game.Rust.Libraries.Player replacement.</summary>
    public class RustPlayerLibrary
    {
        public void Message(BasePlayer player, string message, string prefix = null, ulong userId = 0UL, params object[] args)
        {
            if (player == null || string.IsNullOrEmpty(message)) return;
            try
            {
                string text = args != null && args.Length > 0 ? string.Format(message, args) : message;
                if (!string.IsNullOrEmpty(prefix)) text = prefix + text;
                player.ChatMessage(text);
            }
            catch (Exception ex) { Debug.LogWarning("[RemoverTool] Player.Message: " + ex.Message); }
        }

        public void Reply(BasePlayer player, string message) => Message(player, message);
    }

    /// <summary>Oxide.Game.Rust.RustCore.FindPlayer replacement.</summary>
    public static class RustCore
    {
        public static BasePlayer FindPlayer(string nameOrIdOrIp)
        {
            if (string.IsNullOrWhiteSpace(nameOrIdOrIp)) return null;
            nameOrIdOrIp = nameOrIdOrIp.Trim();

            if (ulong.TryParse(nameOrIdOrIp, out var uid) && uid.IsSteamId())
            {
                var byId = BasePlayer.FindByID(uid) ?? BasePlayer.FindSleeping(uid);
                if (byId != null) return byId;
            }

            BasePlayer match = null;
            try
            {
                foreach (var p in BasePlayer.activePlayerList)
                {
                    if (p == null) continue;
                    if (p.UserIDString == nameOrIdOrIp) return p;
                    if (!string.IsNullOrEmpty(p.displayName) &&
                        p.displayName.IndexOf(nameOrIdOrIp, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (match != null) return null; // ambiguous
                        match = p;
                    }
                }
            }
            catch { }
            return match;
        }
    }

    #endregion

    #region Config / Data files

    public class DynamicConfigFile : IEnumerable<KeyValuePair<string, object>>
    {
        private JToken _data;
        private readonly string _path;

        public string Filename => _path ?? "";

        public JsonSerializerSettings Settings { get; } = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Populate
        };

        public DynamicConfigFile(string path = null, JToken data = null)
        {
            _path = path;
            _data = data ?? new JObject();
        }

        public bool Exists() => !string.IsNullOrEmpty(_path) && File.Exists(_path);

        public void Clear() => _data = new JObject();

        private JObject AsObject()
        {
            if (_data is JObject obj) return obj;
            _data = new JObject();
            return (JObject)_data;
        }

        public object this[string key]
        {
            get => ConvertToken(AsObject()?[key]);
            set => AsObject()[key] = value == null ? JValue.CreateNull() : JToken.FromObject(value, JsonSerializer.Create(Settings));
        }

        public object Get(params string[] keys)
        {
            if (keys == null || keys.Length == 0) return null;
            JToken token = _data;
            foreach (var key in keys)
            {
                if (token == null) return null;
                token = token[key];
            }
            return ConvertToken(token);
        }

        /// <summary>Oxide Config.Set(path..., value) — last arg is the value, preceding args are keys.</summary>
        public void Set(params object[] pathAndTrailingValue)
        {
            if (pathAndTrailingValue == null || pathAndTrailingValue.Length < 2) return;
            var value = pathAndTrailingValue[pathAndTrailingValue.Length - 1];
            JObject cur = AsObject();
            for (int i = 0; i < pathAndTrailingValue.Length - 2; i++)
            {
                string key = pathAndTrailingValue[i]?.ToString();
                if (key == null) return;
                if (!(cur[key] is JObject next))
                {
                    next = new JObject();
                    cur[key] = next;
                }
                cur = next;
            }
            string lastKey = pathAndTrailingValue[pathAndTrailingValue.Length - 2]?.ToString();
            if (lastKey == null) return;
            cur[lastKey] = value == null ? JValue.CreateNull() : JToken.FromObject(value, JsonSerializer.Create(Settings));
        }

        /// <summary>Oxide Config.ConvertValue&lt;T&gt; — coerce a raw config value into T.</summary>
        public T ConvertValue<T>(object value)
        {
            if (value == null) return default(T);
            if (value is T typed) return typed;
            JToken token = value as JToken ?? JToken.FromObject(value, JsonSerializer.Create(Settings));
            return token.ToObject<T>(JsonSerializer.Create(Settings));
        }

        public Dictionary<string, object> ToDictionary()
        {
            var result = new Dictionary<string, object>();
            if (!(_data is JObject jo)) return result;
            foreach (var prop in jo.Properties())
                result[prop.Name] = ConvertToken(prop.Value);
            return result;
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            foreach (var kv in ToDictionary())
                yield return kv;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public T ReadObject<T>() where T : class, new()
        {
            try
            {
                if (_data == null || _data.Type == JTokenType.Null)
                    return new T();
                return _data.ToObject<T>(JsonSerializer.Create(Settings)) ?? new T();
            }
            catch
            {
                return new T();
            }
        }

        public void WriteObject(object obj, bool sync = true)
        {
            if (obj == null) return;
            _data = JToken.FromObject(obj, JsonSerializer.Create(Settings));
            if (sync) Save();
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(_path)) return;
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_path, (_data ?? new JObject()).ToString(Formatting.Indented));
        }

        public void Load(string path = null)
        {
            var p = path ?? _path;
            if (string.IsNullOrEmpty(p) || !File.Exists(p))
            {
                _data = new JObject();
                return;
            }
            try { _data = JToken.Parse(File.ReadAllText(p)); }
            catch { _data = new JObject(); }
        }

        internal JToken Raw => _data;

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
        private readonly Dictionary<string, DynamicConfigFile> _cache =
            new Dictionary<string, DynamicConfigFile>(StringComparer.OrdinalIgnoreCase);

        public DataFileSystem(string root)
        {
            _root = root;
            if (!Directory.Exists(_root))
                Directory.CreateDirectory(_root);
        }

        private string ResolvePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return Path.Combine(_root, "data.json");
            relativePath = relativePath.Replace('\\', '/').Trim('/');
            if (relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                relativePath = relativePath.Substring(0, relativePath.Length - 5);
            var segments = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) return Path.Combine(_root, "data.json");
            var dir = _root;
            for (int i = 0; i < segments.Length - 1; i++)
                dir = Path.Combine(dir, segments[i]);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return Path.Combine(dir, segments[segments.Length - 1] + ".json");
        }

        public DynamicConfigFile GetFile(string relativePath)
        {
            var path = ResolvePath(relativePath);
            if (_cache.TryGetValue(path, out var cached))
                return cached;
            JToken data = new JObject();
            if (File.Exists(path))
            {
                try { data = JToken.Parse(File.ReadAllText(path)); }
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

        public bool ExistsDatafile(string relativePath) => File.Exists(ResolvePath(relativePath));

        public T ReadObject<T>(string relativePath) where T : class, new()
        {
            var path = ResolvePath(relativePath);
            if (!File.Exists(path))
                return new T();
            _cache.Remove(path);
            return GetFile(relativePath).ReadObject<T>();
        }

        public void WriteObject(string relativePath, object obj)
        {
            var path = ResolvePath(relativePath);
            _cache.Remove(path);
            var file = GetFile(relativePath);
            file.WriteObject(obj, true);
        }

        public void DeleteDataFile(string relativePath)
        {
            var path = ResolvePath(relativePath);
            _cache.Remove(path);
            if (File.Exists(path))
            {
                try { File.Delete(path); }
                catch { }
            }
        }
    }

    #endregion

    #region Interface / Oxide stub

    public class OxideStub
    {
        public DataFileSystem DataFileSystem => Interface.DataFileSystem;
        public string DataDirectory => RemoverToolHost.Instance?.DataDirectory ?? "";
        public void LogError(string message) => Debug.LogError("[RemoverTool] " + message);
        public object CallHook(string name, params object[] args) => null;
        public void NextTick(Action action) => Interface.NextTick(action);
    }

    public static class Interface
    {
        public static DataFileSystem DataFileSystem { get; set; }
        public static OxideStub Oxide { get; } = new OxideStub();

        public static object CallHook(string name, params object[] args) => null;
        public static object Call(string name, params object[] args) => null;

        public static void NextTick(Action action)
        {
            try { ServerMgr.Instance?.StartCoroutine(NextTickCoroutine(action)); }
            catch { }
        }

        private static IEnumerator NextTickCoroutine(Action action)
        {
            yield return null;
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[RemoverTool] NextTick: " + ex.Message); }
        }
    }

    #endregion

    #region WebRequest

    public enum RequestMethod
    {
        GET,
        POST,
        PUT,
        DELETE,
        PATCH
    }

    public class WebRequestHelper
    {
        private static readonly HttpClient Http = new HttpClient();

        public void Enqueue(string url, string body, Action<int, string> callback, object owner,
            RequestMethod method = RequestMethod.GET, Dictionary<string, string> headers = null, float timeout = 30f)
        {
            if (string.IsNullOrEmpty(url)) return;
            Task.Run(async () =>
            {
                int code = 0;
                string response = "";
                try
                {
                    using (var req = new HttpRequestMessage(
                               method == RequestMethod.POST ? HttpMethod.Post :
                               method == RequestMethod.PUT ? HttpMethod.Put :
                               method == RequestMethod.DELETE ? HttpMethod.Delete :
                               HttpMethod.Get, url))
                    {
                        if (!string.IsNullOrEmpty(body))
                            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                        using (var resp = await Http.SendAsync(req).ConfigureAwait(false))
                        {
                            code = (int)resp.StatusCode;
                            response = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    code = 0;
                    response = ex.Message;
                }
                try
                {
                    Interface.NextTick(() =>
                    {
                        try { callback?.Invoke(code, response); }
                        catch (Exception ex) { Debug.LogWarning("[RemoverTool] webrequest callback: " + ex.Message); }
                    });
                }
                catch { }
            });
        }
    }

    #endregion

    #region CmdHelper

    public class CommandRegistration
    {
        public string Command;
        public object Plugin;
        public string MethodName;
    }

    public class CmdHelper
    {
        private readonly List<CommandRegistration> _chatCommands = new List<CommandRegistration>();

        public IReadOnlyList<CommandRegistration> ChatCommands => _chatCommands;

        public void AddChatCommand(string command, object plugin, string methodName)
        {
            if (string.IsNullOrEmpty(command) || string.IsNullOrEmpty(methodName)) return;
            _chatCommands.RemoveAll(c => string.Equals(c.Command, command, StringComparison.OrdinalIgnoreCase));
            _chatCommands.Add(new CommandRegistration { Command = command, Plugin = plugin, MethodName = methodName });
        }

        public void Clear() => _chatCommands.Clear();
    }

    #endregion

    #region Host

    public class RemoverToolHost
    {
        public static RemoverToolHost Instance { get; private set; }
        public HarmonyPermissionHelper Permission { get; } = new HarmonyPermissionHelper();
        public TimerHelper Timer { get; } = new TimerHelper();
        public LangHelper Lang { get; } = new LangHelper();
        public PlayersHelper Players { get; } = new PlayersHelper();
        public PluginsHelper Plugins { get; } = new PluginsHelper();
        public CovalenceHelper Covalence { get; } = new CovalenceHelper();
        public WebRequestHelper Webrequest { get; } = new WebRequestHelper();
        public RustPlayerLibrary PlayerLib { get; } = new RustPlayerLibrary();
        public CmdHelper Cmd { get; } = new CmdHelper();
        public DynamicConfigFile Config { get; private set; }
        public RemoverTool Plugin { get; set; }
        public string ServerRoot { get; private set; }
        public string DataDirectory { get; private set; }
        public string ModDataDirectory { get; private set; }

        public IReadOnlyList<CommandRegistration> ChatCommands => Cmd.ChatCommands;

        public static void Init(string serverRoot)
        {
            Instance = new RemoverToolHost();
            Instance.ServerRoot = serverRoot;
            var dataDir = Path.Combine(serverRoot, "HarmonyData");
            var configDir = Path.Combine(serverRoot, "HarmonyConfig");
            var langDir = Path.Combine(serverRoot, "HarmonyLanguage");
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
            if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);
            if (!Directory.Exists(langDir)) Directory.CreateDirectory(langDir);
            var modData = Path.Combine(dataDir, "RemoverTool");
            Directory.CreateDirectory(modData);
            Directory.CreateDirectory(Path.Combine(modData, "logs"));
            Instance.DataDirectory = dataDir;
            Instance.ModDataDirectory = modData;
            Interface.DataFileSystem = new DataFileSystem(dataDir);

            var configPath = Path.Combine(configDir, "RemoverTool.json");
            JToken data = null;
            if (File.Exists(configPath))
            {
                try { data = JToken.Parse(File.ReadAllText(configPath)); }
                catch { data = null; }
            }
            Instance.Config = new DynamicConfigFile(configPath, data ?? new JObject());

            // Optional language override: HarmonyLanguage/RemoverTool.json (merged into RegisterMessages).
            var langPath = Path.Combine(langDir, "RemoverTool.json");
            if (File.Exists(langPath))
            {
                try
                {
                    var over = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(langPath));
                    if (over != null)
                    {
                        Instance.Lang.SetOverride(over);
                        Debug.Log($"[RemoverTool] OK: Loaded language override -> {langPath}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[RemoverTool] Language override load failed: " + ex.Message);
                }
            }

            Debug.Log($"[RemoverTool] Config: {configPath} (exists={File.Exists(configPath)})");
            Debug.Log($"[RemoverTool] Data:   {modData}");
        }

        public static void Shutdown()
        {
            Instance?.Timer?.DestroyAll();
            Instance?.Cmd?.Clear();
            Instance = null;
        }

        public void Puts(string message) => Debug.Log("[RemoverTool] " + message);
        public void Puts(string format, params object[] args) =>
            Debug.Log("[RemoverTool] " + (args == null || args.Length == 0 ? format : string.Format(format, args)));
        public void PrintWarning(string message) => Debug.LogWarning("[RemoverTool] " + message);
        public void PrintWarning(string format, params object[] args) =>
            Debug.LogWarning("[RemoverTool] " + (args == null || args.Length == 0 ? format : string.Format(format, args)));
        public void PrintError(string message) => Debug.LogError("[RemoverTool] " + message);
        public void PrintError(string format, params object[] args) =>
            Debug.LogError("[RemoverTool] " + (args == null || args.Length == 0 ? format : string.Format(format, args)));

        public void LogToFile(string filename, string text, object plugin, bool timestamp = true)
        {
            try
            {
                var dir = Path.Combine(ModDataDirectory ?? Path.Combine(DataDirectory, "RemoverTool"), "logs");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, (filename ?? "RemoverTool") + ".txt");
                var line = (timestamp ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] " : "") + text + Environment.NewLine;
                File.AppendAllText(path, line);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RemoverTool] LogToFile: " + ex.Message);
            }
        }
    }

    #endregion

    #region RemoverToolPluginBase

    public abstract class RemoverToolPluginBase
    {
        public string Name => "RemoverTool";
        public string Title => "RemoverTool";
        public VersionNumber Version { get; protected set; } = new VersionNumber(4, 3, 431);
        public bool IsLoaded { get; set; } = true;

        protected RemoverToolHost Host => RemoverToolHost.Instance;
        protected HarmonyPermissionHelper permission => Host?.Permission;
        protected TimerHelper timer => Host?.Timer;
        protected LangHelper lang => Host?.Lang;
        protected CovalenceHelper covalence => Host?.Covalence;
        protected WebRequestHelper webrequest => Host?.Webrequest;
        protected PluginsHelper plugins => Host?.Plugins;
        protected PlayersHelper players => Host?.Players;
        protected CmdHelper cmd => Host?.Cmd;
        protected RustPlayerLibrary Player => Host?.PlayerLib;
        protected DynamicConfigFile Config => Host?.Config;

        private readonly HashSet<string> _subscribedHooks = new HashSet<string>(StringComparer.Ordinal);

        public void Subscribe(string hook)
        {
            if (!string.IsNullOrEmpty(hook)) _subscribedHooks.Add(hook);
        }

        public void Unsubscribe(string hook)
        {
            if (!string.IsNullOrEmpty(hook)) _subscribedHooks.Remove(hook);
        }

        public bool IsSubscribed(string hook) =>
            !string.IsNullOrEmpty(hook) && _subscribedHooks.Contains(hook);

        protected void Puts(string message) => Host?.Puts(message);
        protected void Puts(string format, params object[] args) => Host?.Puts(format, args);
        protected void PrintWarning(string message) => Host?.PrintWarning(message);
        protected void PrintWarning(string format, params object[] args) => Host?.PrintWarning(format, args);
        protected void PrintError(string message) => Host?.PrintError(message);
        protected void PrintError(string format, params object[] args) => Host?.PrintError(format, args);

        protected void PrintToConsole(BasePlayer player, string format, params object[] args)
        {
            if (player?.net == null) return;
            try
            {
                string text = args != null && args.Length > 0 ? string.Format(format, args) : format;
                player.ConsoleMessage(text);
            }
            catch (Exception ex) { Debug.LogWarning("[RemoverTool] PrintToConsole: " + ex.Message); }
        }

        protected void NextTick(Action action)
        {
            try { ServerMgr.Instance?.StartCoroutine(NextTickCoroutine(action)); }
            catch { }
        }

        private static IEnumerator NextTickCoroutine(Action action)
        {
            yield return null;
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[RemoverTool] NextTick: " + ex.Message); }
        }

        protected void LogToFile(string filename, string text, object plugin, bool timestamp = true) =>
            Host?.LogToFile(filename, text, plugin, timestamp);

        protected virtual void LoadConfig() { }
        protected virtual void SaveConfig() { }
        protected virtual void LoadDefaultConfig() { }
        protected virtual void LoadDefaultMessages() { }

        public abstract void HarmonyInit();
        public abstract void HarmonyServerInitialized();
        public abstract void HarmonyUnload();
    }

    #endregion
}
