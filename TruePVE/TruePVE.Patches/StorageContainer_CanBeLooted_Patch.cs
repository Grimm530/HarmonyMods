using System;
using HarmonyLib;
using UnityEngine;

namespace TruePVE.Patches;

[HarmonyPatch(typeof(StorageContainer), "CanBeLooted", new Type[] { typeof(BasePlayer) })]
public static class StorageContainer_CanBeLooted_Patch
{
	[HarmonyPrefix]
	public static bool Prefix(StorageContainer __instance, BasePlayer player, ref bool __result)
	{
		if ((Object)(object)__instance == (Object)null || (Object)(object)player == (Object)null)
		{
			return true;
		}
		TruePVEMod instance = TruePVEMod.Instance;
		if (instance?.Config?.PreventLooting == null || !instance.Config.PreventLooting.Enabled)
		{
			instance?.LogLootAllowed(player, __instance, "prevent looting disabled in CanBeLooted");
			return true;
		}
		if (__instance is BuildingPrivlidge && !instance.CanAccessToolCupboard(player, __instance))
		{
			return Block(instance, player, __instance, "tool cupboard owner or teammate required in CanBeLooted", ref __result);
		}
		if (instance.Config.PreventLooting.AdminCanLoot && TruePVEMod.IsAdminOrDeveloperLooter(player))
		{
			return Allow(instance, player, __instance, "admin override in CanBeLooted");
		}
		if (__instance.OwnerID == 0L)
		{
			return Allow(instance, player, __instance, "unowned storage in CanBeLooted");
		}
		if (instance.ShouldAllowStorageAccess(player, __instance))
		{
			return Allow(instance, player, __instance, "storage owner, ally, or cupboard auth in CanBeLooted");
		}
		if (!instance.Config.PreventLooting.AllowLootingStorageContainers)
		{
			return Block(instance, player, __instance, "storage access denied in CanBeLooted", ref __result);
		}
		return Allow(instance, player, __instance, "storage looting allowed by config in CanBeLooted");
	}

	private static bool Allow(TruePVEMod instance, BasePlayer looter, BaseEntity target, string reason)
	{
		instance.LogLootAllowed(looter, target, reason);
		return true;
	}

	private static bool Block(TruePVEMod instance, BasePlayer looter, BaseEntity target, string reason, ref bool result)
	{
		instance.LogLootBlocked(looter, target, reason);
		result = false;
		return false;
	}
}
