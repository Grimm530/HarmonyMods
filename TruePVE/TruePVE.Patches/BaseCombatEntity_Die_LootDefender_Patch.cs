using HarmonyLib;

namespace TruePVE.Patches;

[HarmonyPatch(typeof(BaseCombatEntity), "Die")]
public static class BaseCombatEntity_Die_LootDefender_Patch
{
	[HarmonyPostfix]
	public static void Postfix(BaseCombatEntity __instance)
	{
		if (__instance?.net == null || TruePVEMod.Instance?.Config?.LootDefender == null || !TruePVEMod.Instance.Config.LootDefender.Enabled)
		{
			return;
		}
		float lockRadius = TruePVEMod.Instance.Config.LootDefender.LockRadius;
		if (__instance is PatrolHelicopter)
		{
			if (TruePVEMod.Instance.Config.LootDefender.LockHeli)
			{
				int heliLockTime = TruePVEMod.Instance.Config.LootDefender.HeliLockTime;
				LootDefenderState.ApplyPositionLock(__instance, LootDefenderState.LockType.Heli, heliLockTime, lockRadius);
			}
		}
		else if (__instance is BradleyAPC)
		{
			if (TruePVEMod.Instance.Config.LootDefender.LockBradley)
			{
				int heliLockTime = TruePVEMod.Instance.Config.LootDefender.BradleyLockTime;
				LootDefenderState.ApplyPositionLock(__instance, LootDefenderState.LockType.Bradley, heliLockTime, lockRadius);
			}
		}
		else if ((__instance is BaseNpc || ((object)__instance).GetType().Name == "BaseNPC2") && TruePVEMod.Instance.Config.LootDefender.LockNpc && !LootDefenderHelpers.ShouldSkipLootDefenderNpcLock(__instance))
		{
			int heliLockTime = TruePVEMod.Instance.Config.LootDefender.NpcLockTime;
			LootDefenderState.ApplyPositionLock(__instance, LootDefenderState.LockType.NPC, heliLockTime, lockRadius);
		}
	}
}
