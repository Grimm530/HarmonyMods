using System;
using System.Collections.Generic;
using UnityEngine;

namespace HudHarmony
{
    /// <summary>
    /// Optional cross-mod refresh for other Inventory-parent CUI (e.g. Backpacks button).
    /// Backpacks registers <see cref="AppDomainBackpackRefreshKey"/> on load.
    /// </summary>
    internal static class InventoryLayerUiBridge
    {
        internal const string AppDomainBackpackRefreshKey = "Backpacks_RefreshInventoryGuiButton";
        private const float MinSecondsConnectedBeforeLock = 3f;

        private static readonly HashSet<ulong> ReadyPlayers = new HashSet<ulong>();

        internal static bool IsReady(ulong userId) => ReadyPlayers.Contains(userId);

        internal static void MarkReady(ulong userId) => ReadyPlayers.Add(userId);

        internal static void Clear(ulong userId) => ReadyPlayers.Remove(userId);

        internal static void ClearAll() => ReadyPlayers.Clear();

        internal static bool HasBeenConnectedLongEnough(BasePlayer player)
        {
            var conn = player?.net?.connection;
            if (conn == null)
                return false;
            try
            {
                return conn.GetSecondsConnected() >= MinSecondsConnectedBeforeLock;
            }
            catch
            {
                return Time.realtimeSinceStartup - conn.connectionTime >= MinSecondsConnectedBeforeLock;
            }
        }

        internal static void RefreshBackpackButton(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
                return;

            try
            {
                if (AppDomain.CurrentDomain.GetData(AppDomainBackpackRefreshKey) is Action<BasePlayer> refresh)
                    refresh(player);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Hud] Backpack GUI refresh: " + ex.Message);
            }
        }
    }
}
