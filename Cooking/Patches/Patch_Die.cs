using HarmonyLib;
using CookingPlugin = Oxide.Plugins.Cooking;

namespace CookingHarmony.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) })]
    public static class Patch_Die
    {
        [HarmonyPostfix]
        public static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            if (__instance == null) return;

            if (__instance is BasePlayer player)
            {
                CookingPlugin.Dispatch_OnPlayerDeath(player, info);
                return;
            }

            CookingPlugin.Dispatch_OnEntityDeath(__instance, info);
        }
    }
}
