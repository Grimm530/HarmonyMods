using System;
using System.Reflection;
using HarmonyLib;

namespace StackManager.Patches;

/// <summary>When Harmony mod host is loaded, we patch a no-op instead of ServerMgr.UpdateServerInformation so Harmony does not read the legacy-patched body (avoids MissingMethodException).</summary>
[HarmonyPatch]
public class ServerMgr_UpdateServerInformation
{
	private static readonly bool HarmonyModLoaded;

	static ServerMgr_UpdateServerInformation()
	{
		HarmonyModLoaded = Type.GetType("Harmony.Core.HarmonyModInterface, Harmony.Core") != null;
	}

	static MethodBase TargetMethod()
	{
		if (HarmonyModLoaded)
			return AccessTools.Method(typeof(ServerMgr_UpdateServerInformation), nameof(NoOp));
		return AccessTools.Method(typeof(ServerMgr), "UpdateServerInformation");
	}

	static void NoOp() { }
}
