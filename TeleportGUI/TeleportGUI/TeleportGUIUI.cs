using System.Collections.Generic;
using System.Globalization;
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
        private const string Carrier = "cui.endtest TELEPORTGUI ";

        private static string Cmd(string action)
        {
            return Carrier + (action ?? string.Empty);
        }

        private static string F(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

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
                    new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = Cmd(command), ["color"] = bg, ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
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
                    ["command"] = Cmd(command),
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
            return BuildUI(player, mode, page, searchString, playerEntries, homeEntries, warpEntries,
                hasNextPage, perPage, isAdmin, dailyLimitText, uiColors, new UIAccess
                {
                    CanTeleport = true,
                    CanHome = true,
                    CanWarp = true,
                    CanAddHome = true,
                    CanAddWarp = isAdmin,
                    CanDeleteWarps = isAdmin
                });
        }

        /// <summary>
        /// Builds the main UI using permission/state flags prepared by the core. The UI never
        /// evaluates permissions itself, which keeps visibility deterministic and testable.
        /// </summary>
        public static string BuildUI(BasePlayer player, string mode, int page, string searchString,
            List<PlayerEntry> playerEntries,
            List<HomeEntry> homeEntries,
            List<WarpEntry> warpEntries,
            bool hasNextPage,
            int perPage,
            bool isAdmin,
            string dailyLimitText,
            TeleportGUIConfig.UIOptions.UIColors uiColors,
            UIAccess access)
        {
            access = access ?? new UIAccess();
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
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = mode == "teleport" ? activeTabColor : (access.CanTeleport ? tabColor : "0.12 0.12 0.14 0.8") },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "0.333 1", ["offsetmin"] = "2 2", ["offsetmax"] = "-2 -2" }
            });
            AddText(list, PanelName + "_tab_tp", PanelName + "_tab_tp_t", "Teleport", 12, "MiddleCenter", access.CanTeleport ? "1 1 1 1" : "1 1 1 0.2", "0 0", "1 1", "2 2", "-2 -2");
            if (access.CanTeleport) AddElem(list, PanelName + "_tab_tp_b", PanelName + "_tab_tp", new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = Cmd("mode.teleport"), ["color"] = "0 0 0 0", ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
            });
            // Tab 2: Home (middle third) - button tab; highlight when active
            AddElem(list, PanelName + "_tab_home", PanelName + "_tabbar", new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = mode == "home" ? activeTabColor : (access.CanHome ? tabColor : "0.12 0.12 0.14 0.8") },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0.333 0", ["anchormax"] = "0.666 1", ["offsetmin"] = "2 2", ["offsetmax"] = "-2 -2" }
            });
            AddText(list, PanelName + "_tab_home", PanelName + "_tab_home_t", "Homes", 12, "MiddleCenter", access.CanHome ? "1 1 1 1" : "1 1 1 0.2", "0 0", "1 1", "2 2", "-2 -2");
            if (access.CanHome) AddElem(list, PanelName + "_tab_home_b", PanelName + "_tab_home", new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = Cmd("mode.home"), ["color"] = "0 0 0 0", ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
            });
            // Tab 3: Warp (right third) - button tab; highlight when active
            AddElem(list, PanelName + "_tab_warp", PanelName + "_tabbar", new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = mode == "warp" ? activeTabColor : (access.CanWarp ? tabColor : "0.12 0.12 0.14 0.8") },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0.666 0", ["anchormax"] = "1 1", ["offsetmin"] = "2 2", ["offsetmax"] = "-2 -2" }
            });
            AddText(list, PanelName + "_tab_warp", PanelName + "_tab_warp_t", "Warps", 12, "MiddleCenter", access.CanWarp ? "1 1 1 1" : "1 1 1 0.2", "0 0", "1 1", "2 2", "-2 -2");
            if (access.CanWarp) AddElem(list, PanelName + "_tab_warp_b", PanelName + "_tab_warp", new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = Cmd("mode.warp"), ["color"] = "0 0 0 0", ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
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
                    new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = Cmd("close"), ["color"] = closeColor, ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
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
            if (page > 0) list.Add(new Dictionary<string, object>
            {
                ["name"] = PanelName + "_prev_btn",
                ["parent"] = PanelName + "_header",
                ["components"] = new List<object>
                {
                    new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = Cmd("prev"), ["color"] = buttonColor, ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
                    new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0.5", ["anchormax"] = "0 0.5", ["offsetmin"] = "5 -14", ["offsetmax"] = "38 14" }
                }
            });
            if (page > 0) list.Add(new Dictionary<string, object> { ["name"] = PanelName + "_prev_txt", ["parent"] = PanelName + "_prev_btn", ["components"] = new List<object> { new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Text", ["text"] = "<", ["fontSize"] = 14, ["align"] = "MiddleCenter", ["color"] = "1 1 1 1" }, new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" } } });
            // + Add button (Home tab for all; Warp tab for admin only)
            if ((mode == "home" && access.CanAddHome) || (mode == "warp" && access.CanAddWarp))
            {
                string addCmd = mode == "home" ? "addhome.open" : "addwarp.open";
                list.Add(new Dictionary<string, object>
                {
                    ["name"] = PanelName + "_add_btn",
                    ["parent"] = PanelName + "_header",
                    ["components"] = new List<object>
                    {
                        new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = Cmd(addCmd), ["color"] = buttonColor, ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
                        new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0.5", ["anchormax"] = "0 0.5", ["offsetmin"] = "42 -14", ["offsetmax"] = "62 14" }
                    }
                });
                list.Add(new Dictionary<string, object> { ["name"] = PanelName + "_add_txt", ["parent"] = PanelName + "_add_btn", ["components"] = new List<object> { new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Text", ["text"] = "+", ["fontSize"] = 16, ["align"] = "MiddleCenter", ["color"] = "1 1 1 1" }, new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" } } });
            }
            // Search input: center of header (command fires on Enter with typed text)
            AddInputField(list, PanelName + "_header", PanelName + "_search", "search", searchString ?? "", "0.18 0", "1 0.98", "68 2", "-45 -2");
            if (mode == "teleport" && access.CanOpenSettings)
                AddButton(list, PanelName + "_header", PanelName + "_settings", "settings.open", "⚙", 13, "1 1 1 1", "42 -14", "64 14", "0 0.5", "0 0.5", buttonColor);
            // Next: right (anchor 1 0.5)
            if (hasNextPage) list.Add(new Dictionary<string, object>
            {
                ["name"] = PanelName + "_next_btn",
                ["parent"] = PanelName + "_header",
                ["components"] = new List<object>
                {
                    new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = Cmd("next"), ["color"] = buttonColor, ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
                    new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "1 0.5", ["anchormax"] = "1 0.5", ["offsetmin"] = "-38 -14", ["offsetmax"] = "-5 14" }
                }
            });
            if (hasNextPage) list.Add(new Dictionary<string, object> { ["name"] = PanelName + "_next_txt", ["parent"] = PanelName + "_next_btn", ["components"] = new List<object> { new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Text", ["text"] = ">", ["fontSize"] = 14, ["align"] = "MiddleCenter", ["color"] = "1 1 1 1" }, new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" } } });

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
                AddElem(list, PanelName + "_limitbar", PanelName + "_content", Panel(headerColor, "0 1", "1 1", "2 " + F(y - 20), "-2 " + F(y)));
                AddText(list, PanelName + "_limitbar", PanelName + "_limitbar_txt", dailyLimitText, 11, "MiddleLeft", "1 1 1 1", "0 0", "1 1", "5 0", "-5 0");
                y -= 22;
            }

            if (mode == "teleport" && playerEntries != null)
            {
                for (int i = 0; i < playerEntries.Count; i++)
                {
                    var e = playerEntries[i];
                    string rowName = PanelName + "_row_tp_" + i;
                    string tpCmd = "tpr." + e.UserId;
                    string hereCmd = "tphere." + e.UserId;
                    bool canRequest = e.CanRequest != false;
                    bool canHere = access.CanTeleportHere && e.CanTeleportHere != false;
                    AddElem(list, rowName, PanelName + "_content", Panel("0.18 0.18 0.22 0.95", "0 1", "1 1", "2 " + F(y - rowH), "-2 " + F(y)));
                    AddText(list, rowName, rowName + "_lbl", (e.DisplayName ?? e.UserId) + (e.IsSleeper ? " (sleeping)" : ""), 12, "MiddleLeft",
                        canRequest ? "1 1 1 1" : "1 1 1 0.25", "0 0", "1 1", "5 2", canHere ? "-92 -2" : "-47 -2");
                    if (canHere)
                        AddButton(list, rowName, rowName + "_here", hereCmd, "HERE", 10, "1 1 1 1", "-86 -8", "-46 8", "1 0.5", "1 0.5", buttonColor);
                    if (canRequest)
                        AddButton(list, rowName, rowName + "_go", tpCmd, "TPR", 10, "1 1 1 1", "-41 -8", "-1 8", "1 0.5", "1 0.5", buttonColor);
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
                    string homeCmd = "home." + safeName;
                    string deleteCmd = "deletehome." + safeName;
                    bool canGo = e.CanGo != false;
                    bool canDelete = e.CanDelete != false;
                    AddElem(list, rowName, PanelName + "_content", Panel("0.18 0.18 0.22 0.95", "0 1", "1 1", "2 " + F(y - rowH), "-2 " + F(y)));
                    AddText(list, rowName, rowName + "_lbl", e.Name ?? "home", 12, "MiddleLeft", canGo ? "1 1 1 1" : "1 1 1 0.25", "0 0", "1 1", "5 2", "-78 -2");
                    if (canDelete) AddButton(list, rowName, rowName + "_del", deleteCmd, "X", 10, "1 1 1 1", "-72 -8", "-47 8", "1 0.5", "1 0.5", closeColor);
                    if (canGo) AddButton(list, rowName, rowName + "_go", homeCmd, "GO", 10, "1 1 1 1", "-42 -8", "-2 8", "1 0.5", "1 0.5", buttonColor);
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
                    string warpCmd = "warp." + safeName;
                    bool canWarp = e.HasPermission != false;
                    bool canDelete = access.CanDeleteWarps && e.CanDelete != false;
                    AddElem(list, rowName, PanelName + "_content", Panel("0.18 0.18 0.22 0.95", "0 1", "1 1", "2 " + F(y - rowH), "-2 " + F(y)));
                    AddText(list, rowName, rowName + "_lbl", e.Name ?? "warp", 12, "MiddleLeft", canWarp ? "1 1 1 1" : "1 1 1 0.25", "0 0", "1 1", "5 2", "-78 -2");
                    if (canDelete)
                    {
                        string deleteCmd = "deletewarp." + safeName;
                        AddButton(list, rowName, rowName + "_del", deleteCmd, "X", 10, "1 1 1 1", "-72 -8", "-47 8", "1 0.5", "1 0.5", closeColor);
                    }
                    if (canWarp) AddButton(list, rowName, rowName + "_go", warpCmd, "GO", 10, "1 1 1 1", "-42 -8", "-2 8", "1 0.5", "1 0.5", buttonColor);
                    y -= rowH + 2;
                }
            }

            if ((mode == "teleport" && (playerEntries == null || playerEntries.Count == 0)) ||
                (mode == "home" && (homeEntries == null || homeEntries.Count == 0)) ||
                (mode == "warp" && (warpEntries == null || warpEntries.Count == 0)))
                AddText(list, PanelName + "_content", PanelName + "_empty", "No entries.", 12, "MiddleCenter", "0.7 0.7 0.7 1", "0 0.5", "1 0.5", "5 -10", "-5 10");

            return JsonConvert.SerializeObject(list);
        }

        public class UIAccess
        {
            public bool CanTeleport;
            public bool CanHome;
            public bool CanWarp;
            public bool CanTeleportHere;
            public bool CanOpenSettings;
            public bool CanToggleAutoAccept;
            public bool CanToggleSleepers;
            public bool CanAddHome;
            public bool CanAddWarp;
            public bool CanDeleteWarps;
        }

        public class PlayerEntry
        {
            public string UserId;
            public string DisplayName;
            public bool IsSleeper;
            public bool? CanRequest;
            public bool? CanTeleportHere;
        }

        public class HomeEntry
        {
            public string Name;
            public bool? CanGo;
            public bool? CanDelete;
        }

        public class WarpEntry
        {
            public string Name;
            public bool? HasPermission;
            public bool? CanDelete;
        }

        public class TeleportSettings
        {
            public bool AutoAcceptClans;
            public bool AutoAcceptFriends;
            public bool AutoAcceptTeams;
            public bool AutoAcceptAll;
            public bool ShowSleepers;
            public bool CanToggleAutoAccept;
            public bool CanToggleSleepers;
        }

        public class WarpForm
        {
            public string Name;
            public string Permission;
            public string Command;
        }

        public class RequestPopup
        {
            public string PanelName = "teleportrequest.ui.popup";
            public string Heading = "Teleport request expires in %TIME_LEFT%";
            public string DisplayName;
            public int SecondsRemaining;
            public bool CanAccept;
            public bool CanDecline;
            public string AcceptAction = "popup.accept";
            public string DeclineAction = "popup.decline";
            public string TimeoutAction = "popup.timeout";
            public float PaddingLeft;
            public float PaddingRight;
        }

        /// <summary>Builds the auto-accept/sleeper settings modal.</summary>
        public static string BuildSettingsModal(TeleportSettings settings,
            TeleportGUIConfig.UIOptions.UIColors uiColors = null)
        {
            settings = settings ?? new TeleportSettings();
            var list = new List<object>();
            string panelColor = ToRgba(uiColors?.Panel, "0.18 0.18 0.22 0.98");
            string headerColor = ToRgba(uiColors?.Header, "0.22 0.22 0.28 1");
            string buttonColor = ToRgba(uiColors?.Button, "0.25 0.25 0.32 0.95");
            string closeColor = ToRgba(uiColors?.Close, "0.7 0.25 0.25 0.95");
            string highlightColor = ToRgba(uiColors?.Highlight, "0.63 0.13 0.94 1");

            AddElem(list, PanelName, "Overlay", new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = "0 0 0 0.7" },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" },
                new Dictionary<string, object> { ["type"] = "NeedsCursor" }
            }, PanelName);
            AddElem(list, PanelName + "_settings", PanelName, Panel(panelColor, "0.5 0.5", "0.5 0.5", "-110 -92", "110 92"));
            AddElem(list, PanelName + "_settings_header", PanelName + "_settings", Panel(headerColor, "0 1", "1 1", "0 -25", "0 0"));
            AddText(list, PanelName + "_settings_header", PanelName + "_settings_title", "Teleport Settings", 13, "MiddleCenter", "1 1 1 1", "0 0", "1 1", "5 0", "-5 0");

            AddToggle(list, PanelName + "_settings", "aaclan", "Auto accept clan", settings.AutoAcceptClans,
                settings.CanToggleAutoAccept, -31, buttonColor, highlightColor);
            AddToggle(list, PanelName + "_settings", "aafriend", "Auto accept friends", settings.AutoAcceptFriends,
                settings.CanToggleAutoAccept, -57, buttonColor, highlightColor);
            AddToggle(list, PanelName + "_settings", "aateam", "Auto accept team", settings.AutoAcceptTeams,
                settings.CanToggleAutoAccept, -83, buttonColor, highlightColor);
            AddToggle(list, PanelName + "_settings", "aaall", "Auto accept everyone", settings.AutoAcceptAll,
                settings.CanToggleAutoAccept, -109, buttonColor, highlightColor);
            AddToggle(list, PanelName + "_settings", "sleepers", "Show sleepers", settings.ShowSleepers,
                settings.CanToggleSleepers, -135, buttonColor, highlightColor);
            AddButton(list, PanelName + "_settings", PanelName + "_settings_close", "settings.close", "Close", 12,
                "1 1 1 1", "5 5", "-5 27", "0 0", "1 0", closeColor);
            return JsonConvert.SerializeObject(list);
        }

        private static void AddToggle(List<object> list, string parent, string action, string label, bool active,
            bool enabled, float top, string buttonColor, string highlightColor)
        {
            string name = PanelName + "_toggle_" + action;
            AddElem(list, name, parent, Panel(enabled ? buttonColor : "0.15 0.15 0.17 0.8", "0 1", "0 1",
                "7 " + F(top - 20), "27 " + F(top)));
            if (active)
                AddElem(list, name + "_active", name, Panel(highlightColor, "0 0", "1 1", "5 5", "-5 -5"));
            if (enabled)
                AddElem(list, name + "_button", name, new List<object>
                {
                    new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Button", ["command"] = Cmd("settings." + action), ["color"] = "0 0 0 0", ["material"] = ButtonMaterial, ["sprite"] = ButtonSprite },
                    new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
                });
            AddText(list, parent, name + "_label", label, 12, "MiddleLeft", enabled ? "1 1 1 1" : "1 1 1 0.2",
                "0 1", "1 1", "35 " + F(top - 20), "-5 " + F(top));
        }

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
            Destroy(player, PanelName);
        }

        public static void Destroy(BasePlayer player, string panelName)
        {
            if (player?.net?.connection == null) return;
            var ce = CommunityEntity.ServerInstance;
            if (ce == null || ce.IsDestroyed) return;
            try { ce.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), panelName); }
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
            AddInputField(list, PanelName + "_modal", PanelName + "_modal_in", "addhome", "", "0.36 0.5", "1 0.5", "8 -10", "-8 10");
            AddButton(list, PanelName + "_modal", PanelName + "_modal_save", "addhome", "Save", 12, "1 1 1 1", "-155 -52", "-85 -32", "0.5 0.5", "0.5 0.5", buttonColor);
            AddButton(list, PanelName + "_modal", PanelName + "_modal_cancel", "addhome.cancel", "Cancel", 12, "1 1 1 1", "85 -52", "155 -32", "0.5 0.5", "0.5 0.5", closeColor);
            return JsonConvert.SerializeObject(list);
        }

        /// <summary>Builds the "Add Warp" modal (replaces main UI). Same PanelName.</summary>
        public static string BuildAddWarpModal(TeleportGUIConfig.UIOptions.UIColors uiColors = null)
        {
            return BuildAddWarpModal(new WarpForm(), uiColors);
        }

        /// <summary>Builds the three-field warp editor used by the reference UI.</summary>
        public static string BuildAddWarpModal(WarpForm form, TeleportGUIConfig.UIOptions.UIColors uiColors = null)
        {
            form = form ?? new WarpForm();
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
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0.5 0.5", ["anchormax"] = "0.5 0.5", ["offsetmin"] = "-170 -82", ["offsetmax"] = "170 82" }
            });
            AddElem(list, PanelName + "_modal_header", PanelName + "_modal", new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = headerColor },
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 1", ["anchormax"] = "1 1", ["offsetmin"] = "0 -28", ["offsetmax"] = "0 0" }
            });
            AddText(list, PanelName + "_modal_header", PanelName + "_modal_title", "Add Warp", 14, "MiddleLeft", "1 1 1 1", "0 0", "1 1", "8 0", "-8 0");
            AddText(list, PanelName + "_modal", PanelName + "_warp_name_lbl", "Warp Name", 12, "MiddleLeft", "1 1 1 1", "0 1", "0 1", "8 -55", "82 -35");
            AddInputField(list, PanelName + "_modal", PanelName + "_warp_name", "warpfield.name", form.Name ?? "", "0 1", "1 1", "82 -55", "-8 -35");
            AddText(list, PanelName + "_modal", PanelName + "_warp_perm_lbl", "Permission", 12, "MiddleLeft", "1 1 1 1", "0 1", "0 1", "8 -82", "82 -62");
            AddText(list, PanelName + "_modal", PanelName + "_warp_perm_prefix", "teleportgui.", 11, "MiddleLeft", "1 1 1 0.5", "0 1", "0 1", "87 -82", "153 -62");
            AddInputField(list, PanelName + "_modal", PanelName + "_warp_perm", "warpfield.permission", form.Permission ?? "", "0 1", "1 1", "153 -82", "-8 -62");
            AddText(list, PanelName + "_modal", PanelName + "_warp_cmd_lbl", "Command", 12, "MiddleLeft", "1 1 1 1", "0 1", "0 1", "8 -109", "82 -89");
            AddText(list, PanelName + "_modal", PanelName + "_warp_cmd_prefix", "/", 11, "MiddleLeft", "1 1 1 0.5", "0 1", "0 1", "87 -109", "98 -89");
            AddInputField(list, PanelName + "_modal", PanelName + "_warp_cmd", "warpfield.command", form.Command ?? "", "0 1", "1 1", "98 -109", "-8 -89");
            AddButton(list, PanelName + "_modal", PanelName + "_modal_save", "addwarp.save", "Save", 12, "1 1 1 1", "5 7", "82 29", "0 0", "0 0", buttonColor);
            AddButton(list, PanelName + "_modal", PanelName + "_modal_cancel", "addwarp.cancel", "Cancel", 12, "1 1 1 1", "-82 7", "-5 29", "1 0", "1 0", closeColor);
            return JsonConvert.SerializeObject(list);
        }

        /// <summary>
        /// Builds an incoming or outgoing teleport request popup. The native Countdown component
        /// updates %TIME_LEFT% client-side and sends the carrier timeout action when it reaches zero.
        /// </summary>
        public static string BuildRequestPopup(RequestPopup popup,
            TeleportGUIConfig.UIOptions.RequestPopupOptions popupOptions = null,
            TeleportGUIConfig.UIOptions.UIColors uiColors = null)
        {
            popup = popup ?? new RequestPopup();
            popupOptions = popupOptions ?? new TeleportGUIConfig.UIOptions.RequestPopupOptions();
            string panelName = string.IsNullOrWhiteSpace(popup.PanelName) ? "teleportrequest.ui.popup" : popup.PanelName;
            string anchorMin;
            string anchorMax;
            GetAnchor(popupOptions.Anchor.ToString(), out anchorMin, out anchorMax);
            var offset = popupOptions.Offset ?? new TeleportGUIConfig.UIOptions.RequestPopupOptions.UIOffset(-137.5f, -22.5f, 12.5f, 22.5f);
            var configuredPadding = popupOptions.Padding ?? new TeleportGUIConfig.UIOptions.RequestPopupOptions.HorizontalPadding();
            float paddingLeft = configuredPadding.Left + popup.PaddingLeft;
            float paddingRight = configuredPadding.Right + popup.PaddingRight;
            var list = new List<object>();
            string bgColor = ToRgba(uiColors?.Background, "0.12 0.12 0.15 0.97");
            string panelColor = ToRgba(uiColors?.Panel, "0.18 0.18 0.22 0.98");
            string headerColor = ToRgba(uiColors?.Header, "0.22 0.22 0.28 1");
            string closeColor = ToRgba(uiColors?.Close, "0.7 0.25 0.25 0.95");
            string highlightColor = ToRgba(uiColors?.Highlight, "0.63 0.13 0.94 1");

            AddElem(list, panelName, "Hud", new List<object>
            {
                new Dictionary<string, object> { ["type"] = "UnityEngine.UI.Image", ["color"] = bgColor, ["fadeIn"] = 0.25f },
                new Dictionary<string, object>
                {
                    ["type"] = "RectTransform", ["anchormin"] = anchorMin, ["anchormax"] = anchorMax,
                    ["offsetmin"] = F(offset.XMin) + " " + F(offset.YMin),
                    ["offsetmax"] = F(offset.XMax) + " " + F(offset.YMax)
                }
            }, panelName);
            AddElem(list, panelName + "_contents", panelName, Panel(panelColor, "0 0", "1 1", "5 5", "-5 -5"));
            AddElem(list, panelName + "_header", panelName + "_contents", Panel(headerColor, "0 1", "1 1", "0 -15", "0 0"));
            AddElem(list, panelName + "_countdown", panelName + "_header", new List<object>
            {
                new Dictionary<string, object>
                {
                    ["type"] = "UnityEngine.UI.Text", ["text"] = popup.Heading ?? "Teleport request expires in %TIME_LEFT%",
                    ["fontSize"] = 12, ["align"] = "MiddleCenter", ["color"] = "1 1 1 1"
                },
                new Dictionary<string, object>
                {
                    ["type"] = "Countdown", ["startTime"] = System.Math.Max(0, popup.SecondsRemaining), ["endTime"] = 0,
                    ["step"] = 1, ["interval"] = 1, ["timerFormat"] = "MinutesSeconds",
                    ["destroyIfDone"] = true, ["command"] = Cmd(popup.TimeoutAction)
                },
                new Dictionary<string, object>
                {
                    ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1",
                    ["offsetmin"] = F(paddingLeft) + " 0", ["offsetmax"] = F(-paddingRight) + " 0"
                }
            });
            AddText(list, panelName + "_contents", panelName + "_name", popup.DisplayName ?? "", 12, "MiddleLeft", "1 1 1 1",
                "0 0", "1 1", F(5 + paddingLeft) + " 0", F(-42 - paddingRight) + " -15");
            if (popup.CanAccept)
                AddButton(list, panelName + "_contents", panelName + "_accept", popup.AcceptAction, "✔", 10, "1 1 1 1",
                    F(-38 - paddingRight) + " -15", F(-22 - paddingRight) + " 0", "1 0.5", "1 0.5", highlightColor);
            if (popup.CanDecline)
                AddButton(list, panelName + "_contents", panelName + "_decline", popup.DeclineAction, "✘", 12, "1 1 1 1",
                    F(-18 - paddingRight) + " -15", F(-2 - paddingRight) + " 0", "1 0.5", "1 0.5", closeColor);
            return JsonConvert.SerializeObject(list);
        }

        private static void GetAnchor(string anchor, out string min, out string max)
        {
            switch ((anchor ?? "CenterRight").Trim().ToLowerInvariant())
            {
                case "topleft": min = max = "0 1"; break;
                case "topcenter": min = max = "0.5 1"; break;
                case "topright": min = max = "1 1"; break;
                case "centerleft": min = max = "0 0.5"; break;
                case "center": min = max = "0.5 0.5"; break;
                case "bottomleft": min = max = "0 0"; break;
                case "bottomcenter": min = max = "0.5 0"; break;
                case "bottomright": min = max = "1 0"; break;
                case "fullstretch": min = "0 0"; max = "1 1"; break;
                case "topstretch": min = "0 1"; max = "1 1"; break;
                case "horizontalcenterstretch": min = "0 0.5"; max = "1 0.5"; break;
                case "bottomstretch": min = "0 0"; max = "1 0"; break;
                case "leftstretch": min = "0 0"; max = "0 1"; break;
                case "verticalcenterstretch": min = "0.5 0"; max = "0.5 1"; break;
                case "rightstretch": min = "1 0"; max = "1 1"; break;
                default: min = max = "1 0.5"; break;
            }
        }
    }
}
