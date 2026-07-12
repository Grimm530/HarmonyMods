using System.IO;
using Thorium.Rust.Config;
using UnityEngine;

namespace Thorium.Rust.Services;

public static class DataHandler
{
    public const long MaxCacheSize = 2_000_000_000;

    public static MemoryStream PacketCache { get; private set; } = new(65536);
    public static MemoryStream PvpCache { get; private set; } = new(16384);
    public static MemoryStream JoinCache { get; private set; } = new(4096);
    public static MemoryStream DamageCache { get; private set; } = new(16384);
    public static MemoryStream EntityCache { get; private set; } = new(65536);

    public static long TotalPackets { get; set; }
    public static long TotalPvpPackets { get; set; }
    public static long TotalJoinPackets { get; set; }
    public static long TotalDamagePackets { get; set; }
    public static long TotalEntityPackets { get; set; }

    private static bool _isConfigured;
    private static float _lastCheck;

    public static bool IsConfigured
    {
        get
        {
            var now = Time.realtimeSinceStartup;
            if (now - _lastCheck > 5f)
            {
                _isConfigured = ThoriumConfigService.HasValidToken;
                _lastCheck = now;
            }
            return _isConfigured;
        }
    }

    public static void Reset()
    {
        PacketCache?.Dispose();
        PvpCache?.Dispose();
        JoinCache?.Dispose();
        DamageCache?.Dispose();
        EntityCache?.Dispose();
        PacketCache = new MemoryStream(65536);
        PvpCache = new MemoryStream(16384);
        JoinCache = new MemoryStream(4096);
        DamageCache = new MemoryStream(16384);
        EntityCache = new MemoryStream(65536);
        TotalPackets = 0;
        TotalPvpPackets = 0;
        TotalJoinPackets = 0;
        TotalDamagePackets = 0;
        TotalEntityPackets = 0;
        _isConfigured = false;
        _lastCheck = 0f;
    }
}