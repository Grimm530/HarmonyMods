// OxideCompat.cs  --  Oxide -> Harmony shims for the TruePVE port.
// Provides the Oxide.Core / Oxide.Core.Configuration / Oxide.Core.Libraries /
// Oxide.Core.Libraries.Covalence / Oxide.Core.Plugins / Oxide.Plugins namespaces so
// TruePVEPlugin.cs compiles without modification.
// No Oxide runtime is required.  All paths resolve under HarmonyConfig / HarmonyData.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
// Bring Oxide.Core types (VersionNumber etc.) into scope for Oxide.Plugins members below.
using Oxide.Core;

// ---------------------------------------------------------------------------
// Oxide.Core.Configuration  (DynamicConfigFile primary definition)
// ---------------------------------------------------------------------------
namespace Oxide.Core.Configuration
{
    /// <summary>
    /// Lightweight DynamicConfigFile backed by JSON.  Used by RustPlugin.Config and by
    /// Interface.Oxide.DataFileSystem.GetFile(name) for the PCDDATA player-data handle.
    /// </summary>
    public class DynamicConfigFile
    {
        private JToken _data;
        public string Filename { get; set; }

        public DynamicConfigFile() { _data = new JObject(); }
        public DynamicConfigFile(string filename, JToken data = null)
        {
            Filename = filename;
            _data = data ?? new JObject();
        }

        public void Clear() => _data = new JObject();

        private JObject AsObject()
        {
            if (_data is JObject o) return o;
            _data = new JObject();
            return (JObject)_data;
        }

        public object this[string key]
        {
            get => ConvertToken(AsObject()[key]);
            set => AsObject()[key] = value == null ? JValue.CreateNull() : JToken.FromObject(value);
        }

        public object Get(params string[] keys)
        {
            if (keys == null || keys.Length == 0) return null;
            JToken t = _data;
            foreach (var k in keys)
            {
                if (t == null) return null;
                t = t[k];
            }
            return ConvertToken(t);
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

        public bool Exists() => !string.IsNullOrEmpty(Filename) && File.Exists(Filename);

        public void Load()
        {
            if (string.IsNullOrEmpty(Filename) || !File.Exists(Filename)) return;
            try { _data = JToken.Parse(File.ReadAllText(Filename)); }
            catch { _data = new JObject(); }
        }

        public T ReadObject<T>()
        {
            try
            {
                if (_data == null || _data.Type == JTokenType.Null)
                    return typeof(T).IsClass ? Activator.CreateInstance<T>() : default;
                return _data.ToObject<T>() ?? Activator.CreateInstance<T>();
            }
            catch { return typeof(T).IsClass ? Activator.CreateInstance<T>() : default; }
        }

        public void WriteObject<T>(T obj, bool sync = false)
        {
            if (obj == null) return;
            _data = JToken.FromObject(obj, JsonSerializer.Create(new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            }));
            if (sync) Save();
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(Filename)) return;
            try
            {
                var dir = Path.GetDirectoryName(Filename);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(Filename, (_data ?? new JObject()).ToString(Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TruePVE] DynamicConfigFile.Save: " + ex.Message);
            }
        }

        internal JToken Raw => _data;

        private static object ConvertToken(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return null;
            switch (t.Type)
            {
                case JTokenType.Object:
                    var d = new Dictionary<string, object>();
                    foreach (var p in ((JObject)t).Properties()) d[p.Name] = ConvertToken(p.Value);
                    return d;
                case JTokenType.Array:
                    var l = new List<object>();
                    foreach (var i in (JArray)t) l.Add(ConvertToken(i));
                    return l;
                case JTokenType.Integer:
                    var lng = t.ToObject<long>();
                    return lng >= int.MinValue && lng <= int.MaxValue ? (object)(int)lng : lng;
                case JTokenType.Float: return t.ToObject<double>();
                case JTokenType.Boolean: return t.ToObject<bool>();
                case JTokenType.String: return t.ToObject<string>();
                default: return ((JValue)t).Value;
            }
        }
    }
}

// ---------------------------------------------------------------------------
// Oxide.Core  (VersionNumber, Interface, OxideMod, DataFileSystem)
// ---------------------------------------------------------------------------
namespace Oxide.Core
{
    using Oxide.Core.Configuration;

    public struct VersionNumber : IEquatable<VersionNumber>, IComparable<VersionNumber>
    {
        public int Major, Minor, Patch;
        public VersionNumber(int major, int minor, int patch) { Major = major; Minor = minor; Patch = patch; }
        public bool Equals(VersionNumber o) => Major == o.Major && Minor == o.Minor && Patch == o.Patch;
        public override bool Equals(object obj) => obj is VersionNumber vn && Equals(vn);
        public override int GetHashCode() => (Major * 397 ^ Minor) * 397 ^ Patch;
        public int CompareTo(VersionNumber o)
        {
            int c = Major.CompareTo(o.Major); if (c != 0) return c;
            c = Minor.CompareTo(o.Minor); if (c != 0) return c;
            return Patch.CompareTo(o.Patch);
        }
        public static bool operator ==(VersionNumber a, VersionNumber b) => a.Equals(b);
        public static bool operator !=(VersionNumber a, VersionNumber b) => !a.Equals(b);
        public static bool operator <(VersionNumber a, VersionNumber b) => a.CompareTo(b) < 0;
        public static bool operator >(VersionNumber a, VersionNumber b) => a.CompareTo(b) > 0;
        public static bool operator <=(VersionNumber a, VersionNumber b) => a.CompareTo(b) <= 0;
        public static bool operator >=(VersionNumber a, VersionNumber b) => a.CompareTo(b) >= 0;
        public override string ToString() => $"{Major}.{Minor}.{Patch}";
    }

    public static class Interface
    {
        public static readonly OxideMod Oxide = new OxideMod();

        /// <summary>
        /// Harmony has no full Oxide hook bus. Most hooks return null ("not handled").
        /// CanEntityTakeDamage / CanEntityBeTargeted chain:
        /// 1) PveMode first (non-owner block; explicit allow for event NPCs/tanks/helis/turrets)
        /// 2) RaidableBases (allow damage inside active raid zones)
        /// 3) Convoy (allow damage to convoy entities / event turrets target players)
        /// 4) ArmoredTrain (allow damage to train turrets/sams; turrets hurt/target players)
        /// 5) AnimalSpawn (custom GrimmBoss animals vs NPCs/animals)
        /// 6) DefendableHomes (event GrimmNPCs can damage the defended base / players; turrets can target them)
        /// </summary>
        public static object CallHook(string hook, params object[] args)
        {
            if (string.IsNullOrEmpty(hook) || args == null || args.Length < 2)
                return null;

            if (hook == "CanEntityTakeDamage")
            {
                object pve = TryAppDomainBool("PveMode_CanEntityTakeDamage", args[0], args[1], "CanEntityTakeDamage PveMode");
                if (pve is bool) return pve;

                object rb = TryAppDomainBool("RaidableBases_CanEntityTakeDamage", args[0], args[1], "CanEntityTakeDamage RaidableBases");
                if (rb is bool) return rb;

                object convoy = TryAppDomainBool("Convoy_CanEntityTakeDamage", args[0], args[1], "CanEntityTakeDamage Convoy");
                if (convoy is bool) return convoy;

                object train = TryAppDomainBool("ArmoredTrain_CanEntityTakeDamage", args[0], args[1], "CanEntityTakeDamage ArmoredTrain");
                if (train is bool) return train;

                object animal = TryAppDomainBool("AnimalSpawn_CanEntityTakeDamage", args[0], args[1], "CanEntityTakeDamage AnimalSpawn");
                if (animal is bool) return animal;

                object homes = TryAppDomainBool("DefendableHomes_CanEntityTakeDamage", args[0], args[1], "CanEntityTakeDamage DefendableHomes");
                if (homes is bool) return homes;
            }
            else if (hook == "CanEntityBeTargeted")
            {
                // CallHook args: [target, attacker] (matches TruePVE OnEntityEnterInternal).
                object pve = TryAppDomainBool("PveMode_CanEntityBeTargeted", args[0], args[1], "CanEntityBeTargeted PveMode");
                if (pve is bool) return pve;

                object rb = TryAppDomainBool("RaidableBases_CanEntityBeTargeted", args[0], args[1], "CanEntityBeTargeted RaidableBases");
                if (rb is bool) return rb;

                object convoy = TryAppDomainBool("Convoy_CanEntityBeTargeted", args[0], args[1], "CanEntityBeTargeted Convoy");
                if (convoy is bool) return convoy;

                object train = TryAppDomainBool("ArmoredTrain_CanEntityBeTargeted", args[0], args[1], "CanEntityBeTargeted ArmoredTrain");
                if (train is bool) return train;

                object animal = TryAppDomainBool("AnimalSpawn_CanEntityBeTargeted", args[0], args[1], "CanEntityBeTargeted AnimalSpawn");
                if (animal is bool) return animal;

                object homes = TryAppDomainBool("DefendableHomes_CanEntityBeTargeted", args[0], args[1], "CanEntityBeTargeted DefendableHomes");
                if (homes is bool) return homes;
            }

            return null;
        }

        private static object TryAppDomainBool(string key, object arg0, object arg1, string logLabel)
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData(key) is Delegate fn)
                {
                    object result = fn.DynamicInvoke(arg0, arg1);
                    if (result is bool)
                        return result;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TruePVE] " + logLabel + " bridge: " + ex.Message);
            }
            return null;
        }

        public static object Call(string hook, params object[] args) => CallHook(hook, args);

        public static void NextTick(Action action)
        {
            try { ServerMgr.Instance?.StartCoroutine(NextTickCoroutine(action)); }
            catch { }
        }
        private static IEnumerator NextTickCoroutine(Action action)
        {
            yield return null;
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[TruePVE] NextTick: " + ex.Message); }
        }
    }

    public class OxideMod
    {
        private string _root;
        private DataFileSystem _dfs;

        public string RootDirectory
        {
            get
            {
                if (!string.IsNullOrEmpty(_root)) return _root;
                return _root = ResolveServerRoot();
            }
        }

        public string ConfigDirectory => Path.Combine(RootDirectory, "HarmonyConfig");
        public string DataDirectory   => Path.Combine(RootDirectory, "HarmonyData");
        public string LangDirectory   => Path.Combine(RootDirectory, "HarmonyLanguage");

        public DataFileSystem DataFileSystem => _dfs ??= new DataFileSystem(DataDirectory);

        public void LogInfo(string message) => Debug.Log("[TruePVE] " + message);
        // Format overload: caller (TruePVE.Puts) already supplies its own "[TruePVE]" tag.
        public void LogInfo(string format, params object[] args)
            => Debug.Log(SafeFormat(format, args));
        public void LogWarning(string message) => Debug.LogWarning("[TruePVE] " + message);
        public void LogWarning(string format, params object[] args)
            => Debug.LogWarning(SafeFormat(format, args));
        public void LogError(string message)   => Debug.LogError("[TruePVE] " + message);
        public void LogError(string format, params object[] args)
            => Debug.LogError(SafeFormat(format, args));

        private static string SafeFormat(string format, object[] args)
        {
            if (args == null || args.Length == 0) return format;
            try { return string.Format(format, args); } catch { return format; }
        }

        public void UnloadPlugin(string name)
            => Debug.LogWarning("[TruePVE] UnloadPlugin('" + name + "') ignored under Harmony.");
        public void ReloadPlugin(string name)
            => Debug.LogWarning("[TruePVE] ReloadPlugin('" + name + "') ignored under Harmony.");

        // Plugin.CallHook via Interface.Oxide — always return null (no inter-plugin bus).
        public object CallHook(string hook, params object[] args) => null;
        public T CallHook<T>(string hook, params object[] args) => default;

        internal static string ResolveServerRoot()
        {
            try
            {
                if (!string.IsNullOrEmpty(Application.dataPath))
                {
                    string p = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    if (LooksLikeRoot(p)) return p;
                }
            }
            catch { }
            string cur = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory ?? ".");
            for (int i = 0; i < 6; i++)
            {
                if (LooksLikeRoot(cur)) return cur;
                string parent = Path.GetDirectoryName(cur);
                if (string.IsNullOrEmpty(parent) || parent == cur) break;
                cur = parent;
            }
            return cur;
        }

        private static bool LooksLikeRoot(string d)
        {
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) return false;
            return Directory.Exists(Path.Combine(d, "HarmonyConfig"))
                || Directory.Exists(Path.Combine(d, "HarmonyData"))
                || Directory.Exists(Path.Combine(d, "HarmonyMods"))
                || File.Exists(Path.Combine(d, "RustDedicated.exe"));
        }
    }

    /// <summary>
    /// DataFileSystem whose root is HarmonyData/.
    /// GetFile(name) → DynamicConfigFile at HarmonyData/{name}.json.
    /// ReadObject/WriteObject mirror the Oxide API used by TruePVE for PCDDATA.
    /// </summary>
    public class DataFileSystem
    {
        private readonly string _root;
        private readonly Dictionary<string, DynamicConfigFile> _cache =
            new Dictionary<string, DynamicConfigFile>(StringComparer.OrdinalIgnoreCase);

        public DataFileSystem(string root)
        {
            _root = root;
            if (!Directory.Exists(_root)) Directory.CreateDirectory(_root);
        }

        private string Resolve(string name)
        {
            name = (name ?? "").Replace('\\', '/').Trim('/');
            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 5);
            var segs = name.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length == 0) return Path.Combine(_root, "data.json");
            var dir = _root;
            for (int i = 0; i < segs.Length - 1; i++)
            {
                dir = Path.Combine(dir, segs[i]);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            return Path.Combine(dir, segs[segs.Length - 1] + ".json");
        }

        public DynamicConfigFile GetFile(string name)
        {
            var path = Resolve(name);
            if (_cache.TryGetValue(path, out var cached)) return cached;
            JToken data = new JObject();
            if (File.Exists(path))
            {
                try { data = JToken.Parse(File.ReadAllText(path)); }
                catch { data = new JObject(); }
            }
            var f = new DynamicConfigFile(path, data);
            _cache[path] = f;
            return f;
        }

        public DynamicConfigFile GetDatafile(string name) => GetFile(name);

        public bool ExistsDatafile(string name) => File.Exists(Resolve(name));

        public T ReadObject<T>(string name)
        {
            var path = Resolve(name);
            _cache.Remove(path);
            if (!File.Exists(path)) return default;
            try { return JsonConvert.DeserializeObject<T>(File.ReadAllText(path)); }
            catch { return default; }
        }

        public void WriteObject<T>(string name, T obj, bool sync = false)
        {
            var path = Resolve(name);
            _cache.Remove(path);
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(obj, Formatting.Indented));
            }
            catch (Exception ex) { Debug.LogWarning("[TruePVE] WriteObject: " + ex.Message); }
        }

        public void SaveDatafile(string name)
        {
            var path = Resolve(name);
            if (_cache.TryGetValue(path, out var f)) f.Save();
        }

        public string[] GetFiles(string relative)
        {
            relative = (relative ?? "").Replace('\\', '/').Trim('/');
            var dir = string.IsNullOrEmpty(relative) ? _root : Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
            return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json") : Array.Empty<string>();
        }

        public void DeleteDataFile(string name)
        {
            var path = Resolve(name);
            _cache.Remove(path);
            if (File.Exists(path)) { try { File.Delete(path); } catch { } }
        }
    }
}

// ---------------------------------------------------------------------------
// Oxide.Core.Libraries  (Lang, Timer)
// ---------------------------------------------------------------------------
namespace Oxide.Core.Libraries
{
    public class Lang
    {
        private readonly Dictionary<string, Dictionary<string, string>> _msgs =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public void RegisterMessages(Dictionary<string, string> messages, object plugin, string lang = "en")
        {
            if (messages == null) return;
            if (!_msgs.TryGetValue(lang, out var map)) _msgs[lang] = map = new Dictionary<string, string>();
            foreach (var kv in messages) map[kv.Key] = kv.Value;
        }

        public string GetMessage(string key, object plugin = null, string userId = null)
        {
            if (string.IsNullOrEmpty(key)) return key;
            if (_msgs.TryGetValue("en", out var en) && en.TryGetValue(key, out var msg)) return msg;
            foreach (var map in _msgs.Values)
                if (map.TryGetValue(key, out var any)) return any;
            return key;
        }

        public string[] GetLanguages(object plugin = null) => new[] { "en" };
    }

    /// <summary>Cancellable timer handle.</summary>
    public class Timer
    {
        public bool Destroyed { get; private set; }
        public void Destroy() => Destroyed = true;
        // Oxide Timer.Reset restarts the timer; the coroutine-backed shim cannot re-arm,
        // so this is a no-op that simply keeps the handle alive. (Minor parity gap.)
        public void Reset(float delay = -1f, int number = 1) { }
    }

    /// <summary>HTTP method enum used by WebRequests.Enqueue.</summary>
    public enum RequestMethod
    {
        GET,
        POST,
        PUT,
        PATCH,
        DELETE,
    }
}

// ---------------------------------------------------------------------------
// Oxide.Core.Libraries.Covalence
// ---------------------------------------------------------------------------
namespace Oxide.Core.Libraries.Covalence
{
    public interface IPlayer
    {
        string Id { get; }
        string Name { get; }
        object Object { get; }
        bool IsAdmin { get; }
        bool IsConnected { get; }
        bool IsServer { get; }
        void Reply(string message);
        void Message(string message);
        bool HasPermission(string perm);
    }

    public class BasePlayerWrapper : IPlayer
    {
        private readonly BasePlayer _p;
        public BasePlayerWrapper(BasePlayer p) => _p = p;
        public string Id => _p?.UserIDString ?? "0";
        public string Name => _p?.displayName ?? "";
        public object Object => _p;
        public bool IsAdmin => _p != null && _p.IsAdmin;
        public bool IsConnected => _p != null && _p.IsConnected;
        public bool IsServer => false;
        public void Reply(string message)
        {
            if (_p == null || !_p.IsConnected || _p.net?.connection == null) return;
            try { ConsoleNetwork.SendClientCommand(_p.net.connection, "chat.add", 0, 0, message ?? ""); }
            catch { _p?.ChatMessage(message); }
        }
        public void Message(string message) => Reply(message);
        public bool HasPermission(string perm)
        {
            if (_p == null) return false;
            return TruePVEHarmony.PermissionsBridge.UserHasPermission(_p.UserIDString, perm);
        }
    }

    public class RustConsolePlayer : IPlayer
    {
        public string Id => "0";
        public string Name => "Server";
        public object Object => null;
        public bool IsAdmin => true;
        public bool IsConnected => true;
        public bool IsServer => true;
        public void Reply(string msg) => Debug.Log("[TruePVE] " + msg);
        public void Message(string msg) => Reply(msg);
        public bool HasPermission(string perm) => true;
    }

    /// <summary>Covalence player-manager surface used by TruePVE (covalence.Players.*).</summary>
    public interface IPlayerManager
    {
        IPlayer FindPlayer(string partialNameOrId);
        IPlayer FindPlayerById(string id);
        IEnumerable<IPlayer> FindPlayers(string partialNameOrId);
        IEnumerable<IPlayer> Connected { get; }
        IEnumerable<IPlayer> All { get; }
    }

    /// <summary>
    /// Scans BasePlayer.activePlayerList + sleeping players to resolve covalence IPlayer lookups.
    /// </summary>
    public class RustPlayerManager : IPlayerManager
    {
        private static IEnumerable<BasePlayer> AllBasePlayers()
        {
            var seen = new HashSet<ulong>();
            var active = BasePlayer.activePlayerList;
            if (active != null)
                for (int i = 0; i < active.Count; i++)
                {
                    var p = active[i];
                    if (p != null && seen.Add(p.userID)) yield return p;
                }
            var sleepers = BasePlayer.sleepingPlayerList;
            if (sleepers != null)
                for (int i = 0; i < sleepers.Count; i++)
                {
                    var p = sleepers[i];
                    if (p != null && seen.Add(p.userID)) yield return p;
                }
        }

        public IEnumerable<IPlayer> All
        {
            get { foreach (var p in AllBasePlayers()) yield return new BasePlayerWrapper(p); }
        }

        public IEnumerable<IPlayer> Connected
        {
            get
            {
                var active = BasePlayer.activePlayerList;
                if (active == null) yield break;
                for (int i = 0; i < active.Count; i++)
                {
                    var p = active[i];
                    if (p != null && p.IsConnected) yield return new BasePlayerWrapper(p);
                }
            }
        }

        public IPlayer FindPlayerById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var p in AllBasePlayers())
                if (p.UserIDString == id) return new BasePlayerWrapper(p);
            return null;
        }

        public IPlayer FindPlayer(string partialNameOrId)
        {
            if (string.IsNullOrEmpty(partialNameOrId)) return null;
            var byId = FindPlayerById(partialNameOrId);
            if (byId != null) return byId;
            BasePlayer match = null;
            int matches = 0;
            foreach (var p in AllBasePlayers())
            {
                if (p.displayName != null &&
                    p.displayName.IndexOf(partialNameOrId, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (string.Equals(p.displayName, partialNameOrId, StringComparison.OrdinalIgnoreCase))
                        return new BasePlayerWrapper(p);
                    match = p; matches++;
                }
            }
            return matches == 1 ? new BasePlayerWrapper(match) : null;
        }

        public IEnumerable<IPlayer> FindPlayers(string partialNameOrId)
        {
            var results = new List<IPlayer>();
            if (string.IsNullOrEmpty(partialNameOrId)) return results;
            foreach (var p in AllBasePlayers())
            {
                if (p.UserIDString == partialNameOrId ||
                    (p.displayName != null && p.displayName.IndexOf(partialNameOrId, StringComparison.OrdinalIgnoreCase) >= 0))
                    results.Add(new BasePlayerWrapper(p));
            }
            return results;
        }
    }

    /// <summary>Covalence library stand-in (covalence.Players).</summary>
    public class Covalence
    {
        public IPlayerManager Players { get; } = new RustPlayerManager();
    }
}

// ---------------------------------------------------------------------------
// Oxide.Core.Plugins
// ---------------------------------------------------------------------------
namespace Oxide.Core.Plugins
{
    [AttributeUsage(AttributeTargets.Field)]
    public class PluginReferenceAttribute : Attribute
    {
        public PluginReferenceAttribute() { }
        public PluginReferenceAttribute(string name) { }
    }

    /// <summary>
    /// Stand-in for an Oxide Plugin reference.  Supports Call / Call{T} / implicit bool.
    /// Concrete subclasses (PluginBridgeApi, ImageLibraryStub) override Call.
    /// </summary>
    public class Plugin
    {
        public string Name  { get; set; } = "";
        public string Title { get; set; } = "";
        public bool IsLoaded { get; set; }

        public virtual object Call(string hook, params object[] args) => null;
        public virtual object CallHook(string hook, params object[] args) => Call(hook, args);

        public virtual T Call<T>(string hook, params object[] args)
        {
            var r = Call(hook, args);
            if (r is T t) return t;
            if (r == null) return default;
            try { return (T)Convert.ChangeType(r, typeof(T)); }
            catch { return default; }
        }

        public static implicit operator bool(Plugin p) => p != null && p.IsLoaded;
    }

    /// <summary>
    /// Bridges Oxide-style Plugin.Call to a Harmony mod AppDomain API.
    /// Resolution order: *_Plugin wrapper Call → static methods on ApiType →
    /// Instance.Call → Instance/Plugin property method invoke.
    /// </summary>
    public sealed class PluginBridgeApi : Plugin
    {
        private readonly Type _api;
        private readonly object _wrapper;

        public PluginBridgeApi(Type api, object wrapper = null)
        {
            _api = api;
            _wrapper = wrapper;
            IsLoaded = api != null || wrapper != null;
        }

        public override object Call(string method, params object[] args)
        {
            if (string.IsNullOrEmpty(method)) return null;
            args ??= Array.Empty<object>();
            try
            {
                // 1) Economics_Plugin / RustRewards_Plugin wrappers expose Call(string, object[])
                if (_wrapper != null)
                {
                    var wCall = _wrapper.GetType().GetMethod("Call", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(string), typeof(object[]) }, null);
                    if (wCall != null)
                        return wCall.Invoke(_wrapper, new object[] { method, args });
                }

                if (_api == null) return null;

                // 2) Static API methods (Economics.Deposit, etc.)
                var staticMi = FindMethod(_api, method, args, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (staticMi != null)
                    return staticMi.Invoke(null, AdaptArgs(staticMi, args));

                // 3) Instance on ApiType with Call(string, object[])
                object live = ReadInstance(_api);
                if (live != null)
                {
                    var instCall = live.GetType().GetMethod("Call", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(string), typeof(object[]) }, null);
                    if (instCall != null)
                        return instCall.Invoke(live, new object[] { method, args });

                    var instMi = FindMethod(live.GetType(), method, args, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (instMi != null)
                        return instMi.Invoke(live, AdaptArgs(instMi, args));
                }

                // 4) SkillTree-style static Plugin property holding the Oxide plugin body
                var pluginProp = _api.GetProperty("Plugin", BindingFlags.Public | BindingFlags.Static)
                              ?? _api.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                var pluginObj = pluginProp?.GetValue(null);
                if (pluginObj != null && !ReferenceEquals(pluginObj, live))
                {
                    var pCall = pluginObj.GetType().GetMethod("Call", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(string), typeof(object[]) }, null);
                    if (pCall != null)
                        return pCall.Invoke(pluginObj, new object[] { method, args });

                    var pMi = FindMethod(pluginObj.GetType(), method, args, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (pMi != null)
                        return pMi.Invoke(pluginObj, AdaptArgs(pMi, args));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TruePVE] Plugin.Call " + Name + "." + method + ": " + (ex.InnerException?.Message ?? ex.Message));
            }
            return null;
        }

        private static object ReadInstance(Type api)
        {
            try
            {
                return api.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            }
            catch { return null; }
        }

        private static MethodInfo FindMethod(Type type, string name, object[] args, BindingFlags flags)
        {
            if (type == null) return null;
            Type[] types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++)
                types[i] = args[i]?.GetType() ?? typeof(object);

            var exact = type.GetMethod(name, flags, null, types, null);
            if (exact != null) return exact;

            MethodInfo fallback = null;
            foreach (var m in type.GetMethods(flags))
            {
                if (!string.Equals(m.Name, name, StringComparison.Ordinal)) continue;
                var pars = m.GetParameters();
                if (pars.Length == args.Length) return m;
                if (fallback == null && pars.Length >= args.Length) fallback = m;
            }
            return fallback;
        }

        private static object[] AdaptArgs(MethodInfo mi, object[] args)
        {
            var pars = mi.GetParameters();
            if (pars.Length == args.Length) return args;
            if (pars.Length < args.Length)
            {
                var trimmed = new object[pars.Length];
                Array.Copy(args, trimmed, pars.Length);
                return trimmed;
            }
            var full = new object[pars.Length];
            for (int i = 0; i < args.Length; i++) full[i] = args[i];
            for (int i = args.Length; i < pars.Length; i++)
                full[i] = pars[i].HasDefaultValue ? pars[i].DefaultValue
                    : (pars[i].ParameterType.IsValueType ? Activator.CreateInstance(pars[i].ParameterType) : null);
            return full;
        }
    }

    /// <summary>
    /// ImageLibrary stub. Returns empty string / false for Get/Has calls.
    /// </summary>
    public sealed class ImageLibraryStub : Plugin
    {
        public ImageLibraryStub() { Name = "ImageLibrary"; Title = "ImageLibrary"; IsLoaded = true; }

        public override object Call(string hook, params object[] args)
        {
            if (string.IsNullOrEmpty(hook)) return null;
            switch (hook)
            {
                case "GetImage": return "";
                case "HasImage": return false;
                case "ImportImageList":
                    if (args != null && args.Length > 0 && args[args.Length - 1] is Action cb)
                        Oxide.Core.Interface.NextTick(cb);
                    return true;
                case "IsReady": return false;
                default: return null;
            }
        }
    }

    /// <summary>
    /// Resolves optional plugin references to Harmony mods via AppDomain *_ApiType / *_Plugin.
    /// Permissions / Economics / RustRewards / SkillTree / RaidableBases are first-class.
    /// </summary>
    public class PluginManager
    {
        private static readonly ImageLibraryStub _imgLib = new ImageLibraryStub();

        public string ConfigPath => Path.Combine(OxideMod.ResolveServerRoot(), "HarmonyConfig");
        public string DataPath   => Path.Combine(OxideMod.ResolveServerRoot(), "HarmonyData");

        public bool Exists(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return Find(name) != null;
        }

        public Plugin Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            if (name.Equals("ImageLibrary", StringComparison.OrdinalIgnoreCase))
                return _imgLib;

            // Preferred: AppDomain keys published by Harmony mods.
            var fromDomain = TryFromAppDomain(name);
            if (fromDomain != null) return fromDomain;

            // Assembly scan fallbacks for known Harmony ports.
            string[] typeNames = KnownTypeNames(name);
            if (typeNames != null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    for (int i = 0; i < typeNames.Length; i++)
                    {
                        try
                        {
                            var t = asm.GetType(typeNames[i]);
                            if (t == null) continue;
                            if (ReadLiveInstance(t) == null && AppDomain.CurrentDomain.GetData(name + "_ApiType") == null)
                            {
                                // Type exists but mod may not be loaded yet — still return a bridge so
                                // Call can resolve once Instance is set (Permissions style).
                            }
                            return new PluginBridgeApi(t) { Name = name, IsLoaded = ReadLiveInstance(t) != null };
                        }
                        catch { }
                    }
                }
            }

            return null;
        }

        private static Plugin TryFromAppDomain(string name)
        {
            try
            {
                string apiKey = name + "_ApiType";
                string pluginKey = name + "_Plugin";
                // RustRewards also registers as GrimmRewards in some wrappers — keep name-based keys.
                var apiType = AppDomain.CurrentDomain.GetData(apiKey) as Type;
                var wrapper = AppDomain.CurrentDomain.GetData(pluginKey);

                if (apiType != null || wrapper != null)
                {
                    bool loaded = wrapper != null || ReadLiveInstance(apiType) != null;
                    return new PluginBridgeApi(apiType, wrapper) { Name = name, IsLoaded = loaded };
                }

                // Permissions publishes Permissions_ApiType (mod DLL may be named 0Permissions).
                if (name.Equals("Permissions", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("0Permissions", StringComparison.OrdinalIgnoreCase))
                {
                    apiType = AppDomain.CurrentDomain.GetData("Permissions_ApiType") as Type;
                    if (apiType != null)
                        return new PluginBridgeApi(apiType) { Name = "Permissions", IsLoaded = ReadLiveInstance(apiType) != null };
                }

                // PveMode publishes PveMode_ApiType (mod DLL named 0PveMode).
                if (name.Equals("PveMode", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("0PveMode", StringComparison.OrdinalIgnoreCase))
                {
                    apiType = AppDomain.CurrentDomain.GetData("PveMode_ApiType") as Type;
                    if (apiType != null)
                        return new PluginBridgeApi(apiType) { Name = "PveMode", IsLoaded = ReadLiveInstance(apiType) != null };
                }
            }
            catch { }
            return null;
        }

        private static object ReadLiveInstance(Type t)
        {
            if (t == null) return null;
            try { return t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null); }
            catch { return null; }
        }

        private static string[] KnownTypeNames(string name)
        {
            if (name.Equals("Economics", StringComparison.OrdinalIgnoreCase))
                return new[] { "EconomicsHarmony.EconomicsHarmonyMod", "Economics.EconomicsHarmonyMod", "EconomicsHarmony.EconomicsMod" };
            if (name.Equals("RustRewards", StringComparison.OrdinalIgnoreCase) || name.Equals("GrimmRewards", StringComparison.OrdinalIgnoreCase))
                return new[] { "RustRewardsHarmony.RustRewardsHarmonyMod", "RustRewards.RustRewardsHarmonyMod" };
            if (name.Equals("Permissions", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("0Permissions", StringComparison.OrdinalIgnoreCase))
                return new[] { "PermissionsHarmony.PermissionsMod" };
            if (name.Equals("PveMode", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("0PveMode", StringComparison.OrdinalIgnoreCase))
                return new[] { "PveModeHarmony.PveModeApi", "PveModeHarmony.PveModeMod" };
            if (name.Equals("SkillTree", StringComparison.OrdinalIgnoreCase))
                return new[] { "SkillTreeHarmony.SkillTreeMod" };
            if (name.Equals("RaidableBases", StringComparison.OrdinalIgnoreCase))
                return new[] { "RaidableBases.RaidableBasesHost", "RaidableBases.RaidableBasesMod" };
            if (name.Equals("ServerRewards", StringComparison.OrdinalIgnoreCase))
                return new[] { "ServerRewardsHarmony.ServerRewardsMod" };
            if (name.Equals("MovementSpeed", StringComparison.OrdinalIgnoreCase))
                return new[] { "MovementSpeedHarmony.MovementSpeedMod" };
            if (name.Equals("Kits", StringComparison.OrdinalIgnoreCase))
                return new[] { "KitsHarmony.KitsHarmonyMod", "Kits.KitsMod" };
            return null;
        }
    }
}

// ---------------------------------------------------------------------------
// Oxide.Plugins  (attributes, shims, CommandLib, PermissionLib, TimerLib,
//                 PlayerLibrary, RustPlugin base)
// ---------------------------------------------------------------------------
namespace Oxide.Plugins
{
    using Oxide.Core.Configuration;
    using Oxide.Core.Libraries;
    using Oxide.Core.Plugins;

    // Timer is made globally available via GlobalUsings.cs (global using alias).

    // ---- Attributes --------------------------------------------------------

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InfoAttribute : Attribute
    {
        public string Title   { get; }
        public string Author  { get; }
        public string Version { get; }
        public InfoAttribute(string title, string author, string version)
        { Title = title; Author = author; Version = version; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DescriptionAttribute : Attribute
    {
        public string Description { get; }
        public DescriptionAttribute(string d) => Description = d;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class ChatCommandAttribute : Attribute
    {
        public string Command { get; }
        public ChatCommandAttribute(string cmd) => Command = cmd;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class ConsoleCommandAttribute : Attribute
    {
        public string Command { get; }
        public ConsoleCommandAttribute(string cmd) => Command = cmd;
    }

    /// <summary>Marks a method as a hook handler. No-op in Harmony (all hooks dispatched explicitly).</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HookMethodAttribute : Attribute
    {
        public HookMethodAttribute(string hook) { }
    }

    /// <summary>No-op: HarmonyLoader PatchAll handles [HarmonyPatch] attributes.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class AutoPatchAttribute : Attribute { }

    // ---- ServerShim -------------------------------------------------------

    public class ServerShim
    {
        public void Command(string cmd, params object[] args)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return;
            try { ConsoleSystem.Run(ConsoleSystem.Option.Server, cmd, args); }
            catch (Exception ex) { Debug.LogWarning("[TruePVE] Server.Command: " + ex.Message); }
        }

        public void Broadcast(string msg)
        {
            var list = BasePlayer.activePlayerList;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++) list[i]?.ChatMessage(msg);
        }

        // Oxide rust Server.Broadcast(message, prefix, userId, args) signature.
        public void Broadcast(string message, string prefix = null, ulong userId = 0UL, params object[] args)
        {
            if (args != null && args.Length > 0)
            {
                try { message = string.Format(message, args); } catch { }
            }
            string full = string.IsNullOrEmpty(prefix) ? message : prefix + " " + message;
            var list = BasePlayer.activePlayerList;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p == null || p.net?.connection == null) continue;
                try { ConsoleNetwork.SendClientCommand(p.net.connection, "chat.add", 2, userId, full); }
                catch { p.ChatMessage(full); }
            }
        }
    }

    // ---- CommandLib -------------------------------------------------------

    public class CommandLib
    {
        private readonly List<(string name, string method)> _chat    = new List<(string, string)>();
        private readonly List<(string name, string method)> _console = new List<(string, string)>();

        public IReadOnlyList<(string name, string method)> RegisteredChatCommands    => _chat;
        public IReadOnlyList<(string name, string method)> RegisteredConsoleCommands => _console;

        public void AddChatCommand(string name, object plugin, string method)
        {
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(method))
                _chat.Add((name.Trim().ToLowerInvariant(), method));
        }

        public void AddConsoleCommand(string name, object plugin, string method)
        {
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(method))
                _console.Add((name.Trim().ToLowerInvariant(), method));
        }

        public void RemoveChatCommand(string name, object plugin)
        {
            if (string.IsNullOrEmpty(name)) return;
            var n = name.Trim().ToLowerInvariant();
            _chat.RemoveAll(t => t.name == n);
        }

        public void RemoveConsoleCommand(string name, object plugin)
        {
            if (string.IsNullOrEmpty(name)) return;
            var n = name.Trim().ToLowerInvariant();
            _console.RemoveAll(t => t.name == n);
        }
    }

    // ---- PermissionLib ----------------------------------------------------

    /// <summary>
    /// Oxide permission.* surface backed by TruePVEHarmony.PermissionsBridge
    /// (reflection bridge to PermissionsHarmony.PermissionsMod).
    /// </summary>
    public class PermissionLib
    {
        public void RegisterPermission(string perm, object plugin = null)
            => TruePVEHarmony.PermissionsBridge.RegisterPermission(perm);

        public bool PermissionExists(string perm)
            => TruePVEHarmony.PermissionsBridge.PermissionExists(perm);
        // Overload with plugin owner (Oxide passes the plugin; we ignore it).
        public bool PermissionExists(string perm, object plugin)
            => TruePVEHarmony.PermissionsBridge.PermissionExists(perm);

        public bool UserHasPermission(string userId, string perm)
            => TruePVEHarmony.PermissionsBridge.UserHasPermission(userId, perm);

        public void GrantUserPermission(string userId, string perm, object plugin = null)
            => TruePVEHarmony.PermissionsBridge.GrantUserPermission(userId, perm);

        public void RevokeUserPermission(string userId, string perm, object plugin = null)
            => TruePVEHarmony.PermissionsBridge.RevokeUserPermission(userId, perm);

        public string[] GetGroupPermissions(string group)
            => TruePVEHarmony.PermissionsBridge.GetGroupPermissions(group);

        public string[] GetUsersInGroup(string group)
            => TruePVEHarmony.PermissionsBridge.GetUsersInGroup(group);

        public string[] GetGroups()
            => TruePVEHarmony.PermissionsBridge.GetGroups();

        public bool GroupExists(string group)
            => TruePVEHarmony.PermissionsBridge.GroupExists(group);

        public bool CreateGroup(string name, string title, int rank)
            => TruePVEHarmony.PermissionsBridge.CreateGroup(name, title, rank);

        public bool GroupHasPermission(string group, string perm)
            => TruePVEHarmony.PermissionsBridge.GroupHasPermission(group, perm);

        public bool GrantGroupPermission(string group, string perm, object plugin = null)
            => TruePVEHarmony.PermissionsBridge.GrantGroupPermission(group, perm);

        public bool RevokeGroupPermission(string group, string perm, object plugin = null)
            => TruePVEHarmony.PermissionsBridge.RevokeGroupPermission(group, perm);

        public bool UserHasGroup(string userId, string group)
            => TruePVEHarmony.PermissionsBridge.UserHasGroup(userId, group);

        public void AddUserGroup(string userId, string group, object plugin = null)
            => TruePVEHarmony.PermissionsBridge.AddUserGroup(userId, group);

        public void RemoveUserGroup(string userId, string group, object plugin = null)
            => TruePVEHarmony.PermissionsBridge.RemoveUserGroup(userId, group);

        public string[] GetUserGroups(string userId)
            => TruePVEHarmony.PermissionsBridge.GetUserGroups(userId);
    }

    // ---- TimerLib ---------------------------------------------------------

    /// <summary>
    /// Oxide timer.* surface backed by ServerMgr coroutines.
    /// </summary>
    public class TimerLib
    {
        private readonly List<Timer> _active = new List<Timer>();

        public Timer Once(float seconds, Action callback)  => In(seconds, callback);

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
            try { cb(); } catch (Exception ex) { Debug.LogWarning("[TruePVE] Timer: " + ex.Message); }
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
                try { cb(); } catch (Exception ex) { Debug.LogWarning("[TruePVE] Timer: " + ex.Message); }
                count++;
            }
            t.Destroy();
            lock (_active) _active.Remove(t);
        }
    }

    // ---- PlayerLibrary ----------------------------------------------------

    /// <summary>
    /// Oxide Game.Rust.Libraries.Player surface used by TruePVE for Player.Message.
    /// </summary>
    public class PlayerLibrary
    {
        /// <summary>Send a chat message from the server to a player with an optional steam icon ID.</summary>
        public void Message(BasePlayer player, string message, ulong chatId = 0UL)
        {
            if (player == null || !player.IsConnected || string.IsNullOrEmpty(message)) return;
            if (player.net?.connection == null) { player.ChatMessage(message); return; }
            try
            {
                ConsoleNetwork.SendClientCommand(player.net.connection, "chat.add", 2, chatId, message);
            }
            catch { player.ChatMessage(message); }
        }

        /// <summary>Convenience overload accepting a format + args.</summary>
        public void Message(BasePlayer player, string message, ulong chatId, params object[] args)
        {
            if (args != null && args.Length > 0)
            {
                try { message = string.Format(message, args); } catch { }
            }
            Message(player, message, chatId);
        }

        public void Reply(BasePlayer player, string message, string prefix = "", ulong chatIcon = 0UL)
            => Message(player, string.IsNullOrEmpty(prefix) ? message : prefix + " " + message, chatIcon);
    }

    // ---- RustPlugin -------------------------------------------------------

    /// <summary>
    /// Minimal Oxide RustPlugin base class.  Provides:
    ///   - Name/Title/Author/Version from [Info] attribute
    ///   - lang, plugins, Server, permission, timer, cmd, Player members
    ///   - Config (DynamicConfigFile) with HarmonyConfig/{Name}.json resolution
    ///   - Subscribe/Unsubscribe/IsSubscribed for hook gating
    ///   - Puts/PrintWarning/PrintError/PrintToChat/LogToFile helpers
    ///   - NextTick delegation to ModRunner
    /// </summary>
    public abstract class RustPlugin
    {
        public string Name   { get; protected set; }
        public string Title  { get; protected set; }
        public string Author { get; protected set; }
        public VersionNumber Version { get; protected set; }

        // Oxide member fields -- same names as in the original plugin.
        public readonly Lang                              lang       = new Lang();
        public readonly Oxide.Core.Plugins.PluginManager  plugins    = new Oxide.Core.Plugins.PluginManager();
        public readonly ServerShim                        Server     = new ServerShim();
        public readonly PermissionLib    permission = new PermissionLib();
        public readonly TimerLib         timer      = new TimerLib();
        public readonly CommandLib       cmd        = new CommandLib();
        public readonly PlayerLibrary    Player     = new PlayerLibrary();
        public readonly Oxide.Core.Libraries.Covalence.Covalence covalence = new Oxide.Core.Libraries.Covalence.Covalence();

        private DynamicConfigFile _config;

        protected DynamicConfigFile Config
        {
            get
            {
                if (_config == null) _config = new DynamicConfigFile(ResolveConfigPath(Name));
                return _config;
            }
            set => _config = value;
        }

        private readonly HashSet<string> _unsubscribed = new HashSet<string>(StringComparer.Ordinal);

        protected RustPlugin()
        {
            var info = (InfoAttribute)Attribute.GetCustomAttribute(GetType(), typeof(InfoAttribute));
            if (info != null)
            {
                Title   = info.Title;
                Name    = (info.Title ?? GetType().Name).Replace(" ", string.Empty);
                Author  = info.Author;
                Version = ParseVersion(info.Version);
            }
            else
            {
                Name  = GetType().Name;
                Title = Name;
            }
        }

        private static VersionNumber ParseVersion(string v)
        {
            try
            {
                if (string.IsNullOrEmpty(v)) return new VersionNumber(1, 0, 0);
                var p = v.Split('.');
                return new VersionNumber(
                    p.Length > 0 ? int.Parse(p[0]) : 0,
                    p.Length > 1 ? int.Parse(p[1]) : 0,
                    p.Length > 2 ? int.Parse(p[2]) : 0);
            }
            catch { return new VersionNumber(1, 0, 0); }
        }

        private static string ResolveConfigPath(string name)
        {
            string root = Oxide.Core.OxideMod.ResolveServerRoot();
            string[] candidates =
            {
                Path.Combine(root, "HarmonyConfig", name + ".json"),
                Path.Combine(root, "oxide", "config", name + ".json"),
                Path.Combine(root, name + ".json"),
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
            return candidates[0];
        }

        // Config lifecycle
        protected virtual void LoadDefaultConfig() { }
        protected virtual void LoadConfig()
        {
            _config = new DynamicConfigFile(ResolveConfigPath(Name));
            if (!File.Exists(_config.Filename))
            {
                LoadDefaultConfig();
                SaveConfig();
                return;
            }
            _config.Load();
        }
        protected virtual void SaveConfig()
        {
            if (_config != null && !string.IsNullOrEmpty(_config.Filename))
                _config.Save();
        }
        protected virtual void LoadDefaultMessages() { }

        public void HarmonyLoadConfig()           => LoadConfig();
        public void HarmonyLoadDefaultMessages()  => LoadDefaultMessages();

        // Hook subscription tracking  (default = all subscribed).
        public void Subscribe(string hook)   { _unsubscribed.Remove(hook); }
        public void Unsubscribe(string hook) { _unsubscribed.Add(hook); }
        public bool IsSubscribed(string hook)=> !_unsubscribed.Contains(hook);

        // Logging
        private string Tag => "[" + (Title ?? Name ?? "TruePVE") + "]";
        private static string Fmt(string fmt, object[] args)
        {
            if (args == null || args.Length == 0) return fmt;
            try { return string.Format(fmt, args); } catch { return fmt; }
        }

        public void Puts(string fmt, params object[] args)          => Debug.Log(Tag + " " + Fmt(fmt, args));
        public void PrintWarning(string fmt, params object[] args)  => Debug.LogWarning(Tag + " " + Fmt(fmt, args));
        public void PrintError(string fmt, params object[] args)    => Debug.LogError(Tag + " " + Fmt(fmt, args));

        public void PrintToChat(string fmt, params object[] args) => Server.Broadcast(Fmt(fmt, args));
        public void PrintToChat(BasePlayer p, string fmt, params object[] args)
        {
            if (p == null || !p.IsConnected) return;
            p.ChatMessage(Fmt(fmt, args));
        }

        public void PrintToConsole(string message)
        {
            Debug.Log(Tag + " " + message);
        }
        public void PrintToConsole(BasePlayer p, string fmt, params object[] args)
        {
            if (p == null) { Debug.Log(Tag + " " + Fmt(fmt, args)); return; }
            p.ConsoleMessage(Fmt(fmt, args));
        }

        public void LogToFile(string filename, string text, object plugin, bool timestamp = true)
            => LogToFile(filename, text, plugin, timestamp, false);

        public void LogToFile(string filename, string text, object plugin, bool timestamp, bool extraFlag)
        {
            try
            {
                var root  = Oxide.Core.OxideMod.ResolveServerRoot();
                var dir   = Path.Combine(root, "HarmonyData", Name ?? "TruePVE", "logs");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var path  = Path.Combine(dir, (filename ?? Name ?? "TruePVE") + ".txt");
                var line  = (timestamp ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] " : "") + text + Environment.NewLine;
                File.AppendAllText(path, line);
            }
            catch (Exception ex) { Debug.LogWarning(Tag + " LogToFile: " + ex.Message); }
        }

        // Oxide exposes plugin manager as Manager with ConfigPath, DataPath etc.
        public Oxide.Core.Plugins.PluginManager Manager => plugins;

        // Oxide web request shim (no-op; discord webhook just won't fire).
        public readonly WebRequests webrequest = new WebRequests();

        public void NextTick(Action action)
        {
            if (action == null) return;
            TruePVEHarmony.ModRunner.Enqueue(action);
        }

        public void NextFrame(Action action)
        {
            if (action == null) return;
            TruePVEHarmony.ModRunner.Enqueue(action);
        }

        // Oxide command registration: register both chat and console under the same name.
        protected void AddCovalenceCommand(string command, string callback)
        {
            if (string.IsNullOrEmpty(command) || string.IsNullOrEmpty(callback)) return;
            cmd.AddChatCommand(command, this, callback);
            cmd.AddConsoleCommand(command, this, callback);
        }

        protected void AddCovalenceCommand(string[] commands, string callback)
        {
            if (commands == null) return;
            foreach (var c in commands) AddCovalenceCommand(c, callback);
        }

        // Oxide SendReply variants used by TruePVE.
        public void SendReply(BasePlayer player, string message, params object[] args)
        {
            if (player == null || !player.IsConnected || string.IsNullOrEmpty(message)) return;
            var text = Fmt(message, args);
            if (player.net?.connection == null) { player.ChatMessage(text); return; }
            try { ConsoleNetwork.SendClientCommand(player.net.connection, "chat.add", 2, 0UL, text); }
            catch { player.ChatMessage(text); }
        }

        public void SendReply(Oxide.Core.Libraries.Covalence.IPlayer user, string message, params object[] args)
        {
            if (user == null) return;
            user.Reply(Fmt(message, args));
        }

        public void SendReply(ConsoleSystem.Arg arg, string message, params object[] args)
        {
            var text = Fmt(message, args);
            var player = arg?.Player();
            if (player != null) { SendReply(player, text); return; }
            Debug.Log(Tag + " " + text);
        }
    }

    // ---- WebRequests shim --------------------------------------------------
    public class WebRequests
    {
        /// <summary>No-op web request enqueue; discord webhooks simply don't fire.</summary>
        public void Enqueue(string url, string body, Action<int, string> callback,
                            object owner = null,
                            Oxide.Core.Libraries.RequestMethod method = Oxide.Core.Libraries.RequestMethod.GET,
                            Dictionary<string, string> headers = null)
        {
            // Silently no-op; fire callback with error code so plugin handles gracefully.
            try { callback?.Invoke(503, null); } catch { }
        }
    }

    // ---- Extension methods (IsSteamId, TitleCase) --------------------------
    public static class FacepunchExtensions
    {
        private const ulong SteamIdBase = 76561197960265728UL;

        public static bool IsSteamId(this ulong id)
            => id > SteamIdBase;

        public static bool IsSteamId(this string s)
            => ulong.TryParse(s, out var id) && id.IsSteamId();

        public static bool IsSteamId(this EncryptedValue<ulong> ev)
        {
            try { return ((ulong)ev).IsSteamId(); }
            catch { return false; }
        }

        public static string TitleCase(this string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
        }
    }
}
