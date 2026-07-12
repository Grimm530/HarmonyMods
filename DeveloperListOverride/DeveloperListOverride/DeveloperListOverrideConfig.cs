using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace DeveloperListOverride
{
    public static class DeveloperListOverrideConfig
    {
        static DeveloperListOverrideConfig()
        {
            LoadConfig();
        }

        [Serializable]
        public class ConfigData
        {
            [JsonProperty("Developer Steam IDs (64-bit; these players get orange name and developer tools)")]
            public List<string> DeveloperSteamIds { get; set; } = new List<string>();
        }

        public static ConfigData Config;
        private static string _configPath;

        public static void LoadConfig()
        {
            try
            {
                var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var paths = new[]
                {
                    Path.Combine(serverRoot, "HarmonyConfig", "DeveloperListOverride.json"),
                    Path.Combine(serverRoot, "oxide", "config", "DeveloperListOverride.json"),
                    Path.Combine(serverRoot, "Config", "DeveloperListOverride.json"),
                    Path.Combine(serverRoot, "DeveloperListOverride.json"),
                };
                foreach (var p in paths)
                {
                    if (File.Exists(p))
                    {
                        _configPath = p;
                        var json = File.ReadAllText(p);
                        Config = JsonConvert.DeserializeObject<ConfigData>(json);
                        if (Config != null)
                        {
                            UnityEngine.Debug.Log("[DeveloperListOverride] Config loaded from " + p);
                            return;
                        }
                    }
                }
                Config = new ConfigData();
                _configPath = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "HarmonyConfig", "DeveloperListOverride.json");
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                SaveConfig();
                UnityEngine.Debug.Log("[DeveloperListOverride] Config created at " + _configPath);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[DeveloperListOverride] Config load error: " + ex.Message);
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
                UnityEngine.Debug.LogError("[DeveloperListOverride] Config save error: " + ex.Message);
            }
        }

        public static bool IsOverrideDeveloper(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId)) return false;
            var list = Config?.DeveloperSteamIds;
            if (list == null || list.Count == 0) return false;
            var normalized = steamId.Trim();
            return list.Exists(id => string.Equals(id?.Trim(), normalized, StringComparison.Ordinal));
        }
    }
}
