using System;
using System.Collections.Generic;
using System.Linq;
using Facepunch;
using Network;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Leaderboard;

public static class LeaderboardUI
{
    public const string PanelName = "Leaderboard_Main";
    public const string ServerPanelRoot = "UI.Server.Panel.Content.Plugin";
    private const string OverlayParent = "Overlay";
    private const string ServerPanelParent = "UI.Server.Panel.Content";
    private const string BgSprite = "assets/content/ui/UI.Background.TileTex.psd";
    private const string BlurMat = "assets/content/ui/uibackgroundblur.mat";
    private const string BlurBgMat = "assets/content/ui/uibackgroundblur-ingamemenu.mat";
    private const string HeaderSprite = "assets/content/ui/UI.Background.Transparent.LinearLTR.tga";
    private const string CloseIcon = "assets/icons/close.png";
    private const string TileSprite = "assets/content/ui/ui.background.tile.psd";
    /// <summary>Card background color (darker alpha so cards stand out).</summary>
    private const string CardBgColor = "0.22 0.224 0.247 0.45";
    private const string ScrollContentParent = "Leaderboard_Scroll___Content";

    public static void Show(BasePlayer player)
    {
        if (player?.net?.connection == null) return;
        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.IsDestroyed) return;

        var category = LeaderboardMod.Instance?.GetLeaderboardCategory(player.userID) ?? 0;
        var subTab = category == 0
            ? (LeaderboardMod.Instance?.GetLeaderboardProfileTab(player.userID) ?? 0)
            : (LeaderboardMod.Instance?.GetLeaderboardTop10Tab(player.userID) ?? 0);
        var elements = BuildFullPanel(player, category, subTab, inServerPanel: false);
        var json = elements.ToString();
        try { ce.ClientRPC(RpcTarget.Player("AddUI", player.net.connection), json); }
        catch (System.Exception) { }
    }

    /// <summary>
    /// Build CUI JSON for ServerPanel embedding (parent UI.Server.Panel.Content).
    /// Returns bracket-stripped element list for ShowContentUISerialized merge.
    /// </summary>
    public static string BuildForServerPanel(BasePlayer player)
    {
        if (player == null) return null;
        var category = LeaderboardMod.Instance?.GetLeaderboardCategory(player.userID) ?? 0;
        var subTab = category == 0
            ? (LeaderboardMod.Instance?.GetLeaderboardProfileTab(player.userID) ?? 0)
            : (LeaderboardMod.Instance?.GetLeaderboardTop10Tab(player.userID) ?? 0);
        return StripArrayBrackets(BuildFullPanel(player, category, subTab, inServerPanel: true).ToString());
    }

    /// <summary>Rebuild and AddUI while already embedded under ServerPanel content.</summary>
    public static void RefreshInServerPanel(BasePlayer player)
    {
        if (player?.net?.connection == null) return;
        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.IsDestroyed) return;
        var body = BuildForServerPanel(player);
        if (string.IsNullOrWhiteSpace(body)) return;
        try { ce.ClientRPC(RpcTarget.Player("AddUI", player.net.connection), "[" + body + "]"); }
        catch (System.Exception) { }
    }

    public static void Destroy(BasePlayer player)
    {
        if (player?.net?.connection == null) return;
        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.IsDestroyed) return;
        try { ce.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), PanelName); }
        catch (System.Exception) { }
    }

    public static void DestroyServerPanel(BasePlayer player)
    {
        if (player?.net?.connection == null) return;
        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.IsDestroyed) return;
        try { ce.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), ServerPanelRoot); }
        catch (System.Exception) { }
    }

    private static string StripArrayBrackets(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        json = json.Trim();
        if (json.Length >= 2 && json[0] == '[' && json[json.Length - 1] == ']')
            json = json.Substring(1, json.Length - 2);
        return string.IsNullOrWhiteSpace(json) ? null : json;
    }

    private static JArray BuildFullPanel(BasePlayer player, int categoryIndex, int subTab, bool inServerPanel = false)
    {
        var list = new JArray();

        if (inServerPanel)
        {
            // --- Root fills ServerPanel content area ---
            list.Add(new JObject
            {
                ["name"] = ServerPanelRoot,
                ["parent"] = ServerPanelParent,
                ["destroyUi"] = ServerPanelRoot,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Image", ["color"] = "0 0 0 0" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" },
                    new JObject { ["type"] = "NeedsCursor" },
                    new JObject { ["type"] = "NeedsKeyboard" }
                }
            });

            // --- Main panel fills content (no separate Overlay fullscreen) ---
            list.Add(new JObject
            {
                ["name"] = PanelName + "_Main",
                ["parent"] = ServerPanelRoot,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Image", ["color"] = "0.098 0.098 0.098 0.5", ["material"] = BlurMat },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1", ["offsetmin"] = "0 0", ["offsetmax"] = "0 0" }
                }
            });
        }
        else
        {
            // --- Root: fullscreen dimmed background (destroyUi clears this and all children) ---
            list.Add(new JObject
            {
                ["name"] = PanelName,
                ["parent"] = OverlayParent,
                ["destroyUi"] = PanelName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Image", ["sprite"] = BgSprite, ["color"] = "0.098 0.098 0.098 0.9", ["material"] = BlurBgMat },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" },
                    new JObject { ["type"] = "NeedsCursor" },
                    new JObject { ["type"] = "NeedsKeyboard" }
                }
            });

            // --- Main panel: centered 600x300, blur ---
            list.Add(new JObject
            {
                ["name"] = PanelName + "_Main",
                ["parent"] = PanelName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Image", ["color"] = "0.098 0.098 0.098 0.5", ["material"] = BlurMat },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0.5 0.5", ["anchormax"] = "0.5 0.5", ["offsetmin"] = "-600 -300", ["offsetmax"] = "600 300" }
                }
            });
        }

        var main = PanelName + "_Main";

        // --- Header bar (50px top); fullscreen leaves room for close button ---
        list.Add(new JObject
        {
            ["name"] = PanelName + "_Header",
            ["parent"] = main,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Image", ["sprite"] = HeaderSprite, ["color"] = "0.286 0.286 0.286 1" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "0 -50", ["offsetmax"] = inServerPanel ? "0 0" : "-50 0" }
            }
        });

        var header = PanelName + "_Header";

        list.Add(new JObject
        {
            ["name"] = PanelName + "_HeaderTitle",
            ["parent"] = header,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "Leaderboard", ["fontSize"] = 18, ["align"] = "MiddleLeft", ["color"] = "0.886 0.859 0.827 1" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1", ["offsetmin"] = "20 0", ["offsetmax"] = "0 0" }
            }
        });

        if (!inServerPanel)
        {
            list.Add(new JObject
            {
                ["name"] = PanelName + "_Close",
                ["parent"] = header,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Button", ["command"] = "cui.endtest LEADERBOARD close", ["color"] = "0.894 0.251 0.157 1", ["sprite"] = TileSprite },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "1 1", ["anchormax"] = "1 1", ["offsetmin"] = "0 -50", ["offsetmax"] = "50 0" }
                }
            });
            list.Add(new JObject
            {
                ["name"] = PanelName + "_CloseIcon",
                ["parent"] = PanelName + "_Close",
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Image", ["sprite"] = CloseIcon, ["color"] = "1 1 1 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0.5 0.5", ["anchormax"] = "0.5 0.5", ["offsetmin"] = "-9 -9", ["offsetmax"] = "9 9" }
                }
            });
        }

        // --- Category bar (under header): 0.5 1, offset -580 -105 to 580 -70 ---
        const float catW = 168f;
        const float gap = 4f;
        float catY0 = -105f;
        float catY1 = -70f;
        string[] catLabels = { "MY STATISTICS", "TOP 10 PLAYERS", "SEARCH" };
        for (int i = 0; i < 3; i++)
        {
            float x0 = -580f + i * (catW + gap);
            float x1 = x0 + catW;
            bool selected = (i == categoryIndex);
            string color = selected ? "0.843 0.286 0.2 1" : "0.184 0.184 0.184 1";
            string btnName = PanelName + "_Cat" + i;
            list.Add(new JObject
            {
                ["name"] = btnName,
                ["parent"] = main,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Button", ["command"] = "cui.endtest LEADERBOARD page " + i, ["color"] = color, ["sprite"] = TileSprite },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0.5 1", ["anchormax"] = "0.5 1", ["offsetmin"] = x0 + " " + catY0, ["offsetmax"] = x1 + " " + catY1 }
                }
            });
            list.Add(new JObject
            {
                ["name"] = btnName + "_Text",
                ["parent"] = btnName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = catLabels[i], ["fontSize"] = 17, ["align"] = "MiddleCenter", ["color"] = "0.886 0.859 0.827 0.9" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
                }
            });
        }

        // When My Statistics is selected, add sub-tab row: General, Resources, Building, Hitrate
        // When Top 10 Players is selected, add sub-tab row: Top Killers, Top Raiders, Top Farmers, Total Play Time
        float scrollTop = -115f;
        if (categoryIndex == 0)
        {
            const float tabW = 130f;
            const float tabGap = 5f;
            float subY0 = -147f;
            float subY1 = -112f;
            float subX0 = -580f;
            string[] subLabels = { "GENERAL", "RESOURCES", "BUILDING", "HITRATE" };
            for (int i = 0; i < 4; i++)
            {
                float x0 = subX0 + i * (tabW + tabGap);
                float x1 = x0 + tabW;
                bool selected = (i == subTab);
                string color = selected ? "0.843 0.286 0.2 1" : "0.184 0.184 0.184 1";
                string btnName = PanelName + "_SubTab" + i;
                list.Add(new JObject
                {
                    ["name"] = btnName,
                    ["parent"] = main,
                    ["components"] = new JArray
                    {
                        new JObject { ["type"] = "UnityEngine.UI.Button", ["command"] = "cui.endtest LEADERBOARD tab " + i, ["color"] = color, ["sprite"] = TileSprite },
                        new JObject { ["type"] = "RectTransform", ["anchormin"] = "0.5 1", ["anchormax"] = "0.5 1", ["offsetmin"] = x0 + " " + subY0, ["offsetmax"] = x1 + " " + subY1 }
                    }
                });
                list.Add(new JObject
                {
                    ["name"] = btnName + "_Text",
                    ["parent"] = btnName,
                    ["components"] = new JArray
                    {
                        new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = subLabels[i], ["fontSize"] = 16, ["align"] = "MiddleCenter", ["color"] = "0.886 0.859 0.827 0.9" },
                        new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
                    }
                });
            }
            scrollTop = -155f;
        }
        else if (categoryIndex == 1)
        {
            const float tabW = 150f;
            const float tabGap = 5f;
            float subY0 = -147f;
            float subY1 = -112f;
            float subX0 = -580f;
            string[] subLabels = { "TOP KILLERS", "TOP RAIDERS", "TOP FARMERS", "TOTAL PLAY TIME" };
            for (int i = 0; i < 4; i++)
            {
                float x0 = subX0 + i * (tabW + tabGap);
                float x1 = x0 + tabW;
                bool selected = (i == subTab);
                string color = selected ? "0.843 0.286 0.2 1" : "0.184 0.184 0.184 1";
                string btnName = PanelName + "_Top10SubTab" + i;
                list.Add(new JObject
                {
                    ["name"] = btnName,
                    ["parent"] = main,
                    ["components"] = new JArray
                    {
                        new JObject { ["type"] = "UnityEngine.UI.Button", ["command"] = "cui.endtest LEADERBOARD tab " + i, ["color"] = color, ["sprite"] = TileSprite },
                        new JObject { ["type"] = "RectTransform", ["anchormin"] = "0.5 1", ["anchormax"] = "0.5 1", ["offsetmin"] = x0 + " " + subY0, ["offsetmax"] = x1 + " " + subY1 }
                    }
                });
                list.Add(new JObject
                {
                    ["name"] = btnName + "_Text",
                    ["parent"] = btnName,
                    ["components"] = new JArray
                    {
                        new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = subLabels[i], ["fontSize"] = 14, ["align"] = "MiddleCenter", ["color"] = "0.886 0.859 0.827 0.9" },
                        new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
                    }
                });
            }
            scrollTop = -155f;
            if (subTab == 0 || subTab == 1 || subTab == 2 || subTab == 3)
                scrollTop = -155f - 62f; // leave room for fixed header (Top Killers, Top Raiders, Top Farmers, Total Play Time)
        }

        // --- Content: ScrollView below header (+ category + sub-tabs when My Statistics or Top 10) ---
        int scrollContentHeight = categoryIndex == 0
            ? (subTab == 1 || subTab == 2 ? 3600 : (subTab == 3 ? 480 : 800))
            : (categoryIndex == 1 ? 600 : (categoryIndex == 2 ? 3600 : 800));
        list.Add(new JObject
        {
            ["name"] = "Leaderboard_Scroll",
            ["parent"] = main,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Image", ["color"] = "0 0 0 0" },
                new JObject
                {
                    ["type"] = "UnityEngine.UI.ScrollView",
                    ["contentTransform"] = new JObject
                    {
                        ["anchormin"] = "0 1",
                        ["anchormax"] = "1 1",
                        ["offsetmin"] = "0 -" + scrollContentHeight,
                        ["offsetmax"] = "0 0"
                    },
                    ["vertical"] = true,
                    ["horizontal"] = false,
                    ["movementType"] = "Clamped",
                    ["scrollSensitivity"] = 24f,
                    ["verticalScrollbar"] = new JObject { ["size"] = 16, ["autoHide"] = true }
                },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1", ["offsetmin"] = "20 0", ["offsetmax"] = "-20 " + scrollTop },
                new JObject { ["type"] = "NeedsCursor" }
            }
        });

        // --- Fixed row titles above scroller (Top Killers or Top Raiders): parent = main ---
        if (categoryIndex == 1 && subTab == 0)
            AddTopKillersFixedHeader(list, scrollTop, main);
        else if (categoryIndex == 1 && subTab == 1)
            AddTopRaidersFixedHeader(list, scrollTop, main);
        else if (categoryIndex == 1 && subTab == 2)
            AddTopFarmersFixedHeader(list, scrollTop, main);
        else if (categoryIndex == 1 && subTab == 3)
            AddTopPlayTimeFixedHeader(list, scrollTop, main);

        // --- Scroll content: parent is Leaderboard_Scroll___Content (created by ScrollView) ---
        AddContentByCategory(list, player, categoryIndex, subTab);

        return list;
    }

    private static void AddContentByCategory(JArray list, BasePlayer player, int categoryIndex, int subTab)
    {
        if (categoryIndex == 0)
        {
            var mod = LeaderboardMod.Instance;
            ulong profileTarget = mod?.GetViewedProfileTarget(player.userID) ?? player.userID;
            AddMyStatisticsContent(list, player, subTab, profileTarget);
        }
        else if (categoryIndex == 1)
            AddTop10Content(list, subTab, player);
        else if (categoryIndex == 2)
            AddSearchContent(list);
        else
            AddSearchPlaceholderContent(list);
    }

    private static void AddMyStatisticsContent(JArray list, BasePlayer player, int subTab, ulong profileTargetId)
    {
        if (subTab == 0)
        {
            AddGeneralTabContent(list, player, profileTargetId);
            return;
        }
        if (subTab == 1)
        {
            AddResourcesTabContent(list, player, profileTargetId);
            return;
        }
        if (subTab == 2)
        {
            AddBuildingTabContent(list, player, profileTargetId);
            return;
        }
        if (subTab == 3)
        {
            AddHitrateTabContent(list, player, profileTargetId);
            return;
        }
        list.Add(new JObject
        {
            ["name"] = PanelName + "_TabPlaceholder",
            ["parent"] = ScrollContentParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "This tab is not implemented yet.", ["fontSize"] = 16, ["align"] = "MiddleCenter", ["color"] = "0.7 0.7 0.7 1" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "0 -80", ["offsetmax"] = "0 0" }
            }
        });
    }

    private static void AddHitrateTabContent(JArray list, BasePlayer player, ulong profileTargetId)
    {
        const float imgHeight = 420f;
        const string hitratePanel = PanelName + "_Hitrate";
        var mod = LeaderboardMod.Instance;
        string pngId = mod?.GetImageId("HitRate-Stage-2.png");
        string url = mod?.GetImageUrl("HitRate-Stage-2.png");
        var bgComponents = new JArray
        {
            new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "0 -" + imgHeight, ["offsetmax"] = "0 0" }
        };
        if (!string.IsNullOrEmpty(pngId))
            bgComponents.Insert(0, new JObject { ["type"] = "UnityEngine.UI.RawImage", ["png"] = pngId, ["color"] = "1 1 1 1" });
        else if (!string.IsNullOrEmpty(url))
            bgComponents.Insert(0, new JObject { ["type"] = "UnityEngine.UI.RawImage", ["url"] = url, ["color"] = "1 1 1 1" });
        else
            bgComponents.Insert(0, new JObject { ["type"] = "UnityEngine.UI.Image", ["color"] = "0.15 0.15 0.15 1" });
        list.Add(new JObject
        {
            ["name"] = hitratePanel,
            ["parent"] = ScrollContentParent,
            ["components"] = bgComponents
        });

        // Five hit% boxes: label + gap + percentage (PvP body hits from stats)
        PlayerStats stats = null;
        if (mod != null)
            mod.TryGetStats(profileTargetId, out stats);
        const float fs = 14f;
        const string hitColor = "0.843 0.286 0.2 1";
        const string labelGap = "                    ";
        int hitIdx = 0;
        float total = stats != null ? stats.GetTotalBodyHits() : 0f;
        string Pct(string key) => stats == null ? "0%" : FormatHitratePct(stats.GetBodyHits(key), total);
        // Left: ARM, Stomach
        const float armX = 150f;
        const float stomachX = 115f;
        AddHitrateLabel(list, hitratePanel, armX, -86f, "ARM" + labelGap + Pct("arm"), fs, hitColor, ref hitIdx);
        AddHitrateLabel(list, hitratePanel, stomachX, -226f, "Stomach" + labelGap + Pct("stomach"), fs, hitColor, ref hitIdx);
        // Right: HEAD, CHEST, LEG
        const float headX = 730f;
        const float chestLegX = 820f;
        const float headY = -35f;
        const float chestY = -198f;
        const float legY = -358f;
        AddHitrateLabel(list, hitratePanel, headX, headY, "HEAD" + labelGap + Pct("head"), fs, hitColor, ref hitIdx);
        AddHitrateLabel(list, hitratePanel, chestLegX, chestY, "CHEST" + labelGap + Pct("chest"), fs, hitColor, ref hitIdx);
        AddHitrateLabel(list, hitratePanel, chestLegX, legY, "LEG" + labelGap + Pct("leg"), fs, hitColor, ref hitIdx);
    }

    private static string FormatHitratePct(float part, float total)
    {
        if (total <= 0) return "0%";
        return ((int)System.Math.Round(part / total * 100f)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    private static void AddHitrateLabel(JArray list, string parentName, float x, float yTop, string text, float fontSize, string color, ref int index)
    {
        const float w = 220f;
        const float h = 22f;
        list.Add(new JObject
        {
            ["name"] = parentName + "_Hit_" + (index++),
            ["parent"] = parentName,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = text, ["fontSize"] = (int)fontSize, ["align"] = "UpperLeft", ["color"] = color },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = x + " " + (yTop - h), ["offsetmax"] = (x + w) + " " + yTop }
            }
        });
    }

    private static void AddResourcesTabContent(JArray list, BasePlayer player, ulong profileTargetId)
    {
        var mod = LeaderboardMod.Instance;
        if (mod == null) return;
        mod.TryGetStats(profileTargetId, out var stats);

        const float contentTopMargin = -2f;
        const float leftIndent = 10f;
        const float fieldW = 220f;
        const float fieldH = 48f;
        const float gap = 44f;
        const float fieldMarginY = 14f;
        float rowHeight = fieldH + fieldMarginY;
        const float sectionTitleHeight = 22f;
        const float sectionGap = 8f;

        float y = contentTopMargin;

        // Sections match UltimateLeaderboard RESOURCES block
        var sections = new[]
        {
            new ResourceSection("RESOURCES", new[]
            {
                (LootType.Gather, "stones", "Stone"),
                (LootType.Gather, "wood", "Wood"),
                (LootType.Gather, "sulfur.ore", "Sulfur Ore"),
                (LootType.Gather, "metal.ore", "Metal Ore"),
                (LootType.Gather, "hq.metal.ore", "HQ Metal Ore"),
                (LootType.Gather, "leather", "Leather"),
                (LootType.Gather, "bone.fragments", "Bone Fragments"),
                (LootType.Gather, "fat.animal", "Animal Fat"),
                (LootType.LootItems, "scrap", "Scrap")
            }),
            new ResourceSection("FARMING", new[]
            {
                (LootType.Gather, "hemp-collectable", "Hemp"),
                (LootType.Gather, "blue.berry", "Blue Berry"),
                (LootType.Gather, "red.berry", "Red Berry"),
                (LootType.Gather, "yellow.berry", "Yellow Berry"),
                (LootType.Gather, "black.berry", "Black Berry"),
                (LootType.Gather, "green.berry", "Green Berry"),
                (LootType.Gather, "white.berry", "White Berry"),
                (LootType.Gather, "potato", "Potato"),
                (LootType.Gather, "cloth", "Cloth"),
                (LootType.Gather, "mushroom", "Mushroom"),
                (LootType.Gather, "corn", "Corn"),
                (LootType.Gather, "pumpkin", "Pumpkin"),
                (LootType.Gather, "orchid", "Orchid"),
                (LootType.Gather, "rose", "Rose"),
                (LootType.Gather, "sunflower", "Sunflower"),
                (LootType.Gather, "wheat", "Wheat")
            }),
            new ResourceSection("MISC", new[]
            {
                (LootType.Kill, "bear", "Bears"),
                (LootType.Kill, "polarbear", "Polar Bears"),
                (LootType.Kill, "boar", "Boars"),
                (LootType.Kill, "chicken", "Chicken"),
                (LootType.Kill, "stag", "Stag"),
                (LootType.Kill, "wolf2", "Wolf"),
                (LootType.Kill, "panther", "Panther"),
                (LootType.Kill, "crocodile", "Crocodile"),
                (LootType.Kill, "snake.entity", "Snake"),
                (LootType.Kill, "tiger", "Tiger"),
                (LootType.Kill, "simpleshark", "Shark"),
                (LootType.Kill, "bradleyapc", "Bradley"),
                (LootType.Kill, "helicopter", "Helicopter")
            }),
            new ResourceSection("RAID", new[]
            {
                (LootType.ExplosiveUsed, "explosive.satchel", "Satchel"),
                (LootType.ExplosiveUsed, "grenade.molotov", "Molotov"),
                (LootType.ExplosiveUsed, "grenade.flashbang", "Flashbang"),
                (LootType.ExplosiveUsed, "surveycharge", "Survey Charge"),
                (LootType.ExplosiveUsed, "grenade.f1", "Grenade"),
                (LootType.ExplosiveUsed, "grenade.beancan", "Beancan"),
                (LootType.ExplosiveUsed, "ammo.rocket.basic", "Rocket"),
                (LootType.ExplosiveUsed, "ammo.rocket.fire", "Incendiary Rocket"),
                (LootType.ExplosiveUsed, "ammo.rocket.hv", "Rocket HV"),
                (LootType.ShotFired, "ammo.rifle.explosive", "Explosive 5.56 Rifle Ammo"),
                (LootType.ExplosiveUsed, "ammo.grenadelauncher.he", "GL HE"),
                (LootType.ExplosiveUsed, "explosive.timed", "C4"),
                (LootType.ShotFired, "ammo.rocket.mlrs", "MLRS Rocket"),
                (LootType.RaidableBases, "easy", "Raidable Easy"),
                (LootType.RaidableBases, "medium", "Raidable Medium"),
                (LootType.RaidableBases, "hard", "Raidable Hard"),
                (LootType.RaidableBases, "expert", "Raidable Expert"),
                (LootType.RaidableBases, "nightmare", "Raidable Nightmare")
            }),
            new ResourceSection("EVENTS", new[]
            {
                (LootType.Event, "Convoy", "Convoy"),
                (LootType.Event, "ArmoredTrainEvent", "Armored Train"),
                (LootType.Event, "CHT", "Custom Helicopter (CHT)"),
                (LootType.Kill, "bradleyapc", "Bradley"),
                (LootType.Kill, "helicopter", "Patrol Helicopter")
            }),
            new ResourceSection("RECYCLED", new[]
            {
                (LootType.RecycleItem, "propanetank", "Propane Tanks"),
                (LootType.RecycleItem, "gears", "Gears"),
                (LootType.RecycleItem, "metalpipe", "Metal Pipe"),
                (LootType.RecycleItem, "riflebody", "Rifle Body"),
                (LootType.RecycleItem, "semibody", "Semi Body"),
                (LootType.RecycleItem, "metalspring", "Metal Springs"),
                (LootType.RecycleItem, "roadsigns", "Road Signs"),
                (LootType.RecycleItem, "sewingkit", "Sewing Kits"),
                (LootType.RecycleItem, "tarp", "Tarp"),
                (LootType.RecycleItem, "rope", "Rope"),
                (LootType.RecycleItem, "sheetmetal", "Sheet Metal"),
                (LootType.RecycleItem, "fuse", "Fuse"),
                (LootType.RecycleItem, "metalblade", "Metal Blade"),
                (LootType.RecycleItem, "smgbody", "SMG Body"),
                (LootType.RecycleItem, "techparts", "Tech Parts"),
                (LootType.RecycleItem, "targeting.computer", "Targeting Computer"),
                (LootType.RecycleItem, "cctv.camera", "CCTV Camera")
            }),
            new ResourceSection("FIRED", new[]
            {
                (LootType.ShotFired, "ammo.pistol", "Pistol"),
                (LootType.ShotFired, "ammo.pistol.fire", "Pistol Incendiary"),
                (LootType.ShotFired, "ammo.pistol.hv", "Pistol HV"),
                (LootType.ShotFired, "ammo.nailgun.nails", "Nailguns"),
                (LootType.ShotFired, "ammo.rifle", "Rifle"),
                (LootType.ShotFired, "ammo.rifle.hv", "Rifle HV"),
                (LootType.ShotFired, "ammo.rifle.incendiary", "Rifle Incendiary"),
                (LootType.ShotFired, "ammo.handmade.shell", "HandMades"),
                (LootType.ShotFired, "ammo.grenadelauncher.buckshot", "GL Buckshot"),
                (LootType.ShotFired, "ammo.shotgun.slug", "Shotgun Slug"),
                (LootType.ShotFired, "ammo.shotgun.fire", "Shotgun Fire"),
                (LootType.ShotFired, "ammo.shotgun", "Shotgun"),
                (LootType.ShotFired, "snowball", "SnowBall"),
                (LootType.ShotFired, "arrow.wooden", "Wooden Arrow"),
                (LootType.ShotFired, "arrow.fire", "Fire Arrow"),
                (LootType.ShotFired, "arrow.bone", "Bone Arrow"),
                (LootType.ShotFired, "arrow.hv", "High Velocity Arrow")
            }),
            new ResourceSection("FISHING", new[]
            {
                (LootType.Fishing, "fish.anchovy", "Anchovy"),
                (LootType.Fishing, "fish.catfish", "Catfish"),
                (LootType.Fishing, "fish.herring", "Herring"),
                (LootType.Fishing, "fish.orangeroughy", "Orange Roughy"),
                (LootType.Fishing, "fish.salmon", "Salmon"),
                (LootType.Fishing, "fish.sardine", "Sardine"),
                (LootType.Fishing, "fish.smallshark", "Small Shark"),
                (LootType.Fishing, "fish.troutsmall", "Small Trout"),
                (LootType.Fishing, "fish.yellowperch", "Yellow Perch"),
                (LootType.Fishing, "fish.minnows", "Minnows")
            }),
            new ResourceSection("CRAFTED", new[]
            {
                (LootType.Craft, "wall.frame.garagedoor", "Garage Door"),
                (LootType.Craft, "door.double.hinged.metal", "Sheet Metal Double Door"),
                (LootType.Craft, "door.double.hinged.toptier", "Armored Double Door"),
                (LootType.Craft, "door.double.hinged.wood", "Wood Double Door"),
                (LootType.Craft, "door.hinged.metal", "Sheet Metal Door"),
                (LootType.Craft, "door.hinged.toptier", "Armored Door"),
                (LootType.Craft, "door.hinged.wood", "Wood Door"),
                (LootType.Craft, "box.wooden.large", "Large Wood Box"),
                (LootType.Craft, "box.wooden", "Wood Storage Box"),
                (LootType.Craft, "flameturret", "Flame Turret"),
                (LootType.Craft, "guntrap", "Shotgun Trap"),
                (LootType.Craft, "autoturret", "Auto Turret"),
                (LootType.Craft, "lock.code", "Code Lock"),
                (LootType.Craft, "lock.key", "Key Lock"),
                (LootType.Craft, "ladder.wooden.wall", "Wooden Ladder"),
                (LootType.Craft, "syringe.medical", "Medical Syringe"),
                (LootType.Craft, "bandage", "Bandage"),
                (LootType.Craft, "largemedkit", "Large Medkit"),
                (LootType.Craft, "rocket.launcher", "Rocket Launcher"),
                (LootType.Craft, "ammo.rocket.basic", "Rocket"),
                (LootType.Craft, "ammo.rocket.fire", "Incendiary Rocket"),
                (LootType.Craft, "ammo.rocket.hv", "HV Rocket"),
                (LootType.Craft, "explosive.timed", "C4"),
                (LootType.Craft, "explosive.satchel", "Satchel"),
                (LootType.Craft, "grenade.beancan", "Beancan"),
                (LootType.Craft, "grenade.f1", "Grenade"),
                (LootType.Craft, "ammo.rifle.explosive", "Explosive 5.56 Rifle Ammo"),
                (LootType.Craft, "explosives", "Explosives"),
                (LootType.Craft, "gunpowder", "Gun Powder"),
                (LootType.Craft, "lowgradefuel", "Low Grade Fuel")
            }),
            new ResourceSection("LOOTED", new[]
            {
                (LootType.Crate, "crate_normal_2", "Normal Crate"),
                (LootType.Crate, "crate_basic", "Basic Crate"),
                (LootType.Crate, "crate_elite", "Elite Crate"),
                (LootType.Crate, "crate_normal", "Military Crate"),
                (LootType.Crate, "crate_tools", "Tools Crate"),
                (LootType.Crate, "supply_drop", "Supply Drop"),
                (LootType.Crate, "codelockedhackablecrate|codelockedhackablecrate_oilrig", "Locked Crate")
            })
        };

        foreach (var section in sections)
        {
            y -= sectionTitleHeight;
            float titleYMin = y - sectionTitleHeight;
            list.Add(new JObject
            {
                ["name"] = PanelName + "_Res_" + section.Title.Replace(" ", "_"),
                ["parent"] = ScrollContentParent,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = section.Title, ["fontSize"] = 14, ["align"] = "UpperLeft", ["color"] = "0.886 0.859 0.827 0.9" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = leftIndent + " " + titleYMin, ["offsetmax"] = "-" + leftIndent + " " + y }
                }
            });
            y = titleYMin - sectionGap;

            int index = 0;
            foreach (var (lootType, key, displayName) in section.Entries)
            {
                int col = index % 3;
                int row = index / 3;
                float rYMax = y - row * (rowHeight);
                float rYMin = rYMax - fieldH;
                float x0 = leftIndent + col * (fieldW + gap);
                float x1 = x0 + fieldW;
                string value = GetResourceStatValue(stats, lootType, key);
                string blockName = PanelName + "_Res_" + section.Title.Replace(" ", "_") + "_" + index;
                int? itemId = LeaderboardMod.Instance?.GetItemIdForResource(key);
                AddGeneralStatBlock(list, blockName, x0, x1, rYMin, rYMax, displayName, value, null, itemId);
                index++;
            }
            int rowsInSection = (section.Entries.Length + 2) / 3;
            y -= rowsInSection * rowHeight + sectionGap;
        }
    }

    private static string GetResourceStatValue(PlayerStats stats, LootType type, string key)
    {
        if (stats == null) return "0";
        float v = 0f;
        if (key.IndexOf('|') >= 0)
        {
            foreach (var k in key.Split('|'))
            {
                if (stats.TryGetItem(type, k.Trim(), out var part)) v += part;
            }
        }
        else if (!stats.TryGetItem(type, key, out v)) return "0";
        if (v >= 1000) return (v / 1000f).ToString("F1") + "K";
        return ((int)v).ToString();
    }

    private sealed class ResourceSection
    {
        public readonly string Title;
        public readonly (LootType type, string key, string displayName)[] Entries;
        public ResourceSection(string title, (LootType type, string key, string displayName)[] entries)
        {
            Title = title;
            Entries = entries;
        }
    }

    private static void AddBuildingTabContent(JArray list, BasePlayer player, ulong profileTargetId)
    {
        var mod = LeaderboardMod.Instance;
        if (mod == null) return;
        mod.TryGetStats(profileTargetId, out var stats);

        const float leftIndent = 10f;
        const float fieldW = 220f;
        const float fieldH = 48f;
        const float firstRowH = 62f;
        const float gap = 90f;
        const float contentTopMargin = -16f;
        const float rowGap = 18f;
        const float fieldMarginY = 18f;
        float rowHeight = fieldH + fieldMarginY;
        const float sectionTitleHeight = 20f;
        const float sectionGap = 4f;
        const float resGap = 44f;

        float yMax = contentTopMargin;

        // STRUCTURES category title above the first row
        yMax -= sectionTitleHeight;
        float structTitleYMin = yMax - sectionTitleHeight;
        list.Add(new JObject
        {
            ["name"] = PanelName + "_Bld_StructCategory",
            ["parent"] = ScrollContentParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "STRUCTURES", ["fontSize"] = 14, ["align"] = "UpperLeft", ["color"] = "0.886 0.859 0.827 0.9" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = leftIndent + " " + structTitleYMin, ["offsetmax"] = "-" + leftIndent + " " + yMax }
            }
        });
        yMax = structTitleYMin - sectionGap;
        float yMinFirst = yMax - firstRowH;

        // First row: Structures Built, Upgrades Performed, Favorite Building Material
        float x0 = leftIndent;
        float x1 = x0 + fieldW;
        float structuresTotal = stats?.GetTotal(LootType.Construction) ?? 0f;
        string structuresText = structuresTotal >= 1000 ? (structuresTotal / 1000f).ToString("F1") + "K" : ((int)structuresTotal).ToString();
        AddGeneralBlock(list, PanelName + "_Bld_Structures", x0, x1, yMinFirst, yMax, "Structures Built", structuresText, null, false, "structures_built_icon.png", firstRowH, 52f);
        x0 = leftIndent + fieldW + gap;
        x1 = x0 + fieldW;
        float upgradesTotal = stats?.GetTotal(LootType.Upgrade) ?? 0f;
        string upgradesText = upgradesTotal >= 1000 ? (upgradesTotal / 1000f).ToString("F1") + "K" : ((int)upgradesTotal).ToString();
        AddGeneralBlock(list, PanelName + "_Bld_Upgrades", x0, x1, yMinFirst, yMax, "Upgrades Performed", upgradesText, null, false, "upgrades_performed_icon.png", firstRowH, 52f);
        x0 = leftIndent + (fieldW + gap) * 2f;
        x1 = x0 + fieldW;
        string favoriteMat = FormatBuildingMaterial(GetTopKey(stats, LootType.Upgrade, out _));
        AddGeneralBlock(list, PanelName + "_Bld_Favorite", x0, x1, yMinFirst, yMax, "Favorite Building Material", favoriteMat, null, false, "favorite_building_material_icon.png", firstRowH, 52f);

        float y = yMinFirst - rowGap;

        // One section: "MOST BUILT STRUCTURES & UPGRADES BY MATERIAL" — then structure grid, then upgrades grid
        var constructionAll = stats?.GetAll(LootType.Construction) ?? new Dictionary<string, float>();
        var topStructures = constructionAll.OrderByDescending(kv => kv.Value).Take(9).ToList();
        var upgradeEntries = new[]
        {
            ("wood", "Wood", "Building-Banner-Wood.png"),
            ("stone", "Stone", "Building-Banner-Stones.png"),
            ("metal", "Metal", "Building-Banner-Metal.png"),
            ("toptier", "Top Tier", "Building-Banner-HQM.png"),
            ("twigs", "Twigs", "Building-Banner-Twigs.png")
        };
        const float upgCardW = 195f;
        const float upgCardH = 96f;
        const float upgIconSize = 80f;
        const float upgGap = 44f;
        float upgRowH = upgCardH + 14f;
        const int upgPerRow = 3;

        y -= sectionTitleHeight;
        float titleYMin = y - sectionTitleHeight;
        list.Add(new JObject
        {
            ["name"] = PanelName + "_Bld_MostBuiltAndUpgrades",
            ["parent"] = ScrollContentParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "UPGRADED STRUCTURES BY MATERIAL TYPE — HOW MANY OF EACH YOU'VE BUILT", ["fontSize"] = 14, ["align"] = "UpperLeft", ["color"] = "0.886 0.859 0.827 0.9" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = leftIndent + " " + titleYMin, ["offsetmax"] = "-" + leftIndent + " " + y }
            }
        });
        y = titleYMin - sectionGap;

        // Grid: most built structures (top 9)
        AddBuildingSection(list, "", topStructures.Select(kv => (kv.Key, kv.Value)).ToArray(), y, leftIndent, fieldW, fieldH, resGap, rowHeight, sectionTitleHeight, sectionGap, (key, _) => FormatStructureName(key), null, skipTitle: true);
        int structRows = (topStructures.Count + 2) / 3;
        y -= structRows * rowHeight + sectionGap;

        // Grid: upgrades by material (5 cards)
        for (int i = 0; i < upgradeEntries.Length; i++)
        {
            int col = i % upgPerRow;
            int row = i / upgPerRow;
            float rYMax = y - row * upgRowH;
            float rYMin = rYMax - upgCardH;
            float x0u = leftIndent + col * (upgCardW + upgGap);
            float x1u = x0u + upgCardW;
            float val = GetUpgradeTotalByGrade(stats, upgradeEntries[i].Item1);
            string valStr = val >= 1000 ? (val / 1000f).ToString("F1") + "K" : ((int)val).ToString();
            string blockName = PanelName + "_Bld_Upg_" + i;
            AddGeneralStatBlock(list, blockName, x0u, x1u, rYMin, rYMax, upgradeEntries[i].Item2, valStr, upgradeEntries[i].Item3, null, upgCardH, upgIconSize, iconWidth: 120f);
        }
    }

    private static void AddBuildingSection(JArray list, string sectionTitle, (string key, float value)[] entries, float y, float leftIndent, float fieldW, float fieldH, float gap, float rowHeight, float sectionTitleHeight, float sectionGap, Func<string, float, string> getDisplayName, string iconFileName, bool skipTitle = false)
    {
        if (!skipTitle)
        {
            y -= sectionTitleHeight;
            float titleYMin = y - sectionTitleHeight;
            list.Add(new JObject
            {
                ["name"] = PanelName + "_Bld_" + sectionTitle.Replace(" ", "_"),
                ["parent"] = ScrollContentParent,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = sectionTitle, ["fontSize"] = 14, ["align"] = "UpperLeft", ["color"] = "0.886 0.859 0.827 0.9" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = leftIndent + " " + titleYMin, ["offsetmax"] = "-" + leftIndent + " " + y }
                }
            });
            y = titleYMin - sectionGap;
        }
        for (int i = 0; i < entries.Length; i++)
        {
            int col = i % 3;
            int row = i / 3;
            float rYMax = y - row * rowHeight;
            float rYMin = rYMax - fieldH;
            float x0 = leftIndent + col * (fieldW + gap);
            float x1 = x0 + fieldW;
            string displayName = getDisplayName(entries[i].key, entries[i].value);
            string valueStr = entries[i].value >= 1000 ? (entries[i].value / 1000f).ToString("F1") + "K" : ((int)entries[i].value).ToString();
            string blockName = PanelName + "_Bld_" + sectionTitle.Replace(" ", "_") + "_" + i;
            AddGeneralStatBlock(list, blockName, x0, x1, rYMin, rYMax, displayName, valueStr, iconFileName, null);
        }
    }

    private static float GetUpgradeTotalByGrade(PlayerStats stats, string grade)
    {
        if (stats == null || string.IsNullOrEmpty(grade)) return 0f;
        var all = stats.GetAll(LootType.Upgrade);
        if (all == null) return 0f;
        string suffix = " " + grade;
        float sum = 0f;
        foreach (var kv in all)
            if (kv.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                sum += kv.Value;
        return sum;
    }

    private static string FormatStructureName(string shortName)
    {
        if (string.IsNullOrEmpty(shortName)) return "—";
        return char.ToUpperInvariant(shortName[0]) + (shortName.Length > 1 ? shortName.Substring(1).Replace(".", " ").Replace("_", " ") : "");
    }

    private static void AddGeneralTabContent(JArray list, BasePlayer player, ulong profileTargetId)
    {
        var mod = LeaderboardMod.Instance;
        if (mod == null) return;
        if (!mod.TryGetStats(profileTargetId, out var stats))
            stats = null;

        bool viewingSelf = profileTargetId == player.userID;
        string displayName = viewingSelf ? (player.displayName ?? "—") : (stats?.LastName ?? profileTargetId.ToString());
        string steamIdText = profileTargetId.ToString();

        int kills = stats?.GetKills() ?? 0;
        int deaths = stats?.GetDeaths() ?? 0;
        double playTime = stats?.GetTotalPlayTimeIncludingCurrent() ?? 0;
        string sessionTime = viewingSelf ? GetSessionPlayTime(player) : "—";
        string totalTime = FormatPlayTimeLong(playTime);

        // First row: three blocks (Profile, K/D, Time) — taller row so avatar/icons aren't cropped
        const float leftIndent = 10f;
        const float fieldW = 220f;
        const float fieldH = 48f;
        const float firstRowH = 62f;
        const float gap = 90f;
        const float contentTopMargin = -16f; // first row top (negative = lower; was 10, move down 26)
        float yMax = contentTopMargin;
        float yMinFirst = yMax - firstRowH;

        float x0 = leftIndent;
        float x1 = x0 + fieldW;
        AddGeneralBlock(list, PanelName + "_Gen_Profile", x0, x1, yMinFirst, yMax, "Profile", displayName, steamIdText, true, null, firstRowH, 52f);
        x0 = leftIndent + fieldW + gap;
        x1 = x0 + fieldW;
        AddGeneralBlock(list, PanelName + "_Gen_KD", x0, x1, yMinFirst, yMax, "K/D", "Kills: " + kills + " / Deaths: " + deaths, null, false, "kd_icon.png", firstRowH, 52f);
        x0 = leftIndent + (fieldW + gap) * 2f;
        x1 = x0 + fieldW;
        AddGeneralBlock(list, PanelName + "_Gen_Time", x0, x1, yMinFirst, yMax, "Time", sessionTime + "\n" + totalTime, null, false, "time_icon.png", firstRowH, 52f);

        // Rows 2–4: 9 stat blocks (3 per row)
        const float rowGap = 30f;
        const float fieldMarginY = 30f;
        float rowHeight = fieldH + fieldMarginY;
        float y = yMinFirst - rowGap;
        int blockIndex = 0;
        string[] titles = new[]
        {
            "Longest Kill Distance", "Total Resources Gathered", "Favorite Resource",
            "Total Items Crafted", "Structures Built", "Upgrades Performed",
            "Favorite Building Material", "Events Won", "Favorite Event"
        };
        string[] iconFiles = new[]
        {
            "longest_kill_distance_icon.png", "total_resources_icon.png", "favorite_resource_icon.png",
            "total_items_crafted_icon.png", "structures_built_icon.png", "upgrades_performed_icon.png",
            "favorite_building_material_icon.png", "events_won_icon.png", "favorite_event_icon.png"
        };
        string[] values = GetGeneralStatValues(stats);
        for (int row = 0; row < 3; row++)
        {
            float rYMin = y - fieldH;
            float rYMax = y;
            for (int col = 0; col < 3; col++)
            {
                x0 = leftIndent + col * (fieldW + gap);
                x1 = x0 + fieldW;
                string name = PanelName + "_Gen_Stat" + blockIndex;
                AddGeneralStatBlock(list, name, x0, x1, rYMin, rYMax, titles[blockIndex], values[blockIndex], iconFiles[blockIndex]);
                blockIndex++;
            }
            y = y - rowHeight;
        }
    }

    private static string[] GetGeneralStatValues(PlayerStats stats)
    {
        var v = new string[9];
        if (stats == null)
        {
            for (int i = 0; i < 9; i++) v[i] = "—";
            return v;
        }
        stats.TryGetItem(LootType.Kill, "max_distance", out var maxDist);
        v[0] = maxDist > 0 ? maxDist.ToString("F1") + "m" : "0m";
        float totalRes = stats.GetTotal(LootType.Gather);
        v[1] = totalRes >= 1000 ? (totalRes / 1000f).ToString("F1") + "K" : ((int)totalRes).ToString();
        v[2] = GetTopKey(stats, LootType.Gather, out _) ?? "—";
        float totalCraft = stats.GetTotal(LootType.Craft);
        v[3] = totalCraft >= 1000 ? (totalCraft / 1000f).ToString("F1") + "K" : ((int)totalCraft).ToString();
        float structures = stats.GetTotal(LootType.Construction);
        v[4] = structures >= 1000 ? (structures / 1000f).ToString("F1") + "K" : ((int)structures).ToString();
        float upgrades = stats.GetTotal(LootType.Upgrade);
        v[5] = upgrades >= 1000 ? (upgrades / 1000f).ToString("F1") + "K" : ((int)upgrades).ToString();
        v[6] = FormatBuildingMaterial(GetTopKey(stats, LootType.Upgrade, out _));
        // Events = Bradley + Patrol Helicopter only
        float events = GetEventCount(stats);
        v[7] = ((int)events).ToString();
        v[8] = GetFavoriteEvent(stats);
        return v;
    }

    /// <summary>Total events: LootType.Event (Convoy/AT/CHT) + Bradley/heli kills.</summary>
    private static float GetEventCount(PlayerStats stats)
    {
        if (stats == null) return 0f;
        float events = stats.GetTotal(LootType.Event);
        stats.TryGetItem(LootType.Kill, "helicopter", out var heli);
        stats.TryGetItem(LootType.Kill, "bradleyapc", out var bradley);
        return events + heli + bradley;
    }

    /// <summary>Favorite among Event keys and Bradley/Patrol Helicopter.</summary>
    private static string GetFavoriteEvent(PlayerStats stats)
    {
        if (stats == null) return "—";
        string top = null;
        float topVal = 0f;
        foreach (var kv in stats.GetAll(LootType.Event))
        {
            if (kv.Value > topVal) { topVal = kv.Value; top = kv.Key; }
        }
        stats.TryGetItem(LootType.Kill, "helicopter", out var heli);
        stats.TryGetItem(LootType.Kill, "bradleyapc", out var bradley);
        if (bradley > topVal) { topVal = bradley; top = "Bradley"; }
        if (heli > topVal) { topVal = heli; top = "Patrol Helicopter"; }
        if (topVal <= 0 || string.IsNullOrEmpty(top)) return "—";
        if (top == "ArmoredTrainEvent") return "Armored Train";
        if (top == "CHT") return "Custom Helicopter";
        return FormatEventName(top);
    }

    private static string GetTopKey(PlayerStats stats, LootType type, out float value)
    {
        value = 0f;
        var all = stats.GetAll(type);
        if (all == null || all.Count == 0) return null;
        string top = null;
        foreach (var kv in all)
        {
            if (kv.Value > value) { value = kv.Value; top = kv.Key; }
        }
        return top;
    }

    private static string FormatBuildingMaterial(string key)
    {
        if (string.IsNullOrEmpty(key)) return "—";
        var parts = key.Split(' ');
        if (parts.Length >= 2 && parts[1].Length > 0)
            return char.ToUpperInvariant(parts[1][0]) + (parts[1].Length > 1 ? parts[1].Substring(1).ToLowerInvariant() : "");
        return key.Length > 0 ? char.ToUpperInvariant(key[0]) + (key.Length > 1 ? key.Substring(1).ToLowerInvariant() : "") : key;
    }

    private static string FormatEventName(string key)
    {
        if (string.IsNullOrEmpty(key)) return "—";
        return key.Length > 0 ? char.ToUpperInvariant(key[0]) + key.Substring(1).ToLowerInvariant() : key;
    }

    private static string GetSessionPlayTime(BasePlayer player)
    {
        if (player == null) return "Today: —";
        var mod = LeaderboardMod.Instance;
        if (mod == null || !mod.TryGetStats(player.userID, out var stats)) return "Today: —";
        var sec = (System.DateTime.UtcNow - stats.ConnectTime).TotalSeconds;
        if (sec < 0) return "Today: —";
        return "Today: " + FormatPlayTime(sec);
    }

    private static string FormatPlayTimeLong(double totalSeconds)
    {
        if (totalSeconds < 60) return $"{(int)totalSeconds}s";
        if (totalSeconds < 3600) return $"{(int)(totalSeconds / 60)}m {(int)(totalSeconds % 60)}s";
        if (totalSeconds < 86400) return $"{(int)(totalSeconds / 3600)}h {(int)((totalSeconds % 3600) / 60)}m";
        return $"{(int)(totalSeconds / 86400)}d {(int)((totalSeconds % 86400) / 3600)}h";
    }

    private static void AddGeneralBlock(JArray list, string blockName, float x0, float x1, float yMin, float yMax, string title, string valueText, string steamIdForAvatar, bool hasAvatar, string iconFileName, float cardHeight = 48f, float iconSize = 48f)
    {
        list.Add(new JObject
        {
            ["name"] = blockName,
            ["parent"] = ScrollContentParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Image", ["sprite"] = TileSprite, ["color"] = CardBgColor },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = x0 + " " + yMin, ["offsetmax"] = x1 + " " + yMax }
            }
        });

        float pad = 8f;
        float halfIcon = iconSize * 0.5f;
        if (hasAvatar && !string.IsNullOrEmpty(steamIdForAvatar))
        {
            list.Add(new JObject
            {
                ["name"] = blockName + "_Avatar",
                ["parent"] = blockName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.RawImage", ["steamid"] = steamIdForAvatar, ["color"] = "1 1 1 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0.5", ["anchormax"] = "0 0.5", ["offsetmin"] = pad + " " + (-halfIcon), ["offsetmax"] = (pad + iconSize) + " " + halfIcon }
                }
            });
            pad += iconSize + 6f;
        }
        else if (!string.IsNullOrEmpty(iconFileName))
        {
            AddBlockIcon(list, blockName, pad, iconSize, iconFileName);
            pad += iconSize + 6f;
        }

        // Title and value inside the card (offsets from top: negative = below top)
        float titleTop = 6f;
        float titleBottom = 24f;
        float valueTop = 28f;
        float valueBottom = cardHeight - 4f;
        list.Add(new JObject
        {
            ["name"] = blockName + "_Title",
            ["parent"] = blockName,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = title, ["fontSize"] = 12, ["align"] = "UpperLeft", ["color"] = "0.886 0.859 0.827 0.5" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = pad + " " + (-titleBottom), ["offsetmax"] = "-" + pad + " " + (-titleTop) }
            }
        });
        list.Add(new JObject
        {
            ["name"] = blockName + "_Value",
            ["parent"] = blockName,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = valueText ?? "—", ["fontSize"] = 13, ["align"] = "UpperLeft", ["color"] = "0.886 0.859 0.827 0.9" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = pad + " " + (-valueBottom), ["offsetmax"] = "-" + pad + " " + (-valueTop) }
            }
        });
    }

    private static void AddBlockIcon(JArray list, string blockName, float leftPad, float size, string iconFileName)
    {
        AddBlockIcon(list, blockName, leftPad, size, size, iconFileName);
    }

    private static void AddBlockIcon(JArray list, string blockName, float leftPad, float width, float height, string iconFileName)
    {
        if (string.IsNullOrEmpty(iconFileName)) return;
        var mod = LeaderboardMod.Instance;
        string pngId = mod?.GetImageId(iconFileName);
        string url = null;
        if (string.IsNullOrEmpty(pngId)) url = mod?.GetImageUrl(iconFileName);
        if (string.IsNullOrEmpty(pngId) && string.IsNullOrEmpty(url)) return;

        float halfH = height * 0.5f;
        var components = new JArray
        {
            new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0.5", ["anchormax"] = "0 0.5", ["offsetmin"] = leftPad + " " + (-halfH), ["offsetmax"] = (leftPad + width) + " " + halfH }
        };
        if (!string.IsNullOrEmpty(pngId))
            components.Insert(0, new JObject { ["type"] = "UnityEngine.UI.RawImage", ["png"] = pngId, ["color"] = "1 1 1 1" });
        else
            components.Insert(0, new JObject { ["type"] = "UnityEngine.UI.RawImage", ["url"] = url, ["color"] = "1 1 1 1" });

        list.Add(new JObject
        {
            ["name"] = blockName + "_Icon",
            ["parent"] = blockName,
            ["components"] = components
        });
    }

    private static void AddBlockItemIcon(JArray list, string blockName, float leftPad, float size, int itemId)
    {
        float half = size * 0.5f;
        list.Add(new JObject
        {
            ["name"] = blockName + "_Icon",
            ["parent"] = blockName,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Image", ["itemid"] = itemId, ["color"] = "1 1 1 1" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0.5", ["anchormax"] = "0 0.5", ["offsetmin"] = leftPad + " " + (-half), ["offsetmax"] = (leftPad + size) + " " + half }
            }
        });
    }

    private static void AddGeneralStatBlock(JArray list, string blockName, float x0, float x1, float yMin, float yMax, string title, string valueText, string iconFileName = null, int? itemId = null, float cardHeight = 48f, float iconSizeParam = 40f, float? iconWidth = null)
    {
        const float pad = 8f;
        list.Add(new JObject
        {
            ["name"] = blockName,
            ["parent"] = ScrollContentParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Image", ["sprite"] = TileSprite, ["color"] = CardBgColor },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = x0 + " " + yMin, ["offsetmax"] = x1 + " " + yMax }
            }
        });
        float iconW = iconWidth ?? iconSizeParam;
        if (itemId.HasValue)
            AddBlockItemIcon(list, blockName, pad, iconSizeParam, itemId.Value);
        else if (!string.IsNullOrEmpty(iconFileName))
            AddBlockIcon(list, blockName, pad, iconW, iconSizeParam, iconFileName);
        float textLeft = (itemId.HasValue || !string.IsNullOrEmpty(iconFileName)) ? (pad + iconW + 6f) : pad;
        float titleBottom = cardHeight * 0.5f;
        float titleTop = cardHeight * 0.125f;
        float valueBottom = cardHeight - 4f;
        float valueTop = cardHeight * 0.5f + 4f;
        list.Add(new JObject
        {
            ["name"] = blockName + "_Title",
            ["parent"] = blockName,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = title, ["fontSize"] = 11, ["align"] = "UpperLeft", ["color"] = "0.886 0.859 0.827 0.5" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = textLeft + " " + (-titleBottom), ["offsetmax"] = "-" + pad + " " + (-titleTop) }
            }
        });
        list.Add(new JObject
        {
            ["name"] = blockName + "_Value",
            ["parent"] = blockName,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = valueText ?? "—", ["fontSize"] = 12, ["align"] = "UpperLeft", ["color"] = "0.843 0.286 0.2 1" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = textLeft + " " + (-valueBottom), ["offsetmax"] = "-" + pad + " " + (-valueTop) }
            }
        });
    }

    private static string FormatPlayTime(double totalSeconds)
    {
        if (totalSeconds < 60) return $"{(int)totalSeconds}s";
        if (totalSeconds < 3600) return $"{(int)(totalSeconds / 60)}m";
        if (totalSeconds < 86400) return $"{(int)(totalSeconds / 3600)}h";
        return $"{(int)(totalSeconds / 86400)}d";
    }

    private static void AddStatLine(JArray list, string rowName, string label, string value, float yMin, float yMax)
    {
        list.Add(new JObject
        {
            ["name"] = rowName,
            ["parent"] = ScrollContentParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "8 " + yMin, ["offsetmax"] = "-8 " + yMax }
            }
        });
        list.Add(new JObject
        {
            ["name"] = rowName + "_L",
            ["parent"] = rowName,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = label, ["fontSize"] = 14, ["align"] = "MiddleLeft", ["color"] = "0.75 0.75 0.75 1" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0.5 1", ["offsetmin"] = "0 0", ["offsetmax"] = "-4 0" }
            }
        });
        list.Add(new JObject
        {
            ["name"] = rowName + "_R",
            ["parent"] = rowName,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = value, ["fontSize"] = 14, ["align"] = "MiddleRight", ["color"] = "0.9 0.9 0.9 1" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0.5 0", ["anchormax"] = "1 1", ["offsetmin"] = "4 0", ["offsetmax"] = "0 0" }
            }
        });
    }

    private static void AddStatRow(JArray list, int index, string label, string value, float yMin, float yMax)
    {
        var name = PanelName + "_TopRow" + index;
        list.Add(new JObject
        {
            ["name"] = name,
            ["parent"] = ScrollContentParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Image", ["sprite"] = TileSprite, ["color"] = "0.2 0.2 0.2 0.8" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "4 " + (yMin - 2), ["offsetmax"] = "-4 " + (yMax + 2) }
            }
        });
        list.Add(new JObject
        {
            ["name"] = name + "_Text",
            ["parent"] = name,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = label + "  " + value, ["fontSize"] = 14, ["align"] = "MiddleLeft", ["color"] = "0.9 0.9 0.9 1" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1", ["offsetmin"] = "10 0", ["offsetmax"] = "-10 0" }
            }
        });
    }

    private static void AddTop10Content(JArray list, int subTab, BasePlayer viewer = null)
    {
        if (subTab == 0)
        {
            AddTopKillersContent(list);
            return;
        }
        if (subTab == 1)
        {
            AddTopRaidersContent(list);
            return;
        }
        if (subTab == 2)
        {
            AddTopFarmersContent(list);
            return;
        }
        if (subTab == 3)
        {
            AddTopPlayTimeContent(list, viewer);
            return;
        }
        AddTop10Placeholder(list, "Select a tab above.");
    }

    /// <summary>Fixed header labels on main panel above the scroll view for Top Killers. Parent = main so x matches scroll content.</summary>
    private static void AddTopKillersFixedHeader(JArray list, float scrollTop, string mainParent)
    {
        const float headerHeight = 32f;
        const float nameRight = 44f;
        const float panelSide = 20f;
        const float leftPad = 8f;
        const float avatarSize = 24f;
        const float nameW = 220f;
        const float colW = 72f;
        float nameX = leftPad + avatarSize + 0f + nameRight;
        float killsX = nameX + nameW + 4f;
        float deathsX = killsX + colW;
        float animalsX = deathsX + colW;
        float npcsX = animalsX + colW;
        const float cellPad = 2f;
        float x0 = panelSide + leftPad; // same as scroll content row left

        // Header above scroll view: add positive offset to move UP (higher on screen)
        const float headerUp = 28f; // +4 (half of last +8 move)
        float headerYMax = scrollTop + headerUp;
        float headerYMin = scrollTop + headerUp - headerHeight;

        list.Add(new JObject
        {
            ["name"] = PanelName + "_KillersHdr_Nick",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "Nick Name", ["fontSize"] = 13, ["align"] = "MiddleLeft", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + nameX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + nameX + nameW) + " " + (headerYMax - cellPad) }
            }
        });
        list.Add(new JObject
        {
            ["name"] = PanelName + "_KillersHdr_Kills",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "KILLS", ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + killsX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + killsX + colW) + " " + (headerYMax - cellPad) }
            }
        });
        list.Add(new JObject
        {
            ["name"] = PanelName + "_KillersHdr_Deaths",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "DEATHS", ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + deathsX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + deathsX + colW) + " " + (headerYMax - cellPad) }
            }
        });
        list.Add(new JObject
        {
            ["name"] = PanelName + "_KillersHdr_Animals",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "ANIMALS", ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + animalsX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + animalsX + colW) + " " + (headerYMax - cellPad) }
            }
        });
        list.Add(new JObject
        {
            ["name"] = PanelName + "_KillersHdr_Npcs",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "NPCS", ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + npcsX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + npcsX + colW) + " " + (headerYMax - cellPad) }
            }
        });
    }

    /// <summary>Fixed header for Top Raiders: Nick Name, FOUNDATION, WALLS, FLOORS, DOORS, TOOLCUPBOARDS.</summary>
    private static void AddTopRaidersFixedHeader(JArray list, float scrollTop, string mainParent)
    {
        const float headerHeight = 32f;
        const float nameRight = 44f;
        const float panelSide = 20f;
        const float leftPad = 8f;
        const float avatarSize = 24f;
        const float nameW = 220f;
        const float colW = 72f;
        float nameX = leftPad + avatarSize + 0f + nameRight;
        float foundationX = nameX + nameW + 4f;
        float wallsX = foundationX + colW;
        float floorsX = wallsX + colW;
        float doorsX = floorsX + colW;
        float toolCupboardsX = doorsX + colW;
        const float cellPad = 2f;
        float x0 = panelSide + leftPad;
        const float headerUp = 28f;
        float headerYMax = scrollTop + headerUp;
        float headerYMin = scrollTop + headerUp - headerHeight;

        list.Add(new JObject
        {
            ["name"] = PanelName + "_RaidersHdr_Nick",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "Nick Name", ["fontSize"] = 13, ["align"] = "MiddleLeft", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + nameX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + nameX + nameW) + " " + (headerYMax - cellPad) }
            }
        });
        list.Add(new JObject
        {
            ["name"] = PanelName + "_RaidersHdr_Foundation",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "FOUNDATION", ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + foundationX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + foundationX + colW) + " " + (headerYMax - cellPad) }
            }
        });
        list.Add(new JObject
        {
            ["name"] = PanelName + "_RaidersHdr_Walls",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "WALLS", ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + wallsX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + wallsX + colW) + " " + (headerYMax - cellPad) }
            }
        });
        list.Add(new JObject
        {
            ["name"] = PanelName + "_RaidersHdr_Floors",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "FLOORS", ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + floorsX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + floorsX + colW) + " " + (headerYMax - cellPad) }
            }
        });
        list.Add(new JObject
        {
            ["name"] = PanelName + "_RaidersHdr_Doors",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "DOORS", ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + doorsX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + doorsX + colW) + " " + (headerYMax - cellPad) }
            }
        });
        list.Add(new JObject
        {
            ["name"] = PanelName + "_RaidersHdr_Cupboard",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "CUPBOARD", ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + toolCupboardsX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + toolCupboardsX + colW) + " " + (headerYMax - cellPad) }
            }
        });
    }

    /// <summary>Fixed header for Top Farmers: Nick Name, RESOURCES, HARVESTED, MISC, RECYCLED, FISHING.</summary>
    private static void AddTopFarmersFixedHeader(JArray list, float scrollTop, string mainParent)
    {
        const float headerHeight = 32f;
        const float nameRight = 44f;
        const float panelSide = 20f;
        const float leftPad = 8f;
        const float avatarSize = 24f;
        const float nameW = 220f;
        const float colW = 72f;
        float nameX = leftPad + avatarSize + 0f + nameRight;
        float resourcesX = nameX + nameW + 4f;
        float harvestedX = resourcesX + colW;
        float miscX = harvestedX + colW;
        float recycledX = miscX + colW;
        float fishingX = recycledX + colW;
        const float cellPad = 2f;
        float x0 = panelSide + leftPad;
        const float headerUp = 28f;
        float headerYMax = scrollTop + headerUp;
        float headerYMin = scrollTop + headerUp - headerHeight;

        list.Add(new JObject
        {
            ["name"] = PanelName + "_FarmersHdr_Nick",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "Nick Name", ["fontSize"] = 13, ["align"] = "MiddleLeft", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + nameX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + nameX + nameW) + " " + (headerYMax - cellPad) }
            }
        });
        list.Add(new JObject
        {
            ["name"] = PanelName + "_FarmersHdr_Resources",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "RESOURCES", ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + resourcesX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + resourcesX + colW) + " " + (headerYMax - cellPad) }
            }
        });
        list.Add(new JObject
        {
            ["name"] = PanelName + "_FarmersHdr_Harvested",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "HARVESTED", ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + harvestedX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + harvestedX + colW) + " " + (headerYMax - cellPad) }
            }
        });
        list.Add(new JObject
        {
            ["name"] = PanelName + "_FarmersHdr_Misc",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "MISC", ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + miscX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + miscX + colW) + " " + (headerYMax - cellPad) }
            }
        });
        list.Add(new JObject
        {
            ["name"] = PanelName + "_FarmersHdr_Recycled",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "RECYCLED", ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + recycledX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + recycledX + colW) + " " + (headerYMax - cellPad) }
            }
        });
        list.Add(new JObject
        {
            ["name"] = PanelName + "_FarmersHdr_Fishing",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "FISHING", ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + fishingX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + fishingX + colW) + " " + (headerYMax - cellPad) }
            }
        });
    }

    /// <summary>Fixed header for Total Play Time: Nick Name, TOTAL PLAY TIME (single column).</summary>
    private static void AddTopPlayTimeFixedHeader(JArray list, float scrollTop, string mainParent)
    {
        const float headerHeight = 32f;
        const float nameRight = 44f;
        const float panelSide = 20f;
        const float leftPad = 8f;
        const float avatarSize = 24f;
        const float nameW = 220f;
        const float playTimeColW = 140f;
        float nameX = leftPad + avatarSize + 0f + nameRight;
        float playTimeX = nameX + nameW + 4f;
        const float cellPad = 2f;
        float x0 = panelSide + leftPad;
        const float headerUp = 28f;
        float headerYMax = scrollTop + headerUp;
        float headerYMin = scrollTop + headerUp - headerHeight;

        list.Add(new JObject
        {
            ["name"] = PanelName + "_PlayTimeHdr_Nick",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "Nick Name", ["fontSize"] = 13, ["align"] = "MiddleLeft", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + nameX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + nameX + nameW) + " " + (headerYMax - cellPad) }
            }
        });
        list.Add(new JObject
        {
            ["name"] = PanelName + "_PlayTimeHdr_Total",
            ["parent"] = mainParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "TOTAL PLAY TIME", ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.886 0.859 0.827 0.95" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = (x0 + playTimeX) + " " + (headerYMin + cellPad), ["offsetmax"] = (x0 + playTimeX + playTimeColW) + " " + (headerYMax - cellPad) }
            }
        });
    }

    private static void AddTop10Placeholder(JArray list, string text)
    {
        list.Add(new JObject
        {
            ["name"] = PanelName + "_Top10Placeholder",
            ["parent"] = ScrollContentParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = text, ["fontSize"] = 16, ["align"] = "MiddleCenter", ["color"] = "0.7 0.7 0.7 1" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "0 -80", ["offsetmax"] = "0 0" }
            }
        });
    }

    private static void AddTopKillersContent(JArray list)
    {
        var mod = LeaderboardMod.Instance;
        var top = mod?.GetTop10Killers() ?? new List<(ulong userId, string name, int kills, int deaths, int animalKills, int npcKills)>();

        const float rowHeight = 28f;
        const float rowGap = 2f;
        const float avatarSize = 24f;
        const float leftPad = 8f;
        const float nameRight = 44f; // match header: move Nick Name column right
        const float nameW = 220f;
        const float colW = 72f;
        float nameX = leftPad + avatarSize + 0f + nameRight; // no gap: name right against avatar
        float killsX = nameX + nameW + 4f;
        float deathsX = killsX + colW;
        float animalsX = deathsX + colW;
        float npcsX = animalsX + colW;
        const float cellPad = 2f;

        // Data rows only (header is fixed above scroller via AddTopKillersFixedHeader)
        float y = 0f;
        for (int i = 0; i < top.Count; i++)
        {
            var t = top[i];
            float yMin = -y - rowHeight;
            float yMax = -y;
            string rowName = PanelName + "_KillerRow" + i;
            list.Add(new JObject
            {
                ["name"] = rowName,
                ["parent"] = ScrollContentParent,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Image", ["sprite"] = TileSprite, ["color"] = "0.2 0.2 0.2 0.8" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = leftPad + " " + (yMin - 1), ["offsetmax"] = "-" + leftPad + " " + (yMax + 1) }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Avatar",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.RawImage", ["steamid"] = t.userId.ToString(), ["color"] = "1 1 1 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0.5", ["anchormax"] = "0 0.5", ["offsetmin"] = leftPad + " " + (-avatarSize * 0.5f), ["offsetmax"] = (leftPad + avatarSize) + " " + (avatarSize * 0.5f) }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Name",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = t.name ?? "", ["fontSize"] = 13, ["align"] = "MiddleLeft", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = nameX + " " + cellPad, ["offsetmax"] = (nameX + nameW) + " -" + cellPad }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Kills",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = t.kills.ToString(), ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = killsX + " " + cellPad, ["offsetmax"] = (killsX + colW) + " -" + cellPad }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Deaths",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = t.deaths.ToString(), ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = deathsX + " " + cellPad, ["offsetmax"] = (deathsX + colW) + " -" + cellPad }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Animals",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = t.animalKills.ToString(), ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = animalsX + " " + cellPad, ["offsetmax"] = (animalsX + colW) + " -" + cellPad }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Npcs",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = t.npcKills.ToString(), ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = npcsX + " " + cellPad, ["offsetmax"] = (npcsX + colW) + " -" + cellPad }
                }
            });
            y += rowHeight + rowGap;
        }

        if (top.Count == 0)
        {
            list.Add(new JObject
            {
                ["name"] = PanelName + "_NoKillers",
                ["parent"] = ScrollContentParent,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "No kill data yet.", ["fontSize"] = 14, ["align"] = "MiddleCenter", ["color"] = "0.7 0.7 0.7 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "0 " + (-y - 24), ["offsetmax"] = "0 " + (-y) }
                }
            });
        }
    }

    private static void AddTopRaidersContent(JArray list)
    {
        var mod = LeaderboardMod.Instance;
        var top = mod?.GetTop10Raiders() ?? new List<(ulong userId, string name, int foundation, int walls, int floors, int doors, int toolCupboards)>();

        const float rowHeight = 28f;
        const float rowGap = 2f;
        const float avatarSize = 24f;
        const float leftPad = 8f;
        const float nameRight = 44f;
        const float nameW = 220f;
        const float colW = 72f;
        float nameX = leftPad + avatarSize + 0f + nameRight;
        float foundationX = nameX + nameW + 4f;
        float wallsX = foundationX + colW;
        float floorsX = wallsX + colW;
        float doorsX = floorsX + colW;
        float toolCupboardsX = doorsX + colW;
        const float cellPad = 2f;

        float y = 0f;
        for (int i = 0; i < top.Count; i++)
        {
            var t = top[i];
            float yMin = -y - rowHeight;
            float yMax = -y;
            string rowName = PanelName + "_RaiderRow" + i;
            list.Add(new JObject
            {
                ["name"] = rowName,
                ["parent"] = ScrollContentParent,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Image", ["sprite"] = TileSprite, ["color"] = "0.2 0.2 0.2 0.8" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = leftPad + " " + (yMin - 1), ["offsetmax"] = "-" + leftPad + " " + (yMax + 1) }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Avatar",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.RawImage", ["steamid"] = t.userId.ToString(), ["color"] = "1 1 1 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0.5", ["anchormax"] = "0 0.5", ["offsetmin"] = leftPad + " " + (-avatarSize * 0.5f), ["offsetmax"] = (leftPad + avatarSize) + " " + (avatarSize * 0.5f) }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Name",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = t.name ?? "", ["fontSize"] = 13, ["align"] = "MiddleLeft", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = nameX + " " + cellPad, ["offsetmax"] = (nameX + nameW) + " -" + cellPad }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Foundation",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = t.foundation.ToString(), ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = foundationX + " " + cellPad, ["offsetmax"] = (foundationX + colW) + " -" + cellPad }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Walls",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = t.walls.ToString(), ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = wallsX + " " + cellPad, ["offsetmax"] = (wallsX + colW) + " -" + cellPad }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Floors",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = t.floors.ToString(), ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = floorsX + " " + cellPad, ["offsetmax"] = (floorsX + colW) + " -" + cellPad }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Doors",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = t.doors.ToString(), ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = doorsX + " " + cellPad, ["offsetmax"] = (doorsX + colW) + " -" + cellPad }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Cupboard",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = t.toolCupboards.ToString(), ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = toolCupboardsX + " " + cellPad, ["offsetmax"] = (toolCupboardsX + colW) + " -" + cellPad }
                }
            });
            y += rowHeight + rowGap;
        }

        if (top.Count == 0)
        {
            list.Add(new JObject
            {
                ["name"] = PanelName + "_NoRaiders",
                ["parent"] = ScrollContentParent,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "No building data yet.", ["fontSize"] = 14, ["align"] = "MiddleCenter", ["color"] = "0.7 0.7 0.7 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "0 " + (-y - 24), ["offsetmax"] = "0 " + (-y) }
                }
            });
        }
    }

    private static void AddTopFarmersContent(JArray list)
    {
        var mod = LeaderboardMod.Instance;
        var top = mod?.GetTop10Farmers() ?? new List<(ulong userId, string name, float resources, float harvested, float misc, float recycled, float fishing)>();

        const float rowHeight = 28f;
        const float rowGap = 2f;
        const float avatarSize = 24f;
        const float leftPad = 8f;
        const float nameRight = 44f;
        const float nameW = 220f;
        const float colW = 72f;
        float nameX = leftPad + avatarSize + 0f + nameRight;
        float resourcesX = nameX + nameW + 4f;
        float harvestedX = resourcesX + colW;
        float miscX = harvestedX + colW;
        float recycledX = miscX + colW;
        float fishingX = recycledX + colW;
        const float cellPad = 2f;

        float y = 0f;
        for (int i = 0; i < top.Count; i++)
        {
            var t = top[i];
            float yMin = -y - rowHeight;
            float yMax = -y;
            string rowName = PanelName + "_FarmerRow" + i;
            list.Add(new JObject
            {
                ["name"] = rowName,
                ["parent"] = ScrollContentParent,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Image", ["sprite"] = TileSprite, ["color"] = "0.2 0.2 0.2 0.8" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = leftPad + " " + (yMin - 1), ["offsetmax"] = "-" + leftPad + " " + (yMax + 1) }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Avatar",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.RawImage", ["steamid"] = t.userId.ToString(), ["color"] = "1 1 1 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0.5", ["anchormax"] = "0 0.5", ["offsetmin"] = leftPad + " " + (-avatarSize * 0.5f), ["offsetmax"] = (leftPad + avatarSize) + " " + (avatarSize * 0.5f) }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Name",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = t.name ?? "", ["fontSize"] = 13, ["align"] = "MiddleLeft", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = nameX + " " + cellPad, ["offsetmax"] = (nameX + nameW) + " -" + cellPad }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Resources",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = t.resources.ToString("F0"), ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = resourcesX + " " + cellPad, ["offsetmax"] = (resourcesX + colW) + " -" + cellPad }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Harvested",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = t.harvested.ToString("F0"), ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = harvestedX + " " + cellPad, ["offsetmax"] = (harvestedX + colW) + " -" + cellPad }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Misc",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = t.misc.ToString("F0"), ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = miscX + " " + cellPad, ["offsetmax"] = (miscX + colW) + " -" + cellPad }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Recycled",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = t.recycled.ToString("F0"), ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = recycledX + " " + cellPad, ["offsetmax"] = (recycledX + colW) + " -" + cellPad }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Fishing",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = t.fishing.ToString("F0"), ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = fishingX + " " + cellPad, ["offsetmax"] = (fishingX + colW) + " -" + cellPad }
                }
            });
            y += rowHeight + rowGap;
        }

        if (top.Count == 0)
        {
            list.Add(new JObject
            {
                ["name"] = PanelName + "_NoFarmers",
                ["parent"] = ScrollContentParent,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "No farming data yet.", ["fontSize"] = 14, ["align"] = "MiddleCenter", ["color"] = "0.7 0.7 0.7 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "0 " + (-y - 24), ["offsetmax"] = "0 " + (-y) }
                }
            });
        }
    }

    private static void AddTopPlayTimeContent(JArray list, BasePlayer viewer = null)
    {
        var mod = LeaderboardMod.Instance;
        var top = mod?.GetTop10ByPlayTime(viewer) ?? new List<(ulong userId, string name, double totalSeconds)>();

        const float rowHeight = 28f;
        const float rowGap = 2f;
        const float avatarSize = 24f;
        const float leftPad = 8f;
        const float nameRight = 44f;
        const float nameW = 220f;
        const float playTimeColW = 140f;
        float nameX = leftPad + avatarSize + 0f + nameRight;
        float playTimeX = nameX + nameW + 4f;
        const float cellPad = 2f;

        float y = 0f;
        for (int i = 0; i < top.Count; i++)
        {
            var t = top[i];
            float yMin = -y - rowHeight;
            float yMax = -y;
            string rowName = PanelName + "_PlayTimeRow" + i;
            list.Add(new JObject
            {
                ["name"] = rowName,
                ["parent"] = ScrollContentParent,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Image", ["sprite"] = TileSprite, ["color"] = "0.2 0.2 0.2 0.8" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = leftPad + " " + (yMin - 1), ["offsetmax"] = "-" + leftPad + " " + (yMax + 1) }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Avatar",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.RawImage", ["steamid"] = t.userId.ToString(), ["color"] = "1 1 1 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0.5", ["anchormax"] = "0 0.5", ["offsetmin"] = leftPad + " " + (-avatarSize * 0.5f), ["offsetmax"] = (leftPad + avatarSize) + " " + (avatarSize * 0.5f) }
                }
            });
            list.Add(new JObject
            {
                ["name"] = rowName + "_Name",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = t.name ?? "", ["fontSize"] = 13, ["align"] = "MiddleLeft", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = nameX + " " + cellPad, ["offsetmax"] = (nameX + nameW) + " -" + cellPad }
                }
            });
            string timeText = FormatPlayTimeLong(t.totalSeconds);
            list.Add(new JObject
            {
                ["name"] = rowName + "_PlayTime",
                ["parent"] = rowName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = timeText, ["fontSize"] = 13, ["align"] = "MiddleCenter", ["color"] = "0.9 0.9 0.9 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0 1", ["offsetmin"] = playTimeX + " " + cellPad, ["offsetmax"] = (playTimeX + playTimeColW) + " -" + cellPad }
                }
            });
            y += rowHeight + rowGap;
        }

        if (top.Count == 0)
        {
            list.Add(new JObject
            {
                ["name"] = PanelName + "_NoPlayTime",
                ["parent"] = ScrollContentParent,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "No play time data yet.", ["fontSize"] = 14, ["align"] = "MiddleCenter", ["color"] = "0.7 0.7 0.7 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "0 " + (-y - 24), ["offsetmax"] = "0 " + (-y) }
                }
            });
        }
    }

    private static void AddSearchContent(JArray list)
    {
        var mod = LeaderboardMod.Instance;
        var players = mod?.GetAllPlayersSortedByName() ?? new List<(ulong userId, string name)>();

        const float contentTopMargin = -2f;
        const float leftIndent = 10f;
        const float cardW = 270f;
        const float cardH = 70f;
        const float gap = 20f;
        const float rowGap = 14f;
        const int perRow = 4;

        float y = contentTopMargin;
        for (int i = 0; i < players.Count; i++)
        {
            int col = i % perRow;
            int row = i / perRow;
            float rYMax = y - row * (cardH + rowGap);
            float rYMin = rYMax - cardH;
            float x0 = leftIndent + col * (cardW + gap);
            float x1 = x0 + cardW;

            var p = players[i];
            string cardName = PanelName + "_SearchCard_" + i;
            list.Add(new JObject
            {
                ["name"] = cardName,
                ["parent"] = ScrollContentParent,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Image", ["sprite"] = TileSprite, ["color"] = CardBgColor },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "0 1", ["offsetmin"] = x0 + " " + rYMin, ["offsetmax"] = x1 + " " + rYMax }
                }
            });
            const float pad = 8f;
            const float avatarSize = 48f;
            list.Add(new JObject
            {
                ["name"] = cardName + "_Avatar",
                ["parent"] = cardName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.RawImage", ["steamid"] = p.userId.ToString(), ["color"] = "1 1 1 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0.5", ["anchormax"] = "0 0.5", ["offsetmin"] = pad + " " + (-avatarSize * 0.5f), ["offsetmax"] = (pad + avatarSize) + " " + (avatarSize * 0.5f) }
                }
            });
            float textLeft = pad + avatarSize + 8f;
            float textRight = cardW - pad;
            const float blockH = 36f;
            float halfBlock = blockH * 0.5f;
            list.Add(new JObject
            {
                ["name"] = cardName + "_Name",
                ["parent"] = cardName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = p.name ?? "", ["fontSize"] = 14, ["align"] = "MiddleLeft", ["color"] = "0.886 0.859 0.827 0.95" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0.5", ["anchormax"] = "0 0.5", ["offsetmin"] = textLeft + " " + 2f, ["offsetmax"] = textRight + " " + (halfBlock + 2f) }
                }
            });
            list.Add(new JObject
            {
                ["name"] = cardName + "_SteamId",
                ["parent"] = cardName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = p.userId.ToString(), ["fontSize"] = 11, ["align"] = "MiddleLeft", ["color"] = "0.886 0.859 0.827 0.5" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0.5", ["anchormax"] = "0 0.5", ["offsetmin"] = textLeft + " " + (-halfBlock - 2f), ["offsetmax"] = textRight + " " + -2f }
                }
            });
            // Full-card button on top (last child) so whole card is clickable; color+sprite required by game CUI
            list.Add(new JObject
            {
                ["name"] = cardName + "_Btn",
                ["parent"] = cardName,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Button", ["command"] = "cui.endtest LEADERBOARD viewprofile " + p.userId, ["color"] = "1 1 1 0", ["sprite"] = TileSprite },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
                }
            });
        }

        if (players.Count == 0)
        {
            list.Add(new JObject
            {
                ["name"] = PanelName + "_SearchEmpty",
                ["parent"] = ScrollContentParent,
                ["components"] = new JArray
                {
                    new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "No players found.", ["fontSize"] = 16, ["align"] = "MiddleCenter", ["color"] = "0.7 0.7 0.7 1" },
                    new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "0 -80", ["offsetmax"] = "0 0" }
                }
            });
        }
    }

    private static void AddSearchPlaceholderContent(JArray list)
    {
        list.Add(new JObject
        {
            ["name"] = PanelName + "_SearchPlaceholder",
            ["parent"] = ScrollContentParent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = "Search by player name — coming soon.", ["fontSize"] = 16, ["align"] = "MiddleCenter", ["color"] = "0.7 0.7 0.7 1" },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "0 -60", ["offsetmax"] = "0 0" }
            }
        });
    }
}
