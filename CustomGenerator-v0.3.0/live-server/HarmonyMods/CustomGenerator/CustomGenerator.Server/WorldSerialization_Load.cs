using System;
using System.Reflection;
using HarmonyLib;
using ProtoBuf;

namespace CustomGenerator.Server;

[HarmonyPatch]
internal static class WorldSerialization_Load
{
	internal static byte[] Layer;

	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(WorldSerialization), "Load", new Type[1] { typeof(string) }, (Type[])null);
	}

	private static void Postfix(WorldSerialization __instance)
	{
		MapData map = __instance.GetMap("customgenerator");
		if (map != null)
		{
			Layer = map.data;
		}
	}
}
