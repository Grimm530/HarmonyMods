using System;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Quest
    {
        internal static void SetInstance(Quest inst) => Instance = inst;
        internal static void ClearInstance() => Instance = null;
        internal static Quest GetModInstance() => Instance;

        public void CallInit()
        {
            try { HarmonyLoadDefaultMessages(); }
            catch (Exception ex) { Debug.LogWarning("[Quest] LoadDefaultMessages: " + ex.Message); }
            try { Init(); }
            catch (Exception ex) { Debug.LogWarning("[Quest] Init: " + ex.Message); }
        }

        public void CallOnServerInitialized()
        {
            try { OnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[Quest] OnServerInitialized: " + ex); }
        }

        public void CallUnload()
        {
            try { Unload(); }
            catch (Exception ex) { Debug.LogWarning("[Quest] Unload: " + ex.Message); }
        }

        public void HarmonyRegisterPermissions()
        {
            try
            {
                if (!permission.PermissionExists("quest.admin", this))
                    permission.RegisterPermission("quest.admin", this);
                if (_questList == null) return;
                foreach (var quest in _questList.Values)
                {
                    if (quest == null || string.IsNullOrEmpty(quest.QuestPermission)) continue;
                    string perm = Name + "." + quest.QuestPermission;
                    if (!permission.PermissionExists(perm, this))
                        permission.RegisterPermission(perm, this);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[Quest] HarmonyRegisterPermissions: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerConnected(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerConnected))) return;
            try { inst.OnPlayerConnected(player); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnPlayerConnected: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerDisconnected(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerDisconnected))) return;
            try { inst.OnPlayerDisconnected(player); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnPlayerDisconnected: " + ex.Message); }
        }

        public static void Dispatch_OnServerSave()
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnServerSave))) return;
            try { inst.OnServerSave(); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnServerSave: " + ex.Message); }
        }

        public static void Dispatch_OnNewSave()
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnNewSave))) return;
            try { inst.OnNewSave(); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnNewSave: " + ex.Message); }
        }

        public static object Dispatch_OnStructureUpgrade(BaseCombatEntity entity, BasePlayer player, BuildingGrade.Enum grade)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnStructureUpgrade))) return null;
            try { return inst.OnStructureUpgrade(entity, player, grade); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnStructureUpgrade: " + ex.Message); return null; }
        }

        internal void QuestProgressFromGather(BasePlayer player, string shortname, int amount, bool fishing = false)
        {
            if (player == null || string.IsNullOrEmpty(shortname)) return;
            QuestProgress(player.userID, fishing ? QuestType.Fishing : QuestType.Gather, shortname, "", null, amount);
        }

        public static void Dispatch_OnDispenserGathered(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnDispenserGathered))) return;
            if (item == null) return;
            try { inst.OnDispenserGathered(dispenser, player, item); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnDispenserGathered: " + ex.Message); }
        }

        public static void Dispatch_OnDispenserBonusReceived(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnDispenserBonusReceived))) return;
            if (item == null) return;
            try { inst.OnDispenserBonusReceived(dispenser, player, item); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnDispenserBonusReceived: " + ex.Message); }
        }

        public static void Dispatch_OnCollectiblePickedup(CollectibleEntity collectible, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnCollectiblePickedup))) return;
            try
            {
                if (collectible?.itemList == null || player == null) return;
                foreach (var ia in collectible.itemList)
                {
                    if (ia?.itemDef == null) continue;
                    inst.QuestProgress(player.userID, QuestType.Gather, ia.itemDef.shortname, "", null, (int)ia.amount);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnCollectiblePickedup: " + ex.Message); }
        }

        public static void Dispatch_OnItemCraftFinished(ItemCrafter crafter, ItemCraftTask task)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemCraftFinished))) return;
            try
            {
                if (crafter?.owner == null || task?.blueprint?.targetItem == null) return;
                inst.QuestProgress(crafter.owner.userID, QuestType.Craft, task.blueprint.targetItem.shortname, "", null, task.blueprint.amountToCreate);
            }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnItemCraftFinished: " + ex.Message); }
        }

        public static void Dispatch_OnTechTreeNodeUnlocked(Workbench workbench, TechTreeData.NodeInstance node, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnTechTreeNodeUnlocked))) return;
            try { inst.OnTechTreeNodeUnlocked(workbench, node, player); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnTechTreeNodeUnlocked: " + ex.Message); }
        }

        public static void Dispatch_OnItemResearched(ResearchTable table, int amount)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemResearched))) return;
            try { inst.OnItemResearched(table, amount); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnItemResearched: " + ex.Message); }
        }

        public static void Dispatch_OnEntityBuilt(Planner plan, GameObject go)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityBuilt))) return;
            try { inst.OnEntityBuilt(plan, go); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnEntityBuilt: " + ex.Message); }
        }

        public static void Dispatch_OnEntityDestroy(BaseEntity entity)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityDestroy))) return;
            try { inst.OnEntityDestroy(entity); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnEntityDestroy: " + ex.Message); }
        }

        public static void Dispatch_OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnLootEntity))) return;
            try { inst.OnLootEntity(player, entity); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnLootEntity: " + ex.Message); }
        }

        public static void Dispatch_OnContainerDropItems(ItemContainer container)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnContainerDropItems))) return;
            try { inst.OnContainerDropItems(container); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnContainerDropItems: " + ex.Message); }
        }

        public static void Dispatch_OnCardSwipe(CardReader reader, Keycard card, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnCardSwipe))) return;
            try { inst.OnCardSwipe(reader, card, player); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnCardSwipe: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerDeath))) return;
            try { inst.OnPlayerDeath(player, info); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnPlayerDeath: " + ex.Message); }
        }

        public static void Dispatch_OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityDeath))) return;
            try { inst.OnEntityDeath(entity, info); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnEntityDeath: " + ex.Message); }
        }

        public static void Dispatch_OnEntityKill(BaseNetworkable entity)
        {
            var inst = Instance;
            if (inst == null) return;
            try
            {
                if (entity is PatrolHelicopter heli && inst.IsSubscribed(nameof(OnEntityKill)))
                    inst.OnEntityKill(heli);
                if (entity is BaseEntity be && inst.IsSubscribed(nameof(OnEntityDestroy)))
                    inst.OnEntityDestroy(be);
            }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnEntityKill: " + ex.Message); }
        }

        public static void Dispatch_OnPatrolHelicopterKill(PatrolHelicopter entity, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPatrolHelicopterKill))) return;
            try { inst.OnPatrolHelicopterKill(entity, info); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnPatrolHelicopterKill: " + ex.Message); }
        }

        public static void Dispatch_OnNpcGiveSoldItem(NPCVendingMachine machine, Item soldItem, BasePlayer buyer)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnNpcGiveSoldItem))) return;
            try { inst.OnNpcGiveSoldItem(machine, soldItem, buyer); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnNpcGiveSoldItem: " + ex.Message); }
        }

        public static void Dispatch_OnCrateHack(HackableLockedCrate crate)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnCrateHack))) return;
            try { inst.OnCrateHack(crate); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnCrateHack: " + ex.Message); }
        }

        public static void Dispatch_OnRecyclerToggle(Recycler recycler, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnRecyclerToggle))) return;
            try { inst.OnRecyclerToggle(recycler, player); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnRecyclerToggle: " + ex.Message); }
        }

        public static void Dispatch_OnItemRecycle(Item item, Recycler recycler)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemRecycle))) return;
            try { inst.OnItemRecycle(item, recycler); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnItemRecycle: " + ex.Message); }
        }

        public static void Dispatch_OnGrowableGathered(GrowableEntity plant, Item item, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnGrowableGathered))) return;
            try
            {
                if (player == null || plant == null) return;
                if (item != null)
                    inst.OnGrowableGathered(plant, item, player);
                else if (plant.SourceItemDef != null)
                    inst.QuestProgress(player.userID, QuestType.Growseedlings, plant.SourceItemDef.shortname, "", null, 1);
            }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnGrowableGathered: " + ex.Message); }
        }

        public static void Dispatch_OnFishCatch(Item fish, BaseFishingRod rod, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnFishCatch))) return;
            try { inst.OnFishCatch(fish, rod, player); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnFishCatch: " + ex.Message); }
        }

        public static void Dispatch_OnBigWheelWin(BigWheelGame wheel, Item scrap, BigWheelBettingTerminal terminal, int multiplier)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnBigWheelWin))) return;
            try { inst.OnBigWheelWin(wheel, scrap, terminal, multiplier); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnBigWheelWin: " + ex.Message); }
        }

        public static void Dispatch_OnBigWheelLoss(BigWheelGame wheel, Item scrap, BigWheelBettingTerminal terminal)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnBigWheelLoss))) return;
            try { inst.OnBigWheelLoss(wheel, scrap, terminal); }
            catch (Exception ex) { Debug.LogWarning("[Quest] OnBigWheelLoss: " + ex.Message); }
        }
    }
}
