/*
 * Oxide-free shims for RestoreItems Harmony port.
 * Config: HarmonyConfig/RestoreItems.json
 * Data:   HarmonyData/RestoreItems/
 * Lang:   HarmonyLanguage/RestoreItems.json (optional override)
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Oxide.Core
{
    public struct VersionNumber
    {
        public int Major, Minor, Patch;
        public VersionNumber(int major, int minor, int patch) { Major = major; Minor = minor; Patch = patch; }
        public override string ToString() => Major + "." + Minor + "." + Patch;
    }

    public static class Interface
    {
        public static readonly OxideMod Oxide = new OxideMod();

        public static object CallHook(string hook, params object[] args)
        {
            try
            {
                if (string.Equals(hook, "OnRaidableBaseBackpackEject", StringComparison.Ordinal))
                {
                    var plugin = RestoreItemsHarmony.RestoreItemsHarmonyMod.Plugin;
                    if (plugin == null || args == null || args.Length < 1) return null;
                    if (args[0] is DroppedItemContainer container)
                        plugin.OnRaidableBaseBackpackEject(container);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RestoreItems] CallHook(" + hook + "): " + ex.Message);
            }
            return null;
        }

        public static void NextTick(Action action) => RestoreItemsHarmony.ModRunner.Enqueue(action);
    }

    public class OxideMod
    {
        private string _root;
        private DataFileSystem _dfs;

        public string RootDirectory => _root ??= ResolveServerRoot();
        public string ConfigDirectory => Path.Combine(RootDirectory, "HarmonyConfig");
        public string DataDirectory => Path.Combine(RootDirectory, "HarmonyData");
        public string LangDirectory => Path.Combine(RootDirectory, "HarmonyLanguage");
        public DataFileSystem DataFileSystem => _dfs ??= new DataFileSystem(Path.Combine(DataDirectory, "RestoreItems"));

        public static string ResolveServerRoot()
        {
            try
            {
                if (!string.IsNullOrEmpty(Application.dataPath))
                {
                    string fromUnity = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    if (LooksLikeServerRoot(fromUnity)) return fromUnity;
                }
            }
            catch { }

            string cur = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory ?? ".");
            for (int i = 0; i < 6 && !string.IsNullOrEmpty(cur); i++)
            {
                if (LooksLikeServerRoot(cur)) return cur;
                string parent = Path.GetDirectoryName(cur);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, cur, StringComparison.OrdinalIgnoreCase)) break;
                cur = parent;
            }
            return cur;
        }

        private static bool LooksLikeServerRoot(string d) =>
            !string.IsNullOrEmpty(d) && Directory.Exists(d) &&
            (Directory.Exists(Path.Combine(d, "HarmonyConfig"))
             || Directory.Exists(Path.Combine(d, "HarmonyMods"))
             || File.Exists(Path.Combine(d, "RustDedicated.exe")));
    }

    public class DataFileSystem
    {
        private readonly string _root;

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

        public T ReadObject<T>(string name)
        {
            var path = Resolve(name);
            if (!File.Exists(path)) return default;
            try { return JsonConvert.DeserializeObject<T>(File.ReadAllText(path)); }
            catch { return default; }
        }

        public void WriteObject<T>(string name, T obj, bool sync = false)
        {
            var path = Resolve(name);
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(obj, Formatting.Indented));
            }
            catch (Exception ex) { Debug.LogWarning("[RestoreItems] WriteObject: " + ex.Message); }
        }
    }
}

namespace Oxide.Core.Plugins
{
    public class Plugin
    {
        public string Name { get; set; }
        public bool IsLoaded { get; set; } = true;
        public virtual object Call(string method, params object[] args) => null;

        public T Call<T>(string method, params object[] args)
        {
            var result = Call(method, args);
            if (result == null) return default;
            try
            {
                if (result is T typed) return typed;
                return (T)Convert.ChangeType(result, typeof(T));
            }
            catch { return default; }
        }

        public static implicit operator bool(Plugin p) => p != null && p.IsLoaded;
    }

    public class PluginManager
    {
        public Plugin Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            if (name.Equals("Economics", StringComparison.OrdinalIgnoreCase))
            {
                var wrapper = AppDomain.CurrentDomain.GetData("Economics_Plugin");
                if (wrapper != null)
                    return new PluginBridge(wrapper) { Name = "Economics", IsLoaded = true };
            }

            if (name.Equals("RaidableBases", StringComparison.OrdinalIgnoreCase))
            {
                var plugin = AppDomain.CurrentDomain.GetData("RaidableBases_Plugin");
                if (plugin != null)
                    return new RaidableBasesBridge(plugin) { Name = "RaidableBases", IsLoaded = true };

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var hostType = asm.GetType("RaidableBases.RaidableBasesHost");
                        if (hostType == null) continue;
                        var host = hostType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                        if (host == null) continue;
                        var mod = hostType.GetProperty("ModInstance", BindingFlags.Public | BindingFlags.Instance)?.GetValue(host);
                        if (mod != null)
                            return new RaidableBasesBridge(mod) { Name = "RaidableBases", IsLoaded = true };
                    }
                    catch { }
                }
            }

            return null;
        }
    }

    public sealed class PluginBridge : Plugin
    {
        private readonly object _instance;
        public PluginBridge(object instance) => _instance = instance;

        public override object Call(string method, params object[] args)
        {
            if (_instance == null || string.IsNullOrEmpty(method)) return null;
            try
            {
                var call = _instance.GetType().GetMethod("Call", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (call != null)
                    return call.Invoke(_instance, new object[] { method, args ?? Array.Empty<object>() });

                var mi = _instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null) return null;
                return mi.Invoke(_instance, args ?? Array.Empty<object>());
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RestoreItems] Economics." + method + ": " + ex.Message);
                return null;
            }
        }
    }

    public sealed class RaidableBasesBridge : Plugin
    {
        private readonly object _instance;
        public RaidableBasesBridge(object instance) => _instance = instance;

        public override object Call(string method, params object[] args)
        {
            if (_instance == null || string.IsNullOrEmpty(method)) return null;
            try
            {
                if (method.Equals("IsPositionInRaid", StringComparison.OrdinalIgnoreCase)
                    || method.Equals("IsPositionInZone", StringComparison.OrdinalIgnoreCase))
                {
                    if (args == null || args.Length < 1 || !(args[0] is Vector3 pos)) return false;
                    var mi = _instance.GetType().GetMethod("EventTerritory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (mi != null)
                        return mi.Invoke(_instance, new object[] { pos, 0f }) is bool b && b;
                    return false;
                }

                var call = _instance.GetType().GetMethod("Call", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (call != null)
                    return call.Invoke(_instance, new object[] { method, args ?? Array.Empty<object>() });

                var direct = _instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (direct == null) return null;
                return direct.Invoke(_instance, args ?? Array.Empty<object>());
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RestoreItems] RaidableBases." + method + ": " + ex.Message);
                return null;
            }
        }
    }
}

namespace Oxide.Core.Libraries
{
    public class Lang
    {
        private readonly Dictionary<string, Dictionary<string, string>> _messages =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public void RegisterMessages(Dictionary<string, string> messages, object plugin, string lang = "en")
        {
            if (messages == null) return;
            if (!_messages.TryGetValue(lang, out var map))
                _messages[lang] = map = new Dictionary<string, string>();
            foreach (var kv in messages) map[kv.Key] = kv.Value;
            TryLoadFileOverride(lang);
        }

        private void TryLoadFileOverride(string lang)
        {
            try
            {
                string path = Path.Combine(Interface.Oxide.LangDirectory, "RestoreItems.json");
                if (!File.Exists(path)) return;
                var file = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (file == null) return;
                if (!_messages.TryGetValue(lang, out var map))
                    _messages[lang] = map = new Dictionary<string, string>();
                foreach (var kv in file) map[kv.Key] = kv.Value;
            }
            catch (Exception ex) { Debug.LogWarning("[RestoreItems] Lang file: " + ex.Message); }
        }

        public string GetMessage(string key, object plugin = null, string userId = null)
        {
            if (_messages.TryGetValue("en", out var en) && en.TryGetValue(key, out var msg)) return msg;
            return key;
        }
    }
}

namespace Oxide.Plugins
{
    using Oxide.Core;
    using Oxide.Core.Libraries;
    using Oxide.Core.Plugins;

    [AttributeUsage(AttributeTargets.Class)]
    public class InfoAttribute : Attribute
    {
        public string Title { get; }
        public string Author { get; }
        public string Version { get; }
        public InfoAttribute(string title, string author, string version)
        { Title = title; Author = author; Version = version; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class DescriptionAttribute : Attribute
    {
        public string Description { get; }
        public DescriptionAttribute(string description) { Description = description; }
    }

    public class DynamicConfigFile
    {
        public string Filename;
        private string _json;

        public void Load() => _json = File.Exists(Filename) ? File.ReadAllText(Filename) : null;

        public T ReadObject<T>() =>
            string.IsNullOrWhiteSpace(_json) ? default : JsonConvert.DeserializeObject<T>(_json);

        public void WriteObject<T>(T obj, bool sync = false)
        {
            try
            {
                string dir = Path.GetDirectoryName(Filename);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                _json = JsonConvert.SerializeObject(obj, Formatting.Indented);
                File.WriteAllText(Filename, _json);
            }
            catch (Exception ex) { Debug.LogWarning("[RestoreItems] Config write failed: " + ex.Message); }
        }
    }

    public class CommandLib
    {
        private readonly List<(string name, string method)> _chat = new List<(string, string)>();
        public IReadOnlyList<(string name, string method)> RegisteredChatCommands => _chat;

        public void AddChatCommand(string name, object plugin, string method)
        {
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(method))
                _chat.Add((name, method));
        }
    }

    public class PermissionLib
    {
        private static Type _permType;
        private static MethodInfo _userHas, _register, _exists;
        private static int _boundGen = -1;

        private static void EnsureBound()
        {
            int gen = 0;
            try { if (AppDomain.CurrentDomain.GetData("Permissions_Generation") is int g) gen = g; } catch { }
            if (_permType != null && _boundGen == gen) return;

            _permType = AppDomain.CurrentDomain.GetData("Permissions_ApiType") as Type;
            if (_permType == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        _permType = asm.GetType("PermissionsHarmony.PermissionsMod");
                        if (_permType != null) break;
                    }
                    catch { }
                }
            }

            if (_permType == null) return;
            const BindingFlags S = BindingFlags.Public | BindingFlags.Static;
            _userHas = _permType.GetMethod("UserHasPermission", S, null, new[] { typeof(string), typeof(string) }, null);
            _register = _permType.GetMethod("RegisterPermission", S, null, new[] { typeof(string) }, null);
            _exists = _permType.GetMethod("PermissionExists", S, null, new[] { typeof(string) }, null);
            _boundGen = gen;
        }

        public void RegisterPermission(string perm, object plugin)
        {
            if (string.IsNullOrEmpty(perm)) return;
            EnsureBound();
            try { _register?.Invoke(null, new object[] { perm }); } catch { }
        }

        public bool PermissionExists(string perm, object plugin = null)
        {
            if (string.IsNullOrEmpty(perm)) return false;
            EnsureBound();
            try { if (_exists?.Invoke(null, new object[] { perm }) is bool b) return b; } catch { }
            return false;
        }

        public bool UserHasPermission(string userId, string perm)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(perm)) return false;
            EnsureBound();
            try { if (_userHas?.Invoke(null, new object[] { userId, perm }) is bool b) return b; } catch { }
            return false;
        }
    }

    public class TimerLib
    {
        private readonly List<Timer> _active = new List<Timer>();

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

        private IEnumerator RunRepeat(float secs, int times, Timer t, Action cb)
        {
            int count = 0;
            while (!t.Destroyed && (times < 0 || count < times))
            {
                if (secs > 0f) yield return new WaitForSeconds(secs); else yield return null;
                if (t.Destroyed) break;
                try { cb(); } catch (Exception ex) { Debug.LogWarning("[RestoreItems] Timer: " + ex.Message); }
                count++;
            }
            t.Destroy();
            lock (_active) _active.Remove(t);
        }
    }

    public class Timer
    {
        public bool Destroyed { get; private set; }
        public void Destroy() => Destroyed = true;
    }

    public abstract class RustPlugin
    {
        public string Name { get; protected set; }
        public string Title { get; protected set; }
        public string Author { get; protected set; }
        public VersionNumber Version { get; protected set; }

        public readonly Lang lang = new Lang();
        public readonly PluginManager plugins = new PluginManager();
        public readonly PermissionLib permission = new PermissionLib();
        public readonly TimerLib timer = new TimerLib();
        public readonly CommandLib cmd = new CommandLib();

        private DynamicConfigFile _config;
        protected DynamicConfigFile Config
        {
            get
            {
                if (_config == null) _config = new DynamicConfigFile { Filename = ResolveConfigPath(Name) };
                return _config;
            }
            set => _config = value;
        }

        protected RustPlugin()
        {
            var info = (InfoAttribute)Attribute.GetCustomAttribute(GetType(), typeof(InfoAttribute));
            if (info != null)
            {
                Title = info.Title;
                Name = (info.Title ?? GetType().Name).Replace(" ", string.Empty);
                Author = info.Author;
                Version = ParseVersion(info.Version);
            }
            else { Name = GetType().Name; Title = Name; }
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
            string root = OxideMod.ResolveServerRoot();
            return Path.Combine(root, "HarmonyConfig", name + ".json");
        }

        protected virtual void LoadDefaultConfig() { }
        protected virtual void LoadConfig()
        {
            Config.Filename = ResolveConfigPath(Name);
            if (!File.Exists(Config.Filename))
            {
                LoadDefaultConfig();
                SaveConfig();
            }
            Config.Load();
        }
        protected virtual void SaveConfig() { }
        protected virtual void LoadDefaultMessages() { }

        public void Puts(string format, params object[] args) =>
            Debug.Log("[" + (Title ?? "RestoreItems") + "] " + Fmt(format, args));
        public void PrintWarning(string format, params object[] args) =>
            Debug.LogWarning("[" + (Title ?? "RestoreItems") + "] " + Fmt(format, args));
        public void PrintError(string format, params object[] args) =>
            Debug.LogError("[" + (Title ?? "RestoreItems") + "] " + Fmt(format, args));

        private static string Fmt(string format, object[] args)
        {
            if (args == null || args.Length == 0 || string.IsNullOrEmpty(format)) return format;
            try { return string.Format(format, args); } catch { return format; }
        }
    }
}

namespace RestoreItemsHarmony
{
    public class ModRunner : MonoBehaviour
    {
        public static ModRunner Instance { get; private set; }
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static GameObject _go;

        public static void Ensure()
        {
            if (Instance != null) return;
            _go = new GameObject("RestoreItems_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _go.hideFlags = HideFlags.HideAndDontSave;
            Instance = _go.AddComponent<ModRunner>();
        }

        public static void Destroy()
        {
            lock (_queue) _queue.Clear();
            if (_go != null) { UnityEngine.Object.Destroy(_go); _go = null; Instance = null; }
        }

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            lock (_queue) _queue.Enqueue(action);
        }

        private void Update()
        {
            while (true)
            {
                Action a;
                lock (_queue) { if (_queue.Count == 0) break; a = _queue.Dequeue(); }
                try { a(); }
                catch (Exception ex) { Debug.LogWarning("[RestoreItems] NextTick: " + ex.Message); }
            }
        }
    }

    internal static class DeathHookState
    {
        internal static HitInfo LastHitInfo;
        internal static void Clear() => LastHitInfo = null;
    }

    public static class FacepunchExtensions
    {
        private const ulong SteamIdBase = 76561197960265728UL;

        public static bool IsSteamId(this ulong id) => id > SteamIdBase;

        public static bool IsSteamId(this string s) =>
            ulong.TryParse(s, out var id) && id.IsSteamId();

        public static bool IsSteamId(this EncryptedValue<ulong> ev)
        {
            try { return ((ulong)ev).IsSteamId(); }
            catch { return false; }
        }
    }
}
