using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatIcons;

public class ChatIconsConfig
{
    public class ConfigData
    {
        [JsonProperty(PropertyName = "Steam Avatar User ID")]
        public ulong SteamAvatarUserID = 0;

        [JsonProperty(PropertyName = "Replace MOTD icon")]
        public bool ReplaceMotdIcon = true;
    }

    public static ConfigData Config { get; private set; }

    private static string _configPath;

    public static void LoadConfig()
    {
        try
        {
            string serverRoot = Directory.GetCurrentDirectory();
            string harmonyDir = Path.Combine(serverRoot, "HarmonyConfig");
            _configPath = Path.Combine(harmonyDir, "ChatIcons.json");

            if (!Directory.Exists(harmonyDir))
                Directory.CreateDirectory(harmonyDir);

            if (!File.Exists(_configPath))
            {
                LoadDefaultConfig();
                UnityEngine.Debug.Log("[ChatIcons] Created default config at " + _configPath);
                return;
            }

            var json = File.ReadAllText(_configPath);
            Config = JsonConvert.DeserializeObject<ConfigData>(json) ?? new ConfigData();
            UnityEngine.Debug.Log("[ChatIcons] Config loaded from " + _configPath);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("[ChatIcons] Config load failed, using defaults: " + ex.Message);
            Config = new ConfigData();
        }
    }

    private static void LoadDefaultConfig()
    {
        Config = new ConfigData();
        SaveConfig();
    }

    public static void SaveConfig()
    {
        if (string.IsNullOrEmpty(_configPath)) return;
        try
        {
            var json = JToken.Parse(JsonConvert.SerializeObject(Config)).ToString(Formatting.Indented);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("[ChatIcons] Config save failed: " + ex.Message);
        }
    }
}
