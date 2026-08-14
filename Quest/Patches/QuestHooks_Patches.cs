using HarmonyLib;
using UnityEngine;
using QPlugin = Oxide.Plugins.Quest;

namespace QuestHarmony.Patches
{
    [HarmonyPatch(typeof(BuildingBlock), nameof(BuildingBlock.DoUpgradeToGrade))]
    public static class BuildingBlock_DoUpgradeToGrade_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BuildingBlock __instance, BaseEntity.RPCMessage msg)
        {
            if (__instance == null || msg.player == null) return;
            try { QPlugin.Dispatch_OnStructureUpgrade(__instance, msg.player, __instance.grade); }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnStructureUpgrade: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ResourceDispenser), "GiveResourceFromItem")]
    public static class ResourceDispenser_GiveResourceFromItem_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResourceDispenser __instance, BasePlayer entity, ItemAmount itemAmt)
        {
            if (entity == null || itemAmt?.itemDef == null) return;
            try
            {
                var inst = QPlugin.GetModInstance();
                if (inst == null || !inst.IsSubscribed("OnDispenserGathered")) return;
                inst.QuestProgressFromGather(entity, itemAmt.itemDef.shortname, Mathf.Max(1, (int)itemAmt.amount));
            }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnDispenserGathered: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ResourceDispenser), nameof(ResourceDispenser.AssignFinishBonus))]
    public static class ResourceDispenser_AssignFinishBonus_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResourceDispenser __instance, BasePlayer player)
        {
            if (player == null) return;
            try { QPlugin.Dispatch_OnDispenserBonusReceived(__instance, player, null); }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnDispenserBonusReceived: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(CollectibleEntity), nameof(CollectibleEntity.DoPickup), typeof(BasePlayer), typeof(bool))]
    public static class CollectibleEntity_DoPickup_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(CollectibleEntity __instance, BasePlayer reciever)
        {
            try { QPlugin.Dispatch_OnCollectiblePickedup(__instance, reciever); }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnCollectiblePickedup: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ItemCrafter), nameof(ItemCrafter.FinishCrafting))]
    public static class ItemCrafter_FinishCrafting_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemCrafter __instance, ItemCraftTask task)
        {
            try { QPlugin.Dispatch_OnItemCraftFinished(__instance, task); }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnItemCraftFinished: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ResearchTable), nameof(ResearchTable.ResearchAttemptFinished))]
    public static class ResearchTable_ResearchAttemptFinished_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResearchTable __instance)
        {
            try
            {
                var target = __instance.GetTargetItem();
                int amt = target != null ? ResearchTable.ScrapForResearch(target) : 0;
                QPlugin.Dispatch_OnItemResearched(__instance, amt);
            }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnItemResearched: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Planner), nameof(Planner.DoBuild), typeof(Construction.Target), typeof(Construction))]
    public static class Planner_DoBuild_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Planner __instance, BaseEntity __result)
        {
            if (__instance == null || __result == null) return;
            try { QPlugin.Dispatch_OnEntityBuilt(__instance, __result.gameObject); }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnEntityBuilt: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity))]
    public static class PlayerLoot_StartLootingEntity_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerLoot __instance, BaseEntity targetEntity, bool __result)
        {
            if (!__result || targetEntity == null) return;
            var player = __instance.baseEntity;
            if (player == null) return;
            try { QPlugin.Dispatch_OnLootEntity(player, targetEntity); }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnLootEntity: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(StorageContainer), nameof(StorageContainer.DropItems), typeof(BaseEntity))]
    public static class StorageContainer_DropItems_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(StorageContainer __instance)
        {
            if (__instance?.inventory == null) return;
            try { QPlugin.Dispatch_OnContainerDropItems(__instance.inventory); }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnContainerDropItems: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(CardReader), nameof(CardReader.ServerCardSwiped))]
    public static class CardReader_ServerCardSwiped_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(CardReader __instance, BaseEntity.RPCMessage msg)
        {
            var player = msg.player;
            if (player == null) return;
            try
            {
                var item = player.GetActiveItem();
                var card = item?.GetHeldEntity() as Keycard;
                if (card != null)
                    QPlugin.Dispatch_OnCardSwipe(__instance, card, player);
            }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnCardSwipe: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), typeof(HitInfo))]
    public static class BaseCombatEntity_Die_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            try
            {
                if (__instance is BasePlayer bp)
                    QPlugin.Dispatch_OnPlayerDeath(bp, info);
                QPlugin.Dispatch_OnEntityDeath(__instance, info);
            }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnEntityDeath: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill))]
    public static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseNetworkable __instance)
        {
            try { QPlugin.Dispatch_OnEntityKill(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnEntityKill: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PatrolHelicopter), nameof(PatrolHelicopter.Hurt))]
    public static class PatrolHelicopter_Hurt_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(PatrolHelicopter __instance, HitInfo info)
        {
            if (__instance == null || info == null) return;
            try
            {
                if (info.damageTypes != null && info.damageTypes.Total() >= __instance.health)
                    QPlugin.Dispatch_OnPatrolHelicopterKill(__instance, info);
            }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnPatrolHelicopterKill: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(NPCVendingMachine), nameof(NPCVendingMachine.GiveSoldItem))]
    public static class NPCVendingMachine_GiveSoldItem_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(NPCVendingMachine __instance, Item soldItem, BasePlayer buyer)
        {
            try { QPlugin.Dispatch_OnNpcGiveSoldItem(__instance, soldItem, buyer); }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnNpcGiveSoldItem: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(HackableLockedCrate), nameof(HackableLockedCrate.StartHacking))]
    public static class HackableLockedCrate_StartHacking_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(HackableLockedCrate __instance)
        {
            try { QPlugin.Dispatch_OnCrateHack(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnCrateHack: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Recycler), "SVSwitch")]
    public static class Recycler_SVSwitch_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Recycler __instance, BaseEntity.RPCMessage msg)
        {
            if (__instance == null || msg.player == null) return;
            try { QPlugin.Dispatch_OnRecyclerToggle(__instance, msg.player); }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnRecyclerToggle: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Recycler), "RecycleThink")]
    public static class Recycler_RecycleThink_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Recycler __instance)
        {
            if (__instance?.inventory == null) return;
            try
            {
                for (int i = 0; i < 6; i++)
                {
                    var slot = __instance.inventory.GetSlot(i);
                    if (slot != null && __instance.CanBeRecycled(slot))
                    {
                        QPlugin.Dispatch_OnItemRecycle(slot, __instance);
                        break;
                    }
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnItemRecycle: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(GrowableEntity), nameof(GrowableEntity.PickFruit), typeof(BasePlayer), typeof(bool))]
    public static class GrowableEntity_PickFruit_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(GrowableEntity __instance, BasePlayer player)
        {
            try { QPlugin.Dispatch_OnGrowableGathered(__instance, null, player); }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnGrowableGathered: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseFishingRod), "CatchProcessBudgeted")]
    public static class BaseFishingRod_CatchProcessBudgeted_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseFishingRod __instance)
        {
            try
            {
                if (__instance == null || __instance.CurrentState != BaseFishingRod.CatchState.Caught) return;
                var player = __instance.GetOwnerPlayer();
                var def = __instance.currentFishTarget;
                if (player == null || def == null) return;
                var inst = QPlugin.GetModInstance();
                inst?.QuestProgressFromGather(player, def.shortname, 1, fishing: true);
            }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnFishCatch: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BigWheelGame), nameof(BigWheelGame.Payout))]
    public static class BigWheelGame_Payout_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BigWheelGame __instance)
        {
            try
            {
                if (__instance?.terminals == null) return;
                var hit = __instance.GetCurrentHitType();
                foreach (var terminal in __instance.terminals)
                {
                    if (terminal == null || terminal.isClient || terminal.inventory == null) continue;
                    var slot = terminal.inventory.GetSlot((int)hit.hitType);
                    if (slot != null)
                        QPlugin.Dispatch_OnBigWheelWin(__instance, slot, terminal, hit.ColorToMultiplier(hit.hitType));
                    else
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            var loss = terminal.inventory.GetSlot(i);
                            if (loss != null)
                                QPlugin.Dispatch_OnBigWheelLoss(__instance, loss, terminal);
                        }
                    }
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] BigWheel: " + ex.Message); }
        }
    }
}
