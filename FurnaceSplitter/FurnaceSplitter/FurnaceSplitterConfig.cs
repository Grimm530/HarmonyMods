using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace FurnaceSplitter
{
    public class FurnaceSplitterConfig
    {
        public class OvenConfig
        {
            public bool enabled = true;
            public bool autoFuelTransfer = true;
        }

        /// <summary>When true, log to server console when items move to ovens and what the mod does.</summary>
        public bool debug = false;

        public Dictionary<string, OvenConfig> ovens = new Dictionary<string, OvenConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = new OvenConfig { enabled = true, autoFuelTransfer = true }
        };

        private static FurnaceSplitterConfig _config;
        public static FurnaceSplitterConfig Config => _config ?? Load();

        private static readonly string ConfigPath = Path.Combine("HarmonyConfig", "FurnaceSplitter.json");

        public static FurnaceSplitterConfig Load()
        {
            try
            {
                if (!Directory.Exists("HarmonyConfig"))
                    Directory.CreateDirectory("HarmonyConfig");

                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    _config = JsonConvert.DeserializeObject<FurnaceSplitterConfig>(json);
                    if (_config?.ovens == null)
                        _config = new FurnaceSplitterConfig();
                    else if (json.IndexOf("\"debug\"", StringComparison.OrdinalIgnoreCase) < 0)
                        Save(); // Migrate: add new fields (e.g. debug) to existing config
                }
                else
                {
                    _config = new FurnaceSplitterConfig();
                    Save();
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FurnaceSplitter] Config load error: {ex.Message}");
                _config = new FurnaceSplitterConfig();
            }

            return _config;
        }

        public static void Save()
        {
            try
            {
                if (_config == null) return;
                if (!Directory.Exists("HarmonyConfig"))
                    Directory.CreateDirectory("HarmonyConfig");
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(_config, Formatting.Indented));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FurnaceSplitter] Config save error: {ex.Message}");
            }
        }

        public OvenConfig GetOvenConfig(string shortName)
        {
            if (ovens.TryGetValue(shortName, out var cfg) && cfg.enabled)
                return cfg;
            if (ovens.TryGetValue("*", out cfg) && cfg.enabled)
                return cfg;
            return null;
        }

        public static void Log(string message)
        {
            if (Config?.debug == true)
                UnityEngine.Debug.Log($"[FurnaceSplitter] {message}");
        }
    }
}
