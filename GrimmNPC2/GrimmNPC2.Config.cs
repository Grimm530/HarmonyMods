using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace GrimmNPC2
{
    public partial class GrimmNPC2
    {
        private static readonly string ConfigPath = Path.Combine(Application.dataPath, "..", "HarmonyConfig", "GrimmNPC2.json");

        private GrimmNPC2Config _config;

        /// <summary>
        /// Global Harmony settings for this mod. Loaded only from <c>HarmonyConfig/GrimmNPC2.json</c> (see <c>AI_Framework_GEN2.md</c>).
        /// </summary>
        public static GrimmNPC2Config GetConfig()
        {
            return Instance?._config ?? GrimmNPC2Config.Default();
        }

        /// <summary>Logs when <see cref="GrimmNPC2Config.EnableDebugLogging"/> is true.</summary>
        internal static void LogDebug(string message)
        {
            try
            {
                if (GetConfig().EnableDebugLogging)
                    UnityEngine.Debug.Log("[GrimmNPC2] " + message);
            }
            catch
            {
                // ignored
            }
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    _config = JsonConvert.DeserializeObject<GrimmNPC2Config>(File.ReadAllText(ConfigPath));
                    UnityEngine.Debug.Log("[GrimmNPC2] Config loaded from HarmonyConfig/GrimmNPC2.json");
                }
                else
                {
                    _config = GrimmNPC2Config.Default();
                    SaveConfig();
                    UnityEngine.Debug.Log("[GrimmNPC2] Created default HarmonyConfig/GrimmNPC2.json");
                }

                if (_config == null)
                    _config = GrimmNPC2Config.Default();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[GrimmNPC2] Failed to load config: " + ex);
                _config = GrimmNPC2Config.Default();
            }
        }

        private void SaveConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(_config ?? GrimmNPC2Config.Default(), Formatting.Indented));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[GrimmNPC2] Failed to save config: " + ex);
            }
        }
    }

    /// <summary>
    /// Global Harmony mod settings for GrimmNPC2 (GEN2 support layer). No dependency on other Harmony mods.
    /// </summary>
    public class GrimmNPC2Config
    {
        public bool CanTargetAnimal { get; set; } = false;
        public bool CanTargetNpc { get; set; } = false;
        public bool EnableSwimmingDebug { get; set; } = false;
        public bool CanTargetSleepingPlayer { get; set; } = false;
        /// <summary>When false, custom NPCs reject wounded players via sense policy (boss fights usually want true).</summary>
        public bool CanTargetWoundedPlayer { get; set; } = true;
        public bool CanTargetSafeZonePlayer { get; set; } = false;
        public bool PreventScarecrowTargeting { get; set; } = true;
        public bool ForceRespectAiDormant { get; set; } = false;
        public float DefaultSleepDistance { get; set; } = 160f;
        public bool EnableDebugLogging { get; set; } = false;
        public bool EnableNavMeshValidation { get; set; } = false;
        public bool EnableAssistCallouts { get; set; } = true;
        public float AssistRange { get; set; } = 100f;
        public bool EnableRaidingForAllNpcs { get; set; } = false;
        public List<string> ExcludedTargetTypes { get; set; } = new List<string>();

        public static GrimmNPC2Config Default() => new GrimmNPC2Config();
    }
}
