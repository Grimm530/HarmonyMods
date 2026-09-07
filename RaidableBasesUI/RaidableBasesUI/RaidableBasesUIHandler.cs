using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Facepunch;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Rust;

namespace RaidableBasesUI;

/// <summary>Builds CUI as JSON and sends via CommunityEntity. Persists per-player offsets to HarmonyData.</summary>
public static class RaidableBasesUIHandler
{
    public const string BuyablePanelName = "RB_UI_Buyable";
    public const string HudParent = "Hud";
    public const string OverlayParent = "Overlay";

    private static string DataPath()
    {
        var root = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
        return Path.Combine(root, "HarmonyData", "RaidableBasesUI");
    }

    private static string OffsetsFilePath()
    {
        var dir = DataPath();
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return Path.Combine(dir, "offsets.json");
    }

    private static Dictionary<ulong, Vector2> _buyableOffsets = new();

    public static void LoadOffsetData()
    {
        try
        {
            var path = OffsetsFilePath();
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            var raw = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            if (raw == null) return;
            _buyableOffsets.Clear();
            foreach (var kv in raw)
            {
                if (ulong.TryParse(kv.Key, out var id) && !string.IsNullOrEmpty(kv.Value))
                {
                    var parts = kv.Value.Split(' ');
                    if (parts.Length >= 4 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x1) &&
                        float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y1) &&
                        float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x2) &&
                        float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var y2))
                        _buyableOffsets[id] = new Vector2((x1 + x2) / 2f, (y1 + y2) / 2f);
                }
            }
        }
        catch (Exception ex) { Debug.LogWarning("[RaidableBasesUI] LoadOffsetData: " + ex.Message); }
    }

    public static void SaveOffsetData()
    {
        try
        {
            var path = OffsetsFilePath();
            var raw = new Dictionary<string, string>();
            var cfg = RaidableBasesUIConfig.Config;
            var defMin = cfg?.BuyableOffsetMin?.ToVector2() ?? new Vector2(-34.159f, 86.718f);
            var defMax = cfg?.BuyableOffsetMax?.ToVector2() ?? new Vector2(179.959f, 254.682f);
            foreach (var kv in _buyableOffsets)
            {
                var min = defMin + (kv.Value - new Vector2((defMin.x + defMax.x) / 2f, (defMin.y + defMax.y) / 2f));
                var max = defMax + (kv.Value - new Vector2((defMin.x + defMax.x) / 2f, (defMin.y + defMax.y) / 2f));
                raw[kv.Key.ToString()] = $"{min.x} {min.y} {max.x} {max.y}";
            }
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonConvert.SerializeObject(raw));
        }
        catch (Exception ex) { Debug.LogWarning("[RaidableBasesUI] SaveOffsetData: " + ex.Message); }
    }

    private static Vector2 GetBuyableOffsets(ulong userid)
    {
        var cfg = RaidableBasesUIConfig.Config;
        var defMin = cfg?.BuyableOffsetMin?.ToVector2() ?? new Vector2(-34.159f, 86.718f);
        var defMax = cfg?.BuyableOffsetMax?.ToVector2() ?? new Vector2(179.959f, 254.682f);
        if (_buyableOffsets.TryGetValue(userid, out var delta))
            return delta;
        return new Vector2((defMin.x + defMax.x) / 2f, (defMin.y + defMax.y) / 2f);
    }

    private static void SetBuyableOffsets(ulong userid, Vector2 delta)
    {
        _buyableOffsets[userid] = delta;
    }

    public static double ParseHexComponent(string hex, int j, int k)
    {
        var s = hex != null ? hex.TrimStart('#') : "";
        if (s.Length < j + k) return 1;
        return int.TryParse(s.Substring(j, k), NumberStyles.AllowHexSpecifier, NumberFormatInfo.CurrentInfo, out var num) ? num : 1;
    }

    public static string GetContrastColor(string hex) =>
        ((ParseHexComponent(hex, 0, 2) * 299) + (ParseHexComponent(hex, 2, 2) * 587) + (ParseHexComponent(hex, 4, 2) * 114)) / 1000 >= 128 ? "0 0 0 1" : "1 1 1 1";

    public static string HexToRgba(string hex, float a) =>
        $"{ParseHexComponent(hex, 0, 2) / 255} {ParseHexComponent(hex, 2, 2) / 255} {ParseHexComponent(hex, 4, 2) / 255} {Mathf.Clamp(a, 0f, 1f)}";

    public static void DestroyAllUi(BasePlayer player)
    {
        if (player?.net?.connection == null) return;
        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.IsDestroyed) return;
        try
        {
            ce.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), BuyablePanelName);
        }
        catch { }
    }

    /// <summary>Show buyable events panel. Called by RaidableBases (Oxide or Harmony) via reflection with pre-computed buttons.</summary>
    public static void ShowBuyableUi(BasePlayer player, List<(string mode, string text, string command)> buttons, bool moveUi,
        string titleText = "Buy Raids", string buttonColorHex = "#497CAF", string textColorHex = "#FFFFFF", bool useContrast = true)
    {
        if (player?.net?.connection == null || buttons == null || buttons.Count == 0) return;
        var cfg = RaidableBasesUIConfig.Config ?? new RaidableBasesUIConfig();
        if (!cfg.BuyableEnabled) return;

        var panelAlpha = cfg.PanelAlpha;
        var panelColor = HexToRgba(cfg.PanelColor, panelAlpha);
        var titleColor = HexToRgba(cfg.TitlePanelColor, panelAlpha);
        var closeColor = HexToRgba(cfg.CloseColor, panelAlpha);
        var fontSize = cfg.FontSize < 1 ? 8 : cfg.FontSize;
        var offsets = GetBuyableOffsets(player.userID);
        var dir = moveUi ? "↑" : "→";
        const float titleBarHeight = 31f;
        const float topPadding = 10f;
        const float bottomPadding = 10f;
        const float spaceBetweenButtons = 5f;
        var buttonHeight = fontSize + 10f;
        var totalButtonsHeight = buttonHeight * buttons.Count;
        var totalSpacing = spaceBetweenButtons * (buttons.Count - 1);
        var requiredButtonsHeight = totalButtonsHeight + totalSpacing;
        var requiredPanelHeight = titleBarHeight + topPadding + bottomPadding + requiredButtonsHeight;
        var panelWidth = 200f;

        var list = new JArray();

        // Main panel (cursor enabled)
        list.Add(new JObject
        {
            ["name"] = BuyablePanelName,
            ["parent"] = HudParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.RawImage", ["color"] = panelColor },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0.5 0.1", ["anchormax"] = "0.5 0.1", ["offsetmin"] = $"{offsets.x - panelWidth / 2f} {offsets.y}", ["offsetmax"] = $"{offsets.x + panelWidth / 2f} {offsets.y + requiredPanelHeight}" },
                new JObject { ["type"] = "NeedsCursor" }
            }
        });

        // Title bar
        list.Add(new JObject
        {
            ["name"] = "BR_TITLE_PANEL",
            ["parent"] = BuyablePanelName,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.RawImage", ["color"] = panelColor },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "0 -" + titleBarHeight, ["offsetmax"] = "0 0" }
            }
        });

        // Move button area
        list.Add(new JObject
        {
            ["name"] = "BR_MOVE_PANEL",
            ["parent"] = "BR_TITLE_PANEL",
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.RawImage", ["color"] = titleColor },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0.5", ["anchormax"] = "0 0.5", ["offsetmin"] = "10 -17", ["offsetmax"] = "150 7" }
            }
        });
        list.Add(new JObject
        {
            ["name"] = "BR_MOVE_BUTTON",
            ["parent"] = "BR_MOVE_PANEL",
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Button", ["command"] = "cui.endtest RB_UI rb_ui_move Buyable", ["color"] = panelColor },
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = titleText + " " + dir, ["fontSize"] = fontSize, ["align"] = "MiddleCenter", ["color"] = "1 1 1 1" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1", ["offsetmin"] = "2 2", ["offsetmax"] = "-2 -2" }
            }
        });

        // Close button area
        list.Add(new JObject
        {
            ["name"] = "BR_CLOSE_PANEL",
            ["parent"] = "BR_TITLE_PANEL",
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.RawImage", ["color"] = titleColor },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "1 0.5", ["anchormax"] = "1 0.5", ["offsetmin"] = "-50 -17", ["offsetmax"] = "-10 7" }
            }
        });
        list.Add(new JObject
        {
            ["name"] = "BR_CLOSE_BUTTON",
            ["parent"] = "BR_CLOSE_PANEL",
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Button", ["command"] = "cui.endtest RB_UI ui_buyraid closeui", ["color"] = panelColor },
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "ⓧ", ["fontSize"] = fontSize, ["align"] = "MiddleCenter", ["color"] = closeColor },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1", ["offsetmin"] = "2 2", ["offsetmax"] = "-2 -2" }
            }
        });

        for (int i = 0; i < buttons.Count; i++)
        {
            var (mode, text, command) = buttons[i];
            var btnColor = HexToRgba(buttonColorHex, 1f);
            var txtColor = useContrast ? GetContrastColor(buttonColorHex) : HexToRgba(textColorHex, 1f);
            var buttonY = (titleBarHeight + topPadding) + i * (buttonHeight + spaceBetweenButtons);
            list.Add(new JObject
            {
                ["name"] = "BR_" + mode.Replace(" ", "_") + "_BUTTON",
                ["parent"] = BuyablePanelName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Button", ["command"] = "cui.endtest RB_UI " + command.Trim(), ["color"] = btnColor },
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = text, ["fontSize"] = fontSize, ["align"] = "MiddleCenter", ["color"] = txtColor },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = $"10 {-buttonY - buttonHeight}", ["offsetmax"] = "-10 {-buttonY}" }
                }
            });
        }

        var json = list.ToString();
        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.IsDestroyed) return;
        try { ce.ClientRPC(RpcTarget.Player("AddUI", player.net.connection), json); }
        catch { }
    }

    /// <summary>Handle move: adjust stored offset and re-show (caller can re-call ShowBuyableUi).</summary>
    public static void MoveBuyableUi(BasePlayer player, string direction)
    {
        if (player == null) return;
        var cfg = RaidableBasesUIConfig.Config;
        var defMin = cfg?.BuyableOffsetMin?.ToVector2() ?? new Vector2(-34.159f, 86.718f);
        var defMax = cfg?.BuyableOffsetMax?.ToVector2() ?? new Vector2(179.959f, 254.682f);
        var center = GetBuyableOffsets(player.userID);
        const float step = 20f;
        switch (direction?.ToLowerInvariant())
        {
            case "left": center.x -= step; break;
            case "right": center.x += step; break;
            case "up": center.y += step; break;
            case "down": center.y -= step; break;
            default: return;
        }
        SetBuyableOffsets(player.userID, center);
        SaveOffsetData();
    }

    /// <summary>Close buyable UI (destroy panel).</summary>
    public static void CloseBuyableUi(BasePlayer player)
    {
        DestroyAllUi(player);
    }
}
