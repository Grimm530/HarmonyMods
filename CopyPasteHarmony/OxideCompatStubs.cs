using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Minimal Oxide "API surface" so CopyPaste.cs can compile and run inside a Harmony mod.
// This is not the full Oxide runtime; it only implements what CopyPaste actually uses.

namespace Oxide.Core
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class InfoAttribute : Attribute
    {
        public string Title { get; }
        public string Author { get; }
        public string VersionString { get; }

        public InfoAttribute(string title, string author, string version)
        {
            Title = title;
            Author = author;
            VersionString = version;
        }
    }

    public struct VersionNumber : IComparable<VersionNumber>
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

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

        public static bool operator <(VersionNumber a, VersionNumber b) => a.CompareTo(b) < 0;
        public static bool operator >(VersionNumber a, VersionNumber b) => a.CompareTo(b) > 0;
        public static bool operator <=(VersionNumber a, VersionNumber b) => a.CompareTo(b) <= 0;
        public static bool operator >=(VersionNumber a, VersionNumber b) => a.CompareTo(b) >= 0;

        public override string ToString() => $"{Major}.{Minor}.{Patch}";
    }

    public static class Interface
    {
        public static OxideInterface Oxide { get; internal set; } = new OxideInterface();

        // CopyPaste uses this to fire "OnCopyFinished" and "OnPasteFinished" for other Oxide plugins.
        // Harmony port: no dispatcher; keep it as a no-op.
        public static void CallHook(string hookName, params object[] args)
        {
            // Intentionally no-op.
        }
    }

    public sealed class OxideInterface
    {
        public DataFileSystem DataFileSystem { get; set; }
    }

    public sealed class DataFileSystem
    {
        private readonly string _rootDir;

        // Cache loaded dataframes so GetFile(...)+Clear()+SaveDatafile(...) can work.
        private readonly Dictionary<string, DataFile> _loaded = new(StringComparer.OrdinalIgnoreCase);

        public DataFileSystem(string rootDir)
        {
            _rootDir = rootDir ?? throw new ArgumentNullException(nameof(rootDir));
        }

        private string NormalizeKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return path.Replace('\\', '/').TrimStart('/').Trim();
        }

        private string GetJsonFilePath(string datafilePath)
        {
            // Oxide typically takes "subdir/name" (no .json) and saves "subdir/name.json".
            var key = NormalizeKey(datafilePath);
            if (key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                key = key.Substring(0, key.Length - 5);

            return Path.Combine(_rootDir, key.Replace('/', Path.DirectorySeparatorChar) + ".json");
        }

        private static object ConvertJToken(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;

            switch (token.Type)
            {
                case JTokenType.Object:
                {
                    var obj = (JObject)token;
                    var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in obj.Properties())
                        dict[prop.Name] = ConvertJToken(prop.Value);
                    return dict;
                }
                case JTokenType.Array:
                {
                    var arr = (JArray)token;
                    var list = new List<object>(arr.Count);
                    foreach (var item in arr)
                        list.Add(ConvertJToken(item));
                    return list;
                }
                case JTokenType.Integer:
                    return token.ToObject<long>();
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

        private DataFile LoadFromDisk(string datafilePath)
        {
            var jsonPath = GetJsonFilePath(datafilePath);
            if (!File.Exists(jsonPath)) return new DataFile();

            var json = File.ReadAllText(jsonPath);
            if (string.IsNullOrWhiteSpace(json)) return new DataFile();

            var token = JToken.Parse(json);
            if (token.Type != JTokenType.Object) return new DataFile();

            var dictObj = (Dictionary<string, object>)ConvertJToken(token);
            var df = new DataFile();
            foreach (var kv in dictObj)
                df[kv.Key] = kv.Value;
            return df;
        }

        public bool ExistsDatafile(string path)
        {
            var jsonPath = GetJsonFilePath(path);
            return File.Exists(jsonPath);
        }

        public DataFile GetFile(string path)
        {
            var key = NormalizeKey(path);
            if (string.IsNullOrEmpty(key)) return new DataFile();

            if (_loaded.TryGetValue(key, out var existing))
                return existing;

            var df = LoadFromDisk(key);
            _loaded[key] = df;
            return df;
        }

        public DataFile GetDatafile(string path) => GetFile(path);

        public void SaveDatafile(string path)
        {
            var key = NormalizeKey(path);
            if (!_loaded.TryGetValue(key, out var df))
                df = GetFile(key);

            Directory.CreateDirectory(_rootDir);
            var jsonPath = GetJsonFilePath(key);
            var json = JsonConvert.SerializeObject(df, Formatting.Indented);
            File.WriteAllText(jsonPath, json);
        }

        public IEnumerable<string> GetFiles(string directory)
        {
            var dirKey = NormalizeKey(directory);
            if (string.IsNullOrEmpty(dirKey)) dirKey = "";

            // The caller passes "copypaste/" and expects "copypaste/<file>.json".
            var diskDir = Path.Combine(_rootDir, dirKey.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(diskDir)) yield break;

            foreach (var file in Directory.EnumerateFiles(diskDir, "*.json", SearchOption.TopDirectoryOnly))
            {
                var justName = Path.GetFileName(file);
                var rel = (dirKey.Length == 0 ? justName : dirKey.TrimEnd('/') + "/" + justName).Replace('\\', '/');
                yield return rel;
            }
        }
    }

    public sealed class DataFile : Dictionary<string, object>
    {
    }

    public sealed class ConfigFile
    {
        public JsonSerializerSettings Settings { get; } = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented
        };

        public string FilePath { get; }

        public ConfigFile(string filePath)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        }

        public T ReadObject<T>() where T : new()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new T();

                var json = File.ReadAllText(FilePath);
                if (string.IsNullOrWhiteSpace(json))
                    return new T();

                var obj = JsonConvert.DeserializeObject<T>(json, Settings);
                return obj ?? new T();
            }
            catch
            {
                return new T();
            }
        }

        public object ReadObject(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (!File.Exists(FilePath)) return Activator.CreateInstance(type);
            var json = File.ReadAllText(FilePath);
            return JsonConvert.DeserializeObject(json, type, Settings);
        }

        public void WriteObject<T>(T obj, bool pretty)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? ".");
                var formatting = pretty ? Formatting.Indented : Formatting.None;
                var json = JsonConvert.SerializeObject(obj, formatting, Settings);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CopyPasteHarmony] Config write failed: {ex}");
            }
        }
    }
}

namespace Oxide.Core.Libraries.Covalence
{
    public interface IPlayer
    {
        object Object { get; }
        string Id { get; }
        string UserIDString { get; }
        bool IsAdmin { get; }
        bool HasPermission(string permName);
        void Reply(string message);
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class CommandAttribute : Attribute
    {
        public string Name { get; }
        public CommandAttribute(string name) => Name = name;
    }

    public sealed class Timer
    {
        public void In(float seconds, Action action) => Once(seconds, action);

        public void Once(float seconds, Action action)
        {
            if (action == null) return;
            if (ServerMgr.Instance == null) return;
            ServerMgr.Instance.StartCoroutine(OnceCoroutine(seconds, action));
        }

        private static System.Collections.IEnumerator OnceCoroutine(float seconds, Action action)
        {
            if (seconds > 0f)
                yield return CoroutineEx.waitForSeconds(seconds);
            action?.Invoke();
        }
    }

    public sealed class Permission
    {
        private readonly HashSet<string> _registered = new(StringComparer.OrdinalIgnoreCase);

        public void RegisterPermission(string permName, object plugin)
        {
            if (string.IsNullOrWhiteSpace(permName)) return;
            _registered.Add(permName.Trim());
        }

        public bool UserHasPermission(string userId, string permName)
        {
            if (string.IsNullOrWhiteSpace(userId)) return false;
            if (!OxideCoreCompat.PermissionStore.IsAllowed(userId))
                return false;
            return true;
        }
    }

    public sealed class Lang
    {
        private readonly Dictionary<(Type pluginType, string langKey), Dictionary<string, string>> _messages = new();

        public void RegisterMessages(Dictionary<string, string> messages, object plugin, string langKey)
        {
            if (messages == null || plugin == null || string.IsNullOrWhiteSpace(langKey)) return;
            var key = (plugin.GetType(), langKey);
            if (!_messages.TryGetValue(key, out var dict))
            {
                dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _messages[key] = dict;
            }

            foreach (var kv in messages)
                dict[kv.Key] = kv.Value;
        }

        public string GetMessage(string key, object plugin, string userId)
        {
            if (string.IsNullOrEmpty(key) || plugin == null) return key;

            // Minimal: always "en" if available; otherwise first language.
            var pluginType = plugin.GetType();
            var preferred = ("en");
            if (_messages.TryGetValue((pluginType, preferred), out var enDict) && enDict.TryGetValue(key, out var enMsg))
                return enMsg;

            // Fallback: search any language dict.
            foreach (var kv in _messages)
            {
                if (kv.Key.pluginType != pluginType) continue;
                if (kv.Value.TryGetValue(key, out var anyMsg))
                    return anyMsg;
            }

            return key;
        }
    }

    public sealed class PlayerManager
    {
        public IPlayer FindPlayerById(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return null;
            if (!ulong.TryParse(userId, out var id)) return null;
            var bp = BasePlayer.FindByID(id);
            return bp == null ? null : new RustIPlayer(bp);
        }

        private sealed class RustIPlayer : IPlayer
        {
            private readonly BasePlayer _bp;
            public RustIPlayer(BasePlayer bp) => _bp = bp;
            public object Object => _bp;
            public string Id => _bp.userID.ToString();
            public string UserIDString => _bp.userID.ToString();
            public bool IsAdmin => _bp.IsAdmin;
            public bool HasPermission(string permName) => OxideCoreCompat.PermissionStore.IsAllowed(Id);
            public void Reply(string message)
            {
                if (_bp?.net?.connection == null) return;
                ConsoleNetwork.SendClientCommand(_bp.net.connection, "chat.add", 0, 0, message);
            }
        }
    }

    public class CovalencePlugin
    {
        protected Oxide.Core.ConfigFile Config { get; }
        protected Permission permission { get; }
        protected Lang lang { get; }
        protected Timer timer { get; }
        protected PlayerManager players { get; }

        // CopyPaste.cs expects a plugin version value (used in saved protocol dictionaries).
        public VersionNumber Version { get; }

        protected CovalencePlugin()
        {
            permission = new Permission();
            lang = new Lang();
            timer = new Timer();
            players = new PlayerManager();

            var typeName = GetType().Name;
            var serverRoot = OxideCoreCompat.OxideCompatBootstrap.ServerRoot;
            var configPath = Path.Combine(serverRoot, "HarmonyConfig", $"{typeName}.json");
            Config = new Oxide.Core.ConfigFile(configPath);

            // Best-effort: parse the [Info(..., ..., "<x.y.z>")] version string.
            var infoAttr = GetType().GetCustomAttribute<InfoAttribute>();
            Version = ParseVersionNumber(infoAttr?.VersionString) ?? new VersionNumber(0, 0, 0);

            // Ensure DataFileSystem exists for this process.
            if (Oxide.Core.Interface.Oxide.DataFileSystem == null)
            {
                var dataRoot = Path.Combine(serverRoot, "HarmonyData", typeName);
                Oxide.Core.Interface.Oxide.DataFileSystem = new Oxide.Core.DataFileSystem(dataRoot);
            }
        }

        private static VersionNumber? ParseVersionNumber(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var parts = s.Split('.');
            if (parts.Length < 3) return null;
            if (!int.TryParse(parts[0], out var major)) return null;
            if (!int.TryParse(parts[1], out var minor)) return null;
            if (!int.TryParse(parts[2], out var patch)) return null;
            return new VersionNumber(major, minor, patch);
        }

        public void EnsureConfigLoaded()
        {
            if (File.Exists(Config.FilePath)) return;
            LoadDefaultConfig();
            // LoadDefaultConfig should have written the file.
        }

        protected virtual void LoadDefaultConfig() { }

        protected void NextTick(Action action)
        {
            if (action == null) return;
            if (ServerMgr.Instance == null) { action(); return; }
            ServerMgr.Instance.StartCoroutine(NextTickCoroutine(action));
        }

        private static System.Collections.IEnumerator NextTickCoroutine(Action action)
        {
            yield return null;
            action?.Invoke();
        }

        protected void PrintWarning(string message) => UnityEngine.Debug.LogWarning(message);
        protected void PrintWarning(string message, params object[] args) => UnityEngine.Debug.LogWarning(string.Format(message, args));
        protected void Puts(string message) => UnityEngine.Debug.Log(message);
        protected void Puts(string format, params object[] args) => UnityEngine.Debug.Log(string.Format(format, args));
    }
}

namespace Oxide.Game.Rust.Libraries.Covalence
{
    public sealed class RustConsolePlayer : Oxide.Core.Libraries.Covalence.IPlayer
    {
        public object Object => null;
        public string Id => "0";
        public string UserIDString => "0";
        public bool IsAdmin => true;
        public bool HasPermission(string permName) => true;
        public void Reply(string message) => Debug.Log($"[CopyPasteHarmony] {message}");
    }

    public sealed class RustIPlayer : Oxide.Core.Libraries.Covalence.IPlayer
    {
        private readonly BasePlayer _bp;
        public RustIPlayer(BasePlayer bp) => _bp = bp;

        public object Object => _bp;
        public string Id => _bp?.userID.ToString() ?? "0";
        public string UserIDString => _bp?.userID.ToString() ?? "0";
        public bool IsAdmin => _bp != null && _bp.IsAdmin;
        public bool HasPermission(string permName) => OxideCoreCompat.PermissionStore.IsAllowed(Id);

        public void Reply(string message)
        {
            if (_bp?.net?.connection == null) return;
            ConsoleNetwork.SendClientCommand(_bp.net.connection, "chat.add", 0, 0, message);
        }
    }

    public static class BasePlayerCovalenceExtensions
    {
        public static Oxide.Core.Libraries.Covalence.IPlayer IPlayer(this BasePlayer player)
        {
            if (player == null) return new RustConsolePlayer();
            return new RustIPlayer(player);
        }
    }
}

namespace OxideCoreCompat
{
    public static class OxideCompatBootstrap
    {
        public static string ServerRoot { get; private set; }
        public static string PluginName { get; private set; }

        public static void Initialize(string pluginName)
        {
            PluginName = pluginName ?? "CopyPaste";
            ServerRoot = Path.GetFullPath(Path.Combine(Application.dataPath ?? ".", ".."));

            // Permission + data roots for this Harmony mod's embedded Oxide plugin.
            PermissionStore.Initialize(ServerRoot, PluginName);

            var dataRoot = Path.Combine(ServerRoot, "HarmonyData", PluginName);
            Oxide.Core.Interface.Oxide.DataFileSystem ??= new Oxide.Core.DataFileSystem(dataRoot);
        }
    }

    public static class PermissionStore
    {
        private static readonly HashSet<string> AllowedSteamIds = new(StringComparer.OrdinalIgnoreCase);
        private static bool _allowAllNonAdmins;
        private static bool _initialized;

        public static void Initialize(string serverRoot, string pluginName)
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                var cfgPath = Path.Combine(serverRoot, "HarmonyConfig", $"{pluginName}Permissions.json");
                if (!File.Exists(cfgPath))
                {
                    // Default: admins only (non-admins denied).
                    SaveDefault(cfgPath);
                }

                var json = File.ReadAllText(cfgPath);
                var parsed = JsonConvert.DeserializeObject<PermissionConfig>(json);
                AllowedSteamIds.Clear();
                if (parsed?.AllowedSteamIds != null)
                {
                    foreach (var id in parsed.AllowedSteamIds)
                        if (!string.IsNullOrWhiteSpace(id))
                            AllowedSteamIds.Add(id.Trim());
                }

                _allowAllNonAdmins = parsed?.AllowAllNonAdmins ?? false;
            }
            catch
            {
                AllowedSteamIds.Clear();
                _allowAllNonAdmins = false;
            }
        }

        private static void SaveDefault(string cfgPath)
        {
            var dir = Path.GetDirectoryName(cfgPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var cfg = new PermissionConfig
            {
                AllowAllNonAdmins = false,
                AllowedSteamIds = new List<string>()
            };

            File.WriteAllText(cfgPath, JsonConvert.SerializeObject(cfg, Formatting.Indented));
        }

        public static bool IsAllowed(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return false;
            if (_allowAllNonAdmins) return true;
            return AllowedSteamIds.Contains(userId.Trim());
        }

        private sealed class PermissionConfig
        {
            public bool AllowAllNonAdmins { get; set; }
            public List<string> AllowedSteamIds { get; set; }
        }
    }
}

// Required for CopyPaste.cs deconstruction syntax on KeyValuePair in net48.
namespace System.Collections.Generic
{
    public static class KeyValuePairDeconstructExtensions
    {
        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
        {
            key = pair.Key;
            value = pair.Value;
        }
    }
}

// Provide missing game helper methods that the CopyPaste source calls.
namespace Oxide.Plugins
{
    public static class CopyPasteGameExtensions
    {
        public static void SetChandelierLength(this Chandelier chandelier, float length)
        {
            if (chandelier == null) return;

            // Try expected method name first.
            var mi = chandelier.GetType().GetMethod("SetChandelierLength", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(float) }, null);
            if (mi != null)
            {
                mi.Invoke(chandelier, new object[] { length });
                return;
            }

            // Fallback: try a field/property that looks like "chandelierLength".
            var t = chandelier.GetType();
            var prop = t.GetProperty("chandelierLength", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                       ?? t.GetProperty("ChandelierLength", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(chandelier, length, null);
                return;
            }

            var field = t.GetField("chandelierLength", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? t.GetField("ChandelierLength", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            field?.SetValue(chandelier, length);
        }
    }

    public static class CopyPasteReflectionHelpers
    {
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static bool TryGetMemberValue(object obj, string memberName, out object value)
        {
            value = null;
            if (obj == null || string.IsNullOrWhiteSpace(memberName)) return false;

            var t = obj.GetType();
            var prop = t.GetProperty(memberName, Flags);
            if (prop != null)
            {
                value = prop.GetValue(obj);
                return true;
            }

            var field = t.GetField(memberName, Flags);
            if (field != null)
            {
                value = field.GetValue(obj);
                return true;
            }

            return false;
        }

        public static void SetMemberValue(object obj, string memberName, object value)
        {
            if (obj == null || string.IsNullOrWhiteSpace(memberName)) return;

            var t = obj.GetType();

            var prop = t.GetProperty(memberName, Flags);
            if (prop != null)
            {
                var setMethod = prop.GetSetMethod(true);
                if (setMethod != null)
                {
                    setMethod.Invoke(obj, new[] { value });
                    return;
                }
            }

            var field = t.GetField(memberName, Flags);
            field?.SetValue(obj, value);
        }

        public static bool TryGetIntMember(object obj, string memberName, out int value)
        {
            value = default;
            if (!TryGetMemberValue(obj, memberName, out var raw) || raw == null) return false;
            try
            {
                value = Convert.ToInt32(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static int[] GetIntArrayMember(object obj, string memberName)
        {
            if (obj == null) return null;
            if (!TryGetMemberValue(obj, memberName, out var raw) || raw == null) return null;
            if (raw is Array arr)
            {
                var result = new int[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                    result[i] = Convert.ToInt32(arr.GetValue(i));
                return result;
            }
            return null;
        }

        public static void SetIntArrayMember(object obj, string memberName, IList<object> values)
        {
            if (obj == null || values == null) return;
            if (!TryGetMemberValue(obj, memberName, out var raw) || raw == null) return;
            if (!(raw is Array arr)) return;

            int len = Math.Min(arr.Length, values.Count);
            for (int i = 0; i < len; i++)
                arr.SetValue(Convert.ToInt32(values[i]), i);
        }

        public static bool TryInvoke(object obj, string methodName, params object[] args)
        {
            if (obj == null || string.IsNullOrWhiteSpace(methodName)) return false;
            var mi = obj.GetType().GetMethod(methodName, Flags, null, args?.Select(a => a?.GetType()).ToArray() ?? Type.EmptyTypes, null);
            if (mi == null)
            {
                // Fallback: name-only (no args match)
                mi = obj.GetType().GetMethods(Flags).FirstOrDefault(m => m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase) && m.GetParameters().Length == (args?.Length ?? 0));
            }
            if (mi == null) return false;
            mi.Invoke(obj, args ?? Array.Empty<object>());
            return true;
        }

        public static bool TrySetOrientableLightPitchYaw(OrientableLight light, float? pitchAmount, float? yawAmount)
        {
            if (light == null) return false;
            bool any = false;
            if (pitchAmount.HasValue)
            {
                TrySetFloatMember(light, "pitchAmount", pitchAmount.Value);
                any = true;
            }
            if (yawAmount.HasValue)
            {
                TrySetFloatMember(light, "yawAmount", yawAmount.Value);
                any = true;
            }
            return any;
        }

        private static bool TrySetFloatMember(object obj, string memberName, float value)
        {
            try
            {
                var t = obj.GetType();
                var prop = t.GetProperty(memberName, Flags);
                if (prop != null)
                {
                    var set = prop.GetSetMethod(true);
                    if (set != null)
                    {
                        set.Invoke(obj, new object[] { value });
                        return true;
                    }
                }
                var field = t.GetField(memberName, Flags);
                if (field != null)
                {
                    field.SetValue(obj, value);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static bool TryGetRidableHorseCurrentBreedIndex(RidableHorse horse, out int breedIndex)
        {
            breedIndex = default;
            if (horse == null) return false;
            return TryGetIntMember(horse, "currentBreedIndex", out breedIndex);
        }

        public static bool TryGetRidableHorseTowingEntityId(RidableHorse horse, out ulong towingEntityId)
        {
            towingEntityId = 0;
            if (horse == null) return false;

            if (!TryGetMemberValue(horse, "towingEntityId", out var towingRef) || towingRef == null)
                return false;

            // Check IsValid
            bool isValid = false;
            var t = towingRef.GetType();
            var isValidMethod = t.GetMethod("IsValid", new[] { typeof(bool) }) ?? t.GetMethod("IsValid", Type.EmptyTypes);
            if (isValidMethod != null)
            {
                isValid = (bool)(isValidMethod.GetParameters().Length == 1 ? isValidMethod.Invoke(towingRef, new object[] { true }) : isValidMethod.Invoke(towingRef, null));
            }
            else
            {
                var isValidProp = t.GetProperty("IsValid", Flags) ?? t.GetProperty("IsValid", BindingFlags.Instance | BindingFlags.Public);
                if (isValidProp != null)
                    isValid = Convert.ToBoolean(isValidProp.GetValue(towingRef));
            }

            if (!isValid) return false;

            var valueProp = t.GetProperty("Value", Flags) ?? t.GetProperty("value", Flags);
            if (valueProp != null)
            {
                towingEntityId = Convert.ToUInt64(valueProp.GetValue(towingRef));
                return true;
            }

            return false;
        }

        public static void TryAttachRidableHorseTowing(RidableHorse horse, BaseEntity newEntity, ITowing iTowing)
        {
            if (horse == null || newEntity == null || iTowing == null) return;

            // towingEntityId
            try
            {
                var prop = horse.GetType().GetProperty("towingEntityId", Flags);
                if (prop != null)
                {
                    var set = prop.GetSetMethod(true);
                    if (set != null)
                        prop.SetValue(horse, newEntity.net.ID, null);
                }
            }
            catch { }

            // towableEntity
            try
            {
                var prop = horse.GetType().GetProperty("towableEntity", Flags);
                if (prop != null)
                {
                    var set = prop.GetSetMethod(true);
                    if (set != null)
                        prop.SetValue(horse, iTowing, null);
                }
            }
            catch { }

            // TowAttach()
            try
            {
                TryInvoke(horse, "TowAttach");
            }
            catch { }
        }

        public static ulong? TryGetAudioVisualConnectedToUid(AudioVisualisationEntity audioVisual)
        {
            if (audioVisual == null) return null;

            var t = audioVisual.GetType();
            var member = t.GetMember("connectedTo", Flags).FirstOrDefault()
                         ?? t.GetMembers(Flags).FirstOrDefault(m => m.Name.IndexOf("connectedto", StringComparison.OrdinalIgnoreCase) >= 0);
            if (member == null) return null;

            object connectedToObj = member switch
            {
                PropertyInfo pi => pi.GetValue(audioVisual),
                FieldInfo fi => fi.GetValue(audioVisual),
                _ => null
            };

            if (connectedToObj == null) return null;

            // IsValid(true) if available
            var ctType = connectedToObj.GetType();
            bool isValid = false;
            var isValidMethod = ctType.GetMethod("IsValid", new[] { typeof(bool) }) ?? ctType.GetMethod("IsValid", Type.EmptyTypes);
            if (isValidMethod != null)
            {
                isValid = (bool)(isValidMethod.GetParameters().Length == 1 ? isValidMethod.Invoke(connectedToObj, new object[] { true }) : isValidMethod.Invoke(connectedToObj, null));
            }
            else
            {
                var isValidProp = ctType.GetProperty("IsValid", Flags);
                if (isValidProp != null) isValid = Convert.ToBoolean(isValidProp.GetValue(connectedToObj));
            }

            if (!isValid) return null;

            // uid.Value
            var uidProp = ctType.GetProperty("uid", Flags);
            var uidValueObj = uidProp?.GetValue(connectedToObj);
            if (uidValueObj == null) return null;

            var valueProp = uidValueObj.GetType().GetProperty("Value", Flags);
            if (valueProp != null) return Convert.ToUInt64(valueProp.GetValue(uidValueObj));

            // Some implementations might expose uid directly as ulong
            try { return Convert.ToUInt64(uidValueObj); } catch { return null; }
        }

        public static void TrySetAudioVisualConnectedToUid(AudioVisualisationEntity audioVisual, ulong uid)
        {
            if (audioVisual == null) return;

            var t = audioVisual.GetType();
            var member = t.GetMember("connectedTo", Flags).FirstOrDefault()
                         ?? t.GetMembers(Flags).FirstOrDefault(m => m.Name.IndexOf("connectedto", StringComparison.OrdinalIgnoreCase) >= 0);
            if (member == null) return;

            object connectedToObj = member switch
            {
                PropertyInfo pi => pi.GetValue(audioVisual),
                FieldInfo fi => fi.GetValue(audioVisual),
                _ => null
            };

            if (connectedToObj == null) return;

            // Set connectedTo.uid
            var ctType = connectedToObj.GetType();
            var uidProp = ctType.GetProperty("uid", Flags);
            var uidField = ctType.GetField("uid", Flags);

            var newId = new NetworkableId(uid);
            if (uidProp != null && uidProp.CanWrite)
                uidProp.SetValue(connectedToObj, newId, null);
            else if (uidField != null)
                uidField.SetValue(connectedToObj, newId);

            // If connectedTo is a value type/struct, write it back.
            switch (member)
            {
                case PropertyInfo pi when pi.CanWrite:
                    pi.SetValue(audioVisual, connectedToObj, null);
                    break;
                case FieldInfo fi:
                    fi.SetValue(audioVisual, connectedToObj);
                    break;
            }
        }

        public static void TrySetAudioVisualEnumProperty<TEnum>(AudioVisualisationEntity audioVisual, string memberName, int rawValue)
            where TEnum : struct
        {
            if (audioVisual == null) return;
            var prop = audioVisual.GetType().GetProperty(memberName, Flags);
            if (prop == null) return;

            var enumObj = Enum.ToObject(typeof(TEnum), rawValue);
            var setMethod = prop.GetSetMethod(true);
            if (setMethod != null)
                setMethod.Invoke(audioVisual, new object[] { enumObj });
        }
    }
}

// Helper to allow BasePlayerCovalenceExtensions to create an IPlayer wrapper without exposing PlayerManager internals.
namespace Oxide.Core.Libraries.Covalence
{
    public static class PlayerManagerAccessor
    {
        public sealed class RustIPlayer : IPlayer
        {
            private readonly BasePlayer _bp;
            public RustIPlayer(BasePlayer bp) => _bp = bp;
            public object Object => _bp;
            public string Id => _bp.userID.ToString();
            public string UserIDString => _bp.userID.ToString();
            public bool IsAdmin => _bp.IsAdmin;
            public bool HasPermission(string permName) => OxideCoreCompat.PermissionStore.IsAllowed(Id);
            public void Reply(string message)
            {
                if (_bp?.net?.connection == null) return;
                ConsoleNetwork.SendClientCommand(_bp.net.connection, "chat.add", 0, 0, message);
            }
        }
    }
}

