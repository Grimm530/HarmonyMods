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
			if (ExtConfig.Config.Generator.RemovePowerlines)
			{
				World.Config.Powerlines = false;
				Logging.Generation("Powerlines disabled");
			}
			if (ExtConfig.Config.Generator.RemoveTunnels)
			{
				World.Config.BelowGroundRails = false;
				Logging.Generation("Underground tunnels disabled");
			}
			LoadPercentages();
		}
	}

	private static void LoadPercentages()
	{
		if (ExtConfig.Config.Generator.ModifyPercentages)
		{
			ExtConfig.TierSettings tier = ExtConfig.Config.Generator.Tier;
			ExtConfig.BiomSettings biom = ExtConfig.Config.Generator.Biom;
			float num = tier.Tier0 + tier.Tier1 + tier.Tier2;
			World.Config.PercentageTier0 = tier.Tier0 / num;
			World.Config.PercentageTier1 = tier.Tier1 / num;
			World.Config.PercentageTier2 = tier.Tier2 / num;
			float num2 = biom.Arid + biom.Temperate + biom.Tundra + biom.Arctic;
			World.Config.PercentageBiomeArid = biom.Arid / num2;
			World.Config.PercentageBiomeTemperate = biom.Temperate / num2;
			World.Config.PercentageBiomeTundra = biom.Tundra / num2;
			World.Config.PercentageBiomeArctic = biom.Arctic / num2;
			World.Config.PercentageBiomeJungle = biom.Jungle / 100f;
			Logging.Generation("Tier and biome percentages changed");
		}
	}
}
