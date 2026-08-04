using System.Collections.Generic;

namespace Prodigy;

public class ProdigyConfig
{
    /// <summary>If true, only admins can use prodigy. If false, AllowedSteamIds is used (empty = no one except admins).</summary>
    public bool AdminOnly { get; set; } = false;

    /// <summary>Steam IDs allowed to use prodigy when AdminOnly is false. Admins can always use.</summary>
    public List<ulong> AllowedSteamIds { get; set; } = new();

    /// <summary>Steam IDs allowed to use MLRS repair (prodigy.mlrs equivalent).</summary>
    public List<ulong> AllowedMlrsSteamIds { get; set; } = new();

    /// <summary>Data folder name under server root (e.g. HarmonyData/Prodigy).</summary>
    public string DataFolder { get; set; } = "HarmonyData/Prodigy";
}
