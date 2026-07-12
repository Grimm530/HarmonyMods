using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace InventoryShortcuts;

public class InventoryShortcutsConfig
{
    public static ConfigData Config { get; private set; }

    private static string GetConfigPath()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(root, "HarmonyConfig", "InventoryShortcuts.json");
    }

    public static void Load()
    {
        string path = GetConfigPath();
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                Config = JsonConvert.DeserializeObject<ConfigData>(json) ?? new ConfigData();
            }
            else
            {
                Config = new ConfigData();
                Save();
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[InventoryShortcuts] Failed to load config: {ex.Message}. Using defaults.");
            Config = new ConfigData();
        }
    }

    public static void Save()
    {
        try
        {
            string path = GetConfigPath();
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            string json = JsonConvert.SerializeObject(Config ?? new ConfigData(), Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[InventoryShortcuts] Failed to save config: {ex.Message}");
        }
    }

    public class ConfigData
    {
        [JsonProperty("Show inventory panel buttons (Quests/Skills on Tab inventory)")]
        public bool ShowInventoryPanelButtons = false;

        [JsonProperty("Show main-screen hotbar buttons (Outpost/Players/Kits/Shop/Skins/Vehicles)")]
        public bool ShowHotbarButtons = true;

        [JsonProperty("CUI parent (Inventory, Hud, or Overlay)")]
        public string CuiParent = "Inventory";

        [JsonProperty("Button background color (R G B A, 0-1)")]
        public string ButtonColor = "0.969 0.922 0.882 0.15";

        [JsonProperty("Button background material (e.g. assets/content/ui/uibackgroundblur-ingamemenu.mat, empty = none)")]
        public string ButtonBackgroundMaterial = "assets/content/ui/uibackgroundblur-ingamemenu.mat";

        [JsonProperty("Button background scale (0.5-1.5, 1 = full size)")]
        public float ButtonBackgroundScale = 0.85f;

        [JsonProperty("Text and icon color (R G B A, 0-1)")]
        public string TextColor = "0.875 0.827 0.780 1";

        [JsonProperty("Button row anchor top (0-1, top of screen)")]
        public float AnchorTop = 1.0f;

        [JsonProperty("Button row bottom (0-1; QUESTS/SKILLS bottom edge; 0 = use Button height from top)")]
        public float ButtonRowBottom = 0.933f;

        [JsonProperty("Button height (0-1; used only when Button row bottom = 0)")]
        public float ButtonHeight = 0.069f;

        [JsonProperty("Button width left/QUESTS (0-1; 0.154 to 0.3113 = 0.1573)")]
        public float ButtonWidth = 0.1573f;

        [JsonProperty("Button width right/SKILLS (0-1; 0 = same as QUESTS)")]
        public float RightButtonWidth = 0f;

        [JsonProperty("Left button center X (0-1; QUESTS 0.154-0.3113 → 0.23265)")]
        public float LeftButtonCenter = 0.23265f;

        [JsonProperty("Right button center X (0-1; SKILLS 0.6522-0.829 → 0.7406)")]
        public float RightButtonCenter = 0.7406f;

        [JsonProperty("Quest button shift X (normalized, negative=left positive=right; 0.005 = slightly right)")]
        public float QuestButtonShiftX = 0.005f;

        [JsonProperty("Skill button shift X (normalized, negative=left positive=right; 0.01 = slightly right)")]
        public float SkillButtonShiftX = 0.01f;

        [JsonProperty("Extra button row height (0-1, fraction of screen height added to top row)")]
        public float ExtraButtonHeight = 0f;

        [JsonProperty("Quest icon item shortname (e.g. note, paper, map; empty = text only)")]
        public string QuestIconShortname = "map";

        [JsonProperty("Quest icon left (0-1, fraction of button width)")]
        public float QuestIconLeft = 0.04f;

        [JsonProperty("Quest icon right (0-1, fraction of button width)")]
        public float QuestIconRight = 0.20f;

        [JsonProperty("Skill icon item shortname (e.g. xpboost; empty = text only)")]
        public string SkillIconShortname = "";

        [JsonProperty("Skill icon image URL (https URL only; client loads externally — no FileStorage/png RPC)")]
        public string SkillIconImageUrl = "";

        [JsonProperty("Hotbar button row height (0-1, tiny buttons)")]
        public float HotbarButtonHeight = 0.017f;

        [JsonProperty("Debug logging")]
        public bool Debug = false;
    }
}
