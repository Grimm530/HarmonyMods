using System;
using System.Globalization;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace InventoryShortcuts;

public class InventoryShortcutsMod : IHarmonyModHooks
{
    public static InventoryShortcutsMod Instance { get; private set; }
    private const string PanelName = "InventoryShortcuts.buttons";
    private const string PanelNameHotbar = "InventoryShortcuts.hotbar";

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;
        InventoryShortcutsConfig.Load();
        foreach (var player in BasePlayer.activePlayerList)
        {
            if (player != null && !player.IsDestroyed && player.IsConnected)
                player.StartCoroutine(SendHotbarWithRetry(player));
        }
        UnityEngine.Debug.Log("[InventoryShortcuts] Mod loaded. Hotbar shortcuts on Hud; inventory Quests/Skills optional via config.");
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        foreach (var player in BasePlayer.activePlayerList)
            DestroyUi(player);
        Instance = null;
    }

    /// <summary>Send hotbar UI (main screen). Optionally send inventory-panel row when loot opens.</summary>
    public void ShowButtons(BasePlayer player, bool includeInventoryPanel = false)
    {
        TryShowButtons(player, includeInventoryPanel);
    }

    /// <summary>Retry hotbar send until CommunityEntity/client is ready (spawn, reconnect, or harmony.reload).</summary>
    public static System.Collections.IEnumerator SendHotbarWithRetry(BasePlayer player)
    {
        float[] retryDelays = { 0.2f, 1f, 3f, 8f };
        foreach (var delay in retryDelays)
        {
            yield return new WaitForSeconds(delay);
            if (player == null || player.IsDestroyed || !player.IsConnected) yield break;
            if (Instance == null) yield break;
            var cfg = InventoryShortcutsConfig.Config;
            if (cfg?.ShowHotbarButtons == false) yield break;
            if (SendHotbarUi(player, cfg)) yield break;
        }
    }

    private bool TryShowButtons(BasePlayer player, bool includeInventoryPanel = false)
    {
        if (player?.net?.connection == null) return false;
        if (player.IsReceivingSnapshot) return false;

        var cfg = InventoryShortcutsConfig.Config;
        bool sentHotbar = cfg?.ShowHotbarButtons != false && SendHotbarUi(player, cfg);
        bool sentInventory = includeInventoryPanel
            && cfg?.ShowInventoryPanelButtons == true
            && SendInventoryPanelUi(player, cfg);
        return sentHotbar || sentInventory;
    }

    private static bool SendHotbarUi(BasePlayer player, InventoryShortcutsConfig.ConfigData cfg)
    {
        string btnColor = cfg?.ButtonColor ?? "0.42 0.40 0.37 0.85";
        string textColor = cfg?.TextColor ?? "0.875 0.827 0.780 1";
        float hotbarHeight = Mathf.Clamp(cfg?.HotbarButtonHeight ?? 0.018f, 0.008f, 0.08f);
        float hotbarYMin = 0.004f;
        float hotbarYMax = hotbarYMin + hotbarHeight;

        var elements = new List<JObject>();
        var hotbarContainer = Container(PanelNameHotbar, "Hud",
            "0 " + hotbarYMin.ToString("F3", CultureInfo.InvariantCulture),
            "1 " + hotbarYMax.ToString("F3", CultureInfo.InvariantCulture), "0 0", "0 0");
        hotbarContainer["destroyUi"] = PanelNameHotbar;
        elements.Add(hotbarContainer);

        float hbBtnW = 0.047f;
        float gap = 0.003f;
        float x0 = 0.344f;
        var hotbarButtons = new[]
        {
            ("invshortcut_outpost", "OUTPOST", "/outpost"),
            ("invshortcut_players", "PLAYERS", "/tp"),
            ("invshortcut_kits", "KITS", "/kits"),
            ("invshortcut_shop", "SHOP", "/s"),
            ("invshortcut_skins", "SKINS", "/skinshop"),
            ("invshortcut_vehicles", "VEHICLES", "/vehicles")
        };
        for (int i = 0; i < hotbarButtons.Length; i++)
        {
            float xMin = x0 + i * (hbBtnW + gap);
            float xMax = xMin + hbBtnW;
            var (btnName, btnText, cmd) = hotbarButtons[i];
            elements.AddRange(Button(btnName, PanelNameHotbar, btnColor, textColor, btnText, 8,
                xMin.ToString("F3", CultureInfo.InvariantCulture) + " 0",
                xMax.ToString("F3", CultureInfo.InvariantCulture) + " 1",
                "0 0", "0 0", "chat.say " + cmd, 0, false, null));
        }

        return SendUi(player, elements);
    }

    private static bool SendInventoryPanelUi(BasePlayer player, InventoryShortcutsConfig.ConfigData cfg)
    {
        string parent = !string.IsNullOrWhiteSpace(cfg?.CuiParent) ? cfg.CuiParent.Trim() : "Inventory";
        parent = parent.ToLowerInvariant() switch
        {
            "inventory" => "Inventory",
            "hud" => "Hud",
            "overlay" => "Overlay",
            _ => "Inventory"
        };

        string btnColor = cfg?.ButtonColor ?? "0.42 0.40 0.37 0.85";
        string textColor = cfg?.TextColor ?? "0.875 0.827 0.780 1";
        float top = Mathf.Clamp01(cfg?.AnchorTop ?? 1.0f);
        float bottom;
        float rowBottom = cfg?.ButtonRowBottom ?? 0f;
        if (rowBottom > 0f)
            bottom = Mathf.Clamp01(rowBottom);
        else
        {
            float btnHeight = Mathf.Clamp(cfg?.ButtonHeight ?? 0.069f, 0.02f, 0.20f);
            float extraHeightNorm = Mathf.Clamp01(cfg?.ExtraButtonHeight ?? 0f);
            bottom = Mathf.Clamp01(top - btnHeight - extraHeightNorm);
        }

        float leftWidth = Mathf.Clamp(cfg?.ButtonWidth ?? 0.1573f, 0.05f, 0.35f);
        float rightWidth = cfg?.RightButtonWidth > 0f ? Mathf.Clamp(cfg.RightButtonWidth, 0.05f, 0.35f) : leftWidth;
        float leftHalf = leftWidth * 0.5f;
        float rightHalf = rightWidth * 0.5f;
        float leftCenter = Mathf.Clamp01(cfg?.LeftButtonCenter ?? 0.23265f);
        float rightCenter = Mathf.Clamp01(cfg?.RightButtonCenter ?? 0.7406f);
        float questShiftX = Mathf.Clamp(cfg?.QuestButtonShiftX ?? 0f, -0.1f, 0.1f);
        float skillsShiftX = Mathf.Clamp(cfg?.SkillButtonShiftX ?? 0f, -0.1f, 0.1f);
        float leftMin = Mathf.Clamp01(leftCenter - leftHalf + questShiftX);
        float leftMax = Mathf.Clamp01(leftCenter + leftHalf + questShiftX);
        float rightMin = Mathf.Clamp01(rightCenter - rightHalf + skillsShiftX);
        float rightMax = Mathf.Clamp01(rightCenter + rightHalf + skillsShiftX);
        const string zeroOffset = "0 0";

        var elements = new List<JObject>();
        var container = Container(PanelName, parent,
            "0.000 " + bottom.ToString("F3", CultureInfo.InvariantCulture),
            "1.000 " + top.ToString("F3", CultureInfo.InvariantCulture),
            "0 0", "0 0");
        container["destroyUi"] = PanelName;
        elements.Add(container);

        int questIcon = GetItemId(cfg?.QuestIconShortname ?? "");
        float questIconL = Mathf.Clamp01(cfg?.QuestIconLeft ?? 0.04f);
        float questIconR = Mathf.Clamp01(cfg?.QuestIconRight ?? 0.20f);
        if (questIconR <= questIconL) questIconR = questIconL + 0.08f;
        elements.AddRange(Button("invshortcut_quests", PanelName, btnColor, textColor, "QUESTS", 24,
            leftMin.ToString("F3", CultureInfo.InvariantCulture) + " 0.000",
            leftMax.ToString("F3", CultureInfo.InvariantCulture) + " 1.000",
            zeroOffset, zeroOffset,
            "cui.endtest INVSHORTCUTS QUEST", questIcon, true, null, questIconL, questIconR));

        int skillIcon = GetItemId(cfg?.SkillIconShortname ?? "");
        string skillImageUrl = (cfg?.SkillIconImageUrl ?? "").Trim();
        if (string.IsNullOrEmpty(skillImageUrl)) skillImageUrl = null;
        elements.AddRange(Button("invshortcut_skills", PanelName, btnColor, textColor, "SKILLS", 24,
            rightMin.ToString("F3", CultureInfo.InvariantCulture) + " 0.000",
            rightMax.ToString("F3", CultureInfo.InvariantCulture) + " 1.000",
            zeroOffset, zeroOffset,
            "cui.endtest INVSHORTCUTS SKILLS", skillIcon, false, skillImageUrl));

        return SendUi(player, elements);
    }

    private static bool SendUi(BasePlayer player, List<JObject> elements)
    {
        if (elements == null || elements.Count == 0) return false;
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(elements);
        return CuiHelper.AddUi(player, json);
    }

    public void DestroyUi(BasePlayer player)
    {
        if (player?.net?.connection == null) return;
        CuiHelper.DestroyUi(player, PanelName);
        CuiHelper.DestroyUi(player, PanelNameHotbar);
        DestroyGridOverlay(player);
    }

    public void ShowGridOverlay(BasePlayer player)
    {
        if (player?.net?.connection == null) return;
        var elements = UIGridOverlay.BuildGridElements();
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(elements);
        CuiHelper.AddUi(player, json);
    }

    public void DestroyGridOverlay(BasePlayer player)
    {
        if (player?.net?.connection == null) return;
        CuiHelper.DestroyUi(player, UIGridOverlay.PanelName);
    }

    private static JObject Container(string name, string parent, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
    {
        return new JObject
        {
            ["name"] = name,
            ["parent"] = parent,
            ["components"] = new JArray
            {
                new JObject
                {
                    ["type"] = "RectTransform",
                    ["anchormin"] = anchorMin,
                    ["anchormax"] = anchorMax,
                    ["offsetmin"] = offsetMin,
                    ["offsetmax"] = offsetMax
                }
            }
        };
    }

    private static List<JObject> Button(string name, string parent, string color, string textColor, string text, int fontSize,
        string anchorMin, string anchorMax, string offsetMin, string offsetMax, string command, int iconItemId = 0, bool iconOnLeft = false,
        string customRightIconUrl = null, float? leftIconMin = null, float? leftIconMax = null)
    {
        var list = new List<JObject>
        {
            new JObject
            {
                ["name"] = name,
                ["parent"] = parent,
                ["components"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "UnityEngine.UI.Button",
                        ["command"] = command,
                        ["color"] = color,
                        ["sprite"] = "assets/content/ui/ui.background.tile.psd",
                        ["imagetype"] = "Simple",
                        ["material"] = "assets/content/ui/namefontmaterial.mat",
                        ["normalColor"] = "1 1 1 1",
                        ["highlightedColor"] = "1 1 1 1",
                        ["pressedColor"] = "1 1 1 1"
                    },
                    new JObject
                    {
                        ["type"] = "RectTransform",
                        ["anchormin"] = anchorMin,
                        ["anchormax"] = anchorMax,
                        ["offsetmin"] = offsetMin,
                        ["offsetmax"] = offsetMax
                    }
                }
            }
        };
        list.Add(new JObject
        {
            ["name"] = name + "_tex",
            ["parent"] = name,
            ["components"] = new JArray
            {
                new JObject
                {
                    ["type"] = "UnityEngine.UI.Image",
                    ["color"] = "0.9686 0.9216 0.8824 0.04",
                    ["sprite"] = "assets/content/ui/ui.background.tile.psd",
                    ["material"] = "assets/icons/greyout.mat",
                    ["imagetype"] = "Simple"
                },
                new JObject
                {
                    ["type"] = "RectTransform",
                    ["anchormin"] = "0 0",
                    ["anchormax"] = "1 1"
                }
            }
        });
        bool hasRightIcon = !string.IsNullOrEmpty(customRightIconUrl) || (!iconOnLeft && iconItemId != 0);
        float lblStart = iconOnLeft && iconItemId != 0 ? 0.15f : 0.05f;
        string labelAnchorMin = lblStart.ToString("F2", CultureInfo.InvariantCulture) + " 0";
        string labelAnchorMax = hasRightIcon ? "0.58 1" : "0.95 1";
        list.Add(new JObject
        {
            ["name"] = name + "_label",
            ["parent"] = name,
            ["components"] = new JArray
            {
                new JObject
                {
                    ["type"] = "UnityEngine.UI.Text",
                    ["text"] = text,
                    ["fontSize"] = fontSize,
                    ["font"] = "RobotoCondensed-Bold.ttf",
                    ["color"] = textColor,
                    ["align"] = "MiddleCenter"
                },
                new JObject
                {
                    ["type"] = "RectTransform",
                    ["anchormin"] = labelAnchorMin,
                    ["anchormax"] = labelAnchorMax
                }
            }
        });
        if (iconItemId != 0)
        {
            float iMin = iconOnLeft ? (leftIconMin ?? 0.02f) : 0.75f;
            float iMax = iconOnLeft ? (leftIconMax ?? 0.12f) : 0.92f;
            string iconMin = iMin.ToString("F2", CultureInfo.InvariantCulture) + " 0.15";
            string iconMax = iMax.ToString("F2", CultureInfo.InvariantCulture) + " 0.85";
            list.Add(new JObject
            {
                ["name"] = name + "_icon",
                ["parent"] = name,
                ["components"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "UnityEngine.UI.Image",
                        ["itemid"] = iconItemId,
                        ["color"] = textColor
                    },
                    new JObject
                    {
                        ["type"] = "RectTransform",
                        ["anchormin"] = iconMin,
                        ["anchormax"] = iconMax
                    }
                }
            });
        }
        if (!string.IsNullOrEmpty(customRightIconUrl))
        {
            list.Add(new JObject
            {
                ["name"] = name + "_customicon",
                ["parent"] = name,
                ["components"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "UnityEngine.UI.RawImage",
                        ["url"] = customRightIconUrl,
                        ["color"] = textColor
                    },
                    new JObject
                    {
                        ["type"] = "RectTransform",
                        ["anchormin"] = "0.58 0.05",
                        ["anchormax"] = "0.98 0.95"
                    }
                }
            });
        }
        return list;
    }

    private static int GetItemId(string shortname)
    {
        if (string.IsNullOrWhiteSpace(shortname)) return 0;
        var def = ItemManager.FindItemDefinition(shortname);
        return def?.itemid ?? 0;
    }
}
