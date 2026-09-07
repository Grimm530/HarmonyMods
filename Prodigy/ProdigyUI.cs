using System.Collections.Generic;
using Network;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Prodigy;

public static class ProdigyUI
{
    private const string PanelName = "PRODIGY_BACKDROP";
    private const string Parent = "Overlay";

    public static void Destroy(BasePlayer player)
    {
        if (player?.net?.connection == null) return;
        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.IsDestroyed) return;
        try { ce.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), PanelName); } catch { }
    }

    public static void Show(BasePlayer player, string entityName, string entityOwner, string position, string prefabId, string getType, string health, string size, string buildingID, string collider, string skin, string last, string code, string info, string lastOnline, bool isSmallUi, string offsetMin, string offsetMax, bool isTimed)
    {
        if (player?.net?.connection == null) return;
        Destroy(player);
        if (string.IsNullOrEmpty(info)) info = "NetID: 0 : Actual owner: Server (0)";
        if (string.IsNullOrEmpty(code)) code = "N/A";
        if (string.IsNullOrEmpty(entityOwner)) entityOwner = "Server";
        if (string.IsNullOrEmpty(lastOnline)) lastOnline = "N/A";
        position = position.Replace("(", "").Replace(")", "").Replace(",", "");
        collider = collider.Replace(".prefab", "");

        var list = new JArray();
        string arg = string.Join("|", new[] { entityName, entityOwner, position, prefabId, getType, health, size, buildingID, collider, skin, last, code, info, lastOnline }).Replace(" ", "_");

        if (isSmallUi)
            BuildSmallPanel(list, entityName, entityOwner, position, prefabId, getType, health, size, buildingID, collider, skin, last, code, info, lastOnline, arg, offsetMin, offsetMax);
        else
            BuildLargePanel(list, entityName, entityOwner, position, prefabId, getType, health, size, buildingID, collider, skin, last, code, info, lastOnline, arg, offsetMin, offsetMax);

        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.IsDestroyed) return;
        try { ce.ClientRPC(RpcTarget.Player("AddUI", player.net.connection), list.ToString()); } catch { }
    }

    private static void AddPanel(JArray list, string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax, string parent, string name, string destroyUi = null, bool needsCursor = false)
    {
        var comps = new JArray
        {
            new JObject { ["type"] = "UnityEngine.UI.Image", ["color"] = color },
            new JObject { ["type"] = "RectTransform", ["anchormin"] = anchorMin, ["anchormax"] = anchorMax, ["offsetmin"] = offsetMin, ["offsetmax"] = offsetMax }
        };
        if (needsCursor) comps.Add(new JObject { ["type"] = "NeedsCursor" });
        var el = new JObject { ["name"] = name, ["parent"] = parent, ["components"] = comps };
        if (!string.IsNullOrEmpty(destroyUi)) el["destroyUi"] = destroyUi;
        list.Add(el);
    }

    private static void AddText(JArray list, string text, int fontSize, string align, string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax, string parent, string name)
    {
        list.Add(new JObject
        {
            ["name"] = name,
            ["parent"] = parent,
            ["destroyUi"] = name,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = text, ["fontSize"] = fontSize, ["align"] = align, ["color"] = color },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = anchorMin, ["anchormax"] = anchorMax, ["offsetmin"] = offsetMin, ["offsetmax"] = offsetMax }
            }
        });
    }

    private static void AddButton(JArray list, string command, string text, int fontSize, string align, string buttonColor, string textColor, string offsetMin, string offsetMax, string parent, string name)
    {
        list.Add(new JObject
        {
            ["name"] = name,
            ["parent"] = parent,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Button", ["command"] = command, ["color"] = buttonColor },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0.5 0.5", ["anchormax"] = "0.5 0.5", ["offsetmin"] = offsetMin, ["offsetmax"] = offsetMax }
            }
        });
        list.Add(new JObject
        {
            ["name"] = name + "_Label",
            ["parent"] = name,
            ["components"] = new JArray
            {
                new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = text, ["fontSize"] = fontSize, ["align"] = align, ["color"] = textColor },
                new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
            }
        });
    }

    private static string BuildFullCopyText(string entityName, string entityOwner, string position, string prefabId, string getType, string health, string size, string buildingID, string collider, string skin, string last, string code, string info, string lastOnline)
    {
        return $"Entity: {entityName}\nOwner: {entityOwner}\nPosition: {position}\nPrefabId: {prefabId}\nType: {getType}\nHealth: {health}\nSize: {size}\nBuilding ID: {buildingID}\nCollider: {collider}\nSkin: {skin}\nLast: {last}\nCode: {code}\nLast Online: {lastOnline}\nDetails: {info}";
    }

    private static void BuildLargePanel(JArray list, string entityName, string entityOwner, string position, string prefabId, string getType, string health, string size, string buildingID, string collider, string skin, string last, string code, string info, string lastOnline, string arg, string offsetMin, string offsetMax)
    {
        AddPanel(list, "0.145098 0.1294118 0.1294118 1", "0.5 0", "0.5 0", offsetMin, offsetMax, Parent, PanelName, PanelName, needsCursor: false);
        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "-204.334 61.289", "203.534 82.512", PanelName, PanelName + "_ENTITY");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-199.644 -8.12", "200.154 8.12", PanelName + "_ENTITY", "ENTITY_EMBED_PANEL");
        AddText(list, entityName, 14, "MiddleCenter", "0.7058824 0.5137255 0.1490196 1", "0 0", "1 1", "-199.644 -10.929", "200.154 10.929", "ENTITY_EMBED_PANEL", "ENTITY_DESC_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "-230.602 35.489", "-16.598 56.712", PanelName, PanelName + "_POSITION");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-101.933 -8.12", "101.933 8.12", PanelName + "_POSITION", "POSITION_EMBED_PANEL");
        AddText(list, "POSITION:", 14, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-100.07 -10.929", "-40.73 10.929", "POSITION_EMBED_PANEL", "POSITION_DESC_LABEL");
        AddText(list, position, 12, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-34.497 -10.929", "98.034 10.929", "POSITION_EMBED_PANEL", "POSITION_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "16.598 35.489", "230.602 56.712", PanelName, PanelName + "_PREFABID");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-101.933 -8.12", "101.933 8.12", PanelName + "_PREFABID", "PREFABID_EMBED_PANEL");
        AddText(list, "PREFABID:", 14, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-100.07 -10.929", "-40.73 10.929", "PREFABID_EMBED_PANEL", "PREFABID_DESC_LABEL");
        AddText(list, prefabId, 12, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-47.989 -10.929", "98.925 10.929", "PREFABID_EMBED_PANEL", "PREFABID_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "-230.602 12.489", "-16.598 33.712", PanelName, PanelName + "_GETTYPE");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-101.933 -8.12", "101.933 8.12", PanelName + "_GETTYPE", "GETTYPE_EMBED_PANEL");
        AddText(list, "TYPE:", 14, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-100.238 -10.929", "-43.462 10.929", "GETTYPE_EMBED_PANEL", "GETTYPE_DESC_LABEL");
        AddText(list, getType, 12, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-64.263 -10.929", "98.985 10.929", "GETTYPE_EMBED_PANEL", "GETTYPE_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "16.598 12.489", "230.602 33.712", PanelName, PanelName + "_HP");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-101.933 -8.12", "101.933 8.12", PanelName + "_HP", "HP_EMBED_PANEL");
        AddText(list, "HEALTH:", 14, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-98.846 -10.929", "-26.034 10.929", "HP_EMBED_PANEL", "HP_DESC_LABEL");
        AddText(list, health, 12, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-46.17 -10.929", "98.985 10.929", "HP_EMBED_PANEL", "HP_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "-230.602 -10.511", "-16.598 10.712", PanelName, PanelName + "_SIZE");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-101.933 -8.12", "101.933 8.12", PanelName + "_SIZE", "SIZE_EMBED_PANEL");
        AddText(list, "SIZE:", 14, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-100.452 -10.929", "-45.346 10.929", "SIZE_EMBED_PANEL", "SIZE_DESC_LABEL");
        AddText(list, size, 12, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-69.295 -10.929", "98.986 10.929", "SIZE_EMBED_PANEL", "SIZE_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "16.598 -10.511", "230.602 10.712", PanelName, PanelName + "_BUILDINGID");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-102.089 -8.12", "100.889 8.12", PanelName + "_BUILDINGID", "BUILDINGID_EMBED_PANEL");
        AddText(list, "BUILDING ID:", 14, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-98.668 -10.929", "-23.792 10.929", "BUILDINGID_EMBED_PANEL", "BUILDINGID_DESC_LABEL");
        AddText(list, buildingID, 12, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-38.349 -10.929", "98.767 10.929", "BUILDINGID_EMBED_PANEL", "BUILDINGID_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "-230.602 -33.511", "-16.598 -12.288", PanelName, PanelName + "_COLLIDER");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-101.933 -8.12", "101.933 8.12", PanelName + "_COLLIDER", "COLLIDER_EMBED_PANEL");
        AddText(list, "COL:", 14, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-100.093 -10.929", "-73.303 10.929", "COLLIDER_EMBED_PANEL", "COLLIDER_DESC_LABEL");
        AddText(list, collider, 12, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-68.718 -10.929", "98.237 10.929", "COLLIDER_EMBED_PANEL", "COLLIDER_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "16.598 -33.511", "230.602 -12.288", PanelName, PanelName + "_SKIN");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-101.933 -8.12", "101.933 8.12", PanelName + "_SKIN", "SKIN_EMBED_PANEL");
        AddText(list, "SKIN:", 14, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-99.011 -10.929", "-21.469 10.929", "SKIN_EMBED_PANEL", "SKIN_DESC_LABEL");
        AddText(list, skin, 12, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-66.358 -10.929", "98.88 10.929", "SKIN_EMBED_PANEL", "SKIN_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "-230.602 -56.411", "-16.598 -35.188", PanelName, PanelName + "_LA");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-101.933 -8.12", "101.933 8.12", PanelName + "_LA", "LA_EMBED_PANEL");
        AddText(list, "LAST:", 14, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-100.093 -10.929", "-43.507 10.929", "LA_EMBED_PANEL", "LA_DESC_LABEL");
        AddText(list, last, 12, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-66.376 -10.929", "99.464 10.929", "LA_EMBED_PANEL", "LA_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "16.598 -56.411", "230.602 -35.188", PanelName, PanelName + "_CODE");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-101.933 -8.12", "101.933 8.12", PanelName + "_CODE", "CODE_EMBED_PANEL");
        AddText(list, "CODE:", 14, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-99.011 -10.929", "-21.469 10.929", "CODE_EMBED_PANEL", "CODE_DESC_LABEL");
        AddText(list, code, 12, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-62.885 -10.929", "98.48 10.929", "CODE_EMBED_PANEL", "CODE_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "-230.602 -79.311", "-16.598 -58.088", PanelName, PanelName + "_OWNER");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-101.933 -8.12", "101.933 8.12", PanelName + "_OWNER", "OWNER_EMBED_PANEL");
        AddText(list, "OWNER:", 14, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-100.093 -10.929", "-43.507 10.929", "OWNER_EMBED_PANEL", "OWNER_DESC_LABEL");
        AddText(list, entityOwner, 12, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-40.73 -10.929", "99.464 10.929", "OWNER_EMBED_PANEL", "OWNER_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "16.598 -79.311", "230.602 -58.088", PanelName, PanelName + "_LASTONLINE");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-101.933 -8.12", "101.933 8.12", PanelName + "_LASTONLINE", "LASTONLINE_EMBED_PANEL");
        AddText(list, "LAST ONLINE:", 14, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-100.07 -10.929", "-8.12 10.929", "LASTONLINE_EMBED_PANEL", "LASTONLINE_DESC_LABEL");
        AddText(list, lastOnline, 12, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-6.12 -10.929", "98.48 10.929", "LASTONLINE_EMBED_PANEL", "LASTONLINE_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "-204.334 -105.211", "203.534 -83.988", PanelName, PanelName + "_DETAILS");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-199.644 -8.12", "200.154 8.12", PanelName + "_DETAILS", "DETAILS_EMBED_PANEL");
        // NetID / Actual owner text: same row as before, shifted right with gap (MiddleRight, right edge -8px)
        AddText(list, info, 12, "MiddleRight", "0.2509804 0.9960785 0 1", "0.5 0.5", "0.5 0.5", "0 -10.929", "195.06 10.929", "DETAILS_EMBED_PANEL", "DETAILS_INFO_LABEL");

        AddButton(list, "cui.endtest PRODIGY paste " + arg, "Click to copy to F1 Console", 10, "MiddleCenter", "0 0 0 0", "1 1 1 1", "-204 -102", "-80 -87", PanelName, PanelName + "_COPY");
        AddButton(list, "cui.endtest PRODIGY up " + arg, "↑", 10, "MiddleCenter", "0.145098 0.1294118 0.1294118 1", "0.682353 0.4980392 0.145098 1", "-9.271 7.549", "8.471 22.645", PanelName, PanelName + "_BUTTON_T");
        AddButton(list, "cui.endtest PRODIGY right " + arg, "→", 10, "MiddleCenter", "0.145098 0.1294118 0.1294118 1", "0.682353 0.4980392 0.145098 1", "-1.145 -7.448", "16.598 7.648", PanelName, PanelName + "_BUTTON_R");
        AddButton(list, "cui.endtest PRODIGY down " + arg, "↓", 10, "MiddleCenter", "0.145098 0.1294118 0.1294118 1", "0.682353 0.4980392 0.145098 1", "-8.871 -22.645", "8.871 -7.549", PanelName, PanelName + "_BUTTON_B");
        AddButton(list, "cui.endtest PRODIGY left " + arg, "←", 10, "MiddleCenter", "0.145098 0.1294118 0.1294118 1", "0.682353 0.4980392 0.145098 1", "-16.598 -7.548", "1.144 7.548", PanelName, PanelName + "_BUTTON_L");
        AddButton(list, "cui.endtest PRODIGY close", "X", 10, "MiddleCenter", "0.145098 0.1294118 0.1294118 1", "0.682353 0.4980392 0.145098 1", "-8.976 26.356", "8.767 41.452", PanelName, PanelName + "_BUTTON_X");
    }

    private static void BuildSmallPanel(JArray list, string entityName, string entityOwner, string position, string prefabId, string getType, string health, string size, string buildingID, string collider, string skin, string last, string code, string info, string lastOnline, string arg, string offsetMin, string offsetMax)
    {
        AddPanel(list, "0.145098 0.1294118 0.1294118 1", "0 0", "0 0", offsetMin, offsetMax, Parent, PanelName, PanelName, needsCursor: false);
        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "-105.519 44.005", "105.311 58.644", PanelName, PanelName + "_ENTITY");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-101.288 -5.157", "101.288 5.157", PanelName + "_ENTITY", "ENTITY_EMBED_PANEL");
        AddText(list, entityName, 6, "MiddleCenter", "0.7058824 0.5137255 0.1490196 1", "0 0", "1 1", "-101.288 -5.157", "101.288 5.157", "ENTITY_EMBED_PANEL", "ENTITY_DESC_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "-105.505 26.813", "-18.071 41.452", PanelName, PanelName + "_POSITION");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-40 -5.157", "40 5.157", PanelName + "_POSITION", "POSITION_EMBED_PANEL");
        AddText(list, "POSITION:", 6, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-37.237 -5.157", "-11.796 5.157", "POSITION_EMBED_PANEL", "POSITION_DESC_LABEL");
        AddText(list, position, 6, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-11.797 -5.157", "37.489 5.157", "POSITION_EMBED_PANEL", "POSITION_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "17.882 26.813", "105.316 41.452", PanelName, PanelName + "_PREFABID");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-40 -5.157", "40 5.157", PanelName + "_PREFABID", "PREFABID_EMBED_PANEL");
        AddText(list, "PREFABID:", 6, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-38.3 -5.157", "-13.3 5.157", "PREFABID_EMBED_PANEL", "PREFABID_DESC_LABEL");
        AddText(list, prefabId, 6, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-13.3 -5.157", "37.968 5.157", "PREFABID_EMBED_PANEL", "PREFABID_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "-105.505 10.313", "-18.071 24.952", PanelName, PanelName + "_GETTYPE");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-40 -5.157", "40 5.157", PanelName + "_GETTYPE", "GETTYPE_EMBED_PANEL");
        AddText(list, "TYPE:", 6, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-37.511 -5.157", "-12.511 5.157", "GETTYPE_EMBED_PANEL", "GETTYPE_DESC_LABEL");
        AddText(list, getType, 6, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-12.511 -5.157", "37.962 5.157", "GETTYPE_EMBED_PANEL", "GETTYPE_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "17.882 10.313", "105.316 24.952", PanelName, PanelName + "_HP");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-40 -5.157", "40 5.157", PanelName + "_HP", "HP_EMBED_PANEL");
        AddText(list, "HEALTH:", 6, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-38.3 -5.157", "-13.3 5.157", "HP_EMBED_PANEL", "HP_DESC_LABEL");
        AddText(list, health, 6, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-13.3 -5.157", "37.774 5.157", "HP_EMBED_PANEL", "HP_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "-105.505 -6.187", "-18.071 8.452", PanelName, PanelName + "_SIZE");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-40.871 -5.157", "39.129 5.157", PanelName + "_SIZE", "SIZE_EMBED_PANEL");
        AddText(list, "SIZE:", 6, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-37.375 -8.12", "-13.2 8.12", "SIZE_EMBED_PANEL", "SIZE_DESC_LABEL");
        AddText(list, size, 6, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-13.2 -5.157", "37.4 5.157", "SIZE_EMBED_PANEL", "SIZE_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "17.882 -6.187", "105.316 8.452", PanelName, PanelName + "_BUILDINGID");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-40.041 -5.157", "39.959 5.157", PanelName + "_BUILDINGID", "BUILDINGID_EMBED_PANEL");
        AddText(list, "BUILDING ID:", 6, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-37.7 -5.157", "-12.7 5.157", "BUILDINGID_EMBED_PANEL", "BUILDINGID_DESC_LABEL");
        AddText(list, buildingID, 6, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-12.699 -5.157", "38.032 5.157", "BUILDINGID_EMBED_PANEL", "BUILDINGID_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "-105.519 -22.687", "-18.085 -8.048", PanelName, PanelName + "_COLLIDER");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-40 -5.157", "40 5.157", PanelName + "_COLLIDER", "COLLIDER_EMBED_PANEL");
        AddText(list, "COL:", 6, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-37.335 -5.157", "-16.327 5.157", "COLLIDER_EMBED_PANEL", "COLLIDER_DESC_LABEL");
        AddText(list, collider, 6, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-16.327 -5.157", "37.515 5.157", "COLLIDER_EMBED_PANEL", "COLLIDER_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "17.881 -22.687", "105.315 -8.048", PanelName, PanelName + "_SKIN");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-40 -5.157", "40 5.157", PanelName + "_SKIN", "SKIN_EMBED_PANEL");
        AddText(list, "SKIN:", 6, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-38.074 -5.157", "-15 5.157", "SKIN_EMBED_PANEL", "SKIN_DESC_LABEL");
        AddText(list, skin, 6, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-14.999 -5.157", "37.291 5.157", "SKIN_EMBED_PANEL", "SKIN_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "-105.519 -39.187", "-18.085 -24.548", PanelName, PanelName + "_LA");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-40 -5.157", "40 5.157", PanelName + "_LA", "LA_EMBED_PANEL");
        AddText(list, "LAST:", 6, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-37.982 -5.157", "-19.692 5.157", "LA_EMBED_PANEL", "LA_DESC_LABEL");
        AddText(list, last, 6, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-19.691 -5.157", "37.473 5.157", "LA_EMBED_PANEL", "LA_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "17.881 -39.187", "105.315 -24.548", PanelName, PanelName + "_CODE");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-40 -5.157", "40 5.157", PanelName + "_CODE", "CODE_EMBED_PANEL");
        AddText(list, "CODE:", 6, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-37.588 -5.157", "-17.541 5.157", "CODE_EMBED_PANEL", "CODE_DESC_LABEL");
        AddText(list, code, 6, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-17.541 -5.157", "37.451 5.157", "CODE_EMBED_PANEL", "CODE_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "-105.519 -55.687", "-18.085 -41.048", PanelName, PanelName + "_OWNER");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-40 -5.157", "40 5.157", PanelName + "_OWNER", "OWNER_EMBED_PANEL");
        AddText(list, "OWNER:", 6, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-37.982 -5.157", "-19.692 5.157", "OWNER_EMBED_PANEL", "OWNER_DESC_LABEL");
        AddText(list, entityOwner, 6, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-19.691 -5.157", "37.473 5.157", "OWNER_EMBED_PANEL", "OWNER_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "17.881 -55.687", "105.315 -41.048", PanelName, PanelName + "_LASTONLINE");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-40 -5.157", "40 5.157", PanelName + "_LASTONLINE", "LASTONLINE_EMBED_PANEL");
        AddText(list, "LAST ONLINE:", 6, "MiddleLeft", "0.7058824 0.5137255 0.1490196 1", "0.5 0.5", "0.5 0.5", "-38.3 -5.157", "-4.2 5.157", "LASTONLINE_EMBED_PANEL", "LASTONLINE_DESC_LABEL");
        AddText(list, lastOnline, 6, "MiddleRight", "0.2431373 0.9411765 0.003921569 1", "0.5 0.5", "0.5 0.5", "-4.2 -5.157", "37.451 5.157", "LASTONLINE_EMBED_PANEL", "LASTONLINE_INFO_LABEL");

        AddPanel(list, "0 0 0 1", "0.5 0.5", "0.5 0.5", "-105.414 -73.986", "105.406 -59.347", PanelName, PanelName + "_DETAILS");
        AddPanel(list, "0.1415094 0.1263795 0.1263795 1", "0.5 0.5", "0.5 0.5", "-100.4 -5.157", "101.37 5.157", PanelName + "_DETAILS", "DETAILS_EMBED_PANEL");
        AddText(list, info, 6, "MiddleRight", "0.2509804 0.9960785 0 1", "0.5 0.5", "0.5 0.5", "0 -5.157", "98.339 5.157", "DETAILS_EMBED_PANEL", "DETAILS_INFO_LABEL");

        AddButton(list, "cui.endtest PRODIGY paste " + arg, "Click to copy to F1 Console", 6, "MiddleCenter", "0 0 0 0", "1 1 1 1", "-105.414 -72.5", "-42 -60.5", PanelName, PanelName + "_COPY");
        AddButton(list, "cui.endtest PRODIGY up " + arg, "↑", 6, "MiddleCenter", "0.145098 0.1294118 0.1294118 1", "0.682353 0.4980392 0.145098 1", "-9.347 8.981", "8.396 24.078", PanelName, PanelName + "_BUTTON_T");
        AddButton(list, "cui.endtest PRODIGY right " + arg, "→", 6, "MiddleCenter", "0.145098 0.1294118 0.1294118 1", "0.682353 0.4980392 0.145098 1", "-1.22 -6.015", "16.523 9.081", PanelName, PanelName + "_BUTTON_R");
        AddButton(list, "cui.endtest PRODIGY down " + arg, "↓", 6, "MiddleCenter", "0.145098 0.1294118 0.1294118 1", "0.682353 0.4980392 0.145098 1", "-8.947 -21.213", "8.796 -6.116", PanelName, PanelName + "_BUTTON_B");
        AddButton(list, "cui.endtest PRODIGY left " + arg, "←", 6, "MiddleCenter", "0.145098 0.1294118 0.1294118 1", "0.682353 0.4980392 0.145098 1", "-16.669 -6.116", "-1.215 8.981", PanelName, PanelName + "_BUTTON_L");
        AddButton(list, "cui.endtest PRODIGY close", "X", 6, "MiddleCenter", "0.145098 0.1294118 0.1294118 1", "0.682353 0.4980392 0.145098 1", "-8.976 26.356", "8.767 41.452", PanelName, PanelName + "_BUTTON_X");
    }
}
