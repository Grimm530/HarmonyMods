using System;
using System.IO;

namespace Thorium.Rust.Services;

internal sealed class DataHandlerPayload
{
    public byte[]? PacketCacheBytes { get; set; }
    public byte[]? PvpCacheBytes { get; set; }
    public byte[]? JoinCacheBytes { get; set; }
    public byte[]? DamageCacheBytes { get; set; }
    public byte[]? EntityCacheBytes { get; set; }

    public long TotalPackets { get; set; }
    public long TotalPvpPackets { get; set; }
    public long TotalJoinPackets { get; set; }
    public long TotalDamagePackets { get; set; }
    public long TotalEntityPackets { get; set; }

    public bool HasAnyBytes =>
        (PacketCacheBytes != null && PacketCacheBytes.Length > 0) ||
        (PvpCacheBytes != null && PvpCacheBytes.Length > 0) ||
        (JoinCacheBytes != null && JoinCacheBytes.Length > 0) ||
        (DamageCacheBytes != null && DamageCacheBytes.Length > 0) ||
        (EntityCacheBytes != null && EntityCacheBytes.Length > 0);

    public static DataHandlerPayload? TryDrainAndReset()
    {
        var packet = DrainStream(DataHandler.PacketCache);
        var pvp = DrainStream(DataHandler.PvpCache);
        var join = DrainStream(DataHandler.JoinCache);
        var damage = DrainStream(DataHandler.DamageCache);
        var entity = DrainStream(DataHandler.EntityCache);

        var payload = new DataHandlerPayload
        {
            PacketCacheBytes = packet,
            PvpCacheBytes = pvp,
            JoinCacheBytes = join,
            DamageCacheBytes = damage,
            EntityCacheBytes = entity,

            TotalPackets = DataHandler.TotalPackets,
            TotalPvpPackets = DataHandler.TotalPvpPackets,
            TotalJoinPackets = DataHandler.TotalJoinPackets,
            TotalDamagePackets = DataHandler.TotalDamagePackets,
            TotalEntityPackets = DataHandler.TotalEntityPackets,
        };

        ResetStream(DataHandler.PacketCache);
        ResetStream(DataHandler.PvpCache);
        ResetStream(DataHandler.JoinCache);
        ResetStream(DataHandler.DamageCache);
        ResetStream(DataHandler.EntityCache);


        return payload.HasAnyBytes ? payload : null;
    }

    private static byte[]? DrainStream(MemoryStream? ms)
    {
        if (ms == null)
            return null;

        var length = (int)Math.Min(ms.Length, int.MaxValue);
        if (length <= 0)
            return null;

        // Make an exact-length copy so protobuf length prefix is correct.
        var buf = new byte[length];

        // Prefer GetBuffer for performance when available.
        if (ms.TryGetBuffer(out var segment))
        {
            Array.Copy(segment.Array!, segment.Offset, buf, 0, length);
            return buf;
        }

        var pos = ms.Position;
        ms.Position = 0;
        _ = ms.Read(buf, 0, length);
        ms.Position = pos;
        return buf;
    }

    private static void ResetStream(MemoryStream? ms)
    {
        if (ms == null)
            return;

        ms.SetLength(0);
        ms.Position = 0;
    }
}
