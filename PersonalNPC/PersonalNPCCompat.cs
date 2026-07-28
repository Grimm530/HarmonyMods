/*
 * Harmony shims so the ported PersonalNPC / PersonalNPCHelper / PNPCAddonBuilder logic can run
 * without Oxide or Carbon. No Oxide assemblies are referenced or loaded.
 *
 * Three Oxide plugins are merged into this one DLL. Cross-plugin Oxide calls
 * (plugin.Call("HasBot", player) etc.) are routed by LocalPluginBridge, which reflects into the
 * co-hosted instance, so the original Oxide call sites keep working unchanged.
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

namespace PersonalNPCHarmony
{
    #region Attributes (stubs - commands registered manually)

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InfoAttribute : Attribute
    {
        public InfoAttribute(string title, string author, string version) { }
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
        public ConsoleCommandAttribute(string command) { }
    }

    #endregion

    #region Plugin bridges

    /// <summary>
    /// Oxide Plugin surface used by [PluginReference] fields. The base implementation is an inert
    /// stub (soft dependency that is not installed); LocalPluginBridge overrides it for the three
    /// plugins merged into this DLL.
    /// </summary>
    public class Plugin
    {
        public virtual string Name { get; set; } = "";
        public virtual string Title { get; set; } = "";
        public virtual bool IsLoaded { get; set; }

        public virtual object Call(string method, params object[] args) => null;

        public object CallHook(string method, params object[] args) => Call(method, args);

        public T Call<T>(string method, params object[] args)
        {
            var result = Call(method, args);
            if (result is T typed) return typed;
            if (result == null) return default;
            try { return (T)Convert.ChangeType(result, typeof(T)); }
            catch { return default; }
        }
    }

    /// <summary>
    /// Routes Oxide-style Call/CallHook into a co-hosted plugin instance by reflection.
    /// Reflection (rather than direct calls) keeps the converted Oxide sources untouched: their
    /// API methods stay private exactly as they were written.
    /// </summary>
    public class LocalPluginBridge : Plugin
    {
        private readonly object _target;
        private readonly Type _type;
        private readonly Dictionary<string, MethodInfo[]> _cache =
            new Dictionary<string, MethodInfo[]>(StringComparer.Ordinal);

        public LocalPluginBridge(string name, object target)
        {
            _target = target;
            _type = target?.GetType();
            Name = name;
            Title = name;
            IsLoaded = target != null;
        }

        public object Target => _target;

        public override object Call(string method, params object[] args)
        {
            if (_target == null || _type == null || string.IsNullOrEmpty(method)) return null;
            args ??= Array.Empty<object>();

            MethodInfo[] candidates;
            if (!_cache.TryGetValue(method, out candidates))
            {
                candidates = _type
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => string.Equals(m.Name, method, StringComparison.Ordinal))
                    .ToArray();
                _cache[method] = candidates;
            }

            var target = PickOverload(candidates, args);
            if (target == null)
            {
                Debug.LogWarning("[PersonalNPC] " + Name + ".Call: no method '" + method + "' with " + args.Length + " args");
                return null;
            }

            try { return target.Invoke(_target, args); }
            catch (TargetInvocationException tie)
            {
                Debug.LogWarning("[PersonalNPC] " + Name + "." + method + ": " + (tie.InnerException?.Message ?? tie.Message));
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PersonalNPC] " + Name + "." + method + ": " + ex.Message);
                return null;
            }
        }

        private static MethodInfo PickOverload(MethodInfo[] candidates, object[] args)
        {
            if (candidates == null || candidates.Length == 0) return null;

            MethodInfo lastArityMatch = null;
            foreach (var m in candidates)
            {
                var ps = m.GetParameters();
                if (ps.Length != args.Length) continue;
                lastArityMatch = m;

                bool ok = true;
                for (int i = 0; i < ps.Length; i++)
                {
                    if (args[i] == null)
                    {
                        if (ps[i].ParameterType.IsValueType && Nullable.GetUnderlyingType(ps[i].ParameterType) == null)
                        {
                            ok = false;
                            break;
                        }
                        continue;
                    }
                    if (!ps[i].ParameterType.IsInstanceOfType(args[i]))
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) return m;
            }
            return lastArityMatch;
        }
    }

    #endregion

    #region ImageLibrary replacement

    /// <summary>
    /// Minimal ImageLibrary stand-in. AddImage downloads a PNG into HarmonyImages/PersonalNPC/
    /// (cached on disk between boots) and stores it in FileStorage; GetImage returns the CRC id
    /// that CUI RawImage png fields expect. Behaves as an inert stub while offline.
    /// </summary>
    public class ImageLibraryPlugin : Plugin
    {
        private static readonly HttpClient Http = new HttpClient();

        private readonly string _imageDir;
        private readonly Dictionary<string, string> _stored =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _inFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<KeyValuePair<string, byte[]>> _pendingStore =
            new Queue<KeyValuePair<string, byte[]>>();

        public ImageLibraryPlugin(string imageDir)
        {
            _imageDir = imageDir;
            Name = "ImageLibrary";
            Title = "ImageLibrary";
            IsLoaded = true;
            try { if (!Directory.Exists(_imageDir)) Directory.CreateDirectory(_imageDir); }
            catch (Exception ex) { Debug.LogWarning("[PersonalNPC] ImageLibrary dir: " + ex.Message); }
        }

        public override object Call(string method, params object[] args)
        {
            switch (method)
            {
                case "AddImage":
                    if (args != null && args.Length >= 2)
                        AddImage(args[0] as string, args[1] as string);
                    return null;

                case "GetImage":
                    return args != null && args.Length >= 1 ? GetImage(args[0] as string) : "";

                case "HasImage":
                    return args != null && args.Length >= 1 && !string.IsNullOrEmpty(GetImage(args[0] as string));

                case "GetImageURL":
                    return args != null && args.Length >= 1 ? args[0] as string : "";
            }
            return null;
        }

        public string GetImage(string imageName)
        {
            if (string.IsNullOrEmpty(imageName)) return "";
            FlushPendingStores();
            return _stored.TryGetValue(imageName, out var crc) ? crc : "";
        }

        public void AddImage(string url, string imageName)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(imageName)) return;
            if (_stored.ContainsKey(imageName)) return;

            var cachePath = Path.Combine(_imageDir, SanitizeFileName(imageName) + ".png");
            if (File.Exists(cachePath))
            {
                try
                {
                    Enqueue(imageName, File.ReadAllBytes(cachePath));
                    return;
                }
                catch { }
            }

            lock (_inFlight)
            {
                if (!_inFlight.Add(imageName)) return;
            }

            Task.Run(async () =>
            {
                byte[] data = null;
                try { data = await Http.GetByteArrayAsync(url).ConfigureAwait(false); }
                catch { data = null; }

                if (data != null && data.Length > 0)
                {
                    try { File.WriteAllBytes(cachePath, data); }
                    catch { }
                    Enqueue(imageName, data);
                }

                lock (_inFlight) { _inFlight.Remove(imageName); }
            });
        }

        private void Enqueue(string imageName, byte[] data)
        {
            lock (_pendingStore)
            {
                _pendingStore.Enqueue(new KeyValuePair<string, byte[]>(imageName, data));
            }
        }

        /// <summary>Stores queued downloads into FileStorage. Must run on the main thread.</summary>
        public void FlushPendingStores()
        {
            List<KeyValuePair<string, byte[]>> batch = null;
            lock (_pendingStore)
            {
                if (_pendingStore.Count == 0) return;
                batch = new List<KeyValuePair<string, byte[]>>(_pendingStore);
                _pendingStore.Clear();
            }

            var community = CommunityEntity.ServerInstance;
            if (community == null || community.net == null)
            {
                lock (_pendingStore)
                {
                    foreach (var kv in batch) _pendingStore.Enqueue(kv);
                }
                return;
            }

            foreach (var kv in batch)
            {
                try
                {
                    var crc = FileStorage.server.Store(kv.Value, FileStorage.Type.png, community.net.ID);
                    _stored[kv.Key] = crc.ToString();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[PersonalNPC] ImageLibrary store " + kv.Key + ": " + ex.Message);
                }
            }
        }

        public int StoredCount => _stored.Count;

        private static string SanitizeFileName(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' ? c : '_');
            return sb.ToString();
        }
    }

    #endregion

    #region rust helper

    /// <summary>Oxide's `rust` convenience library (chat + client command helpers).</summary>
    public static class rust
    {
        public static void SendChatMessage(BasePlayer player, string name, string message = null, string userId = "0")
        {
            if (player?.net?.connection == null) return;

            string text = message != null
                ? (string.IsNullOrEmpty(name) ? message : name + ": " + message)
                : name;

            ulong uid = 0;
            if (!string.IsNullOrEmpty(userId)) ulong.TryParse(userId, out uid);

            try { ConsoleNetwork.SendClientCommand(player.net.connection, "chat.add", 2, uid, text ?? ""); }
            catch (Exception ex) { Debug.LogWarning("[PersonalNPC] SendChatMessage: " + ex.Message); }
        }

        public static void RunClientCommand(BasePlayer player, string command, params object[] args)
        {
            if (player?.net?.connection == null || string.IsNullOrEmpty(command)) return;
            try { ConsoleNetwork.SendClientCommand(player.net.connection, command, args ?? Array.Empty<object>()); }
            catch (Exception ex) { Debug.LogWarning("[PersonalNPC] RunClientCommand: " + ex.Message); }
        }

        public static void BroadcastChat(string name, string message = null, string userId = "0")
        {
            foreach (var player in BasePlayer.activePlayerList)
                SendChatMessage(player, name, message, userId);
        }

        public static void RunServerCommand(string command, params object[] args)
        {
            if (string.IsNullOrEmpty(command)) return;
            try { ConsoleSystem.Run(ConsoleSystem.Option.Server, command, args ?? Array.Empty<object>()); }
            catch (Exception ex) { Debug.LogWarning("[PersonalNPC] RunServerCommand: " + ex.Message); }
        }

        public static string QuoteSafe(string str) => str == null ? null : str.Replace("\"", "\\\"");
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
        public bool HasPermission(string perm) =>
            PersonalNPCHost.Instance?.Permission?.UserHasPermission(Id, perm) == true;
    }

    public static class PlayerExtensions
    {
        public static IPlayer ToIPlayer(this BasePlayer player) =>
            player == null ? null : new BasePlayerWrapper(player);

        // Oxide ships these on its Rust extension; the ported code relies on them everywhere.
        public const ulong SteamIdBase = 76561197960265728UL;

        public static bool IsSteamId(this ulong id) => id >= SteamIdBase;

        public static bool IsSteamId(this string id) =>
            !string.IsNullOrEmpty(id) && ulong.TryParse(id, out var uid) && uid.IsSteamId();

        public static bool IsSteamId(this EncryptedValue<ulong> id)
        {
            try { return ((ulong)id) >= SteamIdBase; }
            catch { return false; }
        }

        public static BasePlayer ToPlayer(this IPlayer user) => (user?.Object) as BasePlayer;

        public static bool IsHuman(this BasePlayer player) =>
            player != null && !player.IsNpc && player.userID.IsSteamId();

        /// <summary>Console arg helper; the Rust extension of the same name is not always visible.</summary>
        public static BasePlayer ArgPlayer(this ConsoleSystem.Arg arg) =>
            arg?.Connection?.player as BasePlayer;
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
        public override string ToString() => Major + "." + Minor + "." + Patch;
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
                        catch (Exception ex) { Debug.LogWarning("[PersonalNPC] Timer: " + ex.Message); }
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
            try { ServerMgr.Instance?.StartCoroutine(RepeatRun(seconds, times, t, callback)); }
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
            catch (Exception ex) { Debug.LogWarning("[PersonalNPC] Timer: " + ex.Message); }
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
                catch (Exception ex) { Debug.LogWarning("[PersonalNPC] Timer: " + ex.Message); }
                count++;
            }
            timer.Destroy();
            lock (_timers) _timers.Remove(timer);
        }
    }

    #endregion

    #region Lang / Plugins / Server / Players

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

        public string GetMessage(string key, object plugin, string userId = null)
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

    /// <summary>
    /// plugins.Find / plugins.Exists. Only the three merged plugins plus the ImageLibrary
    /// replacement resolve; every other soft dependency stays null, which is exactly what the
    /// original Oxide null-checks expect.
    /// </summary>
    public class PluginsHelper
    {
        private readonly Dictionary<string, Plugin> _known =
            new Dictionary<string, Plugin>(StringComparer.OrdinalIgnoreCase);

        public void Register(string name, Plugin plugin)
        {
            if (string.IsNullOrEmpty(name) || plugin == null) return;
            _known[name] = plugin;
        }

        public Plugin Find(string name) =>
            !string.IsNullOrEmpty(name) && _known.TryGetValue(name, out var p) ? p : null;

        public bool Exists(string name) => Find(name) != null;

        public Plugin[] GetAll() => _known.Values.ToArray();
    }

    public class ServerHelper
    {
        public void Command(string command, params object[] args)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            try { ConsoleSystem.Run(ConsoleSystem.Option.Server, command, args ?? Array.Empty<object>()); }
            catch (Exception ex) { Debug.LogWarning("[PersonalNPC] Server.Command: " + ex.Message); }
        }

        public void Broadcast(string message, string prefix = null, ulong userId = 0)
        {
            foreach (var player in BasePlayer.activePlayerList)
                rust.SendChatMessage(player, prefix ?? "", message, userId.ToString());
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
        private readonly DynamicConfigFile _parent;
        private readonly string _sectionKey;

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

        /// <summary>
        /// A view over one top-level key of another config file. Used so PNPCAddonBuilder can keep
        /// its own Configuration class while living inside the merged PersonalNPC.json.
        /// </summary>
        private DynamicConfigFile(DynamicConfigFile parent, string sectionKey, JToken data)
        {
            _parent = parent;
            _sectionKey = sectionKey;
            _data = data ?? new JObject();
        }

        public static DynamicConfigFile Section(DynamicConfigFile parent, string sectionKey)
        {
            JToken slice = new JObject();
            try
            {
                if (parent?.Raw is JObject root && root[sectionKey] != null)
                    slice = new JObject(new JProperty(sectionKey, root[sectionKey].DeepClone()));
            }
            catch { }
            return new DynamicConfigFile(parent, sectionKey, slice);
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
                return _data.ToObject<T>(JsonSerializer.Create(Settings)) ?? new T();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PersonalNPC] Config parse failed, using defaults: " + ex.Message);
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
            if (_parent != null)
            {
                try
                {
                    if (_parent.Raw is JObject root && _data is JObject slice)
                    {
                        root[_sectionKey] = slice[_sectionKey] ?? slice;
                        _parent.Save();
                    }
                }
                catch (Exception ex) { Debug.LogWarning("[PersonalNPC] Config section save: " + ex.Message); }
                return;
            }

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

    /// <summary>Data root = HarmonyData so relative Oxide keys such as copypaste/x resolve.</summary>
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
            GetFile(relativePath).WriteObject(obj, true);
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
        public string DataDirectory => PersonalNPCHost.Instance?.DataDirectory ?? "";
        public void LogError(string message) => Debug.LogError("[PersonalNPC] " + message);
        public void LogWarning(string message) => Debug.LogWarning("[PersonalNPC] " + message);
        public object CallHook(string name, params object[] args) => null;
        public void NextTick(Action action) => Interface.NextTick(action);

        public T GetLibrary<T>() where T : class
        {
            if (typeof(T) == typeof(HarmonyPermissionHelper))
                return PersonalNPCHost.Instance?.Permission as T;
            return null;
        }

        /// <summary>All three plugins are co-hosted in this DLL - nothing to load.</summary>
        public void LoadPlugin(string name) { }

        /// <summary>No-op: unloading a co-hosted plugin separately is not supported.</summary>
        public void UnloadPlugin(string name) { }

        public void ReloadPlugin(string name) =>
            Debug.LogWarning("[PersonalNPC] ReloadPlugin is a no-op under Harmony (reload the mod instead).");
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
            catch (Exception ex) { Debug.LogWarning("[PersonalNPC] NextTick: " + ex.Message); }
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
                        catch (Exception ex) { Debug.LogWarning("[PersonalNPC] webrequest callback: " + ex.Message); }
                    });
                }
                catch { }
            });
        }
    }

    #endregion

    #region Host

    public class PersonalNPCHost
    {
        public static PersonalNPCHost Instance { get; private set; }

        public HarmonyPermissionHelper Permission { get; } = new HarmonyPermissionHelper();
        public HarmonyTimerRunner Timer { get; } = new HarmonyTimerRunner();
        public LangHelper Lang { get; } = new LangHelper();
        public PlayersHelper Players { get; } = new PlayersHelper();
        public PluginsHelper Plugins { get; } = new PluginsHelper();
        public ServerHelper Server { get; } = new ServerHelper();
        public CovalenceHelper Covalence { get; } = new CovalenceHelper();
        public WebRequestHelper Webrequest { get; } = new WebRequestHelper();

        public DynamicConfigFile Config { get; private set; }
        public DynamicConfigFile BuilderConfig { get; private set; }
        public ImageLibraryPlugin ImageLibrary { get; private set; }

        public string ServerRoot { get; private set; }
        public string DataDirectory { get; private set; }
        public string ConfigPath { get; private set; }

        public const string BuilderConfigSection = "Available buildings (by PNPC bot spawn name)";

        public static void Init(string serverRoot)
        {
            Instance = new PersonalNPCHost();
            Instance.ServerRoot = serverRoot;

            var dataDir = Path.Combine(serverRoot, "HarmonyData");
            var configDir = Path.Combine(serverRoot, "HarmonyConfig");
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
            if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);
            Directory.CreateDirectory(Path.Combine(dataDir, "PersonalNPC"));
            Directory.CreateDirectory(Path.Combine(dataDir, "PersonalNPC", "Inventories"));
            Instance.DataDirectory = dataDir;
            Interface.DataFileSystem = new DataFileSystem(dataDir);

            var imageDir = Path.Combine(serverRoot, "HarmonyImages", "PersonalNPC");
            Instance.ImageLibrary = new ImageLibraryPlugin(imageDir);

            // Preferred merged layout, with a flat fallback for servers that never had the folder.
            var nested = Path.Combine(configDir, "PersonalNPC", "PersonalNPC.json");
            var flat = Path.Combine(configDir, "PersonalNPC.json");
            var configPath = File.Exists(nested) ? nested : (File.Exists(flat) ? flat : nested);
            Instance.ConfigPath = configPath;

            JToken data = null;
            if (File.Exists(configPath))
            {
                try { data = JToken.Parse(File.ReadAllText(configPath)); }
                catch (Exception ex)
                {
                    Debug.LogError("[PersonalNPC] Config is not valid JSON, defaults will be written: " + ex.Message);
                    data = null;
                }
            }
            Instance.Config = new DynamicConfigFile(configPath, data ?? new JObject());

            MergeBuilderConfig(configDir, Instance.Config);
            Instance.BuilderConfig = DynamicConfigFile.Section(Instance.Config, BuilderConfigSection);

            Debug.Log("[PersonalNPC] Config: " + configPath + " (exists=" + File.Exists(configPath) + ")");
            Debug.Log("[PersonalNPC] Data:   " + Path.Combine(dataDir, "PersonalNPC"));
            Debug.Log("[PersonalNPC] Images: " + imageDir);
        }

        /// <summary>
        /// One config file for all three plugins: pull the builder section out of the legacy
        /// PNPCAddonBuilder.json the first time, then keep it inside PersonalNPC.json.
        /// </summary>
        private static void MergeBuilderConfig(string configDir, DynamicConfigFile merged)
        {
            try
            {
                if (merged.Raw is not JObject root) return;
                if (root[BuilderConfigSection] != null) return;

                var legacy = Path.Combine(configDir, "PersonalNPC", "PNPCAddonBuilder.json");
                if (!File.Exists(legacy))
                    legacy = Path.Combine(configDir, "PNPCAddonBuilder.json");
                if (!File.Exists(legacy)) return;

                var token = JToken.Parse(File.ReadAllText(legacy));
                if (token is not JObject legacyRoot) return;

                var section = legacyRoot[BuilderConfigSection];
                if (section == null) return;

                root[BuilderConfigSection] = section.DeepClone();
                merged.Save();
                Debug.Log("[PersonalNPC] Merged PNPCAddonBuilder.json into PersonalNPC.json");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PersonalNPC] Builder config merge failed: " + ex.Message);
            }
        }

        public static void Shutdown()
        {
            Instance?.Timer?.DestroyAll();
            Instance = null;
        }

        public void Puts(string message) => Debug.Log("[PersonalNPC] " + message);
        public void Puts(string format, params object[] args) =>
            Debug.Log("[PersonalNPC] " + (args == null || args.Length == 0 ? format : string.Format(format, args)));
        public void PrintWarning(string message) => Debug.LogWarning("[PersonalNPC] " + message);
        public void PrintWarning(string format, params object[] args) =>
            Debug.LogWarning("[PersonalNPC] " + (args == null || args.Length == 0 ? format : string.Format(format, args)));
        public void PrintError(string message) => Debug.LogError("[PersonalNPC] " + message);
        public void PrintError(string format, params object[] args) =>
            Debug.LogError("[PersonalNPC] " + (args == null || args.Length == 0 ? format : string.Format(format, args)));

        public void LogToFile(string filename, string text, object plugin, bool timestamp = true)
        {
            try
            {
                var dir = Path.Combine(DataDirectory, "PersonalNPC", "logs");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, (filename ?? "PersonalNPC") + ".txt");
                var line = (timestamp ? "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " : "") + text + Environment.NewLine;
                File.AppendAllText(path, line);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PersonalNPC] LogToFile: " + ex.Message);
            }
        }
    }

    #endregion

    #region PersonalNPCPluginBase

    /// <summary>
    /// Shared RustPlugin replacement for PersonalNPC, PersonalNPCHelper and PNPCAddonBuilder.
    /// </summary>
    public abstract class PersonalNPCPluginBase
    {
        public virtual string Name => "PersonalNPC";
        public virtual string Title => Name;
        public VersionNumber Version { get; protected set; } = new VersionNumber(1, 0, 0);
        public bool IsLoaded { get; set; } = true;

        /// <summary>Per-plugin config file; assigned by the host before HarmonyInit runs.</summary>
        public DynamicConfigFile Config { get; internal set; }

        protected PersonalNPCHost Host => PersonalNPCHost.Instance;
        protected HarmonyPermissionHelper permission => Host?.Permission;
        protected HarmonyTimerRunner timer => Host?.Timer;
        protected LangHelper lang => Host?.Lang;
        protected CovalenceHelper covalence => Host?.Covalence;
        protected WebRequestHelper webrequest => Host?.Webrequest;
        protected PluginsHelper plugins => Host?.Plugins;
        protected ServerHelper Server => Host?.Server;

        protected void Puts(string message) => Host?.Puts(Prefix() + message);
        protected void Puts(string format, params object[] args) =>
            Host?.Puts(Prefix() + (args == null || args.Length == 0 ? format : string.Format(format, args)));
        protected void PrintWarning(string message) => Host?.PrintWarning(Prefix() + message);
        protected void PrintWarning(string format, params object[] args) =>
            Host?.PrintWarning(Prefix() + (args == null || args.Length == 0 ? format : string.Format(format, args)));
        protected void PrintError(string message) => Host?.PrintError(Prefix() + message);
        protected void PrintError(string format, params object[] args) =>
            Host?.PrintError(Prefix() + (args == null || args.Length == 0 ? format : string.Format(format, args)));

        private string Prefix() => Name == "PersonalNPC" ? "" : Name + ": ";

        protected void NextTick(Action action)
        {
            try { ServerMgr.Instance?.StartCoroutine(NextTickCoroutine(action)); }
            catch { }
        }

        private static IEnumerator NextTickCoroutine(Action action)
        {
            yield return null;
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[PersonalNPC] NextTick: " + ex.Message); }
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
                Debug.Log("[PersonalNPC] " + message);
        }

        protected void LogToFile(string filename, string text, object plugin, bool timestamp = true) =>
            Host?.LogToFile(filename, text, plugin, timestamp);

        /// <summary>Hook subscription is meaningless here - Harmony patches are always active.</summary>
        protected void Unsubscribe(string hook) { }
        protected void Subscribe(string hook) { }

        /// <summary>Oxide self-call used by nested controller classes (plugin.Call&lt;string&gt;("GetMsg", ...)).</summary>
        public object Call(string method, params object[] args)
        {
            if (string.IsNullOrEmpty(method)) return null;
            args ??= Array.Empty<object>();
            try
            {
                var mi = GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == method && m.GetParameters().Length == args.Length);
                return mi?.Invoke(this, args);
            }
            catch (TargetInvocationException tie)
            {
                Debug.LogWarning("[PersonalNPC] " + Name + "." + method + ": " + (tie.InnerException?.Message ?? tie.Message));
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PersonalNPC] " + Name + "." + method + ": " + ex.Message);
                return null;
            }
        }

        public object CallHook(string method, params object[] args) => Call(method, args);

        public T Call<T>(string method, params object[] args)
        {
            var result = Call(method, args);
            if (result is T typed) return typed;
            if (result == null) return default;
            try { return (T)Convert.ChangeType(result, typeof(T)); }
            catch { return default; }
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
    }

    #endregion
}
