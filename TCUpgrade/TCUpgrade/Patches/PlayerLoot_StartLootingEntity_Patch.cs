using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TCUpgrade.Patches;

[HarmonyPatch(typeof(PlayerLoot), "StartLootingEntity")]
public static class PlayerLoot_StartLootingEntity_Patch
{
	private static PropertyInfo _baseEntityProp;

	[HarmonyPostfix]
	public static void Postfix(PlayerLoot __instance, BaseEntity targetEntity, bool __result)
	{
		if (__result && !((Object)(object)targetEntity == (Object)null) && targetEntity is BuildingPrivlidge cup && TCUpgradeMod.Instance != null)
		{
			BasePlayer player = GetPlayer(__instance);
			if (!((Object)(object)player == (Object)null))
			{
				TCUpgradeMod.Instance.OnLootStarted(player, cup);
			}
		}
	}

	private static BasePlayer GetPlayer(PlayerLoot loot)
	{
		if ((Object)(object)loot == (Object)null)
		{
			return null;
		}
		try
		{
			BasePlayer componentInParent = ((Component)loot).GetComponentInParent<BasePlayer>();
			if ((Object)(object)componentInParent != (Object)null)
			{
				return componentInParent;
			}
			if ((object)_baseEntityProp == null)
			{
				_baseEntityProp = AccessTools.Property(((object)loot).GetType().BaseType, "baseEntity");
			}
			return _baseEntityProp?.GetValue(loot) as BasePlayer;
		}
		catch
		{
			return null;
		}
	}
}
