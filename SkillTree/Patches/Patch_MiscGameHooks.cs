// Misc game hooks — targets verified against .cursor/!Assembly-RUST for this server build.
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using STPlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony.Patches
{
    // ---- Building ---------------------------------------------------------
    // Planner has two DoBuild overloads; patch the Construction.Target path (returns BaseEntity).

    [HarmonyPatch(typeof(Planner), "DoBuild", new[] { typeof(Construction.Target), typeof(Construction) })]
    public static class Planner_DoBuild_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Planner __instance, BaseEntity __result)
        {
            if (__instance == null || __result == null) return;
            try { STPlugin.Dispatch_OnEntityBuilt(__instance, __result.gameObject); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnEntityBuilt: " + ex.Message); }
        }
    }

    // ---- Item condition ---------------------------------------------------

    [HarmonyPatch(typeof(Item), "LoseCondition", new[] { typeof(float) })]
    public static class Item_LoseCondition_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Item __instance, ref float amount)
        {
            try { STPlugin.Dispatch_OnLoseCondition(__instance, ref amount); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnLoseCondition: " + ex.Message); }
        }
    }

    // ---- Item repair (MaxRepair / Free_Repairs) ----------------------------
    // Oxide CallHook("OnItemRepair") only reaches Oxide plugins; Harmony SkillTree must patch here.

    [HarmonyPatch(typeof(RepairBench), nameof(RepairBench.RepairAnItem))]
    public static class RepairBench_RepairAnItem_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Item itemToRepair, BasePlayer player, BaseEntity repairBenchEntity, float maxConditionLostOnRepair, bool mustKnowBlueprint)
        {
            if (player == null || itemToRepair == null) return true;
            object r = null;
            try { r = STPlugin.Dispatch_OnItemRepair(player, itemToRepair); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnItemRepair: " + ex.Message); }
            // Oxide: non-null cancels default repair (Free_Repairs already repaired).
            return r == null;
        }
    }

    // ---- Mount / dismount -------------------------------------------------
    // DismountPlayer is (BasePlayer, bool) in IL even when lite has a default.

    [HarmonyPatch(typeof(BaseMountable), "MountPlayer", new[] { typeof(BasePlayer) })]
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

    [HarmonyPatch(typeof(BaseMountable), "DismountPlayer", new[] { typeof(BasePlayer), typeof(bool) })]
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

    // ---- Player input (boat turbo) ----------------------------------------
    // No BasePlayer.ServerInput — Oxide OnPlayerInput comes from OnReceiveTick.

    [HarmonyPatch(typeof(BasePlayer), "OnReceiveTick", new[] { typeof(PlayerTick), typeof(bool) })]
    public static class BasePlayer_OnReceiveTick_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            if (__instance == null || __instance.serverInput == null) return;
            if (!STPlugin.IsHookSubscribed("OnPlayerInput")) return;
            try { STPlugin.Dispatch_OnPlayerInput(__instance, __instance.serverInput); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerInput: " + ex.Message); }
        }
    }

    // ---- Entity spawn -----------------------------------------------------

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    public static class BaseNetworkable_Spawn_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseNetworkable __instance)
        {
            if (__instance == null || __instance is BasePlayer) return;
            try { STPlugin.Dispatch_OnEntitySpawned(__instance); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnEntitySpawned: " + ex.Message); }
        }
    }

    // ---- Revive (medical tool on wounded teammate) ------------------------

    [HarmonyPatch(typeof(BasePlayer), "OnMedicalToolApplied")]
    public static class BasePlayer_OnMedicalToolApplied_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance, BasePlayer fromPlayer, bool canRevive)
        {
            if (__instance == null || fromPlayer == null || !canRevive) return;
            if (fromPlayer == __instance) return;
            try { STPlugin.Dispatch_OnPlayerRevive(fromPlayer, __instance); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerRevive: " + ex.Message); }
        }
    }

    // ---- Health change ---------------------------------------------------

    [HarmonyPatch(typeof(BaseCombatEntity), "SetHealth", new[] { typeof(float) })]
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

    [HarmonyPatch(typeof(BasePlayer), "UpdateActiveItem", new[] { typeof(ItemId) })]
    public static class BasePlayer_UpdateActiveItem_Patch
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

    // ---- Melee attack (Mining_Hotspot) ----------------------------------
    // Oxide OnMeleeAttack fires in BaseMelee.PlayerAttack with a real HitInfo.
    // DoAttackShared is the shared player path that still has that HitInfo.

    [HarmonyPatch(typeof(BaseMelee), nameof(BaseMelee.DoAttackShared))]
    public static class BaseMelee_DoAttackShared_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseMelee __instance, HitInfo info)
        {
            if (__instance == null || info == null || __instance is Hammer) return;
            var player = __instance.GetOwnerPlayer();
            if (player == null) return;
            try { STPlugin.Dispatch_OnMeleeAttack(player, info); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnMeleeAttack: " + ex.Message); }
        }
    }

    // ---- Hammer (Vehicle_Mechanic) --------------------------------------

    [HarmonyPatch(typeof(Hammer), nameof(Hammer.DoAttackShared))]
    public static class Hammer_DoAttackShared_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Hammer __instance, HitInfo info)
        {
            var player = __instance?.GetOwnerPlayer();
            if (player == null || info == null) return true;
            try
            {
                if (STPlugin.Dispatch_OnHammerHit(player, info) != null)
                    return false;
            }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnHammerHit: " + ex.Message); }
            return true;
        }
    }

    // ---- Explosive throw / drop ------------------------------------------
    // Both DoThrowImpl and DoDrop call SetUpThrownWeapon(ent) after Spawn.
    // Old ServerThrow patch passed null TimedExplosive and NRE'd in HandleExplosionRadius.

    [HarmonyPatch(typeof(ThrownWeapon), "SetUpThrownWeapon")]
    public static class ThrownWeapon_SetUpThrownWeapon_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThrownWeapon __instance, BaseEntity ent)
        {
            if (__instance == null || ent is not TimedExplosive timed) return;
            var player = __instance.GetOwnerPlayer();
            if (player == null) return;
            try { STPlugin.Dispatch_OnExplosiveThrown(player, timed, __instance); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnExplosiveThrown: " + ex.Message); }
        }
    }

    // ---- Timed explosive explode ----------------------------------------
    // Parameterless Explode() calls Explode(Vector3); patch the Vector3 overload once.

    [HarmonyPatch(typeof(TimedExplosive), "Explode", new[] { typeof(Vector3) })]
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

    [HarmonyPatch(typeof(MixingTable), "StartMixing", new[] { typeof(BasePlayer) })]
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

    // ---- Healing item use (Double_Bandage_Heal) -------------------------
    // Players use UseSelf/UseOther → GiveEffectsTo. ServerUse is NPC-only.

    [HarmonyPatch(typeof(MedicalTool), "GiveEffectsTo")]
    public static class MedicalTool_GiveEffectsTo_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(MedicalTool __instance, BasePlayer fromPlayer)
        {
            if (__instance == null || fromPlayer == null) return true;
            object r = null;
            try { r = STPlugin.Dispatch_OnHealingItemUse(__instance, fromPlayer); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnHealingItemUse: " + ex.Message); }
            return r == null;
        }
    }

    // ---- Bandage assist revive (Reviver) --------------------------------

    [HarmonyPatch(typeof(BasePlayer), "RPC_Assist")]
    public static class BasePlayer_RPC_Assist_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BasePlayer __instance, BaseEntity.RPCMessage msg)
        {
            if (__instance == null || msg.player == null || !__instance.IsWounded()) return;
            try { STPlugin.Dispatch_OnPlayerRevive(msg.player, __instance); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerAssist/Revive: " + ex.Message); }
        }
    }

    // ---- Mission success ------------------------------------------------

    [HarmonyPatch(typeof(BaseMission), "MissionSuccess", new[] { typeof(BaseMission.MissionInstance), typeof(BasePlayer) })]
    public static class BaseMission_MissionSuccess_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseMission __instance, BaseMission.MissionInstance instance, BasePlayer assignee)
        {
            if (__instance == null || assignee == null) return;
            try { STPlugin.Dispatch_OnMissionSucceeded(__instance, instance, assignee); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnMissionSucceeded: " + ex.Message); }
        }
    }

    // ---- Item action / container changes --------------------------------

    [HarmonyPatch(typeof(ItemMod), "ServerCommand", new[] { typeof(Item), typeof(string), typeof(BasePlayer) })]
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

    [HarmonyPatch(typeof(ItemContainer), "Insert", new[] { typeof(Item) })]
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

    // ---- Entity kill ----------------------------------------------------

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), new[] { typeof(BaseNetworkable.DestroyMode), typeof(bool) })]
    public static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseNetworkable __instance)
        {
            if (__instance == null) return;
            try
            {
                switch (__instance)
                {
                    case PatrolHelicopter ph:   STPlugin.Dispatch_OnEntityKill_PatrolHelicopter(ph);   break;
                    case PlayerBoat boat:       STPlugin.Dispatch_OnEntityKill_PlayerBoat(boat);       break;
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

    [HarmonyPatch(typeof(BuildingBlock), "PayForUpgrade", new[] { typeof(ConstructionGrade), typeof(BasePlayer) })]
    public static class BuildingBlock_PayForUpgrade_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BuildingBlock __instance, ConstructionGrade g, BasePlayer player)
        {
            object r = STPlugin.Dispatch_OnPayForUpgrade(player, __instance, g);
            return r == null;
        }
    }

    // ---- Player wound ---------------------------------------------------

    [HarmonyPatch(typeof(BasePlayer), "BecomeWounded", new[] { typeof(HitInfo) })]
    public static class BasePlayer_BecomeWounded_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BasePlayer __instance, HitInfo info)
        {
            object r = STPlugin.Dispatch_OnPlayerWound(__instance, info);
            // Oxide: return false to prevent wound. Non-null object that is false => block.
            if (r is bool b) return b;
            return r == null;
        }
    }
}
