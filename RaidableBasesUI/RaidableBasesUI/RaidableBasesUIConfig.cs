using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace RaidableBasesUI;

/// <summary>UI-only config for RaidableBasesUI. Stored in HarmonyConfig/RaidableBasesUI.json.</summary>
public class RaidableBasesUIConfig
{
    public static RaidableBasesUIConfig Config { get; private set; }

    public bool BuyableEnabled { get; set; } = true;
    public float PanelAlpha { get; set; } = 0.98f;
    public string PanelColor { get; set; } = "#252121";
    public string TitlePanelColor { get; set; } = "#000000";
    public string CloseColor { get; set; } = "#497CAF";
    public int FontSize { get; set; } = 14;
    public bool Contrast { get; set; } = true;
    public Vector2Serial BuyableOffsetMin { get; set; } = new(-34.159f, 86.718f);
    public Vector2Serial BuyableOffsetMax { get; set; } = new(179.959f, 254.682f);

    [Serializable]
    public class Vector2Serial
    {
        public float x, y;
        public Vector2Serial() { }
        public Vector2Serial(float x, float y) { this.x = x; this.y = y; }
        public Vector2 ToVector2() => new(x, y);
        public static Vector2Serial FromVector2(Vector2 v) => new(v.x, v.y);
    }

    public static void Load()
    {
        var path = GetConfigPath();
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                Config = JsonConvert.DeserializeObject<RaidableBasesUIConfig>(json);
                if (Config != null) return;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBasesUI] Config load failed: " + ex.Message);
            }
        }

        Config = new RaidableBasesUIConfig();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonConvert.SerializeObject(Config, Formatting.Indented));
            Debug.Log("[RaidableBasesUI] Default config created at " + path);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[RaidableBasesUI] Config save failed: " + ex.Message);
        }
    }

    private static string GetConfigPath()
    {
        var root = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
        return Path.Combine(root, "HarmonyConfig", "RaidableBasesUI.json");
    }
}
