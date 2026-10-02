using System;
using System.Collections;

namespace Leaderboard.Patches;

/// <summary>
/// Records event wins from 1.5.57–1.5.69 that this server can receive either through
/// Leaderboard.Call / UltimateLeaderboard_Plugin.Call, or the GrimmCore hook bus.
/// The event plugins themselves are not in this workspace, so there is no direct Harmony target.
/// </summary>
internal static class EventWinHooks
{
    private const string ModId = "Leaderboard";
    private static EventIntegrationConfig _cfg = new();
    private static bool _registered;

    public static void Register(EventIntegrationConfig cfg)
    {
        _cfg = cfg ?? new EventIntegrationConfig();
        if (_registered) return;

        var reg = AppDomain.CurrentDomain.GetData("GrimmCore_RegisterHook") as Action<string, string, int, Delegate>;
        if (reg == null) return;

        if (_cfg.SubwayEvent)
            reg(ModId, "OnSubwayEventCompleted", 20, (Func<object, object, object>)((a, b) => { RecordUser(a, "SubwayEvent"); return null; }));
        if (_cfg.CargoPlaneCrash)
            reg(ModId, "CargoPlaneCrashEventWinner", 20, (Func<object, object>)(a => { RecordUser(a, "CargoPlaneCrash"); return null; }));
        if (_cfg.GuardedCrate)
            reg(ModId, "OnGuardedCrateEventEnded", 20, (Func<object, object, object>)((a, b) => { RecordUser(a, "GuardedCrate"); return null; }));
        if (_cfg.F15CrashEvent)
            reg(ModId, "F15CrashEventWinner", 20, (Func<object, object>)(a => { RecordUser(a, "F15CrashEvent"); return null; }));
        if (_cfg.Shipwreck)
            reg(ModId, "OnShipwreckEventWin", 20, (Func<object, object>)(a => { RecordUser(a, "Shipwreck"); return null; }));

        _registered = true;
    }

    public static void Unregister()
    {
        _registered = false;
        try
        {
            var un = AppDomain.CurrentDomain.GetData("GrimmCore_UnregisterHookMod") as Action<string>;
            un?.Invoke(ModId);
        }
        catch { }
    }

    public static bool TryHandle(string method, object[] args)
    {
        args ??= Array.Empty<object>();
        switch (method)
        {
            case "API_OnEventWin":
                if (args.Length >= 2)
                    RecordUser(args[0], args[1]?.ToString());
                return true;
            case "OnSubwayEventCompleted":
                if (_cfg.SubwayEvent) RecordUser(First(args), "SubwayEvent");
                return true;
            case "CargoPlaneCrashEventWinner":
                if (_cfg.CargoPlaneCrash) RecordUser(First(args), "CargoPlaneCrash");
                return true;
            case "OnGuardedCrateEventEnded":
                if (_cfg.GuardedCrate) RecordUser(First(args), "GuardedCrate");
                return true;
            case "F15CrashEventWinner":
                if (_cfg.F15CrashEvent) RecordUser(First(args), "F15CrashEvent");
                return true;
            case "OnShipwreckEventWin":
                if (_cfg.Shipwreck) RecordUser(First(args), "Shipwreck");
                return true;
            case "OnRaidableBoatCompleted":
                if (_cfg.RaidableBoats) RecordBoats(args);
                return true;
            default:
                return false;
        }
    }

    private static object First(object[] args) => args.Length > 0 ? args[0] : null;

    private static void RecordUser(object value, string eventName)
    {
        if (string.IsNullOrEmpty(eventName)) return;
        var userId = CoerceUserId(value);
        if (userId == 0) return;
        EventRecording.RecordEvent(userId, eventName);
    }

    private static void RecordBoats(object[] args)
    {
        if (args.Length < 4) return;
        var difficulty = args[3]?.ToString();
        if (string.IsNullOrEmpty(difficulty)) return;
        if (args[1] is not IEnumerable raiders) return;
        foreach (var item in raiders)
        {
            var userId = CoerceUserId(item);
            if (userId == 0) continue;
            EventRecording.RecordRaidableBoat(userId, difficulty);
        }
    }

    internal static ulong CoerceUserId(object value)
    {
        if (value == null) return 0;
        if (value is BasePlayer bp)
        {
            if (bp.IsNpc || !SteamIdHelper.IsSteamId(bp.userID)) return 0;
            return bp.userID;
        }
        if (value is ulong u) return SteamIdHelper.IsSteamId(u) ? u : 0;
        if (value is long l && l > 0)
        {
            var id = (ulong)l;
            return SteamIdHelper.IsSteamId(id) ? id : 0;
        }
        if (value is int i && i > 0)
        {
            var id = (ulong)i;
            return SteamIdHelper.IsSteamId(id) ? id : 0;
        }
        if (value is string s && ulong.TryParse(s, out var parsed) && SteamIdHelper.IsSteamId(parsed))
            return parsed;
        return 0;
    }
}
