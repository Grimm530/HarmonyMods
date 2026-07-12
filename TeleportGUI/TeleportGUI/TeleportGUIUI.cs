using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Network;
using UnityEngine;

namespace TeleportGUI
{
    /// <summary>Builds CUI JSON for the Teleport GUI (panel, tabs, list, buttons). No Oxide/Chaos.</summary>
    public static class TeleportGUIUI
    {
        public const string PanelName = "teleport.ui";
        private const string Parent = "Hud";
        // Rust CUI buttons need material + sprite for proper hit-testing and interactivity (same as TCUpgrade)
        private const string ButtonMaterial = "assets/content/ui/namefontmaterial.mat";
        private const string ButtonSprite = "assets/content/ui/ui.background.tile.psd";

        private static void AddElem(List<object> list, string name, string parent, List<object> components, string destroyUi = null)
        {
            var el = new Dictionary<string, object> { ["name"] = name, ["parent"] = parent, ["components"] = components };
            if (!string.IsNullOrEmpty(destroyUi)) el["destroyUi"] = destroyUi;
            list.Add(el);
        }

        private static List<object> Panel(string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            var comps = new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = color },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = anchorMin, ["anchormax"] = anchorMax, ["offsetmin"] = offsetMin, ["offsetmax"] = offsetMax }
            };
            return comps;
        }

        private static void AddButton(List<object> list, string parent, string name, string command, string text, int fontSize, string textColor, string offsetMin, string offsetMax, string anchorMin = "0.5 0.5", string anchorMax = "0.5 0.5", string buttonColor = null)
        {
            var btnName = name + "_btn";
            string bg = string.IsNullOrEmpty(buttonColor) ? "0.2 0.2 0.25 0.95" : buttonColor;
            list.Add(new Dictionary<string, object>
            {
                ["name"] = btnName,
                ["parent"] = parent,
                ["components"] = new List<object>
                {
                    new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = command, ["color"] = bg, ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
                    new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = anchorMin, ["anchormax"] = anchorMax, ["offsetmin"] = offsetMin, ["offsetmax"] = offsetMax }
                }
            });
            list.Add(new Dictionary<string, object>
            {
                ["name"] = btnName + "_txt",
                ["parent"] = btnName,
                ["components"] = new List<object>
                {
                    new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Text", ["text"] = text, ["fontSize"] = fontSize, ["align"] = "MiddleCenter", ["color"] = textColor },
                    new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
                }
            });
        }

        private static void AddText(List<object> list, string parent, string name, string text, int fontSize, string align, string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            list.Add(new Dictionary<string, object>
            {
                ["name"] = name,
                ["parent"] = parent,
                ["components"] = new List<object>
                {
                    new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Text", ["text"] = text ?? "", ["fontSize"] = fontSize, ["align"] = align, ["color"] = color },
                    new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = anchorMin, ["anchormax"] = anchorMax, ["offsetmin"] = offsetMin, ["offsetmax"] = offsetMax }
                }
            });
        }

        /// <summary>Adds a CUI InputField. On Enter/submit the command runs with the typed text as argument.</summary>
        /// <summary>Converts config Hex + Alpha to CUI color string "r g b a" (0-1).</summary>
        private static string ToRgba(TeleportGUIConfig.UIOptions.UIColorEntry entry, string defaultRgba)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Hex)) return defaultRgba;
            string hex = entry.Hex.Trim().TrimStart('#');
            if (hex.Length != 6) return defaultRgba;
            if (!int.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out int r) ||
                !int.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out int g) ||
                !int.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out int b))
                return defaultRgba;
            float a = UnityEngine.Mathf.Clamp(entry.Alpha, 0f, 1f);
            return (r / 255f).ToString(System.Globalization.CultureInfo.InvariantCulture) + " " +
                   (g / 255f).ToString(System.Globalization.CultureInfo.InvariantCulture) + " " +
                   (b / 255f).ToString(System.Globalization.CultureInfo.InvariantCulture) + " " +
                   a.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void AddInputField(List<object> list, string parent, string name, string command, string initialOrPlaceholder, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            var comps = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["type"] = "UnityEngine.UI.InputField",
                    ["command"] = command,
                    ["text"] = initialOrPlaceholder ?? "",
                    ["fontSize"] = 12,
                    ["align"] = "MiddleLeft",
                    ["color"] = "1 1 1 1",
                    ["characterLimit"] = 64,
                    ["needsKeyboard"] = true
                },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = anchorMin, ["anchormax"] = anchorMax, ["offsetmin"] = offsetMin, ["offsetmax"] = offsetMax }
            };
            list.Add(new Dictionary<string, object> { ["name"] = name, ["parent"] = parent, ["components"] = comps });
        }

        /// <param name="isAdmin">If true, show + in header on Warp tab and delete on warp rows.</param>
        /// <param name="dailyLimitText">Optional line shown at top of content (e.g. "Daily limit remaining: 3" or "Cooldown: 45s").</param>
        /// <param name="uiColors">Optional UI colors from config (Background, Panel, Header, Button, Close, Highlight).</param>
        public static string BuildUI(BasePlayer player, string mode, int page, string searchString,
            List<PlayerEntry> playerEntries,
            List<HomeEntry> homeEntries,
            List<WarpEntry> warpEntries,
            bool hasNextPage,
            int perPage = 8,
            bool isAdmin = false,
            string dailyLimitText = null,
            TeleportGUIConfig.UIOptions.UIColors uiColors = null)
        {
            var list = new List<object>();
            string bgColor = ToRgba(uiColors?.Background, "0.12 0.12 0.15 0.97");
            string tabColor = ToRgba(uiColors?.Panel, "0.2 0.2 0.25 0.95");
            string headerColor = ToRgba(uiColors?.Header, "0.15 0.15 0.2 0.98");
            string buttonColor = ToRgba(uiColors?.Button, "0.2 0.2 0.25 0.95");
            string closeColor = ToRgba(uiColors?.Close, "0.25 0.2 0.2 0.95");

            // Root panel: center, 450x530 (anchormin/max 0.5 0.5 = center; offset in pixels)
            AddElem(list, PanelName, Parent, new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = bgColor },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0.5 0.5", ["anchormax"] = "0.5 0.5", ["offsetmin"] = "-225 -265", ["offsetmax"] = "225 265" },
                new Dictionary<string, object> { ["type"] = "NeedsCursor" }
            }, PanelName);

            // --- TOP: Tab bar (Teleport | Homes | Warps) - anchor 0 1 to 1 1 = top of panel
            AddElem(list, PanelName + "_tabbar", PanelName, new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = tabColor },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "5 0", ["offsetmax"] = "-5 -28" }
            });
            string activeTabColor = headerColor;
            // Tab 1: Players (left third) - button tab; highlight when active
            AddElem(list, PanelName + "_tab_tp", PanelName + "_tabbar", new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = mode == "teleport" ? activeTabColor : tabColor },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0.333 1", ["offsetmin"] = "2 2", ["offsetmax"] = "-2 -2" }
            });
            AddText(list, PanelName + "_tab_tp", PanelName + "_tab_tp_t", "Players", 12, "MiddleCenter", "1 1 1 1", "0 0", "1 1", "2 2", "-2 -2");
            AddElem(list, PanelName + "_tab_tp_b", PanelName + "_tab_tp", new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = "teleportgui.cui mode.teleport", ["color"] = "0 0 0 0", ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
            });
            // Tab 2: Home (middle third) - button tab; highlight when active
            AddElem(list, PanelName + "_tab_home", PanelName + "_tabbar", new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = mode == "home" ? activeTabColor : tabColor },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0.333 0", ["anchormax"] = "0.666 1", ["offsetmin"] = "2 2", ["offsetmax"] = "-2 -2" }
            });
            AddText(list, PanelName + "_tab_home", PanelName + "_tab_home_t", "Home", 12, "MiddleCenter", "1 1 1 1", "0 0", "1 1", "2 2", "-2 -2");
            AddElem(list, PanelName + "_tab_home_b", PanelName + "_tab_home", new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = "teleportgui.cui mode.home", ["color"] = "0 0 0 0", ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
            });
            // Tab 3: Warp (right third) - button tab; highlight when active
            AddElem(list, PanelName + "_tab_warp", PanelName + "_tabbar", new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = mode == "warp" ? activeTabColor : tabColor },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0.666 0", ["anchormax"] = "1 1", ["offsetmin"] = "2 2", ["offsetmax"] = "-2 -2" }
            });
            AddText(list, PanelName + "_tab_warp", PanelName + "_tab_warp_t", "Warp", 12, "MiddleCenter", "1 1 1 1", "0 0", "1 1", "2 2", "-2 -2");
            AddElem(list, PanelName + "_tab_warp_b", PanelName + "_tab_warp", new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = "teleportgui.cui mode.warp", ["color"] = "0 0 0 0", ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
            });

            // --- Title bar (below tabs): full width, title left, Close top-right
            AddElem(list, PanelName + "_titlebar", PanelName, new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = headerColor },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "5 -58", ["offsetmax"] = "-5 -33" }
            });
            // Static title: one main panel "Teleport GUI"; tabs (Players / Home / Warp) switch the page inside it
            AddText(list, PanelName + "_titlebar", PanelName + "_title", "Teleport GUI", 14, "MiddleLeft", "1 1 1 1", "0 0.5", "1 0.5", "5 -12", "-35 12");
            // Close button: right side of title bar (anchor 1 0.5 = right center) - material/sprite for interactivity
            list.Add(new Dictionary<string, object>
            {
                ["name"] = PanelName + "_close_btn",
                ["parent"] = PanelName + "_titlebar",
                ["components"] = new List<object>
                {
                    new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = "teleportgui.cui close", ["color"] = closeColor, ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
                    new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "1 0.5", ["anchormax"] = "1 0.5", ["offsetmin"] = "-28 -12", ["offsetmax"] = "-5 12" }
                }
            });
            list.Add(new Dictionary<string, object>
            {
                ["name"] = PanelName + "_close_txt",
                ["parent"] = PanelName + "_close_btn",
                ["components"] = new List<object>
                {
                    new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Text", ["text"] = "X", ["fontSize"] = 14, ["align"] = "MiddleCenter", ["color"] = "1 1 1 1" },
                    new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
                }
            });

            // --- Header (prev, + add, search, next) - like plugin CreateHeaderBar
            AddElem(list, PanelName + "_header", PanelName, new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = headerColor },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "5 -93", ["offsetmax"] = "-5 -63" }
            });
            // Prev: left (anchor 0 0.5)
            list.Add(new Dictionary<string, object>
            {
                ["name"] = PanelName + "_prev_btn",
                ["parent"] = PanelName + "_header",
                ["components"] = new List<object>
                {
                    new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = "teleportgui.cui prev", ["color"] = buttonColor, ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
                    new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0.5", ["anchormax"] = "0 0.5", ["offsetmin"] = "5 -14", ["offsetmax"] = "38 14" }
                }
            });
            list.Add(new Dictionary<string, object> { ["name"] = PanelName + "_prev_txt", ["parent"] = PanelName + "_prev_btn", ["components"] = new List<object> { new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Text", ["text"] = "<", ["fontSize"] = 14, ["align"] = "MiddleCenter", ["color"] = "1 1 1 1" }, new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" } } });
            // + Add button (Home tab for all; Warp tab for admin only)
            if (mode == "home" || (mode == "warp" && isAdmin))
            {
                string addCmd = mode == "home" ? "teleportgui.cui addhome.open" : "teleportgui.cui addwarp.open";
                list.Add(new Dictionary<string, object>
                {
                    ["name"] = PanelName + "_add_btn",
                    ["parent"] = PanelName + "_header",
                    ["components"] = new List<object>
                    {
                        new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = addCmd, ["color"] = buttonColor, ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
                        new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0.5", ["anchormax"] = "0 0.5", ["offsetmin"] = "42 -14", ["offsetmax"] = "62 14" }
                    }
                });
                list.Add(new Dictionary<string, object> { ["name"] = PanelName + "_add_txt", ["parent"] = PanelName + "_add_btn", ["components"] = new List<object> { new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Text", ["text"] = "+", ["fontSize"] = 16, ["align"] = "MiddleCenter", ["color"] = "1 1 1 1" }, new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" } } });
            }
            // Search input: center of header (command fires on Enter with typed text)
            AddInputField(list, PanelName + "_header", PanelName + "_search", "teleportgui.cui search", searchString ?? "", "0.18 0", "1 0.98", "68 2", "-45 -2");
            // Next: right (anchor 1 0.5)
            list.Add(new Dictionary<string, object>
            {
                ["name"] = PanelName + "_next_btn",
                ["parent"] = PanelName + "_header",
                ["components"] = new List<object>
                {
                    new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = "teleportgui.cui next", ["color"] = buttonColor, ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
                    new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "1 0.5", ["anchormax"] = "1 0.5", ["offsetmin"] = "-38 -14", ["offsetmax"] = "-5 14" }
                }
            });
            list.Add(new Dictionary<string, object> { ["name"] = PanelName + "_next_txt", ["parent"] = PanelName + "_next_btn", ["components"] = new List<object> { new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Text", ["text"] = ">", ["fontSize"] = 14, ["align"] = "MiddleCenter", ["color"] = "1 1 1 1" }, new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" } } });

            // --- Content area (below header, fill rest). NeedsKeyboard for search input.
            var contentComps = new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = "0.1 0.1 0.12 0.95" },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1", ["offsetmin"] = "5 5", ["offsetmax"] = "-5 -98" },
                new Dictionary<string, object> { ["type"] = "NeedsKeyboard" }
            };
            AddElem(list, PanelName + "_content", PanelName, contentComps);

            float rowH = 32f;
            float y = 0f;

            // Daily limit / cooldown line at top of content (plugin-style)
            if (!string.IsNullOrEmpty(dailyLimitText))
            {
                AddElem(list, PanelName + "_limitbar", PanelName + "_content", Panel(headerColor, "0 1", "1 1", "2 " + (y - 20).ToString(), "-2 " + y.ToString()));
                AddText(list, PanelName + "_limitbar", PanelName + "_limitbar_txt", dailyLimitText, 11, "MiddleLeft", "1 1 1 1", "0 0", "1 1", "5 0", "-5 0");
                y -= 22;
            }

            if (mode == "teleport" && playerEntries != null)
            {
                for (int i = 0; i < playerEntries.Count; i++)
                {
                    var e = playerEntries[i];
                    string rowName = PanelName + "_row_tp_" + i;
                    string tpCmd = "teleportgui.cui tp." + e.UserId;
                    AddElem(list, rowName, PanelName + "_content", Panel("0.18 0.18 0.22 0.95", "0 1", "1 1", "2 " + (y - rowH).ToString(), "-2 " + y.ToString()));
                    // Full-row clickable: clicking the player (name or row) teleports
                    AddElem(list, rowName + "_click", rowName, new List<object>
                    {
                        new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = tpCmd, ["color"] = "0 0 0 0", ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
                        new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
                    });
                    AddText(list, rowName, rowName + "_lbl", e.DisplayName ?? e.UserId, 12, "MiddleLeft", "1 1 1 1", "0 0", "1 1", "5 2", "-95 -2");
                    AddButton(list, rowName, rowName + "_go", tpCmd, "TP To", 11, "1 1 1 1", "-90 -8", "-5 8", "0.5 0.5", "0.5 0.5", buttonColor);
                    y -= rowH + 2;
                }
            }
            else if (mode == "home" && homeEntries != null)
            {
                for (int i = 0; i < homeEntries.Count; i++)
                {
                    var e = homeEntries[i];
                    string safeName = (e.Name ?? "home").Replace(" ", "_");
                    string rowName = PanelName + "_row_home_" + i;
                    string homeCmd = "teleportgui.cui home." + safeName;
                    string deleteCmd = "teleportgui.cui deletehome." + safeName;
                    AddElem(list, rowName, PanelName + "_content", Panel("0.18 0.18 0.22 0.95", "0 1", "1 1", "2 " + (y - rowH).ToString(), "-2 " + y.ToString()));
                    AddElem(list, rowName + "_click", rowName, new List<object>
                    {
                        new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = homeCmd, ["color"] = "0 0 0 0", ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
                        new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
                    });
                    AddText(list, rowName, rowName + "_lbl", e.Name ?? "home", 12, "MiddleLeft", "1 1 1 1", "0 0", "1 1", "5 2", "-50 -2");
                    AddButton(list, rowName, rowName + "_del", deleteCmd, "X", 10, "0.9 0.3 0.3 1", "-48 -6", "-28 6", "0.5 0.5", "0.5 0.5", "0.25 0.15 0.15 0.95");
                    AddButton(list, rowName, rowName + "_go", homeCmd, "Go", 11, "1 1 1 1", "-25 -8", "-5 8", "0.5 0.5", "0.5 0.5", buttonColor);
                    y -= rowH + 2;
                }
            }
            else if (mode == "warp" && warpEntries != null)
            {
                for (int i = 0; i < warpEntries.Count; i++)
                {
                    var e = warpEntries[i];
                    string safeName = (e.Name ?? "warp").Replace(" ", "_");
                    string rowName = PanelName + "_row_warp_" + i;
                    string warpCmd = "teleportgui.cui warp." + safeName;
                    AddElem(list, rowName, PanelName + "_content", Panel("0.18 0.18 0.22 0.95", "0 1", "1 1", "2 " + (y - rowH).ToString(), "-2 " + y.ToString()));
                    AddElem(list, rowName + "_click", rowName, new List<object>
                    {
                        new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = warpCmd, ["color"] = "0 0 0 0", ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
                        new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
                    });
                    AddText(list, rowName, rowName + "_lbl", e.Name ?? "warp", 12, "MiddleLeft", "1 1 1 1", "0 0", "1 1", "5 2", isAdmin ? "-50 -2" : "-25 -2");
                    if (isAdmin)
                    {
                        string deleteCmd = "teleportgui.cui deletewarp." + safeName;
                        AddButton(list, rowName, rowName + "_del", deleteCmd, "X", 10, "0.9 0.3 0.3 1", "-48 -6", "-28 6", "0.5 0.5", "0.5 0.5", "0.25 0.15 0.15 0.95");
                    }
                    AddButton(list, rowName, rowName + "_go", warpCmd, "Go", 11, "1 1 1 1", "-25 -8", "-5 8", "0.5 0.5", "0.5 0.5", buttonColor);
                    y -= rowH + 2;
                }
            }

            if ((mode == "teleport" && (playerEntries == null || playerEntries.Count == 0)) ||
                (mode == "home" && (homeEntries == null || homeEntries.Count == 0)) ||
                (mode == "warp" && (warpEntries == null || warpEntries.Count == 0)))
                AddText(list, PanelName + "_content", PanelName + "_empty", "No entries.", 12, "MiddleCenter", "0.7 0.7 0.7 1", "0 0.5", "1 0.5", "5 -10", "-5 10");

            return JsonConvert.SerializeObject(list);
        }

        public class PlayerEntry { public string UserId; public string DisplayName; }
        public class HomeEntry { public string Name; }
        public class WarpEntry { public string Name; }

        public static void Show(BasePlayer player, string json)
        {
            if (player?.net?.connection == null || string.IsNullOrEmpty(json)) return;
            var ce = CommunityEntity.ServerInstance;
            if (ce == null || ce.IsDestroyed) return;
            try { ce.ClientRPC(RpcTarget.Player("AddUI", player.net.connection), json); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[TeleportGUI] AddUI failed: " + ex.Message); }
        }

        public static void Destroy(BasePlayer player)
        {
            if (player?.net?.connection == null) return;
            var ce = CommunityEntity.ServerInstance;
            if (ce == null || ce.IsDestroyed) return;
            try { ce.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), PanelName); }
            catch { }
        }

        /// <summary>Builds the "Create New Home" modal (replaces main UI). Same PanelName. Save/Cancel + input (Enter = addhome with name).</summary>
        public static string BuildCreateHomeModal(TeleportGUIConfig.UIOptions.UIColors uiColors = null)
        {
            var list = new List<object>();
            string bgColor = ToRgba(uiColors?.Background, "0.12 0.12 0.15 0.97");
            string panelColor = ToRgba(uiColors?.Panel, "0.18 0.18 0.22 0.98");
            string headerColor = ToRgba(uiColors?.Header, "0.22 0.22 0.28 1");
            string buttonColor = ToRgba(uiColors?.Button, "0.25 0.25 0.32 0.95");
            string closeColor = ToRgba(uiColors?.Close, "0.7 0.25 0.25 0.95");
            // Overlay
            AddElem(list, PanelName, Parent, new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = "0 0 0 0.7" },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" },
                new Dictionary<string, object> { ["type"] = "NeedsCursor" },
                new Dictionary<string, object> { ["type"] = "NeedsKeyboard" }
            });
            // Center panel
            AddElem(list, PanelName + "_modal", PanelName, new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = panelColor },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0.5 0.5", ["anchormax"] = "0.5 0.5", ["offsetmin"] = "-160 -60", ["offsetmax"] = "160 60" }
            });
            AddElem(list, PanelName + "_modal_header", PanelName + "_modal", new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = headerColor },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "0 -28", ["offsetmax"] = "0 0" }
            });
            AddText(list, PanelName + "_modal_header", PanelName + "_modal_title", "Create New Home", 14, "MiddleLeft", "1 1 1 1", "0 0", "1 1", "8 0", "-8 0");
            AddText(list, PanelName + "_modal", PanelName + "_modal_lbl", "Home Name", 12, "MiddleLeft", "1 1 1 1", "0 0.5", "0.35 0.5", "8 -8", "8 8");
            AddInputField(list, PanelName + "_modal", PanelName + "_modal_in", "teleportgui.cui addhome", "", "0.36 0.5", "1 0.5", "8 -10", "-8 10");
            AddButton(list, PanelName + "_modal", PanelName + "_modal_save", "teleportgui.cui addhome", "Save", 12, "1 1 1 1", "-155 -52", "-85 -32", "0.5 0.5", "0.5 0.5", buttonColor);
            AddButton(list, PanelName + "_modal", PanelName + "_modal_cancel", "teleportgui.cui addhome.cancel", "Cancel", 12, "1 1 1 1", "85 -52", "155 -32", "0.5 0.5", "0.5 0.5", closeColor);
            return JsonConvert.SerializeObject(list);
        }

        /// <summary>Builds the "Add Warp" modal (replaces main UI). Same PanelName.</summary>
        public static string BuildAddWarpModal(TeleportGUIConfig.UIOptions.UIColors uiColors = null)
        {
            var list = new List<object>();
            string panelColor = ToRgba(uiColors?.Panel, "0.18 0.18 0.22 0.98");
            string headerColor = ToRgba(uiColors?.Header, "0.22 0.22 0.28 1");
            string buttonColor = ToRgba(uiColors?.Button, "0.25 0.25 0.32 0.95");
            string closeColor = ToRgba(uiColors?.Close, "0.7 0.25 0.25 0.95");
            AddElem(list, PanelName, Parent, new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = "0 0 0 0.7" },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" },
                new Dictionary<string, object> { ["type"] = "NeedsCursor" },
                new Dictionary<string, object> { ["type"] = "NeedsKeyboard" }
            });
            AddElem(list, PanelName + "_modal", PanelName, new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = panelColor },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0.5 0.5", ["anchormax"] = "0.5 0.5", ["offsetmin"] = "-160 -60", ["offsetmax"] = "160 60" }
            });
            AddElem(list, PanelName + "_modal_header", PanelName + "_modal", new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = headerColor },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "0 -28", ["offsetmax"] = "0 0" }
            });
            AddText(list, PanelName + "_modal_header", PanelName + "_modal_title", "Add Warp", 14, "MiddleLeft", "1 1 1 1", "0 0", "1 1", "8 0", "-8 0");
            AddText(list, PanelName + "_modal", PanelName + "_modal_lbl", "Warp Name", 12, "MiddleLeft", "1 1 1 1", "0 0.5", "0.35 0.5", "8 -8", "8 8");
            AddInputField(list, PanelName + "_modal", PanelName + "_modal_in", "teleportgui.cui addwarp", "", "0.36 0.5", "1 0.5", "8 -10", "-8 10");
            AddButton(list, PanelName + "_modal", PanelName + "_modal_save", "teleportgui.cui addwarp", "Save", 12, "1 1 1 1", "-155 -52", "-85 -32", "0.5 0.5", "0.5 0.5", buttonColor);
            AddButton(list, PanelName + "_modal", PanelName + "_modal_cancel", "teleportgui.cui addwarp.cancel", "Cancel", 12, "1 1 1 1", "85 -52", "155 -32", "0.5 0.5", "0.5 0.5", closeColor);
            return JsonConvert.SerializeObject(list);
        }
    }
}
