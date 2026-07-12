using System;
using System.Reflection;
using HarmonyLib;

namespace StackManager.Patches;

/// <summary>When Oxide is loaded, we patch a no-op instead of ServerMgr.UpdateServerInformation so Harmony does not read the Oxide-patched body (avoids MissingMethodException).</summary>
[HarmonyPatch]
public class ServerMgr_UpdateServerInformation
{
	private static readonly bool OxideLoaded;

	static ServerMgr_UpdateServerInformation()
	{
		OxideLoaded = Type.GetType("Oxide.Core.Interface, Oxide.Core") != null;
	}

	static MethodBase TargetMethod()
	{
		if (OxideLoaded)
			return AccessTools.Method(typeof(ServerMgr_UpdateServerInformation), nameof(NoOp));
		return AccessTools.Method(typeof(ServerMgr), "UpdateServerInformation");
	}

	static void NoOp() { }
}
