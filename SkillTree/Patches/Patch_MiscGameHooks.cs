// Misc game hooks. All method names use string literals to avoid compile errors
// when the exact publicized name differs from what Oxide reports.
using System;
using HarmonyLib;
using UnityEngine;
using STPlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony.Patches
{
    // ---- Building ---------------------------------------------------------

    [HarmonyPatch(typeof(Planner), "DoBuild")]
    public static class Planner_DoBuild_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Planner __instance)
        {
            if (__instance == null) return;
            try { STPlugin.Dispatch_OnEntityBuilt(__instance, null); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnEntityBuilt: " + ex.Message); }
        }
    }

    // ---- Item condition ---------------------------------------------------

    [HarmonyPatch(typeof(Item), "LoseCondition")]
    public static class Item_LoseCondition_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Item __instance, ref float val)
        {
            try { STPlugin.Dispatch_OnLoseCondition(__instance, ref val); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnLoseCondition: " + ex.Message); }
        }
    }

    // ---- Mount / dismount -------------------------------------------------

    [HarmonyPatch(typeof(BaseMountable), "MountPlayer")]
    public static class BaseMountable_MountPlayer_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseMountable __instance, BasePlayer player)
        {
            if (__instance == null || player == null) return;
            try { STPlugin.Dispatch_OnEntityMounted(__instance, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnEntityMounted: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseMountable), "DismountPlayer")]
    public static class BaseMountable_DismountPlayer_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseMountable __instance, BasePlayer player)
        {
            if (__instance == null || player == null) return;
            try { STPlugin.Dispatch_OnEntityDismounted(__instance, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnEntityDismounted: " + ex.Message); }
        }
    }

    // ---- Player input -----------------------------------------------------

    [HarmonyPatch(typeof(BasePlayer), "ServerInput")]
    public static class BasePlayer_ServerInput_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance, InputState input)
        {
            if (__instance == null || input == null) return;
            try { STPlugin.Dispatch_OnPlayerInput(__instance, input); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerInput: " + ex.Message); }
        }
    }

    // ---- Entity spawn (via BaseNetworkable.Spawned or NetworkableAwake) ---

    [HarmonyPatch(typeof(BaseNetworkable), "Spawned")]
    public static class BaseNetworkable_Spawned_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseNetworkable __instance)
        {
            if (__instance == null || __instance is BasePlayer) return;
            try { STPlugin.Dispatch_OnEntitySpawned(__instance); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnEntitySpawned: " + ex.Message); }
        }
    }

    // ---- Revive -----------------------------------------------------------

    [HarmonyPatch(typeof(BasePlayer), "Revive")]
    public static class BasePlayer_Revive_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance, BasePlayer reviver)
        {
            if (__instance == null) return;
            try { STPlugin.Dispatch_OnPlayerRevive(reviver, __instance); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerRevive: " + ex.Message); }
        }
    }

    // ---- Health change ---------------------------------------------------

    [HarmonyPatch(typeof(BaseCombatEntity), "SetHealth")]
    public static class BaseCombatEntity_SetHealth_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseCombatEntity __instance, out float __state)
        {
            __state = __instance?.Health() ?? 0f;
        }
        [HarmonyPostfix]
        public static void Postfix(BaseCombatEntity __instance, float __state)
        {
            if (__instance is not BasePlayer player) return;
            try { STPlugin.Dispatch_OnPlayerHealthChange(player, __state, __instance.Health()); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerHealthChange: " + ex.Message); }
        }
    }

    // ---- Active item change ----------------------------------------------

    [HarmonyPatch(typeof(BasePlayer), "SwitchHandAndEquipActive")]
    public static class BasePlayer_SwitchHandAndEquipActive_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BasePlayer __instance, out Item __state)
        {
            __state = __instance?.GetActiveItem();
        }
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance, Item __state)
        {
            if (__instance == null) return;
            var newItem = __instance.GetActiveItem();
            if (__state == newItem) return;
            try { STPlugin.Dispatch_OnActiveItemChanged(__instance, __state, newItem); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnActiveItemChanged: " + ex.Message); }
        }
    }

    // ---- Melee attack ---------------------------------------------------

    [HarmonyPatch(typeof(BaseMelee), "ServerUse")]
    public static class BaseMelee_ServerUse_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseMelee __instance)
        {
            if (__instance == null) return;
            var player = __instance.GetOwnerPlayer();
            if (player == null) return;
            try { STPlugin.Dispatch_OnMeleeAttack(player, null); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnMeleeAttack: " + ex.Message); }
        }
    }

    // ---- Explosive throw -------------------------------------------------

    [HarmonyPatch(typeof(ThrownWeapon), "ServerThrow")]
    public static class ThrownWeapon_ServerThrow_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThrownWeapon __instance, Vector3 position)
        {
            var player = __instance?.GetOwnerPlayer();
            if (player == null) return;
            // ThrownWeapon itself is the weapon; dispatch with null explosive entity.
            try { STPlugin.Dispatch_OnExplosiveThrown(player, null, __instance); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnExplosiveThrown: " + ex.Message); }
        }
    }

    // ---- Timed explosive explode -----------------------------------------

    [HarmonyPatch(typeof(TimedExplosive), "Explode")]
    public static class TimedExplosive_Explode_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(TimedExplosive __instance)
        {
            if (__instance == null) return;
            try { STPlugin.Dispatch_OnTimedExplosiveExplode(__instance); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnTimedExplosiveExplode: " + ex.Message); }
        }
    }

    // ---- Mixing table toggle --------------------------------------------

    [HarmonyPatch(typeof(MixingTable), "StartMixing")]
    public static class MixingTable_StartMixing_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(MixingTable __instance, BasePlayer player)
        {
            if (__instance == null) return;
            try { STPlugin.Dispatch_OnMixingTableToggle(__instance, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnMixingTableToggle: " + ex.Message); }
        }
    }

    // ---- Healing item use -----------------------------------------------

    [HarmonyPatch(typeof(MedicalTool), "ServerUse")]
    public static class MedicalTool_ServerUse_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(MedicalTool __instance)
        {
            var player = __instance?.GetOwnerPlayer();
            if (player == null) return true;
            object r = STPlugin.Dispatch_OnHealingItemUse(__instance, player);
            return r == null;
        }
    }

    // ---- Mission success ------------------------------------------------

    [HarmonyPatch(typeof(BaseMission), "MissionSuccess")]
    public static class BaseMission_MissionSuccess_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseMission __instance, BaseMission.MissionInstance missionInstance, BasePlayer assignee)
        {
            if (__instance == null || assignee == null) return;
            try { STPlugin.Dispatch_OnMissionSucceeded(__instance, missionInstance, assignee); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnMissionSucceeded: " + ex.Message); }
        }
    }

    // ---- Item action / container changes --------------------------------

    [HarmonyPatch(typeof(ItemMod), "ServerCommand")]
    public static class ItemMod_ServerCommand_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Item item, string command, BasePlayer player)
        {
            if (string.IsNullOrEmpty(command) || item == null) return;
            try { STPlugin.Dispatch_OnItemAction(item, command, player); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnItemAction: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ItemContainer), "Insert")]
    public static class ItemContainer_Insert_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemContainer __instance, Item item, bool __result)
        {
            if (!__result || __instance == null || item == null) return;
            try { STPlugin.Dispatch_OnItemAddedToContainer(__instance, item); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnItemAddedToContainer: " + ex.Message); }
        }
    }

    // ---- Entity kill (storage containers, collectibles, workbenches etc.) -

    [HarmonyPatch(typeof(BaseNetworkable), "Kill", typeof(BaseNetworkable.DestroyMode))]
    public static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseNetworkable __instance)
        {
            if (__instance == null) return;
            try
            {
                // Order matters: more-derived types first (Workbench < StorageContainer).
                switch (__instance)
                {
                    case Workbench wb:          STPlugin.Dispatch_OnEntityKill_Workbench(wb);          break;
                    case StorageContainer sc:   STPlugin.Dispatch_OnEntityKill_StorageContainer(sc);  break;
                    case CollectibleEntity ce:  STPlugin.Dispatch_OnEntityKill_CollectibleEntity(ce); break;
                    case DudTimedExplosive dud: STPlugin.Dispatch_OnEntityKill_DudTimedExplosive(dud); break;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnEntityKill: " + ex.Message); }
        }
    }

    // ---- Pay for upgrade ------------------------------------------------

    [HarmonyPatch(typeof(BuildingBlock), "PayForUpgrade")]
    public static class BuildingBlock_PayForUpgrade_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BuildingBlock __instance, ConstructionGrade grade, BasePlayer player)
        {
            object r = STPlugin.Dispatch_OnPayForUpgrade(player, __instance, grade);
            return r == null;
        }
    }

    // ---- Player wound ---------------------------------------------------

    [HarmonyPatch(typeof(BasePlayer), "BecomeWounded")]
    public static class BasePlayer_BecomeWounded_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BasePlayer __instance, HitInfo info)
        {
            object r = STPlugin.Dispatch_OnPlayerWound(__instance, info);
            return r == null;
        }
    }
}
