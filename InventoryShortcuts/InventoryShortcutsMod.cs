using System;
using GrimmCuiHarmony;
using System.Globalization;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace InventoryShortcuts;
public class InventoryShortcutsMod : IHarmonyModHooks
{
    public static InventoryShortcutsMod Instance { get; private set; }
    private const string PanelNameHotbar = "InventoryShortcuts.hotbar";
    private static readonly HashSet<ulong> _sentToPlayers = new();
    private static CommunityEntity GetCommunityEntity()
    {
        var ce = CommunityEntity.ServerInstance;
        if (ce != null && !ce.IsDestroyed) return ce;
        if (BaseNetworkable.serverEntities != null)
        {
            foreach (var e in BaseNetworkable.serverEntities)
            {
                if (e is CommunityEntity c && c != null && !c.IsDestroyed)
                    return c;
            }
        }
        return null;
    }
    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
            GrimmCui.RegisterReadyCallback(GrimmCuiRegistration.Register);
        Instance = this;
        InventoryShortcutsConfig.Load();
        UnityEngine.Debug.Log("[InventoryShortcuts] Mod loaded. Hotbar: Outpost/Players/Kits/Shop/Skins/Vehicles.");
    }
    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        foreach (var player in BasePlayer.activePlayerList)
            DestroyUi(player);
        _sentToPlayers.Clear();
        Instance = null;
    }
    public static void ClearPlayerSent(ulong userId)
    {
        _sentToPlayers.Remove(userId);
    }
    public bool ShowButtonsIfNeeded(BasePlayer player)
    {
        if (player?.net?.connection == null) return false;
        if (_sentToPlayers.Contains(player.userID)) return false;
        if (!TryShowButtons(player)) return false;
        _sentToPlayers.Add(player.userID);
        return true;
    }
    public void ShowButtons(BasePlayer player)
    {
        TryShowButtons(player);
    }
    private bool TryShowButtons(BasePlayer player)
    {
        if (player?.net?.connection == null) return false;
        if (player.IsReceivingSnapshot) return false;
        var ce = GetCommunityEntity();
        if (ce == null) return false;
        var cfg = InventoryShortcutsConfig.Config;
        string btnColor = cfg?.ButtonColor ?? "0.42 0.40 0.37 0.85";
        string textColor = cfg?.TextColor ?? "0.875 0.827 0.780 1";

        // Bottom hotbar row: Hud parent = always visible, centered across screen
        float hotbarHeight = Mathf.Clamp(cfg?.HotbarButtonHeight ?? 0.018f, 0.008f, 0.08f);
        float hotbarYMin = 0.004f;
        float hotbarYMax = hotbarYMin + hotbarHeight;
        var elements = new List<JObject>();
        var hotbarContainer = Container(PanelNameHotbar, "Hud",
            "0 " + hotbarYMin.ToString("F3", CultureInfo.InvariantCulture),
            "1 " + hotbarYMax.ToString("F3", CultureInfo.InvariantCulture), "0 0", "0 0");
        hotbarContainer["destroyUi"] = PanelNameHotbar;
        elements.Add(hotbarContainer);
        float hbBtnW = 0.047f; // 0.04f is the width of the button
        float gap = 0.003f;
        float x0 = 0.344f; // fixed left position so adding buttons only extends right reduce to move the buttons to the left
        var hotbarButtons = new[] { ("invshortcut_outpost", "OUTPOST", "/outpost"), ("invshortcut_players", "PLAYERS", "/tp"), ("invshortcut_kits", "KITS", "/kits"), ("invshortcut_shop", "SHOP", "/s"), ("invshortcut_skins", "SKINS", "/skinshop"), ("invshortcut_vehicles", "VEHICLES", "/vehicles") };
        for (int i = 0; i < hotbarButtons.Length; i++)
        {
            float xMin = x0 + i * (hbBtnW + gap);
            float xMax = xMin + hbBtnW;
            var (btnName, btnText, cmd) = hotbarButtons[i];
            // Use direct chat.say so client runs it natively (same as Hud/IndustrialRecycler) – works with Harmony mods
            elements.AddRange(Button(btnName, PanelNameHotbar, btnColor, textColor, btnText, 8,
                xMin.ToString("F3", CultureInfo.InvariantCulture) + " 0",
                xMax.ToString("F3", CultureInfo.InvariantCulture) + " 1",
                "0 0", "0 0", "chat.say " + cmd));
        }

        string json = Newtonsoft.Json.JsonConvert.SerializeObject(elements);
        ce.ClientRPC(RpcTarget.Player("AddUI", player.net.connection), json);
        return true;
    }
    public void DestroyUi(BasePlayer player)
    {
        if (player?.net?.connection == null) return;
        var ce = GetCommunityEntity();
        if (ce != null)
        {
            ce.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), PanelNameHotbar);
            DestroyGridOverlay(player);
        }
    }

    /// <summary>Shows the percentage grid overlay (0-1) for UI layout reference. Admin only via /gridlines.</summary>
    public void ShowGridOverlay(BasePlayer player)
    {
        if (player?.net?.connection == null) return;
        var ce = GetCommunityEntity();
        if (ce == null) return;
        var elements = UIGridOverlay.BuildGridElements();
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(elements);
        ce.ClientRPC(RpcTarget.Player("AddUI", player.net.connection), json);
    }

    /// <summary>Removes the grid overlay (called when user clicks X or on disconnect).</summary>
    public void DestroyGridOverlay(BasePlayer player)
    {
        if (player?.net?.connection == null) return;
        var ce = GetCommunityEntity();
        if (ce != null)
            ce.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), UIGridOverlay.PanelName);
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
        string anchorMin, string anchorMax, string offsetMin, string offsetMax, string command)
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
        // Texture overlay layer (greyout.mat) – matches Hud/Backpacks slot-style panels
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
                    ["anchormin"] = "0.05 0",
                    ["anchormax"] = "0.95 1"
                }
            }
        });
        return list;
    }
}