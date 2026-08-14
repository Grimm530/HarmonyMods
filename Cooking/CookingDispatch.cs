// CookingDispatch.cs — partial class Oxide.Plugins.Cooking
// Instance management, lifecycle wrappers, Dispatch_* for Harmony patches.

using System;
using UnityEngine;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    public partial class Cooking
    {
        internal static void SetInstance(Cooking inst) => Instance = inst;
        internal static void ClearInstance() { Instance = null; }
        internal static Cooking GetModInstance() => Instance;

        public void CallInit()
        {
            try { Init(); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] Init failed: " + ex.Message); }

            try { HarmonyLoadDefaultMessages(); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] LoadDefaultMessages failed: " + ex.Message); }
        }

        public void CallOnServerInitialized()
        {
            try { OnServerInitialized(true); }
            catch (Exception ex) { Debug.LogError("[Cooking] OnServerInitialized failed: " + ex); }
        }

        public void CallUnload()
        {
            try { Unload(); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] Unload failed: " + ex.Message); }
        }

        public void HarmonyReregisterPermissions()
        {
            try
            {
                permission.RegisterPermission(perm_recipemenu_chat, this);
                permission.RegisterPermission(perm_use, this);
                permission.RegisterPermission(perm_admin, this);
                permission.RegisterPermission(perm_bag_cmd, this);
                permission.RegisterPermission(perm_instant, this);
                permission.RegisterPermission(perm_free, this);
                permission.RegisterPermission(perm_nogather, this);
                permission.RegisterPermission(perm_disable_notify_drop, this);
                permission.RegisterPermission(perm_disable_notify_proc, this);
                permission.RegisterPermission(perm_disable_sound, this);
                permission.RegisterPermission(perm_market_cmd, this);
                permission.RegisterPermission(perm_market_npc, this);
                permission.RegisterPermission(perm_gather, this);
                permission.RegisterPermission(perm_recipecards, this);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Cooking] HarmonyReregisterPermissions: " + ex.Message);
            }
        }

        public void ResolvePluginReferences()
        {
            try
            {
                ServerRewards = plugins.Find("ServerRewards");
                Economics = plugins.Find("Economics");
                RandomTrader = plugins.Find("RandomTrader");
                SkillTree = plugins.Find("SkillTree");
                CustomItemVending = plugins.Find("CustomItemVending");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Cooking] ResolvePluginReferences: " + ex.Message);
            }
        }

        public static bool IsHookSubscribed(string hookName)
        {
            var inst = Instance;
            return inst == null || inst.IsSubscribed(hookName);
        }

        public static object Dispatch_OnItemAction(Item item, string action, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemAction))) return null;
            try { return inst.OnItemAction(item, action, player); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnItemAction: " + ex.Message); return null; }
        }

        public static object Dispatch_OnItemSplit(Item item, int amount)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemSplit))) return null;
            try { return inst.OnItemSplit(item, amount); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnItemSplit: " + ex.Message); return null; }
        }

        public static object Dispatch_OnPlayerAddModifiers(BasePlayer player, Item item, ItemModConsumable consumable)
        {
            var inst = Instance;
            if (inst == null || player == null || item == null) return null;
            try { return inst.OnPlayerAddModifiers(player, item, consumable); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnPlayerAddModifiers: " + ex.Message); return null; }
        }

        public static object Dispatch_OnDispenserGather(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            var inst = Instance;
            if (inst == null) return null;
            try { return inst.OnDispenserGather(dispenser, player, item); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnDispenserGather: " + ex.Message); return null; }
        }

        public static object Dispatch_OnDispenserBonus(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            var inst = Instance;
            if (inst == null) return null;
            try { return inst.OnDispenserBonus(dispenser, player, item); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnDispenserBonus: " + ex.Message); return null; }
        }

        public static void Dispatch_OnCollectiblePickup(CollectibleEntity entity, BasePlayer player, bool eat)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnCollectiblePickup(entity, player, eat); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnCollectiblePickup: " + ex.Message); }
        }

        public static void Dispatch_OnGrowableGathered(GrowableEntity plant, Item item, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || plant == null || item == null || player == null) return;
            try { inst.OnGrowableGathered(plant, item, player); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnGrowableGathered: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerDig(BasePlayer player, BaseDiggableEntity diggable)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnPlayerDig(player, diggable); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnPlayerDig: " + ex.Message); }
        }

        public static void Dispatch_OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            var inst = Instance;
            if (inst == null || entity is not StorageContainer container) return;
            try { inst.OnLootEntity(player, container); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnLootEntity: " + ex.Message); }
        }

        public static void Dispatch_OnLootEntityEnd(BasePlayer player, BaseEntity entity)
        {
            var inst = Instance;
            if (inst == null || entity is not StorageContainer container) return;
            try { inst.OnLootEntityEnd(player, container); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnLootEntityEnd: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerConnected(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnPlayerConnected(player); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnPlayerConnected: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerDisconnected(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnPlayerDisconnected(player, string.Empty); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnPlayerDisconnected: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerRespawned(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnPlayerRespawned(player); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnPlayerRespawned: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnPlayerDeath(player, info); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnPlayerDeath: " + ex.Message); }
        }

        public static void Dispatch_OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnEntityDeath(entity, info); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnEntityDeath: " + ex.Message); }
        }

        public static object Dispatch_OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityTakeDamage))) return null;
            try { return inst.OnEntityTakeDamage(entity, info); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnEntityTakeDamage: " + ex.Message); return null; }
        }

        public static void Dispatch_OnServerSave()
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnServerSave(); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnServerSave: " + ex.Message); }
        }

        public static void Dispatch_OnNewSave(string filename)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnNewSave(filename); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnNewSave: " + ex.Message); }
        }

        public static void Dispatch_OnLoseCondition(Item item, ref float amount)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnLoseCondition))) return;
            try { inst.OnLoseCondition(item, ref amount); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnLoseCondition: " + ex.Message); }
        }

        public static void Dispatch_OnEntityMounted(BaseMountable entity, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityMounted))) return;
            try { inst.OnEntityMounted(entity, player); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnEntityMounted: " + ex.Message); }
        }

        public static void Dispatch_OnEntityDismounted(BaseMountable entity, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnEntityDismounted))) return;
            try { inst.OnEntityDismounted(entity, player); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnEntityDismounted: " + ex.Message); }
        }

        public static void Dispatch_OnPlayerRevive(BasePlayer reviver, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerRevive))) return;
            try { inst.OnPlayerRevive(reviver, player); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnPlayerRevive: " + ex.Message); }
        }

        public static object Dispatch_OnPayForUpgrade(BasePlayer player, BuildingBlock block, ConstructionGrade grade)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPayForUpgrade))) return null;
            try { return inst.OnPayForUpgrade(player, block, grade); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnPayForUpgrade: " + ex.Message); return null; }
        }

        public static object Dispatch_OnResearchCostDetermine(Item item)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnResearchCostDetermine))) return null;
            try { return inst.OnResearchCostDetermine(item); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnResearchCostDetermine: " + ex.Message); return null; }
        }

        public static void Dispatch_OnItemRepair(BasePlayer player, Item item)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemRepair))) return;
            try { inst.OnItemRepair(player, item); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnItemRepair: " + ex.Message); }
        }

        public static void Dispatch_OnItemCraftFinished(ItemCraftTask task, Item item, ItemCrafter crafter)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnItemCraftFinished))) return;
            try { inst.OnItemCraftFinished(task, item, crafter); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnItemCraftFinished: " + ex.Message); }
        }

        public static object Dispatch_OnPlayerVoice(BasePlayer player, byte[] data)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerVoice))) return null;
            try { return inst.OnPlayerVoice(player, data); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnPlayerVoice: " + ex.Message); return null; }
        }

        public static void Dispatch_OnPlayerHealthChange(BasePlayer player, float oldValue, float newValue)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerHealthChange))) return;
            try { inst.OnPlayerHealthChange(player, oldValue, newValue); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnPlayerHealthChange: " + ex.Message); }
        }

        public static object Dispatch_OnPlayerWound(BasePlayer player, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnPlayerWound))) return null;
            try { return inst.OnPlayerWound(player, info); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnPlayerWound: " + ex.Message); return null; }
        }

        public static object Dispatch_CanDropActiveItem(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanDropActiveItem))) return null;
            try { return inst.CanDropActiveItem(player); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] CanDropActiveItem: " + ex.Message); return null; }
        }

        public static void Dispatch_OnEntityKill(BaseNetworkable entity)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnEntityKill(entity); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnEntityKill: " + ex.Message); }
        }

        public static object Dispatch_CanBradleyApcTarget(BradleyAPC apc, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanBradleyApcTarget))) return null;
            try { return inst.CanBradleyApcTarget(apc, player); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] CanBradleyApcTarget: " + ex.Message); return null; }
        }

        public static void Dispatch_OnMeleeAttack(BasePlayer player, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnMeleeAttack))) return;
            try { inst.OnMeleeAttack(player, info); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnMeleeAttack: " + ex.Message); }
        }

        public static object Dispatch_OnTreeMarkerHit(TreeEntity tree, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(OnTreeMarkerHit))) return null;
            try { return inst.OnTreeMarkerHit(tree, info); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnTreeMarkerHit: " + ex.Message); return null; }
        }

        public static void Dispatch_OnFishCatch(Item item, BaseFishingRod rod, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnFishCatch(item, rod, player); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnFishCatch: " + ex.Message); }
        }

        public static void Dispatch_CanCatchFish(BasePlayer player, BaseFishingRod rod, Item fish)
        {
            var inst = Instance;
            if (inst == null || !inst.IsSubscribed(nameof(CanCatchFish))) return;
            try { inst.CanCatchFish(player, rod, fish); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] CanCatchFish: " + ex.Message); }
        }

        public static object Dispatch_OnNpcConversationStart(NPCSimpleMissionProvider npc, BasePlayer player, ConversationData conversationData)
        {
            var inst = Instance;
            if (inst == null) return null;
            try { return inst.OnNpcConversationStart(npc, player, conversationData); }
            catch (Exception ex) { Debug.LogWarning("[Cooking] OnNpcConversationStart: " + ex.Message); return null; }
        }
    }
}
