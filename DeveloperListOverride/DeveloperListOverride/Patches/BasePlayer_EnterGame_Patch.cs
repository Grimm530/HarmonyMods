using System.Reflection;
using HarmonyLib;

namespace DeveloperListOverride.Patches
{
    /// <summary>
    /// EnterGame runs after the client has the player snapshot (IsDeveloper flag).
    /// Sending client.skins_access from PlayerInit is too early — the F1/repair-bench
    /// picker still filters to Steam-owned skins until this command sticks locally.
    /// </summary>
    [HarmonyPatch]
    public static class BasePlayer_EnterGame_Patch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(BasePlayer), "EnterGame");
        }

        static void Postfix(BasePlayer __instance)
        {
            DeveloperListOverrideMod.SendSkinsAccess(__instance);
        }
    }
}
