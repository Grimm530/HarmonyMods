/*
 * Harmony shims so the ported ServerPanel 2.0.20 / ServerPanelPopUps 2.0.20 logic can run
 * without Oxide/Carbon. No Oxide assemblies are referenced or loaded.
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
using UnityEngine.Networking;

namespace ServerPanelHarmony
{
    #region Attributes (stubs - commands registered manually)

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

    #region Plugin stubs

    public class Plugin
    {
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public VersionNumber Version { get; set; } = new VersionNumber(0, 0, 0);
        public virtual bool IsLoaded { get; set; }
        public virtual object Call(string method, params object[] args) => null;

        public T Call<T>(string method, params object[] args)
        {
            var result = Call(method, args);
            if (result is T direct) return direct;
            if (result == null) return default;
            try { return (T)Convert.ChangeType(result, typeof(T)); }
            catch
            {
                try { return JToken.FromObject(result).ToObject<T>(); }
                catch { return default; }
            }
        }
    }

    /// <summary>Bridge onto a Harmony mod wrapper object that exposes IsLoaded + Call(string, object[]).</summary>
    public sealed class PluginBridge : Plugin
    {
        private readonly object _wrapped;
        private readonly MethodInfo _call;
        private readonly PropertyInfo _isLoaded;

        public PluginBridge(object wrapped)
        {
            _wrapped = wrapped;
            var type = wrapped?.GetType();
            _call = type?.GetMethod("Call", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _isLoaded = type?.GetProperty("IsLoaded", BindingFlags.Instance | BindingFlags.Public);
        }

        public object Target => _wrapped;

        public override bool IsLoaded
        {
            get
            {
                if (_wrapped == null) return false;
                try { return _isLoaded?.GetValue(_wrapped) is not bool b || b; }
                catch { return true; }
            }
            set { }
        }

        public override object Call(string method, params object[] args)
        {
            if (_call == null || string.IsNullOrEmpty(method)) return null;
            try { return _call.Invoke(_wrapped, new object[] { method, args ?? Array.Empty<object>() }); }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerPanel] Plugin call " + Name + "." + method + ": " +
                                 (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }
    }

    /// <summary>Bridge onto a static API type (Kits_ApiType style).</summary>
    public sealed class PluginBridgeApi : Plugin
    {
        private readonly Type _apiType;
        public PluginBridgeApi(Type apiType) => _apiType = apiType;

        public override bool IsLoaded
        {
            get => _apiType != null;
            set { }
        }

        public override object Call(string method, params object[] args)
        {
            if (_apiType == null || string.IsNullOrEmpty(method)) return null;
            try
            {
                var argTypes = args?.Select(a => a?.GetType() ?? typeof(object)).ToArray() ?? Type.EmptyTypes;
                var mi = _apiType.GetMethod(method,
                             BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, argTypes, null)
                         ?? _apiType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                             .FirstOrDefault(m => m.Name == method && m.GetParameters().Length == (args?.Length ?? 0));
                return mi?.Invoke(null, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerPanel] API call " + Name + "." + method + ": " +
                                 (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }
    }

    /// <summary>
    /// Bridge onto the live plugin object owned by another Harmony mod. Resolves
    /// {ModApiType}.Instance.Plugin (or a private _plugin field) and reflects method calls onto it.
    /// </summary>
    public sealed class HarmonyModPluginBridge : Plugin
    {
        private readonly Func<object> _resolver;
        private object _cached;

        public HarmonyModPluginBridge(Func<object> resolver) => _resolver = resolver;

        private object Target
        {
            get
            {
                try { _cached = _resolver?.Invoke(); }
                catch { _cached = null; }
                return _cached;
            }
        }

        public override bool IsLoaded
        {
            get => Target != null;
            set { }
        }

        public override object Call(string method, params object[] args)
        {
            var target = Target;
            if (target == null || string.IsNullOrEmpty(method)) return null;
            try
            {
                var type = target.GetType();
                var count = args?.Length ?? 0;
                var mi = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == method && m.GetParameters().Length == count);
                return mi?.Invoke(target, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerPanel] Mod call " + Name + "." + method + ": " +
                                 (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }
    }

    /// <summary>Bridge onto one of the two plugin objects hosted by this mod.</summary>
    public sealed class LocalPluginBridge : Plugin
    {
        private readonly Func<object> _resolver;
        public LocalPluginBridge(Func<object> resolver) => _resolver = resolver;

        private object Target
        {
            get
            {
                try { return _resolver?.Invoke(); }
                catch { return null; }
            }
        }

        public override bool IsLoaded
        {
            get => Target != null;
            set { }
        }

        public override object Call(string method, params object[] args)
        {
            var target = Target;
            if (target == null || string.IsNullOrEmpty(method)) return null;
            try
            {
                var count = args?.Length ?? 0;
                var mi = target.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == method && m.GetParameters().Length == count);
                if (mi == null) return null;
                return mi.Invoke(target, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerPanel] Local call " + Name + "." + method + ": " +
                                 (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }
    }

    public class CommandLib
    {
        private readonly List<(string name, string method)> _console = new List<(string, string)>();
        private readonly List<(string name, string method)> _chat = new List<(string, string)>();

        public IReadOnlyList<(string name, string method)> RegisteredConsoleCommands => _console;
        public IReadOnlyList<(string name, string method)> RegisteredChatCommands => _chat;

        public void AddConsoleCommand(string name, object plugin, string method)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(method)) return;
            if (!_console.Any(c => string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase)))
                _console.Add((name, method));
        }

        public void AddChatCommand(string name, object plugin, string method)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(method)) return;
            if (!_chat.Any(c => string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase)))
                _chat.Add((name, method));
        }

        public void RemoveConsoleCommand(string name, object plugin) =>
            _console.RemoveAll(c => string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase));

        public void RemoveChatCommand(string name, object plugin) =>
            _chat.RemoveAll(c => string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase));

        public void Clear()
        {
            _console.Clear();
            _chat.Clear();
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
        public void Reply(string message) => Debug.Log("[ServerPanel] " + message);
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
            return ServerPanelHost.Instance?.Permission?.UserHasPermission(Id, perm) == true;
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
            lock (_timers)
            {
                copy = new List<Timer>(_timers);
                _timers.Clear();
            }

            foreach (var t in copy) t?.Destroy();
        }

        public Timer Once(float seconds, Action callback)
        {
            if (callback == null) return new Timer();
            var t = new Timer { Callback = callback };
            lock (_timers) _timers.Add(t);
            try
            {
                ServerMgr.Instance?.StartCoroutine(WaitAndRun(seconds, () =>
                {
                    if (!t.Destroyed)
                    {
                        try { callback(); }
                        catch (Exception ex) { Debug.LogWarning("[ServerPanel] Timer: " + ex.Message); }
                    }

                    t.Destroy();
                    lock (_timers) _timers.Remove(t);
                }));
            }
            catch { lock (_timers) _timers.Remove(t); }

            return t;
        }

        public Timer In(float seconds, Action callback) => Once(seconds, callback);

        public Timer Every(float seconds, Action callback) => Repeat(seconds, -1, callback);

        public Timer Repeat(float seconds, int times, Action callback)
        {
            if (callback == null) return new Timer();
            var t = new Timer { Callback = callback };
            lock (_timers) _timers.Add(t);
            try { ServerMgr.Instance?.StartCoroutine(RepeatRun(seconds, times, t, callback)); }
            catch { lock (_timers) _timers.Remove(t); }

            return t;
        }

        private static IEnumerator WaitAndRun(float seconds, Action callback)
        {
            if (seconds > 0f)
                yield return new WaitForSeconds(seconds);
            else
                yield return null;
            try { callback?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[ServerPanel] Timer: " + ex.Message); }
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
                catch (Exception ex) { Debug.LogWarning("[ServerPanel] Timer: " + ex.Message); }

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
            try { return type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null); }
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
                        Debug.LogWarning(
                            "[ServerPanel] Permissions mod not loaded - category permissions require 0Permissions.dll.");
                    }

                    return;
                }

                _resolveAttempted = false;
                BindingFlags sf = BindingFlags.Public | BindingFlags.Static;
                _userHas = _permType.GetMethod("UserHasPermission", sf, null,
                    new[] { typeof(string), typeof(string) }, null);
                _register = _permType.GetMethod("RegisterPermission", sf, null, new[] { typeof(string) }, null);
                _exists = _permType.GetMethod("PermissionExists", sf, null, new[] { typeof(string) }, null);
                _grantUser = _permType.GetMethod("GrantUserPermission", sf, null,
                    new[] { typeof(string), typeof(string) }, null);
                _registerReady = _permType.GetMethod("RegisterReadyCallback", sf, null, new[] { typeof(Action) }, null);
                _boundGen = gen;

                if (!_loggedLink)
                {
                    _loggedLink = true;
                    Debug.Log("[ServerPanel] Linked to Permissions Harmony mod for access checks.");
                }
                else
                    Debug.Log($"[ServerPanel] Re-linked to Permissions Harmony mod (gen={gen}).");
            }
            catch (Exception ex)
            {
                _permType = null;
                Debug.LogWarning("[ServerPanel] Permissions bind failed: " + ex.Message);
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
                var list = AppDomain.CurrentDomain.GetData("Permissions_ReadyCallbacks") as IList;
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
                try { _register?.Invoke(null, new object[] { perm }); }
                catch { }
            }
        }

        public void RegisterPermission(string perm, object plugin)
        {
            if (string.IsNullOrEmpty(perm)) return;
            _registered.Add(perm);
            EnsureBound();
            EnsureReadyCallback();
            try { _register?.Invoke(null, new object[] { perm }); }
            catch { }
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
            try { _grantUser?.Invoke(null, new object[] { userId, perm }); }
            catch { }
        }
    }

    /// <summary>Per-plugin language store, optionally seeded from HarmonyLanguage/&lt;lang&gt;/&lt;Plugin&gt;.json.</summary>
    public class LangHelper
    {
        private readonly string _pluginName;

        private readonly Dictionary<string, Dictionary<string, string>> _byLang =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public LangHelper(string pluginName) => _pluginName = pluginName ?? "ServerPanel";

        public void RegisterMessages(Dictionary<string, string> messages, object plugin, string language = "en")
        {
            if (messages == null) return;
            language = string.IsNullOrEmpty(language) ? "en" : language;
            if (!_byLang.TryGetValue(language, out var dict))
                _byLang[language] = dict = new Dictionary<string, string>();
            foreach (var kv in messages)
                dict[kv.Key] = kv.Value;

            LoadOverrides(language);
        }

        private void LoadOverrides(string language)
        {
            try
            {
                var root = ServerPanelHost.Instance?.LangDirectory;
                if (string.IsNullOrEmpty(root)) return;
                var path = Path.Combine(Path.Combine(root, language), _pluginName + ".json");
                if (!File.Exists(path)) return;
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (loaded == null) return;
                if (!_byLang.TryGetValue(language, out var dict))
                    _byLang[language] = dict = new Dictionary<string, string>();
                foreach (var kv in loaded)
                    dict[kv.Key] = kv.Value;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerPanel] Lang overrides: " + ex.Message);
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

        public string GetMessage(string key, object plugin) => GetMessage(key, plugin, null);

        public string[] GetLanguages(object plugin = null)
        {
            if (_byLang.Count > 0)
            {
                var keys = new string[_byLang.Count];
                _byLang.Keys.CopyTo(keys, 0);
                return keys;
            }

            return new[] { "en" };
        }

        public string GetLanguage(string userId) => "en";
    }

    /// <summary>
    /// Resolves the plugin names ServerPanel data references onto Harmony mods through AppDomain keys.
    /// </summary>
    public class PluginsHelper
    {
        private readonly Dictionary<string, Plugin> _cache =
            new Dictionary<string, Plugin>(StringComparer.OrdinalIgnoreCase);

        public void Clear() => _cache.Clear();

        private Plugin Cached(string key, Func<Plugin> factory)
        {
            if (_cache.TryGetValue(key, out var cached) && cached != null && cached.IsLoaded)
                return cached;
            var built = factory();
            if (built != null) _cache[key] = built;
            else _cache.Remove(key);
            return built;
        }

        private static object GetDomain(string key)
        {
            try { return AppDomain.CurrentDomain.GetData(key); }
            catch { return null; }
        }

        private static Plugin FromWrapper(string key, string name)
        {
            var wrapper = GetDomain(key);
            return wrapper == null ? null : new PluginBridge(wrapper) { Name = name };
        }

        private static Plugin FromModInstance(string apiKey, string name)
        {
            if (GetDomain(apiKey) is not Type apiType) return null;
            return new HarmonyModPluginBridge(() =>
            {
                var t = GetDomain(apiKey) as Type;
                var inst = t?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (inst == null) return null;
                var it = inst.GetType();
                var viaProp = it.GetProperty("Plugin", BindingFlags.Public | BindingFlags.Instance)?.GetValue(inst);
                if (viaProp != null) return viaProp;
                return it.GetField("_plugin", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(inst);
            }) { Name = name };
        }

        public Plugin Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            switch (name.ToLowerInvariant())
            {
                case "serverpanel":
                    return Cached("ServerPanel",
                        () => new LocalPluginBridge(() => ServerPanelHost.Instance?.Panel) { Name = "ServerPanel" });

                case "serverpanelpopups":
                    return Cached("ServerPanelPopUps",
                        () => new LocalPluginBridge(() => ServerPanelHost.Instance?.PopUps)
                            { Name = "ServerPanelPopUps" });

                case "imagelibrary":
                    return ServerPanelHost.Instance?.ImageLibrary;

                case "shop":
                    return Cached("Shop", () => FromWrapper("Shop_Plugin", "Shop"));

                case "kits":
                    return Cached("Kits", () => FromWrapper("Kits_Plugin", "Kits") ?? FromModInstance("Kits_ApiType", "Kits"));

                case "wipeschedule":
                    return Cached("WipeSchedule", () => FromWrapper("WipeSchedule_Plugin", "WipeSchedule")
                                                        ?? FromModInstance("WipeSchedule_ApiType", "WipeSchedule"));

                // The GUI mod owns API_OpenPlugin; the core RustVehicles mod is license/spawn logic only.
                case "rustvehiclesgui":
                    return Cached("RustVehiclesGUI", () => FromWrapper("RustVehiclesGUI_Plugin", name)
                                                           ?? FromModInstance("RustVehiclesGUI_ApiType", name)
                                                           ?? FromWrapper("RustVehicles_Plugin", name));

                case "rustvehicles":
                    return Cached("RustVehicles", () => FromWrapper("RustVehicles_Plugin", name)
                                                        ?? FromModInstance("RustVehicles_ApiType", name));

                case "leaderboard":
                case "ultimateleaderboard":
                    return Cached("Leaderboard", () => FromWrapper("Leaderboard_Plugin", name)
                                                       ?? FromWrapper("UltimateLeaderboard_Plugin", name)
                                                       ?? FromModInstance("Leaderboard_ApiType", name));

                case "raidablebasesui":
                case "raidablebasesbuyableui":
                    return Cached("RaidableBasesUI", () => FromWrapper("RaidableBasesBuyableUI_Plugin", name)
                                                           ?? (GetDomain("RaidableBasesBuyableUI_ApiType") is Type rb
                                                               ? new PluginBridgeApi(rb) { Name = name }
                                                               : null));

                case "economics":
                    return Cached("Economics", () => FromWrapper("Economics_Plugin", "Economics"));

                case "skilltree":
                    return Cached("SkillTree", () => FromWrapper("SkillTree_Plugin", "SkillTree"));

                case "backpacks":
                    return Cached("Backpacks", () => FromWrapper("Backpacks_Plugin", "Backpacks"));

                case "playtimetracker":
                    return Cached("PlaytimeTracker", () => FromWrapper("PlaytimeTracker_Plugin", "PlaytimeTracker"));
            }

            // Generic fallback: <Name>_Plugin then <Name>_ApiType.
            return Cached(name, () =>
            {
                var wrapper = FromWrapper(name + "_Plugin", name);
                if (wrapper != null) return wrapper;
                return GetDomain(name + "_ApiType") is Type t ? new PluginBridgeApi(t) { Name = name } : null;
            });
        }
    }

    public class ServerHelper
    {
        public void Command(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            try
            {
                if (!TrySplitCommandLine(command.Trim(), out string cmdName, out string[] args))
                {
                    ConsoleSystem.Run(ConsoleSystem.Option.Server, command.Trim());
                    return;
                }

                if (args == null || args.Length == 0)
                    ConsoleSystem.Run(ConsoleSystem.Option.Server, cmdName);
                else
                    ConsoleSystem.Run(ConsoleSystem.Option.Server, cmdName, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerPanel] Server.Command: " + ex.Message);
            }
        }

        internal static bool TrySplitCommandLine(string line, out string cmdName, out string[] args)
        {
            cmdName = null;
            args = null;
            if (string.IsNullOrWhiteSpace(line)) return false;

            var tokens = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0)
                    {
                        tokens.Add(sb.ToString());
                        sb.Clear();
                    }

                    continue;
                }

                sb.Append(c);
            }

            if (sb.Length > 0)
                tokens.Add(sb.ToString());

            if (tokens.Count == 0) return false;
            cmdName = tokens[0];
            if (tokens.Count == 1)
            {
                args = Array.Empty<string>();
                return true;
            }

            args = new string[tokens.Count - 1];
            for (int i = 1; i < tokens.Count; i++)
                args[i - 1] = tokens[i];
            return true;
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
            set => AsObject()[key] =
                value == null ? JValue.CreateNull() : JToken.FromObject(value, JsonSerializer.Create(Settings));
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

        public T Get<T>(params string[] keys)
        {
            var obj = Get(keys);
            if (obj == null) return default;
            if (obj is T direct) return direct;
            try { return (T)Convert.ChangeType(obj, typeof(T)); }
            catch
            {
                try { return JToken.FromObject(obj).ToObject<T>(); }
                catch { return default; }
            }
        }

        public string Filename => _path ?? "";

        public bool Exists() => !string.IsNullOrEmpty(_path) && File.Exists(_path);

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
                Debug.LogWarning("[ServerPanel] ReadObject<" + typeof(T).Name + ">: " + ex.Message);
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

    /// <summary>Data root = HarmonyData so Oxide paths like ServerPanel/Categories resolve correctly.</summary>
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

        public string Root => _root;

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

        public T ReadObject<T>(string relativePath)
        {
            var path = ResolvePath(relativePath);
            if (!File.Exists(path))
                return default;
            _cache.Remove(path);
            var file = GetFile(relativePath);
            try
            {
                var raw = file.Raw;
                if (raw == null || raw.Type == JTokenType.Null) return default;
                return raw.ToObject<T>(JsonSerializer.Create(file.Settings));
            }
            catch (Exception ex)
            {
                Debug.LogError("[ServerPanel] Failed to read " + path + ": " + ex.Message);
                return default;
            }
        }

        public void WriteObject<T>(string relativePath, T obj)
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
        public string DataDirectory => ServerPanelHost.Instance?.DataDirectory ?? "";
        public string RootDirectory => ServerPanelHost.Instance?.ServerRoot ?? "";
        public string LangDirectory => ServerPanelHost.Instance?.LangDirectory ?? "HarmonyLanguage";

        public void LogError(string message) => Debug.LogError("[ServerPanel] " + message);

        public object CallHook(string name, params object[] args) => Interface.CallHook(name, args);

        public void NextTick(Action action) => Interface.NextTick(action);

        public T GetLibrary<T>() where T : class
        {
            if (typeof(T) == typeof(HarmonyPermissionHelper))
                return ServerPanelHost.Instance?.Permission as T;
            return null;
        }

        public void ReloadPlugin(string name) =>
            Debug.LogWarning("[ServerPanel] ReloadPlugin is a no-op under Harmony (use harmony.load ServerPanel).");
    }

    public static class Interface
    {
        public static DataFileSystem DataFileSystem { get; set; }
        public static OxideStub Oxide { get; } = new OxideStub();

        /// <summary>
        /// Oxide broadcast hooks are not available under Harmony. Forward the small set of
        /// ServerPanel consumer hooks to the Harmony mods that implement them.
        /// </summary>
        public static object CallHook(string name, params object[] args)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var host = ServerPanelHost.Instance;
            if (host == null) return null;

            switch (name)
            {
                case "OnServerPanelClosed":
                case "OnServerPanelCategoryPage":
                    return BroadcastToConsumers(host, name, args);
                case "SendNotify":
                    return null;
                default:
                    return null;
            }
        }

        private static readonly string[] ConsumerPlugins =
        {
            "Shop", "Kits", "WipeSchedule", "RustVehicles", "RustVehiclesGUI", "Leaderboard", "RaidableBasesUI"
        };

        private static object BroadcastToConsumers(ServerPanelHost host, string name, object[] args)
        {
            object result = null;
            for (int i = 0; i < ConsumerPlugins.Length; i++)
            {
                try
                {
                    var plugin = host.Plugins.Find(ConsumerPlugins[i]);
                    if (plugin is not { IsLoaded: true }) continue;
                    // Expand args into params so bridges never receive a nested object[] as one argument.
                    object value = args == null || args.Length == 0
                        ? plugin.Call(name)
                        : plugin.Call(name, args);
                    // Oxide cancel convention: only an explicit non-null cancel token blocks the switch.
                    // Ignore void/null returns; do not treat accidental containers/strings as cancel.
                    if (value is bool b)
                        result ??= b ? (object)true : null;
                    else if (value != null && value is not string && value.GetType().Name.IndexOf("Cui", StringComparison.Ordinal) < 0)
                        result ??= value;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[ServerPanel] " + name + " -> " + ConsumerPlugins[i] + ": " + ex.Message);
                }
            }

            return result;
        }

        public static void NextTick(Action action)
        {
            try { ServerMgr.Instance?.StartCoroutine(NextTickCoroutine(action)); }
            catch { }
        }

        private static IEnumerator NextTickCoroutine(Action action)
        {
            yield return null;
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[ServerPanel] NextTick: " + ex.Message); }
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
                                if (kv.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) &&
                                    req.Content != null)
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
                        catch (Exception ex) { Debug.LogWarning("[ServerPanel] webrequest callback: " + ex.Message); }
                    });
                }
                catch { }
            });
        }
    }

    #endregion

    #region ImageLibrary replacement

    /// <summary>
    /// Stand-in for the Oxide ImageLibrary plugin. Downloads images (or reads them from
    /// HarmonyData / HarmonyImages) and stores them in FileStorage so CUI can use raw PNG ids.
    /// Exposes the same AddImage / GetImage / HasImage / ImportImageList call surface, so the
    /// ported plugin code needs no changes and _enabledImageLibrary stays true.
    /// </summary>
    public sealed class ImageLibraryBridge : Plugin
    {
        private readonly Dictionary<string, string> _stored =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly HashSet<string> _loading = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<KeyValuePair<string, string>> _pending = new Queue<KeyValuePair<string, string>>();
        private bool _pumpRunning;
        private int _failures;

        public ImageLibraryBridge()
        {
            Name = "ImageLibrary";
            Title = "Image Library";
            Version = new VersionNumber(2, 0, 0);
        }

        public override bool IsLoaded
        {
            get => true;
            set { }
        }

        public int StoredCount => _stored.Count;
        public int PendingCount => _pending.Count + _loading.Count;

        public override object Call(string method, params object[] args)
        {
            switch (method)
            {
                case "AddImage":
                    if (args != null && args.Length >= 2)
                        AddImage(args[0] as string, args[1] as string);
                    return true;

                case "GetImage":
                    return args != null && args.Length >= 1 ? GetImage(args[0] as string) : string.Empty;

                case "HasImage":
                    return args != null && args.Length >= 1 && HasImage(args[0] as string);

                case "ImportImageList":
                    if (args != null && args.Length >= 2 && args[1] is Dictionary<string, string> list)
                        ImportImageList(list);
                    return true;

                case "IsReady":
                    return true;

                default:
                    return null;
            }
        }

        public void ImportImageList(Dictionary<string, string> images)
        {
            if (images == null) return;
            foreach (var kv in images)
                AddImage(kv.Value, kv.Key);
        }

        public void AddImage(string url, string fileName)
        {
            var key = string.IsNullOrEmpty(fileName) ? url : fileName;
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(url)) return;
            if (_stored.ContainsKey(key) || _loading.Contains(key)) return;

            _loading.Add(key);
            _pending.Enqueue(new KeyValuePair<string, string>(key, url));
            StartPump();
        }

        public string GetImage(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            if (_stored.TryGetValue(name, out var id)) return id;
            if (!_loading.Contains(name) && (name.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                                             name.StartsWith("TheMevent", StringComparison.Ordinal)))
                AddImage(name, name);
            return string.Empty;
        }

        public bool HasImage(string name) => !string.IsNullOrEmpty(name) && _stored.ContainsKey(name);

        public void Register(string name, string crc)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(crc)) return;
            _stored[name] = crc;
        }

        public void Clear()
        {
            _stored.Clear();
            _loading.Clear();
            _pending.Clear();
        }

        private void StartPump()
        {
            if (_pumpRunning) return;
            _pumpRunning = true;
            try { ServerMgr.Instance?.StartCoroutine(Pump()); }
            catch { _pumpRunning = false; }
        }

        private IEnumerator Pump()
        {
            // Wait for CommunityEntity so FileStorage.Store has a valid net id.
            while (CommunityEntity.ServerInstance == null || CommunityEntity.ServerInstance.net == null)
                yield return CoroutineEx.waitForSeconds(1f);

            int inFlight = 0;
            while (_pending.Count > 0)
            {
                var entry = _pending.Dequeue();
                yield return Download(entry.Key, entry.Value);
                if (++inFlight % 10 == 0)
                    yield return null;
            }

            _pumpRunning = false;
            if (_pending.Count > 0) StartPump();
        }

        private IEnumerator Download(string key, string url)
        {
            string target = ResolveSource(url);

            using (var www = UnityWebRequestTexture.GetTexture(target))
            {
                www.timeout = 30;
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    _loading.Remove(key);
                    if (_failures++ < 15)
                        Debug.LogWarning("[ServerPanel] Image failed (" + www.error + "): " + target);
                    yield break;
                }

                Texture2D texture = null;
                try
                {
                    texture = DownloadHandlerTexture.GetContent(www);
                    var bytes = texture.EncodeToPNG();
                    var crc = FileStorage.server
                        .Store(bytes, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID).ToString();
                    _stored[key] = crc;
                    if (!string.Equals(key, url, StringComparison.Ordinal))
                        _stored[url] = crc;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[ServerPanel] Image store failed for " + key + ": " + ex.Message);
                }
                finally
                {
                    if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                    _loading.Remove(key);
                }
            }
        }

        /// <summary>Offline paths (TheMevent/...) resolve against HarmonyData then HarmonyImages.</summary>
        private static string ResolveSource(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return url;

            var host = ServerPanelHost.Instance;
            if (host == null) return url;

            var relative = url.Replace('/', Path.DirectorySeparatorChar);
            var candidates = new[]
            {
                Path.Combine(host.DataDirectory, relative),
                Path.Combine(host.ImageDirectory, relative),
                Path.Combine(host.ImageDirectory, Path.Combine("ServerPanel", relative))
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return "file://" + candidate;
            }

            return "file://" + candidates[0];
        }
    }

    #endregion

    #region Host

    public class ServerPanelHost
    {
        public static ServerPanelHost Instance { get; private set; }

        public HarmonyPermissionHelper Permission { get; } = new HarmonyPermissionHelper();
        public HarmonyTimerRunner Timer { get; } = new HarmonyTimerRunner();
        public PlayersHelper Players { get; } = new PlayersHelper();
        public PluginsHelper Plugins { get; } = new PluginsHelper();
        public ServerHelper Server { get; } = new ServerHelper();
        public CovalenceHelper Covalence { get; } = new CovalenceHelper();
        public WebRequestHelper Webrequest { get; } = new WebRequestHelper();
        public ImageLibraryBridge ImageLibrary { get; } = new ImageLibraryBridge();

        private readonly Dictionary<string, DynamicConfigFile> _configs =
            new Dictionary<string, DynamicConfigFile>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, LangHelper> _langs =
            new Dictionary<string, LangHelper>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Live ServerPanel plugin object (set by the mod entry point).</summary>
        public object Panel { get; set; }

        /// <summary>Live ServerPanelPopUps plugin object (set by the mod entry point).</summary>
        public object PopUps { get; set; }

        public string ServerRoot { get; private set; }
        public string DataDirectory { get; private set; }
        public string ConfigDirectory { get; private set; }
        public string LangDirectory { get; private set; }
        public string ImageDirectory { get; private set; }

        public static void Init(string serverRoot)
        {
            Instance = new ServerPanelHost();
            Instance.ServerRoot = serverRoot;

            var dataDir = Path.Combine(serverRoot, "HarmonyData");
            var configDir = Path.Combine(serverRoot, "HarmonyConfig");
            var langDir = Path.Combine(serverRoot, "HarmonyLanguage");
            var imageDir = Path.Combine(serverRoot, "HarmonyImages");

            foreach (var dir in new[] { dataDir, configDir, langDir, imageDir })
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

            Directory.CreateDirectory(Path.Combine(dataDir, "ServerPanel"));
            Directory.CreateDirectory(Path.Combine(dataDir, "ServerPanelPopUps"));

            Instance.DataDirectory = dataDir;
            Instance.ConfigDirectory = configDir;
            Instance.LangDirectory = langDir;
            Instance.ImageDirectory = imageDir;

            Interface.DataFileSystem = new DataFileSystem(dataDir);

            Debug.Log("[ServerPanel] Config dir: " + configDir);
            Debug.Log("[ServerPanel] Data dir:   " + Path.Combine(dataDir, "ServerPanel"));
        }

        public DynamicConfigFile GetConfig(string pluginName)
        {
            if (string.IsNullOrEmpty(pluginName)) pluginName = "ServerPanel";
            if (_configs.TryGetValue(pluginName, out var cached)) return cached;

            var path = Path.Combine(ConfigDirectory ?? "HarmonyConfig", pluginName + ".json");
            JToken data = null;
            if (File.Exists(path))
            {
                try { data = JToken.Parse(File.ReadAllText(path)); }
                catch (Exception ex)
                {
                    Debug.LogError("[ServerPanel] Config parse failed for " + path + ": " + ex.Message);
                    data = null;
                }
            }
            else
            {
                Debug.LogWarning("[ServerPanel] Config not found, defaults will be written: " + path);
            }

            var file = new DynamicConfigFile(path, data ?? new JObject());
            _configs[pluginName] = file;
            return file;
        }

        public LangHelper GetLang(string pluginName)
        {
            if (string.IsNullOrEmpty(pluginName)) pluginName = "ServerPanel";
            if (_langs.TryGetValue(pluginName, out var cached)) return cached;
            var helper = new LangHelper(pluginName);
            _langs[pluginName] = helper;
            return helper;
        }

        public static void Shutdown()
        {
            Instance?.Timer?.DestroyAll();
            Instance?.Plugins?.Clear();
            Instance = null;
        }

        public void Puts(string message) => Debug.Log("[ServerPanel] " + message);

        public void Puts(string format, params object[] args) =>
            Debug.Log("[ServerPanel] " + (args == null || args.Length == 0 ? format : string.Format(format, args)));

        public void PrintWarning(string message) => Debug.LogWarning("[ServerPanel] " + message);

        public void PrintWarning(string format, params object[] args) =>
            Debug.LogWarning("[ServerPanel] " +
                             (args == null || args.Length == 0 ? format : string.Format(format, args)));

        public void PrintError(string message) => Debug.LogError("[ServerPanel] " + message);

        public void PrintError(string format, params object[] args) =>
            Debug.LogError("[ServerPanel] " +
                           (args == null || args.Length == 0 ? format : string.Format(format, args)));

        public void LogToFile(string filename, string text, object plugin, bool timestamp = true)
        {
            try
            {
                var dir = Path.Combine(DataDirectory, "ServerPanel");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, (filename ?? "ServerPanel") + ".txt");
                var line = (timestamp ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] " : "") + text + Environment.NewLine;
                File.AppendAllText(path, line);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerPanel] LogToFile: " + ex.Message);
            }
        }
    }

    #endregion

    #region Plugin base

    public abstract class ServerPanelPluginBase
    {
        public abstract string Name { get; }
        public virtual string Title => Name;
        public VersionNumber Version { get; protected set; } = new VersionNumber(2, 0, 20);
        public bool IsLoaded { get; set; } = true;

        protected ServerPanelHost Host => ServerPanelHost.Instance;
        protected HarmonyPermissionHelper permission => Host?.Permission;
        protected HarmonyTimerRunner timer => Host?.Timer;
        protected LangHelper lang => Host?.GetLang(Name);
        protected CovalenceHelper covalence => Host?.Covalence;
        protected WebRequestHelper webrequest => Host?.Webrequest;
        protected PluginsHelper plugins => Host?.Plugins;
        protected ServerHelper Server => Host?.Server;
        protected DynamicConfigFile Config => Host?.GetConfig(Name);

        internal CommandLib cmd = new CommandLib();

        private readonly List<(string[] commands, string methodName)> _covalenceCommands =
            new List<(string[], string)>();

        internal IReadOnlyList<(string[] commands, string methodName)> RegisteredCovalenceCommands =>
            _covalenceCommands;

        protected void Puts(string message) => Host?.Puts(message);
        protected void Puts(string format, params object[] args) => Host?.Puts(format, args);
        protected void PrintWarning(string message) => Host?.PrintWarning(message);
        protected void PrintWarning(string format, params object[] args) => Host?.PrintWarning(format, args);
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
            catch (Exception ex) { Debug.LogWarning("[ServerPanel] NextTick: " + ex.Message); }
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
                Debug.Log("[ServerPanel] " + message);
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

        protected void AddCovalenceCommand(string command, string methodName)
        {
            if (string.IsNullOrEmpty(command) || string.IsNullOrEmpty(methodName)) return;
            _covalenceCommands.Add((new[] { command }, methodName));
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

    #region Formatter

    /// <summary>
    /// Oxide Covalence Formatter shim. The ported code only uses ToPlaintext to strip markup before
    /// measuring / escaping label text.
    /// </summary>
    public static class Formatter
    {
        private static readonly System.Text.RegularExpressions.Regex Tags =
            new System.Text.RegularExpressions.Regex(@"\[/?(#[0-9a-fA-F]{3,8}|\+[a-z]+|[a-z]+)\]",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        public static string ToPlaintext(string text) =>
            string.IsNullOrEmpty(text) ? text : Tags.Replace(text, string.Empty);

        public static string ToUnity(string text) => text;
        public static string ToRustLegacy(string text) => ToPlaintext(text);
    }

    #endregion

    #region Cross-assembly CUI helpers

    /// <summary>
    /// Plugin pages call another Harmony mod's API_OpenPlugin. That mod returns a CuiElementContainer
    /// built from its own copy of the CUI types, so a direct type check fails. Serialize whatever we
    /// get back into the JSON element list ServerPanel expects.
    /// </summary>
    public static class ForeignCui
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            DefaultValueHandling = DefaultValueHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            FloatFormatHandling = FloatFormatHandling.Symbol,
            StringEscapeHandling = StringEscapeHandling.Default
        };

        public static string ToElementsJson(object value)
        {
            if (value == null) return null;
            if (value is string s) return string.IsNullOrWhiteSpace(s) ? null : StripBrackets(s);

            // Prefer instance ToJson/ToString (each mod's CuiElementContainer already wires CuiHelper).
            try
            {
                var type = value.GetType();
                var toJsonInst = type.GetMethod("ToJson", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                if (toJsonInst != null)
                {
                    var json = toJsonInst.Invoke(value, null) as string;
                    if (!string.IsNullOrWhiteSpace(json) && json != "[]") return StripBrackets(json);
                }

                // List/container ToString often delegates to CuiHelper.ToJson.
                if (value is System.Collections.ICollection coll && coll.Count > 0)
                {
                    var asText = value.ToString();
                    if (!string.IsNullOrWhiteSpace(asText) && asText[0] == '[')
                        return StripBrackets(asText);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerPanel] Plugin page ToJson/ToString failed: " +
                                 (ex.InnerException?.Message ?? ex.Message));
            }

            // Prefer the owning assembly's own CuiHelper so component converters match.
            try
            {
                var owner = value.GetType().Assembly;
                var helper = owner.GetType("Oxide.Game.Rust.Cui.CuiHelper");
                var toJson = helper?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "ToJson" && m.GetParameters().Length >= 1);
                if (toJson != null)
                {
                    object[] invokeArgs = toJson.GetParameters().Length >= 2
                        ? new[] { value, (object)false }
                        : new[] { value };
                    var json = toJson.Invoke(null, invokeArgs) as string;
                    if (!string.IsNullOrWhiteSpace(json) && json != "[]") return StripBrackets(json);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerPanel] Plugin page CuiHelper.ToJson failed: " +
                                 (ex.InnerException?.Message ?? ex.Message));
            }

            // Do NOT JsonConvert.SerializeObject CUI containers — that emits CLR property dumps,
            // not CommunityEntity AddUI JSON, and the client silently shows nothing.
            Debug.LogWarning("[ServerPanel] Plugin page returned " + (value.GetType().FullName ?? "unknown") +
                             " that could not be serialized to CUI JSON");
            return null;
        }

        private static string StripBrackets(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;
            json = json.Trim();
            if (json.Length >= 2 && json[0] == '[' && json[json.Length - 1] == ']')
                json = json.Substring(1, json.Length - 2);
            return string.IsNullOrWhiteSpace(json) ? null : json;
        }
    }

    #endregion
}
