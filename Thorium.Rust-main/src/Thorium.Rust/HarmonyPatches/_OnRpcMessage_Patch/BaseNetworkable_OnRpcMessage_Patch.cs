using System;
using HarmonyLib;
using Network;
using Thorium.Rust.Services;
using UnityEngine;

namespace Thorium.Rust.HarmonyPatches._OnRpcMessage_Patch;

[HarmonyPatch(typeof(ServerMgr), nameof(ServerMgr.OnRPCMessage))]
internal static class BaseNetworkable_OnRpcMessage_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Message packet)
    {
        try
        {
            if (!DataHandler.IsConfigured) return;

            if (packet.type != Message.Type.RPCMessage) return;

            var readStream = packet.read;
            if (readStream == null) return;

            var savedPosition = readStream.Position;

            BasePlayer? player;
            uint rpcId;
            NetworkableId entityId;
            var rawPacketData = Array.Empty<byte>();
            int rawPacketLength = 0;

            try
            {
                player = packet.connection?.player as BasePlayer;

                readStream.Position = 1;
                entityId = readStream.EntityID();
                rpcId = readStream.UInt32();

                var payloadStream = readStream.stream;
                if (payloadStream != null && payloadStream._length > 0 && payloadStream._buffer != null)
                {
                    rawPacketLength = payloadStream._length;
                    rawPacketData = new byte[rawPacketLength];
                    Buffer.BlockCopy(payloadStream._buffer, 0, rawPacketData, 0, rawPacketLength);
                }
            }
            catch
            {
                return;
            }
            finally
            {
                readStream.Position = savedPosition;
            }

            if (player != null && ThoriumLoader.rpcActions?.TryGetValue(rpcId, out var action) == true)
            {
                try
                {
                    var entity = BaseNetworkable.serverEntities.Find(entityId) as BaseEntity;
                    action(player, entity);
                }
                catch
                {
                }
            }

            if (DataHandler.PacketCache.Length > DataHandler.MaxCacheSize) return;

            DataHandler.TotalPackets++;
            var cache = DataHandler.PacketCache;

            ProtoBufManager.WriteZInt32(cache, 1);
            ProtoBufManager.WriteUint(cache, rpcId);
            ProtoBufManager.WriteString(cache, player?.UserIDString ?? string.Empty);
            ProtoBufManager.WriteInt64(cache, (long)entityId.Value);
            ProtoBufManager.WriteSingle(cache, Time.time);
            ProtoBufManager.WriteZInt32(cache, Time.frameCount);
            ProtoBufManager.WriteVector(cache, player != null ? player.transform.position : Vector3.zero);
            ProtoBufManager.WriteZInt32(cache, -(rawPacketLength + 1));
            if (rawPacketLength > 0)
                cache.Write(rawPacketData, 0, rawPacketLength);
        }
        catch
        {
        }
    }
}
