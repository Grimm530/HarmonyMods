using System.Reflection;
using HarmonyLib;

namespace DeveloperListOverride.Patches
{
    /// <summary>
    /// After the game sets IsDeveloper from DeveloperList, force it true for our override list.
    /// This guarantees the player flag is set so all game code that checks player.IsDeveloper works.
    /// </summary>
    [HarmonyPatch]
    public static class BasePlayer_PlayerInit_Patch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(BasePlayer), "PlayerInit");
        }

        static void Postfix(BasePlayer __instance)
        {
            if (__instance == null) return;
            if (DeveloperListOverrideConfig.IsOverrideDeveloper(__instance.UserIDString))
                __instance.SetPlayerFlag(BasePlayer.PlayerFlags.IsDeveloper, true);
        }
    }
}
