using System;
using Network;

namespace InventoryShortcuts;

/// <summary>
/// UI-only RPC helpers — same pattern as Oxide RustCui / Vanish AddUi (AddUI + DestroyUI string RPC only).
/// Does not send CL_ReceiveFilePng or touch FileStorage; png icons are not used.
/// </summary>
internal static class CuiHelper
{
    private const int MaxJsonLength = 100_000;

    public static bool AddUi(BasePlayer player, string json)
    {
        if (player == null || player.IsDestroyed || player.net?.connection == null || string.IsNullOrEmpty(json))
            return false;
        if (!player.net.connection.connected)
            return false;
        if (json.Length > MaxJsonLength)
        {
            UnityEngine.Debug.LogWarning($"[InventoryShortcuts] UI JSON too large ({json.Length} bytes) for {player.UserIDString}, skipping.");
            return false;
        }

        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.net == null)
            return false;

        try
        {
            string trimmed = json.Trim();
            if (!trimmed.StartsWith("[") || !trimmed.EndsWith("]"))
                return false;

            ce.ClientRPC(RpcTarget.Player("AddUI", player.net.connection), json);
            return true;
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[InventoryShortcuts] AddUi failed for {player.UserIDString}: {ex.Message}");
            return false;
        }
    }

    public static void DestroyUi(BasePlayer player, string panelName)
    {
        if (player == null || player.IsDestroyed || player.net?.connection == null || string.IsNullOrEmpty(panelName))
            return;
        if (!player.net.connection.connected)
            return;

        var ce = CommunityEntity.ServerInstance;
        if (ce == null || ce.net == null)
            return;

        try
        {
            ce.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), panelName);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[InventoryShortcuts] DestroyUi failed for {player.UserIDString}: {ex.Message}");
        }
    }
}
