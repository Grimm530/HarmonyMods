using System;

namespace Leaderboard.Storage;

public interface ILeaderboardStorage
{
    void LoadPlayer(ulong userId, Action<PlayerStats> callback);
    void SavePlayer(PlayerStats stats);
    void SaveAll(bool isUnload = false);
    void Wipe();
}
