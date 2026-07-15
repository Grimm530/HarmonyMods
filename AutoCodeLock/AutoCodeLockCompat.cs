/*
 * Oxide-free shims for AutoCodeLock 3.0.12 Chaos UI under Harmony.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace AutoCodeLockHarmony
{
    /// <summary>Oxide Hash&lt;TKey,TValue&gt; — Dictionary subclass with default-on-miss get.</summary>
    public class Hash<TKey, TValue> : Dictionary<TKey, TValue>
    {
        public new TValue this[TKey key]
        {
            get => TryGetValue(key, out var value) ? value : default;
            set => base[key] = value;
        }
    }

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

    public static class PlayerExtensions
    {
        public static bool IsSteamId(this ulong id) => id > 76561197960265728UL;

        public static bool IsSteamId(this string id) =>
            !string.IsNullOrEmpty(id) && ulong.TryParse(id, out var uid) && uid.IsSteamId();

        public static bool HasPermission(this BasePlayer player, string perm)
        {
            if (player == null || string.IsNullOrEmpty(perm)) return false;
            return PermissionsBridge.UserHasPermission(player.UserIDString, perm);
        }

        public static ulong GetUserId(this BasePlayer player)
        {
            if (player == null) return 0UL;
            try { return player.userID.Get(); }
            catch { return (ulong)player.userID; }
        }
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

        /// <summary>Load HarmonyLanguage/&lt;Mod&gt;.json — file entries win over embedded defaults.</summary>
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
                Debug.LogWarning("[AutoCodeLock] Lang file load failed: " + ex.Message);
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

    public class AutoCodeLockHost
    {
        public static AutoCodeLockHost Instance { get; private set; }

        public string ServerRoot { get; private set; }
        public string ConfigDirectory { get; private set; }
        public string DataDirectory { get; private set; }
        public string LangDirectory { get; private set; }
        public string ConfigPath { get; private set; }
        public string DataPath { get; private set; }
        public string LangPath { get; private set; }
        public LangHelper Lang { get; } = new LangHelper();

        public static void Init(string serverRoot)
        {
            Instance = new AutoCodeLockHost();
            Instance.ServerRoot = serverRoot ?? ".";
            Instance.ConfigDirectory = Path.Combine(Instance.ServerRoot, "HarmonyConfig");
            Instance.DataDirectory = Path.Combine(Instance.ServerRoot, "HarmonyData", "AutoCodeLock");
            Instance.LangDirectory = Path.Combine(Instance.ServerRoot, "HarmonyLanguage");
            Instance.ConfigPath = Path.Combine(Instance.ConfigDirectory, "AutoCodeLock.json");
            Instance.DataPath = Path.Combine(Instance.DataDirectory, "user_data.json");
            Instance.LangPath = Path.Combine(Instance.LangDirectory, "AutoCodeLock.json");

            Directory.CreateDirectory(Instance.ConfigDirectory);
            Directory.CreateDirectory(Instance.DataDirectory);
            Directory.CreateDirectory(Instance.LangDirectory);

            Debug.Log("[AutoCodeLock] Config: " + Instance.ConfigPath);
            Debug.Log("[AutoCodeLock] Data:   " + Instance.DataPath);
            Debug.Log("[AutoCodeLock] Lang:   " + Instance.LangPath);
        }

        /// <summary>Reload HarmonyLanguage/AutoCodeLock.json (does not overwrite the file).</summary>
        public void ReloadLanguage()
        {
            if (Lang.LoadLanguageFile(LangPath))
                Debug.Log($"[AutoCodeLock] OK: Loaded {Lang.FileMessageCount} lang entries from HarmonyLanguage/AutoCodeLock.json");
            else
                Debug.LogWarning("[AutoCodeLock] HarmonyLanguage/AutoCodeLock.json missing or empty — using embedded defaults");
        }

        public static void Shutdown() => Instance = null;
    }

    /// <summary>Oxide Interface stubs used by soft hooks (CanAutoLock).</summary>
    public static class Interface
    {
        public static object CallHook(string name, params object[] args)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                // Allow other Harmony mods to register AppDomain handlers: AutoCodeLock_CanAutoLock etc.
                var key = "AutoCodeLock_Hook_" + name;
                if (AppDomain.CurrentDomain.GetData(key) is Func<object[], object> fn)
                    return fn(args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AutoCodeLock] CallHook(" + name + "): " + ex.Message);
            }
            return null;
        }

        public static void NextTick(Action action)
        {
            try { ServerMgr.Instance?.StartCoroutine(NextTickCoroutine(action)); }
            catch (Exception ex) { Debug.LogWarning("[AutoCodeLock] NextTick: " + ex.Message); }
        }

        private static IEnumerator NextTickCoroutine(Action action)
        {
            yield return null;
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[AutoCodeLock] NextTick: " + ex.Message); }
        }
    }

    /// <summary>Optional NoEscape soft-dependency (Harmony or Oxide not required).</summary>
    public static class NoEscape
    {
        public static bool IsLoaded => Resolve() != null;

        public static bool IsRaidBlocked(BasePlayer player)
        {
            var t = Resolve();
            if (t == null || player == null) return false;
            try
            {
                var mi = t.GetMethod("IsRaidBlocked", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(BasePlayer) }, null);
                if (mi != null && mi.Invoke(null, new object[] { player }) is bool b) return b;
                mi = t.GetMethod("IsRaidBlocked", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string) }, null);
                if (mi != null && mi.Invoke(null, new object[] { player.UserIDString }) is bool b2) return b2;
            }
            catch { }
            return false;
        }

        public static bool IsCombatBlocked(BasePlayer player)
        {
            var t = Resolve();
            if (t == null || player == null) return false;
            try
            {
                var mi = t.GetMethod("IsCombatBlocked", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(BasePlayer) }, null);
                if (mi != null && mi.Invoke(null, new object[] { player }) is bool b) return b;
                mi = t.GetMethod("IsCombatBlocked", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string) }, null);
                if (mi != null && mi.Invoke(null, new object[] { player.UserIDString }) is bool b2) return b2;
            }
            catch { }
            return false;
        }

        private static Type Resolve()
        {
            try
            {
                var fromDomain = AppDomain.CurrentDomain.GetData("NoEscape_ApiType") as Type;
                if (fromDomain != null) return fromDomain;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var t = asm.GetType("NoEscapeHarmony.NoEscapeMod")
                             ?? asm.GetType("NoEscape.NoEscapeMod");
                        if (t != null) return t;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }
    }

    /// <summary>Optional PlayerDLCAPI soft-dependency for codelock skins.</summary>
    public static class PlayerDlcApi
    {
        public static bool IsLoaded => Resolve() != null;

        public static bool IsOwnedOrFreeItem(BasePlayer player, int itemId)
        {
            var t = Resolve();
            if (t == null || player == null) return false;
            try
            {
                var mi = t.GetMethod("IsOwnedOrFreeItem", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(BasePlayer), typeof(int) }, null);
                if (mi != null && mi.Invoke(null, new object[] { player, itemId }) is bool b) return b;
            }
            catch { }
            return false;
        }

        private static Type Resolve()
        {
            try
            {
                var fromDomain = AppDomain.CurrentDomain.GetData("PlayerDlcApi_ApiType") as Type;
                if (fromDomain != null) return fromDomain;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var t = asm.GetType("PlayerDlcApiHarmony.PlayerDlcApiMod")
                             ?? asm.GetType("PlayerDLCAPI.PlayerDLCAPIMod");
                        if (t != null) return t;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
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
