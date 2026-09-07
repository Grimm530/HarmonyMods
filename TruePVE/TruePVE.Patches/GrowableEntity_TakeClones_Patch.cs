using System;
using HarmonyLib;
using UnityEngine;

namespace TruePVE.Patches;

[HarmonyPatch(typeof(GrowableEntity), "TakeClones", new Type[] { typeof(BasePlayer) })]
public static class GrowableEntity_TakeClones_Patch
{
	[HarmonyPrefix]
	public static bool Prefix(GrowableEntity __instance, BasePlayer player)
	{
		if (TruePVEMod.Instance?.Config?.PreventLooting == null || !TruePVEMod.Instance.Config.PreventLooting.ProtectPlanterboxes)
		{
			return true;
		}
		if ((Object)(object)player == (Object)null)
		{
			return true;
		}
		BaseEntity parentEntity = __instance.GetParentEntity();
		if ((Object)(object)parentEntity == (Object)null)
		{
			return true;
		}
		if (parentEntity.OwnerID == 0L || parentEntity.OwnerID == (ulong)player.userID)
		{
			return true;
		}
		if (TruePVEMod.Instance.IsAlly(parentEntity.OwnerID, player.userID))
		{
			return true;
		}
		return false;
	}
}
