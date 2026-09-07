using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace InventoryShortcuts;

public class InventoryShortcutsConfig
{
    public static ConfigData Config { get; private set; }

    private static string GetConfigPath()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(root, "HarmonyConfig", "InventoryShortcuts.json");
    }

    public static void Load()
    {
        string path = GetConfigPath();
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                Config = JsonConvert.DeserializeObject<ConfigData>(json) ?? new ConfigData();
            }
            else
            {
                Config = new ConfigData();
                Save();
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[InventoryShortcuts] Failed to load config: {ex.Message}. Using defaults.");
            Config = new ConfigData();
        }
    }

    public static void Save()
    {
        try
        {
            string path = GetConfigPath();
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            string json = JsonConvert.SerializeObject(Config ?? new ConfigData(), Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[InventoryShortcuts] Failed to save config: {ex.Message}");
        }
    }

    public class ConfigData
    {
        [JsonProperty("Button background color (R G B A, 0-1)")]
        public string ButtonColor = "0.33 0.33 0.33 0.62";

        [JsonProperty("Text and icon color (R G B A, 0-1)")]
        public string TextColor = "0.82 0.82 0.82 1";

        [JsonProperty("Hotbar button row height (0-1, tiny buttons)")]
        public float HotbarButtonHeight = 0.018f;

        [JsonProperty("Debug logging")]
        public bool Debug = false;
    }
}
