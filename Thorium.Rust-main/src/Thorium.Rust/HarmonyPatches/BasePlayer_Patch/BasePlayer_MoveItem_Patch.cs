using System;
using HarmonyLib;
using Thorium.Rust.Core;
using Thorium.Rust.HarmonyPatches.Utility;
using Thorium.Rust.Models;
using Thorium.Rust.Services;
using UnityEngine;

namespace Thorium.Rust.HarmonyPatches.BasePlayer_Patch;

[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.MoveItem))]
internal static class BasePlayer_MoveItem_Patch
{
    [HarmonyPrefix]
    private static void Prefix(BaseEntity.RPCMessage msg)
    {
        try
        {
            if (!DataHandler.IsConfigured) return;

            var player = msg.player;

            if (player == null) return;
            if (player.IsAdmin || player.IsDeveloper) return;
            if (DataHandler.PacketCache.Length > DataHandler.MaxCacheSize) return;
            DataHandler.TotalPackets++;
            var packetCache = DataHandler.PacketCache;
            ProtoBufManager.WriteZInt32(packetCache, 0);
            ProtoBufManager.WriteUint(packetCache, 3041092525u);
            ProtoBufManager.WriteString(packetCache, player.UserIDString);
            ProtoBufManager.WriteUint(packetCache, player.prefabID);
            ProtoBufManager.WriteSingle(packetCache, Time.time);
            ProtoBufManager.WriteZInt32(packetCache, Time.frameCount);

            const int itemId = 0;
            ProtoBufManager.WriteZInt32(packetCache, itemId);

            var read = msg.read;
            if (read != null)
            {
                var stream = read.stream;
                var buffer = stream._buffer;
                var length = stream._length;

                ProtoBufManager.WriteCappedBytes(packetCache, buffer, length);
            }

            var steamId = Helpers.GetSteamIdOrZero(player);
            var pos = player.transform.position;
            var snapshot = PlayerSnapshot.Create(pos, player, SnapshotTypeEnums.MoveItem, 
                new CombatData { Weapon = itemId.ToString() },
                player.estimatedVelocity, player.IsOnGround());

            AntiCheatSnapshotProcessor.Enqueue(steamId, snapshot);
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
        }
    }
}