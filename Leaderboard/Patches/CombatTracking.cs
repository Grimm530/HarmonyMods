using System.Collections.Generic;

namespace Leaderboard.Patches;

/// <summary>
/// Last player to damage a patrol helicopter, and the player who switched a recycler on.
/// Cleared on wipe so leftover ids are not credited on the next save.
/// </summary>
internal static class CombatTracking
{
    private static readonly Dictionary<ulong, ulong> LastHeliAttacker = new();
    private static readonly Dictionary<ulong, ulong> RecyclerPlayer = new();

    public static void NoteHeliAttacker(PatrolHelicopter heli, BasePlayer attacker)
    {
        if (heli?.net == null || !heli.net.ID.IsValid) return;
        if (attacker == null || attacker.IsNpc || !SteamIdHelper.IsSteamId(attacker.userID)) return;
        LastHeliAttacker[heli.net.ID.Value] = attacker.userID;
    }

    public static bool TryConsumeHeliAttacker(PatrolHelicopter heli, out ulong userId)
    {
        userId = 0;
        if (heli?.net == null || !heli.net.ID.IsValid) return false;
        if (!LastHeliAttacker.TryGetValue(heli.net.ID.Value, out userId)) return false;
        LastHeliAttacker.Remove(heli.net.ID.Value);
        return SteamIdHelper.IsSteamId(userId);
    }

    public static void ForgetHeli(PatrolHelicopter heli)
    {
        if (heli?.net == null || !heli.net.ID.IsValid) return;
        LastHeliAttacker.Remove(heli.net.ID.Value);
    }

    public static void SetRecyclerPlayer(ulong recyclerNetId, ulong userId)
    {
        if (recyclerNetId == 0 || !SteamIdHelper.IsSteamId(userId)) return;
        RecyclerPlayer[recyclerNetId] = userId;
    }

    public static bool TryGetRecyclerPlayer(ulong recyclerNetId, out ulong userId)
    {
        userId = 0;
        return recyclerNetId != 0 && RecyclerPlayer.TryGetValue(recyclerNetId, out userId) && SteamIdHelper.IsSteamId(userId);
    }

    public static void ClearRecyclerPlayer(ulong recyclerNetId)
    {
        if (recyclerNetId == 0) return;
        RecyclerPlayer.Remove(recyclerNetId);
    }

    public static void Clear()
    {
        LastHeliAttacker.Clear();
        RecyclerPlayer.Clear();
    }
}
