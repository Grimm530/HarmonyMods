using HarmonyLib;

namespace DeveloperListOverride.Patches
{
    /// <summary>
    /// Grant full skin access (repair bench, etc.) to override list developers.
    /// We patch GetSkinsAccessLevel and the property getters (by explicit name for compatibility).
    /// </summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.GetSkinsAccessLevel))]
    public static class BasePlayer_GetSkinsAccessLevel_Patch
    {
        static void Postfix(BasePlayer __instance, ref int __result)
        {
            if (__instance != null && DeveloperListOverrideConfig.IsOverrideDeveloper(__instance.UserIDString))
                __result = 1;
        }
    }

    [HarmonyPatch(typeof(BasePlayer), "get_AllSkinsUnlocked")]
    public static class BasePlayer_get_AllSkinsUnlocked_Patch
    {
        static bool Prefix(BasePlayer __instance, ref bool __result)
        {
            if (!DeveloperListOverrideConfig.IsOverrideDeveloper(__instance?.UserIDString ?? ""))
                return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(BasePlayer), "get_AllSkinsLocked")]
    public static class BasePlayer_get_AllSkinsLocked_Patch
    {
        static bool Prefix(BasePlayer __instance, ref bool __result)
        {
            if (!DeveloperListOverrideConfig.IsOverrideDeveloper(__instance?.UserIDString ?? ""))
                return true;
            __result = false;
            return false;
        }
    }
}
