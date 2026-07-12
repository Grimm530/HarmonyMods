using HarmonyLib;

namespace AdminAlias.Patches
{
    /// <summary>
    /// When displayName is read, return the configured alias for this player's Steam ID if present.
    /// </summary>
    [HarmonyPatch(typeof(BasePlayer), "get_displayName")]
    public static class BasePlayer_get_displayName_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance, ref string __result)
        {
            if (__instance == null) return;
            var overrideName = AdminAliasConfig.GetOverride(__instance.userID);
            if (overrideName != null)
                __result = overrideName;
        }
    }
}
