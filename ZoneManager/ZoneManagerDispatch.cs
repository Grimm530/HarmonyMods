using System;
using System.Reflection;
using UnityEngine;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    public partial class ZoneManager
    {
        internal static void SetInstance(ZoneManager inst) => Instance = inst;
        internal static void ClearInstance() => Instance = null;
        internal static ZoneManager GetModInstance() => Instance;

        public void CallInit()
        {
            try { Init(); }
            catch (Exception ex) { Debug.LogWarning("[ZoneManager] Init failed: " + ex); }
            ResolvePluginReferences();
        }

        public void CallOnServerInitialized()
        {
            try { OnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[ZoneManager] OnServerInitialized failed: " + ex); }
            ResolvePluginReferences();
        }

        public void CallUnload()
        {
            try { Unload(); }
            catch (Exception ex) { Debug.LogWarning("[ZoneManager] Unload: " + ex.Message); }
        }

        public void HarmonyRegisterPermissions()
        {
            permission.RegisterPermission(PERMISSION_ZONE, this);
            foreach (string flag in ZoneFlags.NameToIndex.Keys)
                permission.RegisterPermission(PERMISSION_IGNORE_FLAG + flag.ToLower(), this);
        }

        public void ResolvePluginReferences()
        {
            try
            {
                Backpacks = plugins?.Find("Backpacks");
                Spawns = plugins?.Find("Spawns");
                PopupNotifications = null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ZoneManager] ResolvePluginReferences: " + ex.Message);
            }
        }

        public bool HarmonyNoSuicide(BasePlayer player)
        {
            if (player == null || !IsSubscribed(nameof(OnServerCommand))) return false;
            if (!HasPlayerFlag(player, ZoneFlags.NoSuicide)) return false;
            SendMessage(player, Message("noSuicide", player.UserIDString));
            return true;
        }

        public void HarmonyCcmdEditFlag(ConsoleSystem.Arg arg) => ccmdEditFlag(arg);

        public static bool IsHookSubscribed(string hookName)
        {
            var inst = Instance;
            return inst != null && inst.IsSubscribed(hookName);
        }

        private static void Warn(string hook, Exception ex)
        {
            Debug.LogWarning("[ZoneManager] " + hook + ": " + ex.Message);
        }

        public static void Dispatch_OnPlayerConnected(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || player == null) return;
            try
            {
                inst.ResolvePluginReferences();
                inst.OnPlayerConnected(player);
            }
            catch (Exception ex) { Warn(nameof(OnPlayerConnected), ex); }
        }

        public static void Dispatch_OnPlayerDisconnected(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || player == null || !IsHookSubscribed(nameof(OnPlayerDisconnected))) return;
            try { inst.OnPlayerDisconnected(player); }
            catch (Exception ex) { Warn(nameof(OnPlayerDisconnected), ex); }
        }

        public static void Dispatch_OnPlayerSleep(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || player == null) return;
            try { inst.OnPlayerSleep(player); }
            catch (Exception ex) { Warn(nameof(OnPlayerSleep), ex); }
        }

        public static void Dispatch_OnPlayerSleepEnd(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || player == null) return;
            try { inst.OnPlayerSleepEnd(player); }
            catch (Exception ex) { Warn(nameof(OnPlayerSleepEnd), ex); }
        }

        public static void Dispatch_OnTerrainInitialized()
        {
            var inst = Instance;
            if (inst == null) return;
            try { inst.OnTerrainInitialized(); }
            catch (Exception ex) { Warn(nameof(OnTerrainInitialized), ex); }
        }

        public static void Dispatch_OnEntityKill(BaseEntity entity)
        {
            var inst = Instance;
            if (inst == null || entity == null) return;
            try { inst.OnEntityKill(entity); }
            catch (Exception ex) { Warn(nameof(OnEntityKill), ex); }
        }

        public static void Dispatch_OnEntitySpawned(BaseEntity entity)
        {
            var inst = Instance;
            if (inst == null || entity == null) return;
            try { inst.OnEntitySpawned(entity); }
            catch (Exception ex) { Warn(nameof(OnEntitySpawned), ex); }
        }

        public static void Dispatch_OnEntityBuilt(Planner planner, GameObject go)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(OnEntityBuilt))) return;
            try { inst.OnEntityBuilt(planner, go); }
            catch (Exception ex) { Warn(nameof(OnEntityBuilt), ex); }
        }

        public static object Dispatch_OnStructureUpgrade(BuildingBlock block, BasePlayer player, BuildingGrade.Enum grade)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(OnStructureUpgrade))) return null;
            try { return inst.OnStructureUpgrade(block, player, grade); }
            catch (Exception ex) { Warn(nameof(OnStructureUpgrade), ex); return null; }
        }

        public static void Dispatch_OnItemDeployed(Deployer deployer, ItemModDeployable mod, BaseEntity entity)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(OnItemDeployed))) return;
            try { inst.OnItemDeployed(deployer, mod, entity); }
            catch (Exception ex) { Warn(nameof(OnItemDeployed), ex); }
        }

        public static void Dispatch_OnItemDeployed(Deployer deployer, BaseEntity parent, BaseEntity entity)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(OnItemDeployed))) return;
            try { inst.OnItemDeployed(deployer, parent, entity); }
            catch (Exception ex) { Warn(nameof(OnItemDeployed), ex); }
        }

        public static void Dispatch_OnItemUse(Item item, int amount)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(OnItemUse))) return;
            try { inst.OnItemUse(item, amount); }
            catch (Exception ex) { Warn(nameof(OnItemUse), ex); }
        }

        public static object Dispatch_OnPlayerChat(BasePlayer player, string message, ConVar.Chat.ChatChannel channel)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(OnPlayerChat))) return null;
            try { return inst.OnPlayerChat(player, message, channel); }
            catch (Exception ex) { Warn(nameof(OnPlayerChat), ex); return null; }
        }

        public static object Dispatch_OnPlayerVoice(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(OnPlayerVoice))) return null;
            try { return inst.OnPlayerVoice(player, Array.Empty<byte>()); }
            catch (Exception ex) { Warn(nameof(OnPlayerVoice), ex); return null; }
        }

        public static object Dispatch_OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || entity == null || !IsHookSubscribed(nameof(OnEntityTakeDamage))) return null;
            try { return inst.OnEntityTakeDamage(entity, info); }
            catch (Exception ex) { Warn(nameof(OnEntityTakeDamage), ex); return null; }
        }

        public static object Dispatch_CanBeWounded(BasePlayer player, HitInfo info)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(CanBeWounded))) return null;
            try { return inst.CanBeWounded(player, info); }
            catch (Exception ex) { Warn(nameof(CanBeWounded), ex); return null; }
        }

        public static object Dispatch_CanUpdateSign(BasePlayer player, Signage sign)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(CanUpdateSign))) return null;
            try { return inst.CanUpdateSign(player, sign); }
            catch (Exception ex) { Warn(nameof(CanUpdateSign), ex); return null; }
        }

        public static object Dispatch_OnOvenToggle(BaseOven oven, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(OnOvenToggle))) return null;
            try { return inst.OnOvenToggle(oven, player); }
            catch (Exception ex) { Warn(nameof(OnOvenToggle), ex); return null; }
        }

        public static object Dispatch_CanUseVending(BasePlayer player, VendingMachine machine)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(CanUseVending))) return null;
            try { return inst.CanUseVending(player, machine); }
            catch (Exception ex) { Warn(nameof(CanUseVending), ex); return null; }
        }

        public static object Dispatch_CanHideStash(BasePlayer player, StashContainer stash)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(CanHideStash))) return null;
            try { return inst.CanHideStash(player, stash); }
            catch (Exception ex) { Warn(nameof(CanHideStash), ex); return null; }
        }

        public static object Dispatch_CanCraft(ItemCrafter crafter, ItemBlueprint bp, int amount)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(CanCraft))) return null;
            try { return inst.CanCraft(crafter, bp, amount); }
            catch (Exception ex) { Warn(nameof(CanCraft), ex); return null; }
        }

        public static void Dispatch_OnDoorOpened(Door door, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(OnDoorOpened))) return;
            try { inst.OnDoorOpened(door, player); }
            catch (Exception ex) { Warn(nameof(OnDoorOpened), ex); }
        }

        public static object Dispatch_OnSprayCreate(SprayCan spray, Vector3 position, Quaternion rotation)
        {
            var inst = Instance;
            if (inst == null) return null;
            try { return inst.OnSprayCreate(spray, position, rotation); }
            catch (Exception ex) { Warn(nameof(OnSprayCreate), ex); return null; }
        }

        public static object Dispatch_CanLootPlayer(BasePlayer target, BasePlayer looter)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(CanLootPlayer))) return null;
            try { return inst.CanLootPlayer(target, looter); }
            catch (Exception ex) { Warn(nameof(CanLootPlayer), ex); return null; }
        }

        public static void Dispatch_OnLootPlayer(BasePlayer looter, BasePlayer target)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(OnLootPlayer))) return;
            try { inst.OnLootPlayer(looter, target); }
            catch (Exception ex) { Warn(nameof(OnLootPlayer), ex); }
        }

        public static void Dispatch_OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(OnLootEntity))) return;
            try { inst.OnLootEntity(player, entity); }
            catch (Exception ex) { Warn(nameof(OnLootEntity), ex); }
        }

        public static object Dispatch_CanLootEntity(BasePlayer player, BaseEntity entity)
        {
            var inst = Instance;
            if (inst == null || entity == null || !IsHookSubscribed(nameof(CanLootEntity))) return null;
            try
            {
                if (entity is LootableCorpse corpse) return inst.CanLootEntity(player, corpse);
                if (entity is DroppedItemContainer container) return inst.CanLootEntity(player, container);
                if (entity is StorageContainer storage) return inst.CanLootEntity(player, storage);
            }
            catch (Exception ex) { Warn(nameof(CanLootEntity), ex); }
            return null;
        }

        public static object Dispatch_CanPickupEntity(BasePlayer player, BaseCombatEntity entity)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(CanPickupEntity))) return null;
            try { return inst.CanPickupEntity(player, entity); }
            catch (Exception ex) { Warn(nameof(CanPickupEntity), ex); return null; }
        }

        public static object Dispatch_CanPickupLock(BasePlayer player, BaseLock baseLock)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(CanPickupLock))) return null;
            try { return inst.CanPickupLock(player, baseLock); }
            catch (Exception ex) { Warn(nameof(CanPickupLock), ex); return null; }
        }

        public static object Dispatch_OnItemPickup(Item item, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(OnItemPickup))) return null;
            try { return inst.OnItemPickup(item, player); }
            catch (Exception ex) { Warn(nameof(OnItemPickup), ex); return null; }
        }

        public static object Dispatch_OnGather(BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || player == null) return null;
            if (!IsHookSubscribed(nameof(OnCollectiblePickup)) &&
                !IsHookSubscribed(nameof(OnGrowableGather)) &&
                !IsHookSubscribed(nameof(OnDispenserGather)) &&
                !IsHookSubscribed(nameof(CanLootEntity)))
                return null;
            try { return inst.OnGatherInternal(player); }
            catch (Exception ex) { Warn("OnGather", ex); return null; }
        }

        public static object Dispatch_OnTurretTarget(AutoTurret turret, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(OnTurretTarget))) return null;
            try { return inst.OnTurretTarget(turret, player); }
            catch (Exception ex) { Warn(nameof(OnTurretTarget), ex); return null; }
        }

        public static object Dispatch_CanBradleyApcTarget(BradleyAPC apc, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(CanBradleyApcTarget))) return null;
            try { return inst.CanBradleyApcTarget(apc, player); }
            catch (Exception ex) { Warn(nameof(CanBradleyApcTarget), ex); return null; }
        }

        public static object Dispatch_CanHelicopterTarget(PatrolHelicopterAI heli, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(CanHelicopterTarget))) return null;
            try { return inst.CanHelicopterTarget(heli, player); }
            catch (Exception ex) { Warn(nameof(CanHelicopterTarget), ex); return null; }
        }

        public static object Dispatch_CanHelicopterStrafeTarget(PatrolHelicopterAI heli, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(CanHelicopterStrafeTarget))) return null;
            try { return inst.CanHelicopterStrafeTarget(heli, player); }
            catch (Exception ex) { Warn(nameof(CanHelicopterStrafeTarget), ex); return null; }
        }

        public static object Dispatch_OnHelicopterTarget(HelicopterTurret turret, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(OnHelicopterTarget))) return null;
            try { return inst.OnHelicopterTarget(turret, player); }
            catch (Exception ex) { Warn(nameof(OnHelicopterTarget), ex); return null; }
        }

        public static object Dispatch_OnNpcTarget(BaseCombatEntity npc, BasePlayer player)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(OnNpcTarget))) return null;
            try { return inst.OnNpcTarget(npc, player); }
            catch (Exception ex) { Warn(nameof(OnNpcTarget), ex); return null; }
        }

        public static object Dispatch_CanMountEntity(BasePlayer player, BaseMountable entity)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(CanMountEntity))) return null;
            try { return inst.CanMountEntity(player, entity); }
            catch (Exception ex) { Warn(nameof(CanMountEntity), ex); return null; }
        }

        public static object Dispatch_CanDismountEntity(BasePlayer player, BaseMountable entity)
        {
            var inst = Instance;
            if (inst == null || !IsHookSubscribed(nameof(CanDismountEntity))) return null;
            try { return inst.CanDismountEntity(player, entity); }
            catch (Exception ex) { Warn(nameof(CanDismountEntity), ex); return null; }
        }
    }
}
