using CustomGenerator.Utility;
using HarmonyLib;
using Rust.Ai;

namespace CustomGenerator;

[HarmonyPatch(typeof(Bootstrap), "StartupShared")]
internal static class Bootstrap_StartupShared
{
	[HarmonyPrefix]
	private static void Prefix()
	{
		Logging.StartingMessage();
		AiManager.nav_disable = true;
		AiManager.nav_wait = false;
		Logging.ClearOldLogs();
	}
}
