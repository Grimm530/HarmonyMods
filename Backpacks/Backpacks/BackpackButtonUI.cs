using System.Collections.Generic;
using Newtonsoft.Json;
using Network;
using UnityEngine;

namespace Backpacks
{
    /// <summary>
    /// CUI for the on-screen backpack button (image + click to open). Uses CommunityEntity AddUI/DestroyUI.
    /// </summary>
    public static class BackpackButtonUI
    {
        public const string PanelName = "BackpacksButton";

        private const string Parent = "Hud.Menu";
        private const int FallbackItemId = 1400460850; // Saddle bag / backpack icon

        /// <summary>
        /// Build CUI JSON: one panel with image (png id, url, or item icon) and transparent button with command "backpack".
        /// Position matches Oxide Backpacks: anchor 0.5 0.0 (center bottom) + offsetmin/offsetmax in pixels for small button.
        /// </summary>
        public static string BuildJson(string anchorsMin, string anchorsMax, string offsetMin, string offsetMax, bool useUrl, string url, bool usePngId, uint pngId)
        {
            var list = new List<object>();

            var rect = new Dictionary<string, object>
            {
                ["type"] = "RectTransform",
                ["anchormin"] = anchorsMin,
                ["anchormax"] = anchorsMax
            };
            if (!string.IsNullOrWhiteSpace(offsetMin)) rect["offsetmin"] = offsetMin;
            if (!string.IsNullOrWhiteSpace(offsetMax)) rect["offsetmax"] = offsetMax;

            var panelComps = new List<object> { rect };

            list.Add(new Dictionary<string, object>
            {
                ["name"] = PanelName,
                ["parent"] = Parent,
                ["destroyUi"] = PanelName,
                ["components"] = panelComps
            });

            var imageComps = new List<object>
            {
                new Dictionary<string, object> { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
            };

            if (useUrl && !string.IsNullOrWhiteSpace(url))
            {
                imageComps.Insert(0, new Dictionary<string, object>
                {
                    ["type"] = "UnityEngine.UI.RawImage",
                    ["url"] = url.Trim(),
                    ["color"] = "1 1 1 1"
                });
            }
            else if (usePngId && pngId != 0)
            {
                imageComps.Insert(0, new Dictionary<string, object>
                {
                    ["type"] = "UnityEngine.UI.RawImage",
                    ["png"] = pngId.ToString(),
                    ["color"] = "1 1 1 1"
                });
            }
            else
            {
                imageComps.Insert(0, new Dictionary<string, object>
                {
                    ["type"] = "UnityEngine.UI.Image",
                    ["itemid"] = FallbackItemId,
                    ["color"] = "1 1 1 1"
                });
            }

            list.Add(new Dictionary<string, object>
            {
                ["name"] = PanelName + "_Image",
                ["parent"] = PanelName,
                ["components"] = imageComps
            });

            list.Add(new Dictionary<string, object>
            {
                ["name"] = PanelName + "_Btn",
                ["parent"] = PanelName,
                ["components"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["type"] = "UnityEngine.UI.Button",
                        ["command"] = "backpack",
                        ["color"] = "0 0 0 0"
                    },
                    new Dictionary<string, object>
                    {
                        ["type"] = "RectTransform",
                        ["anchormin"] = "0 0",
                        ["anchormax"] = "1 1"
                    }
                }
            });

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
                UnityEngine.Debug.LogWarning("[Backpacks] AddUI failed: " + ex.Message);
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
