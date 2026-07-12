using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace AdminAlias
{
    /// <summary>
    /// Config for AdminAlias. Stored at server-root HarmonyConfig/AdminAlias.json.
    /// Maps Steam64 IDs to the display name shown in-game (player list, chat, kill feed, etc.).
    /// </summary>
    public static class AdminAliasConfig
    {
        [Serializable]
        public class ConfigData
        {
            /// <summary>
            /// Steam64 ID -> display name. When a player's Steam ID is in this dictionary,
            /// their in-game displayName is replaced by this value everywhere.
            /// </summary>
            [JsonProperty("Overrides")]
            public Dictionary<string, string> Overrides { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public static ConfigData Config { get; private set; }
        private static string _configPath;

        public static void LoadConfig()
        {
            try
            {
                var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var paths = new[]
                {
                    Path.Combine(serverRoot, "HarmonyConfig", "AdminAlias.json"),
                    Path.Combine(serverRoot, "oxide", "config", "AdminAlias.json"),
                    Path.Combine(serverRoot, "AdminAlias.json")
                };

                foreach (var p in paths)
                {
                    if (!File.Exists(p))
                        continue;

                    var json = File.ReadAllText(p);
                    var cfg = JsonConvert.DeserializeObject<ConfigData>(json);
                    if (cfg != null && cfg.Overrides != null)
                    {
                        Config = cfg;
                        _configPath = p;
                        Debug.Log("[AdminAlias] Config loaded from " + p);
                        return;
                    }
                }

                // Create default config in HarmonyConfig on first load
                Config = new ConfigData();
                _configPath = Path.Combine(serverRoot, "HarmonyConfig", "AdminAlias.json");
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                SaveConfig();
                Debug.Log("[AdminAlias] Config created at " + _configPath);
            }
            catch (Exception ex)
            {
                Debug.LogError("[AdminAlias] Config load error: " + ex.Message);
                Config ??= new ConfigData();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                if (_configPath == null || Config == null) return;
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError("[AdminAlias] Config save error: " + ex.Message);
            }
        }

        /// <summary>
        /// Returns the override name for the given Steam64 ID, or null if none.
        /// </summary>
        public static string GetOverride(ulong steamId)
        {
            var overrides = Config?.Overrides;
            if (overrides == null) return null;
            var key = steamId.ToString();
            return overrides.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name) ? name.Trim() : null;
        }
    }
}
