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

        [JsonProperty("ComposterAdaptorLocalPosition (x y z)")]
        public float[] ComposterAdaptorLocalPosition { get; set; } = { 0f, 0.7f, 0.62f };

        [JsonProperty("ComposterAdaptorLocalRotation (x y z)")]
        public float[] ComposterAdaptorLocalRotation { get; set; } = { 90f, 0f, 0f };

        [JsonProperty("ComposterAdaptorLayoutVersion")]
        public int ComposterAdaptorLayoutVersion { get; set; }

        [JsonProperty("PlanterAdaptorLocalPosition (x y z)")]
        public float[] PlanterAdaptorLocalPosition { get; set; } = { 0f, 0.2f, 0.32f };

        [JsonProperty("PlanterAdaptorLocalRotation (x y z)")]
        public float[] PlanterAdaptorLocalRotation { get; set; } = { 0f, 0f, 0f };

        [JsonProperty("PlanterAdaptorLayoutVersion")]
        public int PlanterAdaptorLayoutVersion { get; set; }

        [JsonProperty("PlanterAutoHarvestEnabled")]
        public bool PlanterAutoHarvestEnabled { get; set; } = true;

        [JsonProperty("PlanterAutoHarvestIntervalSeconds")]
        public float PlanterAutoHarvestIntervalSeconds { get; set; } = 10f;

        [JsonProperty("PlanterAutoHarvestMode (Harvest, Seed, or Clone)")]
        public string PlanterAutoHarvestMode { get; set; } = "Harvest";

        [JsonProperty("PlanterAutoHarvestStage (Fruiting or Ripe)")]
        public string PlanterAutoHarvestStage { get; set; } = "Ripe";

        [JsonProperty("PlanterAutoHarvestStageThresholdPercent")]
        public int PlanterAutoHarvestStageThresholdPercent { get; set; }

        [JsonProperty("PlanterAutoCloneStage (Sapling, Mature, Fruiting, or Ripe)")]
        public string PlanterAutoCloneStage { get; set; } = "Sapling";

        [JsonProperty("PlanterAutoCloneStageThresholdPercent")]
        public int PlanterAutoCloneStageThresholdPercent { get; set; }

        [JsonProperty("ComposterAdaptorLocalPositions (x y z)")]
        public float[][] ComposterAdaptorLocalPositions { get; set; } =
        {
            new[] { 0f, 0.7f, 0.62f }
        };

        [JsonProperty("ComposterAdaptorLocalRotations (x y z)")]
        public float[][] ComposterAdaptorLocalRotations { get; set; } =
        {
            new[] { 90f, 0f, 0f }
        };

        private static readonly float[][] DefaultComposterAdaptorLocalPositions =
        {
            new[] { 0f, 0.7f, 0.62f }
        };

        private static readonly float[][] DefaultComposterAdaptorLocalRotations =
        {
            new[] { 90f, 0f, 0f }
        };

        private static readonly float[] DefaultPlanterAdaptorLocalPosition = { 0f, 0.2f, 0.32f };

        private static readonly float[] DefaultPlanterAdaptorLocalRotation = { 0f, 0f, 0f };

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
                    _config.NormalizeComposterAdaptorTransform();
                    Save();
                }
                else
                {
                    _config = new IndustrialTransferSpeedConfig();
                    _config.NormalizeComposterAdaptorTransform();
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

        private void NormalizeComposterAdaptorTransform()
        {
            if (ComposterAdaptorLocalPosition == null || ComposterAdaptorLocalPosition.Length != 3)
            {
                ComposterAdaptorLocalPosition = new[] { 0f, 0.7f, 0.62f };
            }

            if (ComposterAdaptorLocalRotation == null || ComposterAdaptorLocalRotation.Length != 3)
            {
                ComposterAdaptorLocalRotation = new[] { 90f, 0f, 0f };
            }

            if (ComposterAdaptorLayoutVersion != 10)
            {
                ComposterAdaptorLayoutVersion = 10;
                ComposterAdaptorLocalPositions = CloneLayout(DefaultComposterAdaptorLocalPositions);
                ComposterAdaptorLocalRotations = CloneLayout(DefaultComposterAdaptorLocalRotations);
            }

            if (PlanterAdaptorLocalPosition == null || PlanterAdaptorLocalPosition.Length != 3)
            {
                PlanterAdaptorLocalPosition = CloneVector(DefaultPlanterAdaptorLocalPosition);
            }

            if (PlanterAdaptorLocalRotation == null || PlanterAdaptorLocalRotation.Length != 3)
            {
                PlanterAdaptorLocalRotation = CloneVector(DefaultPlanterAdaptorLocalRotation);
            }

            if (PlanterAdaptorLayoutVersion != 3)
            {
                PlanterAdaptorLayoutVersion = 3;
                PlanterAdaptorLocalPosition = CloneVector(DefaultPlanterAdaptorLocalPosition);
                PlanterAdaptorLocalRotation = CloneVector(DefaultPlanterAdaptorLocalRotation);
            }

            PlanterAutoHarvestIntervalSeconds = Math.Max(1f, Math.Min(300f, PlanterAutoHarvestIntervalSeconds));
            if (string.Equals(PlanterAutoHarvestMode, "Harvest", StringComparison.OrdinalIgnoreCase))
            {
                PlanterAutoHarvestMode = "Fruit";
            }

            if (!string.Equals(PlanterAutoHarvestMode, "Clone", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(PlanterAutoHarvestMode, "Seed", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(PlanterAutoHarvestMode, "Fruit", StringComparison.OrdinalIgnoreCase))
            {
                PlanterAutoHarvestMode = "Fruit";
            }

            if (!IsValidHarvestStage(PlanterAutoHarvestStage))
            {
                PlanterAutoHarvestStage = "Ripe";
            }

            if (!IsValidCloneStage(PlanterAutoCloneStage))
            {
                PlanterAutoCloneStage = "Sapling";
            }

            PlanterAutoHarvestStageThresholdPercent = Math.Max(0, Math.Min(100, PlanterAutoHarvestStageThresholdPercent));
            PlanterAutoCloneStageThresholdPercent = Math.Max(0, Math.Min(100, PlanterAutoCloneStageThresholdPercent));

            if (ComposterAdaptorLocalPositions == null || ComposterAdaptorLocalPositions.Length == 0)
            {
                ComposterAdaptorLocalPositions = new[] { ComposterAdaptorLocalPosition };
            }

            for (int i = 0; i < ComposterAdaptorLocalPositions.Length; i++)
            {
                if (ComposterAdaptorLocalPositions[i] == null || ComposterAdaptorLocalPositions[i].Length != 3)
                {
                    ComposterAdaptorLocalPositions[i] = ComposterAdaptorLocalPosition;
                }
            }

            if (ComposterAdaptorLocalRotations == null || ComposterAdaptorLocalRotations.Length != ComposterAdaptorLocalPositions.Length)
            {
                ComposterAdaptorLocalRotations = new float[ComposterAdaptorLocalPositions.Length][];
            }

            for (int i = 0; i < ComposterAdaptorLocalRotations.Length; i++)
            {
                if (ComposterAdaptorLocalRotations[i] == null || ComposterAdaptorLocalRotations[i].Length != 3)
                {
                    ComposterAdaptorLocalRotations[i] = ComposterAdaptorLocalRotation;
                }
            }
        }

        private static float[][] CloneLayout(float[][] layout)
        {
            float[][] clone = new float[layout.Length][];
            for (int i = 0; i < layout.Length; i++)
            {
                clone[i] = new[] { layout[i][0], layout[i][1], layout[i][2] };
            }
            return clone;
        }

        private static float[] CloneVector(float[] vector)
        {
            return new[] { vector[0], vector[1], vector[2] };
        }

        private static bool IsValidHarvestStage(string stage)
        {
            return string.Equals(stage, "Fruiting", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stage, "Ripe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidCloneStage(string stage)
        {
            return string.Equals(stage, "Sapling", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stage, "Mature", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stage, "Fruiting", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stage, "Ripe", StringComparison.OrdinalIgnoreCase);
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
