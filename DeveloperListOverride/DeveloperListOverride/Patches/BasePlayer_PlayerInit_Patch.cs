using System.Reflection;
using HarmonyLib;

namespace DeveloperListOverride.Patches
{
    /// <summary>
    /// PlayerInit already sent a snapshot before Harmony postfix would run if we
    /// only set the flag here. Apply full developer state and re-sync to the client.
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
            DeveloperListOverrideMod.ApplyDeveloperPrivileges(__instance);
        }
    }
}
