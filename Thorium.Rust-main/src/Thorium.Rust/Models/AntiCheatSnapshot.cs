using System.Collections.Generic;

namespace Thorium.Rust.Models;

/// <summary>
/// Represents a collection of player snapshots for anti-cheat analysis
/// Contains all movement and behavior data for a specific player
/// </summary>
public class AntiCheatSnapshot
{
    /// <summary>
    /// The Steam ID of the player this snapshot belongs to
    /// </summary>
    public long SteamId { get; set; }

    /// <summary>
    /// Collection of player snapshots captured over time
    /// Contains movement, input, and state data for analysis
    /// </summary>
    public List<PlayerSnapshot> Snapshots { get; set; } = [];
}