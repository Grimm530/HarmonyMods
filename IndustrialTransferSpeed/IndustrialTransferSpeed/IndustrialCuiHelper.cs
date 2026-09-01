using System.Collections.Generic;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace IndustrialTransferSpeed
{
    public static class IndustrialCuiHelper
    {
        private const string PanelMaterial = "assets/content/ui/namefontmaterial.mat";
        private const string PanelSprite = "assets/content/ui/ui.background.tile.psd";

        public static void AddUi(BasePlayer player, List<JObject> elements)
        {
            AddUi(player, JsonConvert.SerializeObject(elements));
        }

        public static void AddUi(BasePlayer player, string json)
        {
            if (player?.net?.connection == null || string.IsNullOrEmpty(json))
            {
                return;
            }

            CommunityEntity communityEntity = GetCommunityEntity();
            if (communityEntity != null)
            {
                communityEntity.ClientRPC(RpcTarget.Player("AddUI", player.net.connection), json);
            }
        }

        public static void DestroyUi(BasePlayer player, string name)
        {
            if (player?.net?.connection == null || string.IsNullOrEmpty(name))
            {
                return;
            }

            CommunityEntity communityEntity = GetCommunityEntity();
            if (communityEntity != null)
            {
                communityEntity.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), name);
            }
        }

        public static JObject Panel(string name, string parent, string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax, bool needsCursor = false)
        {
            JArray components = new JArray
            {
                new JObject
                {
                    ["type"] = "UnityEngine.UI.Image",
                    ["color"] = color,
                    ["material"] = PanelMaterial,
                    ["sprite"] = PanelSprite
                },
                RectTransform(anchorMin, anchorMax, offsetMin, offsetMax)
            };

            if (needsCursor)
            {
                components.Add(new JObject { ["type"] = "NeedsCursor" });
            }

            return new JObject
            {
                ["name"] = name,
                ["parent"] = parent,
                ["components"] = components
            };
        }

        public static JObject Label(string name, string parent, string text, int fontSize, string anchorMin, string anchorMax, string color = "1 1 1 0.8", string align = "MiddleCenter")
        {
            return new JObject
            {
                ["name"] = name,
                ["parent"] = parent,
                ["components"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "UnityEngine.UI.Text",
                        ["text"] = text,
                        ["fontSize"] = fontSize,
                        ["color"] = color,
                        ["align"] = align
                    },
                    RectTransform(anchorMin, anchorMax)
                }
            };
        }

        public static List<JObject> Button(string name, string parent, string color, string text, int fontSize, string anchorMin, string anchorMax, string command)
        {
            List<JObject> elements = new List<JObject>
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
                            ["material"] = PanelMaterial,
                            ["sprite"] = PanelSprite
                        },
                        RectTransform(anchorMin, anchorMax)
                    }
                }
            };

            if (!string.IsNullOrEmpty(text))
            {
                elements.Add(Label(name + ".Label", name, text, fontSize, "0 0", "1 1", "1 1 1 0.9"));
            }

            return elements;
        }

        private static JObject RectTransform(string anchorMin, string anchorMax, string offsetMin = null, string offsetMax = null)
        {
            JObject component = new JObject
            {
                ["type"] = "RectTransform",
                ["anchormin"] = anchorMin,
                ["anchormax"] = anchorMax
            };

            if (offsetMin != null)
            {
                component["offsetmin"] = offsetMin;
            }

            if (offsetMax != null)
            {
                component["offsetmax"] = offsetMax;
            }

            return component;
        }

        private static CommunityEntity GetCommunityEntity()
        {
            CommunityEntity serverInstance = CommunityEntity.ServerInstance;
            if (serverInstance != null && !serverInstance.IsDestroyed)
            {
                return serverInstance;
            }

            if (BaseNetworkable.serverEntities == null)
            {
                return null;
            }

            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is CommunityEntity communityEntity && communityEntity != null && !communityEntity.IsDestroyed)
                {
                    return communityEntity;
                }
            }

            return null;
        }
    }
}
