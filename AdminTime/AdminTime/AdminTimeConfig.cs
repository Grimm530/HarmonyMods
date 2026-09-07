using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace AdminTime
{
    public static class AdminTimeConfig
    {
        public class ConfigData
        {
            [JsonProperty("Allowed Steam IDs (who can use /mytime and /myweather; empty = no one except admins if AdminsCanUseMytime)")]
            public List<string> AllowedSteamIds { get; set; } = new List<string>();

            [JsonProperty("Admins can use mytime/myweather without being in AllowedSteamIds")]
            public bool AdminsCanUseMytime { get; set; } = true;

            [JsonProperty("Storm command is admin-only (global thunder/lightning)")]
            public bool StormAdminOnly { get; set; } = true;

            [JsonProperty("Block time/weather overrides in event territory (RaidableBases-style; mod cannot call Oxide - use BlockPositions instead)")]
            public bool BlockInEventTerritory { get; set; } = false;

            [JsonProperty("Block positions: list of \"x,z,radius\" to block overrides (e.g. \"100,200,50\")")]
            public List<string> BlockPositions { get; set; } = new List<string>();
        }

        public static ConfigData Config;
        private static List<BlockEntry> _parsedBlocks;

        public static void LoadConfig()
        {
            try
            {
                string serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string[] paths = new[]
                {
                    Path.Combine(serverRoot, "oxide", "config", "AdminTime.json"),
                    Path.Combine(serverRoot, "HarmonyConfig", "AdminTime.json"),
                    Path.Combine(serverRoot, "Config", "AdminTime.json"),
                    Path.Combine(serverRoot, "AdminTime.json"),
                };
                foreach (string p in paths)
                {
                    if (File.Exists(p))
                    {
                        string json = File.ReadAllText(p);
                        Config = JsonConvert.DeserializeObject<ConfigData>(json);
                        if (Config != null)
                        {
                            ParseBlockPositions();
                            Debug.Log("[AdminTime] Config loaded from " + p);
                            return;
                        }
                    }
                }
                Config = new ConfigData();
                _parsedBlocks = new List<BlockEntry>();
            }
            catch (Exception ex)
            {
                Debug.LogError("[AdminTime] Config load error: " + ex.Message);
                Config ??= new ConfigData();
                _parsedBlocks = new List<BlockEntry>();
            }
        }

        private struct BlockEntry
        {
            public float X, Z, RadiusSq;
        }

        private static void ParseBlockPositions()
        {
            _parsedBlocks = new List<BlockEntry>();
            if (Config?.BlockPositions == null) return;
            foreach (string s in Config.BlockPositions)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                string[] parts = s.Trim().Split(',');
                if (parts.Length < 3) continue;
                if (float.TryParse(parts[0].Trim(), out float x) &&
                    float.TryParse(parts[1].Trim(), out float z) &&
                    float.TryParse(parts[2].Trim(), out float r) && r > 0)
                {
                    _parsedBlocks.Add(new BlockEntry { X = x, Z = z, RadiusSq = r * r });
                }
            }
        }

        public static bool IsInBlockedPosition(Vector3 position)
        {
            if (_parsedBlocks == null || _parsedBlocks.Count == 0) return false;
            float px = position.x, pz = position.z;
            foreach (var b in _parsedBlocks)
            {
                float dx = px - b.X, dz = pz - b.Z;
                if (dx * dx + dz * dz <= b.RadiusSq) return true;
            }
            return false;
        }
    }
}
