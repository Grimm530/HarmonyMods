using HarmonyLib;

namespace NoActiveItemDrop;

[HarmonyPatch(typeof(BasePlayer), "ShouldDropActiveItem")]
public class BasePlayer_ShouldDropActiveItem
{
	[HarmonyPrefix]
	private static bool Prefix()
	{
		return false;
	}
}
