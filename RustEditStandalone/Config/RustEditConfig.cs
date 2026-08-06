using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace RustEditStandalone.Config;

public sealed class RustEditConfig
{
    public const string FileName = "RustEdit.json";

    [JsonProperty("Automatic Updates")]
    public AutoUpdaterSettings Updater { get; set; } = new();

    [JsonProperty("Spawn Handlers")]
    public SpawnSystems Spawnables { get; set; } = new();

    [JsonProperty("Respawn Times")]
    public RespawnTimes Respawn { get; set; } = new();

    public static RustEditConfig Data { get; private set; } = new();

    public static string ConfigPath
    {
        get
        {
            string serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(serverRoot, "HarmonyConfig", FileName);
        }
    }

    public static void Load()
    {
        try
        {
            string path = ConfigPath;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(path))
            {
                Data = new RustEditConfig();
                Save();
                Debug.Log("[RustEditStandalone] Created default config at HarmonyConfig/RustEdit.json");
                return;
            }

            Data = JsonConvert.DeserializeObject<RustEditConfig>(File.ReadAllText(path)) ?? new RustEditConfig();
            if (Data.Updater == null) Data.Updater = new AutoUpdaterSettings();
            if (Data.Spawnables == null) Data.Spawnables = new SpawnSystems();
            if (Data.Respawn == null) Data.Respawn = new RespawnTimes();
            Data.Updater.Enabled = false;
            Save();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[RustEditStandalone] Config load failed, using defaults: " + ex.Message);
            Data = new RustEditConfig();
        }
    }

    public static void Save()
    {
        try
        {
            string path = ConfigPath;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonConvert.SerializeObject(Data, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[RustEditStandalone] Config save failed: " + ex.Message);
        }
    }

    public sealed class AutoUpdaterSettings
    {
        [JsonProperty("Enabled")]
        public bool Enabled { get; set; }
    }

    public sealed class SpawnSystems
    {
        [JsonProperty("Enable loot container spawn handlers")]
        public bool Loot { get; set; } = true;

        [JsonProperty("Enable resource spawn handlers")]
        public bool Resources { get; set; } = true;

        [JsonProperty("Enable NPC spawn handlers")]
        public bool NPCs { get; set; } = true;

        [JsonProperty("Enable APC spawn handlers")]
        public bool APC { get; set; } = true;
    }

    public sealed class RespawnTimes
    {
        [JsonProperty("Default loot containers")]
        public RespawnMinMax Loot { get; set; } = new(30, 60);

        [JsonProperty("Desk keycard")]
        public RespawnMinMax Keycard { get; set; } = new(15, 20);

        [JsonProperty("Diesel Collectable")]
        public RespawnMinMax Diesel { get; set; } = new(30, 45);

        [JsonProperty("Junk piles")]
        public RespawnMinMax JunkPile { get; set; } = new(20, 45);

        [JsonProperty("Resources")]
        public RespawnMinMax Resources { get; set; } = new(20, 45);

        [JsonProperty("Traps/Barricades (Respawn/Re-Arm)")]
        public RespawnMinMax Traps { get; set; } = new(25, 40);

        [JsonProperty("Vehicles")]
        public RespawnMinMax Vehicles { get; set; } = new(45, 60);
    }

    public sealed class RespawnMinMax
    {
        [JsonProperty("Minimum (minutes)")]
        public int Min { get; set; }

        [JsonProperty("Maximum (minutes)")]
        public int Max { get; set; }

        [JsonIgnore]
        public int RandomMinutes
        {
            get
            {
                int min = Min;
                int max = Max;
                if (max < min)
                {
                    int t = min;
                    min = max;
                    max = t;
                }
                return UnityEngine.Random.Range(min, max + 1);
            }
        }

        [JsonIgnore]
        public float RandomSeconds => RandomMinutes * 60f;

        public RespawnMinMax() { }

        public RespawnMinMax(int min, int max)
        {
            Min = min;
            Max = max;
        }
    }
}
