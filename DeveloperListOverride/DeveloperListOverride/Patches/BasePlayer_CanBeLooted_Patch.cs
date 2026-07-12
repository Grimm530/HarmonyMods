using HarmonyLib;

namespace DeveloperListOverride.Patches
{
    /// <summary>
    /// Allow override developers (and admins/developers) to loot any player by holding R.
    /// Vanilla only allows looting when target is wounded/sleeping/surrendering/restrained.
    /// </summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.CanBeLooted))]
    public static class BasePlayer_CanBeLooted_Patch
    {
        static bool Prefix(BasePlayer __instance, BasePlayer player, ref bool __result)
        {
            if (player == null || player == __instance)
                return true;
            if (player.IsAdmin || player.IsDeveloper)
            {
                __result = true;
                return false;
            }
            if (DeveloperListOverrideConfig.IsOverrideDeveloper(player.UserIDString))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
}
