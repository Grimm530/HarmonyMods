using System;
using HarmonyLib;
using UnityEngine;

namespace TruePVE.Patches;

[HarmonyPatch(typeof(BasePlayer), "CanBeLooted", new Type[] { typeof(BasePlayer) })]
public static class BasePlayer_CanBeLooted_Patch
{
	[HarmonyPrefix]
	public static bool Prefix(BasePlayer __instance, BasePlayer player, ref bool __result)
	{
		if ((Object)(object)__instance == (Object)null || (Object)(object)player == (Object)null || (Object)(object)player == (Object)(object)__instance)
		{
			return true;
		}
		if (!__instance.IsSleeping() && !__instance.IsWounded() && !__instance.CurrentGestureIsSurrendering && !__instance.IsRestrainedOrSurrendering)
		{
			return true;
		}
		TruePVEMod instance = TruePVEMod.Instance;
		if (instance?.Config?.PreventLooting == null || !instance.Config.PreventLooting.Enabled)
		{
			return true;
		}
		if (instance.Config.PreventLooting.AdminCanLoot && TruePVEMod.IsAdminOrDeveloperLooter(player))
		{
			return true;
		}
		if (instance.IsAlly(__instance.userID, player.userID))
		{
			return true;
		}
		if (instance.Config.PreventLooting.AllowLootingSleepers)
		{
			return true;
		}
		instance.LogLootBlocked(player, __instance, "BasePlayer.CanBeLooted denied");
		__result = false;
		return false;
	}
}
