using System.Collections.Generic;

namespace AlphaLoot.Harmony;

public static class AlphaLootContext
{
	public static AlphaLootConfig Config { get; set; }

	public static Dictionary<string, HashSet<SkinEntry>> WeightedSkinIds { get; set; }

	public static Dictionary<string, List<ulong>> ImportedSkinIds { get; set; }

	public static HashSet<ulong> BlockedWorkshopSkinIds { get; set; }
}
