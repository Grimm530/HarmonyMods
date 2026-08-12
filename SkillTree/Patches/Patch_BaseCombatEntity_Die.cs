// OnEntityDeath / OnPlayerDeath — postfix on BaseCombatEntity.Die.
// ScientistNPC / ScarecrowNPC / etc. inherit BasePlayer, so typed NPC XP must run
// BEFORE the generic BasePlayer branch (otherwise scientist kill XP is dead).
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

            // Typed NPC deaths (ScientistNPC is a BasePlayer subclass).
            if (__instance is ScarecrowNPC ||
                __instance is GingerbreadNPC ||
                __instance is ScientistNPC ||
                __instance is TunnelDweller ||
                __instance is UnderwaterDweller)
            {
                STPlugin.Dispatch_OnPlayerDeathNpc(__instance, info);
                return;
            }

            if (__instance is BasePlayer player)
            {
                STPlugin.Dispatch_OnPlayerDeath(player, info);
                return;
            }

            STPlugin.Dispatch_OnEntityDeath(__instance, info);
        }
    }

    // Ore nodes call ResourceEntity.OnDied (not BaseCombatEntity.Die) — Node_Spawn_Chance.
    [HarmonyPatch(typeof(ResourceEntity), nameof(ResourceEntity.OnDied))]
    public static class ResourceEntity_OnDied_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResourceEntity __instance, HitInfo info)
        {
            if (__instance == null) return;
            STPlugin.Dispatch_OnEntityDeath(__instance, info);
        }
    }
}
