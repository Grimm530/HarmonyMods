// Oxide -> Harmony compatibility shims for the ported GrimmBoss plugin.
// Provides the Oxide API surface the plugin uses so the original body compiles almost verbatim.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;

namespace Oxide.Core
{
    public struct VersionNumber : IEquatable<VersionNumber>, IComparable<VersionNumber>
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

        public bool Equals(VersionNumber other) => Major == other.Major && Minor == other.Minor && Patch == other.Patch;
        public override bool Equals(object obj) => obj is VersionNumber vn && Equals(vn);
        public override int GetHashCode() => (Major * 397 ^ Minor) * 397 ^ Patch;
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
        private static Type _oxideInterfaceType;
        private static bool _oxideInterfaceResolved;

        private static Type GetOxideInterfaceType()
        {
            if (_oxideInterfaceResolved)
                return _oxideInterfaceType;
            _oxideInterfaceResolved = true;
            _oxideInterfaceType = Type.GetType("Oxide.Core.Interface, Oxide.Core");
            return _oxideInterfaceType;
        }

        /// <summary>
        /// Forwards select hooks to Harmony mods (Kits GiveKit). Otherwise null — no Oxide hook bus.
        /// </summary>
        public static object CallHook(string hook, params object[] args)
        {
            if (string.IsNullOrEmpty(hook)) return null;
            args ??= Array.Empty<object>();

            if (string.Equals(hook, "GiveKit", StringComparison.OrdinalIgnoreCase))
                return KitsPluginBridge.TryGiveKit(args);

            // Soft-forward to real Oxide when present (optional cross-plugin notifications).
            // Cache the lookup: HarmonyLoader logs every AssemblyResolve, and Oxide.Core is not installed.
            try
            {
                Type t = GetOxideInterfaceType();
                if (t != null)
                {
                    MethodInfo mi = t.GetMethod("CallHook", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(object[]) }, null);
                    if (mi != null) return mi.Invoke(null, new object[] { hook, args });
                }
            }
            catch { }

            return null;
        }
    }

    public class OxideMod
    {
        private DataFileSystem _dataFileSystem;
        private string _rootDirectory;

        public string RootDirectory
        {
            get
            {
                if (!string.IsNullOrEmpty(_rootDirectory))
                    return _rootDirectory;
                _rootDirectory = ResolveServerRoot();
                return _rootDirectory;
            }
        }

        public string ConfigDirectory => Path.Combine(RootDirectory, "HarmonyConfig");
        public string DataDirectory => Path.Combine(RootDirectory, "HarmonyData");
        public DataFileSystem DataFileSystem => _dataFileSystem ??= new DataFileSystem(DataDirectory);

        public object CallHook(string hook, params object[] args) => Interface.CallHook(hook, args);

        public void UnloadPlugin(string name)
        {
            Debug.LogWarning("[GrimmBoss] UnloadPlugin('" + name + "') requested but ignored (Harmony port keeps running / soft-fail).");
        }

        internal static string ResolveServerRoot()
        {
            try
            {
                if (!string.IsNullOrEmpty(Application.dataPath))
                {
                    string fromUnity = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    if (LooksLikeServerRoot(fromUnity))
                        return fromUnity;
                }
            }
            catch { }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory ?? ".";
            try
            {
                string cur = Path.GetFullPath(baseDir);
                for (int i = 0; i < 6 && !string.IsNullOrEmpty(cur); i++)
                {
                    if (LooksLikeServerRoot(cur))
                        return cur;
                    string parent = Path.GetDirectoryName(cur);
                    if (string.IsNullOrEmpty(parent) || string.Equals(parent, cur, StringComparison.OrdinalIgnoreCase))
                        break;
                    cur = parent;
                }
            }
            catch { }

            return Path.GetFullPath(baseDir);
        }

        private static bool LooksLikeServerRoot(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return false;
            return Directory.Exists(Path.Combine(dir, "HarmonyConfig"))
                || Directory.Exists(Path.Combine(dir, "HarmonyData"))
                || Directory.Exists(Path.Combine(dir, "HarmonyMods"))
                || File.Exists(Path.Combine(dir, "RustDedicated.exe"));
        }
    }

    public class DataFileSystem
    {
        private readonly string _baseDir;
        public DataFileSystem(string baseDir) { _baseDir = baseDir; }

        private string PathFor(string name)
        {
            string rel = (name ?? string.Empty).Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(_baseDir, rel);
            return Path.Combine(_baseDir, rel + ".json");
        }

        public bool ExistsDatafile(string name) => File.Exists(PathFor(name));

        /// <summary>Oxide-compatible: returns full paths of *.json files under the given relative folder.</summary>
        public string[] GetFiles(string folder)
        {
            try
            {
                string rel = (folder ?? string.Empty).Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                while (rel.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    rel = rel.Substring(0, rel.Length - 1);
                string dir = Path.Combine(_baseDir, rel);
                if (!Directory.Exists(dir))
                    return Array.Empty<string>();
                return Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmBoss] DataFileSystem.GetFiles('" + folder + "') failed: " + ex.Message);
                return Array.Empty<string>();
            }
        }

        public T ReadObject<T>(string name)
        {
            try
            {
                string path = PathFor(name);
                if (!File.Exists(path))
                    return default;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return default;
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmBoss] DataFileSystem.ReadObject('" + name + "') failed: " + ex.Message);
                return default;
            }
        }

        public void WriteObject<T>(string name, T obj, bool sync = false)
        {
            try
            {
                string path = PathFor(name);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(obj, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmBoss] DataFileSystem.WriteObject('" + name + "') failed: " + ex.Message);
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
            {
                map = new Dictionary<string, string>();
                _messages[lang] = map;
            }
            foreach (var kv in messages)
                map[kv.Key] = kv.Value;
        }

        public string GetMessage(string key, object plugin = null, string userId = null)
        {
            if (string.IsNullOrEmpty(key)) return key;
            if (_messages.TryGetValue("en", out var en) && en.TryGetValue(key, out var msg))
                return msg;
            foreach (var map in _messages.Values)
                if (map.TryGetValue(key, out var any))
                    return any;
            return key;
        }
    }
}

namespace Oxide.Core.Plugins
{
    [AttributeUsage(AttributeTargets.Field)]
    public class PluginReferenceAttribute : Attribute
    {
        public PluginReferenceAttribute() { }
        public PluginReferenceAttribute(string name) { }
    }

    public class Plugin
    {
        public string Name { get; set; }
        public bool IsLoaded { get; set; }
        public virtual object Call(string hook, params object[] args) => null;
        public virtual T Call<T>(string hook, params object[] args)
        {
            object r = Call(hook, args);
            return r is T t ? t : default;
        }
    }

    public sealed class PveModePluginBridge : Plugin
    {
        public PveModePluginBridge()
        {
            Name = "PveMode";
            IsLoaded = true;
        }

        public override object Call(string hook, params object[] args)
        {
            if (string.IsNullOrEmpty(hook)) return null;
            args ??= Array.Empty<object>();
            try
            {
                Type apiType = AppDomain.CurrentDomain.GetData("PveMode_ApiType") as Type;
                if (apiType == null) return null;
                object instance = apiType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (instance == null) return null;

                MethodInfo call = instance.GetType().GetMethod("Call", new[] { typeof(string), typeof(object[]) });
                if (call != null)
                    return call.Invoke(instance, new object[] { hook, args });

                MethodInfo mi = instance.GetType().GetMethod(hook, BindingFlags.Public | BindingFlags.Instance);
                return mi?.Invoke(instance, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmBoss] PveMode.Call " + hook + " failed: " + (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }

        public static bool IsApiLive()
        {
            try
            {
                Type apiType = AppDomain.CurrentDomain.GetData("PveMode_ApiType") as Type;
                if (apiType == null) return false;
                return apiType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) != null;
            }
            catch { return false; }
        }
    }

    public sealed class KitsPluginBridge : Plugin
    {
        public KitsPluginBridge()
        {
            Name = "Kits";
            IsLoaded = true;
        }

        public static bool IsApiLive()
        {
            try { return AppDomain.CurrentDomain.GetData("Kits_ApiType") is Type; }
            catch { return false; }
        }

        public static object TryGiveKit(object[] args)
        {
            try
            {
                Type api = AppDomain.CurrentDomain.GetData("Kits_ApiType") as Type;
                if (api == null || args == null || args.Length < 2 || !(args[0] is BasePlayer player))
                    return null;
                string kitName = args[1] as string ?? args[1]?.ToString();
                MethodInfo give = api.GetMethod("GiveKit", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(BasePlayer), typeof(string) }, null);
                return give?.Invoke(null, new object[] { player, kitName });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmBoss] Kits GiveKit failed: " + (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }

        public override object Call(string hook, params object[] args)
        {
            if (string.Equals(hook, "GiveKit", StringComparison.OrdinalIgnoreCase))
                return TryGiveKit(args);
            return null;
        }
    }

    /// <summary>
    /// Forwards SpawnAnimal to the AnimalSpawn Harmony mod (AppDomain AnimalSpawn.Instance).
    /// Horse shop limits stay in Shop — this bridge is spawn/AI only.
    /// </summary>
    public sealed class AnimalSpawnPluginBridge : Plugin
    {
        public AnimalSpawnPluginBridge()
        {
            Name = "AnimalSpawn";
            IsLoaded = true;
        }

        public static bool IsApiLive()
        {
            try
            {
                object instance = AppDomain.CurrentDomain.GetData("AnimalSpawn.Instance");
                if (instance != null) return true;
                return AppDomain.CurrentDomain.GetData("AnimalSpawn.Type") is Type
                    || AppDomain.CurrentDomain.GetData("AnimalSpawn_ApiType") is Type;
            }
            catch { return false; }
        }

        public override object Call(string hook, params object[] args)
        {
            if (string.IsNullOrEmpty(hook)) return null;
            args ??= Array.Empty<object>();
            try
            {
                object instance = AppDomain.CurrentDomain.GetData("AnimalSpawn.Instance");
                Type apiType = instance?.GetType()
                    ?? AppDomain.CurrentDomain.GetData("AnimalSpawn.Type") as Type
                    ?? AppDomain.CurrentDomain.GetData("AnimalSpawn_ApiType") as Type;
                if (apiType == null) return null;
                if (instance == null)
                    instance = apiType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (instance == null) return null;

                MethodInfo call = instance.GetType().GetMethod("Call", new[] { typeof(string), typeof(object[]) });
                if (call != null)
                    return call.Invoke(instance, new object[] { hook, args });

                if (string.Equals(hook, "SpawnAnimal", StringComparison.OrdinalIgnoreCase) && args.Length >= 2 && args[0] is UnityEngine.Vector3 pos)
                {
                    MethodInfo spawn = instance.GetType().GetMethod("SpawnAnimal", BindingFlags.Public | BindingFlags.Instance, null,
                        new[] { typeof(UnityEngine.Vector3), typeof(object) }, null);
                    return spawn?.Invoke(instance, new object[] { pos, args[1] });
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmBoss] AnimalSpawn.Call " + hook + " failed: " + (ex.InnerException?.Message ?? ex.Message));
            }
            return null;
        }
    }

    public class PluginManager
    {
        public bool Exists(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (string.Equals(name, "NpcSpawn", StringComparison.OrdinalIgnoreCase))
                return global::GrimmBoss.GrimmBossGrimmNpc.Available || global::GrimmBoss.GrimmBossGrimmNpc.TryBindQuiet();
            if (string.Equals(name, "PveMode", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "0PveMode", StringComparison.OrdinalIgnoreCase))
                return PveModePluginBridge.IsApiLive();
            if (string.Equals(name, "Kits", StringComparison.OrdinalIgnoreCase))
                return KitsPluginBridge.IsApiLive();
            if (string.Equals(name, "AnimalSpawn", StringComparison.OrdinalIgnoreCase))
                return AnimalSpawnPluginBridge.IsApiLive();
            return false;
        }

        public Plugin Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (string.Equals(name, "PveMode", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "0PveMode", StringComparison.OrdinalIgnoreCase))
                return PveModePluginBridge.IsApiLive() ? new PveModePluginBridge() : null;
            if (string.Equals(name, "Kits", StringComparison.OrdinalIgnoreCase))
                return KitsPluginBridge.IsApiLive() ? new KitsPluginBridge() : null;
            if (string.Equals(name, "AnimalSpawn", StringComparison.OrdinalIgnoreCase))
                return AnimalSpawnPluginBridge.IsApiLive() ? new AnimalSpawnPluginBridge() : null;
            return null;
        }
    }
}

namespace Oxide.Plugins
{
    [AttributeUsage(AttributeTargets.Class)]
    public class InfoAttribute : Attribute
    {
        public string Title { get; }
        public string Author { get; }
        public string Version { get; }
        public InfoAttribute(string title, string author, string version)
        {
            Title = title;
            Author = author;
            Version = version;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class DescriptionAttribute : Attribute
    {
        public string Description { get; }
        public DescriptionAttribute(string description) { Description = description; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class ChatCommandAttribute : Attribute
    {
        public string Command { get; }
        public ChatCommandAttribute(string command) { Command = command; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class ConsoleCommandAttribute : Attribute
    {
        public string Command { get; }
        public ConsoleCommandAttribute(string command) { Command = command; }
    }

    public class DynamicConfigFile
    {
        public string Filename;
        private string _json;

        public void Load()
        {
            _json = File.Exists(Filename) ? File.ReadAllText(Filename) : null;
        }

        public T ReadObject<T>()
        {
            if (string.IsNullOrWhiteSpace(_json))
                return default;
            return JsonConvert.DeserializeObject<T>(_json);
        }

        public void WriteObject<T>(T obj, bool sync = false)
        {
            try
            {
                string dir = Path.GetDirectoryName(Filename);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                _json = JsonConvert.SerializeObject(obj, Formatting.Indented);
                File.WriteAllText(Filename, _json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmBoss] Config write failed: " + ex.Message);
            }
        }
    }

    public class ServerShim
    {
        public void Command(string command, params object[] args)
        {
            if (string.IsNullOrEmpty(command)) return;
            try { ConsoleSystem.Run(ConsoleSystem.Option.Server, command, args); }
            catch (Exception ex) { Debug.LogWarning("[GrimmBoss] Server.Command failed: " + ex.Message); }
        }

        public void Broadcast(string message)
        {
            var list = BasePlayer.activePlayerList;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
                list[i]?.ChatMessage(message);
        }
    }

    public sealed class TimerHelper
    {
        public void Once(float delay, Action action)
        {
            if (action == null) return;
            global::GrimmBoss.ModRunner.StartCoroutineStatic(OnceCo(delay, action));
        }

        public void In(float delay, Action action) => Once(delay, action);

        private static IEnumerator OnceCo(float delay, Action action)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            try { action(); }
            catch (Exception ex) { Debug.LogWarning("[GrimmBoss] timer: " + ex.Message); }
        }
    }

    public abstract class RustPlugin
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public VersionNumber Version { get; set; }

        public readonly Lang lang = new Lang();
        public readonly PluginManager plugins = new PluginManager();
        public readonly ServerShim Server = new ServerShim();
        public readonly TimerHelper timer = new TimerHelper();

        protected DynamicConfigFile Config { get; private set; } = new DynamicConfigFile();

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
            else
            {
                Name = GetType().Name;
                Title = Name;
            }
        }

        private static VersionNumber ParseVersion(string v)
        {
            try
            {
                if (string.IsNullOrEmpty(v)) return new VersionNumber(1, 0, 0);
                string[] parts = v.Split('.');
                int a = parts.Length > 0 ? int.Parse(parts[0]) : 0;
                int b = parts.Length > 1 ? int.Parse(parts[1]) : 0;
                int c = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                return new VersionNumber(a, b, c);
            }
            catch { return new VersionNumber(1, 0, 0); }
        }

        private static string ResolveConfigPath(string name)
        {
            string baseDir = Oxide.Core.OxideMod.ResolveServerRoot();
            string[] candidates =
            {
                Path.Combine(baseDir, "HarmonyConfig", name + ".json"),
                Path.Combine(baseDir, "oxide", "config", name + ".json"),
                Path.Combine(baseDir, "config", name + ".json"),
                Path.Combine(baseDir, name + ".json"),
            };
            foreach (string c in candidates)
                if (File.Exists(c))
                    return c;
            return candidates[0];
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

        public void HarmonyLoadConfig() => LoadConfig();
        public void HarmonyLoadDefaultMessages() => LoadDefaultMessages();

        public void Puts(string format, params object[] args) => Debug.Log("[" + (Title ?? "GrimmBoss") + "] " + Fmt(format, args));
        public void PrintWarning(string format, params object[] args) => Debug.LogWarning("[" + (Title ?? "GrimmBoss") + "] " + Fmt(format, args));
        public void PrintError(string format, params object[] args) => Debug.LogError("[" + (Title ?? "GrimmBoss") + "] " + Fmt(format, args));

        public void PrintToChat(BasePlayer player, string format, params object[] args)
        {
            if (player == null || !player.IsConnected) return;
            player.ChatMessage(Fmt(format, args));
        }

        public void PrintToChat(string format, params object[] args) => Server.Broadcast(Fmt(format, args));

        private static string Fmt(string format, object[] args)
        {
            if (args == null || args.Length == 0 || string.IsNullOrEmpty(format)) return format;
            try { return string.Format(format, args); }
            catch { return format; }
        }

        public void NextTick(Action action)
        {
            if (action == null) return;
            global::GrimmBoss.ModRunner.Enqueue(action);
        }

        public void Subscribe(string hook) { }
        public void Unsubscribe(string hook) { }
    }
}
