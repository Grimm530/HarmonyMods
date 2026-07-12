using System.Collections.Generic;
using System.Globalization;
using Facepunch;
using Network;
using Newtonsoft.Json;

namespace ShorterNights;

/// <summary>
/// CUI panel showing server game time under the hotbar.
/// </summary>
internal static class GameTimeDisplayUI
{
    internal const string PanelName = "ShorterNights_Time";
    private const string Parent = "Overlay";
    private const string PanelSprite = "assets/content/ui/ui.background.tile.psd";
    // Under hotbar: small strip at bottom center (half size)
    private const string Anchormin = "0.46 0";
    private const string Anchormax = "0.54 0.02";

    internal static void RefreshAll()
    {
        if (TOD_Sky.Instance == null) return;
        string timeText = "TOD: " + TOD_Sky.Instance.Cycle.DateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        foreach (var player in BasePlayer.activePlayerList)
        {
            if (player == null || !player.IsConnected || player.net?.connection == null) continue;
            RefreshFor(player, timeText);
        }
    }

    internal static void RefreshFor(BasePlayer player, string timeText = null)
    {
        if (player == null || !player.IsConnected || player.net?.connection == null) return;
        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.IsDestroyed) return;
        if (string.IsNullOrEmpty(timeText) && TOD_Sky.Instance != null)
            timeText = "TOD: " + TOD_Sky.Instance.Cycle.DateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(timeText)) timeText = "TOD: --:--";
        var elements = BuildUI(timeText);
        string json = JsonConvert.SerializeObject(elements);
        try { ce.ClientRPC(RpcTarget.Player("AddUI", player.net.connection), json); }
        catch { }
    }

    internal static void DestroyAll()
    {
        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.IsDestroyed) return;
        foreach (var player in BasePlayer.activePlayerList)
        {
            if (player?.net?.connection == null) continue;
            try { ce.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), PanelName); }
            catch { }
        }
    }

    private static List<object> BuildUI(string timeText)
    {
        return new List<object>
        {
            new Dictionary<string, object>
            {
                ["name"] = PanelName,
                ["parent"] = Parent,
                ["destroyUi"] = PanelName,
                ["components"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["type"] = "UnityEngine.UI.Image",
                        ["sprite"] = PanelSprite,
                        ["color"] = "0 0 0 0"
                    },
                    new Dictionary<string, object>
                    {
                        ["type"] = "RectTransform",
                        ["anchormin"] = Anchormin,
                        ["anchormax"] = Anchormax
                    }
                }
            },
            new Dictionary<string, object>
            {
                ["parent"] = PanelName,
                ["name"] = PanelName + "_Text",
                ["components"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["type"] = "UnityEngine.UI.Text",
                        ["text"] = timeText,
                        ["fontSize"] = 12,
                        ["align"] = "MiddleCenter",
                        ["color"] = "1 1 1 1"
                    },
                    new Dictionary<string, object>
                    {
                        ["type"] = "RectTransform",
                        ["anchormin"] = "0 0",
                        ["anchormax"] = "1 1"
                    }
                }
            }
        };
    }
}
