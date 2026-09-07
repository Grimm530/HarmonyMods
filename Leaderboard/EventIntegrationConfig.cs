namespace Leaderboard;

/// <summary>Optional event-mod stat hooks (applied via reflection when target assemblies are loaded).</summary>
public class EventIntegrationConfig
{
    public bool RaidableBases { get; set; } = true;
    public bool Convoy { get; set; } = true;
    public bool ArmoredTrain { get; set; } = true;
    public bool CustomHelicopterTiers { get; set; } = true;
}
