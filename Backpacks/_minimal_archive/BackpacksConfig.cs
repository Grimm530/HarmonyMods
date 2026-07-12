using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Backpacks
{
    public static class BackpacksConfig
    {
        public class ConfigData
        {
            [JsonProperty("Drop on death (spawn dropped backpack at death position)")]
            public bool DropOnDeath { get; set; } = true;

            [JsonProperty("Erase on death (delete backpack contents instead of dropping)")]
            public bool EraseOnDeath { get; set; } = false;

            [JsonProperty("Backpack capacity (slots) - used for single page; if Pages > 1 see SlotsPerPage")]
            public int Capacity { get; set; } = 24;

            [JsonProperty("Number of backpack pages (e.g. 3 = Page 1, 2, 3 buttons)")]
            public int PageCount { get; set; } = 3;

            [JsonProperty("Slots per page (8 rows × 6 = 48)")]
            public int SlotsPerPage { get; set; } = 48;

            [JsonProperty("Minimum despawn time for dropped backpacks (seconds)")]
            public float MinimumDespawnTime { get; set; } = 300f;

            [JsonProperty("Show backpack button on screen (click to open)")]
            public bool ShowButton { get; set; } = true;

            [JsonProperty("Button image path (relative to server root; loaded into FileStorage on plugin load and cached for CUI)")]
            public string ButtonImagePath { get; set; } = "HarmonyImages/Backpack/backpackgz.png";

            [JsonProperty("Button image URL (optional; overrides path if set; use for hosted image)")]
            public string ButtonImageUrl { get; set; } = "";

            [JsonProperty("Button position (match Oxide Backpacks: anchor 0.5 0.0 = center bottom, offsets in pixels)")]
            public string ButtonAnchorsMin { get; set; } = "0.5 0.0";

            [JsonProperty("Button position anchormax (same as min = point anchor)")]
            public string ButtonAnchorsMax { get; set; } = "0.5 0.0";

            [JsonProperty("Button offsetmin (pixels, e.g. -260 18 = left 260px of center, 18px up)")]
            public string ButtonOffsetsMin { get; set; } = "-260 18";

            [JsonProperty("Button offsetmax (pixels, e.g. -200 78 = 60px wide, 60px tall)")]
            public string ButtonOffsetsMax { get; set; } = "-200 78";

            [JsonProperty("Data folder path (relative to server root; empty = HarmonyMods_Data/BackpacksData)")]
            public string DataFolderPath { get; set; } = "HarmonyMods_Data/BackpacksData";

            [JsonProperty("Log backpack load path and item count (for debugging)")]
            public bool LogLoadPath { get; set; } = true;
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
                    Path.Combine(serverRoot, "oxide", "config", "Backpacks.json"),
                    Path.Combine(serverRoot, "HarmonyConfig", "Backpacks.json"),
                    Path.Combine(serverRoot, "Config", "Backpacks.json"),
                    Path.Combine(serverRoot, "Backpacks.json"),
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
                            Config.Capacity = Mathf.Clamp(Config.Capacity, 1, 48);
                            Config.PageCount = Mathf.Clamp(Config.PageCount, 1, 8);
                            Config.SlotsPerPage = Mathf.Clamp(Config.SlotsPerPage, 6, 48);
                            UnityEngine.Debug.Log("[Backpacks] Config loaded from " + p);
                            return;
                        }
                    }
                }
                Config = new ConfigData();
                _configPath = Path.Combine(serverRoot, "HarmonyConfig", "Backpacks.json");
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                SaveConfig();
                UnityEngine.Debug.Log("[Backpacks] Config created at " + _configPath);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[Backpacks] Config load error: " + ex.Message);
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
                UnityEngine.Debug.LogError("[Backpacks] Config save error: " + ex.Message);
            }
        }
    }
}
