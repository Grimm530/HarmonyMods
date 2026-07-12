using System;
using HarmonyLib;

namespace TruePVE.Patches;

[HarmonyPatch(typeof(BaseCombatEntity), "OnAttacked", new Type[] { typeof(HitInfo) })]
public static class BaseCombatEntity_OnAttacked_LootDefender_Patch
{
	[HarmonyPostfix]
	public static void Postfix(BaseCombatEntity __instance, HitInfo info)
	{
		if (TruePVEMod.Instance?.Config?.LootDefender == null || !TruePVEMod.Instance.Config.LootDefender.Enabled || __instance?.net == null || info == null)
		{
			return;
		}
		BasePlayer attacker = info.InitiatorPlayer;
		if ((Object)(object)attacker == (Object)null && (Object)(object)info.Initiator != (Object)null && info.Initiator.OwnerID != 0UL && LootDefenderHelpers.IsSteamId(info.Initiator.OwnerID))
		{
			attacker = BasePlayer.FindByID(info.Initiator.OwnerID);
		}
		if ((Object)(object)attacker == (Object)null || !LootDefenderHelpers.IsSteamId(attacker.userID))
		{
			return;
		}
		float num = info.damageTypes?.Total() ?? 0f;
		if (!(num <= 0f))
		{
			string weapon = info.Weapon?.GetItem()?.info?.shortname ?? "";
			LootDefenderState.RecordDamage(__instance.net.ID.Value, attacker, num, weapon);
		}
	}
}
