using System;
using System.Reflection;
using CustomGenerator.Utility;
using HarmonyLib;

namespace CustomGenerator.Patches;

[HarmonyPatch]
internal static class Timing_Start
{
	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(Timing), "Start", new Type[1] { typeof(string) }, (Type[])null);
	}

	private static void Prefix(ref string name)
	{
		if (!(name != "Processing World"))
		{
			if (ExtConfig.Config.Generator.RemoveRivers)
			{
				World.Config.Rivers = false;
				Logging.Generation("Rivers disabled");
			}
			LoadPercentages();
			Logging.Generation("Changing tier percentages...");
		}
	}

	private static void LoadPercentages()
	{
		if (ExtConfig.Config.Generator.ModifyPercentages)
		{
			float num = ExtConfig.Config.Generator.Tier.Tier0 + ExtConfig.Config.Generator.Tier.Tier1 + ExtConfig.Config.Generator.Tier.Tier2;
			float num2 = ExtConfig.Config.Generator.Biom.Arid + ExtConfig.Config.Generator.Biom.Arctic + ExtConfig.Config.Generator.Biom.Temperate + ExtConfig.Config.Generator.Biom.Tundra + ExtConfig.Config.Generator.Biom.Jungle;
			World.Config.PercentageTier0 = ((num >= 100f) ? (ExtConfig.Config.Generator.Tier.Tier0 / num) : ExtConfig.Config.Generator.Tier.Tier0);
			World.Config.PercentageTier1 = ((num >= 100f) ? (ExtConfig.Config.Generator.Tier.Tier1 / num) : ExtConfig.Config.Generator.Tier.Tier1);
			World.Config.PercentageTier2 = ((num >= 100f) ? (ExtConfig.Config.Generator.Tier.Tier2 / num) : ExtConfig.Config.Generator.Tier.Tier2);
			if (num < 100f)
			{
				Logging.Error("Tier perc. summs lower than 100! Set default.");
			}
			World.Config.PercentageBiomeArid = ((num2 >= 100f) ? (ExtConfig.Config.Generator.Biom.Arid / num2) : ExtConfig.Config.Generator.Biom.DefaultArid);
			World.Config.PercentageBiomeArctic = ((num2 >= 100f) ? (ExtConfig.Config.Generator.Biom.Arctic / num2) : ExtConfig.Config.Generator.Biom.DefaultArctic);
			World.Config.PercentageBiomeTemperate = ((num2 >= 100f) ? (ExtConfig.Config.Generator.Biom.Temperate / num2) : ExtConfig.Config.Generator.Biom.DefaultTemperate);
			World.Config.PercentageBiomeTundra = ((num2 >= 100f) ? (ExtConfig.Config.Generator.Biom.Tundra / num2) : ExtConfig.Config.Generator.Biom.DefaultTundra);
			World.Config.PercentageBiomeJungle = ((num2 >= 100f) ? (ExtConfig.Config.Generator.Biom.Jungle / num2) : ExtConfig.Config.Generator.Biom.DefaultJungle);
			if (num2 < 100f)
			{
				Logging.Error("Biom perc. summs lower than 100! Set default.");
			}
		}
	}
}
