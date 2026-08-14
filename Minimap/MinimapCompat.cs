using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    public class Hash<TKey, TValue> : Dictionary<TKey, TValue>
    {
        public new TValue this[TKey key]
        {
            get => TryGetValue(key, out var value) ? value : default;
            set => base[key] = value;
        }
    }
}

namespace Oxide.Ext.Chaos.Data
{
    public class Datafile<T> where T : class, new()
    {
        private readonly string _path;
        public T Data { get; set; }

        public Datafile(string relativeName)
        {
            var root = MinimapHarmony.MinimapHost.Instance?.DataDirectory
                       ?? Path.Combine(MinimapHarmony.MinimapHost.Instance?.ServerRoot ?? ".", "HarmonyData", "Minimap");
            if (!Directory.Exists(root))
                Directory.CreateDirectory(root);

            relativeName = (relativeName ?? "data").Replace('\\', '/').Trim('/');
            if (!relativeName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                relativeName += ".json";

            _path = Path.Combine(root, relativeName.Replace('/', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    Data = JsonConvert.DeserializeObject<T>(File.ReadAllText(_path)) ?? new T();
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] Datafile load: " + ex.Message);
            }
            Data = new T();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_path, JsonConvert.SerializeObject(Data ?? new T(), Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] Datafile save: " + ex.Message);
            }
        }
    }
}

namespace MinimapHarmony
{
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

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InfoAttribute : Attribute
    {
        public InfoAttribute(string title, string author, string version) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class PermissionAttribute : Attribute
    {
    }

    public static class PlayerExtensions
    {
        public static bool HasPermission(this BasePlayer player, string perm)
        {
            if (player == null || string.IsNullOrEmpty(perm)) return false;
            return PermissionsBridge.UserHasPermission(player.UserIDString, perm);
        }

        public static void LocalizedMessage(this BasePlayer player, object plugin, string key, params object[] args)
        {
            if (player == null) return;
            string msg = MinimapHost.Instance?.Lang?.GetMessage(key, player.UserIDString) ?? key ?? "";
            if (args != null && args.Length > 0)
            {
                try { msg = string.Format(msg, args); }
                catch { }
            }
            player.ChatMessage(msg);
        }
    }

    public class PermissionLib
    {
        public bool UserHasPermission(string playerId, string perm) =>
            PermissionsBridge.UserHasPermission(playerId, perm);

        public void RegisterPermission(string perm, object owner = null) =>
            PermissionsBridge.RegisterPermission(perm);

        public bool PermissionExists(string perm, object owner = null) =>
            PermissionsBridge.PermissionExists(perm);

        public bool GrantGroupPermission(string group, string perm, object owner = null) =>
            PermissionsBridge.GrantGroupPermission(group, perm, owner);

        public bool GroupExists(string group) => PermissionsBridge.GroupExists(group);

        public bool CreateGroup(string name, string title, int rank) =>
            PermissionsBridge.CreateGroup(name, title, rank);
    }

    public class LangHelper
    {
        private readonly Dictionary<string, string> _embedded =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _file =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int FileMessageCount => _file.Count;

        public void RegisterMessages(Dictionary<string, string> messages)
        {
            if (messages == null) return;
            foreach (var kv in messages)
                _embedded[kv.Key] = kv.Value;
        }

        public bool LoadLanguageFile(string path)
        {
            _file = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            try
            {
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (loaded == null) return false;
                foreach (var kv in loaded)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                        _file[kv.Key] = kv.Value;
                }
                return _file.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] Lang file load failed: " + ex.Message);
                return false;
            }
        }

        public string GetMessage(string key, string userId = null)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (_file.TryGetValue(key, out var fromFile) && !string.IsNullOrEmpty(fromFile))
                return fromFile;
            if (_embedded.TryGetValue(key, out var msg))
                return msg;
            return key;
        }
    }

    public class MinimapHost
    {
        public static MinimapHost Instance { get; private set; }

        public string ServerRoot { get; private set; }
        public string ConfigDirectory { get; private set; }
        public string DataDirectory { get; private set; }
        public string LangDirectory { get; private set; }
        public string ImagesDirectory { get; private set; }
        public string ConfigPath { get; private set; }
        public string LangPath { get; private set; }

        public PermissionLib Permission { get; } = new PermissionLib();
        public LangHelper Lang { get; } = new LangHelper();

        public static void Init(string serverRoot)
        {
            Instance = new MinimapHost();
            Instance.ServerRoot = serverRoot ?? ".";
            Instance.ConfigDirectory = Path.Combine(Instance.ServerRoot, "HarmonyConfig");
            Instance.DataDirectory = Path.Combine(Instance.ServerRoot, "HarmonyData", "Minimap");
            Instance.LangDirectory = Path.Combine(Instance.ServerRoot, "HarmonyLanguage");
            Instance.ImagesDirectory = Path.Combine(Instance.ServerRoot, "HarmonyImages", "Minimap");
            Instance.ConfigPath = Path.Combine(Instance.ConfigDirectory, "Minimap.json");
            Instance.LangPath = Path.Combine(Instance.LangDirectory, "Minimap.json");

            Directory.CreateDirectory(Instance.ConfigDirectory);
            Directory.CreateDirectory(Instance.DataDirectory);
            Directory.CreateDirectory(Instance.LangDirectory);
            Directory.CreateDirectory(Instance.ImagesDirectory);

            Debug.Log($"[Minimap] Config: {Instance.ConfigPath}");
            Debug.Log($"[Minimap] Data:   {Instance.DataDirectory}");
            Debug.Log($"[Minimap] Lang:   {Instance.LangPath}");
            Debug.Log($"[Minimap] Images: {Instance.ImagesDirectory}");
        }

        public void ReloadLanguage()
        {
            if (Lang.LoadLanguageFile(LangPath))
                Debug.Log($"[Minimap] OK: Loaded {Lang.FileMessageCount} lang entries from HarmonyLanguage/Minimap.json");
            else
                Debug.LogWarning("[Minimap] HarmonyLanguage/Minimap.json missing or empty — using embedded defaults");
        }

        public static void Shutdown()
        {
            Instance = null;
        }
    }
}

namespace Oxide.Ext.Chaos
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class PermissionAttribute : Attribute
    {
    }
}
