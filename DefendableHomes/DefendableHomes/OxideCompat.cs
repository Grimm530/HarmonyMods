// Oxide -> Harmony compatibility shims for the ported DefendableHomes plugin.
// These provide the small Oxide API surface the plugin uses (RustPlugin base, Plugin
// references, Interface.Oxide data/config, VersionNumber, lang, command attributes) so the
// original plugin body compiles almost verbatim. No Oxide runtime is required.
using System;
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
    public struct VersionNumber : IEquatable<VersionNumber>
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

        public bool Equals(VersionNumber other) => Major == other.Major && Minor == other.Minor && Patch == other.Patch;
        public override bool Equals(object obj) => obj is VersionNumber vn && Equals(vn);
        public override int GetHashCode() => (Major * 397 ^ Minor) * 397 ^ Patch;
        public static bool operator ==(VersionNumber a, VersionNumber b) => a.Equals(b);
        public static bool operator !=(VersionNumber a, VersionNumber b) => !a.Equals(b);
        public static bool operator <(VersionNumber a, VersionNumber b)
        {
            if (a.Major != b.Major) return a.Major < b.Major;
            if (a.Minor != b.Minor) return a.Minor < b.Minor;
            return a.Patch < b.Patch;
        }
        public static bool operator >(VersionNumber a, VersionNumber b) => b < a;
        public static bool operator <=(VersionNumber a, VersionNumber b) => a < b || a == b;
        public static bool operator >=(VersionNumber a, VersionNumber b) => a > b || a == b;
        public override string ToString() => $"{Major}.{Minor}.{Patch}";
    }

    /// <summary>Minimal replacement for Oxide's Interface.Oxide + Interface.CallHook.</summary>
    public static class Interface
    {
        public static readonly OxideMod Oxide = new OxideMod();

        /// <summary>
        /// Optional cross-plugin notifications stay no-ops. GrimmNPC-originated hooks
        /// (OnCustomNpcTarget / OnBomberExplosion / OnCustomNpcParentEnd) are dispatched
        /// via AppDomain Harmony_CallHookList from 0GrimmNPC, not this method.
        /// </summary>
        public static object CallHook(string hook, params object[] args) => null;
    }

    public class OxideMod
    {
        private DataFileSystem _dataFileSystem;
        private string _rootDirectory;

        /// <summary>
        /// Server root (folder that contains HarmonyConfig / HarmonyData / HarmonyMods).
        /// Unity sets BaseDirectory to RustDedicated_Data/Managed — do not use that as the root.
        /// Prefer Application.dataPath/.. (same as AdminMenu / RustRewards).
        /// </summary>
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

        /// <summary>
        /// Oxide-style data root. Plugin paths like Images/ resolve under HarmonyData/.
        /// </summary>
        public string DataDirectory => Path.Combine(RootDirectory, "HarmonyData");

        public DataFileSystem DataFileSystem => _dataFileSystem ??= new DataFileSystem(DataDirectory);

        /// <summary>Soft no-op: never unload the Harmony mod on missing images / optional deps.</summary>
        public void UnloadPlugin(string name)
        {
            Debug.LogWarning("[DefendableHomes] UnloadPlugin('" + name + "') requested but ignored (Harmony port keeps running / soft-fail).");
        }

        public object CallHook(string hook, params object[] args) => Interface.CallHook(hook, args);

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
            return Path.Combine(_baseDir, rel + ".json");
        }

        public bool ExistsDatafile(string name) => File.Exists(PathFor(name));

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
                Debug.LogWarning("[DefendableHomes] DataFileSystem.ReadObject('" + name + "') failed: " + ex.Message);
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
                Debug.LogWarning("[DefendableHomes] DataFileSystem.WriteObject('" + name + "') failed: " + ex.Message);
            }
        }
    }
}

namespace Oxide.Core.Libraries
{
    public class Lang
    {
        // langCode -> (key -> message)
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

    /// <summary>Stand-in for an Oxide plugin reference. Base does nothing; subclasses can override Call.</summary>
    public class Plugin
    {
        public string Name { get; set; }
        public string Author { get; set; }
        public bool IsLoaded { get; set; }
        public virtual object Call(string hook, params object[] args) => null;
        public virtual T Call<T>(string hook, params object[] args)
        {
            object r = Call(hook, args);
            return r is T t ? t : default;
        }
    }

    /// <summary>
    /// Bridges Oxide-style Plugin.Call to the shared 0PveMode Harmony mod via AppDomain
    /// (<c>PveMode_ApiType</c>), same pattern as TruePVE / Convoy.
    /// </summary>
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
                Debug.LogWarning("[DefendableHomes] PveMode.Call " + hook + " failed: " + (ex.InnerException?.Message ?? ex.Message));
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

    /// <summary>Reports which optional plugins exist. NpcSpawn (GrimmNPC) + live Economics Harmony mod.</summary>
    public class PluginManager
    {
        public bool Exists(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (string.Equals(name, "NpcSpawn", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "GrimmNPC", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "0GrimmNPC", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(name, "Economics", StringComparison.OrdinalIgnoreCase))
                return EconomicsPluginBridge.IsApiLive();
            if (string.Equals(name, "Clans", StringComparison.OrdinalIgnoreCase))
                return AppDomain.CurrentDomain.GetData("Clans_ApiType") is Type;
            if (string.Equals(name, "Friends", StringComparison.OrdinalIgnoreCase))
                return AppDomain.CurrentDomain.GetData("Friends_ApiType") is Type;
            if (string.Equals(name, "PveMode", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "0PveMode", StringComparison.OrdinalIgnoreCase))
                return PveModePluginBridge.IsApiLive();
            return false;
        }

        public Plugin Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (string.Equals(name, "Economics", StringComparison.OrdinalIgnoreCase))
                return EconomicsPluginBridge.IsApiLive() ? new EconomicsPluginBridge() : null;
            if (string.Equals(name, "PveMode", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "0PveMode", StringComparison.OrdinalIgnoreCase))
                return PveModePluginBridge.IsApiLive() ? new PveModePluginBridge() : null;
            return null;
        }
    }

    public sealed class EconomicsPluginBridge : Plugin
    {
        public EconomicsPluginBridge()
        {
            Name = "Economics";
            IsLoaded = true;
        }

        public static bool IsApiLive()
        {
            try { return AppDomain.CurrentDomain.GetData("Economics_Plugin") != null || AppDomain.CurrentDomain.GetData("Economics_ApiType") is Type; }
            catch { return false; }
        }

        public override object Call(string hook, params object[] args)
        {
            if (string.IsNullOrEmpty(hook)) return null;
            args ??= Array.Empty<object>();
            try
            {
                object plugin = AppDomain.CurrentDomain.GetData("Economics_Plugin");
                if (plugin != null)
                {
                    MethodInfo mi = plugin.GetType().GetMethod(hook, BindingFlags.Public | BindingFlags.Instance);
                    if (mi != null) return mi.Invoke(plugin, args);
                }

                Type apiType = AppDomain.CurrentDomain.GetData("Economics_ApiType") as Type;
                object instance = apiType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                MethodInfo method = instance?.GetType().GetMethod(hook, BindingFlags.Public | BindingFlags.Instance);
                return method?.Invoke(instance, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DefendableHomes] Economics.Call " + hook + " failed: " + (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
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

    /// <summary>Config file wrapper matching the Oxide DynamicConfigFile surface used by the plugin.</summary>
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
                Debug.LogWarning("[DefendableHomes] Config write failed: " + ex.Message);
            }
        }
    }

    public class ServerShim
    {
        public void Command(string command, params object[] args)
        {
            if (string.IsNullOrEmpty(command)) return;
            try { ConsoleSystem.Run(ConsoleSystem.Option.Server, command, args); }
            catch (Exception ex) { Debug.LogWarning("[DefendableHomes] Server.Command failed: " + ex.Message); }
        }

        public void Broadcast(string message)
        {
            var list = BasePlayer.activePlayerList;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
                list[i]?.ChatMessage(message);
        }
    }

    /// <summary>Minimal RustPlugin replacement providing the members the ported plugin uses.</summary>
    public abstract class RustPlugin
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public VersionNumber Version { get; set; }

        public readonly Lang lang = new Lang();
        public readonly PluginManager plugins = new PluginManager();
        public readonly ServerShim Server = new ServerShim();
        public readonly PermissionLibrary permission = new PermissionLibrary();
        public readonly CommandLibrary cmd = new CommandLibrary();
        protected readonly ServerShim covalence = null; // unused

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
            return candidates[0]; // default write target: HarmonyConfig
        }

        // ----- config lifecycle (overridden by the plugin) -----
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

        // Invoked by ArmoredTrainMod to drive the Oxide-style lifecycle.
        public void HarmonyLoadConfig() => LoadConfig();
        public void HarmonyLoadDefaultMessages() => LoadDefaultMessages();

        // ----- logging / chat -----
        public void Puts(string format, params object[] args) => Debug.Log("[" + (Title ?? "DefendableHomes") + "] " + Fmt(format, args));
        public void PrintWarning(string format, params object[] args) => Debug.LogWarning("[" + (Title ?? "DefendableHomes") + "] " + Fmt(format, args));
        public void PrintError(string format, params object[] args) => Debug.LogError("[" + (Title ?? "DefendableHomes") + "] " + Fmt(format, args));

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

        // ----- scheduling -----
        public void NextTick(Action action)
        {
            if (action == null) return;
            global::DefendableHomes.ModRunner.Enqueue(action);
        }

        // ----- Oxide hook (un)subscription becomes a no-op; patches always dispatch and early-out. -----
        public void Subscribe(string hook) { }
        public void Unsubscribe(string hook) { }
    }

    /// <summary>Oxide permission.* via the 0Permissions Harmony mod (AppDomain Permissions_ApiType).</summary>
    public class PermissionLibrary
    {
        public void RegisterPermission(string perm, object plugin = null)
        {
            if (string.IsNullOrEmpty(perm)) return;
            try
            {
                object service = GetService();
                service?.GetType().GetMethod("RegisterPermission", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null)
                    ?.Invoke(service, new object[] { perm });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DefendableHomes] RegisterPermission failed: " + ex.Message);
            }
        }

        public bool UserHasPermission(string userId, string perm)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(perm)) return false;
            try
            {
                object service = GetService();
                object result = service?.GetType().GetMethod("UserHasPermission", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string), typeof(string) }, null)
                    ?.Invoke(service, new object[] { userId, perm });
                return result is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        private static object GetService()
        {
            Type apiType = AppDomain.CurrentDomain.GetData("Permissions_ApiType") as Type;
            if (apiType == null) return null;
            object instance = apiType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            return instance?.GetType().GetProperty("Service", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
        }
    }

    /// <summary>Oxide cmd.AddChatCommand — registers on DefendableHomesMod after it is loaded.</summary>
    public class CommandLibrary
    {
        public void AddChatCommand(string name, object plugin, Action<BasePlayer, string, string[]> callback)
        {
            if (string.IsNullOrEmpty(name) || callback == null) return;
            global::DefendableHomes.DefendableHomesMod.Instance?.RegisterChatCommand(name, callback);
        }
    }
}
