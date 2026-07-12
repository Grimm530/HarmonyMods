/*
 * Harmony-only bridge for RaidableBases.
 * Replaces Oxide APIs so the mod runs as a standalone Harmony mod with CopyPaste Harmony mod.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using UnityEngine;
using Rust;

namespace RaidableBases
{
    /// <summary>Minimal player/console interface for Harmony (replaces Oxide IPlayer).</summary>
    public interface IPlayer
    {
        string Id { get; }
        object Object { get; }
        void Reply(string message);
        bool IsServer { get; }
        string Name { get; }
        void Message(string msg);
        bool IsAdmin { get; }
        bool IsConnected { get; }
        bool IsBanned { get; }
        void Teleport(float x, float y, float z);
    }

    /// <summary>Console "player" for Paste and commands when no BasePlayer.</summary>
    public class RustConsolePlayer : IPlayer
    {
        public string Id => "0";
        public object Object => null;
        public bool IsServer => true;
        public string Name => "Server";
        public bool IsAdmin => true;
        public bool IsConnected => true;
        public bool IsBanned => false;
        public void Reply(string message) { Debug.Log("[RaidableBases] " + message); }
        public void Message(string msg) { Reply(msg); }
        public void Teleport(float x, float y, float z) { }
    }

    /// <summary>Version number for CopyPaste API compatibility (4.2.7+ per RaidableBases 3.1.5).</summary>
    public struct VersionNumber : IComparable<VersionNumber>
    {
        public int Major;
        public int Minor;
        public int Patch;
        public VersionNumber(int major, int minor, int patch) { Major = major; Minor = minor; Patch = patch; }
        public int CompareTo(VersionNumber other)
        {
            if (Major != other.Major) return Major.CompareTo(other.Major);
            if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
            return Patch.CompareTo(other.Patch);
        }
        public static bool operator >=(VersionNumber a, VersionNumber b) => a.CompareTo(b) >= 0;
        public static bool operator <=(VersionNumber a, VersionNumber b) => a.CompareTo(b) <= 0;
        public override string ToString() => $"{Major}.{Minor}.{Patch}";
    }

    /// <summary>Replaces Oxide DynamicConfigFile for paste/data files. Indexable like data["entities"].</summary>
    public class HarmonyDataFile
    {
        private readonly Newtonsoft.Json.Linq.JObject _data;
        private readonly string _relativePath;
        public HarmonyDataFile() { _data = new Newtonsoft.Json.Linq.JObject(); _relativePath = null; }
        public HarmonyDataFile(Newtonsoft.Json.Linq.JObject data) { _data = data ?? new Newtonsoft.Json.Linq.JObject(); _relativePath = null; }
        public HarmonyDataFile(string relativePath, Newtonsoft.Json.Linq.JObject data) { _relativePath = relativePath; _data = data ?? new Newtonsoft.Json.Linq.JObject(); }
        public void Save() { if (!string.IsNullOrEmpty(_relativePath)) HarmonyDataLayer.WriteObject(_relativePath, _data); }

        /// <summary>
        /// Oxide DynamicConfigFile returns nested Dictionary/List graphs.
        /// JArray.ToObject&lt;List&lt;object&gt;&gt;() leaves elements as JObject, which breaks
        /// RaidableBases checks like <c>obj is Dictionary&lt;string, object&gt;</c>.
        /// </summary>
        internal static object ConvertToken(Newtonsoft.Json.Linq.JToken tok)
        {
            if (tok == null || tok.Type == Newtonsoft.Json.Linq.JTokenType.Null)
                return null;
            if (tok is Newtonsoft.Json.Linq.JValue jv)
                return jv.Value;
            if (tok is Newtonsoft.Json.Linq.JArray ja)
            {
                var list = new List<object>(ja.Count);
                foreach (var item in ja)
                    list.Add(ConvertToken(item));
                return list;
            }
            if (tok is Newtonsoft.Json.Linq.JObject jo)
            {
                var dict = new Dictionary<string, object>(jo.Count);
                foreach (var prop in jo.Properties())
                    dict[prop.Name] = ConvertToken(prop.Value);
                return dict;
            }
            return tok.ToObject<object>();
        }

        public object this[string key]
        {
            get => ConvertToken(_data?[key]);
            set => _data[key] = value != null ? Newtonsoft.Json.Linq.JToken.FromObject(value) : null;
        }
    }

    /// <summary>File I/O under HarmonyData/RaidableBases/ and HarmonyData/copypaste/. Replaces Interface.Oxide.DataFileSystem.</summary>
    public static class HarmonyDataLayer
    {
        private static string _serverRoot;
        private static string _modDir;
        private static string _copypasteDir;
        private static string _profilesDir;
        /// <summary>Full path to Profiles directory (same path used when listing profile files).</summary>
        public static string GetProfilesDir()
        {
            if (string.IsNullOrEmpty(_modDir)) Init();
            return _profilesDir ?? Path.Combine(_modDir, "Profiles");
        }
        /// <summary>Config path: HarmonyConfig/RaidableBases.json.</summary>
        public static string GetPreferredConfigPath()
        {
            var root = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            return Path.Combine(root, "HarmonyConfig", "RaidableBases.json");
        }
        public static void Init()
        {
            if (!string.IsNullOrEmpty(_modDir)) return;
            _serverRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            _modDir = Path.Combine(_serverRoot, "HarmonyData", "RaidableBases");
            _profilesDir = Path.Combine(_modDir, "Profiles");
            _copypasteDir = Path.Combine(_serverRoot, "HarmonyData", "copypaste");

            foreach (var dir in new[] { _modDir, _copypasteDir, _profilesDir, Path.Combine(_modDir, "SpawnsDatabase"), Path.Combine(_modDir, "Editable_Lists") })
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            Debug.Log("[RaidableBases] Data: HarmonyData/RaidableBases. Profiles: " + _profilesDir + ". Paste: HarmonyData/copypaste/");
        }
        /// <summary>Resolved path to Profiles directory (for logging when no profiles found).</summary>
        public static string GetProfilesPathForLog()
        {
            try
            {
                var dir = ResolvePath("RaidableBases/Profiles");
                return string.IsNullOrEmpty(dir) ? "(null)" : Path.GetFullPath(dir);
            }
            catch { return "(error resolving path)"; }
        }

        private static string ResolvePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;
            if (string.IsNullOrEmpty(_modDir)) Init();
            relativePath = relativePath.Replace('\\', '/').TrimStart('/');
            if (relativePath.StartsWith("copypaste/", StringComparison.OrdinalIgnoreCase))
            {
                var sub = relativePath.Substring(10).TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                return Path.Combine(_copypasteDir, sub);
            }
            if (relativePath.StartsWith("RaidableBases/", StringComparison.OrdinalIgnoreCase))
                relativePath = relativePath.Substring(14).TrimStart('/');
            var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(_modDir, normalized);
        }
        public static bool ExistsDatafile(string relativePath)
        {
            return ResolveExistingFile(ResolvePath(relativePath)) != null;
        }
        public static string[] GetFiles(string relativePath, string pattern = "*")
        {
            if (string.IsNullOrEmpty(_modDir)) Init();
            var normalized = (relativePath ?? "").Replace('\\', '/').TrimStart('/');
            // Profiles live directly under HarmonyData/RaidableBases/Profiles (not .../RaidableBases/RaidableBases/Profiles).
            string dir;
            if (normalized.Equals("Profiles", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("RaidableBases/Profiles", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("/Profiles", StringComparison.OrdinalIgnoreCase))
                dir = _profilesDir ?? Path.Combine(_modDir, "Profiles");
            else
                dir = ResolvePath(relativePath);
            if (dir == null || !Directory.Exists(dir)) return Array.Empty<string>();
            var list = new List<string>();
            foreach (var f in Directory.GetFiles(dir, "*.json"))
                list.Add(Path.GetFileName(f));
            return list.ToArray();
        }

        /// <summary>Returns full paths to profile JSON files in the Profiles directory (same dir used when listing). Use these paths with ReadObjectFromFullPath to avoid path resolution issues.</summary>
        public static string[] GetProfileFileFullPaths()
        {
            if (string.IsNullOrEmpty(_modDir)) Init();
            var dir = _profilesDir ?? Path.Combine(_modDir, "Profiles");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return Array.Empty<string>();
            var list = new List<string>();
            foreach (var f in Directory.GetFiles(dir, "*.json"))
            {
                if (Path.GetFileName(f).Contains("_empty")) continue;
                list.Add(f);
            }
            return list.ToArray();
        }
        /// <summary>Oxide-style paths omit .json; prefer existing path, else path.json.</summary>
        private static string ResolveExistingFile(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            if (File.Exists(p)) return p;
            if (!p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var withJson = p + ".json";
                if (File.Exists(withJson)) return withJson;
            }
            return null;
        }

        public static HarmonyDataFile GetDatafile(string relativePath)
        {
            var p = ResolveExistingFile(ResolvePath(relativePath));
            if (p == null) return new HarmonyDataFile(relativePath, new Newtonsoft.Json.Linq.JObject());
            try
            {
                var json = File.ReadAllText(p);
                var jobj = Newtonsoft.Json.Linq.JObject.Parse(json);
                return new HarmonyDataFile(relativePath, jobj);
            }
            catch { return new HarmonyDataFile(relativePath, new Newtonsoft.Json.Linq.JObject()); }
        }
        /// <summary>Read and deserialize from a full file path (use for profile files to avoid path resolution issues).</summary>
        public static T ReadObjectFromFullPath<T>(string fullPath) where T : class, new()
        {
            if (string.IsNullOrEmpty(fullPath)) return null;
            var p = fullPath;
            if (!p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) p = p + ".json";
            if (!File.Exists(p)) return null;
            try { return JsonConvert.DeserializeObject<T>(File.ReadAllText(p)); }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBases] ReadObjectFromFullPath failed: " + p + " -> " + ex.Message);
                return null;
            }
        }

        public static T ReadObject<T>(string relativePath) where T : class, new()
        {
            var p = ResolveExistingFile(ResolvePath(relativePath));
            if (p == null) return null;
            try { return JsonConvert.DeserializeObject<T>(File.ReadAllText(p)); }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBases] ReadObject failed: " + p + " -> " + ex.Message);
                return null;
            }
        }
        public static void WriteObject(string relativePath, object obj)
        {
            var p = ResolvePath(relativePath);
            if (p == null) return;
            if (!p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) p = p + ".json";
            try
            {
                var dir = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(p, JsonConvert.SerializeObject(obj, Formatting.Indented));
            }
            catch (Exception ex) { Debug.LogWarning("[RaidableBases] WriteObject failed: " + ex.Message); }
        }
    }

    /// <summary>Reflection wrapper for CopyPaste Harmony mod (CopyPasteHarmony.CopyPasteHarmonyMod).</summary>
    public static class CopyPasteAPI
    {
        /// <summary>Set true to log CopyPaste resolution steps (why handshake passed or failed).</summary>
        private const bool DebugCopyPasteResolve = true;

        private static Type _modType;
        private static MethodInfo _preLoadData;
        private static MethodInfo _paste;
        private static MethodInfo _findBestHeight;
        private static PropertyInfo _versionProp;
        private static FieldInfo _versionField;
        private static MethodInfo _isPasteReady;
        private static bool _initialized;

        private static void DebugLog(string message)
        {
            if (DebugCopyPasteResolve)
                Debug.Log("[RaidableBases] CopyPaste resolve: " + message);
        }

        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            if (DebugCopyPasteResolve)
                Debug.Log("[RaidableBases] CopyPaste resolve: starting (AppDomain -> Registry -> AssemblyScan -> HarmonyLoader loadedMods -> TryLoadMod).");
            _modType = TryGetCopyPasteFromAppDomain();
            if (_modType != null) { DebugLog("AppDomain -> found " + _modType.FullName); goto bound; }
            DebugLog("AppDomain -> key missing or null");
            _modType = TryGetCopyPasteFromRegistry();
            if (_modType != null) { DebugLog("Registry -> found " + _modType.FullName); goto bound; }
            DebugLog("Registry -> not present or no GetModApiType");
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    _modType = asm.GetType("CopyPasteHarmony.CopyPasteHarmonyMod");
                    if (_modType != null) { DebugLog("AssemblyScan -> found in " + asm.GetName().Name); goto bound; }
                }
                catch { }
            }
            DebugLog("AssemblyScan -> type not in any assembly");
            TryFindCopyPasteViaHarmonyLoader();
            if (_modType != null) { DebugLog("HarmonyLoader loadedMods -> found " + _modType.FullName); goto bound; }
            TryLoadCopyPasteViaHarmonyLoader();
            if (_modType != null) { DebugLog("TryLoadMod then loadedMods -> found " + _modType.FullName); goto bound; }
            DebugLog("TryLoadMod -> CopyPaste still not loaded or type not found");
            return;
        bound:
            _preLoadData = _modType.GetMethod("PreLoadData", BindingFlags.Public | BindingFlags.Static);
            _paste = _modType.GetMethod("Paste", BindingFlags.Public | BindingFlags.Static);
            _findBestHeight = _modType.GetMethod("FindBestHeight", BindingFlags.Public | BindingFlags.Static);
            _versionProp = _modType.GetProperty("Version", BindingFlags.Public | BindingFlags.Static);
            if (_versionProp == null)
                _versionField = _modType.GetField("Version", BindingFlags.Public | BindingFlags.Static);
            _isPasteReady = _modType.GetMethod("IsPasteReady", BindingFlags.Public | BindingFlags.Static);
            DebugLog("bound methods: PreLoadData=" + (_preLoadData != null) + " Paste=" + (_paste != null) + " FindBestHeight=" + (_findBestHeight != null) + " Version=" + (_versionProp != null || _versionField != null) + " IsPasteReady=" + (_isPasteReady != null));
        }

        /// <summary>Handshake: get CopyPaste API type from AppDomain (CopyPaste sets it in OnLoaded). Works regardless of which HarmonyLoader the game uses.</summary>
        private static Type TryGetCopyPasteFromAppDomain()
        {
            try
            {
                var key = "CopyPaste_ApiType";
                var data = AppDomain.CurrentDomain.GetData(key);
                return data as Type;
            }
            catch (Exception ex) { if (DebugCopyPasteResolve) Debug.Log("[RaidableBases] CopyPaste resolve: AppDomain GetData threw " + ex.Message); return null; }
        }

        /// <summary>Get CopyPaste API type from HarmonyLoader's registry if the loader has RegisterModApi/GetModApiType (e.g. custom Rust.Harmony build).</summary>
        private static Type TryGetCopyPasteFromRegistry()
        {
            try
            {
                Type loaderType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { loaderType = asm.GetType("HarmonyLoader"); if (loaderType != null) break; }
                    catch { }
                }
                if (loaderType == null) return null;
                var method = loaderType.GetMethod("GetModApiType", BindingFlags.Public | BindingFlags.Static);
                if (method == null) return null;
                var result = method.Invoke(null, new object[] { "CopyPaste" });
                return result as Type;
            }
            catch { return null; }
        }

        /// <summary>Find CopyPaste via HarmonyLoader's loaded mod list (same assembly the loader used). Required because the loader renames assemblies (CopyPaste_guid) so type search by name can miss it.</summary>
        private static void TryFindCopyPasteViaHarmonyLoader()
        {
            try
            {
                Type loaderType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        loaderType = asm.GetType("HarmonyLoader");
                        if (loaderType != null) break;
                    }
                    catch { }
                }
                if (loaderType == null) { DebugLog("HarmonyLoader loadedMods -> HarmonyLoader type not found"); return; }
                var field = loaderType.GetField("loadedMods", BindingFlags.NonPublic | BindingFlags.Static);
                if (field == null) { DebugLog("HarmonyLoader loadedMods -> field 'loadedMods' not found"); return; }
                var list = field.GetValue(null) as System.Collections.IEnumerable;
                if (list == null) { DebugLog("HarmonyLoader loadedMods -> list is null"); return; }
                var names = new List<string>();
                foreach (var mod in list)
                {
                    if (mod == null) continue;
                    var modType = mod.GetType();
                    var nameProp = modType.GetProperty("Name");
                    var name = nameProp?.GetValue(mod) as string;
                    names.Add(name ?? "(null)");
                    if (!"CopyPaste".Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                    var asmProp = modType.GetProperty("Assembly");
                    var asm = asmProp?.GetValue(mod) as Assembly;
                    if (asm == null) continue;
                    _modType = asm.GetType("CopyPasteHarmony.CopyPasteHarmonyMod");
                    if (_modType != null) break;
                }
                if (_modType == null)
                    DebugLog("HarmonyLoader loadedMods -> CopyPaste not in list. Loaded mods: [" + string.Join(", ", names) + "]");
            }
            catch (Exception ex) { DebugLog("HarmonyLoader loadedMods -> " + ex.Message); }
        }

        /// <summary>Ask HarmonyLoader to load CopyPaste.dll if not already loaded, then resolve via loadedMods.</summary>
        private static void TryLoadCopyPasteViaHarmonyLoader()
        {
            try
            {
                Type loaderType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { loaderType = asm.GetType("HarmonyLoader"); if (loaderType != null) break; }
                    catch { }
                }
                if (loaderType == null) return;
                var tryLoad = loaderType.GetMethod("TryLoadMod", BindingFlags.Public | BindingFlags.Static);
                if (tryLoad == null) { DebugLog("TryLoadMod -> method not found"); return; }
                var ok = tryLoad.Invoke(null, new object[] { "CopyPaste" });
                DebugLog("TryLoadMod(CopyPaste) -> " + (ok is bool b && b ? "true" : "false"));
                TryFindCopyPasteViaHarmonyLoader();
            }
            catch (Exception ex) { DebugLog("TryLoadMod -> " + ex.Message); }
        }

        public static bool IsAvailable => _modType != null && (_versionProp != null || _versionField != null);

        /// <summary>Returns null if CopyPaste is available and version OK; otherwise the error message to show (for reporting at plugin init).</summary>
        public static string GetAvailabilityError()
        {
            if (IsAvailable)
            {
                try
                {
                    if (Version.CompareTo(new VersionNumber(4, 2, 7)) < 0)
                        return "CopyPaste version too old. Need 4.2.7 or newer (found " + Version.Major + "." + Version.Minor + "." + Version.Patch + ").";
                    return null;
                }
                catch { return "CopyPaste check failed."; }
            }
            return "CopyPaste not found. Load HarmonyMods/CopyPaste.dll before RaidableBases.";
        }

        public static VersionNumber Version
        {
            get
            {
                object v = null;
                if (_versionProp != null)
                    v = _versionProp.GetValue(null);
                else if (_versionField != null)
                    v = _versionField.GetValue(null);
                if (v == null) return default;
                try
                {
                    var t = v.GetType();
                    return new VersionNumber(
                        ReadVersionPart(t, v, "Major"),
                        ReadVersionPart(t, v, "Minor"),
                        ReadVersionPart(t, v, "Patch"));
                }
                catch { return default; }
            }
        }

        private static int ReadVersionPart(Type t, object v, string name)
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) return Convert.ToInt32(f.GetValue(v));
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null) return Convert.ToInt32(p.GetValue(v));
            throw new MissingFieldException(t.FullName, name);
        }

        public static bool IsPasteReady()
        {
            if (_isPasteReady == null) return false;
            try { return (bool)_isPasteReady.Invoke(null, null); }
            catch { return false; }
        }

        /// <summary>MethodInfo.Invoke does not apply C# optional defaults — pad with ParameterInfo.DefaultValue.</summary>
        private static object[] PadInvokeArgs(MethodInfo method, object[] args)
        {
            if (method == null || args == null) return args;
            var parms = method.GetParameters();
            if (args.Length >= parms.Length) return args;
            var full = new object[parms.Length];
            Array.Copy(args, full, args.Length);
            for (int i = args.Length; i < parms.Length; i++)
            {
                if (parms[i].HasDefaultValue)
                    full[i] = parms[i].DefaultValue;
                else if (parms[i].ParameterType.IsByRef)
                    full[i] = null;
                else if (Nullable.GetUnderlyingType(parms[i].ParameterType) != null)
                    full[i] = null;
                else if (parms[i].ParameterType.IsValueType)
                    full[i] = Activator.CreateInstance(parms[i].ParameterType);
                else
                    full[i] = null;
            }
            return full;
        }

        public static object Call(string methodName, params object[] args)
        {
            Init();
            switch (methodName)
            {
                case "PreLoadData":
                    if (_preLoadData != null && args != null && args.Length >= 7)
                        return _preLoadData.Invoke(null, PadInvokeArgs(_preLoadData, new object[] { args[0], args[1], args[2], args[3], args[4], args[5], args[6] }));
                    break;
                case "FindBestHeight":
                    if (_findBestHeight != null && args != null && args.Length >= 2)
                        return _findBestHeight.Invoke(null, PadInvokeArgs(_findBestHeight, new object[] { args[0], args[1] }));
                    break;
                case "Paste":
                    if (_paste != null && args != null)
                    {
                        // Oxide Call("Paste", new object[]{...}) arrives as a single nested array via params.
                        var pasteArgs = args.Length == 1 && args[0] is object[] o ? o : args;
                        if (pasteArgs.Length >= 14)
                            return _paste.Invoke(null, PadInvokeArgs(_paste, pasteArgs));
                    }
                    break;
            }
            return null;
        }
    }

    /// <summary>
    /// Oxide-style permission helper backed by the Permissions Harmony mod
    /// (<c>PermissionsHarmony.PermissionsMod</c> / AppDomain key <c>Permissions_ApiType</c>).
    /// </summary>
    public class HarmonyPermissionHelper
    {
        private readonly HashSet<string> _registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Action _readyCallback;
        private static Type _permType;
        private static MethodInfo _userHas;
        private static MethodInfo _groupHas;
        private static MethodInfo _register;
        private static MethodInfo _exists;
        private static MethodInfo _grantUser;
        private static MethodInfo _grantGroup;
        private static MethodInfo _revokeUser;
        private static MethodInfo _revokeGroup;
        private static MethodInfo _userHasGroup;
        private static MethodInfo _addUserGroup;
        private static MethodInfo _removeUserGroup;
        private static MethodInfo _createGroup;
        private static MethodInfo _groupExists;
        private static MethodInfo _registerReady;
        private static int _boundGen = -1;
        private static bool _resolveAttempted;
        private static bool _linkedLogged;

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
                _userHas = _groupHas = _register = _exists = null;
                _grantUser = _grantGroup = _revokeUser = _revokeGroup = null;
                _userHasGroup = _addUserGroup = _removeUserGroup = _createGroup = _groupExists = _registerReady = null;

                _permType = ResolveLivePermType();
                live = ReadLiveInstance(_permType);
                if (_permType == null || live == null)
                {
                    if (!_resolveAttempted)
                    {
                        _resolveAttempted = true;
                        Debug.LogWarning("[RaidableBases] Permissions mod not loaded - grant/check via harmony.load Permissions. Server admins still pass.");
                    }
                    return;
                }

                _resolveAttempted = false;
                BindingFlags sf = BindingFlags.Public | BindingFlags.Static;
                _userHas = _permType.GetMethod("UserHasPermission", sf, null, new[] { typeof(string), typeof(string) }, null);
                _groupHas = _permType.GetMethod("GroupHasPermission", sf, null, new[] { typeof(string), typeof(string) }, null);
                _register = _permType.GetMethod("RegisterPermission", sf, null, new[] { typeof(string) }, null);
                _exists = _permType.GetMethod("PermissionExists", sf, null, new[] { typeof(string) }, null);
                _grantUser = _permType.GetMethod("GrantUserPermission", sf, null, new[] { typeof(string), typeof(string) }, null);
                _grantGroup = _permType.GetMethod("GrantGroupPermission", sf, null, new[] { typeof(string), typeof(string) }, null);
                _revokeUser = _permType.GetMethod("RevokeUserPermission", sf, null, new[] { typeof(string), typeof(string) }, null);
                _revokeGroup = _permType.GetMethod("RevokeGroupPermission", sf, null, new[] { typeof(string), typeof(string) }, null);
                _userHasGroup = _permType.GetMethod("UserHasGroup", sf, null, new[] { typeof(string), typeof(string) }, null);
                _addUserGroup = _permType.GetMethod("AddUserGroup", sf, null, new[] { typeof(string), typeof(string) }, null);
                _removeUserGroup = _permType.GetMethod("RemoveUserGroup", sf, null, new[] { typeof(string), typeof(string) }, null);
                _createGroup = _permType.GetMethod("CreateGroup", sf, null, new[] { typeof(string), typeof(string), typeof(int) }, null);
                _groupExists = _permType.GetMethod("GroupExists", sf, null, new[] { typeof(string) }, null);
                _registerReady = _permType.GetMethod("RegisterReadyCallback", sf, null, new[] { typeof(Action) }, null);
                _boundGen = gen;

                if (!_linkedLogged)
                {
                    _linkedLogged = true;
                    Debug.Log("[RaidableBases] Linked to Permissions Harmony mod.");
                }
                else
                    Debug.Log($"[RaidableBases] Re-linked to Permissions Harmony mod (gen={gen}).");
            }
            catch (Exception ex)
            {
                _permType = null;
                Debug.LogWarning("[RaidableBases] Permissions bind failed: " + ex.Message);
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
                try { InvokeVoid(_register, perm); } catch { }
            }
        }

        private static bool InvokeBool(MethodInfo mi, params object[] args)
        {
            if (mi == null) return false;
            try { return mi.Invoke(null, args) is bool b && b; }
            catch { return false; }
        }

        private static void InvokeVoid(MethodInfo mi, params object[] args)
        {
            if (mi == null) return;
            try { mi.Invoke(null, args); } catch { }
        }

        public void SetAdminIds(IEnumerable<string> ids) { /* admins resolved via BasePlayer.IsAdmin */ }

        public bool UserHasPermission(string userId, string perm)
        {
            if (string.IsNullOrEmpty(userId)) return false;
            if (string.IsNullOrEmpty(perm)) return true;

            // Do not auto-pass IsAdmin — that grants deny perms like raidablebases.banned.
            // Use Permissions groups (e.g. admin) and explicit grants only.
            EnsureBound();
            return InvokeBool(_userHas, userId, perm);
        }

        public bool UserHasGroup(string userId, string groupName)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(groupName)) return false;
            EnsureBound();
            return InvokeBool(_userHasGroup, userId, groupName);
        }

        public bool PermissionExists(string perm)
        {
            if (string.IsNullOrEmpty(perm)) return false;
            if (_registered.Contains(perm)) return true;
            EnsureBound();
            return InvokeBool(_exists, perm);
        }

        public void RegisterPermission(string perm, object plugin)
        {
            if (string.IsNullOrEmpty(perm)) return;
            _registered.Add(perm);
            EnsureBound();
            EnsureReadyCallback();
            InvokeVoid(_register, perm);
        }

        public string[] GetUserGroups(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return Array.Empty<string>();
            EnsureBound();
            try
            {
                // Prefer static API if present; else read PermissionService via Instance.
                var mi = _permType?.GetMethod("GetUserGroups", BindingFlags.Public | BindingFlags.Static);
                if (mi?.Invoke(null, new object[] { userId }) is string[] arr)
                    return arr;
                var instProp = _permType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                var inst = instProp?.GetValue(null);
                var svc = inst?.GetType().GetProperty("Service")?.GetValue(inst);
                var user = svc?.GetType().GetMethod("GetUserData")?.Invoke(svc, new object[] { userId });
                if (user?.GetType().GetProperty("Groups")?.GetValue(user) is System.Collections.IEnumerable groups)
                {
                    var list = new List<string>();
                    foreach (var g in groups)
                        if (g is string s) list.Add(s);
                    return list.ToArray();
                }
            }
            catch { }
            return Array.Empty<string>();
        }

        public bool GroupHasPermission(string groupName, string perm)
        {
            if (string.IsNullOrEmpty(groupName) || string.IsNullOrEmpty(perm)) return false;
            EnsureBound();
            return InvokeBool(_groupHas, groupName, perm);
        }

        public void RevokeGroupPermission(string groupName, string perm)
        {
            EnsureBound();
            InvokeVoid(_revokeGroup, groupName, perm);
        }

        public void RevokeUserPermission(string userId, string perm)
        {
            EnsureBound();
            InvokeVoid(_revokeUser, userId, perm);
        }

        public bool GroupExists(string groupName)
        {
            if (string.IsNullOrEmpty(groupName)) return false;
            EnsureBound();
            if (_groupExists != null)
                return InvokeBool(_groupExists, groupName);
            // Fallback: CreateGroup is idempotent on Permissions service when group exists.
            try
            {
                var instProp = _permType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                var inst = instProp?.GetValue(null);
                var svc = inst?.GetType().GetProperty("Service")?.GetValue(inst);
                if (svc?.GetType().GetMethod("GroupExists")?.Invoke(svc, new object[] { groupName }) is bool b)
                    return b;
            }
            catch { }
            return false;
        }

        public void CreateGroup(string groupName, string title = null) => CreateGroup(groupName, title ?? groupName, 0);

        public void CreateGroup(string groupName, string title, int rank)
        {
            if (string.IsNullOrEmpty(groupName)) return;
            EnsureBound();
            InvokeVoid(_createGroup, groupName, title ?? groupName, rank);
            if (_createGroup == null)
            {
                try
                {
                    var instProp = _permType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    var inst = instProp?.GetValue(null);
                    var svc = inst?.GetType().GetProperty("Service")?.GetValue(inst);
                    svc?.GetType().GetMethod("CreateGroup", new[] { typeof(string), typeof(string), typeof(int) })
                        ?.Invoke(svc, new object[] { groupName, title ?? groupName, rank });
                }
                catch { }
            }
        }

        public void GrantGroupPermission(string groupName, string perm) => GrantGroupPermission(groupName, perm, null);

        public void GrantGroupPermission(string groupName, string perm, object plugin)
        {
            EnsureBound();
            InvokeVoid(_grantGroup, groupName, perm);
        }

        public void GrantUserPermission(string userId, string perm) => GrantUserPermission(userId, perm, null);

        public void GrantUserPermission(string userId, string perm, object plugin)
        {
            EnsureBound();
            InvokeVoid(_grantUser, userId, perm);
        }

        public void AddUserGroup(string userId, string groupName)
        {
            EnsureBound();
            InvokeVoid(_addUserGroup, userId, groupName);
        }

        public void RemoveUserGroup(string userId, string groupName)
        {
            EnsureBound();
            InvokeVoid(_removeUserGroup, userId, groupName);
        }
    }

    /// <summary>Stub for plugin references. Find("Kits") / Find("CopyPaste") return Harmony stubs.</summary>
    public class PluginsStub
    {
        public object Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.Equals("Kits", StringComparison.OrdinalIgnoreCase))
                return RaidableBasesHost.Instance?.Kits ?? new KitsPluginStub();
            if (name.Equals("CopyPaste", StringComparison.OrdinalIgnoreCase))
                return RaidableBasesHost.Instance?.CopyPaste ?? new CopyPastePluginStub();
            return null;
        }
    }

    /// <summary>
    /// Oxide lang replacement: RegisterMessages + HarmonyLanguage/RaidableBases.json overrides.
    /// </summary>
    public class LangStub
    {
        private readonly Dictionary<string, Dictionary<string, string>> _byLang =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private bool _fileLoaded;

        public void RegisterMessages(object messages, object plugin, string language = "en")
        {
            if (messages == null) return;
            language = string.IsNullOrEmpty(language) ? "en" : language;
            if (!_byLang.TryGetValue(language, out var dict))
                _byLang[language] = dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (messages is Dictionary<string, string> map)
            {
                foreach (var kv in map)
                    dict[kv.Key] = kv.Value ?? "";
                return;
            }

            // Newtonsoft / other IDictionary shapes from decompressed lang blobs
            if (messages is System.Collections.IDictionary idict)
            {
                foreach (System.Collections.DictionaryEntry entry in idict)
                {
                    if (entry.Key == null) continue;
                    dict[entry.Key.ToString()] = entry.Value?.ToString() ?? "";
                }
            }
        }

        public void EnsureHarmonyLanguageLoaded()
        {
            if (_fileLoaded) return;
            _fileLoaded = true;
            LoadHarmonyLanguageFile();
        }

        /// <summary>Re-apply HarmonyLanguage/RaidableBases.json so it overrides embedded strings.</summary>
        public void ReloadHarmonyLanguageOverrides()
        {
            _fileLoaded = false;
            EnsureHarmonyLanguageLoaded();
        }

        private void LoadHarmonyLanguageFile()
        {
            try
            {
                var root = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
                var path = Path.Combine(root, "HarmonyLanguage", "RaidableBases.json");
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var map = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                if (map == null || map.Count == 0) return;
                RegisterMessages(map, null, "en");
                Debug.Log($"[RaidableBases] Loaded {map.Count} language strings from HarmonyLanguage/RaidableBases.json");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBases] HarmonyLanguage load failed: " + ex.Message);
            }
        }

        public string GetMessage(string key, object plugin, string id)
        {
            if (string.IsNullOrEmpty(key)) return "";
            EnsureHarmonyLanguageLoaded();
            if (_byLang.TryGetValue("en", out var en) && en.TryGetValue(key, out var msg) && !string.IsNullOrEmpty(msg))
                return msg;
            foreach (var d in _byLang.Values)
            {
                if (d.TryGetValue(key, out msg) && !string.IsNullOrEmpty(msg))
                    return msg;
            }
            return key;
        }

        public string GetLanguage(string userId) => "en";
    }

    /// <summary>Timer runner using ServerMgr coroutines. Replaces Oxide timer.Once / timer.Repeat. Tracks timers so DestroyAll() stops them on unload.</summary>
    public class HarmonyTimerRunner
    {
        private readonly List<Timer> _timers = new List<Timer>();
        private static readonly float LowFpsThreshold = 0.2f;

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
                    if (!t.Destroyed) try { callback?.Invoke(); } catch { }
                    t.Destroy();
                    lock (_timers) _timers.Remove(t);
                }));
            }
            catch { lock (_timers) _timers.Remove(t); }
            return t;
        }
        public Timer Repeat(float interval, int repeatCount, Action callback)
        {
            if (callback == null) return new Timer();
            var t = new Timer { Callback = callback };
            lock (_timers) _timers.Add(t);
            try
            {
                ServerMgr.Instance?.StartCoroutine(RepeatCoroutine(interval, repeatCount, t, () =>
                {
                    if (!t.Destroyed) try { callback?.Invoke(); } catch { }
                }));
            }
            catch { lock (_timers) _timers.Remove(t); }
            return t;
        }
        private IEnumerator RepeatCoroutine(float interval, int repeatCount, Timer timer, Action callback)
        {
            int count = 0;
            while (!timer.Destroyed && (repeatCount <= 0 || count < repeatCount))
            {
                yield return new WaitForSeconds(interval);
                if (timer.Destroyed) break;
                if (UnityEngine.Time.deltaTime > LowFpsThreshold)
                    yield return null;
                callback?.Invoke();
                count++;
            }
            lock (_timers) _timers.Remove(timer);
        }
        private IEnumerator WaitAndRun(float seconds, Timer timer, Action callback)
        {
            yield return new WaitForSeconds(seconds);
            try { callback?.Invoke(); } catch (Exception ex) { Debug.LogWarning("[RaidableBases] Timer: " + ex.Message); }
        }
        /// <summary>Alias for Once (Oxide timer.In).</summary>
        public Timer In(float seconds, Action callback) => Once(seconds, callback);
    }

    /// <summary>Fake plugin reference that only supports CopyPaste via CopyPasteAPI. Replaces plugins.Find("CopyPaste").</summary>
    public class CopyPastePluginStub
    {
        public object Version => CopyPasteAPI.Version;
        public object Call(string methodName, params object[] args) => CopyPasteAPI.Call(methodName, args);
    }

    /// <summary>Fake plugin reference for Kits Harmony mod. Supports Call("GiveKit") / Call("isKit") used by NPC kit loadouts.</summary>
    public class KitsPluginStub
    {
        public object Call(string methodName, params object[] args) => KitsAPI.Call(methodName, args);
    }

    /// <summary>Reflection wrapper for Kits Harmony mod (KitsHarmony.KitsHarmonyMod).</summary>
    public static class KitsAPI
    {
        private static Type _modType;
        private static MethodInfo _giveKit;
        private static MethodInfo _isKit;
        private static bool _resolveAttempted;

        public static void Init()
        {
            if (_modType != null) return;
            _modType = TryGetKitsFromAppDomain();
            if (_modType == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        _modType = asm.GetType("KitsHarmony.KitsHarmonyMod");
                        if (_modType != null) break;
                    }
                    catch { }
                }
            }
            if (_modType == null)
            {
                if (!_resolveAttempted)
                {
                    _resolveAttempted = true;
                    Debug.Log("[RaidableBases] Kits resolve: KitsHarmony.KitsHarmonyMod not found yet (load Kits.dll for profile Scientist/Murderer Kits).");
                }
                return;
            }
            _giveKit = _modType.GetMethod("GiveKit", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(BasePlayer), typeof(string) }, null);
            _isKit = _modType.GetMethod("IsKit", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string) }, null)
                ?? _modType.GetMethod("isKit", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string) }, null);
            Debug.Log("[RaidableBases] Kits resolve: bound " + _modType.FullName +
                      " GiveKit=" + (_giveKit != null) + " IsKit=" + (_isKit != null));
        }

        private static Type TryGetKitsFromAppDomain()
        {
            try
            {
                return AppDomain.CurrentDomain.GetData("Kits_ApiType") as Type;
            }
            catch { return null; }
        }

        public static bool IsAvailable
        {
            get
            {
                Init();
                return _modType != null && _giveKit != null && _isKit != null;
            }
        }

        public static object Call(string methodName, params object[] args)
        {
            Init();
            if (_modType == null) return methodName == "isKit" || methodName == "IsKit" ? (object)false : null;
            try
            {
                switch (methodName)
                {
                    case "GiveKit":
                        if (_giveKit != null && args != null && args.Length >= 2 && args[0] is BasePlayer bp)
                            return _giveKit.Invoke(null, new object[] { bp, args[1] as string ?? args[1]?.ToString() });
                        return "Kits GiveKit unavailable";
                    case "isKit":
                    case "IsKit":
                        if (_isKit != null && args != null && args.Length >= 1)
                            return _isKit.Invoke(null, new object[] { args[0] as string ?? args[0]?.ToString() });
                        return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBases] KitsAPI." + methodName + ": " + (ex.InnerException ?? ex).Message);
                if (methodName == "isKit" || methodName == "IsKit") return false;
                return (ex.InnerException ?? ex).Message;
            }
            return null;
        }
    }

    /// <summary>Host that provides all Oxide-like APIs for RaidableBases when running as Harmony mod.</summary>
    public class RaidableBasesHost
    {
        public static RaidableBasesHost Instance { get; private set; }
        public HarmonyPermissionHelper Permission { get; } = new HarmonyPermissionHelper();
        public HarmonyTimerRunner Timer { get; } = new HarmonyTimerRunner();
        public CopyPastePluginStub CopyPaste { get; } = new CopyPastePluginStub();
        public KitsPluginStub Kits { get; } = new KitsPluginStub();
        public RaidableBases ModInstance { get; set; }

        private readonly ConcurrentDictionary<string, bool> _subscribedHooks = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public void Subscribe(string hook)
        {
            if (!string.IsNullOrEmpty(hook))
                _subscribedHooks.TryAdd(hook, true);
        }

        public void Unsubscribe(string hook)
        {
            if (!string.IsNullOrEmpty(hook))
                _subscribedHooks.TryRemove(hook, out _);
        }

        public bool IsSubscribed(string hook)
        {
            return !string.IsNullOrEmpty(hook) && _subscribedHooks.ContainsKey(hook);
        }

        /// <summary>Invoke a hook on the mod instance if subscribed. Returns hook return value or null.</summary>
        public object InvokeHook(string name, object[] args)
        {
            var mod = ModInstance;
            if (mod == null || !IsSubscribed(name))
                return null;
            try
            {
                var type = mod.GetType();
                var allMethods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var methods = new List<MethodInfo>();
                for (int i = 0; i < allMethods.Length; i++)
                {
                    if (allMethods[i].Name == name)
                        methods.Add(allMethods[i]);
                }
                if (methods.Count == 0)
                    return null;
                var argCount = args?.Length ?? 0;
                foreach (var method in methods.ToArray())
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length != argCount)
                        continue;
                    bool match = true;
                    for (int i = 0; i < parameters.Length && match; i++)
                    {
                        var p = parameters[i];
                        var arg = i < args.Length ? args[i] : null;
                        var paramType = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                        if (paramType == null) { match = false; break; }
                        if (arg == null)
                            match = !paramType.IsValueType || Nullable.GetUnderlyingType(paramType) != null;
                        else
                            match = paramType.IsAssignableFrom(arg.GetType());
                    }
                    if (!match)
                        continue;
                    var result = method.Invoke(mod, args);
                    return result;
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBases] InvokeHook " + name + ": " + ex.Message);
                return null;
            }
        }

        public void Init()
        {
            Instance = this;
            CopyPasteAPI.Init();
            KitsAPI.Init();
            HarmonyDataLayer.Init();
        }

        public void Shutdown()
        {
            Timer.DestroyAll();
            ModInstance = null;
            Instance = null;
            _subscribedHooks.Clear();
        }

        public void Puts(string message) => Debug.Log("[RaidableBases] " + message);
        public void PrintError(string message) => Debug.LogError("[RaidableBases] " + message);
    }

    /// <summary>Base class that replaces RustPlugin so RaidableBases can run without Oxide.</summary>
    public abstract class RaidableBasesBase
    {
        public const string Name = "RaidableBases";
        /// <summary>Oxide Plugin.Manager stand-in (non-null while Harmony host is loaded).</summary>
        public object Manager => Host;
        protected RaidableBasesHost Host => RaidableBasesHost.Instance;
        protected void Puts(string msg) => Host?.Puts(msg);
        protected void PrintError(string msg) => Host?.PrintError(msg);
        protected HarmonyPermissionHelper permission => Host?.Permission;
        protected HarmonyTimerRunner timer => Host?.Timer;
        protected CopyPastePluginStub CopyPaste => Host?.CopyPaste;
        protected KitsPluginStub KitsPlugin => Host?.Kits;
        private PluginsStub _plugins;
        protected PluginsStub plugins => _plugins ??= new PluginsStub();
        /// <summary>Run action next frame (replaces Interface.Oxide.NextTick).</summary>
        internal void NextTick(Action action) { try { ServerMgr.Instance?.StartCoroutine(NextTickCoroutine(action)); } catch { } }
        private static IEnumerator NextTickCoroutine(Action action) { yield return null; try { action?.Invoke(); } catch (Exception ex) { Debug.LogWarning("[RaidableBases] NextTick: " + ex.Message); } }
        private LangStub _lang;
        protected LangStub lang => _lang ??= new LangStub();
        /// <summary>Register chat + console command (Oxide AddCovalenceCommand replacement).</summary>
        protected void AddCovalenceCommand(string cmd, string methodName, string permission = null)
        {
            CommandRegistry.RegisterCovalence(cmd, methodName, permission, this);
        }
        /// <summary>Oxide hook subscribe (Harmony: registers with host for InvokeHook from patches).</summary>
        protected void Subscribe(string hook) { Host?.Subscribe(hook); }
        /// <summary>Oxide hook unsubscribe (Harmony: unregisters from host).</summary>
        protected void Unsubscribe(string hook) { Host?.Unsubscribe(hook); }
        /// <summary>Write to log file or Puts (Harmony: Puts when no file).</summary>
        protected void LogToFile(string name, string text, object plugin, bool timestamp = false, bool append = false)
        {
            try { Host?.Puts(string.IsNullOrEmpty(name) ? text : $"[{name}] {text}"); } catch { }
        }
        public bool IsCopyPasteLoaded(out string error)
        {
            error = null;
            try
            {
                if (!CopyPasteAPI.IsAvailable)
                {
                    error = "CopyPaste not found. Load HarmonyMods/CopyPaste.dll before RaidableBases.";
                    return false;
                }
                if (CopyPasteAPI.Version.CompareTo(new VersionNumber(4, 2, 7)) < 0)
                {
                    error = "CopyPaste version too old. Need 4.2.7 or newer (found " + CopyPasteAPI.Version.Major + "." + CopyPasteAPI.Version.Minor + "." + CopyPasteAPI.Version.Patch + ").";
                    return false;
                }
                return true;
            }
            catch (Exception ex) { error = "CopyPaste check failed: " + ex.Message; return false; }
        }
    }

    /// <summary>Stub for Oxide CUI. DestroyUi/AddUi use CommunityEntity RPCs.</summary>
    public static class CuiHelper
    {
        public static void DestroyUi(BasePlayer player, string name)
        {
            try
            {
                if (player != null && player.IsConnected && player.net?.connection != null)
                    CommunityEntity.ServerInstance?.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), name);
            }
            catch { }
        }

        public static void AddUi(BasePlayer player, CuiElementContainer container)
        {
            try
            {
                if (player == null || !player.IsConnected || container == null || player.net?.connection == null)
                    return;
                string json = RewriteHarmonyButtonCommands(container.ToJson());
                CommunityEntity.ServerInstance?.ClientRPC(RpcTarget.Player("AddUI", player.net.connection), json);
            }
            catch { }
        }

        /// <summary>
        /// Clients only forward ConsoleGen commands. Oxide-style ui_buyraid / rb_ui_move never leave
        /// the client under Harmony — bridge through cui.endtest RBUI (see Cui_Endtest_Patch).
        /// </summary>
        private static string RewriteHarmonyButtonCommands(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;
            bool hasBuy = json.IndexOf("ui_buyraid", StringComparison.Ordinal) >= 0;
            bool hasMove = json.IndexOf("rb_ui_move", StringComparison.Ordinal) >= 0;
            if (!hasBuy && !hasMove) return json;

            if (hasBuy)
                json = json.Replace("\"command\":\"ui_buyraid", "\"command\":\"cui.endtest RBUI ui_buyraid");
            if (hasMove)
                json = json.Replace("\"command\":\"rb_ui_move", "\"command\":\"cui.endtest RBUI rb_ui_move");
            return json;
        }
    }

    /// <summary>Oxide Interface replacement. CallHook invokes RaidableBases hook when subscribed (from Harmony patches).</summary>
    public static class Interface
    {
        public static object CallHook(string name, params object[] args)
        {
            var host = RaidableBasesHost.Instance;
            if (host?.ModInstance != null && host.IsSubscribed(name))
                return host.InvokeHook(name, args ?? Array.Empty<object>());
            return null;
        }
        public static OxideStub Oxide => OxideStub.Instance;
        public class OxideStub
        {
            public static OxideStub Instance { get; } = new OxideStub();
            public void NextTick(Action action) { try { RaidableBasesHost.Instance?.ModInstance?.NextTick(action); } catch { } }
            public object CallHook(string name, params object[] args) => Interface.CallHook(name, args);
        }
    }

    /// <summary>Stub for Oxide RustCore. FindPlayerById/FindPlayerByName/FindPlayer.</summary>
    public static class RustCore
    {
        public static BasePlayer FindPlayerById(ulong id) => BasePlayer.FindByID(id);
        public static BasePlayer FindPlayerByName(string name) => BasePlayer.Find(name);
        public static BasePlayer FindPlayer(string nameOrId)
        {
            if (string.IsNullOrEmpty(nameOrId)) return null;
            if (ulong.TryParse(nameOrId, out var id)) return BasePlayer.FindByID(id);
            return BasePlayer.Find(nameOrId);
        }
    }

    /// <summary>Stub for Oxide covalence. Players.FindPlayerById, Players.All.</summary>
    public static class covalence
    {
        public static CovalenceStub Players => CovalenceStub.Instance;
        public class CovalenceStub
        {
            public static CovalenceStub Instance { get; } = new CovalenceStub();
            public IPlayer FindPlayerById(ulong id) { var p = BasePlayer.FindByID(id); return p != null ? new BasePlayerWrapper(p) : null; }
            public IPlayer FindPlayerById(string id) { if (string.IsNullOrEmpty(id) || !ulong.TryParse(id, out var u)) return null; return FindPlayerById(u); }
            public IEnumerable<IPlayer> All
            {
                get
                {
                    var list = BasePlayer.activePlayerList;
                    if (list == null) return Array.Empty<IPlayer>();
                    var result = new List<IPlayer>(list.Count);
                    for (int i = 0; i < list.Count; i++)
                        result.Add(new BasePlayerWrapper(list[i]));
                    return result;
                }
            }
        }
    }

    /// <summary>Wraps BasePlayer as IPlayer for covalence/commands.</summary>
    public class BasePlayerWrapper : IPlayer
    {
        private readonly BasePlayer _p;
        public BasePlayerWrapper(BasePlayer p) { _p = p; }
        public string Id => _p?.UserIDString ?? "0";
        public object Object => _p;
        public bool IsServer => false;
        public string Name => _p?.displayName ?? "";
        public bool IsAdmin => _p != null && _p.IsAdmin;
        public bool IsConnected => _p != null && _p.IsConnected;
        public bool IsBanned => false;
        public void Reply(string message) { _p?.SendConsoleCommand("chat.add", 0, message ?? ""); }
        public void Message(string msg) { Reply(msg); }
        public void Teleport(float x, float y, float z) { _p?.Teleport(new Vector3(x, y, z)); }
    }

    /// <summary>Stub for Oxide Player (static Message). Prefer ChatMessage — chat.add arg layouts vary by build.</summary>
    public static class Player
    {
        public static void Message(BasePlayer player, string message, string chatId = null)
        {
            if (player == null || !player.IsConnected || string.IsNullOrEmpty(message))
                return;
            // GrimmNPC / ZombieHorde / Kits path — reliable on this server.
            player.ChatMessage(message);
        }
        public static void Message(BasePlayer player, string message, ulong chatId)
        {
            Message(player, message, chatId.ToString());
        }
    }

    /// <summary>Stub for Oxide Core.Random (UnityEngine.Random).</summary>
    public static class Core
    {
        public static CoreRandom Random => CoreRandom.Instance;
    }
    public class CoreRandom
    {
        public static CoreRandom Instance { get; } = new CoreRandom();
        public float Range(float min, float max) => UnityEngine.Random.Range(min, max);
        public double Range(double min, double max) => UnityEngine.Random.Range((float)min, (float)max);
        public int Range(int min, int max) => UnityEngine.Random.Range(min, max);
    }

    /// <summary>Stub for Oxide Utility (e.g. FormatTime).</summary>
    public static class Utility
    {
        public static string FormatTime(double seconds) => seconds <= 0 ? "0s" : TimeSpan.FromSeconds(seconds).ToString(@"d\.hh\:mm\:ss");
        public static string GetFileNameWithoutExtension(string path) => System.IO.Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>Call plugin method (plugin refs are object; CopyPaste/Kits use Harmony APIs).</summary>
    public static class PluginCall
    {
        public static object Call(object plugin, string methodName, params object[] args)
        {
            if (plugin is CopyPastePluginStub cp) return cp.Call(methodName, args);
            if (plugin is KitsPluginStub kits) return kits.Call(methodName, args);
            return null;
        }
    }

    /// <summary>Extension so plugin?.Call("Method", args) compiles without changing every call site.</summary>
    public static class PluginCallExtensions
    {
        public static object Call(this object plugin, string methodName, params object[] args) => PluginCall.Call(plugin, methodName, args);
        public static string GetPluginName(this object plugin)
        {
            if (plugin is CopyPastePluginStub) return "CopyPaste";
            if (plugin is KitsPluginStub) return "Kits";
            return plugin?.GetType()?.Name ?? "N/A";
        }
    }

    /// <summary>Harmony mod entry. Loaded by HarmonyLoader from HarmonyMods/.</summary>
    public class RaidableBasesHarmonyEntry : IHarmonyModHooks
    {
        public static RaidableBasesHarmonyEntry Instance { get; private set; }
        /// <summary>Set when server init is deferred until after ItemManager.Initialize (avoids NRE in OnLoaded).</summary>
        public static bool DeferredServerInitPending { get; set; }
        private const string LoadGenerationKey = "RaidableBases_LoadGeneration";
        private int _generation;
        private RaidableBases _mod;
        private bool _softInitStarted;
        private bool _softInitFinished;

        /// <summary>True when we need another soft-start attempt (cold boot never reached Queues/grid).</summary>
        public bool NeedsSoftInitRetry()
        {
            if (_mod == null)
                return false;
            // Soft-start never kicked off.
            if (!_softInitStarted)
                return true;
            // Started but never finished and queues/automation never came up.
            if (_softInitFinished)
                return false;
            try
            {
                return _mod.Queues == null;
            }
            catch
            {
                return true;
            }
        }

        private static int ReadLoadGeneration()
        {
            try { return AppDomain.CurrentDomain.GetData(LoadGenerationKey) is int g ? g : 0; }
            catch { return 0; }
        }
        private static void WriteLoadGeneration(int g)
        {
            try { AppDomain.CurrentDomain.SetData(LoadGenerationKey, g); } catch { }
        }

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            // AppDomain-shared so a prior DLL's UnloadAsync cannot tear down this load after harmony.load reload.
            _generation = ReadLoadGeneration() + 1;
            WriteLoadGeneration(_generation);
            _softInitStarted = false;
            _softInitFinished = false;
            // HarmonyLoader already calls PatchAll(assembly) before OnLoaded; do not create a second Harmony instance or we double-patch.
            var host = new RaidableBasesHost();
            host.Init();
            // Report CopyPaste handshake at plugin init (CopyPaste loads first alphabetically and sets AppDomain in OnLoaded so it's already ready).
            var copyPasteError = CopyPasteAPI.GetAvailabilityError();
            if (copyPasteError != null)
                Debug.Log("[RaidableBases] " + copyPasteError);
            KitsAPI.Init();
            if (KitsAPI.IsAvailable)
                Debug.Log("[RaidableBases] Kits Harmony mod linked - profile Scientist/Murderer Kits enabled.");
            else
                Debug.Log("[RaidableBases] Kits Harmony mod not loaded - profile kit lists will use Loadout fallback.");
            _mod = new RaidableBases();
            host.ModInstance = _mod;
            _mod.InitMinimal();
            // Heavy init (InitRest + grid, etc.) runs in deferred soft-start coroutine so load/unload don't freeze the server.
            // Startup: ItemManager ready + ServerMgr_Update after world load. Late-load: same path next frames.
            DeferredServerInitPending = true;
            Debug.Log("[RaidableBases] Harmony mod loaded. Config: " + HarmonyDataLayer.GetPreferredConfigPath() + ". Data: HarmonyData/RaidableBases/. Paste: HarmonyData/copypaste/");
        }

        /// <summary>Called by ServerMgr_Update_Patch. Uses soft-start coroutine so server stays responsive.</summary>
        public void RunDeferredServerInit()
        {
            if (_mod == null) return;
            if (_softInitStarted && _mod.Queues != null)
            {
                _softInitFinished = true;
                return;
            }
            _softInitStarted = true;
            _mod.StartSoftInitCoroutine(() => { _softInitFinished = true; });
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            var mod = _mod;
            var hostToShutdown = RaidableBasesHost.Instance;
            var unloadGeneration = _generation;
            _mod = null;
            DeferredServerInitPending = false;
            _softInitStarted = false;
            _softInitFinished = false;
            if (mod == null)
            {
                FinishUnloadHost(hostToShutdown, unloadGeneration);
                return;
            }
            mod.SetUnloadingState(true, true);
            hostToShutdown?.Timer.DestroyAll();
            try { CommandRegistry.UnregisterAll(); } catch { }
            try { mod.StopLoadCoroutines(); } catch { }
            // Must run entity cleanup synchronously before OnUnloaded returns.
            // Harmony reload loads the next DLL immediately after; a deferred unload
            // was aborted by the generation guard and left bases in the world.
            try { mod.RunUnloadStepsSync(); }
            catch (Exception ex) { Debug.LogWarning("[RaidableBases] Unload cleanup: " + ex.Message); }
            FinishUnloadHost(hostToShutdown, unloadGeneration);
        }

        /// <summary>
        /// Tear down the host only when this unload still owns the current load generation.
        /// Entity cleanup always runs in OnUnloaded regardless of generation.
        /// </summary>
        private void FinishUnloadHost(RaidableBasesHost hostToShutdown, int unloadGeneration)
        {
            if (unloadGeneration != ReadLoadGeneration())
                return; // newer harmony.load owns the host
            if (hostToShutdown != null && RaidableBasesHost.Instance == hostToShutdown)
            {
                hostToShutdown.Shutdown();
                if (Instance == this) Instance = null;
                DeferredServerInitPending = false;
                Debug.Log("[RaidableBases] Harmony mod unloaded.");
            }
        }
    }
}
