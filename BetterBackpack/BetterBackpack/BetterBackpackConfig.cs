using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace BetterBackpack;

public static class BetterBackpackConfig
{
    public class ConfigData
    {
        private bool _debug = true;
        private bool _existingEnabled = true;
        private bool _retrievalEnabled = true;

        [JsonProperty("Debug")]
        public bool Debug
        {
            get => _debug;
            set => _debug = value;
        }

        [JsonProperty("Debug (log inventory/loot events to help position buttons; set true then check server console)")]
        public bool DebugLegacy
        {
            get => _debug;
            set => _debug = value;
        }

        /// <summary>When false, Existing (auto-stack on loot) does nothing. Use to work around bugs.</summary>
        [JsonProperty("Existing (auto-stack into backpack when looting; false = disabled)")]
        public bool ExistingEnabled { get => _existingEnabled; set => _existingEnabled = value; }

        /// <summary>When false, Retrieval (craft/use from backpack) does nothing. Use to work around bugs.</summary>
        [JsonProperty("Retrieval (craft/build from backpack; false = disabled)")]
        public bool RetrievalEnabled { get => _retrievalEnabled; set => _retrievalEnabled = value; }

        [JsonProperty("CUI Parent (Overlay, Hud, OverlayNonScaled - buttons shown/hidden by mod)")]
        public string CUIParent = "Overlay";

        [JsonProperty("CUI Parent (Overlay, Hud, or OverlayNonScaled; try Overlay if buttons invisible)")]
        private string CUIParentLegacy { set => CUIParent = value ?? CUIParent; }

        [JsonProperty("CUI Parent (Hud, Overlay, or OverlayNonScaled; Hud matches TCUpgrade, visible when loot open)")]
        private string CUIParentLegacy2 { set => CUIParent = value ?? CUIParent; }

        /// <summary>Button bar position. When UsePixelOffsets is false: anchormin/anchormax (0-1). When true: anchor top-left with pixel offsets.</summary>
        [JsonProperty("Buttons AnchorMin (e.g. 0.05 0.78 for normalized, or 0 1 for pixel mode)")]
        public string ButtonsAnchorMin = "0 1";

        [JsonProperty("Buttons AnchorMax (e.g. 0.28 0.88 for normalized, or 0 1 for pixel mode)")]
        public string ButtonsAnchorMax = "0 1";

        /// <summary>When true, use pixel offset positioning (like TCUpgrade). Position just under MissionsHUDToDo.</summary>
        [JsonProperty("Use Pixel Offsets (true = offsetmin/offsetmax in pixels, for fine placement under HUD)")]
        public bool UsePixelOffsets = true;

        [JsonProperty("Buttons OffsetMin (pixels from anchor, e.g. 20 -120 for top-left 20px in, 120px down)")]
        public string ButtonsOffsetMin = "20 -120";

        [JsonProperty("Buttons OffsetMax (e.g. 300 -20)")]
        public string ButtonsOffsetMax = "300 -20";

        /// <summary>When true, show a big bright panel in screen center to verify CUI works at all.</summary>
        [JsonProperty("Debug UI Mode (bright center panel to test if CUI displays; disable after verifying)")]
        public bool DebugUIMode = false;

        /// <summary>When false, no chat messages are sent (reminders and command feedback are disabled).</summary>
        [JsonProperty("Chat Notifications (false = no reminder or /existing / /retrieval feedback in chat)")]
        public bool ChatNotifications = true;

        [JsonProperty("Reminder Enabled (occasional chat message about /existing and /retrieval)")]
        public bool ReminderEnabled = true;

        [JsonProperty("Reminder Interval Minutes (how often to broadcast reminder; 0 = disabled)")]
        public float ReminderIntervalMinutes = 10f;

        [JsonProperty("Reminder Message")]
        public string ReminderMessage = "To turn off backpack existing item grabs and crafting retrieval from inventory use /existing or /retrieval";
    }

    public static ConfigData Config;
    private static string _configPath;

    public static void LoadConfig()
    {
        try
        {
            var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            _configPath = Path.Combine(serverRoot, "HarmonyConfig", "BetterBackpack.json");
            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                Config = JsonConvert.DeserializeObject<ConfigData>(json);
                Debug.Log("[BetterBackpack] Config loaded from " + _configPath);
            }

            if (Config == null)
                Config = new ConfigData();

            if (!File.Exists(_configPath))
            {
                SaveConfig();
                Debug.Log("[BetterBackpack] Config created at " + _configPath);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BetterBackpack] Config load error: {ex.Message}");
            Config ??= new ConfigData();
        }
    }

    public static void SaveConfig()
    {
        try
        {
            if (_configPath == null || Config == null) return;
            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonConvert.SerializeObject(Config, Formatting.Indented);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BetterBackpack] Config save error: {ex.Message}");
        }
    }
}
