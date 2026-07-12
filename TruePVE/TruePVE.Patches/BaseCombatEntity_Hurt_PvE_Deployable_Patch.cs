using System;
using HarmonyLib;

namespace TruePVE.Patches;

[HarmonyPatch(typeof(BaseCombatEntity), "Hurt", new Type[] { typeof(HitInfo) })]
[HarmonyPriority(Priority.First)]
public static class BaseCombatEntity_Hurt_PvE_Deployable_Patch
{
	[HarmonyPrefix]
	public static bool Prefix(BaseCombatEntity __instance, HitInfo info)
	{
		if (!PvEDamageHelpers.ShouldBlockPlayerOwnedEntityDamage(__instance, info, out BasePlayer attacker))
		{
			return true;
		}
		float blockedDamage = info.damageTypes?.Total() ?? 0f;
		info.damageTypes?.Clear();
		if ((Object)(object)attacker != (Object)null && blockedDamage > 0f)
		{
			attacker.Hurt(blockedDamage, Rust.DamageType.Generic);
		}
		return false;
	}
}
