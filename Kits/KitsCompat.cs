/*
 * Harmony shims so the ported Kits 2.3.8 logic can run without Oxide/Carbon.
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

namespace KitsHarmony
{
    #region Attributes (stubs — commands registered manually)

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InfoAttribute : Attribute
    {
        public InfoAttribute(string title, string author, string version) { }
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
        public ConsoleCommandAttribute(string command) { }
    }

    #endregion

    #region Plugin stub

    public class Plugin
    {
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public bool IsLoaded { get; set; }
        public virtual object Call(string method, params object[] args) => null;
    }

    public sealed class PluginBridge : Plugin
    {
        private readonly object _wrapped;
        private readonly MethodInfo _call;

        public PluginBridge(object wrapped)
        {
            _wrapped = wrapped;
            _call = wrapped?.GetType().GetMethod("Call", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            IsLoaded = wrapped != null;
        }

        public override object Call(string method, params object[] args)
        {
            if (_call == null || string.IsNullOrEmpty(method)) return null;
            try { return _call.Invoke(_wrapped, new object[] { method, args ?? Array.Empty<object>() }); }
            catch (Exception ex)
            {
                Debug.LogWarning("[Kits] Plugin call " + method + ": " + (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }
    }

    #endregion

    #region IPlayer

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
        public void Reply(string message) => Debug.Log("[Kits] " + message);
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
            // No IsAdmin auto-pass — Permissions groups/grants only (see HarmonyConfig/Permissions.json).
            return KitsHost.Instance?.Permission?.UserHasPermission(Id, perm) == true;
        }
    }

    public static class PlayerExtensions
    {
        public static IPlayer ToIPlayer(this BasePlayer player) =>
            player == null ? null : new BasePlayerWrapper(player);

        public static bool IsSteamId(this ulong id) => id > 76561197960265728UL;
        public static bool IsSteamId(this string id) =>
            !string.IsNullOrEmpty(id) && ulong.TryParse(id, out var uid) && uid.IsSteamId();
        public static bool IsSteamId(this EncryptedValue<ulong> id)
        {
            try { return ((ulong)id) > 76561197960265728UL; }
            catch { return false; }
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

    #region Timer

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
                        catch (Exception ex) { Debug.LogWarning("[Kits] Timer: " + ex.Message); }
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
            catch (Exception ex) { Debug.LogWarning("[Kits] Timer: " + ex.Message); }
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
                catch (Exception ex) { Debug.LogWarning("[Kits] Timer: " + ex.Message); }
                count++;
            }
            timer.Destroy();
            lock (_timers) _timers.Remove(timer);
        }
    }

    #endregion

    #region Permission / Lang / Players

    public class HarmonyPermissionHelper
    {
        private readonly HashSet<string> _registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Action _readyCallback;

        // Lazy bind to Permissions Harmony mod (AppDomain key Permissions_ApiType)
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
                        Debug.LogWarning("[Kits] Permissions mod not loaded — kit Permission fields will only allow server admins until 0Permissions.dll is loaded.");
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
                    Debug.Log("[Kits] Linked to Permissions Harmony mod for kit access checks.");
                }
                else
                    Debug.Log($"[Kits] Re-linked to Permissions Harmony mod (gen={gen}).");
            }
            catch (Exception ex)
            {
                _permType = null;
                Debug.LogWarning("[Kits] Permissions bind failed: " + ex.Message);
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

        public void RegisterPermission(string perm, object plugin)
        {
            if (string.IsNullOrEmpty(perm)) return;
            _registered.Add(perm);
            EnsureBound();
            EnsureReadyCallback();
            try { _register?.Invoke(null, new object[] { perm }); } catch { }
        }

        public bool PermissionExists(string perm)
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

            // No IsAdmin auto-pass — would treat deny perms (e.g. *.banned) as granted.
            // Local grants (tests / fallback)
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

    public class LangHelper
    {
        private readonly Dictionary<string, Dictionary<string, string>> _byLang =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public void RegisterMessages(Dictionary<string, string> messages, object plugin, string language = "en")
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

    public class PluginsHelper
    {
        public Plugin Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.Equals("ServerPanel", StringComparison.OrdinalIgnoreCase))
            {
                var wrapper = AppDomain.CurrentDomain.GetData("ServerPanel_Plugin");
                if (wrapper != null)
                    return new PluginBridge(wrapper) { Name = "ServerPanel", IsLoaded = true };
            }
            return null;
        }
    }

    public class ServerHelper
    {
        public void Command(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            try { ConsoleSystem.Run(ConsoleSystem.Option.Server, command); }
            catch (Exception ex) { Debug.LogWarning("[Kits] Server.Command: " + ex.Message); }
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

    public class CovalenceHelper
    {
        public PlayersHelper Players { get; } = new PlayersHelper();
    }

    #endregion

    #region Config / Data files

    public class DynamicConfigFile
    {
        private JToken _data;
        private readonly string _path;
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

        /// <summary>Oxide Config.Get(params string[] keys) — nested path lookup.</summary>
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

        public T ReadObject<T>() where T : class, new()
        {
            try
            {
                if (_data == null || _data.Type == JTokenType.Null)
                    return new T();
                if (_data is JObject jo && !jo.HasValues && typeof(T) != typeof(object))
                {
                    // Empty object — still try deserialize (e.g. empty PluginData)
                }
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
            // Oxide stores both objects and arrays (e.g. DisabledAutoKits is List<ulong>)
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

    /// <summary>Data root = HarmonyData so Oxide paths like Kits/Players/x resolve correctly.</summary>
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
                try
                {
                    // Oxide data files may be objects (Kits.json) or arrays (DisabledAutoKits.json)
                    data = JToken.Parse(File.ReadAllText(path));
                }
                catch
                {
                    data = new JObject();
                }
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
            // Bust cache so we always read current disk contents (array or object)
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

        /// <summary>Returns full file paths ending in .json (Oxide-compatible for Kits PlayerData.GetFiles).</summary>
        public string[] GetFiles(string relativePath)
        {
            relativePath = (relativePath ?? "").Replace('\\', '/').Trim('/');
            var dir = string.IsNullOrEmpty(relativePath)
                ? _root
                : Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            return Directory.GetFiles(dir, "*.json");
        }
    }

    #endregion

    #region Interface / Oxide stub

    public class OxideStub
    {
        public DataFileSystem DataFileSystem => Interface.DataFileSystem;
        public string DataDirectory => KitsHost.Instance?.DataDirectory ?? "";
        public void LogError(string message) => Debug.LogError("[Kits] " + message);
        public object CallHook(string name, params object[] args) => null;
        public void NextTick(Action action) => Interface.NextTick(action);
        public T GetLibrary<T>() where T : class
        {
            if (typeof(T) == typeof(HarmonyPermissionHelper))
                return KitsHost.Instance?.Permission as T;
            return null;
        }
        public void ReloadPlugin(string name) =>
            Debug.LogWarning("[Kits] ReloadPlugin is a no-op under Harmony (restart/reload the mod instead).");
    }

    public static class Interface
    {
        public static DataFileSystem DataFileSystem { get; set; }
        public static OxideStub Oxide { get; } = new OxideStub();

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
            catch (Exception ex) { Debug.LogWarning("[Kits] NextTick: " + ex.Message); }
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
                        if (headers != null)
                        {
                            foreach (var kv in headers)
                            {
                                if (kv.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) && req.Content != null)
                                    req.Content.Headers.ContentType =
                                        new System.Net.Http.Headers.MediaTypeHeaderValue(kv.Value.Split(';')[0].Trim());
                                else
                                    req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                            }
                        }
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
                        catch (Exception ex) { Debug.LogWarning("[Kits] webrequest callback: " + ex.Message); }
                    });
                }
                catch { }
            });
        }
    }

    #endregion

    #region Dictionary polyfill

    public static class DictionaryExtensions
    {
        public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key,
            TValue defaultValue = default)
        {
            if (dict != null && dict.TryGetValue(key, out var value))
                return value;
            return defaultValue;
        }

        public static bool Contains(this string[] array, string value)
        {
            if (array == null) return false;
            for (int i = 0; i < array.Length; i++)
            {
                if (string.Equals(array[i], value, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }

    #endregion

    #region Host

    public class KitsHost
    {
        public static KitsHost Instance { get; private set; }
        public HarmonyPermissionHelper Permission { get; } = new HarmonyPermissionHelper();
        public HarmonyTimerRunner Timer { get; } = new HarmonyTimerRunner();
        public LangHelper Lang { get; } = new LangHelper();
        public PlayersHelper Players { get; } = new PlayersHelper();
        public PluginsHelper Plugins { get; } = new PluginsHelper();
        public ServerHelper Server { get; } = new ServerHelper();
        public CovalenceHelper Covalence { get; } = new CovalenceHelper();
        public WebRequestHelper Webrequest { get; } = new WebRequestHelper();
        public DynamicConfigFile Config { get; private set; }
        public Kits Plugin { get; set; }
        public string ServerRoot { get; private set; }
        public string DataDirectory { get; private set; }

        public static void Init(string serverRoot)
        {
            Instance = new KitsHost();
            Instance.ServerRoot = serverRoot;
            var dataDir = Path.Combine(serverRoot, "HarmonyData");
            var configDir = Path.Combine(serverRoot, "HarmonyConfig");
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
            if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);
            Directory.CreateDirectory(Path.Combine(dataDir, "Kits"));
            Directory.CreateDirectory(Path.Combine(dataDir, "Kits", "Players"));
            Directory.CreateDirectory(Path.Combine(dataDir, "Kits", "logs"));
            Instance.DataDirectory = dataDir;
            Interface.DataFileSystem = new DataFileSystem(dataDir);

            var configPath = Path.Combine(configDir, "Kits.json");
            if (!File.Exists(configPath))
            {
                var oxideConfig = Path.Combine(serverRoot, "oxide", "config", "Kits.json");
                if (File.Exists(oxideConfig))
                {
                    try
                    {
                        File.Copy(oxideConfig, configPath);
                        Debug.Log("[Kits] Migrated config from oxide/config/Kits.json");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[Kits] Config migrate failed: " + ex.Message);
                    }
                }
            }

            // Also migrate oxide/data/Kits/* into HarmonyData/Kits/ if Harmony data is empty
            TryMigrateOxideData(serverRoot, dataDir);

            JToken data = null;
            if (File.Exists(configPath))
            {
                try { data = JToken.Parse(File.ReadAllText(configPath)); }
                catch { data = null; }
            }
            Instance.Config = new DynamicConfigFile(configPath, data ?? new JObject());

            Debug.Log($"[Kits] Config: {configPath} (exists={File.Exists(configPath)})");
            Debug.Log($"[Kits] Data:   {Path.Combine(dataDir, "Kits")} (kits={File.Exists(Path.Combine(dataDir, "Kits", "Kits.json"))}, players={Directory.Exists(Path.Combine(dataDir, "Kits", "Players"))})");
        }

        private static void TryMigrateOxideData(string serverRoot, string harmonyDataRoot)
        {
            try
            {
                var oxideKits = Path.Combine(serverRoot, "oxide", "data", "Kits");
                var harmonyKits = Path.Combine(harmonyDataRoot, "Kits");
                if (!Directory.Exists(oxideKits)) return;

                var harmonyKitsFile = Path.Combine(harmonyKits, "Kits.json");
                if (File.Exists(harmonyKitsFile)) return; // already have Harmony data

                Directory.CreateDirectory(harmonyKits);
                foreach (var file in Directory.GetFiles(oxideKits, "*", SearchOption.AllDirectories))
                {
                    var rel = file.Substring(oxideKits.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var dest = Path.Combine(harmonyKits, rel);
                    var destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);
                    if (!File.Exists(dest))
                        File.Copy(file, dest);
                }
                Debug.Log("[Kits] Migrated data from oxide/data/Kits → HarmonyData/Kits");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Kits] Data migrate failed: " + ex.Message);
            }
        }

        public static void Shutdown()
        {
            Instance?.Timer?.DestroyAll();
            Instance = null;
        }

        public void Puts(string message) => Debug.Log("[Kits] " + message);
        public void Puts(string format, params object[] args) =>
            Debug.Log("[Kits] " + (args == null || args.Length == 0 ? format : string.Format(format, args)));
        public void PrintWarning(string message) => Debug.LogWarning("[Kits] " + message);
        public void PrintError(string message) => Debug.LogError("[Kits] " + message);
        public void PrintError(string format, params object[] args) =>
            Debug.LogError("[Kits] " + (args == null || args.Length == 0 ? format : string.Format(format, args)));

        public void LogToFile(string filename, string text, object plugin, bool timestamp = true)
        {
            try
            {
                var dir = Path.Combine(DataDirectory, "Kits", "logs");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, (filename ?? "Kits") + ".txt");
                var line = (timestamp ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] " : "") + text + Environment.NewLine;
                File.AppendAllText(path, line);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Kits] LogToFile: " + ex.Message);
            }
        }
    }

    #endregion

    #region KitsPluginBase

    public abstract class KitsPluginBase
    {
        public string Name => "Kits";
        public string Title => "Kits";
        public VersionNumber Version { get; protected set; } = new VersionNumber(2, 3, 8);
        public bool IsLoaded { get; set; } = true;

        protected KitsHost Host => KitsHost.Instance;
        protected HarmonyPermissionHelper permission => Host?.Permission;
        protected HarmonyTimerRunner timer => Host?.Timer;
        protected LangHelper lang => Host?.Lang;
        protected CovalenceHelper covalence => Host?.Covalence;
        protected WebRequestHelper webrequest => Host?.Webrequest;
        protected PluginsHelper plugins => Host?.Plugins;
        protected ServerHelper Server => Host?.Server;
        protected DynamicConfigFile Config => Host?.Config;

        private readonly List<(string[] commands, string methodName)> _covalenceCommands =
            new List<(string[], string)>();

        internal IReadOnlyList<(string[] commands, string methodName)> RegisteredCovalenceCommands =>
            _covalenceCommands;

        protected void Puts(string message) => Host?.Puts(message);
        protected void Puts(string format, params object[] args) => Host?.Puts(format, args);
        protected void PrintWarning(string message) => Host?.PrintWarning(message);
        protected void PrintError(string message) => Host?.PrintError(message);
        protected void PrintError(string format, params object[] args) => Host?.PrintError(format, args);

        protected void NextTick(Action action)
        {
            try { ServerMgr.Instance?.StartCoroutine(NextTickCoroutine(action)); }
            catch { }
        }

        private static IEnumerator NextTickCoroutine(Action action)
        {
            yield return null;
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[Kits] NextTick: " + ex.Message); }
        }

        protected void SendReply(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrEmpty(message)) return;
            player.ChatMessage(message);
        }

        protected void SendReply(ConsoleSystem.Arg arg, string message)
        {
            if (arg == null) return;
            var player = arg.Player();
            if (player != null)
                player.ChatMessage(message);
            else
                Debug.Log("[Kits] " + message);
        }

        protected void LogToFile(string filename, string text, object plugin, bool timestamp = true) =>
            Host?.LogToFile(filename, text, plugin, timestamp);

        protected void Unsubscribe(string hook) { }
        protected void Subscribe(string hook) { }

        protected void AddCovalenceCommand(string[] commands, string methodName)
        {
            if (commands == null || string.IsNullOrEmpty(methodName)) return;
            _covalenceCommands.Add((commands, methodName));
        }

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
