// OnEntityDeath / OnPlayerDeath — Prefix at Oxide CallHook timing (NOT Die postfix).
//
// Oxide fires OnEntityDeath inside BaseCombatEntity.Die BEFORE OnDied/DropItems/Kill.
// A Die Postfix runs after loot has already dropped and IsDestroyed is true, so
// OnEntityDeath's early `entity.IsDestroyed` return kills Loot Magnet (HandleLootPickup),
// barrel XP, Node_Spawn_Chance (via ResourceEntity), etc.
//
// This server has no Oxide CallHook strings in game IL, so transpilers never match.
// Prefix calls the same typed dispatch SkillTree already uses.
using HarmonyLib;
using STPlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) })]
    public static class Patch_BaseCombatEntity_Die
    {
        [HarmonyPrefix]
        public static void Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            try
            {
                if (__instance != null)
                    STPlugin.Dispatch_OnEntityDeath(__instance, info);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[SkillTree] OnEntityDeath: " + ex.Message);
            }
        }
    }

    // Ore nodes call ResourceEntity.OnDied (not BaseCombatEntity.Die) — Node_Spawn_Chance.
    [HarmonyPatch(typeof(ResourceEntity), nameof(ResourceEntity.OnDied))]
    public static class ResourceEntity_OnDied_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(ResourceEntity __instance, HitInfo info)
        {
            try
            {
                if (__instance != null)
                    STPlugin.Dispatch_OnEntityDeath(__instance, info);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[SkillTree] OnEntityDeath(ResourceEntity): " + ex.Message);
            }
        }
    }

    // OnPlayerDeath — BasePlayer.Die (can cancel death when non-null).
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die), new[] { typeof(HitInfo) })]
    public static class Patch_BasePlayer_Die
    {
        [HarmonyPrefix]
        public static bool Prefix(BasePlayer __instance, HitInfo info)
        {
            try
            {
                if (__instance != null && STPlugin.Dispatch_OnPlayerDeathHook(__instance, info) != null)
                    return false;
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[SkillTree] OnPlayerDeath: " + ex.Message);
            }
            return true;
        }
    }
}
