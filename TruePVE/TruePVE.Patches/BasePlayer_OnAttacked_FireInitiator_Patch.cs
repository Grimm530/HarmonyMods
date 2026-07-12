using System;
using HarmonyLib;
using UnityEngine;

namespace TruePVE.Patches;

/// <summary>
/// FireBall radial damage may use the fire entity as <see cref="HitInfo.Initiator"/> instead of the shooting player.
/// Normalize initiator before <see cref="BasePlayer.OnAttacked"/> so vanilla <c>server.pve</c> and our Hurt patch apply.
/// </summary>
[HarmonyPatch(typeof(BasePlayer), "OnAttacked", new Type[] { typeof(HitInfo) })]
[HarmonyPriority(Priority.First)]
public static class BasePlayer_OnAttacked_FireInitiator_Patch
{
	[HarmonyPrefix]
	public static void Prefix(HitInfo info)
	{
		if (info == null || info.Initiator is BasePlayer)
		{
			return;
		}
		if (info.Initiator is FireBall || info.InitiatorPlayer == null)
		{
			BasePlayer owner = PvEDamageHelpers.ResolvePlayerInitiator(info);
			if ((Object)(object)owner != (Object)null)
			{
				info.Initiator = owner;
			}
		}
	}
}
