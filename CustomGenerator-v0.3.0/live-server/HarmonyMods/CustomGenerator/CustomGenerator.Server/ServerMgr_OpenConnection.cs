using System;
using System.Reflection;
using CustomGenerator.Shared;
using HarmonyLib;

namespace CustomGenerator.Server;

[HarmonyPatch]
internal static class ServerMgr_OpenConnection
{
	private static bool _applied;

	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(ServerMgr), "OpenConnection", (Type[])null, (Type[])null);
	}

	private static void Postfix()
	{
		if (_applied)
		{
			return;
		}
		_applied = true;
		object obj = WorldSerialization_Load.Layer;
		if (obj == null)
		{
			WorldSerialization serialization = World.Serialization;
			obj = ((serialization == null) ? null : serialization.GetMap("customgenerator")?.data);
		}
		byte[] array = (byte[])obj;
		if (array == null)
		{
			Log.Info("This map has no CustomGenerator data, nothing to apply");
			return;
		}
		MapExtras mapExtras;
		try
		{
			mapExtras = MapExtras.FromBytes(array);
		}
		catch (Exception ex)
		{
			Log.Warning("Can't read the CustomGenerator map data: " + ex.Message);
			return;
		}
		if (mapExtras.Version > 1)
		{
			Log.Warning($"The map data is version {mapExtras.Version}, this mod knows up to {1}: update CustomGenerator.Server");
		}
		try
		{
			IOModule.Apply(mapExtras.IO);
		}
		catch (Exception ex2)
		{
			Log.Warning("IO: failed: " + ex2);
		}
	}
}
