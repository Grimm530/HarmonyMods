using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace ShorterNights;

public static class ShorterNightsConfig
{
    public static ConfigData Config { get; private set; }

    private static string GetConfigPath()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(root, "HarmonyConfig", "ShorterNights.json");
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
            UnityEngine.Debug.LogWarning($"[ShorterNights] Failed to load config: {ex.Message}. Using defaults.");
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
            UnityEngine.Debug.LogWarning($"[ShorterNights] Failed to save config: {ex.Message}");
        }
    }

    public class ConfigData
    {
        [JsonProperty("Show time of day display on screen")]
        public bool ShowTimeOfDayDisplay { get; set; } = true;
    }
}
