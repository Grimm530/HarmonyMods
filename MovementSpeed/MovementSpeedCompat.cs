/*
 * Oxide-free shims for MovementSpeed Harmony port.
 * Config: HarmonyConfig/MovementSpeed.json
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
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
        public static object CallHook(string hook, params object[] args) => null;
        public static void NextTick(Action action) => MovementSpeedHarmony.ModRunner.Enqueue(action);
    }

    public class OxideMod
    {
        private string _root;
        public string RootDirectory => _root ??= ResolveServerRoot();
        public string ConfigDirectory => Path.Combine(RootDirectory, "HarmonyConfig");
        public string DataDirectory => Path.Combine(RootDirectory, "HarmonyData");
        public void UnloadPlugin(string name) =>
            Debug.LogWarning("[MovementSpeed] UnloadPlugin('" + name + "') ignored (Harmony soft-fail).");
        public void LogInfo(string msg) => Debug.Log("[MovementSpeed] " + msg);

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
        public static implicit operator bool(Plugin p) => p != null && p.IsLoaded;
    }

    public class PluginManager
    {
        public Plugin Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            // ZoneManager soft-fail
            return null;
        }
        public bool Exists(string name) => Find(name) != null;
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
            catch (Exception ex) { Debug.LogWarning("[MovementSpeed] Config write failed: " + ex.Message); }
        }
    }

    public class CommandLib
    {
        private readonly List<(string name, string method)> _chat = new List<(string, string)>();
        private readonly List<(string name, string method)> _console = new List<(string, string)>();
        public IReadOnlyList<(string name, string method)> RegisteredChatCommands => _chat;
        public IReadOnlyList<(string name, string method)> RegisteredConsoleCommands => _console;

        public void AddChatCommand(string name, object plugin, string method)
        {
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(method))
                _chat.Add((name, method));
        }
        public void AddConsoleCommand(string name, object plugin, string method)
        {
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(method))
                _console.Add((name, method));
        }
        public void RemoveChatCommand(string name, object plugin)
        {
            if (string.IsNullOrEmpty(name)) return;
            for (int i = _chat.Count - 1; i >= 0; i--)
                if (string.Equals(_chat[i].name, name, StringComparison.OrdinalIgnoreCase))
                    _chat.RemoveAt(i);
        }
        public void RemoveConsoleCommand(string name, object plugin)
        {
            if (string.IsNullOrEmpty(name)) return;
            for (int i = _console.Count - 1; i >= 0; i--)
                if (string.Equals(_console[i].name, name, StringComparison.OrdinalIgnoreCase))
                    _console.RemoveAt(i);
        }
    }

    public class PermissionLib
    {
        private static Type _permType;
        private static MethodInfo _userHas, _register, _exists, _groupHas, _getUsersInGroup;

        private static void EnsureBound()
        {
            if (_permType != null) return;
            try
            {
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
                _groupHas = _permType.GetMethod("GroupHasPermission", S, null, new[] { typeof(string), typeof(string) }, null);
                // GetUsersInGroup may be instance on Service — try static first
                _getUsersInGroup = _permType.GetMethod("GetUsersInGroup", S, null, new[] { typeof(string) }, null);
            }
            catch (Exception ex) { Debug.LogWarning("[MovementSpeed] Permissions bind: " + ex.Message); }
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

        public bool GroupHasPermission(string group, string perm)
        {
            if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(perm)) return false;
            EnsureBound();
            try { if (_groupHas?.Invoke(null, new object[] { group, perm }) is bool b) return b; } catch { }
            return false;
        }

        public string[] GetUsersInGroup(string group)
        {
            EnsureBound();
            try
            {
                if (_getUsersInGroup != null)
                {
                    var r = _getUsersInGroup.Invoke(null, new object[] { group });
                    if (r is string[] arr) return arr;
                    if (r is System.Collections.IEnumerable en)
                    {
                        var list = new List<string>();
                        foreach (var o in en) if (o != null) list.Add(o.ToString());
                        return list.ToArray();
                    }
                }
                // Fallback via Permissions Service instance
                var instanceProp = _permType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                var instance = instanceProp?.GetValue(null);
                var service = instance?.GetType().GetProperty("Service")?.GetValue(instance);
                var mi = service?.GetType().GetMethod("GetUsersInGroup", new[] { typeof(string) });
                var r2 = mi?.Invoke(service, new object[] { group });
                if (r2 is string[] a2) return a2;
            }
            catch { }
            return Array.Empty<string>();
        }
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
            string preferred = Path.Combine(root, "HarmonyConfig", name + ".json");
            if (File.Exists(preferred)) return preferred;
            return preferred;
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

        public void HarmonyLoadConfig() => LoadConfig();

        public void Puts(string format, params object[] args) =>
            Debug.Log("[" + (Title ?? "MovementSpeed") + "] " + Fmt(format, args));
        public void PrintWarning(string format, params object[] args) =>
            Debug.LogWarning("[" + (Title ?? "MovementSpeed") + "] " + Fmt(format, args));
        public void PrintError(string format, params object[] args) =>
            Debug.LogError("[" + (Title ?? "MovementSpeed") + "] " + Fmt(format, args));
        public void PrintToChat(BasePlayer player, string message)
        {
            if (player == null || !player.IsConnected) return;
            player.ChatMessage(message ?? "");
        }
        public void PrintToConsole(string message) => Debug.Log("[" + (Title ?? "MovementSpeed") + "] " + message);

        public void LogToFile(string filename, string text, object plugin, bool timestamp = true, bool console = false)
        {
            try
            {
                string root = OxideMod.ResolveServerRoot();
                string dir = Path.Combine(root, "HarmonyData", "MovementSpeed", "logs");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, filename + ".txt");
                string line = (timestamp ? "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " : "") + text + Environment.NewLine;
                File.AppendAllText(path, line);
                if (console) Debug.Log("[MovementSpeed] " + text);
            }
            catch { }
        }

        public void Subscribe(string hook) { }
        public void Unsubscribe(string hook) { }

        private static string Fmt(string format, object[] args)
        {
            if (args == null || args.Length == 0 || string.IsNullOrEmpty(format)) return format;
            try { return string.Format(format, args); } catch { return format; }
        }
    }
}

namespace MovementSpeedHarmony
{
    public class ModRunner : MonoBehaviour
    {
        public static ModRunner Instance { get; private set; }
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static GameObject _go;

        public static void Ensure()
        {
            if (Instance != null) return;
            _go = new GameObject("MovementSpeed_Runner");
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
                catch (Exception ex) { Debug.LogWarning("[MovementSpeed] NextTick: " + ex.Message); }
            }
        }
    }
}
