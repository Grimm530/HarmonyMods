using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace PveModeHarmony
{
    /// <summary>
    /// Port of the Oxide PveMode PluginConfig. Stored at HarmonyConfig/PveMode.json.
    /// </summary>
    public class PveModeConfig
    {
        [JsonProperty("TimeLastDamage")]
        public int TimeLastDamage { get; set; } = 300;

        [JsonProperty("NoEnterAnotherOwner")]
        public bool NoEnterAnotherOwner { get; set; } = false;

        [JsonProperty("IgnoreAdmin")]
        public bool IgnoreAdmin { get; set; } = false;

        [JsonProperty("PluginVersion")]
        public string PluginVersion { get; set; } = "1.2.9";

        public static PveModeConfig Default()
        {
            return new PveModeConfig
            {
                TimeLastDamage = 300,
                NoEnterAnotherOwner = false,
                IgnoreAdmin = false,
                PluginVersion = "1.2.9"
            };
        }

        public static PveModeConfig Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    PveModeConfig cfg = JsonConvert.DeserializeObject<PveModeConfig>(json);
                    if (cfg != null) return cfg;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PveMode] Failed to load config from " + path + ": " + ex.Message + ". Using defaults.");
            }
            PveModeConfig def = Default();
            def.Save(path);
            return def;
        }

        public void Save(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PveMode] Failed to save config to " + path + ": " + ex.Message);
            }
        }
    }
}
