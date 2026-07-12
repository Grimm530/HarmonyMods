using HarmonyLib;

namespace DeveloperListOverride.Patches
{
    /// <summary>
    /// Override list developers count as having all blueprints (no need to research/unlock).
    /// </summary>
    [HarmonyPatch(typeof(PlayerBlueprints), nameof(PlayerBlueprints.HasUnlocked))]
    public static class PlayerBlueprints_HasUnlocked_Patch
    {
        static bool Prefix(PlayerBlueprints __instance, ref bool __result)
        {
            var player = __instance?.GetComponent<BasePlayer>();
            if (player == null) return true;
            if (!DeveloperListOverrideConfig.IsOverrideDeveloper(player.UserIDString))
                return true;
            __result = true;
            return false;
        }
    }
}
