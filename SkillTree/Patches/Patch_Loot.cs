// OnLootEntity / CanLootEntity / OnLootEntityEnd
// LootContainer does not declare PlayerOpenLoot (inherits StorageContainer) — patching
// LootContainer.PlayerOpenLoot fails with "Undefined target method". Use PlayerLoot like ArmoredTrain.
using HarmonyLib;
using UnityEngine;
using STPlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony.Patches
{
    /// <summary>
    /// OnLootEntity / CanLootEntity — when a player successfully starts looting a LootContainer.
    /// </summary>
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity), new[] { typeof(BaseEntity), typeof(bool) })]
    public static class PlayerLoot_StartLootingEntity_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerLoot __instance, BaseEntity targetEntity, bool __result)
        {
            if (!__result || __instance == null || targetEntity == null) return;
            BasePlayer player = __instance.baseEntity;
            if (player == null) return;

            if (targetEntity is LootContainer loot)
            {
                try
                {
                    STPlugin.Dispatch_CanLootEntity(player, loot);
                    STPlugin.Dispatch_OnLootEntity(player, loot);
                }
                catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnLootEntity: " + ex.Message); }
            }
            else if (targetEntity is ChickenCoop coop)
            {
                try { STPlugin.Dispatch_OnLootEntity_ChickenCoop(player, coop); }
                catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnLootEntity(ChickenCoop): " + ex.Message); }
            }
        }
    }

    /// <summary>
    /// OnLootEntityEnd — PlayerLoot.Clear is the stop-looting entry; entitySource is still set in prefix.
    /// </summary>
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.Clear))]
    public static class PlayerLoot_Clear_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(PlayerLoot __instance)
        {
            BasePlayer player = __instance?.baseEntity;
            BaseEntity source = __instance?.entitySource;
            if (player == null || source == null) return;
            if (source is StorageContainer sc)
            {
                try { STPlugin.Dispatch_OnLootEntityEnd(player, sc); }
                catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnLootEntityEnd: " + ex.Message); }
            }
        }
    }
}
