using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InstantBarrel;

public class InstantBarrelConfig
{
    public class ConfigData
    {
        [JsonProperty("Enable farming with weapons")]
        public bool EnableWeapon = true;

        [JsonProperty("Max farming distance")]
        public float MaxDistance = 3f;

        [JsonProperty("Make barrels 1 hit to kill")]
        public bool OneShot = true;

        [JsonProperty("Enable barrel gibs")]
        public bool Gibs = true;

        [JsonProperty("Require permission (instantbarrel.on). If false, all players get instant barrels")]
        public bool RequirePermission = true;
    }

    public static ConfigData Config;

    private static readonly string Location = Path.Combine("HarmonyConfig", "InstantBarrel.json");

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
            UnityEngine.Debug.LogError($"[InstantBarrel] Failed to save default config: {ex}");
        }
    }
}
