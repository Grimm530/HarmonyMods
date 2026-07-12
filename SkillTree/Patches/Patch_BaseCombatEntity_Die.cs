// OnEntityDeath / OnPlayerDeath — postfix on BaseCombatEntity.Die.
// Routes to typed overloads: BasePlayer -> OnPlayerDeath + typed NPC variants,
// ScientistNPC2 -> OnEntityDeath(ScientistNPC2), others -> OnEntityDeath(BaseEntity).
using HarmonyLib;
using STPlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) })]
    public static class Patch_BaseCombatEntity_Die
    {
        [HarmonyPostfix]
        public static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            if (__instance == null) return;

            if (__instance is BasePlayer player)
            {
                STPlugin.Dispatch_OnPlayerDeath(player, info);
                return;
            }

            if (__instance is ScarecrowNPC   ||
                __instance is GingerbreadNPC  ||
                __instance is ScientistNPC    ||
                __instance is TunnelDweller   ||
                __instance is UnderwaterDweller)
            {
                STPlugin.Dispatch_OnPlayerDeathNpc(__instance, info);
                return;
            }

            STPlugin.Dispatch_OnEntityDeath(__instance, info);
        }
    }
}
