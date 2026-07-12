using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace ChatTranslator;

public class ChatTranslatorConfig
{
    public class ConfigData
    {
        [JsonProperty("Force default server language")]
        public bool ForceServerDefault { get; set; } = false;

        [JsonProperty("Log translated chat messages")]
        public bool LogChatMessages { get; set; } = false;

        [JsonProperty("Show original and translation")]
        public bool ShowBothMessages { get; set; } = false;

        [JsonProperty("Translate message for sender")]
        public bool TranslateForSender { get; set; } = false;

        [JsonProperty("Skip translation when sender and receiver use same language (saves API calls)")]
        public bool SkipSameLanguage { get; set; } = true;

        [JsonProperty("Default server language code (e.g. en, es, de)")]
        public string DefaultServerLanguage { get; set; } = "en";
    }

    public static ConfigData Config { get; private set; }

    public static void LoadConfig()
    {
        var path = "HarmonyConfig/ChatTranslator.json";
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
                ChatTranslatorMod.Log($"Failed to load config from {path}: {ex.Message}", force: true);
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
        var path = Path.Combine(dir, "ChatTranslator.json");
        try
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(Config ?? new ConfigData(), Formatting.Indented));
        }
        catch (Exception ex)
        {
            ChatTranslatorMod.Log($"Failed to save config: {ex.Message}", force: true);
        }
    }

    /// <summary>Gets the server default language code.</summary>
    public static string GetServerLanguage()
    {
        var c = Config?.DefaultServerLanguage;
        if (string.IsNullOrWhiteSpace(c)) return "en";
        c = c.Contains("-") ? c.Split('-')[0].ToLower() : c.ToLower();
        try
        {
            _ = CultureInfo.GetCultureInfo(c);
            return c;
        }
        catch { return "en"; }
    }
}
