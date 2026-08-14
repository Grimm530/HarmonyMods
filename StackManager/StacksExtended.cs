using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using System.Linq;
using System.Globalization;
using Oxide.Ext.Chaos.Collections;
using Oxide.Ext.Chaos.Data;
using Oxide.Ext.Chaos.UIFramework;
using Oxide.Game.Rust.Cui;
using UnityEngine.UI;
using StackManagerHarmony;

using Chaos = Oxide.Ext.Chaos;
using Color = Oxide.Ext.Chaos.UIFramework.Color;
using Axis = Oxide.Ext.Chaos.UIFramework.Axis;
using Debug = UnityEngine.Debug;
using GridLayoutGroup = Oxide.Ext.Chaos.UIFramework.GridLayoutGroup;
using HorizontalLayoutGroup = Oxide.Ext.Chaos.UIFramework.HorizontalLayoutGroup;
using VerticalLayoutGroup = Oxide.Ext.Chaos.UIFramework.VerticalLayoutGroup;
using UIAnchor = Oxide.Ext.Chaos.UIFramework.Anchor;
using Timer = StackManagerHarmony.Timer;

namespace StackManagerHarmony
{
    [Info("StackManager", "k1lly0u", "2.0.24")]
    public class StacksExtended : StackManagerPluginBase
    {
        public override string Title => "StackManager";
        public override string Name => "StackManager";
        public override VersionNumber Version { get; protected set; } = new VersionNumber(2, 0, 24);

        #region Fields
        private PluginHelper FurnaceSplitter => PluginHelper.For("FurnaceSplitter");

        private Datafile<OrderedHash<string, StackLimit>> m_StackLimits;
        private Datafile<OrderedHash<string, StackLimit>> m_PlayerLimits;
        private Datafile<OrderedHash<string, StorageLimit>> m_StorageLimits;
        private VipLimitsDataFile m_VIPLimits;

        private readonly Hash<string, int> m_DefaultItemStackSizes = new Hash<string, int>();
        private readonly Hash<string, int> m_DefaultStorageStackSizes = new Hash<string, int>();
        private readonly Hash<string, int> m_PrefabNameToItemID = new Hash<string, int>();
        private readonly Hash<string, string> m_ShortPrefabNameToPrefabName = new Hash<string, string>();

        private bool m_HookGiveSoldItem = true;

        [Chaos.Permission]
        private const string ADMIN_PERMISSION = "stackmanager.admin";
        #endregion

        #region Harmony Lifecycle
        public override void HarmonyInit()
        {
            m_CallbackHandler = new CommandCallbackHandler(this);
            LoadConfiguration();
            RegisterMessages();
            Host?.ReloadLanguage();
            RegisterConsoleCommands();
        }

        public CommandCallbackHandler CallbackHandler => m_CallbackHandler;

        public override void HarmonyServerInitialized()
        {
            m_StackLimits = new Datafile<OrderedHash<string, StackLimit>>("StacksExtended/stack_limits");
            m_PlayerLimits = new Datafile<OrderedHash<string, StackLimit>>("StacksExtended/player_overrides");
            m_StorageLimits = new Datafile<OrderedHash<string, StorageLimit>>("StacksExtended/storage_limits");
            m_VIPLimits = new VipLimitsDataFile("StacksExtended/vip_limits");

            RegisterPermissionsWithPermissionsMod();

            InitializeUI();
            CheckUpdateConfiguration();

            // Match SE intent: OnGiveSoldItem only useful when inventory stack limit is set.
            m_HookGiveSoldItem = Configuration.Player.InventoryStackLimit > 0;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
                OnPlayerConnected(player);

            foreach (BasePlayer player in BasePlayer.sleepingPlayerList)
                OnPlayerConnected(player);
        }

        public void RegisterPermissionsWithPermissionsMod()
        {
            if (!permission.PermissionExists(ADMIN_PERMISSION, this))
                permission.RegisterPermission(ADMIN_PERMISSION, this);

            if (m_VIPLimits?.Data != null)
            {
                foreach (string perm in m_VIPLimits.Data.Keys)
                {
                    if (!string.IsNullOrEmpty(perm) && !permission.PermissionExists(perm, this))
                        permission.RegisterPermission(perm, this);
                }
            }

            EnsureAdminGroupPermission();
        }

        private void EnsureAdminGroupPermission()
        {
            const string ADMIN_GROUP = "admin";
            if (!PermissionsBridge.IsAvailable)
            {
                Debug.LogWarning("[StackManager] Permissions not available - cannot grant stackmanager.admin to admin group.");
                return;
            }

            if (!permission.GroupExists(ADMIN_GROUP))
                permission.CreateGroup(ADMIN_GROUP, "Administrators", 0);

            permission.GrantGroupPermission(ADMIN_GROUP, ADMIN_PERMISSION, this);
            Debug.Log("[StackManager] Ensured Permissions group 'admin' has stackmanager.admin. Put staff in that group to use /stacks.");
        }

        public override void HarmonyUnload()
        {
            UnregisterConsoleCommands();

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                SetPlayerStackSize(player, true);
                OnPlayerDisconnected(player);
            }

            foreach (BasePlayer player in BasePlayer.sleepingPlayerList)
                SetPlayerStackSize(player, true);

            ResetContainerStackSizes();
            ResetItemStackSizes();
        }

        public void ClearAllUis()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                OnPlayerDisconnected(player);
        }

        public void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;
            if (m_UIUsers.ContainsKey(player.userID))
            {
                m_UIUsers.Remove(player.userID);
                CuiHelper.DestroyUi(player, STACKS_UI);
                CuiHelper.DestroyUi(player, POPUP_UI);
            }
        }

        private readonly List<ConsoleSystem.Command> m_RegisteredConsoleCommands = new List<ConsoleSystem.Command>();

        private void RegisterConsoleCommands()
        {
            RegisterServerCommand("se.stackcategory", ccmdStackCategory);
            RegisterServerCommand("se.stackcategorylimit", ccmdStackCategoryLimit);
            RegisterServerCommand("se.stackcategorymultiplier", ccmdStackCategoryMultiplier);
            RegisterServerCommand("se.stackitem", ccmdStackItem);
            RegisterServerCommand("se.loadoldconfig", ccmdLoadOldConfig);
        }

        private void RegisterServerCommand(string fullName, Action<ConsoleSystem.Arg> callback)
        {
            if (string.IsNullOrEmpty(fullName) || callback == null) return;
            try
            {
                int dot = fullName.IndexOf('.');
                string parent = dot > 0 ? fullName.Substring(0, dot) : fullName;
                string name = dot > 0 ? fullName.Substring(dot + 1) : fullName;
                var cmd = new ConsoleSystem.Command
                {
                    Name = name,
                    Parent = parent,
                    FullName = fullName,
                    ServerAdmin = true,
                    ServerUser = false,
                    Client = false,
                    Variable = false,
                    Call = arg =>
                    {
                        try { callback(arg); }
                        catch (Exception ex) { Debug.LogWarning("[StackManager] " + fullName + ": " + ex.Message); }
                    }
                };
                ConsoleSystem.Index.Server.Dict[cmd.FullName] = cmd;
                m_RegisteredConsoleCommands.Add(cmd);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StackManager] RegisterServerCommand " + fullName + ": " + ex.Message);
            }
        }

        private void UnregisterConsoleCommands()
        {
            foreach (var cmd in m_RegisteredConsoleCommands)
            {
                try
                {
                    if (cmd != null && !string.IsNullOrEmpty(cmd.FullName))
                        ConsoleSystem.Index.Server.Dict.Remove(cmd.FullName);
                }
                catch { }
            }
            m_RegisteredConsoleCommands.Clear();
        }
        #endregion

        #region Localization
        protected override void RegisterMessages()
        {
            var messages = new Dictionary<string, string>
            {
                ["Button.Create"] = "Create",
                ["Button.Cancel"] = "Cancel",
                ["Button.Exit"] = "Exit",
                ["Button.Item"] = "Items",
                ["Button.Storage"] = "Containers",
                ["Button.PlayerOverrides"] = "Player Overrides",
                ["Button.VIPItems"] = "VIP Items",
                ["Button.VIPStorage"] = "VIP Containers",
                ["Button.ItemOverrides"] = "Item Overrides ({0} active)",
                ["Button.AddItemOverride"] = "Add Item Override",
                ["Button.AddStorageOverride"] = "Add Container Override",
                ["Button.AddCustomPermission"] = "Add VIP Permission",

                ["Label.StackSize"] = "Stack Size",
                ["Label.StackMultiplier"] = "Stack Multiplier",
                ["Label.DefaultStackSize"] = "Default stack size : {0}",
                ["Label.AddItemOverride"] = "Add item override",
                ["Label.AddStorageOverride"] = "Add container override",
                ["Label.CreateVIPPermission"] = "Create VIP Permission",
                ["Label.SelectPermission"] = "Select a permission to continue",
                ["Label.VIPPriority"] = "Priority",
                ["Label.VIPItems"] = "Editing item overrides for permission {0}",
                ["Label.VIPStorage"] = "Editing container overrides for permission {0}",
                ["Label.PlayerInventoryOverrides"] = "Player inventory stack overrides",
                ["Label.MaxStackSize"] = "Max Stack Size",
                ["Label.StorageHint"] = "Max stack size of 0 means no container based limit",
                ["Label.ContainerItemOverrides"] = "Item overrides for container {0}",
                ["Label.ContainerItemOverrides.Permission"] = "Item overrides for container {0} with permission {1}",
                
                ["Error.EnterPermission"] = "You must enter a permission in the input field",
                ["Error.Error.PermissionExists"] = "That permission already exists",
                ["Error.PermissionExists"] = "That permission already exists",
                ["Error.NoPermission"] = "You do not have permission to use this command"
            };

            lang?.RegisterMessages(messages);
        }

        #endregion
        
        #region Item Management
        public object CanStackItem(Item item, Item otherItem)
        {
            if (item.parent is { entityOwner: LootContainer })
                return null;
            
            if (Configuration.Exclude.IsExcluded(item))
                return null;

            bool canStack = otherItem != item && item.info.stackable > 1 && otherItem.info.stackable > 1 &&
                            otherItem.info.itemid == item.info.itemid && item.IsValid() &&
                            (!item.IsBlueprint() || item.blueprintTarget == otherItem.blueprintTarget);

            if (!canStack)
                return false;

            if (item.MaxStackable() <= 1)
                return false;

            // Vanilla only stacks condition items at exact max HP. Allow matching durability
            // so deployables (furnaces, boxes, etc.) can stack when both items are equal.
            if (item.hasCondition || otherItem.hasCondition)
            {
                if (!item.hasCondition || !otherItem.hasCondition)
                    return false;
                if (!Mathf.Approximately(item.condition, otherItem.condition) ||
                    !Mathf.Approximately(item.maxCondition, otherItem.maxCondition))
                    return false;
            }
            
            if (item.GetHeldEntity() is BaseLiquidVessel)
            {
                if (!Configuration.Options.EnableLiquidContainerStacks) 
                    return false;
                
                if (item.contents != null && otherItem.contents != null && (item.contents.itemList.Count != otherItem.contents.itemList.Count ||
                     item.contents.itemList.Count > 0 && otherItem.contents.itemList.Count > 0 && item.contents.itemList[0].amount != otherItem.contents.itemList[0].amount)) 
                    return false;
            }

            if (Configuration.Options.BlockModdedWeaponStacks || Configuration.Options.BlockUnequalAmmoWeaponStacks)
            {
                BaseProjectile targetProjectile = item.GetHeldEntity() as BaseProjectile;
                BaseProjectile sourceProjectile = otherItem.GetHeldEntity() as BaseProjectile;

                if (!Configuration.Options.EnableProjectileWeaponStacks && (targetProjectile || sourceProjectile))
                    return false;

                if (Configuration.Options.BlockModdedWeaponStacks && ((targetProjectile && item.contents?.itemList?.Count > 0) || (sourceProjectile && otherItem.contents?.itemList?.Count > 0)))
                    return false;

                if (Configuration.Options.BlockUnequalAmmoWeaponStacks && sourceProjectile && targetProjectile)
                {
                    if (targetProjectile.primaryMagazine.contents != sourceProjectile.primaryMagazine.contents || targetProjectile.primaryMagazine.ammoType != sourceProjectile.primaryMagazine.ammoType)
                        return false;
                }
            }

            if (Configuration.Options.BlockModdedAttireStacks && item.info.category == ItemCategory.Attire && otherItem.info.category == ItemCategory.Attire)
            {
                if (item.contents?.itemList?.Count > 0 || otherItem.contents?.itemList?.Count > 0)
                    return false;
                
                if (item.contents?.capacity != otherItem.contents?.capacity)
                    return false;
            }

            if (Configuration.Options.BeltAntiToolWeaponStack && item.parent != null && item.parent.HasFlag(ItemContainer.Flag.Belt) && item.info == otherItem.info)
            { 
                BaseEntity itemEntity = item.GetHeldEntity();
                BaseEntity otherEntity = otherItem.GetHeldEntity();

                if (!itemEntity)
                {
                    ItemModEntity itemModEntity = item.info.GetComponent<ItemModEntity>();
                    itemEntity = itemModEntity ? itemModEntity.entityPrefab.GetEntity() : null;
                }

                if (!otherEntity)
                {
                    ItemModEntity otherModEntity = otherItem.info.GetComponent<ItemModEntity>();
                    otherEntity = otherModEntity ? otherModEntity.entityPrefab.GetEntity() : null;
                }

                if (itemEntity && otherEntity)
                {
                    if (itemEntity is AttackEntity and not ThrownWeapon && otherEntity is not MedicalTool)
                        return false;
                }
            }

            if (item.skin != otherItem.skin && Configuration.Options.BlockDifferentSkinStacks)
                return false;

            if ((!string.IsNullOrEmpty(item.name) || !string.IsNullOrEmpty(otherItem.name)) && item.name != otherItem.name)
                return false;

            if (item.iconImageId != otherItem.iconImageId)
                return false;

            if (item.info.amountType == ItemDefinition.AmountType.Genetics || otherItem.info.amountType == ItemDefinition.AmountType.Genetics)
            {
                int geneticsA = item.instanceData != null ? item.instanceData.dataInt : -1;
                int geneticsB = otherItem.instanceData != null ? otherItem.instanceData.dataInt : -1;
                if (geneticsA != geneticsB)
                    return false;
            }

            if (HasUniqueSignData(item) || HasUniqueSignData(otherItem))
                return false;

            return true;
        }

        private static bool HasUniqueSignData(Item item)
        {
            if (item?.instanceData == null || !item.instanceData.subEntity.IsValid)
                return false;
            return item.info != null && item.info.GetComponent<ItemModSign>();
        }

        public object CanMoveItem(Item item, PlayerInventory playerInventory, ItemContainerId targetContainerID, int targetSlot, int amount, ItemMoveModifier itemMoveModifier)
        {
            if (item == null || !playerInventory)
                return null;

            /*
            Debug.Log($"CanMoveItem item {item.info.shortname} targetContId {targetContainerID} targetSlot {targetSlot} amount {amount} itemMoveModifier {itemMoveModifier}");
            */
            
            if (item.parent?.entityOwner)
            {
                if (item.parent.entityOwner.GetComponent("Oxide.Plugins.SkinBox/LootHandler") != null)
                    return null;
            }

            if (Configuration.Exclude.IsExcluded(item))
                return null;
            
            if (!targetContainerID.IsValid)
            {
                BaseEntity entityOwner = item.GetEntityOwner();
                
                if (playerInventory.loot.containers.Count > 0)
                    entityOwner = entityOwner == playerInventory.baseEntity ? playerInventory.loot.entitySource : playerInventory.baseEntity;

                IIdealSlotEntity idealSlotEntity = entityOwner as IIdealSlotEntity;
                if (idealSlotEntity != null)
                    targetContainerID = idealSlotEntity.GetIdealContainer(playerInventory.baseEntity, item, itemMoveModifier);
                
                if (!targetContainerID.IsValid && entityOwner is StorageContainer)
                    targetContainerID = (entityOwner as StorageContainer).inventory.uid;

                /*if (!targetContainerID.IsValid && entityOwner == playerInventory.loot.entitySource)
                {
                    foreach (ItemContainer inventoryContainer in playerInventory.loot.containers)
                    {
                        if (!inventoryContainer.PlayerItemInputBlocked() && 
                            !inventoryContainer.IsLocked() && 
                            item.MoveToContainer(inventoryContainer, -1, true, false, playerInventory.baseEntity, true))
                        {
                            targetContainerID = inventoryContainer.uid;
                            break;
                        }
                    }
                }*/
            }
            
            ItemContainer itemContainer = playerInventory.FindContainer(targetContainerID);
            if (itemContainer == null)
                return null;
            
            if (IsUsingFurnaceSplitter(playerInventory, item))
                return null;

            if (item.parent != null)
            {
                if (item.parent.IsLocked() || itemContainer.IsLocked() || itemContainer.PlayerItemInputBlocked() || !CanMoveItemsFrom(playerInventory, item.parent?.entityOwner, item))
                    return null;
                
                if (itemContainer != item.parent)
                {
                    BaseEntity heldEntity = item.GetHeldEntity();
                    if (itemContainer.HasFlag(ItemContainer.Flag.Belt) && item.amount > 1 && heldEntity is AttackEntity && !(heldEntity is ThrownWeapon) && !(heldEntity is MedicalTool))
                    {
                        if (item.amount > 1 && playerInventory.containerBelt.SlotTaken(item, targetSlot))
                            return false;

                        if (playerInventory.containerBelt.SlotTaken(item, targetSlot) && playerInventory.containerBelt.GetSlot(targetSlot).info == item.info)
                            return null;

                        Item splitItem = item.SplitItem(1);
                        if (splitItem != null && !splitItem.MoveToContainer(playerInventory.containerBelt, targetSlot, false))
                        {
                            if (!splitItem.MoveToContainer(playerInventory.containerBelt, -1, false))
                                playerInventory.GiveItem(splitItem, null);
                        }

                        playerInventory.ServerUpdate(0f);
                        return false;
                    }

                    if (itemContainer.SlotTaken(item, targetSlot))
                    {
                        Item slot = itemContainer.GetSlot(targetSlot);
                        if (slot != null)
                        {
                            if (slot.info == item.info && !slot.CanStack(item))
                                return null;

                            heldEntity = slot.GetHeldEntity();

                            if (slot.amount > 1 && heldEntity is AttackEntity && !(heldEntity is ThrownWeapon) && !(heldEntity is MedicalTool))
                                return false;
                        }
                    }
                }

                if (targetSlot != -1 && itemContainer.entityOwner != item.parent.entityOwner)
                {
                    Item otherItem = itemContainer.GetSlot(targetSlot);
                    if (otherItem != null && otherItem.info.itemid != item.info.itemid)
                    {
                        if (item.parent.CanAcceptItem(otherItem, -1) == ItemContainer.CanAcceptResult.CanAccept)
                        {
                            int storageLimit = GetMaxStackable(otherItem, item.parent);
                            if (storageLimit > 0)
                            {
                                int splitAmount = Mathf.FloorToInt((float) otherItem.amount / (float) storageLimit) - 1;

                                //Debug.Log($"item {item.info.shortname} | other {otherItem.info.shortname} | storagel {storageLimit} | splitam {splitAmount} | canaccept {item.parent.CanAcceptItem(otherItem, -1)}");
                                for (int i = 0; i < splitAmount; i++)
                                {
                                    Item splitItem;
                                    if (item.parent.itemList.Count >= item.parent.capacity)
                                    {
                                        splitItem = otherItem.SplitItem(storageLimit * (splitAmount - i));
                                        if (splitItem == null)
                                            break;
                                        splitItem.Drop(itemContainer.dropPosition, itemContainer.dropVelocity);
                                        break;
                                    }

                                    splitItem = otherItem.SplitItem(storageLimit);
                                    if (splitItem == null)
                                        break;
                                    if (!splitItem.MoveToContainer(item.parent))
                                        splitItem.Drop(itemContainer.dropPosition, itemContainer.dropVelocity);
                                }
                            }
                        }
                    }
                }
            }

            if (amount <= 0)
                amount = item.amount;
		    
            amount = Mathf.Clamp(amount, 1, GetMaxStackable(item, itemContainer));
            
            if (playerInventory.baseEntity.GetActiveItem() == item)
                playerInventory.baseEntity.UpdateActiveItem(default(ItemId));

            if (amount > 0 && item.amount > amount)
			{
				int split_Amount = amount;
				if (itemContainer.maxStackSize > 0)
                    split_Amount = Mathf.Min(amount, itemContainer.maxStackSize);

                if (split_Amount > 0)
                {
                    Item splitItem = item.SplitItem(split_Amount);
                    if (splitItem != null)
                    {
                        if (!splitItem.MoveToContainer(itemContainer, targetSlot, true, false, playerInventory.baseEntity, true))
                        {
                            item.amount += splitItem.amount;
                            splitItem.Remove(0f);
                        }

                        ItemManager.DoRemoves();
                        playerInventory.ServerUpdate(0f);
                        return false;
                    }
                    // Split failed (e.g. amount edge case) — fall through to full-item move
                }
			}

            if (!item.MoveToContainer(itemContainer, targetSlot, true, false, playerInventory.baseEntity, true))
                return null;
            
		    ItemManager.DoRemoves();
            playerInventory.ServerUpdate(0f);
            return false;
        }
        
        public object OnItemAction(Item item, string action)
        {
            if (item == null)
                return null;
            
            if (Configuration.Exclude.IsExcluded(item))
                return null;

            if (item.GetHeldEntity() is BaseProjectile && item.amount > 1 && action == "unload_ammo")
                return false;

            return null;
        }

        public object OnItemSplit(Item item, int splitAmount)
        {
            if (item == null || splitAmount <= 0 || Configuration.Exclude.IsExcluded(item))
                return null;

            BaseEntity heldEntity = item.GetHeldEntity();
            if (heldEntity)
            {
                if (heldEntity is BaseLiquidVessel)
                {
                    Item splitItem = SplitItem(item, splitAmount);
                    if (splitItem == null)
                        return null;
                    
                    if (item.contents != null && item.contents.itemList.Count > 0 && splitItem.contents != null && splitItem.contents.itemList.Count > 0)
                        splitItem.contents.itemList[0].amount = item.contents.itemList[0].amount;
                    
                    return splitItem;
                }

                if (heldEntity is BaseProjectile)
                {
                    Item splitItem = SplitItem(item, splitAmount);
                    if (splitItem == null)
                        return null;

                    BaseProjectile splitBaseProjectile = splitItem.GetHeldEntity() as BaseProjectile;
                    if (splitBaseProjectile != null)
                    {
                        splitBaseProjectile.primaryMagazine.contents = (heldEntity as BaseProjectile).primaryMagazine.contents;
                        splitBaseProjectile.SendNetworkUpdateImmediate();
                    }

                    return splitItem;
                }

                if (item.skin != 0UL)
                    return SplitItem(item, splitAmount);
            }
            else if (item.skin != 0UL)
                return SplitItem(item, splitAmount);
                
            return null;
        }

        public object OnMaxStackable(Item item)
        {
            if (item?.info == null || Configuration?.Exclude == null || Configuration.Player == null)
                return null;

            if (Configuration.Exclude.IsExcluded(item))
                return null;
            
            if (item.parent == null || (int) item.parent.flags == 3)
                return null;

            BaseEntity entity = item.parent.entityOwner;
            if (entity is LootContainer || Configuration.Exclude.IsExcluded(item.parent))
                return null;
            
            return GetMaxStackable(item, item.parent);
        }

        private int GetMaxStackable(Item item, ItemContainer container)
        {
            if (item?.info == null || container == null)
                return 1;

            int maxStackable = 0;

            if ((int)container.flags == 1 || (int)container.flags == 5)
            {
                if (Configuration.Player.UseDefaultBeltStacks && (int)container.flags == 5)
                {
                    if (!m_DefaultItemStackSizes.TryGetValue(item.info.shortname, out maxStackable))
                        maxStackable = item.info.stackable;
                }
                else
                {
                    maxStackable = Configuration.Player.InventoryStackLimit;

                    if (m_PlayerLimits?.Data != null && m_PlayerLimits.Data.TryGetValue(item.info.shortname, out StackLimit stackLimit))
                        maxStackable = stackLimit.GetStackSize();
                }
            }
            else 
            {
                if (container.entityOwner)
                {
                    if (container.entityOwner is LootContainer)
                        goto SKIP_BASIC;
                    
                    StorageLimit storageLimit;

                    if (container.entityOwner.OwnerID != 0UL && m_VIPLimits?.Data != null)
                    {
                        foreach (KeyValuePair<string, VIPLimits> kvp in m_VIPLimits.Data)
                        {
                            if (kvp.Value?.StorageOverrides != null
                                && container.entityOwner.OwnerID.HasPermission(kvp.Key)
                                && kvp.Value.StorageOverrides.TryGetValue(container.entityOwner.PrefabName, out storageLimit))
                            {
                                maxStackable = storageLimit.GetMaxStackable(item);
                                goto SKIP_BASIC;
                            }
                        }
                    }

                    if (m_StorageLimits?.Data != null && m_StorageLimits.Data.TryGetValue(container.entityOwner.PrefabName, out storageLimit))
                        maxStackable = storageLimit.GetMaxStackable(item);
                    else maxStackable = container.maxStackSize;
                }
            }
            
            SKIP_BASIC:

            return maxStackable > 0 ? maxStackable : item.info.stackable;
        }
        
        public bool CanMoveItemsFrom(PlayerInventory playerInventory, BaseEntity baseEntity, Item item)
        {
            // Only StorageContainers have a CanMoveFrom gate. Non-storage sources
            // (player inventory/belt/wear, backpacks, etc.) must be allowed — returning
            // false here was blocking all normal inventory moves with a permission toast.
            StorageContainer storageContainer = baseEntity as StorageContainer;
            if (!storageContainer)
                return true;

            PlayerInventory.CanMoveFromResponse result = storageContainer.CanMoveFrom(playerInventory.baseEntity, item);
            return result.allowed;
        }

        /// <summary>Used by Harmony patch to preserve the game deny reason/message.</summary>
        public bool TryGetCanMoveItemsFrom(PlayerInventory playerInventory, BaseEntity baseEntity, Item item, out PlayerInventory.CanMoveFromResponse response)
        {
            response = new PlayerInventory.CanMoveFromResponse(true, default);
            StorageContainer storageContainer = baseEntity as StorageContainer;
            if (!storageContainer)
                return true;

            response = storageContainer.CanMoveFrom(playerInventory.baseEntity, item);
            return response.allowed;
        }
        
        private Item SplitItem(Item item, int splitAmount)
        {
            if (item?.info == null || splitAmount <= 0 || splitAmount >= item.amount)
                return null;

            // Always create with amount >= 1 — ItemManager.Create logs and returns null for <= 0
            Item splitItem = ItemManager.CreateByItemID(item.info.itemid, 1, item.skin);
            if (splitItem == null) 
                return null;
            
            item.amount -= splitAmount;
            item.MarkDirty();
            splitItem.amount = splitAmount;
                
            splitItem.OnVirginSpawn();
                    
            if (item.IsBlueprint()) 
                splitItem.blueprintTarget = item.blueprintTarget;
                    
            if (item.hasCondition) 
                splitItem.condition = item.condition;
                    
            splitItem.MarkDirty();
            return splitItem;
        }
        #endregion
        
        #region Vending Management
        public object OnGiveSoldItem(VendingMachine vendingMachine, Item item, BasePlayer player)
        {
            if (!m_HookGiveSoldItem)
                return null;

            if (Configuration.Player.InventoryStackLimit > 0 && item.amount > Configuration.Player.InventoryStackLimit)
            {
                int amountRemaining = item.amount;
                
                while(amountRemaining > 0)
                {
                    int amount = Mathf.Min(amountRemaining, Configuration.Player.InventoryStackLimit);
                    amountRemaining -= amount;
                    if (amount <= 0)
                        break;
                    Item sold = ItemManager.CreateByItemID(item.info.itemid, amount, item.skin);
                    if (sold != null)
                        player.GiveItem(sold, BaseEntity.GiveItemReason.PickedUp);
                }

                item.Remove(0f);
                return true;
            }            

            return null;
        }
        #endregion
        
        #region Container Management
        public void OnEntityBuilt(Planner planner, GameObject obj)
        {
            if (!planner || !obj)
                return;

            BaseEntity baseEntity = obj.GetComponent<BaseEntity>();
            if (!baseEntity || baseEntity.OwnerID == 0UL)
                return;
            
            if (baseEntity is MiningQuarry)
            {
                OnQuarryBuilt(baseEntity as MiningQuarry);
                return;
            }

            if (!(baseEntity is StorageContainer)) 
                return;
            
            BasePlayer player = planner.GetOwnerPlayer();
            if (!player) 
                return;
            
            UpdateStorageContainerStackSize(baseEntity as StorageContainer);
        }

        public void OnLootEntity(BasePlayer player, StorageContainer storageContainer)
        {
            if (storageContainer.OwnerID == player.userID)
                UpdateStorageContainerStackSize(storageContainer);
        }

        private void OnQuarryBuilt(MiningQuarry miningQuarry)
        {
            StorageContainer hopperContainer = miningQuarry.hopperPrefab.instance as StorageContainer;
            if (hopperContainer)
                UpdateStorageContainerStackSize(hopperContainer);
            
            StorageContainer fuelContainer = miningQuarry.fuelStoragePrefab.instance as StorageContainer;
            if (hopperContainer)
                UpdateStorageContainerStackSize(fuelContainer);
        }
        
        private void UpdateStorageContainerStackSize(StorageContainer storageContainer)
        {
            StorageLimit storageLimit;

            foreach (KeyValuePair<string, VIPLimits> kvp in m_VIPLimits.Data)
            {
                if (storageContainer.OwnerID.HasPermission(kvp.Key) && kvp.Value.StorageOverrides.TryGetValue(storageContainer.PrefabName, out storageLimit))
                    goto SKIP_BASIC;
            }

            m_StorageLimits.Data.TryGetValue(storageContainer.PrefabName, out storageLimit);
            
            SKIP_BASIC:
            
            if (storageLimit != null && storageContainer.inventory.maxStackSize != storageLimit.MaxStackSize)
            {
                storageContainer.inventory.maxStackSize = storageLimit.MaxStackSize;
                storageContainer.maxStackSize = storageLimit.MaxStackSize;
                storageContainer.SendNetworkUpdate();
            }
        }
        #endregion
        
        #region Player Management

        public void OnPlayerRespawned(BasePlayer player) => SetPlayerStackSize(player);

        public void OnPlayerConnected(BasePlayer player)
        {
            SetPlayerStackSize(player);
            //CheckUserPermissions(player.UserIDString);
        }

        private void SetPlayerStackSize(BasePlayer player, bool unload = false)
        {
            if (!player || !player.inventory)
                return;

            if (unload)
            {
                player.inventory.containerWear.maxStackSize = 0;
                player.inventory.containerMain.maxStackSize = 0;
                player.inventory.containerBelt.maxStackSize = 0;
            }
            else
            {
                player.inventory.containerWear.maxStackSize = 1;
                player.inventory.containerMain.maxStackSize = Configuration.Player.InventoryStackLimit;
                player.inventory.containerBelt.maxStackSize = Configuration.Player.InventoryStackLimit;
            }

            player.inventory.SendSnapshot();
        }
        #endregion

        #region Functions
        private readonly string[] m_IgnoreItems = new string[] { "ammo.snowballgun", "blueprintbase", "rhib", "spraycandecal", "vehicle.chassis", "vehicle.module", "water", "water.salt" };
        
        private void CheckUpdateConfiguration()
        {
            int raised = 0;
            foreach (ItemDefinition itemDefinition in ItemManager.itemList)
            {
                if (m_IgnoreItems.Contains(itemDefinition.shortname))
                    continue;
                
                m_DefaultItemStackSizes[itemDefinition.shortname] = itemDefinition.stackable;

                ItemModDeployable itemModDeployable = itemDefinition.GetComponent<ItemModDeployable>();
                if (itemModDeployable)
                {
                    m_PrefabNameToItemID[itemModDeployable.entityPrefab.resourcePath] = itemDefinition.itemid;

                    StorageContainer storageContainer = itemModDeployable.entityPrefab.GetEntity() as StorageContainer;
                    if (storageContainer)
                    {
                        m_DefaultStorageStackSizes[storageContainer.PrefabName] = storageContainer.maxStackSize;
                        m_ShortPrefabNameToPrefabName[storageContainer.ShortPrefabName.Replace(".deployed", "").Replace("_deployed", "")] = storageContainer.PrefabName;

                        if (!m_StorageLimits.Data.TryGetValue(storageContainer.PrefabName, out StorageLimit storageLimit))
                        {
                            storageLimit = m_StorageLimits.Data[storageContainer.PrefabName] = new StorageLimit
                            {
                                MaxStackSize = storageContainer.maxStackSize,
                                ItemOverrides = new OrderedHash<string, StackLimit>()
                            };
                        }

                        storageLimit.NiceName = PrefabNameToNiceName(storageContainer.PrefabName);
                    }
                }

                if (!m_StackLimits.Data.TryGetValue(itemDefinition.shortname, out StackLimit stackLimit))
                    m_StackLimits.Data.Add(itemDefinition.shortname, stackLimit = new StackLimit(itemDefinition.stackable));

                int previousStack = stackLimit.MaxStackSize;
                ApplyDefaultStackPolicy(itemDefinition, stackLimit);
                if (stackLimit.MaxStackSize != previousStack)
                    raised++;

                stackLimit.ItemDefinition = itemDefinition;
                itemDefinition.stackable = stackLimit.GetStackSize();
            }

            StorageContainer[] resources = UnityEngine.Resources.FindObjectsOfTypeAll<StorageContainer>();

            for (int i = 0; i < resources.Length; i++)
            {
                StorageContainer storageContainer = resources[i];

                if (storageContainer && !(storageContainer is LootContainer))
                {
                    if (string.IsNullOrEmpty(storageContainer.PrefabName) || storageContainer.ShortPrefabName.EndsWith("_static"))
                        continue;

                    if (storageContainer is NPCVendingMachine || storageContainer.inventorySlots == 0 || m_StorageIgnoreList.Contains(storageContainer.PrefabName))
                        continue;

                    m_DefaultStorageStackSizes[storageContainer.PrefabName] = storageContainer.maxStackSize;
                    m_ShortPrefabNameToPrefabName[storageContainer.ShortPrefabName.Replace(".deployed", "").Replace("_deployed", "")] = storageContainer.PrefabName;

                    if (!m_StorageLimits.Data.TryGetValue(storageContainer.PrefabName, out StorageLimit storageLimit))
                    {
                        storageLimit = m_StorageLimits.Data[storageContainer.PrefabName] = new StorageLimit
                        {
                            MaxStackSize = storageContainer.maxStackSize,
                            ItemOverrides = new OrderedHash<string, StackLimit>()
                        };
                    }

                    storageLimit.NiceName = PrefabNameToNiceName(storageContainer.PrefabName);
                }
            }
            
            m_StackLimits.Save();
            m_StorageLimits.Save();

            if (raised > 0)
                Debug.Log($"[StackManager] Default stack policy updated {raised} item limits (vanilla stack-1 items → {Configuration.Defaults?.MinStackForUnstackableItems ?? 10}, honey → {Configuration.Defaults?.MinHoneyStack ?? 100}).");
        }

        private void ApplyDefaultStackPolicy(ItemDefinition def, StackLimit stackLimit)
        {
            if (def == null || stackLimit == null)
                return;

            if (ShouldKeepUnstacked(def))
            {
                if (IsProjectileWeapon(def) && !Configuration.Options.EnableProjectileWeaponStacks)
                    stackLimit.MaxStackSize = 1;
                return;
            }

            int minUnstackable = Configuration.Defaults != null ? Configuration.Defaults.MinStackForUnstackableItems : 10;
            if (minUnstackable > 0 && stackLimit.MaxStackSize <= 1)
                stackLimit.MaxStackSize = minUnstackable;

            if (def.shortname == "honey")
            {
                int minHoney = Configuration.Defaults != null ? Configuration.Defaults.MinHoneyStack : 100;
                if (minHoney > 0 && stackLimit.MaxStackSize < minHoney)
                    stackLimit.MaxStackSize = minHoney;
            }
        }

        private bool ShouldKeepUnstacked(ItemDefinition def)
        {
            if (def == null)
                return true;

            if (Configuration?.Exclude != null && Configuration.Exclude.IsExcluded(def.shortname))
                return true;

            for (int i = 0; i < m_IgnoreItems.Length; i++)
            {
                if (m_IgnoreItems[i] == def.shortname)
                    return true;
            }

            if (IsProjectileWeapon(def))
                return true;

            if (IsVehicleItem(def))
                return true;

            return false;
        }

        private static bool IsProjectileWeapon(ItemDefinition def)
        {
            if (def == null)
                return false;

            ItemModEntity modEntity = def.GetComponent<ItemModEntity>();
            if (modEntity == null || modEntity.entityPrefab == null)
                return false;

            BaseEntity entity = null;
            try
            {
                entity = modEntity.entityPrefab.GetEntity();
            }
            catch
            {
            }

            if (entity is BaseProjectile)
                return true;

            // Prefab not loaded: keep Weapon-category unstackables as guns rather than raising them.
            if (entity == null && def.category == ItemCategory.Weapon && def.stackable <= 1)
                return true;

            return false;
        }

        private static bool IsVehicleItem(ItemDefinition def)
        {
            if (def == null)
                return false;

            if (def.GetComponent<Rust.Modular.ItemModVehicleChassis>() != null)
                return true;
            if (def.GetComponent<Rust.Modular.ItemModVehicleModule>() != null)
                return true;

            ItemModDeployable deployable = def.GetComponent<ItemModDeployable>();
            if (deployable == null || deployable.entityPrefab == null)
                return false;

            try
            {
                return deployable.entityPrefab.GetEntity() is BaseVehicle;
            }
            catch
            {
                return false;
            }
        }

        private bool IsUsingFurnaceSplitter(PlayerInventory playerInventory, Item item)
        {
            if (FurnaceSplitter == null || !FurnaceSplitter.IsLoaded || !playerInventory.loot.IsLooting() || !(playerInventory.loot.entitySource is BaseOven)) 
                return false;
            
            BasePlayer player = playerInventory.baseEntity;
            if (player)
            {
                object isEnabled = FurnaceSplitter.Call("GetEnabled", player);
                if (isEnabled is bool) 
                    return (bool) isEnabled;
            }

            List<BasePlayer> looters = FurnaceSplitter.Call<List<BasePlayer>>("GetLooters", playerInventory.loot.entitySource as BaseOven);
            
            if (looters != null && (looters.Contains(player) || playerInventory.loot.entitySource == item.GetRootContainer()?.entityOwner))
                return true;
            
            return false;
        }
        
        private void ResetContainerStackSizes()
        {
            foreach (BaseNetworkable baseNetworkable in BaseNetworkable.serverEntities)
            {
                StorageContainer storageContainer = baseNetworkable as StorageContainer;
                if (storageContainer && m_DefaultStorageStackSizes.TryGetValue(storageContainer.PrefabName, out int maxStackSize))
                    storageContainer.maxStackSize = maxStackSize;
            }
        }

        private void ResetItemStackSizes()
        {
            foreach (ItemDefinition itemDefinition in ItemManager.itemList)
            {
                if (m_DefaultItemStackSizes.TryGetValue(itemDefinition.shortname, out int stackable))
                    itemDefinition.stackable = stackable;
            }
        }
        #endregion
        
        #region Prefab Nice Names

        private readonly string[] _replaceStrings = new string[]
        {
            "assets/content/vehicles/",
            "assets/content/structures/",
            "assets/content/",
            "assets/bundled/prefabs/static/",
            "assets/prefabs/building/wall.frame.shopfront/",
            "assets/prefabs/gamemodes/objects/",
            "assets/prefabs/misc/halloween/",
            "assets/prefabs/misc/chinesenewyear/sky_lantern/skylantern.",
            "assets/prefabs/misc/summer_dlc/",
            "assets/prefabs/misc/xmas/",
            "assets/prefabs/misc/",
            "assets/bundled/prefabs/",
            "assets/prefabs/deployable/",
            "assets/prefabs/npc/",
            "assets/prefabs/voiceaudio/",
            "assets/scenes/prefabs/",
            "trophy skulls/skins/",
            "cursed_cauldron/",
            "skull_fire_pit/",
            "subents/",
            "tool cupboard/",
            "woodenbox/",
            "repair bench/",
            "bigwheel/",
            "slotmachine/",
            "research table/",
            "photoframe/",
            "twitch/",
            "cassetterecorder/",
            "xmastree/",
            "flame turret/",
            "single shot trap/",
            "tuna can wall lamp/",
            "survivalfishtrap/",
            "reclaim/",
            "coffin/",
            "prefabs/",
            "marketplace/",
            "small stash/",
            "bbq/",
            "trains/",
            "composter/",
            "card table/",
            "jack o lantern/",
            "large wood storage/",
            "mailbox/",
            "mixingtable/",
            "planters/",
            "oil refinery/",
            "locker/",
            "hitch & trough/",
            "campfire/",
            "dropbox/",
            "furnace large/",
            "furnace/",
            "fridge/",
            ".prefab",
            ".deployed",
            ".entity",
            "snowmobiles/",
            "stockings/",
            "submarine/",
            "trainyard/",
            "vendingmachine/",
            "tier 1 workbench/",
            "tier 2 workbench/",
            "tier 3 workbench/",
            "caboose/blackjackmachine/",
            "boats/",
            "casino/",
            "chinesenewyear/chineselantern/",
            "frankensteintable/",
            "excavator/",
            "fireplace/",
            "locomotive/",
            "mlrs/",
            "modularcar/",
            "playerioents/",
            "wagons/",
            "workcart/",
            "wall.frame.",
            "scrap heli carrier/",
            "rowboat/",
            "rhib/",
            "furnace.large/",
            "hot air balloon/",
            "lantern/",
            "carvablepumpkin/",
            "trophy skulls/",
            "hobobarrel/"
        };
        
        private string PrefabNameToNiceName(string prefabName)
        {
            for (int i = 0; i < _replaceStrings.Length; i++)
                prefabName = prefabName.Replace(_replaceStrings[i], "");
            
            prefabName = prefabName.Replace(".", " ")
                                   .Replace("_", " ");

            string[] strs = prefabName.Split('/');
            for (int i = 0; i < strs.Length; i++)
            {
                string[] strs2 = strs[i].Split(' ');

                for (int j = 0; j < strs2.Length; j++)
                    strs2[j] = UppercaseFirstLetter(strs2[j]);
                
                strs[i] = string.Join(" ", strs2);
            }

            return string.Join(" | ", strs);
        }

        private string UppercaseFirstLetter(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;
            
            return Char.ToUpper(str[0]) + str.Substring(1);
        }

        #endregion
        
        #region Images
        private readonly Dictionary<string, string> m_PrefabIconUrls = new Dictionary<string, string>
        {
            ["assets/content/vehicles/modularcar/subents/modular_car_1mod_storage.prefab"] = "https://www.rustedit.io/images/imagelibrary/vehicle.1mod.storage.png",
            ["assets/content/vehicles/modularcar/subents/modular_car_v8_engine_storage.prefab"] = "https://www.rustedit.io/images/imagelibrary/vehicle.1mod.engine.png",
            ["assets/content/vehicles/modularcar/subents/modular_car_fuel_storage.prefab"] = "https://www.rustedit.io/images/stacksextended/modular-fuel-storage.png",
            ["assets/content/vehicles/modularcar/subents/modular_car_camper_storage.prefab"] = "https://www.rustedit.io/images/imagelibrary/vehicle.2mod.camper.png",
            ["assets/prefabs/deployable/bbq/bbq.campermodule.prefab"] = "https://www.rustedit.io/images/imagelibrary/vehicle.2mod.camper.png",
            ["assets/prefabs/deployable/locker/locker.campermodule.prefab"] = "https://www.rustedit.io/images/imagelibrary/vehicle.2mod.camper.png",
            ["assets/content/vehicles/modularcar/subents/modular_car_i4_engine_storage.prefab"] = "https://www.rustedit.io/images/imagelibrary/vehicle.1mod.cockpit.with.engine.png",
            ["assets/content/vehicles/workcart/subents/workcart_fuel_storage.prefab"] = "https://www.rustedit.io/images/stacksextended/workcart.png",
            ["assets/content/vehicles/locomotive/subents/locomotive_fuel_storage.prefab"] = "https://www.rustedit.io/images/stacksextended/locomotive.png",
            ["assets/content/vehicles/boats/rowboat/subents/fuel_storage.prefab"] = "https://www.rustedit.io/images/stacksextended/rowboat.png",
            ["assets/prefabs/deployable/oil jack/fuelstorage.prefab"] = "https://www.rustedit.io/images/stacksextended/pumpjack.png",
            ["assets/prefabs/deployable/oil jack/crudeoutput.prefab"] = "https://www.rustedit.io/images/stacksextended/pumpjack.png",
            ["assets/prefabs/deployable/quarry/fuelstorage.prefab"] = "https://www.rustedit.io/images/stacksextended/quarry.png",
            ["assets/prefabs/deployable/quarry/hopperoutput.prefab"] = "https://www.rustedit.io/images/stacksextended/quarry.png",
            ["assets/prefabs/deployable/hot air balloon/subents/hab_storage.prefab"] = "https://www.rustedit.io/images/stacksextended/hab.png",
            ["assets/content/vehicles/boats/rhib/subents/fuel_storage.prefab"] = "https://www.rustedit.io/images/stacksextended/rhib.png",
            ["assets/content/vehicles/boats/rowboat/subents/rowboat_storage.prefab"] = "https://www.rustedit.io/images/stacksextended/rowboat.png",
            ["assets/content/vehicles/boats/rhib/subents/rhib_storage.prefab"] = "https://www.rustedit.io/images/stacksextended/rhib.png",
            ["assets/content/vehicles/snowmobiles/subents/snowmobileitemstorage.prefab"] = "https://www.rustedit.io/images/stacksextended/snow-mobile.png",
            ["assets/content/vehicles/snowmobiles/subents/snowmobilefuelstorage.prefab"] = "https://www.rustedit.io/images/stacksextended/snow-mobile.png",
            ["assets/content/vehicles/mlrs/subents/mlrs_rocket_storage.prefab"] = "https://www.rustedit.io/images/stacksextended/mlrs.png",
            ["assets/content/vehicles/mlrs/subents/mlrs_dashboard_storage.prefab"] = "https://www.rustedit.io/images/stacksextended/mlrs.png",
            ["assets/content/vehicles/train/subents/wagon_storage_lootwagon.prefab"] = "https://www.rustedit.io/images/stacksextended/loot-wagon.png",
            ["assets/content/vehicles/train/subents/wagon_storage_fuel.prefab"] = "https://www.rustedit.io/images/stacksextended/fuel-wagon.png",
            ["assets/scenes/prefabs/trainyard/subents/coaling_tower_ore_storage.entity.prefab"] = "https://www.rustedit.io/images/stacksextended/coaling-fuel-storage.png",
            ["assets/scenes/prefabs/trainyard/subents/coaling_tower_fuel_storage.entity.prefab"] = "https://www.rustedit.io/images/stacksextended/coaling-ore-storage.png",
            ["assets/bundled/prefabs/static/bbq.static.prefab"] = "https://www.rustedit.io/images/imagelibrary/bbq.png",
            ["assets/bundled/prefabs/static/workbench1.static.prefab"] = "https://www.rustedit.io/images/imagelibrary/workbench1.png",
            ["assets/prefabs/misc/marketplace/marketterminal.prefab"] = "https://www.rustedit.io/images/stacksextended/marketplace.png",
            ["assets/bundled/prefabs/static/wall.frame.shopfront.metal.static.prefab"] = "https://www.rustedit.io/images/imagelibrary/wall.frame.shopfront.metal.png",
            ["assets/prefabs/deployable/card table/subents/cardtableplayerstorage.prefab"] = "https://www.rustedit.io/images/stacksextended/card-table.png",
            ["assets/prefabs/deployable/card table/subents/cardtablepotstorage.prefab"] = "https://www.rustedit.io/images/stacksextended/card-table.png",
            ["assets/prefabs/misc/casino/bigwheel/bigwheelbettingterminal.prefab"] = "https://www.rustedit.io/images/stacksextended/betting-terminal.png",
            ["assets/prefabs/misc/casino/slotmachine/slotmachinestorage.prefab"] = "https://www.rustedit.io/images/stacksextended/slot-machine.png",
            ["assets/bundled/prefabs/static/bbq.static_hidden.prefab"] = "https://www.rustedit.io/images/imagelibrary/bbq.png",
            ["assets/bundled/prefabs/static/workbench2.static.prefab"] = "https://www.rustedit.io/images/imagelibrary/workbench2.png",
            ["assets/prefabs/voiceaudio/cassetterecorder/cassetterecorder.deployed.prefab"] = "https://www.rustedit.io/images/stacksextended/cassette-recorder.png",
            ["assets/prefabs/gamemodes/objects/reclaim/reclaimterminal.prefab"] = "https://www.rustedit.io/images/stacksextended/reclaim-terminal.png",
            ["assets/prefabs/gamemodes/objects/reclaim/reclaimbackpack.prefab"] = "https://www.rustedit.io/images/stacksextended/reclaim-terminal.png",
            ["assets/content/vehicles/submarine/subents/submarineitemstorage.prefab"] = "https://www.rustedit.io/images/stacksextended/submarine.png",
            ["assets/content/vehicles/submarine/subents/submarinetorpedostorage.prefab"] = "https://www.rustedit.io/images/stacksextended/submarine.png",
            ["assets/content/vehicles/submarine/subents/submarinefuelstorage.prefab"] = "https://www.rustedit.io/images/stacksextended/submarine.png",
            ["assets/content/vehicles/scrap heli carrier/subents/fuel_storage_scrapheli.prefab"] = "https://www.rustedit.io/images/stacksextended/scrap-heli.png",
            ["assets/content/vehicles/minicopter/subents/fuel_storage.prefab"] = "https://www.rustedit.io/images/stacksextended/minicopter.png",
            ["assets/content/structures/excavator/prefabs/excavator_output_pile.prefab"] = "https://www.rustedit.io/images/stacksextended/excavator.png",
            ["assets/content/structures/excavator/prefabs/engine.prefab"] = "https://www.rustedit.io/images/stacksextended/excavator.png",
        };

        private readonly string[] m_StorageIgnoreList = new string[]
        {
            "assets/bundled/prefabs/modding/events/twitch/twitch_dropbox.deployed.prefab",
            "assets/content/vehicles/train/subents/wagon_storage.prefab",
            "assets/content/vehicles/modularcar/subents/modular_car_1mod_trade.prefab"
        };

        private void RegisterImages()
        {
            StackManagerImages.TryLoad();
            m_MagnifyImage = StackManagerImages.MagnifyCrc;
        }

        private string GetImage(string name, ulong skinId = 0UL) => string.Empty;
        #endregion

        #region UI Creation
        private const string STACKS_UI = "stacksextended.ui";
        private const string POPUP_UI = "stacksextended.popup.ui";

        private readonly Hash<ulong, UIUser> m_UIUsers = new Hash<ulong, UIUser>();

        private string[] m_CharacterFilter = new string[] { "~", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z" };
        private ItemCategory[] m_ItemCategoryTypes = new ItemCategory[] {ItemCategory.Weapon, ItemCategory.Construction, ItemCategory.Items, ItemCategory.Resources, ItemCategory.Attire, ItemCategory.Tool, ItemCategory.Medical, ItemCategory.Food, ItemCategory.Ammunition, ItemCategory.Traps, ItemCategory.Misc, ItemCategory.Component, ItemCategory.Electrical, ItemCategory.Fun};

        private UICategory[] m_UICategories;
        private Hash<ItemCategory, List<ItemDefinition>> m_ItemDefinitionsPerCategory;

        private string m_MagnifyImage;
        
        
        public enum UICategory { Item, Storage, PlayerOverrides, VIPStorage }

        public class UIUser
        { 
            public BasePlayer Player;
            
            public UICategory Category = UICategory.Item;
            public ItemCategory ItemCategory = ItemCategory.Weapon;

            public StorageLimit ContainerItemOverride = null;
            
            public string Permission = string.Empty;
            
            public string SearchFilter = string.Empty;
            public string CharacterFilter = "~";
            
            public int Page = 0;

            public UIUser(BasePlayer player)
            {
                Player = player;
            }
            
            public void Reset()
            {
                ContainerItemOverride = null;
                SearchFilter = string.Empty;
                CharacterFilter = "~";
                Page = 0;
                Permission = string.Empty;
                ItemCategory = ItemCategory.Weapon;
            }
        }

        private CommandCallbackHandler m_CallbackHandler;
        
        #region Styles
        private Style m_PanelStyle = new Style
        {
            ImageColor = new Color(1f, 1f, 1f, 0.1647059f),
            Sprite = Sprites.Background_Rounded,
            ImageType = Image.Type.Tiled,
        };

        private Style m_ButtonStyle = new Style
        {
            ImageColor = new Color(0.1647059f, 0.1803922f, 0.1921569f, 1f),
            Sprite = Sprites.Background_Rounded,
            ImageType = Image.Type.Tiled,
            Alignment = TextAnchor.MiddleCenter,
        };
        
        private Style m_DisabledButtonStyle = new Style
        {
            ImageColor = new Color(0.1647059f, 0.1803922f, 0.1921569f, 0.8f),
            Sprite = Sprites.Background_Rounded,
            ImageType = Image.Type.Tiled,
            FontColor = new Color(1f, 1f, 1f, 0.2f),
            Alignment = TextAnchor.MiddleCenter
        };

        private Style m_BackgroundStyle = new Style
        {
            ImageColor = new Color(0.08235294f, 0.08235294f, 0.08235294f, 0.9490196f),
            Sprite = Sprites.Background_Rounded,
            Material = Materials.BackgroundBlur,
            ImageType = Image.Type.Tiled,
        };
        
        private OutlineComponent m_OutlineGreen = new OutlineComponent(new Color(0.7695657f, 1f, 0f, 1f));
        private OutlineComponent m_OutlineRed = new OutlineComponent(new Color(0.8078431f, 0.2588235f, 0.1686275f, 1f));
        private OutlineComponent m_OutlineWhite = new OutlineComponent(new Color(0.1647059f, 0.1803922f, 0.1921569f, 1f));
        #endregion
        
        #region Layout Groups

        private HorizontalLayoutGroup m_CategoryLayout = new HorizontalLayoutGroup()
        {
            Area = new Area(-535f, -15f, 535f, 15f),
            Spacing = new Spacing(5f, 0f),
            Corner = Corner.Centered,
            FixedSize = new Vector2(120, 20),
            FixedCount = new Vector2Int(4, 1),
        };

        private VerticalLayoutGroup m_SearchFilterLayout = new VerticalLayoutGroup()
        {
            Area = new Area(-10f, -257.5f, 10f, 257.5f),
            Spacing = new Spacing(0f, 2f),
            Corner = Corner.Centered,
            FixedSize = new Vector2(16, 16),
            FixedCount = new Vector2Int(1, 27),
        };
        
        private readonly GridLayoutGroup m_PermissionGridLayout = new GridLayoutGroup(5, 15, Axis.Vertical)
        {
            Area = new Area(-522.5f, -257.5f, 522.5f, 257.5f),
            Spacing = new Spacing(5f, 5f),
            Padding = new Padding(5f, 5f, 5f, 5f),
            Corner = Corner.TopLeft,
        };

        private readonly GridLayoutGroup m_ItemGridLayout = new GridLayoutGroup(4, 6, Axis.Horizontal)
        {
            Area = new Area(-522.5f, -257.5f, 522.5f, 257.5f),
            Spacing = new Spacing(5f, 5f),
            Padding = new Padding(5f, 5f, 5f, 5f),
            Corner = Corner.TopLeft,
        };
        
        private readonly HorizontalLayoutGroup m_SubMenuLayout = new HorizontalLayoutGroup()
        {
            Area = new Area(-535f, -12.5f, 535f, 12.5f),
            Spacing = new Spacing(5f, 5f),
            Corner = Corner.Centered,
            FixedSize = new Vector2(71.5f, 20),
            FixedCount = new Vector2Int(14, 1),
        };
        #endregion
        
        private void InitializeUI()
        {
            if (m_CallbackHandler == null)
                m_CallbackHandler = new CommandCallbackHandler(this);

            m_UICategories = (UICategory[]) Enum.GetValues(typeof(UICategory));
            
            m_ItemDefinitionsPerCategory = new Hash<ItemCategory, List<ItemDefinition>>();
            
            foreach (ItemDefinition itemDefinition in ItemManager.itemList)
            {
                if (string.IsNullOrEmpty(itemDefinition.displayName.english) || itemDefinition.hidden)
                    continue;

                if (!m_ItemDefinitionsPerCategory.TryGetValue(itemDefinition.category, out List<ItemDefinition> list))
                    list = m_ItemDefinitionsPerCategory[itemDefinition.category] = new List<ItemDefinition>();
			    
                list.Add(itemDefinition);
            }

            foreach (KeyValuePair<ItemCategory, List<ItemDefinition>> kvp in m_ItemDefinitionsPerCategory)
                kvp.Value.Sort(((a, b) => a.displayName.english.CompareTo(b.displayName.english)));
            
            RegisterImages();
        }

        private void OpenStacksUI(BasePlayer player)
        {
            if (!m_UIUsers.TryGetValue(player.userID, out UIUser uiUser))
                uiUser = m_UIUsers[player.userID] = new UIUser(player);

            BaseContainer root = ImageContainer.Create(STACKS_UI, Layer.Overall, UIAnchor.Center, new Offset(-540f, -310f, 540f, 310f))
                .WithStyle(m_BackgroundStyle)
                .NeedsCursor()
                .NeedsKeyboard()
                .DestroyExisting()
                .WithChildren(parent =>
                {
                    CreateTitleBar(uiUser, parent);
                    CreateSearchFilterBar(uiUser, parent);

                    switch (uiUser.Category)
                    {
                        case UICategory.Item:
                            CreateSubMenuBar(uiUser, parent, m_ItemCategoryTypes, CreateSubmenuCategory);
                            
                            CreateItemGridLayout(uiUser, parent, "", m_StackLimits.Data.Keys, CreateItemEntry);
                            break;

                        case UICategory.Storage:
                            CreateSubMenuBar(uiUser, parent, Array.Empty<string>(), null);

                            if (uiUser.ContainerItemOverride != null)
                                CreateItemGridLayout(uiUser, parent, FormatString("Label.ContainerItemOverrides", uiUser.Player, uiUser.ContainerItemOverride.NiceName), uiUser.ContainerItemOverride.ItemOverrides.Keys, CreateItemEntry);
                            else CreateStorageGridLayout(uiUser, parent, GetString("Label.StorageHint", uiUser.Player), m_StorageLimits.Data.Keys, CreateStorageEntry);
                            break;

                        case UICategory.PlayerOverrides:
                            CreateSubMenuBar(uiUser, parent, Array.Empty<string>(), null);
                            
                            CreateItemGridLayout(uiUser, parent, GetString("Label.PlayerInventoryOverrides", uiUser.Player), m_PlayerLimits.Data.Keys, CreateItemEntry);
                            break;

                        case UICategory.VIPStorage:
                            CreateSubMenuBar(uiUser, parent, Array.Empty<string>(), null);
                            
                            if (string.IsNullOrEmpty(uiUser.Permission))
                                CreatePermissionLayout(uiUser, parent);
                            else
                            {
                                if (uiUser.ContainerItemOverride != null)
                                    CreateItemGridLayout(uiUser, parent, FormatString("Label.ContainerItemOverrides.Permission", uiUser.Player, uiUser.ContainerItemOverride.NiceName, uiUser.Permission), uiUser.ContainerItemOverride.ItemOverrides.Keys, CreateItemEntry);
                                else
                                {
                                    VIPLimits vipLimits = m_VIPLimits.Data[uiUser.Permission];
                                    BaseContainer.Create(parent, UIAnchor.TopStretch, new Offset(5f, -65f, -5f, -40f))
                                        .WithChildren(subMenu =>
                                        {
                                            TextContainer.Create(subMenu, UIAnchor.CenterLeft, new Offset(10f, -10f, 55f, 10f))
                                                .WithText(GetString("Label.VIPPriority", uiUser.Player.UserIDString))
                                                .WithAlignment(TextAnchor.MiddleLeft);

                                            ImageContainer.Create(subMenu, UIAnchor.CenterLeft, new Offset(60f, -10f, 100f, 10f))
                                                .WithStyle(m_ButtonStyle)
                                                .WithChildren(searchInput =>
                                                {
                                                    InputFieldContainer.Create(searchInput, UIAnchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
                                                        .WithText(vipLimits.Priority.ToString())
                                                        .WithAlignment(TextAnchor.MiddleCenter)
                                                        .WithCallback(m_CallbackHandler, arg =>
                                                            {
                                                                int value = arg.GetInt(1);
                                                                if (value == vipLimits.Priority)
                                                                    return;

                                                                vipLimits.Priority = value;
                                                                m_VIPLimits.Save();

                                                                OpenStacksUI(uiUser.Player);
                                                            }, $"{uiUser.Player.UserIDString}.vip.priority");
                                                });
                                        });
                                    
                                    CreateStorageGridLayout(uiUser, parent, FormatString("Label.VIPStorage", player, uiUser.Permission), vipLimits.StorageOverrides.Keys, CreateStorageEntry);
                                }
                            }

                            break;
                    }
                });

            ChaosUI.Show(player, root);
        }

        #region Bars
        private void CreateTitleBar(UIUser uiUser, BaseContainer parent)
        {
            ImageContainer.Create(parent, UIAnchor.TopStretch, new Offset(5f, -35f, -5f, -5f))
                .WithStyle(m_PanelStyle)
                .WithChildren(titlebar =>
                {
                    TextContainer.Create(titlebar, UIAnchor.FullStretch, new Offset(10f, 0f, 0f, 0f))
                        .WithSize(18)
                        .WithText($"{Title} v{Version}")
                        .WithAlignment(TextAnchor.MiddleLeft)
                        .WithOutline(m_OutlineWhite);

                    // Header Buttons
                    BaseContainer.Create(titlebar, UIAnchor.FullStretch, Offset.zero)
                        .WithLayoutGroup(m_CategoryLayout, m_UICategories, 0, (int i, UICategory t, BaseContainer buttons, UIAnchor anchor, Offset offset) =>
                        {
                            BaseContainer button = ImageContainer.Create(buttons, anchor, offset)
                                .WithStyle(m_ButtonStyle)
                                .WithChildren(items =>
                                {
                                    TextContainer.Create(items, UIAnchor.FullStretch, Offset.zero)
                                        .WithText(GetString($"Button.{t}", uiUser.Player.UserIDString))
                                        .WithAlignment(TextAnchor.MiddleCenter);

                                    ButtonContainer.Create(items, UIAnchor.FullStretch, Offset.zero)
                                        .WithColor(Color.Clear)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            uiUser.Reset();
                                            uiUser.Category = t;
                                            OpenStacksUI(uiUser.Player);
                                        }, $"{uiUser.Player.UserIDString}.category.{t}");

                                });

                            if (uiUser.Category == t)
                                button.WithOutline(m_OutlineGreen);
                        });

                    // Exit Button
                    ImageContainer.Create(titlebar, UIAnchor.CenterRight, new Offset(-55f, -10f, -5f, 10f))
                        .WithStyle(m_ButtonStyle)
                        .WithOutline(m_OutlineRed)
                        .WithChildren(exit =>
                        {
                            TextContainer.Create(exit, UIAnchor.FullStretch, Offset.zero)
                                .WithText(GetString("Button.Exit", uiUser.Player.UserIDString))
                                .WithAlignment(TextAnchor.MiddleCenter);

                            ButtonContainer.Create(exit, UIAnchor.FullStretch, Offset.zero)
                                .WithColor(Color.Clear)
                                .WithCallback(m_CallbackHandler, arg =>
                                {
                                    ChaosUI.Destroy(uiUser.Player, STACKS_UI);
                                    ChaosUI.Destroy(uiUser.Player, POPUP_UI);
                                    m_UIUsers.Remove(uiUser.Player.userID);
                                }, $"{uiUser.Player.UserIDString}.exit");
                        });

                });
        }

        private BaseContainer CreateHeaderBar(UIUser uiUser, BaseContainer parent, string label, bool pageUp, bool pageDown)
        {
            return ImageContainer.Create(parent, UIAnchor.TopStretch, new Offset(5f, -95f, -5f, -70f))
                .WithStyle(m_PanelStyle)
			    .WithChildren(header =>
			    {
				    ImageContainer.Create(header, UIAnchor.CenterLeft, new Offset(5f, -10f, 35f, 10f))
                        .WithStyle(pageDown ? m_ButtonStyle : m_DisabledButtonStyle)
					    .WithChildren(backButton =>
					    {
						    TextContainer.Create(backButton, UIAnchor.FullStretch, Offset.zero)
							    .WithText("<<<")
                                .WithStyle(pageDown ? m_ButtonStyle : m_DisabledButtonStyle);

                            if (pageDown)
                            {
                                ButtonContainer.Create(backButton, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg =>
                                    {
                                        uiUser.Page--;
                                        OpenStacksUI(uiUser.Player);
                                    }, $"{uiUser.Player.UserIDString}.back");
                            }
                        });

				    ImageContainer.Create(header, UIAnchor.CenterRight, new Offset(-35f, -10f, -5f, 10f))
                        .WithStyle(pageUp ? m_ButtonStyle : m_DisabledButtonStyle)
					    .WithChildren(nextButton =>
					    {
						    TextContainer.Create(nextButton, UIAnchor.FullStretch, Offset.zero)
							    .WithText(">>>")
                                .WithStyle(pageUp ? m_ButtonStyle : m_DisabledButtonStyle);

                            if (pageUp)
                            {
                                ButtonContainer.Create(nextButton, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg =>
                                    {
                                        uiUser.Page++;
                                        OpenStacksUI(uiUser.Player);
                                    }, $"{uiUser.Player.UserIDString}.next");
                            }
                        });

				    ImageContainer.Create(header, UIAnchor.CenterRight, new Offset(-240f, -10f, -40f, 10f))
					    .WithStyle(m_ButtonStyle)
					    .WithChildren(searchInput =>
					    {
						    InputFieldContainer.Create(searchInput, UIAnchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
                                .WithText(uiUser.SearchFilter)
							    .WithAlignment(TextAnchor.MiddleLeft)
                                .WithCallback(m_CallbackHandler, arg =>
                                {
                                    uiUser.SearchFilter = arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)) : string.Empty;
                                    uiUser.Page = 0;
                                    OpenStacksUI(uiUser.Player);
                                }, $"{uiUser.Player.UserIDString}.searchinput");

					    });

                    if (!string.IsNullOrEmpty(m_MagnifyImage))
                    {
                        RawImageContainer.Create(header, UIAnchor.Center, new Offset(275f, -10f, 295f, 10f))
                            .WithPNG(m_MagnifyImage);
                    }

				    TextContainer.Create(header, UIAnchor.Center, new Offset(-200f, -12.5f, 200f, 12.5f))
					    .WithText(label)
					    .WithAlignment(TextAnchor.MiddleCenter);

			    });
        }

        private void CreateSubMenuBar<T>(UIUser uiUser, BaseContainer parent, IEnumerable<T> collection, Action<UIUser, T, BaseContainer, UIAnchor, Offset> createAction)
        {
            ImageContainer.Create(parent, UIAnchor.TopStretch, new Offset(5f, -65f, -5f, -40f))
                .WithStyle(m_PanelStyle)
                .WithLayoutGroup(m_SubMenuLayout, collection, 0, (int i, T t, BaseContainer subMenu, UIAnchor anchor, Offset offset) => createAction(uiUser, t, subMenu, anchor, offset));
        }

        private void CreateSubmenuCategory(UIUser uiUser, ItemCategory t, BaseContainer parent, UIAnchor anchor, Offset offset)
        {
            BaseContainer baseContainer = ImageContainer.Create(parent, anchor, offset)
                .WithStyle(m_ButtonStyle)
                .WithChildren(commands =>
                {
                    TextContainer.Create(commands, UIAnchor.FullStretch, Offset.zero)
                        .WithSize(12)
                        .WithText(t.ToString())
                        .WithAlignment(TextAnchor.MiddleCenter);

                    ButtonContainer.Create(commands, UIAnchor.FullStretch, Offset.zero)
                        .WithColor(Color.Clear)
                        .WithCallback(m_CallbackHandler, arg =>
                        {
                            uiUser.Reset();
                            uiUser.ItemCategory = t;
                            OpenStacksUI(uiUser.Player);
                        }, $"{uiUser.Player.UserIDString}.itemcategory.{t}");

                });

            if (uiUser.ItemCategory == t)
                baseContainer.WithOutline(m_OutlineGreen);
        }

        private void CreateSearchFilterBar(UIUser uiUser, BaseContainer parent)
        {
            ImageContainer.Create(parent, UIAnchor.LeftStretch, new Offset(5f, 5f, 25f, -100f))
                .WithStyle(m_PanelStyle)
                .WithLayoutGroup(m_SearchFilterLayout, m_CharacterFilter, 0, (int i, string t, BaseContainer filterList, UIAnchor anchor, Offset offset) =>
                {
                    BaseContainer filterButton = ImageContainer.Create(filterList, anchor, offset)
                        .WithStyle(m_ButtonStyle)
                        .WithChildren(characterTemplate =>
                        {
                            TextContainer.Create(characterTemplate, UIAnchor.FullStretch, Offset.zero)
                                .WithSize(12)
                                .WithText(t)
                                .WithAlignment(TextAnchor.MiddleCenter);

                            if (t != uiUser.CharacterFilter)
                            {
                                ButtonContainer.Create(characterTemplate, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg =>
                                    {
                                        uiUser.CharacterFilter = t;
                                        uiUser.Page = 0;
                                        OpenStacksUI(uiUser.Player);
                                    }, $"{uiUser.Player.UserIDString}.filter.{i}");
                            }
                        });

                    if (t == uiUser.CharacterFilter)
                        filterButton.WithOutline(m_OutlineGreen);
                });
        }
        #endregion
        
        #region Grids
        private void CreateItemGridLayout(UIUser uiUser, BaseContainer parent, string label, IEnumerable<string> keys, Action<UIUser, ItemDefinition, BaseContainer, UIAnchor, Offset> createElement)
        {
            List<ItemDefinition> dst = Facepunch.Pool.Get<List<ItemDefinition>>();

            if (uiUser.CharacterFilter != m_CharacterFilter[0] || !string.IsNullOrEmpty(uiUser.SearchFilter))
            {
                FilterList(ItemManager.itemList, dst, uiUser,
                    ((s, pair) => StartsWithValidator(s, pair.displayName.english)),
                    (s, pair) => ContainsValidator(s, pair.displayName.english));
            }
            else dst.AddRange(uiUser.Category != UICategory.Item ? ItemManager.itemList : m_ItemDefinitionsPerCategory[uiUser.ItemCategory]);

            for (int i = dst.Count - 1; i >= 0; i--)
            {
                if (!keys.Contains(dst[i].shortname) || Configuration.Exclude.IsExcluded(dst[i].shortname))
                    dst.RemoveAt(i);
            }
            
            dst.Sort((a, b) => a.displayName.english.CompareTo(b.displayName.english));
            
            BaseContainer header = CreateHeaderBar(uiUser, parent, label, m_ItemGridLayout.HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0);

            if (uiUser.Category >= UICategory.PlayerOverrides || uiUser.ContainerItemOverride != null)
            {
                ImageContainer.Create(header, UIAnchor.CenterLeft, new Offset(40f, -10f, 180f, 10f))
                    .WithStyle(m_ButtonStyle)
                    .WithChildren(addButton =>
                    {
                        TextContainer.Create(addButton, UIAnchor.FullStretch, Offset.zero)
                            .WithText(GetString("Button.AddItemOverride", uiUser.Player))
                            .WithAlignment(TextAnchor.MiddleCenter);

                        ButtonContainer.Create(addButton, UIAnchor.FullStretch, Offset.zero)
                            .WithColor(Color.Clear)
                            .WithCallback(m_CallbackHandler, arg =>
                            {
                                CreateItemOverrideSelector(uiUser, (definition =>
                                {
                                    StackLimit stackLimit = new StackLimit
                                    {
                                        StackMultiplier = 1,
                                        MaxStackSize = definition.stackable
                                    };

                                    if (uiUser.ContainerItemOverride != null)
                                        uiUser.ContainerItemOverride.ItemOverrides[definition.shortname] = stackLimit;
                                    else m_PlayerLimits.Data[definition.shortname] = stackLimit;
                                        
                                    if (uiUser.Category == UICategory.PlayerOverrides)
                                        m_PlayerLimits.Save();
                                    else if (uiUser.Category == UICategory.Storage)
                                        m_StorageLimits.Save();
                                    else if (uiUser.Category == UICategory.VIPStorage)
                                        m_VIPLimits.Save();

                                    OpenStacksUI(uiUser.Player);
                                }));
                            }, $"{uiUser.Player.UserIDString}.addoverride");
                    });
            }

            ImageContainer.Create(parent, UIAnchor.FullStretch, new Offset(30f, 5f, -5f, -100f))
			.WithStyle(m_PanelStyle)
			.WithLayoutGroup(m_ItemGridLayout, dst, uiUser.Page, (int i, ItemDefinition t, BaseContainer layout, UIAnchor anchor, Offset offset) => createElement(uiUser, t, layout, anchor, offset));
            
            Facepunch.Pool.FreeUnmanaged(ref dst);
        }
        
        private void CreateStorageGridLayout(UIUser uiUser, BaseContainer parent, string label, IEnumerable<string> keys, Action<UIUser, string, BaseContainer, UIAnchor, Offset> createElement)
        {
            List<string> dst = Facepunch.Pool.Get<List<string>>();

            if (uiUser.CharacterFilter != m_CharacterFilter[0] || !string.IsNullOrEmpty(uiUser.SearchFilter))
            {
                FilterList(keys, dst, uiUser, 
                    ((s, pair) => StartsWithValidator(s, pair)), 
                    (s, pair) => ContainsValidator(s, pair));
            }
            else dst.AddRange(keys);
            
            dst.Sort((a, b) => m_StorageLimits.Data[a].NiceName.CompareTo(m_StorageLimits.Data[b].NiceName));
            
            BaseContainer header = CreateHeaderBar(uiUser, parent, label, m_ItemGridLayout.HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0);

            if (uiUser.Category >= UICategory.VIPStorage)
            {
                ImageContainer.Create(header, UIAnchor.CenterLeft, new Offset(40f, -10f, 190f, 10f))
                    .WithStyle(m_ButtonStyle)
                    .WithChildren(addButton =>
                    {
                        TextContainer.Create(addButton, UIAnchor.FullStretch, Offset.zero)
                            .WithText(GetString("Button.AddStorageOverride", uiUser.Player))
                            .WithAlignment(TextAnchor.MiddleCenter);

                        ButtonContainer.Create(addButton, UIAnchor.FullStretch, Offset.zero)
                            .WithColor(Color.Clear)
                            .WithCallback(m_CallbackHandler, arg =>
                            {
                                CreateStorageOverrideSelector(uiUser, (prefab =>
                                {
                                    m_VIPLimits.Data[uiUser.Permission].StorageOverrides[prefab] = new StorageLimit 
                                    { 
                                        MaxStackSize = m_DefaultStorageStackSizes[prefab], 
                                        ItemOverrides = new OrderedHash<string, StackLimit>(),
                                        NiceName = PrefabNameToNiceName(prefab) 
                                    };
                                    m_VIPLimits.Save();
                                    OpenStacksUI(uiUser.Player);
                                }));
                            }, $"{uiUser.Player.UserIDString}.addoverride");
                    });
            }

            ImageContainer.Create(parent, UIAnchor.FullStretch, new Offset(30f, 5f, -5f, -100f))
			.WithStyle(m_PanelStyle)
			.WithLayoutGroup(m_ItemGridLayout, dst, uiUser.Page, (int i, string t, BaseContainer layout, UIAnchor anchor, Offset offset) => createElement(uiUser, t, layout, anchor, offset));
            
            Facepunch.Pool.FreeUnmanaged(ref dst);
        }
        
        private void CreatePermissionLayout(UIUser uiUser, BaseContainer parent)
        {
            List<string> dst = Facepunch.Pool.Get<List<string>>();

            if (uiUser.CharacterFilter != m_CharacterFilter[0] || !string.IsNullOrEmpty(uiUser.SearchFilter))
            {
                FilterList(m_VIPLimits.Data.Keys, dst, uiUser, 
                    ((s, pair) => StartsWithValidator(s, pair)), 
                    (s, pair) => ContainsValidator(s, pair));
            }
            else dst.AddRange(m_VIPLimits.Data.Keys);
            
            BaseContainer header = CreateHeaderBar(uiUser, parent, GetString("Label.SelectPermission", uiUser.Player), m_ItemGridLayout.HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0);
            
            ImageContainer.Create(header, UIAnchor.CenterLeft, new Offset(40f, -10f, 180f, 10f))
                .WithStyle(m_ButtonStyle)
                .WithChildren(addButton =>
                {
                    TextContainer.Create(addButton, UIAnchor.FullStretch, Offset.zero)
                        .WithText(GetString("Button.AddCustomPermission", uiUser.Player))
                        .WithAlignment(TextAnchor.MiddleCenter);

                    ButtonContainer.Create(addButton, UIAnchor.FullStretch, Offset.zero)
                        .WithColor(Color.Clear)
                        .WithCallback(m_CallbackHandler, arg =>
                        {
                            CreateCustomPermissionCreator(uiUser);
                        }, $"{uiUser.Player.UserIDString}.addpermission");
                });

            ImageContainer.Create(parent, UIAnchor.FullStretch, new Offset(30f, 5f, -5f, -100f))
                .WithStyle(m_PanelStyle)
                .WithLayoutGroup(m_PermissionGridLayout, dst, uiUser.Page, (int i, string t, BaseContainer permissionLayout, UIAnchor anchor, Offset offset) =>
                {
                    ImageContainer.Create(permissionLayout, anchor, offset)
                        .WithStyle(m_ButtonStyle)
                        .WithChildren(permissionTemplate =>
                        {
                            TextContainer.Create(permissionTemplate, UIAnchor.FullStretch, Offset.zero)
                                .WithSize(12)
                                .WithText(t)
                                .WithAlignment(TextAnchor.MiddleCenter);

                            ButtonContainer.Create(permissionTemplate, UIAnchor.FullStretch, Offset.zero)
                                .WithColor(Color.Clear)
                                .WithCallback(m_CallbackHandler, arg =>
                                {
                                    uiUser.Permission = t;
                                    OpenStacksUI(uiUser.Player);
                                }, $"{uiUser.Player.UserIDString}.permission.{t}");
                        });

                });
            
            Facepunch.Pool.FreeUnmanaged(ref dst);
        }
        #endregion

        #region Filtering
        private void FilterList<T>(IEnumerable<T> src, List<T> dst, UIUser uiUser, Func<string, T, bool> startsWith, Func<string, T, bool> contains)
        {
            bool useCharacterFilter = !string.IsNullOrEmpty(uiUser.CharacterFilter) && uiUser.CharacterFilter != m_CharacterFilter[0];
            bool useSearchFilter = !string.IsNullOrEmpty(uiUser.SearchFilter);
				                
            if (!useCharacterFilter && !useSearchFilter)
                dst.AddRange(src);
            else
            {
                foreach (T t in src)
                {
                    if (useSearchFilter && useCharacterFilter)
                    {
                        if (startsWith(uiUser.CharacterFilter, t) && contains(uiUser.SearchFilter, t))
                            dst.Add(t);

                        continue;
                    }

                    if (useCharacterFilter)
                    {
                        if (startsWith(uiUser.CharacterFilter, t))
                            dst.Add(t);
				                
                        continue;
                    }
						                
                    if (useSearchFilter && contains(uiUser.SearchFilter, t))
                        dst.Add(t);
                }
            }
        }

        private bool StartsWithValidator(string character, string phrase) => phrase.StartsWith(character, StringComparison.OrdinalIgnoreCase);
                
        private bool ContainsValidator(string character, string phrase) => phrase.Contains(character, CompareOptions.OrdinalIgnoreCase);
        #endregion
        
        #region Grid Entries
        private void CreateItemEntry(UIUser uiUser, ItemDefinition t, BaseContainer layout, UIAnchor anchor, Offset offset)
        {
            StackLimit stackLimit = uiUser.Category == UICategory.Item ? m_StackLimits.Data[t.shortname] :
                                    uiUser.Category == UICategory.PlayerOverrides ? m_PlayerLimits.Data[t.shortname] : 
                                    uiUser.ContainerItemOverride != null ? uiUser.ContainerItemOverride.ItemOverrides[t.shortname] : null;
            
            if (stackLimit == null)
                return;
            
            ImageContainer.Create(layout, anchor, offset)
                .WithStyle(m_PanelStyle)
                .WithChildren(item =>
                {
                    ImageContainer.Create(item, UIAnchor.CenterLeft, new Offset(5.5f, -32f, 69.5f, 32f))
                        .WithIcon(t.itemid);

                    TextContainer.Create(item, UIAnchor.TopStretch, new Offset(74f, -20f, 0f, 0f))
                        .WithSize(12)
                        .WithText(t.displayName.english)
                        .WithAlignment(TextAnchor.MiddleLeft);

                    TextContainer.Create(item, UIAnchor.TopStretch, new Offset(74f, -40f, 0f, -20f))
                        .WithSize(12)
                        .WithText(GetString("Label.StackSize", uiUser.Player))
                        .WithAlignment(TextAnchor.MiddleLeft)
                        .WithChildren(stackSize =>
                        {
                            ImageContainer.Create(stackSize, UIAnchor.FullStretch, new Offset(100f, 1f, -5f, -1f))
                                .WithStyle(m_ButtonStyle)
                                .WithChildren(input =>
                                {
                                    InputFieldContainer.Create(input, UIAnchor.FullStretch, Offset.zero)
                                        .WithSize(12)
                                        .WithText(stackLimit.MaxStackSize.ToString())
                                        .WithAlignment(TextAnchor.MiddleCenter)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            int maxStackSize = arg.GetInt(1);
                                            if (maxStackSize == stackLimit.MaxStackSize)
                                                return;
                                            
                                            stackLimit.MaxStackSize = maxStackSize;

                                            switch (uiUser.Category)
                                            {
                                                case UICategory.Item:
                                                    stackLimit.ItemDefinition.stackable = stackLimit.GetStackSize();
                                                    m_StackLimits.Save();
                                                    break;
                                                case UICategory.Storage:
                                                    m_StorageLimits.Save();
                                                    break;
                                                case UICategory.PlayerOverrides:
                                                    m_PlayerLimits.Save();
                                                    break;
                                                case UICategory.VIPStorage:
                                                    m_VIPLimits.Save();
                                                    break;
                                            }
                                           
                                            OpenStacksUI(uiUser.Player);
                                        }, $"{uiUser.Player.UserIDString}.{t.shortname}.stacklimit");

                                });

                        });

                    TextContainer.Create(item, UIAnchor.TopStretch, new Offset(74f, -60f, 0f, -40f))
                        .WithSize(12)
                        .WithText(GetString("Label.StackMultiplier", uiUser.Player))
                        .WithAlignment(TextAnchor.MiddleLeft)
                        .WithChildren(stackMultiplier =>
                        {
                            ImageContainer.Create(stackMultiplier, UIAnchor.FullStretch, new Offset(100f, 1f, -5f, -1f))
                                .WithStyle(m_ButtonStyle)
                                .WithChildren(input =>
                                {
                                    InputFieldContainer.Create(input, UIAnchor.FullStretch, Offset.zero)
                                        .WithSize(12)
                                        .WithText(stackLimit.StackMultiplier.ToString("n2"))
                                        .WithAlignment(TextAnchor.MiddleCenter)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            float stackMultiplier = arg.GetFloat(1, stackLimit.StackMultiplier);
                                            if (stackMultiplier == stackLimit.StackMultiplier)
                                                return;
                                            
                                            stackLimit.StackMultiplier = stackMultiplier;
                                            
                                            switch (uiUser.Category)
                                            {
                                                case UICategory.Item:
                                                    stackLimit.ItemDefinition.stackable = stackLimit.GetStackSize();
                                                    m_StackLimits.Save();
                                                    break;
                                                case UICategory.Storage:
                                                    m_StorageLimits.Save();
                                                    break;
                                                case UICategory.PlayerOverrides:
                                                    m_PlayerLimits.Save();
                                                    break;
                                                case UICategory.VIPStorage:
                                                    m_VIPLimits.Save();
                                                    break;
                                            }
                                            
                                            OpenStacksUI(uiUser.Player);
                                        }, $"{uiUser.Player.UserIDString}.{t.shortname}.stackmultiplier");
                                });

                        });

                    TextContainer.Create(item, UIAnchor.TopStretch, new Offset(74f, -80f, 0, -60f))
                        .WithSize(12)
                        .WithText(FormatString("Label.DefaultStackSize", uiUser.Player, m_DefaultItemStackSizes[t.shortname]))
                        .WithAlignment(TextAnchor.MiddleLeft);

                    if (uiUser.ContainerItemOverride != null || uiUser.Category == UICategory.PlayerOverrides)
                    {
                        ImageContainer.Create(item, UIAnchor.TopLeft, new Offset(5f, -25f, 25f, -5f))
                            .WithStyle(m_ButtonStyle)
                            .WithOutline(m_OutlineRed)
                            .WithChildren(remove =>
                            {
                                TextContainer.Create(remove, UIAnchor.FullStretch, Offset.zero)
                                    .WithSize(18)
                                    .WithText("<b>×</b>")
                                    .WithAlignment(TextAnchor.MiddleCenter)
                                    .WithWrapMode(VerticalWrapMode.Overflow);

                                ButtonContainer.Create(remove, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg =>
                                    {
                                        if (uiUser.Category == UICategory.PlayerOverrides)
                                            m_PlayerLimits.Data.Remove(t.shortname);
                                        else uiUser.ContainerItemOverride.ItemOverrides.Remove(t.shortname);

                                        switch (uiUser.Category)
                                        {
                                            case UICategory.Item:
                                                stackLimit.ItemDefinition.stackable = stackLimit.GetStackSize();
                                                m_StackLimits.Save();
                                                break;
                                            case UICategory.Storage:
                                                m_StorageLimits.Save();
                                                break;
                                            case UICategory.PlayerOverrides:
                                                m_PlayerLimits.Save();
                                                break;
                                            case UICategory.VIPStorage:
                                                m_VIPLimits.Save();
                                                break;
                                        }

                                        OpenStacksUI(uiUser.Player);
                                    }, $"{uiUser.Player.UserIDString}.remove.{t.shortname}");

                            });
                    }
                });
        }

        private void CreateStorageEntry(UIUser uiUser, string t, BaseContainer layout, UIAnchor anchor, Offset offset)
        {
            StorageLimit storageLimit = uiUser.Category == UICategory.VIPStorage ? m_VIPLimits.Data[uiUser.Permission].StorageOverrides[t] : m_StorageLimits.Data[t];

            ImageContainer.Create(layout, anchor, offset)
                .WithStyle(m_PanelStyle)
                .WithChildren(item =>
                {
                    if (m_PrefabNameToItemID.TryGetValue(t, out int itemId))
                        ImageContainer.Create(item, UIAnchor.CenterLeft, new Offset(5.5f, -32f, 69.5f, 32f))
                            .WithIcon(itemId);
                    else if (m_PrefabIconUrls.ContainsKey(t))
                        RawImageContainer.Create(item, UIAnchor.CenterLeft, new Offset(5.5f, -32f, 69.5f, 32f))
                            .WithPNG(GetImage(t));

                    TextContainer.Create(item, UIAnchor.TopStretch, new Offset(74f, -20f, 0f, 0f))
                        .WithSize(12)
                        .WithText(storageLimit.NiceName)
                        .WithAlignment(TextAnchor.MiddleLeft);

                    TextContainer.Create(item, UIAnchor.TopStretch, new Offset(74f, -40f, 0f, -20f))
                        .WithSize(12)
                        .WithText(GetString("Label.MaxStackSize", uiUser.Player))
                        .WithAlignment(TextAnchor.MiddleLeft)
                        .WithChildren(stackSize =>
                        {
                            ImageContainer.Create(stackSize, UIAnchor.FullStretch, new Offset(100f, 1f, -5f, -1f))
                                .WithStyle(m_ButtonStyle)
                                .WithChildren(input =>
                                {
                                    InputFieldContainer.Create(input, UIAnchor.FullStretch, Offset.zero)
                                        .WithSize(12)
                                        .WithText(storageLimit.MaxStackSize.ToString())
                                        .WithAlignment(TextAnchor.MiddleCenter)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            int maxStackSize = arg.GetInt(1);
                                            if (maxStackSize == storageLimit.MaxStackSize)
                                                return;
                                            
                                            storageLimit.MaxStackSize = maxStackSize;

                                            if (uiUser.Category == UICategory.VIPStorage)
                                                m_VIPLimits.Save();
                                            else m_StorageLimits.Save();

                                            OpenStacksUI(uiUser.Player);
                                        }, $"{uiUser.Player.UserIDString}.maxstack.{t}");
                                });
                        });

                    TextContainer.Create(item, UIAnchor.TopStretch, new Offset(74f, -60f, 0f, -40f))
                        .WithSize(12)
                        .WithText(GetString("Label.StackMultiplier", uiUser.Player))
                        .WithAlignment(TextAnchor.MiddleLeft)
                        .WithChildren(stackMultiplier =>
                        {
                            ImageContainer.Create(stackMultiplier, UIAnchor.FullStretch, new Offset(100f, 1f, -5f, -1f))
                                .WithStyle(m_ButtonStyle)
                                .WithChildren(input =>
                                {
                                    InputFieldContainer.Create(input, UIAnchor.FullStretch, Offset.zero)
                                        .WithSize(12)
                                        .WithText(storageLimit.StackMultiplier.ToString("n2"))
                                        .WithAlignment(TextAnchor.MiddleCenter)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            float stackMultiplier = arg.GetFloat(1, storageLimit.StackMultiplier);
                                            if (stackMultiplier == storageLimit.StackMultiplier)
                                                return;
                                            
                                            storageLimit.StackMultiplier = stackMultiplier;

                                            m_StorageLimits.Save();

                                            OpenStacksUI(uiUser.Player);
                                        }, $"{uiUser.Player.UserIDString}.stackmultiplier.{t}");
                                });
                        });

                    ImageContainer.Create(item, UIAnchor.BottomStretch, new Offset(74f, 3f, -5f, 19f))
                        .WithStyle(m_ButtonStyle)
                        .WithChildren(itemOverrides =>
                        {
                            TextContainer.Create(itemOverrides, UIAnchor.FullStretch, Offset.zero)
                                .WithSize(12)
                                .WithText(FormatString("Button.ItemOverrides", uiUser.Player, storageLimit.ItemOverrides.Count))
                                .WithAlignment(TextAnchor.MiddleCenter);

                            ButtonContainer.Create(itemOverrides, UIAnchor.FullStretch, Offset.zero)
                                .WithColor(Color.Clear)
                                .WithCallback(m_CallbackHandler, arg =>
                                {
                                    uiUser.ContainerItemOverride = storageLimit;
                                    OpenStacksUI(uiUser.Player);
                                }, $"{uiUser.Player.UserIDString}.itemoverride.{t}");

                        });

                    if (uiUser.Category == UICategory.VIPStorage)
                    {
                        ImageContainer.Create(item, UIAnchor.TopLeft, new Offset(5f, -25f, 25f, -5f))
                            .WithStyle(m_ButtonStyle)
                            .WithOutline(m_OutlineRed)
                            .WithChildren(remove =>
                            {
                                TextContainer.Create(remove, UIAnchor.FullStretch, Offset.zero)
                                    .WithSize(18)
                                    .WithText("<b>×</b>")
                                    .WithAlignment(TextAnchor.MiddleCenter)
                                    .WithWrapMode(VerticalWrapMode.Overflow);

                                ButtonContainer.Create(remove, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg =>
                                    {
                                        m_VIPLimits.Data[uiUser.Permission].StorageOverrides.Remove(t);
                                        m_VIPLimits.Save();
                                        OpenStacksUI(uiUser.Player);
                                    }, $"{uiUser.Player.UserIDString}.remove.{t}");

                            });
                    }
                });
        }

        #endregion

        #region Override Selectors

        private HorizontalLayoutGroup m_ItemCategoryLayout = new HorizontalLayoutGroup()
        {
            Area = new Area(-635f, -12.5f, 635f, 12.5f),
            Spacing = new Spacing(5f, 5f),
            Padding = new Padding(0f, 0f, 0f, 0f),
            Corner = Corner.Centered,
            FixedSize = new Vector2(85, 20),
            FixedCount = new Vector2Int(14, 0),
        };

        private GridLayoutGroup m_ItemOverrideLayout = new GridLayoutGroup(12, 6, Axis.Horizontal)
        {
            Area = new Area(-635f, -322.5f, 635f, 322.5f),
            Spacing = new Spacing(5f, 5f),
            Padding = new Padding(5f, 5f, 5f, 5f),
            Corner = Corner.TopLeft,
            FixedCount = new Vector2Int(12, 6),
        };

        private GridLayoutGroup m_StorageOverrideLayout = new GridLayoutGroup(12, 6, Axis.Horizontal)
        {
            Area = new Area(-635f, -337.5f, 635f, 337.5f),
            Spacing = new Spacing(5f, 5f),
            Padding = new Padding(5f, 5f, 5f, 5f),
            Corner = Corner.TopLeft,
            FixedCount = new Vector2Int(12, 6),
        };
        
        private void CreateItemOverrideSelector(UIUser uiUser, Action<ItemDefinition> onSelectItem, ItemCategory itemCategory = ItemCategory.Weapon, int page = 0, string search = "")
        {
            List<ItemDefinition> dst = Facepunch.Pool.Get<List<ItemDefinition>>();

            if (!string.IsNullOrEmpty(search))
            {
                List<ItemDefinition> src = Facepunch.Pool.Get<List<ItemDefinition>>();

                src.AddRange(ItemManager.itemList);
			        
                FilterList(src, dst, uiUser, 
                    (s, itemDefinition) => StartsWithValidator(s, itemDefinition.displayName.english), 
                    (s, itemDefinition) => ContainsValidator(s, itemDefinition.displayName.english));
			        
                Facepunch.Pool.FreeUnmanaged(ref src);
            }
            else dst.AddRange(m_ItemDefinitionsPerCategory[itemCategory]);
                
            BaseContainer root = ImageContainer.Create(STACKS_UI, Layer.Overall, UIAnchor.FullStretch, Offset.zero)
                .WithStyle(m_BackgroundStyle)
                .NeedsCursor()
                .NeedsKeyboard()
                .DestroyExisting()
                .WithChildren(addItemOverride =>
                {
                    ImageContainer.Create(addItemOverride, UIAnchor.TopStretch, new Offset(5f, -35f, -5f, -5f))
                        .WithStyle(m_PanelStyle)
                        .WithChildren(header =>
                        {
                            TextContainer.Create(header, UIAnchor.FullStretch, Offset.zero)
                                .WithText(GetString("Label.AddItemOverride", uiUser.Player))
                                .WithAlignment(TextAnchor.MiddleCenter);

                            ImageContainer.Create(header, UIAnchor.CenterLeft, new Offset(40f, -10f, 100f, 10f))
                                .WithStyle(m_ButtonStyle)
                                .WithChildren(backButton =>
                                {
                                    TextContainer.Create(backButton, UIAnchor.FullStretch, Offset.zero)
                                        .WithText(GetString("Button.Cancel", uiUser.Player))
                                        .WithAlignment(TextAnchor.MiddleCenter);

                                    ButtonContainer.Create(backButton, UIAnchor.FullStretch, Offset.zero)
                                        .WithColor(Color.Clear)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            OpenStacksUI(uiUser.Player);
                                        }, $"{uiUser.Player.UserIDString}.cancel");
                                })
                                .WithOutline(m_OutlineRed);
                            
                            ImageContainer.Create(header, UIAnchor.CenterLeft, new Offset(5f, -10f, 35f, 10f))
                                .WithStyle(page > 0 ? m_ButtonStyle : m_DisabledButtonStyle)
                                .WithChildren(backButton =>
                                {
                                    TextContainer.Create(backButton, UIAnchor.FullStretch, Offset.zero)
                                        .WithText("<<<")
                                        .WithAlignment(TextAnchor.MiddleCenter);

                                    if (page > 0)
                                    {
                                        ButtonContainer.Create(backButton, UIAnchor.FullStretch, Offset.zero)
                                            .WithColor(Color.Clear)
                                            .WithCallback(m_CallbackHandler, arg =>
                                            {
                                                CreateItemOverrideSelector(uiUser, onSelectItem, itemCategory, page - 1, search);
                                            }, $"{uiUser.Player.UserIDString}.back");
                                    }
                                });

                            ImageContainer.Create(header, UIAnchor.CenterRight, new Offset(-35f, -10f, -5f, 10f))
                                .WithStyle(m_ItemOverrideLayout.HasNextPage(page, dst.Count) ? m_ButtonStyle : m_DisabledButtonStyle)
                                .WithChildren(nextButton =>
                                {
                                    TextContainer.Create(nextButton, UIAnchor.FullStretch, Offset.zero)
                                        .WithText(">>>")
                                        .WithAlignment(TextAnchor.MiddleCenter);

                                    if (m_ItemOverrideLayout.HasNextPage(page, dst.Count))
                                    {
                                        ButtonContainer.Create(nextButton, UIAnchor.FullStretch, Offset.zero)
                                            .WithColor(Color.Clear)
                                            .WithCallback(m_CallbackHandler, arg =>
                                            {
                                                CreateItemOverrideSelector(uiUser, onSelectItem, itemCategory, page + 1, search);
                                            }, $"{uiUser.Player.UserIDString}.next");
                                    }

                                });
                            
                            ImageContainer.Create(header, UIAnchor.CenterRight, new Offset(-240f, -10f, -40f, 10f))
                                .WithStyle(m_ButtonStyle)
                                .WithChildren(searchInput =>
                                {
                                    InputFieldContainer.Create(searchInput, UIAnchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
                                        .WithText(search)
                                        .WithAlignment(TextAnchor.MiddleLeft)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            CreateItemOverrideSelector(uiUser, onSelectItem, itemCategory, page, arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)) : string.Empty);
                                        }, $"{uiUser.Player.UserIDString}.searchinput");
                                });

                            if (!string.IsNullOrEmpty(m_MagnifyImage))
                            {
                                RawImageContainer.Create(header, UIAnchor.CenterRight, new Offset(-265f, -10f, -245f, 10f))
                                    .WithPNG(m_MagnifyImage);
                            }
                        });

                    ImageContainer.Create(addItemOverride, UIAnchor.TopStretch, new Offset(5f, -65f, -5f, -40f))
                        .WithStyle(m_PanelStyle)
                        .WithLayoutGroup(m_ItemCategoryLayout, m_ItemCategoryTypes, 0, (int i, ItemCategory t, BaseContainer subMenu, UIAnchor anchor, Offset offset) =>
                        {
                            BaseContainer button = ImageContainer.Create(subMenu, anchor, offset)
                                .WithStyle(m_ButtonStyle)
                                .WithChildren(commands =>
                                {
                                    TextContainer.Create(commands, UIAnchor.FullStretch, Offset.zero)
                                        .WithSize(13)
                                        .WithText(t.ToString())
                                        .WithAlignment(TextAnchor.MiddleCenter);

                                    ButtonContainer.Create(commands, UIAnchor.FullStretch, Offset.zero)
                                        .WithColor(Color.Clear)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            CreateItemOverrideSelector(uiUser, onSelectItem, t, page, search);
                                        }, $"{uiUser.Player.UserIDString}.category.{i}");

                                });

                            if (t == itemCategory)
                                button.WithOutline(m_OutlineGreen);
                        });

                    ImageContainer.Create(addItemOverride, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -70f))
                        .WithStyle(m_PanelStyle)
                        .WithLayoutGroup(m_ItemOverrideLayout, dst, page, (int i, ItemDefinition t, BaseContainer layout, UIAnchor anchor, Offset offset) =>
                        {
                            ImageContainer.Create(layout, anchor, offset)
                                .WithStyle(m_PanelStyle)
                                .WithChildren(template =>
                                {
                                    ImageContainer.Create(template, UIAnchor.TopCenter, new Offset(-32f, -69f, 32f, -5f))
                                        .WithIcon(t.itemid);

                                    TextContainer.Create(template, UIAnchor.BottomStretch, new Offset(5f, 5f, -5f, 31f))
                                        .WithSize(10)
                                        .WithText(t.displayName.english)
                                        .WithAlignment(TextAnchor.MiddleCenter);

                                    ButtonContainer.Create(template, UIAnchor.FullStretch, Offset.zero)
                                        .WithColor(Color.Clear)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            onSelectItem(t);
                                            OpenStacksUI(uiUser.Player);
                                        }, $"{uiUser.Player.UserIDString}.itemoverride.{i}");
                                });
                        });
                });
            
            Facepunch.Pool.FreeUnmanaged(ref dst);
            
            ChaosUI.Show(uiUser.Player, root);
        }

        private void CreateStorageOverrideSelector(UIUser uiUser, Action<string> onSelectAction, int page = 0, string search = "")
        {
            List<string> dst = Facepunch.Pool.Get<List<string>>();

            if (!string.IsNullOrEmpty(search))
            {
                List<string> src = Facepunch.Pool.Get<List<string>>();

                src.AddRange(m_StorageLimits.Data.Keys);
			        
                FilterList(src, dst, uiUser, 
                    (s, prefab) => StartsWithValidator(s, m_StorageLimits.Data[prefab].NiceName), 
                    (s, prefab) => ContainsValidator(s, m_StorageLimits.Data[prefab].NiceName));
			        
                Facepunch.Pool.FreeUnmanaged(ref src);
            }
            else dst.AddRange(m_StorageLimits.Data.Keys);
                
            BaseContainer root = ImageContainer.Create(STACKS_UI, Layer.Overall, UIAnchor.FullStretch, Offset.zero)
                .WithStyle(m_BackgroundStyle)
                .NeedsCursor()
                .NeedsKeyboard()
                .DestroyExisting()
                .WithChildren(addStorageOverride =>
                {
                    ImageContainer.Create(addStorageOverride, UIAnchor.TopStretch, new Offset(5f, -35f, -5f, -5f))
                        .WithStyle(m_PanelStyle)
                        .WithChildren(header =>
                        {
                            TextContainer.Create(header, UIAnchor.FullStretch, Offset.zero)
                                .WithText(GetString("Label.AddStorageOverride", uiUser.Player))
                                .WithAlignment(TextAnchor.MiddleCenter);

                            ImageContainer.Create(header, UIAnchor.CenterLeft, new Offset(40f, -10f, 100f, 10f))
                                .WithStyle(m_ButtonStyle)
                                .WithChildren(backButton =>
                                {
                                    TextContainer.Create(backButton, UIAnchor.FullStretch, Offset.zero)
                                        .WithText(GetString("Button.Cancel", uiUser.Player))
                                        .WithAlignment(TextAnchor.MiddleCenter);

                                    ButtonContainer.Create(backButton, UIAnchor.FullStretch, Offset.zero)
                                            .WithColor(Color.Clear)
                                            .WithCallback(m_CallbackHandler, arg =>
                                            {
                                                OpenStacksUI(uiUser.Player);
                                            }, $"{uiUser.Player.UserIDString}.cancel");
                                })
                                .WithOutline(m_OutlineRed);
                            
                            ImageContainer.Create(header, UIAnchor.CenterLeft, new Offset(5f, -10f, 35f, 10f))
                                .WithStyle(page > 0 ? m_ButtonStyle : m_DisabledButtonStyle)
                                .WithChildren(backButton =>
                                {
                                    TextContainer.Create(backButton, UIAnchor.FullStretch, Offset.zero)
                                        .WithText("<<<")
                                        .WithAlignment(TextAnchor.MiddleCenter);

                                    if (page > 0)
                                    {
                                        ButtonContainer.Create(backButton, UIAnchor.FullStretch, Offset.zero)
                                            .WithColor(Color.Clear)
                                            .WithCallback(m_CallbackHandler, arg =>
                                            {
                                                CreateStorageOverrideSelector(uiUser, onSelectAction, page - 1, search);
                                            }, $"{uiUser.Player.UserIDString}.back");
                                    }
                                });

                            ImageContainer.Create(header, UIAnchor.CenterRight, new Offset(-35f, -10f, -5f, 10f))
                                .WithStyle(m_ItemOverrideLayout.HasNextPage(page, dst.Count) ? m_ButtonStyle : m_DisabledButtonStyle)
                                .WithChildren(nextButton =>
                                {
                                    TextContainer.Create(nextButton, UIAnchor.FullStretch, Offset.zero)
                                        .WithText(">>>")
                                        .WithAlignment(TextAnchor.MiddleCenter);

                                    if (m_ItemOverrideLayout.HasNextPage(page, dst.Count))
                                    {
                                        ButtonContainer.Create(nextButton, UIAnchor.FullStretch, Offset.zero)
                                            .WithColor(Color.Clear)
                                            .WithCallback(m_CallbackHandler, arg =>
                                            {
                                                CreateStorageOverrideSelector(uiUser, onSelectAction, page + 1, search);
                                            }, $"{uiUser.Player.UserIDString}.next");
                                    }

                                });
                            
                            ImageContainer.Create(header, UIAnchor.CenterRight, new Offset(-240f, -10f, -40f, 10f))
                                .WithStyle(m_ButtonStyle)
                                .WithChildren(searchInput =>
                                {
                                    InputFieldContainer.Create(searchInput, UIAnchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
                                        .WithText(search)
                                        .WithAlignment(TextAnchor.MiddleLeft)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            CreateStorageOverrideSelector(uiUser, onSelectAction, page, arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)) : string.Empty);
                                        }, $"{uiUser.Player.UserIDString}.searchinput");
                                });

                            if (!string.IsNullOrEmpty(m_MagnifyImage))
                            {
                                RawImageContainer.Create(header, UIAnchor.CenterRight, new Offset(-265f, -10f, -245f, 10f))
                                    .WithPNG(m_MagnifyImage);
                            }
                        });

                    ImageContainer.Create(addStorageOverride, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -40f))
                        .WithStyle(m_PanelStyle)
                        .WithLayoutGroup(m_StorageOverrideLayout, dst, page, (int i, string t, BaseContainer layout, UIAnchor anchor, Offset offset) =>
                        {
                            ImageContainer.Create(layout, anchor, offset)
                                .WithStyle(m_PanelStyle)
                                .WithChildren(template =>
                                {
                                    if (m_PrefabNameToItemID.TryGetValue(t, out int itemId))
                                        ImageContainer.Create(template, UIAnchor.TopCenter, new Offset(-32f, -69f, 32f, -5f))
                                            .WithIcon(itemId);
                                    else if (m_PrefabIconUrls.ContainsKey(t))
                                        RawImageContainer.Create(template, UIAnchor.TopCenter, new Offset(-32f, -69f, 32f, -5f))
                                            .WithPNG(GetImage(t));
                                    
                                    TextContainer.Create(template, UIAnchor.BottomStretch, new Offset(5f, 5f, -5f, 31f))
                                        .WithSize(10)
                                        .WithText(m_StorageLimits.Data[t].NiceName)
                                        .WithAlignment(TextAnchor.MiddleCenter);

                                    ButtonContainer.Create(template, UIAnchor.FullStretch, Offset.zero)
                                        .WithColor(Color.Clear)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            onSelectAction(t);
                                        }, $"{uiUser.Player.UserIDString}.container.{i}");
                                });
                        });
                });
            
            Facepunch.Pool.FreeUnmanaged(ref dst);
            
            ChaosUI.Show(uiUser.Player, root);
        }

        private void CreateCustomPermissionCreator(UIUser uiUser, string inputText = "")
        {
            BaseContainer root = ImageContainer.Create(STACKS_UI, Layer.Overall, UIAnchor.FullStretch, Offset.zero)
                .WithStyle(m_BackgroundStyle)
                .NeedsCursor()
                .NeedsKeyboard()
                .DestroyExisting()
                .WithChildren(createPermissionPopup =>
                {
                    ImageContainer.Create(createPermissionPopup, UIAnchor.Center, new Offset(-175f, 32.5f, 175f, 52.5f))
                        .WithStyle(m_PanelStyle)
                        .WithChildren(header =>
                        {
                            TextContainer.Create(header, UIAnchor.FullStretch, Offset.zero)
                                .WithText(GetString("Label.CreateVIPPermission", uiUser.Player))
                                .WithAlignment(TextAnchor.MiddleCenter);
                        });

                    ImageContainer.Create(createPermissionPopup, UIAnchor.Center, new Offset(-175f, -27.5f, 175f, 27.5f))
                        .WithStyle(m_PanelStyle)
                        .WithChildren(titleBar =>
                        {
                            ImageContainer.Create(titleBar, UIAnchor.BottomLeft, new Offset(5f, 5f, 95f, 25f))
                                .WithStyle(m_ButtonStyle)
                                .WithOutline(m_OutlineGreen)
                                .WithChildren(confirm =>
                                {
                                    TextContainer.Create(confirm, UIAnchor.FullStretch, Offset.zero)
                                        .WithText(GetString("Button.Create", uiUser.Player))
                                        .WithAlignment(TextAnchor.MiddleCenter);

                                    ButtonContainer.Create(confirm, UIAnchor.FullStretch, Offset.zero)
                                        .WithColor(Color.Clear)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            if (string.IsNullOrEmpty(inputText))
                                            {
                                                CreatePopupMessage(uiUser, GetString("Error.EnterPermission", uiUser.Player));
                                                return;
                                            }

                                            if (!inputText.StartsWith("stacksextended."))
                                                inputText = $"stacksextended.{inputText}";
                                            
                                            if (m_VIPLimits.Data.ContainsKey(inputText))
                                            {
                                                CreatePopupMessage(uiUser, GetString("Error.PermissionExists", uiUser.Player));
                                                return;
                                            }

                                            m_VIPLimits.Data[inputText] = new VIPLimits();
                                            m_VIPLimits.Save();

                                            permission.RegisterPermission(inputText, this);
                                            
                                            ChaosUI.Destroy(uiUser.Player, POPUP_UI);
                                            OpenStacksUI(uiUser.Player);
                                        }, $"{uiUser.Player.UserIDString}.createpermission");

                                });

                            ImageContainer.Create(titleBar, UIAnchor.BottomRight, new Offset(-95f, 5f, -5f, 25f))
                                .WithStyle(m_ButtonStyle)
                                .WithOutline(m_OutlineRed)
                                .WithChildren(cancel =>
                                {
                                    TextContainer.Create(cancel, UIAnchor.FullStretch, Offset.zero)
                                        .WithText(GetString("Button.Cancel", uiUser.Player))
                                        .WithAlignment(TextAnchor.MiddleCenter);

                                    ButtonContainer.Create(cancel, UIAnchor.FullStretch, Offset.zero)
                                        .WithColor(Color.Clear)
                                        .WithCallback(m_CallbackHandler, arg => OpenStacksUI(uiUser.Player), $"{uiUser.Player.UserIDString}.cancelpermission");
                                });

                            TextContainer.Create(titleBar, UIAnchor.BottomStretch, new Offset(5f, 30f, -145f, 50f))
                                .WithText("stacksextended.")
                                .WithAlignment(TextAnchor.MiddleLeft);
                            
                            ImageContainer.Create(titleBar, UIAnchor.BottomStretch, new Offset(97f, 30f, -5f, 50f))
                                .WithStyle(m_ButtonStyle)
                                .WithChildren(input =>
                                {
                                    InputFieldContainer.Create(input, UIAnchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
                                        .WithText(inputText)
                                        .WithAlignment(TextAnchor.MiddleLeft)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            string inputStr = arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)).Replace(" ", "") : string.Empty;
                                            
                                            CreateCustomPermissionCreator(uiUser, inputStr);
                                        }, $"{uiUser.Player.UserIDString}.inputpermission");
                                });
                        });

                });


            ChaosUI.Show(uiUser.Player, root);
        }

        #endregion
        
        #region Popup Message

        private Hash<ulong, Timer> m_PopupTimers = new Hash<ulong, Timer>();

        private void CreatePopupMessage(UIUser uiUser, string message)
        {
            BaseContainer baseContainer = ImageContainer.Create(POPUP_UI, Layer.Overall, UIAnchor.Center, new Offset(-540f, -345f, 540f, -315f))
                .WithColor(Color.Clear)
                .WithChildren(popup =>
                {
                    ImageContainer.Create(popup, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -5f))
                        .WithStyle(m_PanelStyle)
                        .WithChildren(titleBar =>
                        {
                            TextContainer.Create(titleBar, UIAnchor.FullStretch, Offset.zero)
                                .WithText(message)
                                .WithAlignment(TextAnchor.MiddleCenter);

                        });
                })
                .DestroyExisting();
			
            ChaosUI.Show(uiUser.Player, baseContainer);

            if (m_PopupTimers.TryGetValue(uiUser.Player.userID, out Timer t))
                t?.Destroy();

            m_PopupTimers[uiUser.Player.userID] = timer.Once(5f, () => ChaosUI.Destroy(uiUser.Player, POPUP_UI));
        }
        #endregion
        #endregion

        #region Chat Commands

        public void CmdStacks(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (!player.HasPermission(ADMIN_PERMISSION))
            {
                LocalizedMessage(player, "Error.NoPermission");
                return;
            }

            OpenStacksUI(player);
        }
        #endregion
        
        #region Console Commands
        
        public void ccmdStackCategory(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
            {
                SendReply(arg, "This command can only be used in rcon");
                return;
            }
            
            if (arg.Args == null || arg.Args.Length != 3)
            {
                SendReply(arg, "Invalid syntax. se.stackcategory <category> <stacklimit> <stackmultiplier>\nex. se.stackcategory Weapon 10 1.0");
                return;
            }

            if (!int.TryParse(arg.GetString(1), out int stackAmount) || !float.TryParse(arg.GetString(2), out float stackMultiplier))
            {
                SendReply(arg, "Invalid stack amount or stack multiplier entered");
                return;
            }

            ItemCategory itemCategory = ItemCategory.Weapon;

            bool foundCategory = false;
            foreach (ItemCategory itemCategoryType in m_ItemCategoryTypes)
            {
                if (itemCategoryType.ToString().Equals(arg.GetString(0), StringComparison.OrdinalIgnoreCase))
                {
                    itemCategory = itemCategoryType;
                    foundCategory = true;
                    break;
                }
            }

            if (!foundCategory)
            {
                SendReply(arg, $"Invalid category entered. Available categories are {string.Join(", ", m_ItemCategoryTypes)}");
                return;
            }

            foreach (ItemDefinition itemDefinition in m_ItemDefinitionsPerCategory[itemCategory])
            {
                if (m_StackLimits.Data.TryGetValue(itemDefinition.shortname, out StackLimit stackLimit))
                {
                    stackLimit.MaxStackSize = stackAmount;
                    stackLimit.StackMultiplier = stackMultiplier;

                    itemDefinition.stackable = stackLimit.GetStackSize();
                }
            }

            m_StackLimits.Save();
            
            SendReply(arg, $"All items in the {itemCategory} category have now been set to a max stack size of {stackAmount} with a stack multiplier of {stackMultiplier}");
        }
        
        
        public void ccmdStackCategoryLimit(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
            {
                SendReply(arg, "This command can only be used in rcon");
                return;
            }
            
            if (arg.Args == null || arg.Args.Length != 2)
            {
                SendReply(arg, "Invalid syntax. se.stackcategorylimit <category> <stacklimit>\nex. se.stackcategory Weapon 10");
                return;
            }

            if (!int.TryParse(arg.GetString(1), out int stackAmount))
            {
                SendReply(arg, "Invalid stack amount entered");
                return;
            }

            ItemCategory itemCategory = ItemCategory.Weapon;

            bool foundCategory = false;
            foreach (ItemCategory itemCategoryType in m_ItemCategoryTypes)
            {
                if (itemCategoryType.ToString().Equals(arg.GetString(0), StringComparison.OrdinalIgnoreCase))
                {
                    itemCategory = itemCategoryType;
                    foundCategory = true;
                    break;
                }
            }

            if (!foundCategory)
            {
                SendReply(arg, $"Invalid category entered. Available categories are {string.Join(", ", m_ItemCategoryTypes)}");
                return;
            }

            foreach (ItemDefinition itemDefinition in m_ItemDefinitionsPerCategory[itemCategory])
            {
                if (m_StackLimits.Data.TryGetValue(itemDefinition.shortname, out StackLimit stackLimit))
                {
                    stackLimit.MaxStackSize = stackAmount;

                    itemDefinition.stackable = stackLimit.GetStackSize();
                }
            }

            m_StackLimits.Save();
            
            SendReply(arg, $"All items in the {itemCategory} category have now been set to a max stack size of {stackAmount}");
        }
        
        
        public void ccmdStackCategoryMultiplier(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
            {
                SendReply(arg, "This command can only be used in rcon");
                return;
            }
            
            if (arg.Args == null || arg.Args.Length != 2)
            {
                SendReply(arg, "Invalid syntax. se.stackcategory <category> <stackmultiplier>\nex. se.stackcategory Weapon 1.0");
                return;
            }

            if (!float.TryParse(arg.GetString(1), out float stackMultiplier))
            {
                SendReply(arg, "Invalid stack multiplier entered");
                return;
            }

            ItemCategory itemCategory = ItemCategory.Weapon;

            bool foundCategory = false;
            foreach (ItemCategory itemCategoryType in m_ItemCategoryTypes)
            {
                if (itemCategoryType.ToString().Equals(arg.GetString(0), StringComparison.OrdinalIgnoreCase))
                {
                    itemCategory = itemCategoryType;
                    foundCategory = true;
                    break;
                }
            }

            if (!foundCategory)
            {
                SendReply(arg, $"Invalid category entered. Available categories are {string.Join(", ", m_ItemCategoryTypes)}");
                return;
            }

            foreach (ItemDefinition itemDefinition in m_ItemDefinitionsPerCategory[itemCategory])
            {
                if (m_StackLimits.Data.TryGetValue(itemDefinition.shortname, out StackLimit stackLimit))
                {
                    stackLimit.StackMultiplier = stackMultiplier;

                    itemDefinition.stackable = stackLimit.GetStackSize();
                }
            }

            m_StackLimits.Save();
            
            SendReply(arg, $"All items in the {itemCategory} category have now been set to a stack multiplier of {stackMultiplier}");
        }

        
        public void ccmdStackItem(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
            {
                SendReply(arg, "This command can only be used in rcon");
                return;
            }
            
            if (arg.Args == null || arg.Args.Length != 3)
            {
                SendReply(arg, "Invalid syntax. se.stackitem <shortname> <stacklimit> <stackmultiplier>\nex. se.stackcategory wood 2000 1.0");
                return;
            }

            if (!int.TryParse(arg.GetString(1), out int stackAmount) || !float.TryParse(arg.GetString(2), out float stackMultiplier))
            {
                SendReply(arg, "Invalid stack amount or stack multiplier entered");
                return;
            }
            
            ItemDefinition itemDefinition = ItemManager.FindItemDefinition(arg.GetString(0).ToLower());
            if (itemDefinition == null)
            {
                SendReply(arg, $"Failed to find a Item Definition with the shortname {arg.GetString(0)}");
                return;
            }

            if (m_StackLimits.Data.TryGetValue(itemDefinition.shortname, out StackLimit stackLimit))
            {
                stackLimit.MaxStackSize = stackAmount;
                stackLimit.StackMultiplier = stackMultiplier;
                m_StackLimits.Save();

                itemDefinition.stackable = stackLimit.GetStackSize();
                
                SendReply(arg, $"You have set the item {itemDefinition.shortname} to a max stack size of {stackAmount} with a stack multiplier of {stackMultiplier}");
            }
            else SendReply(arg, "The chosen item definition is not covered with stack manipulation");
        }
        #endregion
        
        #region Config
        public ConfigData Configuration { get; private set; }
        
        public class ConfigData
        {
            [JsonProperty("Stack Options")]
            public StackOptions Options { get; set; }
            
            [JsonProperty("Player Inventory Options")]
            public PlayerOptions Player { get; set; }
            
            [JsonProperty("Exclude Options")]
            public ExcludeOptions Exclude { get; set; }

            [JsonProperty("Default Stack Sizes")]
            public DefaultStackOptions Defaults { get; set; }
            
            public class StackOptions
            {
                [JsonProperty("Enable stacking of projectile weapons")]
                public bool EnableProjectileWeaponStacks { get; set; }
                
                [JsonProperty("Prevent weapon stacking in player belt container")]
                public bool BeltAntiToolWeaponStack { get; set; }

                [JsonProperty("Prevent stacking weapons that have attachments")]
                public bool BlockModdedWeaponStacks { get; set; }

                [JsonProperty("Prevent stacking attire that has attachments")]
                public bool BlockModdedAttireStacks { get; set; } = true;
                
                [JsonProperty("Prevent stacking projectile weapons with ammunition in the clip")]
                public bool BlockUnequalAmmoWeaponStacks { get; set; }
                
                [JsonProperty("Enable stacking of liquid containers")]
                public bool EnableLiquidContainerStacks { get; set; }
                
                [JsonProperty("Prevent stacking skinned items that have different skins")]
                public bool BlockDifferentSkinStacks { get; set; }
            }

            public class PlayerOptions
            {
                [JsonProperty("The maximum size of any stack in a players inventory (0 is Rust default)")]
                public int InventoryStackLimit { get; set; }
                
                [JsonProperty("Use default stack sizes in the players belt container")]
                public bool UseDefaultBeltStacks { get; set; }
            }

            public class DefaultStackOptions
            {
                [JsonProperty("Minimum stack size for vanilla stack-1 items (0 = do not raise)")]
                public int MinStackForUnstackableItems { get; set; } = 10;

                [JsonProperty("Minimum stack size for honey")]
                public int MinHoneyStack { get; set; } = 100;
            }

            public class ExcludeOptions
            {
                [JsonProperty("Items to be excluded from stack changes")]
                public HashSet<string> ExcludedItems { get; set; } = new HashSet<string>();
                
                [JsonProperty("Skins to be excluded from stack changes")]
                public HashSet<ulong> ExcludedSkins { get; set; } = new HashSet<ulong>();

                [JsonProperty("Containers to be excluded from stack changes (prefab shortname)")]
                public HashSet<string> ExcludedContainers { get; set; } = new HashSet<string>();

                public bool IsExcluded(Item item)
                {
                    if (item?.info == null)
                        return true;
                    if (ExcludedSkins != null && ExcludedSkins.Contains(item.skin))
                        return true;
                    return ExcludedItems != null && ExcludedItems.Contains(item.info.shortname);
                }

                public bool IsExcluded(string shortname) =>
                    !string.IsNullOrEmpty(shortname) && ExcludedItems != null && ExcludedItems.Contains(shortname);

                public bool IsExcluded(ItemContainer container)
                {
                    if (container == null || ExcludedContainers == null)
                        return false;
                    
                    BaseEntity entity = container.entityOwner;
                    if (!entity || entity.IsDestroyed)
                        return false;
                    
                    return ExcludedContainers.Contains(entity.ShortPrefabName);
                }
            }

            public VersionNumber Version { get; set; } = new VersionNumber(2, 0, 24);
        }     
        
        private void LoadConfiguration()
        {
            Configuration = LoadConfigObject<ConfigData>();
            if (Configuration == null || Configuration.Options == null)
                Configuration = GenerateDefaultConfiguration();

            if (Configuration.Player == null)
                Configuration.Player = GenerateDefaultConfiguration().Player;
            if (Configuration.Exclude == null)
                Configuration.Exclude = GenerateDefaultConfiguration().Exclude;
            if (Configuration.Exclude.ExcludedItems == null)
                Configuration.Exclude.ExcludedItems = new HashSet<string>();
            if (Configuration.Exclude.ExcludedSkins == null)
                Configuration.Exclude.ExcludedSkins = new HashSet<ulong>();
            if (Configuration.Exclude.ExcludedContainers == null)
                Configuration.Exclude.ExcludedContainers = new HashSet<string>();
            if (Configuration.Defaults == null)
                Configuration.Defaults = GenerateDefaultConfiguration().Defaults;
            if (Configuration.Version == default)
                Configuration.Version = new VersionNumber(2, 0, 24);

            SaveConfiguration();
        }

        private void SaveConfiguration() => SaveConfigObject(Configuration);

        private ConfigData GenerateDefaultConfiguration()
        {
            return new ConfigData
            {
                Options = new ConfigData.StackOptions
                {
                    BeltAntiToolWeaponStack = true,
                    BlockDifferentSkinStacks = true,
                    BlockModdedWeaponStacks = true,
                    EnableLiquidContainerStacks = false,
                    EnableProjectileWeaponStacks = false,
                    BlockUnequalAmmoWeaponStacks = true
                },
                Player = new ConfigData.PlayerOptions
                {
                    InventoryStackLimit = 0,
                },
                Exclude = new ConfigData.ExcludeOptions
                {
                    ExcludedItems = new HashSet<string>
                    {
                        "water",
                        "water.salt",
                        "blood",
                        "blueprintbase",
                        "coal",
                        "flare",
                        "generator.wind.scrap",
                        "battery.small",
                        "building.planner",
                        "door.key",
                        "map",
                        "note",
                        "hat.candle",
                        "hat.miner"
                    },
                    ExcludedSkins = new HashSet<ulong>()
                },
                Defaults = new ConfigData.DefaultStackOptions
                {
                    MinStackForUnstackableItems = 10,
                    MinHoneyStack = 100
                },
                Version = new VersionNumber(2, 0, 24)
            };
        }

        #region v1.x.x Config Restoration
        public void ccmdLoadOldConfig(ConsoleSystem.Arg arg)
        {
            SendReply(arg, "se.loadoldconfig is not supported under Harmony StackManager (Oxide data migration removed).");
        }
        
        private void TryLoadv1Config(ConsoleSystem.Arg arg)
        {
            SendReply(arg, "se.loadoldconfig is not supported under Harmony StackManager.");
        }
        
        private object GetConfigValue(object configFile, string menu, string datavalue)
        {
            return null;
        }
        #endregion
        #endregion
        
        #region Data
        public class VIPLimits
        {
            public int Priority { get; set; }
            public OrderedHash<string, StorageLimit> StorageOverrides { get; set; } = new OrderedHash<string, StorageLimit>();
        }
        
        public class StorageLimit
        {
            public string NiceName { get; set; }

            public float StackMultiplier { get; set; } = 1f;
            
            public int MaxStackSize { get; set; }

            public OrderedHash<string, StackLimit> ItemOverrides { get; set; } = new OrderedHash<string, StackLimit>();

            public int GetMaxStackable(Item item)
            {
                if (ItemOverrides.TryGetValue(item.info.shortname, out StackLimit stackLimit))
                    return stackLimit.GetStackSize();

                if (!Mathf.Approximately(StackMultiplier, 1f))
                    return Mathf.CeilToInt((float)item.info.stackable * StackMultiplier);
                
                return MaxStackSize;
            }
        }

        public class StackLimit
        {
            public int MaxStackSize { get; set; }
            
            public float StackMultiplier { get; set; }
            
            [JsonIgnore]
            public ItemDefinition ItemDefinition { get; set; }
            
            public StackLimit(){}

            public StackLimit(int maxStackSize)
            {
                MaxStackSize = maxStackSize;
                StackMultiplier = 1f;
            }

            public int GetStackSize() => Mathf.RoundToInt(MaxStackSize * StackMultiplier);
        }

        public class VipLimitsDataFile : Datafile<Hash<string, VIPLimits>>
        {
            public VipLimitsDataFile(string name, params JsonConverter[] converters) : base(name, converters)
            {
            }

            public override void Save()
            {
                KeyValuePair<string, VIPLimits>[] ordered = Data.OrderByDescending(x => x.Value.Priority).ToArray();
                
                Data.Clear();

                for (int i = 0; i < ordered.Length; i++)
                {
                    KeyValuePair<string, VIPLimits> kvp = ordered[i];
                    Data[kvp.Key] = kvp.Value;
                }

                base.Save();
            }
        }
        #endregion 
    }       
}    
        