using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AlwaysBonus;

public class AlwaysBonusConfig
{
    public class ConfigData
    {
        [JsonProperty("Enable auto tree X farming")]
        public bool TreeX = true;

        [JsonProperty("Enable auto node star farming")]
        public bool NodeX = true;
    }

    public static ConfigData Config;

    private static readonly string Location = Path.Combine("HarmonyConfig", "AlwaysBonus.json");

    public static void LoadConfig()
    {
        if (!Directory.Exists("HarmonyConfig"))
        {
            Directory.CreateDirectory("HarmonyConfig");
        }
        if (!File.Exists(Location))
        {
            LoadDefaultConfig();
            return;
        }
        try
        {
            Config = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(Location));
            if (Config == null) LoadDefaultConfig();
        }
        catch
        {
            LoadDefaultConfig();
        }
    }

    private static void LoadDefaultConfig()
    {
        Config = new ConfigData();
        try
        {
            File.WriteAllText(Location,
                JToken.Parse(JsonConvert.SerializeObject(Config)).ToString(Formatting.Indented));
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[AlwaysBonus] Failed to save default config: {ex}");
        }
    }
}
