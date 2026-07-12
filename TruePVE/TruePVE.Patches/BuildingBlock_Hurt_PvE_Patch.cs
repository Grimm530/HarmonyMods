using System;
using ConVar;
using HarmonyLib;

namespace TruePVE.Patches;

[HarmonyPatch(typeof(BuildingBlock), "Hurt", new Type[] { typeof(HitInfo) })]
public static class BuildingBlock_Hurt_PvE_Patch
{
	[HarmonyPrefix]
	public static bool Prefix(BuildingBlock __instance, HitInfo info, ref bool __state)
	{
		__state = false;
		ulong attackerId = PvEDamageHelpers.ResolvePlayerInitiatorId(info);
		if (attackerId == 0UL)
		{
			return true;
		}
		if (TruePVEMod.Instance?.Config?.PvE == null)
		{
			return true;
		}
		if (!TruePVEMod.Instance.Config.PvE.EnableGamePvE)
		{
			return true;
		}
		if (__instance.grade == BuildingGrade.Enum.Twigs && __instance.OwnerID == attackerId)
		{
			__state = Server.pve;
			Server.pve = false;
			return true;
		}
		// Ownerless blocks (e.g. Raidable Bases sets OwnerID = 0 on pasted event entities).
		if (__instance.OwnerID == 0uL)
		{
			__state = Server.pve;
			Server.pve = false;
			return true;
		}
		return false;
	}

	[HarmonyPostfix]
	public static void Postfix(bool __state)
	{
		if (__state)
		{
			Server.pve = true;
		}
	}
}
