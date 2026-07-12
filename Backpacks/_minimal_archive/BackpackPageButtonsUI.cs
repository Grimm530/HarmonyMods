using System.Collections.Generic;
using Newtonsoft.Json;
using Network;
using UnityEngine;

namespace Backpacks
{
    /// <summary>
    /// CUI for three page buttons (Page 1, Page 2, Page 3) above the backpack loot panel.
    /// Structure matches TCUpgrade: one container + three buttons with fractional anchors (0-1)
    /// so all three buttons render. Position/size same as TC button bar (above TC loot).
    /// </summary>
    public static class BackpackPageButtonsUI
    {
        public const string PanelName = "Backpacks.PageButtons";

        // Same parent as TCUpgrade buttons; use Hud.Menu so it layers with loot UI
        private const string Parent = "Hud.Menu";

        // Same position/size as TCUpgrade, nudged up half as much (higher Y = further from bottom)
        private const string AnchorMin = "0.5 0";
        private const string AnchorMax = "0.5 0";
        private const string OffsetMin = "278 635";
        private const string OffsetMax = "571 657";

        private const string ActiveColor = "0.25 0.5 0.75 0.8";
        private const string InactiveColor = "0.451 0.553 0.271 0.8";
        private const string ActiveTextColor = "0.75 0.85 1 1";
        private const string InactiveTextColor = "0.659 0.918 0.2 1";

        private const string PanelMaterial = "assets/content/ui/namefontmaterial.mat";
        private const string PanelSprite = "assets/content/ui/ui.background.tile.psd";

        /// <summary>
        /// Build CUI JSON: one container + three buttons (Page 1, Page 2, Page 3) with fractional anchors.
        /// Commands: backpack.page 1, backpack.page 2, backpack.page 3.
        /// </summary>
        public static string BuildJson(int numPages, int activePageIndex, int slotsPerPage = 48)
        {
            if (numPages < 1) numPages = 1;
            numPages = Mathf.Min(numPages, 3);
            var list = new List<object>();

            // 1) Container only (no Image) – like TCUpgrade, so only buttons receive clicks
            list.Add(new Dictionary<string, object>
            {
                ["name"] = PanelName,
                ["parent"] = Parent,
                ["destroyUi"] = PanelName,
                ["components"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["type"] = "RectTransform",
                        ["anchormin"] = AnchorMin,
                        ["anchormax"] = AnchorMax,
                        ["offsetmin"] = OffsetMin,
                        ["offsetmax"] = OffsetMax
                    }
                }
            });

            // 2) Three buttons with fractional anchors (same layout as TCUpgrade: three equal columns + gaps)
            const float gap = 0.02f;
            float third = (1f - 2f * gap) / 3f;
            string a0_min = "0 0";
            string a0_max = third + " 1";
            string a1_min = (third + gap) + " 0";
            string a1_max = (2f * third + gap) + " 1";
            string a2_min = (2f * third + 2f * gap) + " 0";
            string a2_max = "1 1";

            string[] anchorMins = { a0_min, a1_min, a2_min };
            string[] anchorMaxs = { a0_max, a1_max, a2_max };

            for (int i = 0; i < numPages; i++)
            {
                bool isActive = i == activePageIndex;
                string cmd = "backpack.page " + (i + 1);
                string btnName = PanelName + ".Btn" + i;

                list.Add(new Dictionary<string, object>
                {
                    ["name"] = btnName,
                    ["parent"] = PanelName,
                    ["components"] = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["type"] = "UnityEngine.UI.Button",
                            ["command"] = cmd,
                            ["color"] = isActive ? ActiveColor : InactiveColor,
                            ["material"] = PanelMaterial,
                            ["sprite"] = PanelSprite
                        },
                        new Dictionary<string, object>
                        {
                            ["type"] = "RectTransform",
                            ["anchormin"] = anchorMins[i],
                            ["anchormax"] = anchorMaxs[i]
                        }
                    }
                });

                list.Add(new Dictionary<string, object>
                {
                    ["name"] = btnName + "_lbl",
                    ["parent"] = btnName,
                    ["components"] = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["type"] = "UnityEngine.UI.Text",
                            ["text"] = "Page " + (i + 1),
                            ["fontSize"] = 12,
                            ["align"] = "MiddleCenter",
                            ["color"] = isActive ? ActiveTextColor : InactiveTextColor
                        },
                        new Dictionary<string, object>
                        {
                            ["type"] = "RectTransform",
                            ["anchormin"] = "0 0",
                            ["anchormax"] = "1 1"
                        }
                    }
                });
            }

            return JsonConvert.SerializeObject(list);
        }

        public static void Show(BasePlayer player, string json)
        {
            if (player?.net?.connection == null || string.IsNullOrEmpty(json)) return;
            var ce = CommunityEntity.ServerInstance;
            if (ce == null || ce.IsDestroyed) return;
            try
            {
                ce.ClientRPC(RpcTarget.Player("AddUI", player.net.connection), json);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Backpacks] Page buttons AddUI failed: " + ex.Message);
            }
        }

        public static void Destroy(BasePlayer player)
        {
            if (player?.net?.connection == null) return;
            var ce = CommunityEntity.ServerInstance;
            if (ce == null || ce.IsDestroyed) return;
            try
            {
                ce.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), PanelName);
            }
            catch { }
        }
    }
}
