/*
 * IndustrialTransferSpeed - Config loader
 * Config: HarmonyConfig/IndustrialTransferSpeed.json
 */

using System;
using System.IO;
using Newtonsoft.Json;

namespace IndustrialTransferSpeed
{
    public class IndustrialTransferSpeedConfig
    {
        [JsonProperty("MaxStackSizePerMove (van is 128)")]
        public int MaxStackSizePerMove { get; set; } = 256;

        private static IndustrialTransferSpeedConfig _config;
        public static IndustrialTransferSpeedConfig Config => _config ?? Load();

        private static readonly string ConfigPath = Path.Combine("HarmonyConfig", "IndustrialTransferSpeed.json");

        public static IndustrialTransferSpeedConfig Load()
        {
            try
            {
                if (!Directory.Exists("HarmonyConfig"))
                    Directory.CreateDirectory("HarmonyConfig");

                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    _config = JsonConvert.DeserializeObject<IndustrialTransferSpeedConfig>(json);
                    if (_config == null)
                        _config = new IndustrialTransferSpeedConfig();
                    _config.MaxStackSizePerMove = Math.Max(1, Math.Min(100000, _config.MaxStackSizePerMove));
                }
                else
                {
                    _config = new IndustrialTransferSpeedConfig();
                    Save();
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[IndustrialTransferSpeed] Config load error: {ex.Message}");
                _config = new IndustrialTransferSpeedConfig();
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
                UnityEngine.Debug.LogError($"[IndustrialTransferSpeed] Config save error: {ex.Message}");
            }
        }
    }
}
