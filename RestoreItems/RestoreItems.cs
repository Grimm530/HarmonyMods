/*
< ----- End-User License Agreement ----->

This software and all associated files (“Software”) are the intellectual property of the Developer.  
By installing, loading, or using this Software, you agree to the following terms:

1. You may not merge, publish, redistribute, sublicense, or sell this Software or any modified versions of it without the Developer’s explicit written consent.

2. You may copy or modify the Software **only for personal, private use on servers you own or operate**.  
   Distribution of modified or unmodified versions to any third party is strictly prohibited.

3. THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDER AND CONTRIBUTORS "AS IS" AND WITHOUT WARRANTY OF ANY KIND, 
   EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, AND NON-INFRINGEMENT.
   
4. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES 
   (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) 
   HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
   ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

Developer: Grimm530 (r3ap3rsg@gmail.com)

Copyright © Grimm530. All rights reserved.
*/
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using RestoreItemsHarmony;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

#region Changelog
/** v2.1.6 Restoration merge (no pre-clear)
 * - Removed clearing player inventory before death/dungeon/corpse restoration
 * - /getstuff merges stored kit with current gear; overflow uses GiveItem / drops
 *
 * v2.1.5 Large/Krieg Backpack Restoration Fix
 * - FIXED: largebackpack and kriegbackpack not being restored after game update
 * - Added robust backpack detection (IsBackpack flag + ItemModBackpack component)
 * - Capture dropped backpacks when missing from OnPlayerDeath snapshot
 * - Fixed RecreateItem to use existing game-created container for backpacks
 * 
 * v2.1.1 Critical Death Capture Fix
 * - FIXED: OnPlayerDeath was called too late - inventory already cleared
 * - Added OnDied hook to capture inventory BEFORE it gets cleared by game
 * - Added detailed logging matching Dungeon plugin format
 * - Now shows exactly what items are captured and their positions
 * - Proper death capture flow: OnDied -> capture -> OnPlayerDeath -> corpse spawn
 * - Enhanced debugging with container-by-container item logging
 * 
 * v2.1.0 Dungeon-Style Normal Restoration
 * - Updated normal /getstuff command to work like Dungeon API
 * - Items are now captured to persistent storage on death (not just in-memory)
 * - /getstuff restores from persistent data and deletes corpse
 * - Eliminates issues with corpse clipping/disappearing
 * - More reliable restoration system matching Dungeon behavior
 * - Maintains backward compatibility with existing API methods
 * 
 * v2.0.0 Enhanced Tracking & Slot Preservation
 * - Complete rewrite with enhanced item tracking
 * - Slot position preservation for all items
 * - Multiple fallback mechanisms for corpse tracking
 * - Direct inventory capture before death
 * - Enhanced persistent storage with full item metadata
 * - Better handling of terrain clipping and corpse disappearance
 * - Support for all container types (belt, wear, main, backpack)
 * 
 * v1.0.3 Economics & Performance Update
 * - Switched from ServerRewards to Economics system
 * - Added permission caching for better performance
 * - Implemented periodic cooldown cleanup
 * - Optimized StringBuilder usage and string operations
 * - Improved entity disposal handling
 * 
 * v1.0.2 Inventory Fix
 * - Directly move items from players corpse instead of storing and recreating each item.
 * 
 * v1.0.1 SR Fix
 *  - Fix for checking points.
 * 
 * v1.0.0 Initial Release
 * Saves items from a players inventory when they die and restores them when they respawn.
 */
#endregion

namespace Oxide.Plugins
{
    /// <summary>
    /// RestoreItems 2.1.6 ported for Harmony (no Oxide). Logic matches Oxide plugin; only I/O and hosting differ.
    /// </summary>
    public partial class RestoreItems : RustPlugin
    {
        #region Vars
        /// <summary>Bound at runtime from Economics Harmony mod.</summary>
        internal Plugin Economics;
        /// <summary>Bound at runtime from RaidableBases Harmony mod.</summary>
        internal Plugin RaidableBases;

        private Dictionary<ulong, DateTime> _cooldowns = new Dictionary<ulong, DateTime>();
        private Dictionary<ulong, BaseEntity> _lastInvs = new Dictionary<ulong, BaseEntity>();
        private Dictionary<ulong, bool> _permissionCache = new Dictionary<ulong, bool>();
        private const string USE_PERM = "restoreitems.use";
        private HashSet<ulong> _inProgress = new HashSet<ulong>();
        
        // Enhanced persistent storage for comprehensive item tracking
        private const string DATA_FILE = "RestoreItems_playerData";
        private PlayerData _playerData = new PlayerData();
        
        // Track players who are about to die for direct inventory capture
        private Dictionary<ulong, PlayerInventorySnapshot> _pendingDeaths = new Dictionary<ulong, PlayerInventorySnapshot>();
        
        // Track invalid weapon contents that need to be added to player inventory instead
        private Dictionary<ItemId, List<Item>> _invalidWeaponContents = new Dictionary<ItemId, List<Item>>();
        #endregion

        #region Enhanced Data Classes
        private class PlayerData
        {
            public Dictionary<ulong, StoredInventory> StoredInventories = new Dictionary<ulong, StoredInventory>();
            public Dictionary<ulong, StoredInventory> DungeonInventories = new Dictionary<ulong, StoredInventory>();
        }

        private class StoredInventory
        {
            public ulong PlayerId;
            public DateTime StoredTime;
            public string DeathReason;
            public Vector3 DeathPosition;
            public List<StoredContainer> Containers = new List<StoredContainer>();
            public bool Restored = false;
        }

        private class StoredContainer
        {
            public string ContainerType; // "belt", "wear", "main", "backpack"
            public int ContainerIndex;
            public List<StoredItem> Items = new List<StoredItem>();
        }

        private class StoredItem
        {
            public ulong OriginalUid;
            public int ClaimedAmount;
            public int ItemId;
            public int Amount;
            public ulong SkinId;
            public string Name; // Optional custom item name override; normal item names come from ItemDefinition.
            public float Condition;
            public int Position; // Slot position
            public float Fuel;
            public string Text;
            public float CookTimeLeft;
            public float Radioactivity;
            public List<StoredItem> Contents = new List<StoredItem>(); // For items with contents (like backpacks)
            public int ContentsCapacity = 0; // Store the original capacity of the container
            public Dictionary<string, object> CustomData = new Dictionary<string, object>();
        }

        // Snapshot for direct inventory capture
        private class PlayerInventorySnapshot
        {
            public ulong PlayerId;
            public DateTime CaptureTime;
            public List<StoredContainer> Containers = new List<StoredContainer>();
        }
        #endregion

        #region Oxide Hooks
        internal void OnServerInitialized()
        {
            if (!permission.PermissionExists(USE_PERM)) permission.RegisterPermission(USE_PERM, this);
            if (!Economics) PrintWarning("Economics not found. Please install it from uMod and reload this plugin.");
            cmd.AddChatCommand(config.chatS.playerChatCommand, this, nameof(ChatCmdGetItems));
            cmd.AddChatCommand("restored.debug", this, nameof(ChatCmdDebug));

            timer.Every(300f, CleanupExpiredCooldowns);
            
            // Load persistent data
            LoadPlayerData();
        }

        private void LoadPlayerData()
        {
            try
            {
                DebugLog($"Loading persistent data from {DATA_FILE}");
                _playerData = Interface.Oxide.DataFileSystem.ReadObject<PlayerData>(DATA_FILE) ?? new PlayerData();
                if (_playerData.StoredInventories == null) _playerData.StoredInventories = new Dictionary<ulong, StoredInventory>();
                
                DebugLog($"Loaded {_playerData.StoredInventories.Count} stored inventories from persistent data");
                
                // Clean up expired entries (older than 1 hour) - use centralized cleanup method
                CleanupExpiredPersistentData();
                
                DebugLog($"Final persistent data count: {_playerData.StoredInventories.Count}");
            }
            catch (Exception e)
            {
                PrintError($"Error loading player data: {e.Message}");
                _playerData = new PlayerData();
            }
        }

        private void SavePlayerData()
        {
            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(DATA_FILE, _playerData);
            }
            catch (Exception e)
            {
                PrintError($"Error saving player data: {e.Message}");
            }
        }

        // Enhanced death handling with direct inventory capture
        internal void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (player == null || !player.userID.IsSteamId() || info == null)
                return;
            
            // Skip NPCs - they should not have their items restored
            // Early return with minimal overhead - check cheapest conditions first
            if (player.userID < 10000000 || player.IsNpc || player is NPCPlayer)
            {
                return; // Skip NPCs silently - no debug log needed for normal operation
            }

            // CRITICAL FIX: Skip capture if player died in RaidableBases area
            // RaidableBases handles its own item restoration, so we should not interfere
            if (RaidableBases != null && IsPositionInRaidableBase(player.transform.position))
            {
                DebugLog($"Player {player.displayName} died in RaidableBases area at {player.transform.position} - skipping RestoreItems capture (RaidableBases will handle it)");
                return;
            }

            DebugLog("=== OnPlayerDeath HOOK CALLED ===");
            DebugLog($"Player {player.displayName} died at {player.transform.position}");
            DebugLog($"Death info: {info?.Initiator?.ShortPrefabName ?? "Unknown"} -> {info?.HitBone ?? 0}");
            DebugLog($"Damage type: {info.damageTypes.GetMajorityDamageType()}");

            // Note: Suicide handling removed - all deaths are treated the same for normal restoration

            // CRITICAL: Capture inventory BEFORE corpse creation (OnPlayerDeath is called before OnDied)
            // This ensures we get the inventory while it's still in the player, not after it's moved to corpse
            DebugLog("=== CAPTURING INVENTORY IN OnPlayerDeath ===");
            CapturePlayerInventoryToPersistentStorage(player, info);
            DebugLog("=== INVENTORY CAPTURE COMPLETE ===");
            
            DebugLog($"Player {player.displayName} died normally - captured inventory to persistent storage");
        }

        // Hook into OnDied - inventory already captured in OnPlayerDeath
        // This hook is kept for compatibility but does minimal work (only debug logging)
        internal void OnDied(BasePlayer player, HitInfo info)
        {
            if (player == null || !player.userID.IsSteamId() || info == null)
                return;
            
            // Skip NPCs - they should not have their items restored
            if (IsNPC(player))
                return;

            // Only log if debug is enabled - this hook is redundant but kept for compatibility
            if (config.enableDebug)
            {
                DebugLog("=== PLAYER DIED (OnDied) ===");
                DebugLog($"Player: {player.displayName}");
                DebugLog($"Death info: {info?.Initiator?.ShortPrefabName ?? "Unknown"} -> {info?.HitBone ?? 0}");
                DebugLog($"Damage type: {info.damageTypes.GetMajorityDamageType()}");
                DebugLog($"Position: {player.transform.position}");
                DebugLog("Note: Inventory already captured in OnPlayerDeath hook");
            }
        }

        internal void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            var player = container?.GetEntityOwner() as BasePlayer;
            if (player == null || item == null)
                return;

            MarkStoredItemClaimed(player, item, item.amount);
        }

        internal void OnItemStacked(Item targetItem, Item sourceItem, ItemContainer container, int amount)
        {
            var player = container?.GetEntityOwner() as BasePlayer;
            if (player == null || sourceItem == null || amount <= 0)
                return;

            MarkStoredItemClaimed(player, sourceItem, amount);
        }

        internal void OnItemStacked(Item targetItem, Item sourceItem, ItemContainer container)
        {
            var player = container?.GetEntityOwner() as BasePlayer;
            if (player == null || sourceItem == null)
                return;

            MarkStoredItemClaimed(player, sourceItem, sourceItem.amount);
        }

        // Direct inventory capture method for persistent storage (like Dungeon API)
        private void CapturePlayerInventoryToPersistentStorage(BasePlayer player, HitInfo info)
        {
            try
            {
                DebugLog($"=== DEATH INVENTORY CAPTURE for {player.displayName} ===");
                DebugLog($"Player ID: {player.userID}");
                DebugLog($"Position: {player.transform.position}");
                DebugLog($"Death Reason: {info?.Initiator?.ShortPrefabName ?? "Unknown"}");

                // Clear any existing stored inventory to use fresh death data
                if (_playerData.StoredInventories.ContainsKey(player.userID))
                {
                    DebugLog($"Clearing old stored inventory for player {player.userID} to use fresh death data");
                    _playerData.StoredInventories.Remove(player.userID);
                }

                var storedInventory = new StoredInventory
                {
                    PlayerId = player.userID,
                    StoredTime = DateTime.Now,
                    DeathReason = info?.Initiator?.ShortPrefabName ?? "Unknown",
                    DeathPosition = player.transform.position,
                    Containers = new List<StoredContainer>(),
                    Restored = false
                };

                // Capture all containers with detailed logging
                DebugLog("=== CAPTURING INVENTORY CONTAINERS ===");
                
                // Main container
                var mainContainer = player.inventory.containerMain;
                if (mainContainer != null)
                {
                    DebugLog($"Main Container ({mainContainer.itemList.Count} items):");
                    CaptureContainerWithLogging(mainContainer, "main", 0, storedInventory.Containers);
                }

                // Wear container
                var wearContainer = player.inventory.containerWear;
                if (wearContainer != null)
                {
                    DebugLog($"Wear Container ({wearContainer.itemList.Count} items):");
                    CaptureContainerWithLogging(wearContainer, "wear", 1, storedInventory.Containers);
                }

                // Belt container
                var beltContainer = player.inventory.containerBelt;
                if (beltContainer != null)
                {
                    DebugLog($"Belt Container ({beltContainer.itemList.Count} items):");
                    CaptureContainerWithLogging(beltContainer, "belt", 2, storedInventory.Containers);
                }

                // Check for backpack - capture it if it exists
                // Backpack can be in wear slot 7, or it might be a separate entity
                var backpack = player.inventory.GetBackpackWithInventory();
                if (backpack != null && backpack.contents != null)
                {
                    // Check if backpack item is in wear container (slot 7)
                    bool backpackInWear = false;
                    if (wearContainer != null)
                    {
                        // Check slot 7 specifically (backpack slot)
                        var backpackItem = wearContainer.GetSlot(7);
                        if (backpackItem != null && backpackItem.contents != null && backpackItem.contents == backpack.contents)
                        {
                            backpackInWear = true;
                            DebugLog($"Backpack item found in wear container at position 7 - contents already captured via item Contents");
                        }
                        else
                        {
                            // Also check all items in case backpack is in a different slot
                            foreach (var item in wearContainer.itemList)
                            {
                                if (item != null && item.contents != null && item.contents == backpack.contents)
                                {
                                    backpackInWear = true;
                                    DebugLog($"Backpack item found in wear container at position {item.position} - contents already captured via item Contents");
                                    break;
                                }
                            }
                        }
                    }
                    
                    // Only capture backpack container separately if backpack is NOT in wear container
                    // (If it's in wear, the contents are already captured as part of the backpack item)
                    if (!backpackInWear)
                    {
                        DebugLog($"Backpack Container ({backpack.contents.itemList.Count} items) - capturing separately (not in wear):");
                        CaptureContainerWithLogging(backpack.contents, "backpack", 3, storedInventory.Containers);
                    }
                    else
                    {
                        DebugLog($"Skipping separate backpack container capture - backpack item is in wear container, contents already captured");
                    }
                }
                else
                {
                    DebugLog("No backpack found on player during death capture");
                }

                _playerData.StoredInventories[player.userID] = storedInventory;
                SavePlayerData();
                
                // Final summary
                int totalItems = 0;
                foreach (var container in storedInventory.Containers)
                {
                    totalItems += container.Items.Count;
                }
                DebugLog("=== DEATH CAPTURE SUMMARY ===");
                DebugLog($"Total items captured: {totalItems}");
                DebugLog($"Containers: {storedInventory.Containers.Count}");
                DebugLog("Inventory Save Result: True");
                DebugLog("=== END DEATH CAPTURE ===");
            }
            catch (Exception e)
            {
                PrintError($"Error capturing inventory to persistent storage for {player.displayName}: {e.Message}");
            }
        }

        // Enhanced container capture with detailed logging
        private void CaptureContainerWithLogging(ItemContainer container, string containerType, int containerIndex, List<StoredContainer> containers)
        {
            if (container == null || container.itemList.Count == 0) 
            {
                DebugLog($"  No items in {containerType} container");
                return;
            }

            var storedContainer = new StoredContainer
            {
                ContainerType = containerType,
                ContainerIndex = containerIndex,
                Items = new List<StoredItem>()
            };

            foreach (var item in container.itemList)
            {
                var storedItem = CreateStoredItem(item);
                storedContainer.Items.Add(storedItem);
                
                // Log each item with position and details
                DebugLog($"  [{item.position}] {item.info.shortname} x{item.amount} (Skin: {item.skin})");
                
                // Log item contents if any (weapon mods, backpack contents, etc.)
                if (item.contents != null && item.contents.itemList != null && item.contents.itemList.Count > 0)
                {
                    DebugLog($"    Item has {item.contents.itemList.Count} items in contents");
                    foreach (var contentItem in item.contents.itemList)
                    {
                        if (contentItem != null && contentItem.info != null)
                        {
                            DebugLog($"      Sub-item: {contentItem.info.shortname} at position {contentItem.position}");
                        }
                    }
                }
            }

            containers.Add(storedContainer);
        }

        // Direct inventory capture method (legacy - kept for compatibility)
        private void CapturePlayerInventory(BasePlayer player, HitInfo info)
        {
            try
            {
                var snapshot = new PlayerInventorySnapshot
                {
                    PlayerId = player.userID,
                    CaptureTime = DateTime.Now,
                    Containers = new List<StoredContainer>()
                };

                // Capture all containers
                CaptureContainer(player.inventory.containerBelt, "belt", 0, snapshot.Containers);
                CaptureContainer(player.inventory.containerWear, "wear", 1, snapshot.Containers);
                CaptureContainer(player.inventory.containerMain, "main", 2, snapshot.Containers);

                // Check for backpack
                var backpack = player.inventory.GetBackpackWithInventory();
                if (backpack != null)
                {
                    CaptureContainer(backpack.contents, "backpack", 3, snapshot.Containers);
                }

                _pendingDeaths[player.userID] = snapshot;
                
                // Debug logging
                int totalItems = 0;
                foreach (var container in snapshot.Containers)
                {
                    totalItems += container.Items.Count;
                }
                DebugLog($"Captured inventory for {player.displayName} with {totalItems} items");
                
                foreach (var container in snapshot.Containers)
                {
                    DebugLog($"  {container.ContainerType}: {container.Items.Count} items");
                    int logged = 0;
                    foreach (var item in container.Items) // Log first 3 items
                    {
                        if (logged >= 3) break;
                        DebugLog($"    {item.Name} at position {item.Position}");
                        logged++;
                    }
                }
            }
            catch (Exception e)
            {
                PrintError($"Error capturing inventory for {player.displayName}: {e.Message}");
            }
        }

        private void CaptureContainer(ItemContainer container, string containerType, int containerIndex, List<StoredContainer> containers)
        {
            if (container == null || container.itemList.Count == 0) return;

            var storedContainer = new StoredContainer
            {
                ContainerType = containerType,
                ContainerIndex = containerIndex,
                Items = new List<StoredItem>()
            };

            foreach (var item in container.itemList)
            {
                var storedItem = CreateStoredItem(item);
                storedContainer.Items.Add(storedItem);
            }

            containers.Add(storedContainer);
        }

        private StoredItem CreateStoredItem(Item item)
        {
            var storedItem = new StoredItem
            {
                OriginalUid = item.uid.Value,
                ClaimedAmount = 0,
                ItemId = item.info.itemid,
                Amount = item.amount,
                SkinId = item.skin,
                Name = item.name,
                Condition = item.condition,
                Position = item.position,
                Fuel = item.fuel,
                Text = item.text,
                CookTimeLeft = item.cookTimeLeft,
                Radioactivity = item.radioactivity,
                Contents = new List<StoredItem>(),
                ContentsCapacity = 0,
                CustomData = new Dictionary<string, object>()
            };

            // Store contents if item has them (like backpacks, boxes, weapons with mods, etc.)
            if (item.contents != null)
            {
                // Store the original capacity of the container
                storedItem.ContentsCapacity = item.contents.capacity;
                
                // CRITICAL: Create a snapshot of the item list to avoid issues if it changes during capture
                // Also check itemList is not null before accessing Count
                if (item.contents.itemList != null && item.contents.itemList.Count > 0)
                {
                    // Create a snapshot of the item list to avoid issues if it changes during capture
                    var contentItems = new List<Item>(item.contents.itemList);
                    foreach (var contentItem in contentItems)
                    {
                        if (contentItem != null && contentItem.info != null)
                        {
                            storedItem.Contents.Add(CreateStoredItem(contentItem));
                        }
                    }
                }
            }

            return storedItem;
        }

        private void MarkStoredItemClaimed(BasePlayer player, Item item, int amount)
        {
            if (player == null || item == null || item.uid.Value == 0 || amount <= 0)
                return;

            if (!_playerData.StoredInventories.TryGetValue(player.userID, out var storedInventory))
                return;

            var claimed = MarkStoredItemClaimed(storedInventory.Containers, item.uid.Value, amount);
            if (claimed <= 0)
                return;

            DebugLog($"Marked death item UID {item.uid.Value} ({item.info?.shortname ?? "unknown"}) as claimed for {player.displayName}: {claimed}");
            SavePlayerData();
        }

        private int MarkStoredItemClaimed(List<StoredContainer> containers, ulong itemUid, int amount)
        {
            if (containers == null || itemUid == 0 || amount <= 0)
                return 0;

            foreach (var container in containers)
            {
                var claimed = MarkStoredItemClaimed(container.Items, itemUid, amount);
                if (claimed > 0)
                    return claimed;
            }

            return 0;
        }

        private int MarkStoredItemClaimed(List<StoredItem> items, ulong itemUid, int amount)
        {
            if (items == null)
                return 0;

            foreach (var storedItem in items)
            {
                if (storedItem.OriginalUid == itemUid)
                {
                    var remaining = Math.Max(0, storedItem.Amount - storedItem.ClaimedAmount);
                    var claimed = Math.Min(remaining, amount);
                    storedItem.ClaimedAmount += claimed;
                    return claimed;
                }

                var childClaimed = MarkStoredItemClaimed(storedItem.Contents, itemUid, amount);
                if (childClaimed > 0)
                    return childClaimed;
            }

            return 0;
        }

        private int RemoveClaimedItems(StoredInventory storedInventory)
        {
            if (storedInventory?.Containers == null)
                return 0;

            var removed = 0;
            foreach (var container in storedInventory.Containers)
            {
                removed += RemoveClaimedItems(container.Items);
            }

            return removed;
        }

        private int RemoveClaimedItems(List<StoredItem> items)
        {
            if (items == null)
                return 0;

            var removed = 0;
            for (var i = items.Count - 1; i >= 0; i--)
            {
                var storedItem = items[i];
                removed += RemoveClaimedItems(storedItem.Contents);

                if (storedItem.ClaimedAmount <= 0)
                    continue;

                if (storedItem.ClaimedAmount >= storedItem.Amount)
                {
                    items.RemoveAt(i);
                    removed++;
                    continue;
                }

                storedItem.Amount -= storedItem.ClaimedAmount;
                storedItem.ClaimedAmount = 0;
            }

            return removed;
        }

        private bool HasStoredItems(StoredInventory storedInventory)
        {
            if (storedInventory?.Containers == null)
                return false;

            foreach (var container in storedInventory.Containers)
            {
                if (HasStoredItems(container.Items))
                    return true;
            }

            return false;
        }

        private bool HasStoredItems(List<StoredItem> items)
        {
            if (items == null)
                return false;

            foreach (var item in items)
            {
                if (item.Amount > 0 || HasStoredItems(item.Contents))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Robust backpack detection for both largebackpack and kriegbackpack.
        /// Uses ItemDefinition.Flag.Backpack and ItemModBackpack component.
        /// </summary>
        private bool IsBackpackItem(Item item)
        {
            if (item == null || item.info == null) return false;
            return item.IsBackpack() || item.info.GetComponent<ItemModBackpack>() != null;
        }

        /// <summary>
        /// Check if StoredItem represents a backpack (by ItemId).
        /// </summary>
        private bool IsStoredBackpack(StoredItem storedItem)
        {
            if (storedItem == null || storedItem.ItemId == 0) return false;
            var def = ItemManager.FindItemDefinition(storedItem.ItemId);
            if (def == null) return false;
            return (def.flags & ItemDefinition.Flag.Backpack) != 0 || def.GetComponent<ItemModBackpack>() != null;
        }

        /// <summary>
        /// Check if stored inventory has a backpack (in wear slot 7 or backpack container).
        /// </summary>
        private bool StoredInventoryHasBackpack(StoredInventory storedInventory)
        {
            if (storedInventory == null || storedInventory.Containers == null) return false;
            foreach (var cont in storedInventory.Containers)
            {
                if (cont.ContainerType == "wear")
                {
                    foreach (var item in cont.Items)
                    {
                        if (item.Position == 7 && IsStoredBackpack(item))
                            return true;
                    }
                }
                if (cont.ContainerType == "backpack" && cont.Items.Count > 0)
                    return true;
            }
            return false;
        }

        internal void OnEntitySpawned(PlayerCorpse corpse)
        {
            if (corpse == null || corpse.IsDestroyed) return;
            
            // Skip NPC corpses - optimized early return
            // Check cheapest conditions first: type check, then ID check
            if (corpse is NPCPlayerCorpse || corpse.playerSteamID < 10000000)
            {
                return; // Skip NPCs silently - no debug log needed for normal operation
            }
            
            DebugLog($"PlayerCorpse spawned for player {corpse.playerSteamID} at {corpse.transform.position}");
            DebugLog($"Corpse has {corpse.containers.Length} containers");
            
            // Count total items in corpse and log container details
            int totalItems = 0;
            for (int i = 0; i < corpse.containers.Length; i++)
            {
                var container = corpse.containers[i];
                var containerType = GetContainerType(i);
                totalItems += container.itemList.Count;
                DebugLog($"Container {i} ({containerType}): {container.itemList.Count} items, capacity: {container.capacity}");
                
                // Log first few items in each container for debugging
                for (int j = 0; j < Math.Min(3, container.itemList.Count); j++)
                {
                    var item = container.itemList[j];
                    DebugLog($"  Item {j}: {item.info.shortname} at position {item.position}");
                    
                    // Check if this item has contents (like a backpack)
                    if (item.contents != null && item.contents.itemList.Count > 0)
                    {
                        DebugLog($"    Item has {item.contents.itemList.Count} items in contents");
                        for (int k = 0; k < Math.Min(2, item.contents.itemList.Count); k++)
                        {
                            var subItem = item.contents.itemList[k];
                            DebugLog($"      Sub-item {k}: {subItem.info.shortname} at position {subItem.position}");
                        }
                    }
                }
            }
            DebugLog($"Corpse contains {totalItems} items total");
            
            _lastInvs[corpse.playerSteamID] = corpse;
            
            // Only store persistently if we don't already have inventory data from OnPlayerDeath
            if (!_playerData.StoredInventories.ContainsKey(corpse.playerSteamID))
            {
                DebugLog("No inventory data from OnPlayerDeath, storing from corpse");
                StoreEntityPersistently(corpse.playerSteamID, corpse);
            }
            else
            {
                DebugLog("Inventory already captured in OnPlayerDeath, skipping corpse storage");
            }
            
            DebugLog($"Captured corpse for player {corpse.playerSteamID} at {corpse.transform.position}");
        }

        internal void OnRaidableBaseBackpackEject(DroppedItemContainer container)
        {
            // We only use the snapshot from OnPlayerDeath, so we ignore backpack ejects
            // The snapshot already contains all items, including backpack contents
            if (container == null || container.IsDestroyed) return;
            DebugLog($"Ignoring RaidableBases backpack eject for player {container.playerSteamID} - using OnPlayerDeath snapshot only");
        }

        // Ignore dropped backpacks - we only use the snapshot from OnPlayerDeath
        internal void OnEntitySpawned(DroppedItemContainer container)
        {
            // We only use the snapshot from OnPlayerDeath, so we ignore dropped backpacks
            // The snapshot already contains all items, including backpack contents
            if (container == null || container.IsDestroyed) return;
            if (container.playerSteamID == 0 || !container.playerSteamID.IsSteamId()) return;
            DebugLog($"Ignoring DroppedItemContainer for player {container.playerSteamID} - using OnPlayerDeath snapshot only");
        }

        // Ignore dropped backpack items - we only use the snapshot from OnPlayerDeath
        internal void OnEntitySpawned(DroppedItem droppedItem)
        {
            // We only use the snapshot from OnPlayerDeath, so we ignore dropped backpack items
            // The snapshot already contains all items, including backpack contents
            if (droppedItem == null || droppedItem.IsDestroyed) return;
            if (droppedItem.item == null || !IsBackpackItem(droppedItem.item)) return;
            if (droppedItem.DroppedBy == 0 || !droppedItem.DroppedBy.IsSteamId()) return;
            DebugLog($"Ignoring DroppedItem backpack for player {droppedItem.DroppedBy} - using OnPlayerDeath snapshot only");
        }

        // Merge backpack container contents into stored inventory
        private void MergeBackpackIntoStoredInventory(StoredInventory storedInventory, DroppedItemContainer container)
        {
            try
            {
                if (container == null || container.inventory == null || container.inventory.itemList.Count == 0)
                {
                    DebugLog("Backpack container is empty, nothing to merge");
                    return;
                }

                // CRITICAL FIX: Check if backpack items are duplicates of items already in stored inventory
                // This prevents double restoration in RaidableBases zones where items are captured from player
                // and then also appear in the dropped backpack
                if (AreBackpackItemsDuplicates(storedInventory, container.inventory))
                {
                    DebugLog($"Backpack contains duplicate items already in stored inventory - skipping merge to prevent double restoration");
                    return;
                }

                DebugLog($"Merging {container.inventory.itemList.Count} items from dropped backpack into stored inventory");

                // Check if we already have a backpack container
                StoredContainer existingBackpack = null;
                foreach (var cont in storedInventory.Containers)
                {
                    if (cont.ContainerType == "backpack")
                    {
                        existingBackpack = cont;
                        break;
                    }
                }
                
                if (existingBackpack != null)
                {
                    DebugLog("Backpack container already exists in stored inventory, merging items");
                    // Merge items into existing backpack container
                    foreach (var item in container.inventory.itemList)
                    {
                        var storedItem = CreateStoredItem(item);
                        existingBackpack.Items.Add(storedItem);
                        DebugLog($"  Merged item: {item.info.shortname} x{item.amount} at position {item.position}");
                    }
                }
                else
                {
                    DebugLog("Creating new backpack container in stored inventory");
                    // Create new backpack container
                    var backpackContainer = new StoredContainer
                    {
                        ContainerType = "backpack",
                        ContainerIndex = 3,
                        Items = new List<StoredItem>()
                    };

                    foreach (var item in container.inventory.itemList)
                    {
                        var storedItem = CreateStoredItem(item);
                        backpackContainer.Items.Add(storedItem);
                        DebugLog($"  Added item: {item.info.shortname} x{item.amount} at position {item.position}");
                    }

                    storedInventory.Containers.Add(backpackContainer);
                }

                int backpackItemCount = 0;
                foreach (var cont in storedInventory.Containers)
                {
                    if (cont.ContainerType == "backpack")
                    {
                        backpackItemCount = cont.Items.Count;
                        break;
                    }
                }
                DebugLog($"Backpack merge complete. Total items in backpack: {backpackItemCount}");
            }
            catch (Exception e)
            {
                PrintError($"Error merging backpack into stored inventory: {e.Message}");
            }
        }

        private bool AreBackpackItemsDuplicates(StoredInventory storedInventory, ItemContainer backpackInventory)
        {
            if (storedInventory == null || backpackInventory == null || backpackInventory.itemList.Count == 0)
                return false;

            // Count items in backpack by ItemId, SkinId, and amount (more accurate than shortname)
            var backpackItemCounts = new Dictionary<string, int>();
            foreach (var item in backpackInventory.itemList)
            {
                if (item?.info == null) continue;
                string key = $"{item.info.itemid}_{item.skin}";
                if (!backpackItemCounts.ContainsKey(key))
                    backpackItemCounts[key] = 0;
                backpackItemCounts[key] += item.amount;
            }

            // Count items in stored inventory by ItemId, SkinId, and amount
            var storedItemCounts = new Dictionary<string, int>();
            foreach (var container in storedInventory.Containers)
            {
                foreach (var storedItem in container.Items)
                {
                    if (storedItem.ItemId == 0) continue; // Skip invalid items
                    string key = $"{storedItem.ItemId}_{storedItem.SkinId}";
                    if (!storedItemCounts.ContainsKey(key))
                        storedItemCounts[key] = 0;
                    storedItemCounts[key] += storedItem.Amount;
                }
            }

            // Check if all backpack items exist in stored inventory with matching amounts
            // If 80% or more of backpack items match stored items, consider it a duplicate
            int matchingItems = 0;
            int totalBackpackItems = backpackItemCounts.Count;
            
            if (totalBackpackItems == 0)
                return false;
            
            foreach (var kvp in backpackItemCounts)
            {
                if (storedItemCounts.TryGetValue(kvp.Key, out int storedAmount) && storedAmount >= kvp.Value)
                {
                    matchingItems++;
                }
            }

            // If most items match, it's likely a duplicate (RaidableBases scenario)
            // Use 80% threshold to account for minor differences (like arrow count changes)
            bool isDuplicate = (matchingItems * 100 / totalBackpackItems) >= 80;
            
            if (isDuplicate)
            {
                DebugLog($"Detected duplicate backpack: {matchingItems}/{totalBackpackItems} items match stored inventory ({(matchingItems * 100 / totalBackpackItems)}% match)");
            }

            return isDuplicate;
        }

        // Merge backpack item contents into stored inventory
        private void MergeBackpackItemIntoStoredInventory(StoredInventory storedInventory, Item backpackItem)
        {
            try
            {
                if (backpackItem == null || backpackItem.contents == null)
                {
                    DebugLog("Backpack item has no contents container");
                    return;
                }

                // CRITICAL FIX: Check if backpack items are duplicates of items already in stored inventory
                // This prevents double restoration in RaidableBases zones where items are captured from player
                // and then also appear in the dropped backpack
                bool skipContentMerge = backpackItem.contents.itemList.Count > 0 && AreBackpackItemsDuplicates(storedInventory, backpackItem.contents);
                if (skipContentMerge)
                {
                    DebugLog($"Backpack item contains duplicate items already in stored inventory - skipping content merge to prevent double restoration");
                }
                
                if (!skipContentMerge && backpackItem.contents.itemList.Count > 0)
                {
                    DebugLog($"Merging {backpackItem.contents.itemList.Count} items from backpack item into stored inventory");

                    // Check if we already have a backpack container
                    StoredContainer existingBackpack = null;
                    foreach (var cont in storedInventory.Containers)
                    {
                        if (cont.ContainerType == "backpack")
                        {
                            existingBackpack = cont;
                            break;
                        }
                    }
                    
                    if (existingBackpack != null)
                    {
                        DebugLog("Backpack container already exists in stored inventory, merging items");
                        // Merge items into existing backpack container
                        foreach (var item in backpackItem.contents.itemList)
                        {
                            var storedItem = CreateStoredItem(item);
                            existingBackpack.Items.Add(storedItem);
                            DebugLog($"  Merged item: {item.info.shortname} x{item.amount} at position {item.position}");
                        }
                    }
                    else
                    {
                        DebugLog("Creating new backpack container in stored inventory");
                        // Create new backpack container
                        var backpackContainer = new StoredContainer
                        {
                            ContainerType = "backpack",
                            ContainerIndex = 3,
                            Items = new List<StoredItem>()
                        };

                        foreach (var item in backpackItem.contents.itemList)
                        {
                            var storedItem = CreateStoredItem(item);
                            backpackContainer.Items.Add(storedItem);
                            DebugLog($"  Added item: {item.info.shortname} x{item.amount} at position {item.position}");
                        }

                        storedInventory.Containers.Add(backpackContainer);
                    }
                }

                // Additionally: ensure we store the actual backpack wearable item in wear slot 7,
                // so it can be re-equipped with its contents during restoration.
                StoredContainer wearContainer = null;
                foreach (var container in storedInventory.Containers)
                {
                    if (container.ContainerType == "wear")
                    {
                        wearContainer = container;
                        break;
                    }
                }
                if (wearContainer == null)
                {
                    wearContainer = new StoredContainer
                    {
                        ContainerType = "wear",
                        ContainerIndex = 1,
                        Items = new List<StoredItem>()
                    };
                    storedInventory.Containers.Add(wearContainer);
                }

                // Avoid adding a duplicate backpack wearable if one already exists at slot 7
                bool hasBackpackWearItem = false;
                if (wearContainer != null)
                {
                    foreach (var item in wearContainer.Items)
                    {
                        if (item.Position == 7 && item.ItemId == backpackItem.info.itemid)
                        {
                            hasBackpackWearItem = true;
                            break;
                        }
                    }
                }
                if (!hasBackpackWearItem)
                {
                    var wearableBackpack = new StoredItem
                    {
                        ItemId = backpackItem.info.itemid,
                        Amount = 1,
                        SkinId = backpackItem.skin,
                        Name = backpackItem.info?.shortname ?? "backpack",
                        Condition = backpackItem.condition,
                        Position = 7, // backpack/parachute/shield slot
                        Fuel = 0f,
                        Text = null,
                        CookTimeLeft = 0f,
                        Radioactivity = 0f,
                        Contents = new List<StoredItem>(),
                        ContentsCapacity = backpackItem.contents != null ? backpackItem.contents.capacity : 0,
                        CustomData = new Dictionary<string, object>()
                    };

                    // Copy contents into the wearable item (omit when duplicates to prevent double restore)
                    if (!skipContentMerge)
                    {
                        foreach (var item in backpackItem.contents.itemList)
                        {
                            wearableBackpack.Contents.Add(CreateStoredItem(item));
                        }
                    }

                    wearContainer.Items.Add(wearableBackpack);
                    DebugLog(skipContentMerge 
                        ? "Added wearable backpack item to wear container at slot 7 (empty - contents already in stored inventory)" 
                        : "Added wearable backpack item to wear container at slot 7 with contents for re-equipping");
                }

                int backpackItemCount2 = 0;
                foreach (var container in storedInventory.Containers)
                {
                    if (container.ContainerType == "backpack")
                    {
                        backpackItemCount2 = container.Items.Count;
                        break;
                    }
                }
                DebugLog($"Backpack item merge complete. Total items in backpack: {backpackItemCount2}");
            }
            catch (Exception e)
            {
                PrintError($"Error merging backpack item into stored inventory: {e.Message}");
            }
        }

        // Check for and capture dropped backpacks near death position before restoration
        // This ensures we capture the backpack even if it spawned after OnPlayerDeath
        private void CaptureDroppedBackpacksForPlayer(ulong playerId, Vector3 deathPosition, StoredInventory storedInventory)
        {
            try
            {
                const float radius = 15f; // Slightly larger radius to catch backpacks
                bool foundBackpack = false;

                // 1) Check for DroppedItemContainer (vanilla death backpack)
                var containers = UnityEngine.Object.FindObjectsOfType<DroppedItemContainer>();
                foreach (var container in containers)
                {
                    if (container == null || container.IsDestroyed) continue;
                    if (container.playerSteamID != playerId) continue;
                    if (Vector3.Distance(container.transform.position, deathPosition) > radius) continue;
                    if (container.inventory == null || container.inventory.itemList.Count == 0) continue;

                    DebugLog($"Found DroppedItemContainer backpack for player {playerId} at {container.transform.position}");
                    DebugLog($"Backpack contains {container.inventory.itemList.Count} items");
                    MergeBackpackIntoStoredInventory(storedInventory, container);
                    foundBackpack = true;
                }

                // 2) Check for DroppedItem backpacks
                var droppedItems = UnityEngine.Object.FindObjectsOfType<DroppedItem>();
                foreach (var droppedItem in droppedItems)
                {
                    if (droppedItem == null || droppedItem.IsDestroyed) continue;
                    if (droppedItem.DroppedBy != playerId) continue;
                    if (droppedItem.item == null || !IsBackpackItem(droppedItem.item)) continue;
                    if (Vector3.Distance(droppedItem.transform.position, deathPosition) > radius) continue;
                    if (droppedItem.item.contents == null) continue;

                    DebugLog($"Found DroppedItem backpack ({droppedItem.item.info.shortname}) for player {playerId} at {droppedItem.transform.position}" +
                        (droppedItem.item.contents.itemList.Count > 0 ? $" with {droppedItem.item.contents.itemList.Count} items" : " (empty)"));
                    DebugLog($"Backpack contains {droppedItem.item.contents.itemList.Count} items");
                    MergeBackpackItemIntoStoredInventory(storedInventory, droppedItem.item);
                    foundBackpack = true;
                }

                if (foundBackpack)
                {
                    // Save the updated inventory with backpack contents
                    SavePlayerData();
                    DebugLog("Successfully captured dropped backpack(s) and merged into stored inventory");
                }
                else
                {
                    DebugLog("No dropped backpacks found near death position");
                }
            }
            catch (Exception e)
            {
                PrintError($"Error capturing dropped backpacks for player {playerId}: {e.Message}");
            }
        }

        // Removed OnBackpackDrop hook - RestoreItems should only handle PlayerCorpse, not interfere with normal backpack operations

        // Clean up in-memory data when entities are destroyed
        internal void OnEntityKill(BaseNetworkable entity)
        {
            if (entity == null || entity.net == null) return;
            
            // Remove from in-memory tracking if this entity was being tracked
            var keysToRemove = new List<ulong>();
            foreach (var kvp in _lastInvs)
            {
                if (kvp.Value != null && kvp.Value.net != null && kvp.Value.net.ID == entity.net.ID)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
            
            foreach (var key in keysToRemove)
            {
                _lastInvs.Remove(key);
            }
        }

        #endregion

        #region Enhanced Persistent Storage Methods
        private void StoreEntityPersistently(ulong playerId, BaseEntity entity)
        {
            try
            {
                // Clear any existing stored inventory to use fresh corpse data
                if (_playerData.StoredInventories.ContainsKey(playerId))
                {
                    DebugLog($"Clearing old stored inventory for player {playerId} to use fresh corpse data");
                    _playerData.StoredInventories.Remove(playerId);
                }

                var storedInventory = new StoredInventory
                {
                    PlayerId = playerId,
                    StoredTime = DateTime.Now,
                    DeathReason = "Unknown",
                    DeathPosition = entity.transform.position,
                    Containers = new List<StoredContainer>(),
                    Restored = false
                };

                // Extract items from the entity
                if (entity is PlayerCorpse corpse)
                {
                    for (int i = 0; i < corpse.containers.Length; i++)
                    {
                        var container = corpse.containers[i];
                        var containerType = GetContainerType(i);
                        
                        var storedContainer = new StoredContainer
                        {
                            ContainerType = containerType,
                            ContainerIndex = i,
                            Items = new List<StoredItem>()
                        };

                        foreach (var item in container.itemList)
                        {
                            storedContainer.Items.Add(CreateStoredItem(item));
                        }

                        storedInventory.Containers.Add(storedContainer);
                    }
                }
                else if (entity is DroppedItemContainer container)
                {
                    var storedContainer = new StoredContainer
                    {
                        ContainerType = "backpack",
                        ContainerIndex = 0,
                        Items = new List<StoredItem>()
                    };

                    foreach (var item in container.inventory.itemList)
                    {
                        storedContainer.Items.Add(CreateStoredItem(item));
                    }

                    storedInventory.Containers.Add(storedContainer);
                }

                _playerData.StoredInventories[playerId] = storedInventory;
                SavePlayerData();
            }
            catch (Exception e)
            {
                PrintError($"Error storing entity persistently: {e.Message}");
            }
        }

        private string GetContainerType(int containerIndex)
        {
            switch (containerIndex)
            {
                case 0: return "main";    // containerMain (24 slots)
                case 1: return "wear";    // containerWear (8 slots)
                case 2: return "belt";    // containerBelt (6 slots)
                case 3: return "backpack";
                default: return "unknown";
            }
        }

        private bool RestoreFromPersistentData(BasePlayer player)
        {
            if (!_playerData.StoredInventories.TryGetValue(player.userID, out StoredInventory storedInventory))
                return false;

            if (storedInventory.Restored)
            {
                DebugLog($"Inventory already restored for player {player.displayName}");
                // Remove already-restored inventory to prevent reuse
                _playerData.StoredInventories.Remove(player.userID);
                SavePlayerData();
                return false;
            }

            try
            {
                // Capture death position before removing stored inventory
                Vector3 deathPosition = storedInventory.DeathPosition;

                // Capture dropped backpacks when missing from stored data (e.g. backpack dropped before OnPlayerDeath in some configs)
                if (!StoredInventoryHasBackpack(storedInventory))
                {
                    DebugLog("No backpack in stored inventory - attempting to capture from dropped backpacks");
                    CaptureDroppedBackpacksForPlayer(player.userID, deathPosition, storedInventory);
                }

                var claimedItemsRemoved = RemoveClaimedItems(storedInventory);
                if (claimedItemsRemoved > 0)
                {
                    DebugLog($"Removed {claimedItemsRemoved} already reclaimed death item(s) before restoration");
                    SavePlayerData();
                }

                if (!HasStoredItems(storedInventory))
                {
                    DebugLog($"All stored death items for {player.displayName} were already reclaimed from the corpse/backpack");
                    _playerData.StoredInventories.Remove(player.userID);
                    SavePlayerData();
                    return false;
                }
                
                // Log what we're about to restore
                LogRestorationPlan(player, storedInventory);

                // CRITICAL FIX: Mark as restored BEFORE restoring so a partial failure cannot be retried as a full second kit
                // This ensures that even if the process fails partway through, the inventory won't be restored again
                storedInventory.Restored = true;
                _playerData.StoredInventories.Remove(player.userID);
                SavePlayerData();
                DebugLog("Marked inventory as restored and removed from storage to prevent duplication");

                // Do not clear current inventory: merge death kit with whatever the player collected since respawn.
                // Full slots fall back via GiveItem / placement failure handling (overflow drops at feet).

                var sb = new StringBuilder(256);
                int restoredCount = 0;

                // Sort containers by type to restore in proper order
                var sortedContainers = new List<StoredContainer>(storedInventory.Containers);
                sortedContainers.Sort((a, b) => a.ContainerIndex.CompareTo(b.ContainerIndex));

                // Track if we've restored a backpack item with contents to avoid double restoration
                bool backpackItemRestored = false;
                int backpackItemContentsCount = 0;
                
                foreach (var storedContainer in sortedContainers)
                {
                    // Skip backpack container if we already restored a backpack item with contents
                    if (storedContainer.ContainerType == "backpack")
                    {
                        if (backpackItemRestored)
                        {
                            DebugLog($"SKIPPING backpack container restoration - backpack item already restored with {backpackItemContentsCount} contents");
                            DebugLog($"Backpack container has {storedContainer.Items.Count} items that would be duplicates");
                            continue;
                        }
                        
                        // Also check if player already has a backpack with items (from wear container restoration)
                        var backpack = player.inventory.GetBackpackWithInventory();
                        if (backpack != null && backpack.contents != null && backpack.contents.itemList.Count > 0)
                        {
                            DebugLog($"SKIPPING backpack container restoration - player already has backpack with {backpack.contents.itemList.Count} items");
                            DebugLog($"Backpack container has {storedContainer.Items.Count} items that would be duplicates");
                            continue;
                        }
                    }
                    
                    var container = GetPlayerContainer(player, storedContainer.ContainerType);
                    if (container == null) 
                    {
                        DebugLog($"Could not find container for type: {storedContainer.ContainerType}");
                        
                        // If it's a backpack container and player has no backpack, put items in main inventory
                        if (storedContainer.ContainerType == "backpack")
                        {
                            DebugLog($"Fallback: putting backpack items in main inventory for {player.displayName}");
                            container = player.inventory.containerMain;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    DebugLog($"=== RESTORING {storedContainer.ContainerType.ToUpper()} CONTAINER ===");
                    DebugLog($"Container type: {storedContainer.ContainerType}, Items to restore: {storedContainer.Items.Count}");
                    DebugLog($"Target container: {(container != null ? container.ToString() : "NULL")}");

                    int containerRestoredCount = RestoreContainer(player, storedContainer, container, sb, ref backpackItemRestored, ref backpackItemContentsCount);
                    restoredCount += containerRestoredCount;
                    
                    DebugLog($"=== COMPLETED {storedContainer.ContainerType.ToUpper()} CONTAINER RESTORATION ===");
                }

                if (restoredCount > 0)
                {
                    if (sb.Length > 2) sb.Length -= 2;
                    DebugLog("=== DEATH RESTORATION COMPLETE ===");
                    DebugLog($"{player.displayName} ({player.userID}) restored death inventory: {sb}");
                    DebugLog($"Items restored: {restoredCount}");
                    DebugLog("=== END DEATH RESTORATION ===");
                    
                    // Console output for command usage
                    Puts($"{player.displayName} ({player.userID}) restored items: {sb}");
                    
                    // Delete the original corpse since items have been restored from stored data
                    DeletePlayerCorpse(player, deathPosition);
                    
                    // Note: Stored inventory already removed at start of method to prevent duplication
                    return true;
                }
            }
            catch (Exception e)
            {
                PrintError($"Error restoring from persistent data: {e.Message}");
            }

            return false;
        }

        private void LogRestorationPlan(BasePlayer player, StoredInventory storedInventory)
        {
            DebugLog($"=== DEATH INVENTORY RESTORATION for {player.displayName} ===");
            int totalItems = 0;
            foreach (var container in storedInventory.Containers)
            {
                totalItems += container.Items.Count;
            }
            DebugLog($"Total items to restore: {totalItems}");
            DebugLog($"Containers: {storedInventory.Containers.Count}");
            
            var sortedContainers = new List<StoredContainer>(storedInventory.Containers);
            sortedContainers.Sort((a, b) => a.ContainerIndex.CompareTo(b.ContainerIndex));
            foreach (var container in sortedContainers)
            {
                DebugLog($"{container.ContainerType.ToUpper()} Container ({container.Items.Count} items):");
                foreach (var item in container.Items)
                {
                    DebugLog($"  [{item.Position}] {item.Name} x{item.Amount} (Skin: {item.SkinId})");
                }
            }
            DebugLog("=== STARTING RESTORATION ===");
        }

        private int RestoreContainer(BasePlayer player, StoredContainer storedContainer, ItemContainer container, StringBuilder sb, ref bool backpackItemRestored, ref int backpackItemContentsCount)
        {
            int restoredCount = 0;

            foreach (var storedItem in storedContainer.Items)
            {
                var item = RecreateItem(storedItem);
                if (item != null)
                {
                    DebugLog($"  -> Restoring item: {item.info.shortname} x{item.amount}");
                    DebugLog($"     Target position: {storedItem.Position}");
                    DebugLog($"     Has contents: {(storedItem.Contents != null && storedItem.Contents.Count > 0 ? storedItem.Contents.Count + " items" : "No")}");
                    
                    // Check if this is a backpack item being restored to wear container
                    bool isBackpackItem = storedContainer.ContainerType == "wear" &&
                                          item != null &&
                                          IsBackpackItem(item);
                    
                    // Try to place item in original position
                    if (TryPlaceItemInSlot(container, item, storedItem.Position))
                    {
                        sb.Append($"{item.info.shortname} ({item.amount}), ");
                        restoredCount++;
                        DebugLog($"  -> SUCCESS: Restored {item.info.shortname} to position {storedItem.Position}");
                        
                        // If this item has contents, log them
                        if (item.contents != null && item.contents.itemList.Count > 0)
                        {
                            DebugLog($"  -> Item has {item.contents.itemList.Count} items in contents after restoration");
                        }

                        HandleInvalidWeaponMods(player, item, sb, ref restoredCount);
                        
                        // Mark backpack as restored ONLY after successful placement
                        if (isBackpackItem)
                        {
                            DebugLog($"  -> Backpack item successfully placed with {storedItem.Contents.Count} contents - marking as restored");
                            backpackItemRestored = true;
                            backpackItemContentsCount = storedItem.Contents.Count;
                            
                            // Verify the backpack was actually placed and has contents
                            if (item.contents != null && item.contents.itemList.Count > 0)
                            {
                                DebugLog($"  -> VERIFIED: Backpack item placed with {item.contents.itemList.Count} items in contents");
                            }
                            else
                            {
                                DebugLog($"  -> WARNING: Backpack item placed but contents may not be fully restored yet");
                            }
                        }
                    }
                    else
                    {
                        // Fallback to normal give item
                        player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
                        sb.Append($"{item.info.shortname} ({item.amount}), ");
                        restoredCount++;
                        DebugLog($"  -> FALLBACK: Gave {item.info.shortname} to player (slot placement failed)");
                        
                        HandleInvalidWeaponMods(player, item, sb, ref restoredCount);
                        
                        // Only mark backpack as restored if GiveItem actually gave it to the player
                        // (GiveItem for wear items should place them correctly)
                        if (isBackpackItem)
                        {
                            // Check if the item is now on the player
                            var checkBackpack = player.inventory.GetBackpackWithInventory();
                            if (checkBackpack != null && checkBackpack.contents != null && checkBackpack.contents.itemList.Count > 0)
                            {
                                DebugLog($"  -> Backpack item given via fallback with {checkBackpack.contents.itemList.Count} items - marking as restored");
                                backpackItemRestored = true;
                                backpackItemContentsCount = storedItem.Contents.Count;
                            }
                            else
                            {
                                DebugLog($"  -> WARNING: Backpack item fallback may not have worked correctly");
                            }
                        }
                    }
                }
                else
                {
                    DebugLog($"  -> FAILED: Could not recreate item ID {storedItem.ItemId}");
                }
            }

            return restoredCount;
        }

        private void HandleInvalidWeaponMods(BasePlayer player, Item item, StringBuilder sb, ref int restoredCount)
        {
            // CRITICAL: Clean up any invalid mods from weapons to prevent InvalidCastException
            // This must be done AFTER the item is placed/given, but BEFORE handling invalid contents
            if (item.info != null && item.info.category == ItemCategory.Weapon)
            {
                CleanupInvalidWeaponMods(item);
            }

            // Note: We do NOT reinitialize weapon attachments - the game handles this automatically
            // when items are added to weapon contents. Reinitializing can cause validation issues.
            
            // Handle invalid weapon contents (items that couldn't be added to mod slots)
            if (_invalidWeaponContents.TryGetValue(item.uid, out List<Item> invalidItems))
            {
                DebugLog($"  -> Found {invalidItems.Count} invalid items from {item.info.shortname} - adding to player inventory");
                foreach (var invalidItem in invalidItems)
                {
                    player.GiveItem(invalidItem, BaseEntity.GiveItemReason.PickedUp);
                    sb.Append($"{invalidItem.info.shortname} ({invalidItem.amount}), ");
                    restoredCount++;
                    DebugLog($"  -> Added invalid weapon content {invalidItem.info.shortname} to player inventory");
                }
                _invalidWeaponContents.Remove(item.uid);
            }
        }

		private void DeletePlayerCorpse(BasePlayer player, Vector3 deathPosition)
		{
			try
			{
				const float radius = 10f;

				// 1) Delete PlayerCorpse near death position
				var corpses = UnityEngine.Object.FindObjectsOfType<PlayerCorpse>();
				foreach (var corpse in corpses)
				{
					if (corpse.playerSteamID != player.userID)
						continue;
					if (Vector3.Distance(corpse.transform.position, deathPosition) > radius)
						continue;
					DebugLog($"Deleting original corpse for {player.displayName} at {corpse.transform.position}");
					corpse.Kill();
				}

				// 2) Delete DroppedItemContainer (vanilla death backpack) near death position
				var containers = UnityEngine.Object.FindObjectsOfType<DroppedItemContainer>();
				foreach (var container in containers)
				{
					if (container.playerSteamID != player.userID)
						continue;
					if (Vector3.Distance(container.transform.position, deathPosition) > radius)
						continue;
					DebugLog($"Deleting dropped death container ({container.ShortPrefabName}) for {player.displayName} at {container.transform.position}");
					container.Kill();
				}

				// 3) Delete DroppedItem backpacks near death position
				var droppedItems = UnityEngine.Object.FindObjectsOfType<DroppedItem>();
				foreach (var droppedItem in droppedItems)
				{
					if (droppedItem == null || droppedItem.IsDestroyed)
						continue;
					
					// Check if this item belongs to the player
					if (droppedItem.DroppedBy != player.userID)
						continue;
					
					// Check if this is a backpack item
					if (droppedItem.item == null || !IsBackpackItem(droppedItem.item))
						continue;
					
					// Check distance from death position
					if (Vector3.Distance(droppedItem.transform.position, deathPosition) > radius)
						continue;
					
					DebugLog($"Deleting dropped backpack item ({droppedItem.item.info.shortname}) for {player.displayName} at {droppedItem.transform.position}");
					droppedItem.Kill();
				}
			}
			catch (Exception e)
			{
				PrintError($"Error deleting corpse / death backpack for {player.displayName}: {e.Message}");
			}
		}

        private ItemContainer GetPlayerContainer(BasePlayer player, string containerType)
        {
            switch (containerType)
            {
                case "belt": return player.inventory.containerBelt;
                case "wear": return player.inventory.containerWear;
                case "main": return player.inventory.containerMain;
                case "backpack": 
                    var backpack = player.inventory.GetBackpackWithInventory();
                    if (backpack == null)
                    {
                        DebugLog($"Player {player.displayName} has no backpack equipped, cannot restore backpack contents");
                        return null;
                    }
                    return backpack.contents;
                default: 
                    DebugLog($"Unknown container type: {containerType}");
                    return null;
            }
        }

        private bool TryPlaceItemInSlot(ItemContainer container, Item item, int position)
        {
            if (container == null || position < 0 || position >= container.capacity) return false;
            
            // First, try to remove the item from its current container if it has one
            if (item.parent != null)
            {
                item.RemoveFromContainer();
            }
            
            // Special handling for slot 7 in wear container (backpack/parachute/shield slot)
            var player = container.entityOwner as BasePlayer;
            if (player != null && container == player.inventory.containerWear && position == 7)
            {
                DebugLog($"Attempting to place {item.info.shortname} in special slot 7 (backpack/parachute/shield slot)");
            }
            
            // Check if the container can accept this item at this position
            var canAccept = container.CanAcceptItem(item, position);
            if (canAccept != ItemContainer.CanAcceptResult.CanAccept)
            {
                DebugLog($"Container cannot accept {item.info.shortname} at position {position}: {canAccept}");
                DebugLog($"Item category: {item.info.category}, Item type: {item.info.itemType}");
                if (player != null && container == player.inventory.containerWear && position == 7)
                {
                    DebugLog("Special slot 7 rejection - item may not be compatible with this slot");
                }
                return false;
            }
            
            // Check if slot is empty first
            if (container.GetSlot(position) == null)
            {
                // Use MoveToContainer with specific position to place item in exact slot
                if (item.MoveToContainer(container, position, allowStack: false, ignoreStackLimit: false, sourcePlayer: null, allowSwap: false))
                {
                    // Verify the item was actually placed in the correct position
                    if (item.position == position)
                    {
                        return true;
                    }
                    else
                    {
                        DebugLog($"Item {item.info.shortname} was placed in position {item.position} instead of requested position {position}");
                        return false;
                    }
                }
            }
            else
            {
                // Slot is occupied, try to swap items
                var existingItem = container.GetSlot(position);
                if (existingItem != null && item.MoveToContainer(container, position, allowStack: false, ignoreStackLimit: false, sourcePlayer: null, allowSwap: true))
                {
                    // Verify the item was actually placed in the correct position
                    if (item.position == position)
                    {
                        return true;
                    }
                    else
                    {
                        DebugLog($"Item {item.info.shortname} swap failed - placed in position {item.position} instead of requested position {position}");
                        return false;
                    }
                }
            }
            
            return false;
        }

        private string GetCustomItemNameToRestore(StoredItem storedItem, Item item)
        {
            if (storedItem == null || item?.info == null || string.IsNullOrEmpty(storedItem.Name))
                return null;

            // Older snapshots stored the item shortname here, which makes Rust treat the item
            // as custom-named and prevents it from stacking with normal items.
            if (string.Equals(storedItem.Name, item.info.shortname, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(storedItem.Name, item.info.displayName?.english, StringComparison.OrdinalIgnoreCase))
                return null;

            return storedItem.Name;
        }

        private Item RecreateItem(StoredItem storedItem)
        {
            try
            {
                var item = ItemManager.CreateByItemID(storedItem.ItemId, storedItem.Amount, storedItem.SkinId);
                if (item == null) return null;

                item.name = GetCustomItemNameToRestore(storedItem, item);
                item.condition = storedItem.Condition;
                item.fuel = storedItem.Fuel;
                item.text = storedItem.Text;
                item.cookTimeLeft = storedItem.CookTimeLeft;
                item.radioactivity = storedItem.Radioactivity;

                // Restore contents if any
                if (storedItem.Contents.Count > 0 || storedItem.ContentsCapacity > 0)
                {
                    // Determine the capacity to use (prefer stored capacity, fallback to content count)
                    int capacity = storedItem.ContentsCapacity > 0 
                        ? storedItem.ContentsCapacity 
                        : Math.Max(storedItem.Contents.Count, 6); // Minimum 6 for backpacks
                    
                    // CRITICAL: Use the proper ItemMod component to create containers with validation
                    // This ensures weapon mod slots can only accept valid mods, preventing InvalidCastException
                    
                    // Check if this item has ItemModContainerArmorSlot (backpacks, armor with slots, etc.)
                    if (item.info != null && item.info.HasComponent<ItemModContainerArmorSlot>())
                    {
                        // Use the proper method for armor/backpack items
                        var armorSlot = item.info.GetComponent<ItemModContainerArmorSlot>();
                        if (armorSlot != null)
                        {
                            armorSlot.CreateAtCapacity(capacity, item);
                            DebugLog($"Restored backpack/armor item {item.info.shortname} with capacity {capacity} using ItemModContainerArmorSlot");
                        }
                        else
                        {
                            // Fallback if component exists but can't be retrieved
                            item.contents = new ItemContainer();
                            item.contents.ServerInitialize(item, capacity);
                            item.contents.GiveUID();
                            DebugLog($"Restored backpack/armor item {item.info.shortname} with capacity {capacity} (fallback method)");
                        }
                    }
                    // CRITICAL FIX: For weapons, use ItemModContainer to create container with proper validation
                    else if (item.info != null && item.info.category == ItemCategory.Weapon && item.info.HasComponent<ItemModContainer>())
                    {
                        var itemModContainer = item.info.GetComponent<ItemModContainer>();
                        if (itemModContainer != null)
                        {
                            // Check if container already exists (created by OnItemCreated)
                            if (item.contents == null)
                            {
                                // Use reflection to call protected CreateContents method
                                // This properly initializes weapon mod slots with validation
                                var createContentsMethod = typeof(ItemModContainer).GetMethod("CreateContents", 
                                    BindingFlags.NonPublic | BindingFlags.Instance);
                                if (createContentsMethod != null)
                                {
                                    createContentsMethod.Invoke(itemModContainer, new object[] { item });
                                    DebugLog($"Restored weapon {item.info.shortname} with capacity {capacity} using ItemModContainer.CreateContents (proper validation enabled)");
                                }
                                else
                                {
                                    // Fallback if reflection fails
                                    itemModContainer.OnItemCreated(item);
                                    DebugLog($"Restored weapon {item.info.shortname} with capacity {capacity} using ItemModContainer.OnItemCreated (proper validation enabled)");
                                }
                            }
                            else
                            {
                                DebugLog($"Weapon {item.info.shortname} already has container from OnItemCreated, using existing container");
                            }
                            
                            // Ensure capacity matches stored capacity
                            if (item.contents != null && item.contents.capacity != capacity)
                            {
                                item.contents.capacity = capacity;
                                DebugLog($"Adjusted weapon container capacity from {item.contents.capacity} to {capacity}");
                            }
                        }
                        else
                        {
                            // Fallback if component exists but can't be retrieved
                            item.contents = new ItemContainer();
                            item.contents.ServerInitialize(item, capacity);
                            item.contents.GiveUID();
                            DebugLog($"Restored weapon {item.info.shortname} with capacity {capacity} (fallback method - WARNING: validation may be incomplete)");
                        }
                    }
                    else if (IsBackpackItem(item) && item.contents != null)
                    {
                        // Backpacks (largebackpack, kriegbackpack): game creates contents via ItemModContainer.OnItemCreated
                        // Use existing container - do NOT overwrite, as it has proper ItemModBackpack setup
                        if (item.contents.capacity != capacity)
                        {
                            item.contents.capacity = capacity;
                            item.MarkDirty();
                        }
                        DebugLog($"Restored backpack {item.info.shortname} using existing container (capacity {capacity})");
                    }
                    else
                    {
                        // For non-armor, non-weapon containers (boxes, etc.)
                        item.contents = new ItemContainer();
                        item.contents.ServerInitialize(item, capacity);
                        item.contents.GiveUID();
                        DebugLog($"Restored container item {item.info?.shortname ?? "unknown"} with capacity {capacity}");
                    }

                    // Restore the items into the container
                    // CRITICAL: Validate mods based on parent item type (weapon vs armor)
                    bool isWeapon = item.info != null && item.info.category == ItemCategory.Weapon;
                    bool isArmor = item.info != null && item.info.HasComponent<ItemModContainerArmorSlot>();
                    List<Item> invalidContents = new List<Item>(); // Track items that can't be added to mod slots
                    
                    foreach (var contentItem in storedItem.Contents)
                    {
                        var recreatedContent = RecreateItem(contentItem);
                        if (recreatedContent != null)
                        {
                            // CRITICAL FIX: Validate mods based on parent item type
                            // For weapons: ensure it's a valid weapon mod (not a weapon itself)
                            // For armor: ensure it has ItemModArmorInsert component
                            // For other containers: allow all items
                            bool isValidMod = true;
                            
                            if (isWeapon)
                            {
                                // For weapons, check if it's a valid mod (not a weapon itself)
                                isValidMod = IsValidWeaponMod(recreatedContent, item);
                                if (!isValidMod)
                                {
                                    DebugLog($"  -> WARNING: Item {recreatedContent.info.shortname} is not a valid weapon mod for {item.info.shortname} - will be excluded");
                                    invalidContents.Add(recreatedContent);
                                    continue;
                                }
                            }
                            else if (isArmor)
                            {
                                // For armor, check if it has ItemModArmorInsert component
                                if (recreatedContent.info == null || !recreatedContent.info.HasComponent<ItemModArmorInsert>())
                                {
                                    DebugLog($"  -> WARNING: Item {recreatedContent.info?.shortname ?? "unknown"} is not a valid armor mod (missing ItemModArmorInsert) for {item.info.shortname} - will be excluded");
                                    invalidContents.Add(recreatedContent);
                                    continue;
                                }
                            }
                            
                            // Try to add the item to the container using normal game methods only
                            // CRITICAL: Never bypass validation - let the game's validation handle it
                            try
                            {
                                // Check if container can accept this item
                                var canAccept = item.contents.CanAcceptItem(recreatedContent, contentItem.Position);
                                if (canAccept == ItemContainer.CanAcceptResult.CanAccept)
                                {
                                    // Try to move item to container using normal game method
                                    // Use position -1 to let the game find the correct slot if the original position is invalid
                                    int targetPos = contentItem.Position;
                                    if (!recreatedContent.MoveToContainer(item.contents, targetPos, allowStack: false, ignoreStackLimit: false, sourcePlayer: null, allowSwap: false))
                                    {
                                        // If specific position fails, try letting the game find a valid slot
                                        if (!recreatedContent.MoveToContainer(item.contents, -1, allowStack: false, ignoreStackLimit: false, sourcePlayer: null, allowSwap: false))
                                        {
                                            // If both attempts fail, the item is invalid for this container - exclude it
                                            DebugLog($"  -> WARNING: Could not add {recreatedContent.info.shortname} to {item.info.shortname} - item rejected by game validation");
                                            invalidContents.Add(recreatedContent);
                                            continue;
                                        }
                                        else
                                        {
                                            DebugLog($"  -> Restored content item {recreatedContent.info.shortname} to auto-assigned position in {item.info.shortname}");
                                        }
                                    }
                                    else
                                    {
                                        DebugLog($"  -> Restored content item {recreatedContent.info.shortname} to position {targetPos} in {item.info.shortname}");
                                    }
                                }
                                else
                                {
                                    // Container rejected the item - exclude it
                                    DebugLog($"  -> WARNING: Container cannot accept {recreatedContent.info.shortname} at position {contentItem.Position} (reason: {canAccept}) - excluding");
                                    invalidContents.Add(recreatedContent);
                                }
                            }
                            catch (Exception ex)
                            {
                                // If there's an error adding the item, exclude it to prevent crashes
                                DebugLog($"  -> ERROR: Exception adding {recreatedContent.info?.shortname ?? "unknown"} to {item.info?.shortname ?? "unknown"}: {ex.Message} - excluding");
                                invalidContents.Add(recreatedContent);
                            }
                        }
                    }
                    
                    // Store invalid contents to be added to player inventory separately
                    // These will be handled by the caller when the weapon is restored
                    if (invalidContents.Count > 0)
                    {
                        _invalidWeaponContents[item.uid] = invalidContents;
                        DebugLog($"  -> Stored {invalidContents.Count} invalid items from {item.info.shortname} to be added to player inventory");
                    }
                }

                return item;
            }
            catch (Exception e)
            {
                PrintError($"Error recreating item {storedItem.ItemId}: {e.Message}");
                return null;
            }
        }
        #endregion

        #region Weapon Mod Validation
        /// <summary>
        /// Checks if an item is a valid weapon mod that can be attached to a weapon
        /// CRITICAL: This prevents InvalidCastException by ensuring only valid mods go into weapon mod slots
        /// </summary>
        private bool IsValidWeaponMod(Item modItem, Item weapon)
        {
            if (modItem == null || modItem.info == null || weapon == null || weapon.info == null)
                return false;
            
            string shortname = modItem.info.shortname ?? "";
            
            // CRITICAL: Items with ItemModEntity that are actual weapons (like melee) should NOT be mods
            // This is the main cause of InvalidCastException - melee weapons being added to projectile weapon mod slots
            if (modItem.info.HasComponent<ItemModEntity>())
            {
                var entityMod = modItem.info.GetComponent<ItemModEntity>();
                if (entityMod != null && entityMod.entityPrefab != null)
                {
                    try
                    {
                        var prefab = entityMod.entityPrefab.Get();
                        if (prefab != null)
                        {
                            // If it has BaseMelee or BaseProjectile component, it's a weapon, not a mod
                            // This prevents InvalidCastException when game tries to cast BaseMelee to ProjectileWeaponMod
                            if (prefab.GetComponent<BaseMelee>() != null || 
                                prefab.GetComponent<BaseProjectile>() != null)
                            {
                                DebugLog($"IsValidWeaponMod: {modItem.info.shortname} is a weapon (BaseMelee/BaseProjectile), not a mod - REJECTING");
                                return false; // This is a weapon, not a mod
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // If we can't check the prefab, err on the side of caution and reject it
                        DebugLog($"IsValidWeaponMod: Error checking prefab for {modItem.info.shortname}: {e.Message} - REJECTING");
                        return false;
                    }
                }
            }
            
            // CRITICAL: Check if weapon is a projectile weapon - if so, mods MUST be ProjectileWeaponMod compatible
            // This prevents InvalidCastException when ProjectileWeaponMod.Multiply tries to cast items
            // Based on assembly code: parentEnt.children.Cast<ProjectileWeaponMod>() at line 190
            if (weapon.info.HasComponent<ItemModEntity>())
            {
                var weaponEntityMod = weapon.info.GetComponent<ItemModEntity>();
                if (weaponEntityMod != null && weaponEntityMod.entityPrefab != null)
                {
                    try
                    {
                        var weaponPrefab = weaponEntityMod.entityPrefab.Get();
                        if (weaponPrefab != null && weaponPrefab.GetComponent<BaseProjectile>() != null)
                        {
                            // This is a projectile weapon - mods MUST be ProjectileWeaponMod instances
                            // Check if the mod item's entity prefab is actually a ProjectileWeaponMod
                            if (modItem.info.HasComponent<ItemModEntity>())
                            {
                                var modEntityMod = modItem.info.GetComponent<ItemModEntity>();
                                if (modEntityMod != null && modEntityMod.entityPrefab != null)
                                {
                                    try
                                    {
                                        var modPrefab = modEntityMod.entityPrefab.Get();
                                        if (modPrefab != null)
                                        {
                                            // CRITICAL: The entity must be a ProjectileWeaponMod to avoid InvalidCastException
                                            // This is the definitive check based on the assembly code
                                            if (modPrefab.GetComponent<ProjectileWeaponMod>() == null)
                                            {
                                                DebugLog($"IsValidWeaponMod: {modItem.info.shortname} entity is not a ProjectileWeaponMod for projectile weapon {weapon.info.shortname} - REJECTING");
                                                return false;
                                            }
                                            // Entity is a ProjectileWeaponMod - allow it
                                            return true;
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        DebugLog($"IsValidWeaponMod: Error checking mod entity prefab for {modItem.info.shortname}: {e.Message} - REJECTING");
                                        return false;
                                    }
                                }
                            }
                            
                            // If mod doesn't have ItemModEntity, fall back to shortname check
                            // For projectile weapons, ONLY allow weapon.mod.* items (not ammunition or other items)
                            // Ammunition should be in the weapon's ammo slot, not mod slots
                            if (!shortname.StartsWith("weapon.mod.", StringComparison.OrdinalIgnoreCase))
                            {
                                DebugLog($"IsValidWeaponMod: {modItem.info.shortname} is not a weapon.mod.* item for projectile weapon {weapon.info.shortname} - REJECTING");
                                return false;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // If we can't check the weapon prefab, be conservative and only allow weapon.mod.* items
                        DebugLog($"IsValidWeaponMod: Error checking weapon prefab for {weapon.info.shortname}: {e.Message} - using strict validation");
                        if (!shortname.StartsWith("weapon.mod.", StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                }
            }
            
            // Allow weapon mods by shortname pattern (most reliable check)
            if (shortname.StartsWith("weapon.mod.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            
            // CRITICAL: For projectile weapons, do NOT allow ammunition in mod slots
            // Ammunition should only be in the weapon's ammo slot, not in mod slots
            // Allowing ammunition in mod slots can cause InvalidCastException
            // Only allow ammunition for non-projectile weapons (melee, etc.)
            if (modItem.info.category == ItemCategory.Ammunition)
            {
                // Check if this is a projectile weapon - if so, reject ammunition
                if (weapon.info.HasComponent<ItemModEntity>())
                {
                    var weaponEntityMod = weapon.info.GetComponent<ItemModEntity>();
                    if (weaponEntityMod != null && weaponEntityMod.entityPrefab != null)
                    {
                        try
                        {
                            var weaponPrefab = weaponEntityMod.entityPrefab.Get();
                            if (weaponPrefab != null && weaponPrefab.GetComponent<BaseProjectile>() != null)
                            {
                                DebugLog($"IsValidWeaponMod: {modItem.info.shortname} is ammunition for projectile weapon {weapon.info.shortname} - REJECTING (ammo should be in ammo slot, not mod slots)");
                                return false;
                            }
                        }
                        catch
                        {
                            // If we can't check, reject to be safe
                            return false;
                        }
                    }
                }
                // For non-projectile weapons, allow ammunition (though it's unusual)
                return true;
            }
            
            // CRITICAL: If we get here, it's not a known weapon mod type
            // We MUST reject it - only weapon.mod.* items and ammunition (for non-projectile weapons) are allowed in weapon mod slots
            // Allowing other items causes InvalidCastException when the game tries to cast them to ProjectileWeaponMod
            DebugLog($"IsValidWeaponMod: {modItem.info.shortname} doesn't match known mod patterns (weapon.mod.* or ammunition) - REJECTING");
            return false;
        }
        
        /// <summary>
        /// Cleans up invalid mods from all weapons in a player's inventory
        /// This prevents InvalidCastException when other plugins trigger weapon mod calculations
        /// </summary>
        private void CleanupAllWeaponModsInPlayerInventory(BasePlayer player)
        {
            if (player == null || player.inventory == null) return;
            
            try
            {
                // Track all invalid items to add to player inventory at the end
                var allInvalidItems = new List<Item>();
                
                // Check all containers
                var containers = new[] 
                { 
                    player.inventory.containerBelt, 
                    player.inventory.containerWear, 
                    player.inventory.containerMain 
                };
                
                // Also check backpack if player has one
                var backpack = player.inventory.GetBackpackWithInventory();
                if (backpack != null && backpack.contents != null)
                {
                    var containerList = new List<ItemContainer>(containers);
                    containerList.Add(backpack.contents);
                    containers = containerList.ToArray();
                }
                
                foreach (var container in containers)
                {
                    if (container == null) continue;
                    
                    foreach (var item in container.itemList)
                    {
                        if (item == null || item.info == null) continue;
                        
                        // Check if it's a weapon
                        if (item.info.category == ItemCategory.Weapon)
                        {
                            // Clean up invalid mods
                            CleanupInvalidWeaponMods(item);
                            
                            // Collect invalid items
                            if (_invalidWeaponContents.TryGetValue(item.uid, out List<Item> invalidItems))
                            {
                                allInvalidItems.AddRange(invalidItems);
                                _invalidWeaponContents.Remove(item.uid);
                            }
                        }
                    }
                }
                
                // Add all invalid items to player inventory
                if (allInvalidItems.Count > 0)
                {
                    DebugLog($"CleanupAllWeaponModsInPlayerInventory: Removed {allInvalidItems.Count} invalid mods from {player.displayName}'s weapons");
                    foreach (var invalidItem in allInvalidItems)
                    {
                        try
                        {
                            player.GiveItem(invalidItem, BaseEntity.GiveItemReason.PickedUp);
                        }
                        catch (Exception e)
                        {
                            PrintError($"Error giving invalid mod to player: {e.Message}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                PrintError($"Error in CleanupAllWeaponModsInPlayerInventory: {e.Message}");
            }
        }

        /// <summary>
        /// Cleans up any invalid mods from a weapon's contents to prevent InvalidCastException
        /// This should be called after restoring a weapon to ensure no invalid mods remain
        /// </summary>
        private void CleanupInvalidWeaponMods(Item weapon)
        {
            if (weapon == null || weapon.info == null || weapon.contents == null)
                return;
            
            // Only process weapons
            if (weapon.info.category != ItemCategory.Weapon)
                return;
            
            // Check if this is a projectile weapon
            bool isProjectileWeapon = false;
            if (weapon.info.HasComponent<ItemModEntity>())
            {
                var weaponEntityMod = weapon.info.GetComponent<ItemModEntity>();
                if (weaponEntityMod != null && weaponEntityMod.entityPrefab != null)
                {
                    try
                    {
                        var weaponPrefab = weaponEntityMod.entityPrefab.Get();
                        if (weaponPrefab != null && weaponPrefab.GetComponent<BaseProjectile>() != null)
                        {
                            isProjectileWeapon = true;
                        }
                    }
                    catch
                    {
                        // If we can't check, assume it might be a projectile weapon to be safe
                        isProjectileWeapon = true;
                    }
                }
            }
            
            // Create a list of items to remove (we can't modify the collection while iterating)
            var itemsToRemove = new List<Item>();
            
            foreach (var modItem in weapon.contents.itemList)
            {
                if (modItem == null || modItem.info == null)
                    continue;
                
                // Validate the mod item
                if (!IsValidWeaponMod(modItem, weapon))
                {
                    DebugLog($"CleanupInvalidWeaponMods: Removing invalid mod {modItem.info.shortname} from weapon {weapon.info.shortname}");
                    itemsToRemove.Add(modItem);
                }
            }
            
            // Remove invalid items from weapon contents
            foreach (var invalidItem in itemsToRemove)
            {
                try
                {
                    invalidItem.RemoveFromContainer();
                    // Store it to be added to player inventory separately
                    if (!_invalidWeaponContents.ContainsKey(weapon.uid))
                    {
                        _invalidWeaponContents[weapon.uid] = new List<Item>();
                    }
                    _invalidWeaponContents[weapon.uid].Add(invalidItem);
                    DebugLog($"CleanupInvalidWeaponMods: Removed invalid mod {invalidItem.info.shortname} from weapon {weapon.info.shortname}");
                }
                catch (Exception e)
                {
                    PrintError($"Error removing invalid mod from weapon: {e.Message}");
                }
            }
        }
        
        #endregion

        #region Core Methods
        private bool TryRestorePlayer(BasePlayer player)
        {
            // Always prioritize persistent data over in-memory entities
            // This ensures items are restored from the pre-death capture, not the dropped corpse
            if (_playerData.StoredInventories.ContainsKey(player.userID))
            {
                DebugLog($"Restoring from persistent data for {player.displayName}");
                return RestoreFromPersistentData(player);
            }
            
            // Fallback to in-memory entity data if no persistent data exists (legacy support)
            if (_lastInvs.TryGetValue(player.userID, out BaseEntity entity) && entity != null && !entity.IsDestroyed)
            {
                DebugLog($"Fallback: Restoring from entity data for {player.displayName}");
                return RestoreFromEntity(player, entity);
            }
            
            DebugLog($"No restoration data found for {player.displayName}");
            return false;
        }

        private bool RestoreFromEntity(BasePlayer player, BaseEntity entity)
            {
                var sb = new StringBuilder(256);
            int restoredCount = 0;

                if (entity is PlayerCorpse corpse)
                {
                // Do not clear current inventory - merge corpse items with existing gear.

                // Restore items from corpse with slot preservation
                for (int containerIndex = 0; containerIndex < corpse.containers.Length; containerIndex++)
                {
                    var container = corpse.containers[containerIndex];
                    var playerContainer = GetPlayerContainer(player, GetContainerType(containerIndex));
                    
                    if (playerContainer != null)
                    {
                        DebugLog($"Restoring from corpse container {containerIndex} ({GetContainerType(containerIndex)}) with {container.itemList.Count} items");
                        
                        for (int i = container.itemList.Count - 1; i >= 0; i--)
                        {
                            var item = container.itemList[i];
                            DebugLog($"Attempting to restore {item.info.shortname} from position {item.position} to {GetContainerType(containerIndex)}");
                            DebugLog($"Item parent: {item.parent?.entityOwner?.ShortPrefabName ?? "null"}, Item position: {item.position}");
                            
                            // Special handling for items that might need to go to backpack contents
                            bool restored = false;
                            
                            // Try to place item in original position first
                            if (TryPlaceItemInSlot(playerContainer, item, item.position))
                            {
                            sb.Append($"{item.info.shortname} ({item.amount}), ");
                                restoredCount++;
                                restored = true;
                                DebugLog($"Successfully restored {item.info.shortname} to position {item.position}");
                                
                                // CRITICAL: Clean up any invalid mods from weapons to prevent InvalidCastException
                                if (item.info != null && item.info.category == ItemCategory.Weapon)
                                {
                                    CleanupInvalidWeaponMods(item);
                                }
                            }
                            else
                            {
                                // If it's a clothing container and the item failed, try backpack contents
                                if (GetContainerType(containerIndex) == "wear")
                                {
                                    var backpack = player.inventory.GetBackpackWithInventory();
                                    if (backpack != null && backpack.contents != null)
                                    {
                                        DebugLog($"Trying to place {item.info.shortname} in backpack contents");
                                        if (TryPlaceItemInSlot(backpack.contents, item, item.position))
                                        {
                                            sb.Append($"{item.info.shortname} ({item.amount}), ");
                                            restoredCount++;
                                            restored = true;
                                            DebugLog($"Successfully restored {item.info.shortname} to backpack contents at position {item.position}");
                                            
                                            // CRITICAL: Clean up any invalid mods from weapons to prevent InvalidCastException
                                            if (item.info != null && item.info.category == ItemCategory.Weapon)
                                            {
                                                CleanupInvalidWeaponMods(item);
                                            }
                                        }
                                    }
                                }
                                
                                if (!restored)
                                {
                                    // Fallback to normal give item
                            player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
                                    sb.Append($"{item.info.shortname} ({item.amount}), ");
                                    restoredCount++;
                                    DebugLog($"Fallback: gave {item.info.shortname} to player - MoveToContainer failed");
                                    
                                    // CRITICAL: Clean up any invalid mods from weapons to prevent InvalidCastException
                                    if (item.info != null && item.info.category == ItemCategory.Weapon)
                                    {
                                        CleanupInvalidWeaponMods(item);
                                    }
                                }
                            }
                        }
                        }
                    }
                    corpse?.Kill(BaseNetworkable.DestroyMode.None);
                }
                else if (entity is DroppedItemContainer container)
                {
                // For dropped containers, just give items normally since we don't have slot info
                for (int i = container.inventory.itemList.Count - 1; i >= 0; i--)
                    {
                    var item = container.inventory.itemList[i];
                        sb.Append($"{item.info.shortname} ({item.amount}), ");
                        player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
                        
                        // CRITICAL: Clean up any invalid mods from weapons to prevent InvalidCastException
                        if (item.info != null && item.info.category == ItemCategory.Weapon)
                        {
                            CleanupInvalidWeaponMods(item);
                        }
                        
                    restoredCount++;
                    }
                    container?.Kill(BaseNetworkable.DestroyMode.None);
            }

            // Handle any invalid weapon contents that were removed during cleanup
            // Collect all invalid items from all weapons that were restored
            var allInvalidItems = new List<Item>();
            foreach (var kvp in _invalidWeaponContents)
            {
                allInvalidItems.AddRange(kvp.Value);
            }
            
            // Give invalid items to player inventory
            foreach (var invalidItem in allInvalidItems)
            {
                try
                {
                    player.GiveItem(invalidItem, BaseEntity.GiveItemReason.PickedUp);
                    sb.Append($"{invalidItem.info.shortname} ({invalidItem.amount}), ");
                    restoredCount++;
                    DebugLog($"Added invalid weapon content {invalidItem.info.shortname} to player inventory");
                }
                catch (Exception e)
                {
                    PrintError($"Error giving invalid weapon content to player: {e.Message}");
                }
            }
            
            // Clear the invalid weapon contents dictionary
            _invalidWeaponContents.Clear();

            if (restoredCount > 0)
            {
                    if (sb.Length > 2) sb.Length -= 2;
                Puts($"{player.displayName} ({player.userID}) restored from entity: {sb}");
                
                // Delete the corpse since items have been restored
                if (entity is PlayerCorpse playerCorpse)
                {
                    DebugLog($"Deleting corpse after restoration for {player.displayName}");
                    playerCorpse.Kill();
                }
                
                return true;
            }

            return false;
        }

        private void CleanupExpiredCooldowns()
        {
            var now = DateTime.Now;
            var expiredKeys = new List<ulong>();
            foreach (var kvp in _cooldowns)
            {
                if (DateTime.Compare(now, kvp.Value) >= 0)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }
            foreach (var key in expiredKeys)
            {
                _cooldowns.Remove(key);
            }
            
            // Also clean up expired persistent data
            CleanupExpiredPersistentData();
        }

        private void CleanupExpiredPersistentData()
        {
            var expiredKeys = new List<ulong>();
            var cutoffTime = DateTime.Now.AddHours(-1);
            
            foreach (var kvp in _playerData.StoredInventories)
            {
                if (kvp.Value.StoredTime < cutoffTime)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }
            
            if (expiredKeys.Count > 0)
            {
                foreach (var key in expiredKeys)
                {
                    _playerData.StoredInventories.Remove(key);
                }
                SavePlayerData();
                // Log cleanup activity (always log, not just debug) to track redundancy
                Puts($" Cleaned up {expiredKeys.Count} expired item restoration entries (cutoff: {cutoffTime:yyyy-MM-dd HH:mm:ss})");
                DebugLog($"Cleaned up {expiredKeys.Count} expired item restoration entries.");
            }
            else
            {
                // Log when cleanup runs but finds nothing (to verify it's running)
                DebugLog("CleanupExpiredPersistentData ran - no expired entries found");
            }
        }
        #endregion

        #region Public API
        /// <summary>
        /// Public API method to restore items for a player without cost or cooldown checks
        /// Used by other plugins like Dungeon for automatic item restoration
        /// NOTE: This bypasses the normal /getstuff command payment system
        /// </summary>
        /// <param name="player">The player to restore items for</param>
        /// <returns>True if items were restored, false if no items to restore</returns>
        public bool RestorePlayerItems(BasePlayer player)
        {
            if (player == null || !player.userID.IsSteamId()) return false;
            
            // Check if player has items to restore (either in-memory or persistent)
            if (!_lastInvs.ContainsKey(player.userID) && !_playerData.StoredInventories.ContainsKey(player.userID))
            {
                DebugLog($"No items found for player {player.displayName} ({player.userID})");
                return false;
            }
            
            DebugLog($"Found items for player {player.displayName} - attempting restoration");
            
            // Use the existing TryRestorePlayer method but bypass cost/cooldown checks
            bool restored = TryRestorePlayer(player);
            
            // Clean up the stored inventory after restoration
            if (restored)
            {
                _lastInvs.Remove(player.userID);
                _playerData.StoredInventories.Remove(player.userID);
                SavePlayerData();
            }
            
            return restored;
        }

        /// <summary>
        /// Public API method to check if a player has items available for restoration
        /// </summary>
        /// <param name="player">The player to check</param>
        /// <returns>True if player has items to restore</returns>
        public bool HasItemsToRestore(BasePlayer player)
        {
            if (player == null || !player.userID.IsSteamId()) return false;
            
            bool inMemory = _lastInvs.ContainsKey(player.userID);
            bool persistent = _playerData.StoredInventories.ContainsKey(player.userID);
            
            return inMemory || persistent;
        }

        /// <summary>
        /// Public API method to force store a player's corpse/backpack for later restoration
        /// Used by other plugins to ensure items are captured before respawn
        /// </summary>
        /// <param name="player">The player whose items to store</param>
        /// <param name="entity">The corpse or backpack entity to store</param>
        public void StorePlayerItems(BasePlayer player, BaseEntity entity)
        {
            if (player == null || !player.userID.IsSteamId() || entity == null) return;
            _lastInvs[player.userID] = entity;
        }
        #endregion

        #region Commands
        internal void CmdRestoreTest(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;
            
            DebugLog($"Test command for {player.displayName} ({player.userID})");
            bool hasItems = HasItemsToRestore(player);
            DebugLog($"Test result: {hasItems}");
            
            if (hasItems)
            {
                bool restored = RestorePlayerItems(player);
                DebugLog($"Restore result: {restored}");
            }
        }

        internal void ChatCmdDebug(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;
            
            if (args.Length == 0)
            {
                // Show current debug status
                player.ChatMessage($"[RestoreItems] Debug logging is currently: {(config.enableDebug ? "ENABLED" : "DISABLED")}");
                player.ChatMessage($"[RestoreItems] Usage: /restored.debug <on|off|toggle>");
                return;
            }
            
            string action = args[0].ToLower();
            bool newState = config.enableDebug;
            
            switch (action)
            {
                case "on":
                case "enable":
                case "true":
                    newState = true;
                    break;
                case "off":
                case "disable":
                case "false":
                    newState = false;
                    break;
                case "toggle":
                    newState = !config.enableDebug;
                    break;
                default:
                    player.ChatMessage($"[RestoreItems] Invalid option. Use: on, off, or toggle");
                    return;
            }
            
            config.enableDebug = newState;
            SaveConfig();
            
            player.ChatMessage($"[RestoreItems] Debug logging is now: {(newState ? "ENABLED" : "DISABLED")}");
            Puts($" Debug logging toggled to {(newState ? "ENABLED" : "DISABLED")} by {player.displayName}");
        }

        // Automatic restoration method for plugins (no permission/cost/cooldown checks)
        public bool AutoRestorePlayerItems(BasePlayer player)
        {
            if (player == null || !player.userID.IsSteamId()) return false;

            DebugLog($"AutoRestorePlayerItems called for {player.displayName}");

            if (!_lastInvs.ContainsKey(player.userID) && !_playerData.StoredInventories.ContainsKey(player.userID))
            {
                DebugLog($"No items found for {player.displayName}");
                return false;
            }

            DebugLog($"Found items for {player.displayName} - attempting automatic restoration");

            // Use the existing TryRestorePlayer method but bypass cost/cooldown checks
            bool restored = TryRestorePlayer(player);

            // Clean up the stored inventory after restoration
            if (restored)
            {
                _lastInvs.Remove(player.userID);
                _playerData.StoredInventories.Remove(player.userID);
                SavePlayerData();
                DebugLog($"Auto-restoration successful for {player.displayName}");
            }
            else
            {
                DebugLog($"Auto-restoration failed for {player.displayName}");
            }

            return restored;
        }

        // Dungeon-specific inventory management methods
        public bool TestMethod(BasePlayer player)
        {
            DebugLog($"TestMethod called for {player?.displayName ?? "NULL"}");
            return true;
        }

        public bool SimpleTest()
        {
            DebugLog("SimpleTest called - no parameters");
            return true;
        }

        public bool SaveDungeonInventory(BasePlayer player)
        {
            try
            {
                DebugLog($"=== SaveDungeonInventory ENTRY for {player?.displayName ?? "NULL"} ===");
                
                if (player == null || !player.userID.IsSteamId()) 
                {
                    DebugLog($"SaveDungeonInventory validation failed - player null: {player == null}, userID valid: {player?.userID.IsSteamId() ?? false}");
                    return false;
                }

                DebugLog($"SaveDungeonInventory called for {player.displayName}");
            }
            catch (Exception e)
            {
                DebugLog($"Exception in SaveDungeonInventory entry: {e.Message}");
                return false;
            }

            try
            {
                // Capture current inventory
                var storedInventory = new StoredInventory
                {
                    PlayerId = player.userID,
                    StoredTime = DateTime.UtcNow,
                    Containers = new List<StoredContainer>()
                };

                // Store main container
                var mainContainer = player.inventory.containerMain;
                if (mainContainer != null)
                {
                    var storedMain = new StoredContainer
                    {
                        ContainerType = "main",
                        ContainerIndex = 0,
                        Items = new List<StoredItem>()
                    };

                    foreach (var item in mainContainer.itemList)
                    {
                        if (item != null)
                        {
                            storedMain.Items.Add(CreateStoredItem(item));
                        }
                    }
                    storedInventory.Containers.Add(storedMain);
                }

                // Store wear container
                var wearContainer = player.inventory.containerWear;
                if (wearContainer != null)
                {
                    var storedWear = new StoredContainer
                    {
                        ContainerType = "wear",
                        ContainerIndex = 1,
                        Items = new List<StoredItem>()
                    };

                    foreach (var item in wearContainer.itemList)
                    {
                        if (item != null)
                        {
                            storedWear.Items.Add(CreateStoredItem(item));
                        }
                    }
                    storedInventory.Containers.Add(storedWear);
                }

                // Store belt container
                var beltContainer = player.inventory.containerBelt;
                if (beltContainer != null)
                {
                    var storedBelt = new StoredContainer
                    {
                        ContainerType = "belt",
                        ContainerIndex = 2,
                        Items = new List<StoredItem>()
                    };

                    foreach (var item in beltContainer.itemList)
                    {
                        if (item != null)
                        {
                            storedBelt.Items.Add(CreateStoredItem(item));
                        }
                    }
                    storedInventory.Containers.Add(storedBelt);
                }

                // Store in dungeon-specific data
                if (_playerData.DungeonInventories == null)
                {
                    _playerData.DungeonInventories = new Dictionary<ulong, StoredInventory>();
                }
                _playerData.DungeonInventories[player.userID] = storedInventory;
                
                DebugLog($"Stored dungeon inventory for {player.userID}, total dungeon inventories: {_playerData.DungeonInventories.Count}");
                
                try
                {
                    SavePlayerData();
                    DebugLog("Player data saved successfully");
                }
                catch (Exception e)
                {
                    PrintError($"Error saving player data: {e.Message}");
                    return false;
                }

                // Log detailed storage information
                DebugLog($"=== DUNGEON INVENTORY SAVED for {player.displayName} ===");
                int totalItems = 0;
                foreach (var container in storedInventory.Containers)
                {
                    totalItems += container.Items.Count;
                }
                DebugLog($"Total items stored: {totalItems}");
                DebugLog($"Containers: {storedInventory.Containers.Count}");
                
                var sortedContainers = new List<StoredContainer>(storedInventory.Containers);
                sortedContainers.Sort((a, b) => a.ContainerIndex.CompareTo(b.ContainerIndex));
                foreach (var container in sortedContainers)
                {
                    DebugLog($"{container.ContainerType.ToUpper()} Container ({container.Items.Count} items):");
                    foreach (var item in container.Items)
                    {
                        DebugLog($"  [{item.Position}] {item.Name} x{item.Amount} (Skin: {item.SkinId})");
                    }
                }
                DebugLog("=== END DUNGEON INVENTORY SAVE ===");
                
                return true;
            }
            catch (Exception e)
            {
                PrintError($"Error saving dungeon inventory for {player.displayName}: {e.Message}");
                return false;
            }
        }

        public bool RestoreDungeonInventory(BasePlayer player)
        {
            if (player == null || !player.userID.IsSteamId()) return false;

            DebugLog($"RestoreDungeonInventory called for {player.displayName}");

            if (!_playerData.DungeonInventories.TryGetValue(player.userID, out StoredInventory storedInventory))
            {
                DebugLog($"No dungeon inventory found for {player.displayName}");
                return false;
            }

            try
            {
                // Log what we're about to restore
                DebugLog($"=== DUNGEON INVENTORY RESTORATION for {player.displayName} ===");
                int totalItems = 0;
                foreach (var container in storedInventory.Containers)
                {
                    totalItems += container.Items.Count;
                }
                DebugLog($"Total items to restore: {totalItems}");
                DebugLog($"Containers: {storedInventory.Containers.Count}");
                
                var sortedContainers = new List<StoredContainer>(storedInventory.Containers);
                sortedContainers.Sort((a, b) => a.ContainerIndex.CompareTo(b.ContainerIndex));
                foreach (var container in sortedContainers)
                {
                    DebugLog($"{container.ContainerType.ToUpper()} Container ({container.Items.Count} items):");
                    foreach (var item in container.Items)
                    {
                        DebugLog($"  [{item.Position}] {item.Name} x{item.Amount} (Skin: {item.SkinId})");
                    }
                }
                DebugLog("=== STARTING RESTORATION ===");

                // Do not clear current inventory - merge saved dungeon kit with existing items.

                var sb = new StringBuilder(256);
                int restoredCount = 0;

                // Reuse sortedContainers from logging section above for restoration
                foreach (var storedContainer in sortedContainers)
                {
                    var container = GetPlayerContainer(player, storedContainer.ContainerType);
                    if (container == null)
                    {
                        DebugLog($"Could not find container for type: {storedContainer.ContainerType}");
                        
                        // If it's a backpack container and player has no backpack, put items in main inventory
                        if (storedContainer.ContainerType == "backpack")
                        {
                            DebugLog($"Fallback: putting backpack items in main inventory for {player.displayName}");
                            container = player.inventory.containerMain;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    DebugLog($"Restoring to {storedContainer.ContainerType} container with {storedContainer.Items.Count} items");

                    foreach (var storedItem in storedContainer.Items)
                    {
                        var item = RecreateItem(storedItem);
                        if (item != null)
                        {
                            DebugLog($"Attempting to restore {item.info.shortname} to position {storedItem.Position} in {storedContainer.ContainerType}");

                            // Try to place item in original position
                            if (TryPlaceItemInSlot(container, item, storedItem.Position))
                            {
                                sb.Append($"{item.info.shortname} ({item.amount}), ");
                                restoredCount++;
                                DebugLog($"Successfully restored {item.info.shortname} to position {storedItem.Position}");
                                
                                // CRITICAL: Clean up any invalid mods from weapons to prevent InvalidCastException
                                // This must be done AFTER the item is placed, but BEFORE handling invalid contents
                                if (item.info != null && item.info.category == ItemCategory.Weapon)
                                {
                                    CleanupInvalidWeaponMods(item);
                                }
                                
                                // Note: We do NOT reinitialize weapon attachments - the game handles this automatically
                                // when items are added to weapon contents. Reinitializing can cause validation issues.
                                
                                // Handle invalid weapon contents (items that couldn't be added to mod slots)
                                if (_invalidWeaponContents.TryGetValue(item.uid, out List<Item> invalidItems))
                                {
                                    DebugLog($"  -> Found {invalidItems.Count} invalid items from {item.info.shortname} - adding to player inventory");
                                    foreach (var invalidItem in invalidItems)
                                    {
                                        player.GiveItem(invalidItem, BaseEntity.GiveItemReason.PickedUp);
                                        sb.Append($"{invalidItem.info.shortname} ({invalidItem.amount}), ");
                                        restoredCount++;
                                        DebugLog($"  -> Added invalid weapon content {invalidItem.info.shortname} to player inventory");
                                    }
                                    _invalidWeaponContents.Remove(item.uid);
                                }
                            }
                            else
                            {
                                // Fallback to normal give item
                                player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
                                sb.Append($"{item.info.shortname} ({item.amount}), ");
                                restoredCount++;
                                DebugLog($"Fallback: gave {item.info.shortname} to player");
                                
                                // CRITICAL: Clean up any invalid mods from weapons to prevent InvalidCastException
                                // This must be done AFTER the item is given, but BEFORE handling invalid contents
                                if (item.info != null && item.info.category == ItemCategory.Weapon)
                                {
                                    CleanupInvalidWeaponMods(item);
                                }
                                
                                // Note: We do NOT reinitialize weapon attachments - the game handles this automatically
                                // when items are added to weapon contents. Reinitializing can cause validation issues.
                                
                                // Handle invalid weapon contents (items that couldn't be added to mod slots)
                                if (_invalidWeaponContents.TryGetValue(item.uid, out List<Item> invalidItems))
                                {
                                    DebugLog($"  -> Found {invalidItems.Count} invalid items from {item.info.shortname} - adding to player inventory");
                                    foreach (var invalidItem in invalidItems)
                                    {
                                        player.GiveItem(invalidItem, BaseEntity.GiveItemReason.PickedUp);
                                        sb.Append($"{invalidItem.info.shortname} ({invalidItem.amount}), ");
                                        restoredCount++;
                                        DebugLog($"  -> Added invalid weapon content {invalidItem.info.shortname} to player inventory");
                                    }
                                    _invalidWeaponContents.Remove(item.uid);
                                }
                            }
                        }
                    }
                }

                if (restoredCount > 0)
                {
                    if (sb.Length > 2) sb.Length -= 2;
                    DebugLog("=== DUNGEON RESTORATION COMPLETE ===");
                    DebugLog($"{player.displayName} ({player.userID}) restored dungeon inventory: {sb}");
                    DebugLog($"Items restored: {restoredCount}");
                    DebugLog("=== END DUNGEON RESTORATION ===");

                    // Delete any existing corpse since we're restoring from saved inventory
                    // Only delete if we have a valid death position (dungeon restoration may not have one)
                    if (storedInventory.DeathPosition != Vector3.zero)
                    {
                        DeletePlayerCorpse(player, storedInventory.DeathPosition);
                    }

                    return true;
                }
            }
            catch (Exception e)
            {
                PrintError($"Error restoring dungeon inventory: {e.Message}");
            }

            return false;
        }

        public bool ClearDungeonInventory(BasePlayer player)
        {
            if (player == null || !player.userID.IsSteamId()) return false;

            DebugLog($"ClearDungeonInventory called for {player.displayName}");

            bool hadInventory = _playerData.DungeonInventories.ContainsKey(player.userID);
            _playerData.DungeonInventories.Remove(player.userID);
            SavePlayerData();

            if (hadInventory)
            {
                DebugLog($"Cleared dungeon inventory for {player.displayName}");
            }
            else
            {
                DebugLog($"No dungeon inventory to clear for {player.displayName}");
            }

            return hadInventory;
        }

        public bool HasDungeonInventory(BasePlayer player)
        {
            if (player == null || !player.userID.IsSteamId()) return false;
            return _playerData.DungeonInventories.ContainsKey(player.userID);
        }

        internal void ChatCmdGetItems(BasePlayer player, string command, string[] args)
        {
            if (!_inProgress.Add(player.userID))
            {
                return;
            }
            try
            {
                if (!_permissionCache.TryGetValue(player.userID, out bool hasPermission))
                {
                    hasPermission = permission.UserHasPermission(player.UserIDString, USE_PERM);
                    _permissionCache[player.userID] = hasPermission;
                }

                if (!hasPermission)
                {
                    Print(player, "Permission");
                    return;
                }

                // CRITICAL: Block restoration if player is in RaidableBases zone
                // RaidableBases handles its own item restoration, so we should not interfere
                if (RaidableBases != null && IsPlayerInRaidableBase(player))
                {
                    DebugLog($"Player {player.displayName} is in RaidableBases area - blocking RestoreItems restoration (RaidableBases will handle it)");
                    Print(player, "No Inv"); // Don't reveal RaidableBases integration to players
                    return;
                }

                // Check for stored inventory in persistent data (primary method)
                if (!_playerData.StoredInventories.ContainsKey(player.userID))
                {
                    Print(player, "No Inv");
                    return;
                }

                // Also check if the stored inventory was from a RaidableBases death
                if (_playerData.StoredInventories.TryGetValue(player.userID, out StoredInventory storedInv))
                {
                    // Check if death position was in a RaidableBases zone
                    if (RaidableBases != null && IsPositionInRaidableBase(storedInv.DeathPosition))
                    {
                        DebugLog($"Stored inventory for {player.displayName} was from RaidableBases death at {storedInv.DeathPosition} - blocking restoration");
                        // Remove the stored inventory since RaidableBases will handle it
                        _playerData.StoredInventories.Remove(player.userID);
                        SavePlayerData();
                        Print(player, "No Inv");
                        return;
                    }
                }

                if (config.chatS.useCooldown)
                {
                    var now = DateTime.Now;
                    if (_cooldowns.TryGetValue(player.userID, out DateTime cooldownTime))
                    {
                        if (DateTime.Compare(now, cooldownTime) < 0)
                        {
                            Print(player, "Cooldown", FormatTs(now - cooldownTime));
                            return;
                        }
                        _cooldowns.Remove(player.userID);
                    }
                    _cooldowns[player.userID] = now.AddSeconds(config.chatS.commandCooldown);
                }

                double playerBalance = Economics?.Call<double>("Balance", player.userID) ?? 0;
                if (playerBalance >= config.srCost)
                {
                    // Restore from persistent data (like Dungeon API)
                    Puts($"{player.displayName} ({player.userID}) used /{config.chatS.playerChatCommand} command to restore items");
                    if (RestoreFromPersistentData(player))
                    {
                        Print(player, "Restore");
                        Economics?.Call("Withdraw", player.userID, (double)config.srCost);
                        
                        // Note: Corpse deletion and inventory cleanup handled in RestoreFromPersistentData
                    }
                    else 
                    {
                        Print(player, "No Inv");
                    }
                }
                else 
                {
                    Print(player, "Points", playerBalance, config.srCost);
                }
            }
            finally
            {
                _inProgress.Remove(player.userID);
            }
        }
        #endregion

        #region Config
        private ConfigData config;

        private class ConfigData
        {
            [JsonProperty("Cost To Restore Items (Server Rewards)")]
            public int srCost = 100;
            [JsonProperty("Enable Debug Logging (true/false)")]
            public bool enableDebug = false;
            [JsonProperty("Chat Settings")]
            public ChatSettings chatS = new ChatSettings();
        }

        public class ChatSettings
        {
            [JsonProperty("Chat Command")]
            public string playerChatCommand = "getstuff";
            [JsonProperty("Use Command Cooldown (true/false)")]
            public bool useCooldown = false;
            [JsonProperty("Command Cooldown (seconds)")]
            public float commandCooldown = 60;
            [JsonProperty("Message Icon (Steam64 ID)")]
            public ulong steamIDIcon = 0;
            [JsonProperty("Message Prefix")]
            public string prefix = "<color=#FFD700>[Restore Items]</color>";
            [JsonProperty("Message Color")]
            public string messageColor = "#FFFFFF";
        }

        protected override void LoadConfig()
        {
            Config.Filename = Path.Combine(OxideMod.ResolveServerRoot(), "HarmonyConfig", Name + ".json");
            if (File.Exists(Config.Filename))
                Config.Load();
            try
            {
                config = Config.ReadObject<ConfigData>();
            }
            catch
            {
            }
            if (config == null)
            {
                LoadDefaultConfig();
            }
            SaveConfig();
        }
        protected override void LoadDefaultConfig()
        {
            PrintError("Configuration file is corrupt(or doesn't exist), creating a new one!");
            config = new ConfigData();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }
        #endregion

        #region Localization
        private void Print(BasePlayer player, string key, params object[] args)
        {
            string message = string.Format(lang.GetMessage(key, this, player.UserIDString), args);
            player.SendConsoleCommand("chat.add", 2, config.chatS.steamIDIcon, $"{config.chatS.prefix} <color={config.chatS.messageColor}>{message}</color>");
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Permission"] = "You don't have permission to use this command.",
                ["Cooldown"] = "That command is on cooldown.\nTry again in {0}.",
                ["No Inv"] = "You don't have a previous life to take items from or it has been too long since your last death.",
                ["Points"] = "You don't have enough points to do that. You have {0} and it requires {1}.",
                ["Restore"] = "Your items have been restored."

            }, this);
        }
        #endregion

        #region Helpers
        #region NPC Detection Helpers
        /// <summary>
        /// Checks if a player is an NPC (bot). NPCs have userID < 10000000 and IsNpc = true
        /// </summary>
        private bool IsNPC(BasePlayer player)
        {
            if (player == null) return false;
            
            // Check if it's a bot ID (userID < 10000000)
            if (player.userID < 10000000) return true;
            
            // Check IsNpc property (NPCPlayer overrides this to return true)
            if (player.IsNpc) return true;
            
            // Check if it's an NPCPlayer instance
            if (player is NPCPlayer) return true;
            
            return false;
        }
        
        /// <summary>
        /// Checks if a userID is a bot ID (userID < 10000000)
        /// </summary>
        private bool IsBotID(ulong userID)
        {
            return userID < 10000000;
        }
        
        /// <summary>
        /// Checks if a PlayerCorpse is actually an NPCPlayerCorpse.
        /// NPCPlayerCorpse inherits from PlayerCorpse, so we need to check the actual type.
        /// Optimized: Removed unnecessary FindByID call since first two checks catch 99.9% of NPCs
        /// </summary>
        private bool IsNPCCorpse(PlayerCorpse corpse)
        {
            if (corpse == null) return false;
            
            // Check if it's actually an NPCPlayerCorpse (which inherits from PlayerCorpse)
            // This is the fastest check and catches all NPC corpses
            if (corpse is NPCPlayerCorpse) return true;
            
            // Check if the playerSteamID is a bot ID (userID < 10000000)
            // This catches any remaining NPC cases without expensive lookups
            if (corpse.playerSteamID < 10000000) return true;
            
            // Note: Removed FindByID check - it's unnecessary since:
            // 1. NPCPlayerCorpse type check catches all NPC corpses
            // 2. Bot ID check catches any edge cases
            // 3. FindByID would likely return null for dead NPCs anyway
            
            return false;
        }
        #endregion
        
        #region RaidableBases Detection
        /// <summary>
        /// Checks if a player is currently in a RaidableBases area.
        /// This prevents RestoreItems from capturing items when RaidableBases handles the death.
        /// </summary>
        private bool IsPlayerInRaidableBase(BasePlayer player)
        {
            if (player == null || RaidableBases == null) return false;
            return IsPositionInRaidableBase(player.transform.position);
        }

        private bool IsPositionInRaidableBase(Vector3 position)
        {
            if (RaidableBases == null) return false;
            
            try
            {
                // Call RaidableBases API to check if position is in a raid zone
                var result = RaidableBases?.Call("IsPositionInRaid", position);
                if (result is bool inRaid && inRaid)
                {
                    return true;
                }
                
                // Alternative: Check if position is in a raid zone
                var inZone = RaidableBases?.Call("IsPositionInZone", position);
                if (inZone is bool inZoneResult && inZoneResult)
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                // If API call fails, assume not in raid to avoid blocking normal deaths
                DebugLog($"Error checking RaidableBases status for position {position}: {e.Message}");
            }
            
            return false;
        }
        #endregion
        
        private void DebugLog(string message)
        {
            if (config.enableDebug)
            {
                Puts($" {message}");
            }
        }

        private string FormatTs(TimeSpan t)
        {
            if (t.TotalMinutes < 1.0)
            {
                return string.Format("{0}s", t.Seconds);
            }
            else if (t.TotalHours < 1.0)
            {
                return string.Format("{0}m:{1:D2}s", t.Minutes, t.Seconds);
            }
            else
            { // more than 1 hour
                return string.Format("{0}h:{1:D2}m:{2:D2}s", (int)t.TotalHours, t.Minutes, t.Seconds);
            }
        }

        // ---- Harmony lifecycle ----
        public void HarmonyInit()
        {
            LoadConfig();
            LoadDefaultMessages();
        }

        public void HarmonyServerInitialized()
        {
            OnServerInitialized();
        }

        public void HarmonyUnload()
        {
            timer?.DestroyAll();
        }
        #endregion
    }
}