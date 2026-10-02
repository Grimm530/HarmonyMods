using Newtonsoft.Json;

namespace Leaderboard;

/// <summary>Optional event-mod stat hooks (applied via reflection when target assemblies are loaded).</summary>
public class EventIntegrationConfig
{
    [JsonProperty("RaidableBases")] public bool RaidableBases { get; set; } = true;
    [JsonProperty("Convoy")] public bool Convoy { get; set; } = true;
    [JsonProperty("ArmoredTrain")] public bool ArmoredTrain { get; set; } = true;
    [JsonProperty("CustomHelicopterTiers")] public bool CustomHelicopterTiers { get; set; } = true;

    /// <summary>1.5.60 Subway Event wins (hook OnSubwayEventCompleted).</summary>
    [JsonProperty("SubwayEvent")] public bool SubwayEvent { get; set; } = true;
    /// <summary>1.5.68 Cargo Plane Crash wins.</summary>
    [JsonProperty("CargoPlaneCrash")] public bool CargoPlaneCrash { get; set; } = true;
    /// <summary>1.5.68 Guarded Crate wins.</summary>
    [JsonProperty("GuardedCrate")] public bool GuardedCrate { get; set; } = true;
    /// <summary>1.5.69 F15 Crash Event wins.</summary>
    [JsonProperty("F15CrashEvent")] public bool F15CrashEvent { get; set; } = true;
    /// <summary>1.5.69 Shipwreck wins.</summary>
    [JsonProperty("Shipwreck")] public bool Shipwreck { get; set; } = true;
    /// <summary>1.5.67 Raidable Boats completions, stored by difficulty.</summary>
    [JsonProperty("RaidableBoats")] public bool RaidableBoats { get; set; } = true;
}
