using ConVar;
using HarmonyLib;

namespace BagCooldowns;

[HarmonyPatch(typeof(ServerMgr), "UpdateServerInformation")]
internal class ServerMgr_UpdateServerInformation
{
	/// <summary>
	/// Ensures "modded" is in server tags before UpdateServerInformation builds the tag string.
	/// Game now uses ServerTagCompressor.CompressTags() - tags are read from ConVar.Server.tags.
	/// </summary>
	[HarmonyPrefix]
	internal static void Prefix()
	{
		try
		{
			var tags = Server.tags ?? "";
			if (!tags.ToLowerInvariant().Contains("modded"))
			{
				Server.tags = string.IsNullOrWhiteSpace(tags) ? "modded" : tags.Trim(',').Trim() + ",modded";
			}
		}
		catch (System.Exception ex)
		{
			UnityEngine.Debug.LogException(ex);
		}
	}
}
