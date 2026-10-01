using System;
using System.Reflection;
using HarmonyLib;
using Unity.Collections;

namespace CustomGenerator.Generators;

[HarmonyPatch]
internal static class GenerateRoadTopology_Process
{
	private static FieldRef<TerrainTopologyMap, int> _res = AccessTools.FieldRefAccess<TerrainTopologyMap, int>("res");

	private static FieldRef<TerrainTopologyMap, NativeArray<int>> _dst = AccessTools.FieldRefAccess<TerrainTopologyMap, NativeArray<int>>("dst");

	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(GenerateRoadTopology), "Process", (Type[])null, (Type[])null);
	}

	private static void Postfix()
	{
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		if (!ExtConfig.Config.Generator.AllowRoadBuild)
		{
			return;
		}
		_ = TerrainMeta.HeightMap;
		TerrainTopologyMap topologyMap = TerrainMeta.TopologyMap;
		NativeArray<int> map = _dst.Invoke(topologyMap);
		int res = _res.Invoke(topologyMap);
		ImageProcessing.Dilate2D(map, res, res, 2048, 1, delegate(int x, int y)
		{
			if ((map[x * res + y] & 0x31) != 0)
			{
				ref NativeArray<int> reference = ref map;
				int num = x * res + y;
				reference[num] &= -2049;
				reference = ref map;
				num = x * res + y;
				reference[num] |= 0x200000;
			}
		});
	}
}
