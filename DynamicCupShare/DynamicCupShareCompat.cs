/*
 * Oxide-free shims for DynamicCupShare 3.1.23 Chaos UI under Harmony.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace DynamicCupShareHarmony
{
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

        public static bool HasPermission(this BasePlayer player, string perm)
        {
            if (player == null || string.IsNullOrEmpty(perm)) return false;
            return PermissionsBridge.UserHasPermission(player.UserIDString, perm);
        }

        public static bool HasPermission(this ulong playerId, string perm)
        {
            if (playerId == 0UL || string.IsNullOrEmpty(perm)) return false;
            return PermissionsBridge.UserHasPermission(playerId.ToString(), perm);
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
                Debug.LogWarning("[DynamicCupShare] Lang file load failed: " + ex.Message);
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

    public class DynamicCupShareHost
    {
        public static DynamicCupShareHost Instance { get; private set; }

        public string ServerRoot { get; private set; }
        public string ConfigDirectory { get; private set; }
        public string DataDirectory { get; private set; }
        public string LangDirectory { get; private set; }
        public string ConfigPath { get; private set; }
        public string DataPath { get; private set; }
        public string TemporarySharesPath { get; private set; }
        public string LangPath { get; private set; }
        public LangHelper Lang { get; } = new LangHelper();

        public static void Init(string serverRoot)
        {
            Instance = new DynamicCupShareHost();
            Instance.ServerRoot = serverRoot ?? ".";
            Instance.ConfigDirectory = Path.Combine(Instance.ServerRoot, "HarmonyConfig");
            Instance.DataDirectory = Path.Combine(Instance.ServerRoot, "HarmonyData", "DynamicCupShare");
            Instance.LangDirectory = Path.Combine(Instance.ServerRoot, "HarmonyLanguage");
            Instance.ConfigPath = Path.Combine(Instance.ConfigDirectory, "DynamicCupShare.json");
            Instance.DataPath = Path.Combine(Instance.DataDirectory, "user_data.json");
            Instance.TemporarySharesPath = Path.Combine(Instance.DataDirectory, "temporary_shares.json");
            Instance.LangPath = Path.Combine(Instance.LangDirectory, "DynamicCupShare.json");

            Directory.CreateDirectory(Instance.ConfigDirectory);
            Directory.CreateDirectory(Instance.DataDirectory);
            Directory.CreateDirectory(Instance.LangDirectory);

            TryMigrateOxideFile(
                Path.Combine(Instance.ServerRoot, "oxide", "config", "DynamicCupShare.json"),
                Instance.ConfigPath,
                "config");
            TryMigrateOxideFile(
                Path.Combine(Instance.ServerRoot, "oxide", "data", "DynamicCupShare", "user_data.json"),
                Instance.DataPath,
                "user data");
            TryMigrateOxideFile(
                Path.Combine(Instance.ServerRoot, "oxide", "data", "DynamicCupShare", "temporary_shares.json"),
                Instance.TemporarySharesPath,
                "temporary shares");

            Debug.Log("[DynamicCupShare] Config: " + Instance.ConfigPath);
            Debug.Log("[DynamicCupShare] Data:   " + Instance.DataPath);
            Debug.Log("[DynamicCupShare] Lang:   " + Instance.LangPath);
        }

        private static void TryMigrateOxideFile(string oxidePath, string destPath, string label)
        {
            if (File.Exists(destPath) || !File.Exists(oxidePath))
                return;
            try
            {
                string dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.Copy(oxidePath, destPath, false);
                Debug.Log($"[DynamicCupShare] Migrated Oxide {label}: {oxidePath} -> {destPath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DynamicCupShare] Oxide " + label + " migrate failed: " + ex.Message);
            }
        }

        public void ReloadLanguage()
        {
            if (Lang.LoadLanguageFile(LangPath))
                Debug.Log($"[DynamicCupShare] OK: Loaded {Lang.FileMessageCount} lang entries from HarmonyLanguage/DynamicCupShare.json");
            else
                Debug.Log("[DynamicCupShare] HarmonyLanguage/DynamicCupShare.json missing or empty — using embedded defaults");
        }

        public static void Shutdown() => Instance = null;
    }

    public static class Interface
    {
        public static void NextTick(Action action)
        {
            try { ServerMgr.Instance?.StartCoroutine(NextTickCoroutine(action)); }
            catch (Exception ex) { Debug.LogWarning("[DynamicCupShare] NextTick: " + ex.Message); }
        }

        private static IEnumerator NextTickCoroutine(Action action)
        {
            yield return null;
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[DynamicCupShare] NextTick: " + ex.Message); }
        }
    }
}
