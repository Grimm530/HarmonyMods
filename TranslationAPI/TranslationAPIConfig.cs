using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace TranslationAPI;

public class TranslationAPIConfig
{
    public class ConfigData
    {
        [JsonProperty("API key (if required)")]
        public string ApiKey { get; set; } = string.Empty;

        [JsonProperty("Translation service")]
        public string Service { get; set; } = "google";
    }

    public static ConfigData Config { get; private set; }

    public static void LoadConfig()
    {
        var path = "HarmonyConfig/TranslationAPI.json";
        var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var fullPath = Path.Combine(serverRoot, path);

        if (File.Exists(fullPath))
        {
            try
            {
                var json = File.ReadAllText(fullPath);
                Config = JsonConvert.DeserializeObject<ConfigData>(json) ?? new ConfigData();
                return;
            }
            catch (Exception ex)
            {
                TranslationAPIMod.Log($"Failed to load config from {path}: {ex.Message}", force: true);
            }
        }

        Config = new ConfigData();
        SaveConfig();
    }

    public static void SaveConfig()
    {
        var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var dir = Path.Combine(serverRoot, "HarmonyConfig");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "TranslationAPI.json");
        try
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(Config ?? new ConfigData(), Formatting.Indented));
        }
        catch (Exception ex)
        {
            TranslationAPIMod.Log($"Failed to save config: {ex.Message}", force: true);
        }
    }
}
