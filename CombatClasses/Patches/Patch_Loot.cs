using HarmonyLib;
using UnityEngine;
using CCPlugin = Oxide.Plugins.CombatClasses;

namespace CombatClassesHarmony.Patches
{
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity), new[] { typeof(BaseEntity), typeof(bool) })]
    public static class PlayerLoot_StartLootingEntity_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerLoot __instance, BaseEntity targetEntity, bool __result)
        {
            if (!__result || __instance == null || targetEntity == null) return;
            BasePlayer player = __instance.baseEntity;
            if (player == null) return;
            try { CCPlugin.Dispatch_OnLootEntity(player, targetEntity); }
            catch (System.Exception ex) { Debug.LogWarning("[CombatClasses] OnLootEntity: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.Clear))]
    public static class PlayerLoot_Clear_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(PlayerLoot __instance)
        {
            BasePlayer player = __instance?.baseEntity;
            BaseEntity source = __instance?.entitySource;
            if (player == null || source == null) return;
            if (source is BaseCombatEntity bce)
            {
                try { CCPlugin.Dispatch_OnLootEntityEnd(player, bce); }
                catch (System.Exception ex) { Debug.LogWarning("[CombatClasses] OnLootEntityEnd: " + ex.Message); }
            }
        }
    }

    [HarmonyPatch(typeof(Planner), "DoBuild", new[] { typeof(Construction.Target), typeof(Construction) })]
    public static class Planner_DoBuild_ConstructionPlace_Patch
    {
        // OnConstructionPlace fires before spawn completes in Oxide; approximate with postfix on built entity.
        // Planner.DoBuild returns BaseEntity — use that.
        [HarmonyPrefix]
        public static void Prefix(Planner __instance, Construction.Target target, Construction component, out Construction.Target __state)
        {
            __state = target;
        }

        [HarmonyPostfix]
        public static void Postfix(Planner __instance, Construction component, Construction.Target __state, BaseEntity __result)
        {
            if (__result == null || __instance == null) return;
            var player = __instance.GetOwnerPlayer();
            if (player == null) return;
            try
            {
                // Fire after place (gearbox skin rename). Non-null return would cancel in Oxide; too late here.
                CCPlugin.Dispatch_OnConstructionPlace(__result, component, __state, player);
            }
            catch (System.Exception ex) { Debug.LogWarning("[CombatClasses] OnConstructionPlace: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ItemManager), nameof(ItemManager.Create), new[] { typeof(ItemDefinition), typeof(int), typeof(ulong), typeof(bool), typeof(ulong) })]
    public static class ItemManager_Create_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Item __result)
        {
            if (__result == null) return;
            try { CCPlugin.Dispatch_OnItemCreated(__result); }
            catch (System.Exception ex) { Debug.LogWarning("[CombatClasses] OnItemCreated: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ItemContainer), "Insert", new[] { typeof(Item) })]
    public static class ItemContainer_Insert_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemContainer __instance, Item item, bool __result)
        {
            if (!__result || __instance == null || item == null) return;
            try { CCPlugin.Dispatch_OnItemAddedToContainer(__instance, item); }
            catch (System.Exception ex) { Debug.LogWarning("[CombatClasses] OnItemAddedToContainer: " + ex.Message); }
        }
    }
}
