using HarmonyLib;
using CCPlugin = Oxide.Plugins.CombatClasses;

namespace CombatClassesHarmony.Patches
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
                CCPlugin.Dispatch_OnPlayerDeath(player, info);
                // Also XP/kill tracking for player deaths may use OnEntityDeath in some paths —
                // CombatClasses OnEntityDeath handles NPCs/animals; OnPlayerDeath handles players.
                return;
            }

            CCPlugin.Dispatch_OnEntityDeath(__instance, info);
        }
    }
}
