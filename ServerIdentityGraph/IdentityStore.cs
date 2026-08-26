using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace ServerIdentityGraph
{
    /// <summary>
    /// One JSON file per Steam ID: current in-game team + clan only.
    /// Atomic replace so the Discord bot can read while the server runs.
    /// </summary>
    internal static class IdentityStore
    {
        internal static readonly object Gate = new object();

        static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        static IdentityConfig _config;
        static readonly Dictionary<ulong, PlayerIdentity> Cache = new Dictionary<ulong, PlayerIdentity>();
        static readonly HashSet<ulong> DirtyPlayers = new HashSet<ulong>();
        static string _playersDir;
        static string _configPath;

        internal static IdentityConfig Config => _config ?? (_config = new IdentityConfig());

        internal static void Init()
        {
            lock (Gate)
            {
                var serverRoot = GetServerRoot();
                _playersDir = Path.Combine(serverRoot, "HarmonyData", "ServerIdentityGraph", "players");
                _configPath = Path.Combine(serverRoot, "HarmonyConfig", "ServerIdentityGraph.json");
                Directory.CreateDirectory(_playersDir);
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath) ?? serverRoot);
                LoadConfig();
                IdentityCollector.Ready = true;
            }
        }

        internal static void Shutdown()
        {
            lock (Gate)
            {
                FlushUnlocked();
                Cache.Clear();
                DirtyPlayers.Clear();
                IdentityCollector.Ready = false;
            }
        }

        internal static string GetServerRoot()
        {
            var dp = Application.dataPath ?? "";
            return string.IsNullOrEmpty(dp) ? "." : Path.GetFullPath(Path.Combine(dp, ".."));
        }

        internal static string NowIso() => System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        internal static PlayerIdentity GetOrLoad(ulong steamId)
        {
            PlayerIdentity record;
            if (Cache.TryGetValue(steamId, out record))
                return record;

            var path = PlayerPath(steamId);
            if (File.Exists(path))
            {
                try
                {
                    record = JsonConvert.DeserializeObject<PlayerIdentity>(File.ReadAllText(path));
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[ServerIdentityGraph] Failed to read " + path + ": " + ex.Message);
                }
            }

            if (record == null)
                record = new PlayerIdentity();

            record.SteamId = steamId.ToString();
            if (record.Team?.Members == null && record.Team != null)
                record.Team.Members = new List<MemberSighting>();
            if (record.Clan?.Members == null && record.Clan != null)
                record.Clan.Members = new List<MemberSighting>();
            Cache[steamId] = record;
            return record;
        }

        internal static void MarkDirty(ulong steamId) => DirtyPlayers.Add(steamId);

        internal static void Flush()
        {
            lock (Gate)
            {
                FlushUnlocked();
            }
        }

        internal static string DebugDump(ulong steamId)
        {
            lock (Gate)
            {
                return JsonConvert.SerializeObject(GetOrLoad(steamId), JsonSettings);
            }
        }

        static void FlushUnlocked()
        {
            foreach (var steamId in DirtyPlayers)
            {
                PlayerIdentity record;
                if (!Cache.TryGetValue(steamId, out record) || record == null)
                    continue;
                AtomicWrite(PlayerPath(steamId), JsonConvert.SerializeObject(record, JsonSettings));
            }
            DirtyPlayers.Clear();
        }

        static string PlayerPath(ulong steamId) => Path.Combine(_playersDir, steamId + ".json");

        static void LoadConfig()
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    _config = JsonConvert.DeserializeObject<IdentityConfig>(File.ReadAllText(_configPath));
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[ServerIdentityGraph] Config load failed: " + ex.Message);
                }
            }

            if (_config == null)
                _config = new IdentityConfig();

            try
            {
                AtomicWrite(_configPath, JsonConvert.SerializeObject(_config, JsonSettings));
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[ServerIdentityGraph] Config save failed: " + ex.Message);
            }
        }

        static void AtomicWrite(string path, string json)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }
    }
}
