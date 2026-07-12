using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace RaidRustPlus;

public sealed class RaidRustPlusConfig
{
    [JsonProperty("Enabled")]
    public bool Enabled = true;

    [JsonProperty("Server Name")]
    public string ServerName = "Rust Server";

    [JsonProperty("Notification Cooldown Seconds")]
    public float CooldownSeconds = 600f;

    [JsonProperty("Minimum Building Grade (0=Twig, 1=Wood, 2=Stone, 3=Metal, 4=TopTier)")]
    public int MinimumBuildingGrade = 1;

    [JsonProperty("Include Extra Deployables")]
    public bool IncludeExtraDeployables;

    [JsonProperty("Extra Deployable Prefab Shortnames")]
    public List<string> ExtraShortnames = new List<string>
    {
        "wall.external.high",
        "wall.external.high.stone",
        "gates.external.high.wood",
        "gates.external.high.stone",
        "wall.window.bars.metal",
        "wall.window.bars.toptier",
        "wall.window.glass.reinforced",
        "wall.window.bars.wood"
    };

    [JsonProperty("Rust+ Title Template")]
    public string TitleTemplate = "Attention! Player {name} destroyed {destroy} in {quad}";

    [JsonProperty("Rust+ Body Template")]
    public string BodyTemplate = "{servername}";

    public static RaidRustPlusConfig Load(out string path)
    {
        path = GetConfigPath();
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (!File.Exists(path))
        {
            RaidRustPlusConfig defaults = new RaidRustPlusConfig();
            File.WriteAllText(path, JsonConvert.SerializeObject(defaults, Formatting.Indented));
            Debug.Log("[RaidRustPlus] Created default config at " + path);
            return defaults;
        }

        try
        {
            RaidRustPlusConfig loaded = JsonConvert.DeserializeObject<RaidRustPlusConfig>(File.ReadAllText(path));
            if (loaded == null)
            {
                loaded = new RaidRustPlusConfig();
            }
            return loaded;
        }
        catch (Exception ex)
        {
            Debug.LogError("[RaidRustPlus] Failed to load config, using defaults. " + ex);
            return new RaidRustPlusConfig();
        }
    }

    public static string GetConfigPath()
    {
        string serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(serverRoot, "HarmonyConfig", "RaidRustPlus.json");
    }
}
