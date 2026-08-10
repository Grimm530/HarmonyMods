using System;
using System.Collections.Generic;

namespace Leaderboard.Storage;

public interface ILeaderboardStorage
{
    void LoadPlayer(ulong userId, Action<PlayerStats> callback);
    /// <summary>Load every player JSON from disk (for Top 10 / Search / relay sync).</summary>
    List<PlayerStats> LoadAllPlayers();
    void SavePlayer(PlayerStats stats);
    void SaveAll(bool isUnload = false);
    void Wipe();
}
