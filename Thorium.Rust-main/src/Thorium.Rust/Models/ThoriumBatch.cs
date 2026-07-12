using System.Collections.Generic;

namespace Thorium.Rust.Models;

/// <summary>
/// Represents a batch of anti-cheat snapshots to be sent to the Thorium backend
/// Contains multiple player snapshots for analysis
/// Note: Server metadata (hostname, map, IP, port) is sent separately as an initial text message on connection
/// </summary>
public class ThoriumBatch
{
    /// <summary>
    /// The starting tick number for this batch
    /// </summary>
    public long StartTick { get; set; }

    /// <summary>
    /// The ending tick number for this batch
    /// </summary>
    public long EndTick { get; set; }

    /// <summary>
    /// Collection of anti-cheat snapshots from multiple players
    /// </summary>
    public List<AntiCheatSnapshot> Snapshots { get; set; } = new();
}