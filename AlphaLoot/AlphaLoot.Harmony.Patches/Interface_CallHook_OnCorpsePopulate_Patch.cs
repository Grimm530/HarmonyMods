using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AlphaLoot.Harmony.Patches;

/// <summary>
/// Optional bridge for Oxide OnCorpsePopulate when Oxide.Core is loaded.
/// Applied manually from AlphaLootMod.OnLoaded — no [HarmonyPatch] attribute so PatchAll does not require Oxide at load time.
/// </summary>
public static class Interface_CallHook_OnCorpsePopulate_Patch
{
	public static bool TryApply(HarmonyLib.Harmony harmony)
	{
		try
		{
			var interfaceType = Type.GetType("Oxide.Core.Interface, Oxide.Core", throwOnError: false);
			if (interfaceType == null)
				return false;

			var method = AccessTools.Method(interfaceType, "CallHook", new[] { typeof(string), typeof(object), typeof(object) });
			if (method == null)
				return false;

			var postfix = new HarmonyMethod(typeof(Interface_CallHook_OnCorpsePopulate_Patch), nameof(Postfix));
			harmony.Patch(method, postfix: postfix);
			return true;
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[AlphaLoot] Oxide OnCorpsePopulate bridge not applied: " + ex.Message);
			return false;
		}
	}

	public static void Postfix(string hook, object obj1, object obj2, ref object __result)
	{
		if (__result != null || hook != "OnCorpsePopulate")
			return;

		AlphaLootMod instance = AlphaLootMod.Instance;
		if (instance == null || obj1 is not BaseEntity entity || obj2 is not LootableCorpse corpse)
			return;

		if ((Object)(object)entity == (Object)null || (Object)(object)corpse == (Object)null)
			return;

		if (!instance.TryGetNPCProfile(entity.ShortPrefabName, out BaseLootProfile profile) || !profile.Enabled)
			return;

		instance.PopulateCorpseLoot(entity, corpse);
		__result = corpse;
	}
}
