using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace IndustrialTransferSpeed
{
    public static class PlanterProductionSettings
    {
        public const string ModeFruit = "Fruit";
        public const string ModeSeed = "Seed";
        public const string ModeClone = "Clone";

        private static readonly string DataPath = Path.Combine("HarmonyConfig", "IndustrialTransferSpeed.Planters.json");
        private static Dictionary<string, string> _planterModes = new Dictionary<string, string>();

        public static void Load()
        {
            try
            {
                if (!Directory.Exists("HarmonyConfig"))
                {
                    Directory.CreateDirectory("HarmonyConfig");
                }

                if (File.Exists(DataPath))
                {
                    _planterModes = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(DataPath))
                        ?? new Dictionary<string, string>();
                    NormalizeLoadedModes();
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[IndustrialTransferSpeed] Planter mode data load error: {ex.Message}");
                _planterModes = new Dictionary<string, string>();
            }
        }

        public static string GetMode(PlanterBox planter)
        {
            string key = GetKey(planter);
            if (!string.IsNullOrEmpty(key) && _planterModes.TryGetValue(key, out string mode))
            {
                return NormalizeMode(mode);
            }

            return NormalizeMode(IndustrialTransferSpeedConfig.Config.PlanterAutoHarvestMode);
        }

        public static void SetMode(PlanterBox planter, string mode)
        {
            string key = GetKey(planter);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            mode = NormalizeMode(mode);
            _planterModes[key] = mode;

            Save();
        }

        public static string GetDisplayName(string mode)
        {
            mode = NormalizeMode(mode);
            if (string.Equals(mode, ModeFruit, StringComparison.OrdinalIgnoreCase))
            {
                return "Harvest";
            }

            if (string.Equals(mode, ModeSeed, StringComparison.OrdinalIgnoreCase))
            {
                return "Seeds";
            }

            if (string.Equals(mode, ModeClone, StringComparison.OrdinalIgnoreCase))
            {
                return "Clones";
            }

            return mode;
        }

        public static string NormalizeMode(string mode)
        {
            if (string.Equals(mode, ModeFruit, StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "Harvest", StringComparison.OrdinalIgnoreCase))
            {
                return ModeFruit;
            }

            if (string.Equals(mode, ModeSeed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "Seeds", StringComparison.OrdinalIgnoreCase))
            {
                return ModeSeed;
            }

            if (string.Equals(mode, ModeClone, StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "Genetic", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "Genetics", StringComparison.OrdinalIgnoreCase))
            {
                return ModeClone;
            }

            return ModeFruit;
        }

        private static void NormalizeLoadedModes()
        {
            List<string> keys = new List<string>(_planterModes.Keys);
            foreach (string key in keys)
            {
                string mode = NormalizeMode(_planterModes[key]);
                _planterModes[key] = mode;
            }

            Save();
        }

        private static string GetKey(PlanterBox planter)
        {
            if (planter == null || planter.net == null)
            {
                return null;
            }

            return planter.net.ID.Value.ToString();
        }

        private static void Save()
        {
            try
            {
                if (!Directory.Exists("HarmonyConfig"))
                {
                    Directory.CreateDirectory("HarmonyConfig");
                }

                File.WriteAllText(DataPath, JsonConvert.SerializeObject(_planterModes, Formatting.Indented));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[IndustrialTransferSpeed] Planter mode data save error: {ex.Message}");
            }
        }
    }
}
