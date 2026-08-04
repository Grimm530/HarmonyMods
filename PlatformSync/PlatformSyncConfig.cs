using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace PlatformSync
{
    /// <summary>
    /// Config for PlatformSync. Primary path: HarmonyConfig/PlatformSync.json
    /// (migrates from oxide/config/PlatformSync.json if present).
    /// </summary>
    public static class PlatformSyncConfig
    {
        public class ConfigData
        {
            [JsonProperty("GuildID")]
            public string GuildID { get; set; } = "Enter your Guild ID here";

            [JsonProperty("APIToken")]
            public string APIToken { get; set; } = "Enter your Platform Sync API token here";

            [JsonProperty("EnableNitro")]
            public bool EnableNitro { get; set; } = true;

            [JsonProperty("EnableDiscordLink")]
            public bool EnableDiscordLink { get; set; } = true;

            [JsonProperty("LogLinks")]
            public bool LogLinks { get; set; } = true;

            [JsonProperty("LocalVerifyDiscordRole")]
            public string LocalVerifyDiscordRole { get; set; } = "Verified";

            [JsonProperty("LocalVerifyOxideGroup")]
            public string LocalVerifyOxideGroup { get; set; } = "verified";
        }

        public static ConfigData Config { get; private set; }
        public static string ConfigPath { get; private set; }

        /// <summary>Dictionary-style access matching Oxide Config[key] usage in the original plugin.</summary>
        public static object Get(string key)
        {
            var c = Config;
            if (c == null) return null;
            switch (key)
            {
                case "GuildID": return c.GuildID;
                case "APIToken": return c.APIToken;
                case "EnableNitro": return c.EnableNitro;
                case "EnableDiscordLink": return c.EnableDiscordLink;
                case "LogLinks": return c.LogLinks;
                case "LocalVerifyDiscordRole": return c.LocalVerifyDiscordRole;
                case "LocalVerifyOxideGroup": return c.LocalVerifyOxideGroup;
                default: return null;
            }
        }

        public static void Set(string key, object value)
        {
            Config ??= new ConfigData();
            switch (key)
            {
                case "GuildID": Config.GuildID = value?.ToString() ?? ""; break;
                case "APIToken": Config.APIToken = value?.ToString() ?? ""; break;
                case "EnableNitro": Config.EnableNitro = value is bool b1 ? b1 : Convert.ToBoolean(value); break;
                case "EnableDiscordLink": Config.EnableDiscordLink = value is bool b2 ? b2 : Convert.ToBoolean(value); break;
                case "LogLinks": Config.LogLinks = value is bool b3 ? b3 : Convert.ToBoolean(value); break;
                case "LocalVerifyDiscordRole": Config.LocalVerifyDiscordRole = value?.ToString() ?? ""; break;
                case "LocalVerifyOxideGroup": Config.LocalVerifyOxideGroup = value?.ToString() ?? ""; break;
            }
        }

        public static void LoadConfig()
        {
            try
            {
                var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var harmonyPath = Path.Combine(serverRoot, "HarmonyConfig", "PlatformSync.json");
                var oxidePath = Path.Combine(serverRoot, "oxide", "config", "PlatformSync.json");

                string path = null;
                if (File.Exists(harmonyPath)) path = harmonyPath;
                else if (File.Exists(oxidePath)) path = oxidePath;

                if (path != null)
                {
                    var json = File.ReadAllText(path);
                    var cfg = JsonConvert.DeserializeObject<ConfigData>(json);
                    if (cfg != null)
                    {
                        Config = cfg;
                        ConfigPath = harmonyPath;
                        if (!string.Equals(path, harmonyPath, StringComparison.OrdinalIgnoreCase))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(harmonyPath) ?? serverRoot);
                            SaveConfig();
                            Debug.Log("[PlatformSync] Migrated config from oxide/config/PlatformSync.json");
                        }
                        else
                        {
                            Debug.Log("[PlatformSync] Config loaded from " + path);
                        }
                        return;
                    }
                }

                Config = new ConfigData();
                ConfigPath = harmonyPath;
                Directory.CreateDirectory(Path.GetDirectoryName(harmonyPath) ?? serverRoot);
                SaveConfig();
                Debug.Log("[PlatformSync] Config created at " + harmonyPath);
            }
            catch (Exception ex)
            {
                Debug.LogError("[PlatformSync] Config load error: " + ex.Message);
                Config ??= new ConfigData();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                if (string.IsNullOrEmpty(ConfigPath) || Config == null) return;
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(Config, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogError("[PlatformSync] Config save error: " + ex.Message);
            }
        }
    }
}
