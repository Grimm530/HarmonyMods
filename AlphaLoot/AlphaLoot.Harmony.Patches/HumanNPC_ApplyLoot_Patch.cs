using HarmonyLib;

namespace AlphaLoot.Harmony.Patches;

[HarmonyPatch(typeof(HumanNPC), "ApplyLoot")]
public class HumanNPC_ApplyLoot_Patch
{
	[HarmonyPrefix]
	private static bool Prefix(HumanNPC __instance, NPCPlayerCorpse corpse)
	{
		AlphaLootMod instance = AlphaLootMod.Instance;
		if (instance == null)
		{
			return true;
		}
		if (!instance.TryGetNPCProfile(__instance.ShortPrefabName, out var profile) || !profile.Enabled)
		{
			return true;
		}
		instance.PopulateCorpseLoot(__instance, corpse);
		return false;
	}
}
