using UnityEngine;

namespace BagCooldowns;

public static class HarmonyMethods
{
	public static void SetBagTimers()
	{
		foreach (SleepingBag item in SleepingBag.sleepingBags)
		{
			SetSecondsBetweenReUses(item);
		}
	}

	public static void SetSecondsBetweenReUses(SleepingBag sleepingBag)
	{
		if (sleepingBag is StaticRespawnArea)
			return;

		var val = (int)sleepingBag.RespawnType;
		// RespawnType: 0=Static, 1=SleepingBag, 2=Bed, 3=BeachTowel, 4=Camper
		if (val < 1)
			return;

		switch (val - 1)
		{
		case 0:
			sleepingBag.secondsBetweenReuses = HarmonyConfig.Config.SleepingBag.SecondsBetweenReuses;
			break;
		case 1:
			sleepingBag.secondsBetweenReuses = HarmonyConfig.Config.Bed.SecondsBetweenReuses;
			break;
		case 2:
			sleepingBag.secondsBetweenReuses = HarmonyConfig.Config.BeachTowel.SecondsBetweenReuses;
			break;
		case 3:
			sleepingBag.secondsBetweenReuses = HarmonyConfig.Config.Camper.SecondsBetweenReuses;
			break;
		default:
			return;
		}
		CheckUnlockTime(sleepingBag);
	}

	public static void SetUnlockTime(SleepingBag sleepingBag)
	{
		if (sleepingBag is StaticRespawnArea)
			return;

		var val = (int)sleepingBag.RespawnType;
		if (val < 1)
			return;

		float unlockSeconds = (val - 1) switch
		{
			0 => HarmonyConfig.Config.SleepingBag.UnlockSeconds,
			1 => HarmonyConfig.Config.Bed.UnlockSeconds,
			2 => HarmonyConfig.Config.BeachTowel.UnlockSeconds,
			3 => HarmonyConfig.Config.Camper.UnlockSeconds,
			_ => HarmonyConfig.Config.SleepingBag.UnlockSeconds
		};
		sleepingBag.SetUnlockTime(unlockSeconds + Time.realtimeSinceStartup);
		CheckUnlockTime(sleepingBag);
	}

	public static void ResetBagTimers()
	{
		foreach (SleepingBag item in SleepingBag.sleepingBags)
		{
			if (item is StaticRespawnArea)
				continue;

			var val = (int)item.RespawnType;
			if (val < 1)
				continue;

			item.secondsBetweenReuses = (val - 1) switch
			{
				0 => 300f,
				1 => 120f,
				2 => 300f,
				3 => 300f,
				_ => 300f
			};
			CheckUnlockTime(item);
		}
	}

	private static void CheckUnlockTime(SleepingBag sleepingBag)
	{
		if (sleepingBag.unlockSeconds > sleepingBag.secondsBetweenReuses)
		{
			sleepingBag.SetUnlockTime(Time.realtimeSinceStartup + sleepingBag.secondsBetweenReuses);
		}
	}
}
