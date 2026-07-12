// OnLootEntity / CanLootEntity / OnLootEntityEnd
using HarmonyLib;
using UnityEngine;
using STPlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony.Patches
{
    /// <summary>
    /// OnLootEntity — postfix on LootContainer.PlayerOpenLoot.
    /// CanLootEntity (void) also called here for the plugin to record the event.
    /// </summary>
    [HarmonyPatch(typeof(LootContainer), nameof(LootContainer.PlayerOpenLoot), typeof(BasePlayer), typeof(string), typeof(bool))]
    public static class LootContainer_PlayerOpenLoot_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(LootContainer __instance, BasePlayer player, bool __result)
        {
            if (__instance == null || player == null || !__result) return;
            try
            {
                STPlugin.Dispatch_CanLootEntity(player, __instance);
                STPlugin.Dispatch_OnLootEntity(player, __instance);
            }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnLootEntity: " + ex.Message); }
        }
    }

    /// <summary>OnLootEntityEnd — postfix on StorageContainer.PlayerStoppedLooting.</summary>
    [HarmonyPatch(typeof(StorageContainer), nameof(StorageContainer.PlayerStoppedLooting))]
    public static class StorageContainer_PlayerStoppedLooting_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(StorageContainer __instance, BasePlayer player)
        {
            if (__instance == null || player == null) return;
            try { STPlugin.Dispatch_OnLootEntityEnd(player, __instance); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnLootEntityEnd: " + ex.Message); }
        }
    }
}
