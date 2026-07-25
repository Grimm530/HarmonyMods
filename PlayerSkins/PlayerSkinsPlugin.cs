using Facepunch;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Ext.Chaos;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using System.Linq;
using System.Collections;
using System.Globalization;
using Oxide.Ext.Chaos.Data;
using Oxide.Ext.Chaos.UIFramework;
using Steamworks;
using UnityEngine.UI;

using Chaos = Oxide.Ext.Chaos;
using Color = Oxide.Ext.Chaos.UIFramework.Color;
using Font = Oxide.Ext.Chaos.UIFramework.Font;
using GridLayoutGroup = Oxide.Ext.Chaos.UIFramework.GridLayoutGroup;
using VerticalLayoutGroup = Oxide.Ext.Chaos.UIFramework.VerticalLayoutGroup;
using UIAnchor = Oxide.Ext.Chaos.UIFramework.Anchor;
using PlayerSkinsHarmony;

namespace PlayerSkinsHarmony
{
    /// <summary>PlayerSkins 3.0.141 Harmony port</summary>
    public class PlayerSkinsPlugin : PlayerSkinsPluginBase
    {
        #region Fields

        private static PlayerSkinsPlugin s_Instance;

        private Datafile<Hash<ulong, UserData>> m_UserData;
        private Datafile<Hash<string, Hash<ulong, SkinData>>> m_SkinData;
        private Datafile<List<ulong>> m_ExcludedSkins;
        
        private readonly List<ulong> m_SkinsToLoad = new List<ulong>();
        private readonly HashSet<ItemDefinition> m_SkinnableItems = new HashSet<ItemDefinition>();
        private static readonly Hash<string, int> m_ShortnameToItemId = new Hash<string, int>();
        private readonly Hash<int, ItemSkinDirectory.Skin> _itemIdToSkin = new Hash<int, ItemSkinDirectory.Skin>();
        
        private readonly string[] m_IgnoreItems = new string[] { "ammo.snowballgun", "blueprintbase", "rhib", "spraycandecal", "vehicle.chassis", "vehicle.module", "water", "water.salt" };

        private static DisplayMode m_ForcedDisplayMode;
        private CurrencyType m_CurrencyType;
        private int m_ScrapItemId;

        [Permission] private const string SHOP_PERMISSION = "playerskins.shop";
        [Permission] private const string RESKIN_PERMISSION = "playerskins.reskin";
        [Permission] private const string NOCHARGE_PERMISSION = "playerskins.nocharge";
        [Permission] private const string ADMIN_PERMISSION = "playerskins.admin";
        [Permission] private const string ADD_SKIN_PERMISSION = "playerskins.addskin";
        
        public enum CurrencyType { None, ServerRewards, Economics, Scrap }
        #endregion
        
        #region Oxide Hooks

        internal void OnServerInitialized()
        {
            m_CurrencyType = ParseType<CurrencyType>(Configuration.Purchase.Type);
            if (Configuration.Purchase.Enabled && m_CurrencyType == CurrencyType.None)
            {
                PrintError("Invalid purchase plugin specified in config. Must be either 'ServerRewards' or 'Economics'!");
                return;
            }

            m_ForcedDisplayMode = ParseType<DisplayMode>(Configuration.Shop.ForcedMode);

            bool updateConfig = false;

            for (int i = 0; i < Configuration.Shop.Permissions.Count; i++)
            {
                string perm = Configuration.Shop.Permissions[i];
                if (!perm.StartsWith("playerskins."))
                {
                    Configuration.Shop.Permissions[i] = perm = $"playerskins.{perm}";
                    updateConfig = true;
                }
                
                PermissionsBridge.RegisterPermission(perm);
            }

            if (updateConfig)
                SaveConfiguration();

            if (Configuration.Commands != null && string.IsNullOrEmpty(Configuration.Commands.AddSkinCommand))
            {
                Configuration.Commands.AddSkinCommand = "addskin";
                SaveConfiguration();
            }

            PermissionsBridge.RegisterPermission(ADD_SKIN_PERMISSION);

            foreach (ItemDefinition itemDefinition in ItemManager.itemList)
            {
                if (itemDefinition.shortname == "scrap")
                    m_ScrapItemId = itemDefinition.itemid;

                if (string.IsNullOrEmpty(itemDefinition.displayName.english))
                    continue;
                
                m_ShortnameToItemId[itemDefinition.shortname] = itemDefinition.itemid;

                string workshopName = itemDefinition.displayName.english.ToLower().Replace("skin", "").Replace(" ", "").Replace("-", "");

                if (!m_WorkshopNameToShortname.ContainsKey(workshopName))
                    m_WorkshopNameToShortname[workshopName] = itemDefinition.shortname;

                if (!m_WorkshopNameToShortname.ContainsKey(itemDefinition.shortname))
                    m_WorkshopNameToShortname[itemDefinition.shortname] = itemDefinition.shortname;

                if (!m_WorkshopNameToShortname.ContainsKey(itemDefinition.shortname.Replace(".", "")))
                    m_WorkshopNameToShortname[itemDefinition.shortname.Replace(".", "")] = itemDefinition.shortname;
            }
            
            if (ImageLibrary.IsLoaded)
            {
                ImageLibrary.AddImage("https://chaoscode.io/oxide/Images/magnifyingglass.png", "playerskins.search", 0UL, () =>
                {
                    m_MagnifyImage = ImageLibrary.GetImage("playerskins.search", 0UL);
                });
            }

            if (string.IsNullOrEmpty(Configuration.Workshop.SteamAPIKey))
            {
                PrintError("You must enter a Steam API key in your config in order to retrieve approved skin icons and/or access workshop items. Unable to continue...");
                return;
            }
            
            if (Configuration.Workshop.ApprovedIfOwned && !PlayerDlcApi.IsLoaded)
                Debug.LogWarning("[PlayerSkins] - PlayerDLCAPI plugin is not loaded, skin ownership checks will not work!");
            
            if (!Configuration.Workshop.ApprovedIfOwned && Configuration.Workshop.UseApproved)
                Debug.LogWarning("[PlayerSkins] WARNING! As of August 7th 2025, granting access to paid DLC that users do not own is against Rust's Terms of Service and can result in your server being delisted or worse.\n" +
                                 "If you continue to allow users to use paid DLC skins, you do so at your own risk!\n" +
                                 "https://facepunch.com/legal/servers");

            if ((Steamworks.SteamInventory.Definitions?.Length ?? 0) == 0)
            {
                PrintWarning("Waiting for Steamworks to update item definitions....");
                Steamworks.SteamInventory.OnDefinitionsUpdated += StartApprovedRequest;
            }
            else StartApprovedRequest();

            timer.In(Configuration.Announcements.Interval * 60, BroadcastAnnouncement);
        }

        internal void OnServerSave() => m_UserData.Save();

        internal void Unload()
        {
            StopReskinDebugTimer();
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                ChaosUI.Destroy(player, PS_UI_MOUSE);
                ChaosUI.Destroy(player, PS_UI);
                ChaosUI.Destroy(player, PS_UI_POPUP);
                ChaosUI.Destroy(player, PS_OVERLAY);
                ChaosUI.Destroy(player, PS_PAGE);
                ChaosUI.Destroy(player, PS_SEARCH);
            }
            foreach (var kvp in m_ActiveReskinLoot.ToList())
            {
                if (kvp.Value != null && kvp.Value.Entity != null && !kvp.Value.Entity.IsDestroyed)
                    UnityEngine.Object.DestroyImmediate(kvp.Value);
            }
            m_ActiveReskinLoot.Clear();
            s_Instance = null;
        }

        internal void OnLootEntityEnd(BasePlayer player, StorageContainer storageContainer)
        {
            ReskinDebug($"OnLootEntityEnd fired containerNetId={storageContainer?.net?.ID}", player);
            if (m_ActiveReskinLoot.TryGetValue(player.userID, out ReskinLootHandler handler))
            {
                ReskinDebug("OnLootEntityEnd: destroying our handler (game closed loot)", player);
                UnityEngine.Object.DestroyImmediate(handler);
            }
        }

        internal void OnPlayerDeath(BasePlayer player, HitInfo hitInfo)
        {
            if (!m_ActiveReskinLoot.TryGetValue(player.userID, out ReskinLootHandler handler))
                return;
            handler.ReturnItemInstantly();
            player.EndLooting();
            UnityEngine.Object.DestroyImmediate(handler);
        }

        internal object CanAcceptItem(ItemContainer container, Item item)
        {
            if (!container?.entityOwner)
                return null;
            ReskinLootHandler handler = container.entityOwner.GetComponent<ReskinLootHandler>();
            if (handler == null)
                return null;
            return handler.CanAcceptItem(item);
        }

        internal object CanMoveItem(Item item, PlayerInventory inventory, ItemContainerId targetContainerID, int targetSlot, int amount, ItemMoveModifier itemMoveModifier)
        {
            if (item.parent?.entityOwner == null)
                return null;
            ReskinLootHandler handler = item.parent.entityOwner.GetComponent<ReskinLootHandler>();
            if (handler == null)
                return null;
            // Oxide blocks the move when hook returns non-null. Return null to allow, false to block.
            return handler.CanMoveItem(item, inventory, targetContainerID, targetSlot) ? null : (object)false;
        }

        internal void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (!container?.entityOwner || container.entityOwner.IsDestroyed)
                return;
            ReskinLootHandler handler = container.entityOwner.GetComponent<ReskinLootHandler>();
            if (handler != null)
                handler.OnItemAdded(item);
        }

        internal void OnItemRemovedFromContainer(ItemContainer container, Item item)
        {
            if (!container?.entityOwner || container.entityOwner.IsDestroyed)
                return;
            ReskinLootHandler handler = container.entityOwner.GetComponent<ReskinLootHandler>();
            if (handler != null)
                handler.OnItemRemoved(item);
        }

        internal void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            if (player == null || !player.IsValid() || !player.userID.IsSteamId())
                return;

            // Only tear down the shop UI; also clear mouse lock so the client is not stuck.
            ChaosUI.Destroy(player, PS_UI);
            ChaosUI.Destroy(player, PS_UI_POPUP);
            ChaosUI.Destroy(player, PS_UI_MOUSE);
            m_UIUsers.Remove(player.userID);
        }

        internal void OnUseNPC(BasePlayer npc, BasePlayer player)
        {
            if (player == null || npc == null) return;

            if (!m_UserData.Data.ContainsKey(player.userID))
                m_UserData.Data.Add(player.userID, new UserData());

            if (Configuration.Shop.NPCs.Contains(npc.UserIDString))
            {
                BaseContainer root = BaseContainer.Create(PS_UI_MOUSE, Layer.Hud, UIAnchor.Center, Offset.Default)
                    .NeedsCursor()
                    .NeedsKeyboard();
			    
                ChaosUI.Show(player, root);
                
                OpenSkinShop(player);
            }
            else if (Configuration.Reskin.NPCs.Contains(npc.UserIDString) && !Configuration.Shop.GiveItemOnPurchase)
                OpenReskinMenu(player);
        }

        internal void OnItemCraftFinished(ItemCraftTask task, Item item, ItemCrafter itemCrafter)
        {
            if (!itemCrafter)
                return;
            
            BasePlayer player = itemCrafter.owner;
            if (player == null || item == null)
                return;

            if (item.skin != 0)
                return;

            if (!m_UserData.Data.TryGetValue(player.userID, out UserData data))
                return;

            if (data.defaultSkins.ContainsKey(item.info.shortname))
            {
                ulong skinId = data.defaultSkins[item.info.shortname];
                string itemName = item.name;

                if (m_SkinData.Data.TryGetValue(item.info.shortname, out Hash<ulong, SkinData> skinLookup))
                {
                    if (skinLookup.TryGetValue(skinId, out SkinData skinData))
                        itemName = skinData.Title;
                }
                
                ChangeItemSkin(item, itemName, skinId);
            }
        }
        #endregion
        
        #region Functions
        private void BroadcastAnnouncement()
        {
            if (!Configuration.Announcements.Enabled && Configuration.Announcements.Interval > 0)
                return;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (Configuration.Shop.DisableCommand)
                    player.LocalizedMessage(this, "Help.Shop.NPC");
                else player.LocalizedMessage(this, "Help.Shop.Command");
                if (Configuration.Reskin.DisableCommand)
                    player.LocalizedMessage(this, "Help.Reskin.NPC");
                else player.LocalizedMessage(this, "Help.Reskin.Command");
            }

            timer.In(Configuration.Announcements.Interval * 60, BroadcastAnnouncement);
        }

        private void ChangeItemSkin(BasePlayer player, ulong targetSkin)
        {
            Item item = player.GetActiveItem();
            if (item == null)
            {
                item = player.inventory.containerBelt.GetSlot(0);
                if (item == null)
                    return;
            }

            string itemName = item.name;
            if (m_SkinData.Data.TryGetValue(item.info.shortname, out Hash<ulong, SkinData> skinLookup))
            {
                if (skinLookup.TryGetValue(targetSkin, out SkinData skinData))
                    itemName = skinData.Title;
            }
            
            ChangeItemSkin(item, itemName, targetSkin);

            player.UpdateActiveItem(default);
            
            int slot = item.position;
            item.SetParent(null);
            item.MarkDirty();
                                
            player.inventory.SendUpdatedInventory(PlayerInventory.Type.Belt, item.parent, false);
                                
            item.SetParent(player.inventory.containerBelt);
            item.position = slot;
            item.MarkDirty();
                                
            player.UpdateActiveItem(item.uid);
        }

        private void ChangeItemSkin(Item item, string skinName, ulong targetSkin)
        {
            item.name = skinName;
            item.skin = targetSkin;
            item.MarkDirty();

            BaseEntity heldEntity = item.GetHeldEntity();
            if (heldEntity != null)
            {
                heldEntity.skinID = targetSkin;
                heldEntity.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
            }
        }

        private int GetUserBalance(BasePlayer player)
        {
            switch (m_CurrencyType)
            {
                case CurrencyType.ServerRewards:
                    if (ServerRewards.IsLoaded)
                    {
                        object value = ServerRewards.CheckPoints(player.userID);
                        if (value is int)
                            return (int) value;
                    }

                    return 0;
                
                case CurrencyType.Economics:
                    if (Economics.IsLoaded)
                        return Convert.ToInt32(Economics.Balance(player.userID));
                    
                    return 0;
                
                case CurrencyType.Scrap:
                    return player.inventory.GetAmount(m_ScrapItemId);
                
                case CurrencyType.None:
                default:
                    return 0;
            }
        }

        private int GetRefundAmount(int price) => Mathf.CeilToInt(price * (Mathf.Clamp(Configuration.Shop.SkinRefund, 1, 100) / 100f));

        /// <summary>Returns the category price for an item shortname (same price as other skins for that item). Used when adding new workshop skins.</summary>
        private int GetCategoryPriceForShortname(string shortname)
        {
            if (Configuration.Purchase.DefaultCosts.TryGetValue(shortname, out int cost))
                return cost;
            if (m_SkinData.Data.TryGetValue(shortname, out Hash<ulong, SkinData> skins) && skins.Count > 0)
            {
                foreach (SkinData skin in skins.Values)
                {
                    cost = skin.cost;
                    Configuration.Purchase.DefaultCosts[shortname] = cost;
                    SaveConfiguration();
                    return cost;
                }
            }
            ItemDefinition def = ItemManager.FindItemDefinition(shortname);
            cost = Mathf.Max(def != null ? (int)def.rarity : 1, 1) * 10;
            Configuration.Purchase.DefaultCosts[shortname] = cost;
            SaveConfiguration();
            return cost;
        }
        
        private void RefundPurchase(BasePlayer player, int price)
        {
            switch (m_CurrencyType)
            {
                case CurrencyType.ServerRewards:
                    if (ServerRewards.IsLoaded)
                        ServerRewards.AddPoints(player.userID, price);
                    break;
                case CurrencyType.Economics:
                    if (Economics.IsLoaded)
                        Economics.Deposit(player.userID, (double) price);
                    break;
                case CurrencyType.Scrap:
                    player.GiveItem(ItemManager.CreateByItemID(m_ScrapItemId, price));
                    break;
                case CurrencyType.None:
                default:
                    return;
            }
        }
        
        private bool ChargeForPurchase(BasePlayer player, int price)
        {
            if (price <= 0)
                return true;
            
            switch (m_CurrencyType)
            {
                case CurrencyType.ServerRewards:
                    if (ServerRewards.IsLoaded)
                    {
                        object value = ServerRewards.TakePoints(player.userID, price);
                        if (value is bool)
                            return (bool) value;
                    }
                    return false;
                
                case CurrencyType.Economics:
                    if (Economics.IsLoaded)
                        return Economics.Withdraw(player.userID, (double) price);
                        
                    return false;
                
                case CurrencyType.Scrap:
                    if (player.inventory.GetAmount(m_ScrapItemId) >= price)
                    {
                        player.inventory.Take(null, m_ScrapItemId, price);
                        return true;
                    }
                    
                    return false;
                
                case CurrencyType.None:
                default:
                    return false;
            }
        }
        
        private bool UserOwnsSkin(BasePlayer player, string shortname, ulong skinID)
        {
            if (!PlayerDlcApi.IsLoaded)
                return false;

            // Community workshop skins (not paid/DLC) require purchase - never treat as owned
            if (!PlayerDlcApi.IsPaidSkin(skinID) && !PlayerDlcApi.IsRedirectedSkin(shortname))
                return false;

            // Paid/DLC skins the player owns on Steam are free (no charge in shop)
            if (PlayerDlcApi.IsOwnedOrFreeSkin(player, skinID))
                return true;

            return false;
        }

        #endregion
        
        #region Workshop Name Conversions
        private Dictionary<string, string> m_WorkshopNameToShortname = new Dictionary<string, string>
        {
            {"longtshirt", "tshirt.long" },
            {"cap", "hat.cap" },
            {"beenie", "hat.beenie" },
            {"boonie", "hat.boonie" },
            {"balaclava", "mask.balaclava" },
            {"pipeshotgun", "shotgun.waterpipe" },
            {"woodstorage", "box.wooden" },
            {"ak47", "rifle.ak" },
            {"bearrug", "rug.bear" },
            {"boltrifle", "rifle.bolt" },
            {"bandana", "mask.bandana" },
            {"hideshirt", "attire.hide.vest" },
            {"snowjacket", "jacket.snow" },
            {"buckethat", "bucket.helmet" },
            {"semiautopistol", "pistol.semiauto" },
            {"roadsignvest", "roadsign.jacket" },
            {"roadsignpants", "roadsign.kilt" },
            {"burlappants", "burlap.trousers" },
            {"collaredshirt", "shirt.collared" },
            {"mp5", "smg.mp5" },
            {"sword", "salvaged.sword" },
            {"workboots", "shoes.boots" },
            {"vagabondjacket", "jacket" },
            {"hideshoes", "attire.hide.boots" },
            {"deerskullmask", "deer.skull.mask" },
            {"minerhat", "hat.miner" },
            {"lr300", "rifle.lr300" },
            {"lr300.item", "rifle.lr300" },
            {"burlapgloves", "burlap.gloves" },
            {"burlap.gloves", "burlap.gloves"},
            {"leather.gloves", "burlap.gloves"},
            {"python", "pistol.python" },
            {"m39", "rifle.m39" },
            {"l96", "rifle.l96" },
            {"woodendoubledoor", "door.double.hinged.wood" }
        };

        private void UpdateWorkshopNameConversionList()
        {
            foreach (ItemDefinition item in ItemManager.itemList)
            {
                string workshopName = item.displayName.english.ToLower().Replace("skin", "").Replace(" ", "").Replace("-", "");

                if (!m_WorkshopNameToShortname.ContainsKey(workshopName))
                    m_WorkshopNameToShortname[workshopName] = item.shortname;

                if (!m_WorkshopNameToShortname.ContainsKey(item.shortname))
                    m_WorkshopNameToShortname[item.shortname] = item.shortname;

                if (!m_WorkshopNameToShortname.ContainsKey(item.shortname.Replace(".", "")))
                    m_WorkshopNameToShortname[item.shortname.Replace(".", "")] = item.shortname;
            }

            foreach (Skinnable skin in Skinnable.All.ToList())
            {
                if (string.IsNullOrEmpty(skin.Name) || string.IsNullOrEmpty(skin.ItemName) || m_WorkshopNameToShortname.ContainsKey(skin.Name.ToLower()))
                    continue;

                m_WorkshopNameToShortname[skin.Name.ToLower()] = skin.ItemName.ToLower();
            }
        }

        private void FindValidSkinnableItems()
        {
            foreach (int itemId in ItemSkinDirectory.Instance.skins.Select(x => x.id))
            {
                ItemDefinition itemDefinition = ItemManager.FindItemDefinition(itemId);
                if (itemDefinition != null)
                    m_SkinnableItems.Add(itemDefinition);
            }

            foreach(Skinnable skin in Skinnable.All)
            {
                ItemDefinition itemDefinition = ItemManager.FindItemDefinition(skin.Name);
                if (itemDefinition != null)
                {
                    m_SkinnableItems.Add(itemDefinition);
                    continue;
                }

                itemDefinition = ItemManager.FindItemDefinition(skin.ItemName);
                if (itemDefinition != null)
                    m_SkinnableItems.Add(itemDefinition);
            }
            
            foreach (ItemDefinition itemDefinition in ItemManager.itemList)
            {
                if (itemDefinition.HasSkins || itemDefinition.skins?.Length > 0 || m_WorkshopNameToShortname.ContainsKey(itemDefinition.shortname))
                    m_SkinnableItems.Add(itemDefinition);                
            }
        }
        #endregion

        #region Approved Skins
        private const string PUBLISHED_FILE_DETAILS = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
        private const string COLLECTION_DETAILS = "https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/";
        private const string ITEMS_BODY = "?key={0}&itemcount={1}";
        private const string ITEM_ENTRY = "&publishedfileids[{0}]={1}";
        private const string COLLECTION_BODY = "?key={0}&collectioncount=1&publishedfileids[0]={1}";

        private bool m_HasChanges = false;
        private bool m_IsReady = false;
        
        private void StartApprovedRequest()
        {
            UpdateWorkshopNameConversionList();

            FindValidSkinnableItems();

            UpdateDefaultCosts();

            UpdateLocalization();

            Steamworks.SteamInventory.OnDefinitionsUpdated -= StartApprovedRequest;

            if (!Configuration.Workshop.UseApproved && !Configuration.Workshop.Enabled)
            {
                PrintError("You have approved skins and workshop skins disabled. This leaves no skins to be shown in the skin shop!");
                return;
            }

            if (!Configuration.Workshop.UseApproved && Configuration.Workshop.Enabled)
            {
                CollectWorkshopSkins();
                return;
            }

            PrintWarning("Retrieving approved skin lists...");

            CollectApprovedSkins();
        }

        private void CollectApprovedSkins()
        {
            for (int i = 0; i < ItemSkinDirectory.Instance.skins.Length; i++)
                _itemIdToSkin[ItemSkinDirectory.Instance.skins[i].id] = ItemSkinDirectory.Instance.skins[i];

            foreach (InventoryDef item in Steamworks.SteamInventory.Definitions)
            {
                string shortname = item.GetProperty("itemshortname");
                if (string.IsNullOrEmpty(shortname) || item.Id < 100)
                    continue;

                if (m_WorkshopNameToShortname.ContainsKey(shortname))
                    shortname = m_WorkshopNameToShortname[shortname];

                if (m_IgnoreItems.Contains(shortname))
                    continue;

                ItemDefinition itemDefinition = ItemManager.FindItemDefinition(shortname);
                if (!itemDefinition || !itemDefinition.HasSkins || itemDefinition.isRedirectOf)
                    continue;

                if (_itemIdToSkin.TryGetValue(item.Id, out ItemSkinDirectory.Skin directorySkin))
                {
                    if (directorySkin.invItem && directorySkin.invItem is ItemSkin itemSkin && itemSkin.Redirect)
                        continue;
                }

                if (!ulong.TryParse(item.GetProperty("workshopid"), out ulong workshopid))
                {
                    if (!_itemIdToSkin.ContainsKey(item.Id))
                        continue;

                    workshopid = (ulong)item.Id;
                }

                if (!m_SkinData.Data.TryGetValue(shortname, out Hash<ulong, SkinData> skins))
                    m_SkinData.Data.Add(shortname, skins = new Hash<ulong, SkinData>());

                if (!skins.TryGetValue(workshopid, out SkinData skin))
                {
                    skin = skins[workshopid] = new SkinData()
                    {
                        cost = GetCategoryPriceForShortname(shortname),
                        isDisabled = false,
                        permission = string.Empty,
                        isApproved = true
                    };

                    m_HasChanges = true;
                }
                else
                {
                    if (!skin.isApproved)
                    {
                        skin.isApproved = true;
                        m_HasChanges = true;
                    }
                }

                skin.Title = item.Name;
                skin.IsValid = true;
            }

            if (Configuration.Workshop.Enabled)
                CollectWorkshopSkins();
            else SendWorkshopQuery();
        }

        private void CollectWorkshopSkins()
        {
            foreach (KeyValuePair<string, Hash<ulong, SkinData>> skinEntry in m_SkinData.Data)
            {
                foreach (KeyValuePair<ulong, SkinData> kvp in skinEntry.Value)
                {
                    // Skip approved skins that were already validated by CollectApprovedSkins
                    if (kvp.Value.isApproved && kvp.Value.IsValid)
                        continue;
                    
                    // Include unvalidated skins (workshop skins mislabeled as approved, or explicitly workshop)
                    // These get validated via Steam Workshop API
                    if (!m_SkinsToLoad.Contains(kvp.Key))
                        m_SkinsToLoad.Add(kvp.Key);
                }
            }

            SendWorkshopQuery();
        }

        private void FinalizeSkinLoading()
        {
            if (m_HasChanges)
            {
                Debug.Log("[PlayerSkins] - The available skin list has been modified");
                m_SkinData.Save();
                m_HasChanges = false;
            }

            m_IsReady = true;

            Hash<string, HashSet<ulong>> skinList = new Hash<string, HashSet<ulong>>();
            foreach (KeyValuePair<string, Hash<ulong, SkinData>> kvp in m_SkinData.Data)
            {
                HashSet<ulong> skins = new HashSet<ulong>();

                foreach (ulong skin in kvp.Value.Keys)
                    skins.Add(skin);

                skinList.Add(kvp.Key, skins);
            }

            Interface.Oxide.CallHook("OnPlayerSkinsSkinsLoaded", skinList);

            Debug.Log("[PlayerSkins] - Skins processed and ready to use!");
        }
        
        private void SendWorkshopQuery(int page = 0, string perm = "")
        {
            if (m_SkinsToLoad.Count == 0)
            {
                FinalizeSkinLoading();
                return;
            }

            int totalPages = Mathf.CeilToInt((float)m_SkinsToLoad.Count / 100f);
            int index = page * 100;
            int limit = Mathf.Min((page + 1) * 100, m_SkinsToLoad.Count);
            string details = string.Format(ITEMS_BODY, Configuration.Workshop.SteamAPIKey, (limit - index));

            for (int i = index; i < limit; i++)
            {
                details += string.Format(ITEM_ENTRY, i - index, m_SkinsToLoad[i]);
            }

            try
            {
                webrequest.Enqueue(PUBLISHED_FILE_DETAILS, details, (code, response) => 
                    ServerMgr.Instance.StartCoroutine(ValidateRequiredSkins(code, response, page + 1, totalPages, false, perm)), this, RequestMethod.POST);
            }
            catch (Exception ex)
            {
                PrintError($"Failed to queue workshop skin validation request: {ex.Message}");
                FinalizeSkinLoading();
            }
        }

        private void SendWorkshopCollectionQuery(ulong collectionId, bool add, string perm = "")
        {            
            string details = string.Format(COLLECTION_BODY, Configuration.Workshop.SteamAPIKey, collectionId);

            try
            {
                webrequest.Enqueue(COLLECTION_DETAILS, details, (code, response) => 
                    ServerMgr.Instance.StartCoroutine(ProcessCollectionRequest(code, response, add, perm)), this, RequestMethod.POST);
            }
            catch { }
        }
       
        private IEnumerator ValidateRequiredSkins(int code, string response, int page, int totalPages, bool isCollection, string perm)
        {
            if (response != null && code == 200)
            {
                QueryResponse queryResponse = JsonConvert.DeserializeObject<QueryResponse>(response);
                if (queryResponse is { response.publishedfiledetails.Length: > 0 })
                {
                    Debug.Log($"[PlayerSkins] Processing workshop response. Page: {page} / {totalPages}");

                    foreach (PublishedFileDetails publishedFileDetails in queryResponse.response.publishedfiledetails)
                    {
                        if (publishedFileDetails.tags != null)
                        {
                            foreach (PublishedFileDetails.Tag tag in publishedFileDetails.tags)
                            {
                                if (string.IsNullOrEmpty(tag.tag))
                                    continue;

                                ulong workshopid = Convert.ToUInt64(publishedFileDetails.publishedfileid);

                                string adjTag = tag.tag.ToLower().Replace("skin", "").Replace(" ", "").Replace("-", "").Replace(".item", "");
                                if (m_WorkshopNameToShortname.ContainsKey(adjTag))
                                {                                    
                                    string shortname = m_WorkshopNameToShortname[adjTag];

                                    if (m_IgnoreItems.Contains(shortname))
                                        continue;

                                    bool isValid = IsValid(publishedFileDetails)/* || HasImage(shortname, workshopid)*/;

                                    if (isValid)
                                    {
                                        if (!m_SkinData.Data.TryGetValue(shortname, out Hash<ulong, SkinData> skins))
                                            m_SkinData.Data.Add(shortname, skins = new Hash<ulong, SkinData>());

                                        if (!skins.TryGetValue(workshopid, out SkinData skin))
                                        {
                                            skin = skins[workshopid] = new SkinData()
                                            {
                                                cost = GetCategoryPriceForShortname(shortname),
                                                isDisabled = false,
                                                permission = perm
                                            };

                                            m_HasChanges = true;
                                        }

                                        skin.Title = publishedFileDetails.title;
                                        //skin.URL = publishedFileDetails.preview_url;
                                        skin.IsValid = isValid;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            yield return CoroutineEx.waitForEndOfFrame;
            yield return CoroutineEx.waitForEndOfFrame;

            if (page < totalPages)
                SendWorkshopQuery(page, perm);
            else
                FinalizeSkinLoading();
        }

        private IEnumerator ProcessCollectionRequest(int code, string response, bool add, string perm)
        {
            if (response != null && code == 200)
            {
                Debug.Log($"[PlayerSkins] Processing collection response");

                CollectionQueryResponse collectionQuery = JsonConvert.DeserializeObject<CollectionQueryResponse>(response);
                if (collectionQuery == null || !(collectionQuery is CollectionQueryResponse))
                {
                    Puts("Failed to receive a valid workshop collection response");
                    yield break;
                }

                if (collectionQuery.response.resultcount == 0 || collectionQuery.response.collectiondetails == null ||
                    collectionQuery.response.collectiondetails.Length == 0 || collectionQuery.response.collectiondetails[0].result != 1)
                {
                    Puts("Failed to receive a valid workshop collection response");
                    yield break;
                }

                m_SkinsToLoad.Clear();
                foreach (CollectionChild child in collectionQuery.response.collectiondetails[0].children)
                {
                    try
                    {
                        m_SkinsToLoad.Add(Convert.ToUInt64(child.publishedfileid));
                    }
                    catch
                    {
                    }
                }

                if (m_SkinsToLoad.Count == 0)
                {
                    Puts("No valid skin ID's in the specified collection");
                    yield break;
                }

                if (add)
                    SendWorkshopQuery(0, perm);
                else RemoveSkins();
            }
            else Debug.Log($"[PlayerSkins] Collection response failed. Error code {code}");
        }

        private void RemoveSkins()
        {
            int removedCount = 0;
            for (int y = m_SkinData.Data.Count - 1; y >= 0; y--)
            {
                KeyValuePair<string, Hash<ulong, SkinData>> skin = m_SkinData.Data.ElementAt(y);

                for (int i = 0; i < m_SkinsToLoad.Count; i++)
                {
                    if (skin.Value.ContainsKey(m_SkinsToLoad[i]))
                    {
                        skin.Value.Remove(m_SkinsToLoad[i]);
                        removedCount++;
                    }
                }

            }

            m_SkinData.Save();
            Puts($"Removed {removedCount} skins");
        }
        #endregion

        #region API Helpers
        private bool ContainsKeyword(string title)
        {
            foreach (string keyword in Configuration.Workshop.Filter)
            {
                if (title.ToLower().Contains(keyword.ToLower()))
                    return true;
            }
            return false;
        }

        private bool IsValid(PublishedFileDetails item)
        {
            if (ContainsKeyword(item.title))
                return false;

            if (string.IsNullOrEmpty(item.preview_url))
                return false;

            if (item.tags == null)
                return false;

            return true;
        }

        private void GetSkinnableShortnames(List<string> list, BasePlayer player)
        {
            foreach (KeyValuePair<string, Hash<ulong, SkinData>> skin in m_SkinData.Data)
            {
                if (skin.Value.Count == 0 || !skin.Value.Any(x => x.Value.IsValid))
                    continue;

                if (Configuration.Shop.BlockedItems.Contains(skin.Key) || m_IgnoreItems.Contains(skin.Key))
                    continue;

                list.Add(skin.Key);
            }

            list.Sort(delegate (string a, string b)
            {
                string displayNameA = GetString(a, player);
                string displayNameB = GetString(b, player);

                return displayNameA.CompareTo(displayNameB);
            });
        }

        private void GetValidSkins(List<KeyValuePair<string, ulong>> list, UIUser uiUser, UserData userData)
        {
            bool hideVipSkins = Configuration.Shop.HideVIPSkins && !uiUser.Player.HasPermission(ADMIN_PERMISSION);
            
            if (!string.IsNullOrEmpty(uiUser.SearchFilter))
            {
                if (uiUser.ShowOwned)
                {
                    foreach (KeyValuePair<string, Hash<ulong, SkinData>> kvp in m_SkinData.Data)
                    {
                        foreach (KeyValuePair<ulong, SkinData> idData in kvp.Value)
                        {
                            if (!idData.Value.Title.Contains(uiUser.SearchFilter, CompareOptions.OrdinalIgnoreCase))
                                continue;
                            if (idData.Value.isDisabled)
                                continue; 
                            if (hideVipSkins && !string.IsNullOrEmpty(idData.Value.permission) && !uiUser.Player.HasPermission(idData.Value.permission))
                                continue;
                            // Include if purchased in shop OR owned via Steam
                            bool purchased = userData.purchasedSkins.TryGetValue(kvp.Key, out List<ulong> purchasedIds) && purchasedIds.Contains(idData.Key);
                            if (!purchased && !UserOwnsSkin(uiUser.Player, kvp.Key, idData.Key))
                                continue;
                            
                            list.Add(new KeyValuePair<string, ulong>(kvp.Key, idData.Key));
                        }
                    }
                }
                else
                {
                    foreach (KeyValuePair<string, Hash<ulong, SkinData>> kvp in m_SkinData.Data)
                    {
                        foreach (KeyValuePair<ulong, SkinData> idData in kvp.Value)
                        {
                            if (idData.Value.Title.Contains(uiUser.SearchFilter, CompareOptions.OrdinalIgnoreCase))
                            {
                                if (idData.Value.isDisabled)
                                    continue;

                                if ((uiUser.ShowAvailable || hideVipSkins) && !string.IsNullOrEmpty(idData.Value.permission) && !uiUser.Player.HasPermission(idData.Value.permission))
                                    continue;

                                if (Configuration.Workshop.ApprovedIfOwned && PlayerDlcApi.IsLoaded && !PlayerDlcApi.IsOwnedOrFreeSkin(uiUser.Player, idData.Key))
                                    continue;
                                
                                list.Add(new KeyValuePair<string, ulong>(kvp.Key, idData.Key));
                            }
                        }
                    }
                }
            }
            else
            {
                if (!m_SkinData.Data.TryGetValue(uiUser.ItemShortname, out Hash<ulong, SkinData> skinList))
                    return;

                if (uiUser.ShowOwned)
                {
                    HashSet<ulong> ownedIds = Pool.Get<HashSet<ulong>>();
                    if (userData.purchasedSkins.TryGetValue(uiUser.ItemShortname, out List<ulong> purchasedSkinIds))
                    {
                        List<ulong> skinIDs = Pool.Get<List<ulong>>();
                        skinIDs.AddRange(purchasedSkinIds);
                        if (Configuration.Workshop.ApprovedIfOwned && PlayerDlcApi.IsLoaded)
                            PlayerDlcApi.FilterOwnedOrFreeSkins(uiUser.Player, skinIDs);
                        foreach (ulong id in skinIDs)
                            ownedIds.Add(id);
                        Pool.FreeUnmanaged(ref skinIDs);
                    }
                    // Also include skins the player owns via Steam (not just purchased in shop)
                    if (PlayerDlcApi.IsLoaded)
                    {
                        foreach (KeyValuePair<ulong, SkinData> skin in skinList)
                        {
                            if (ownedIds.Contains(skin.Key))
                                continue;
                            if (skin.Value.isDisabled || !skin.Value.IsValid || m_ExcludedSkins.Data.Contains(skin.Key))
                                continue;
                            if (hideVipSkins && !string.IsNullOrEmpty(skin.Value.permission) && !uiUser.Player.HasPermission(skin.Value.permission))
                                continue;
                            if (PlayerDlcApi.IsPaidSkin(skin.Key) && PlayerDlcApi.IsOwnedOrFreeSkin(uiUser.Player, skin.Key))
                                ownedIds.Add(skin.Key);
                        }
                    }
                    foreach (ulong skinId in ownedIds)
                    {
                        if (skinList.TryGetValue(skinId, out SkinData skin))
                        {
                            if (!skin.IsValid || m_ExcludedSkins.Data.Contains(skinId) || skin.isDisabled)
                                continue;
                            if (hideVipSkins && !string.IsNullOrEmpty(skin.permission) && !uiUser.Player.HasPermission(skin.permission))
                                continue;
                            list.Add(new KeyValuePair<string, ulong>(uiUser.ItemShortname, skinId));
                        }
                    }
                    Pool.FreeUnmanaged(ref ownedIds);
                }
                else
                {
                    foreach (KeyValuePair<ulong, SkinData> skin in skinList)
                    {
                        if (!skin.Value.isDisabled && skin.Value.IsValid && !m_ExcludedSkins.Data.Contains(skin.Key))
                        {
                            if ((hideVipSkins || uiUser.ShowAvailable) && !string.IsNullOrEmpty(skin.Value.permission) && !uiUser.Player.HasPermission(skin.Value.permission))
                                continue;

                            if (Configuration.Workshop.ApprovedIfOwned && PlayerDlcApi.IsLoaded && !PlayerDlcApi.IsOwnedOrFreeSkin(uiUser.Player, skin.Key))
                                continue;
                            
                            list.Add(new KeyValuePair<string, ulong>(uiUser.ItemShortname, skin.Key));
                        }
                    }
                }
            }
        }
        #endregion
        
        #region Chat Commands
        private void RegisterChatCommands()
        {
        }

        internal void cmdAddSkin(BasePlayer player, string command, string[] args)
        {
            if (player == null || !player.IsValid())
                return;
            if (!player.HasPermission(ADD_SKIN_PERMISSION))
            {
                SendReply(player, "You do not have permission to add workshop skins.");
                return;
            }
            if (string.IsNullOrEmpty(Configuration.Workshop.SteamAPIKey))
            {
                SendReply(player, "Workshop skin adding is not configured (no Steam API key).");
                return;
            }
            if (!Configuration.Workshop.Enabled)
            {
                SendReply(player, "Workshop skins are disabled in the server config.");
                return;
            }
            if (args == null || args.Length == 0)
            {
                SendReply(player, $"Usage: /{Configuration.Commands.AddSkinCommand ?? "addskin"} <workshop skin ID> [ID2] [ID3] ...");
                SendReply(player, "Add one or more workshop skin IDs; the skin will be added to the shop for everyone at the same price as other skins for that item.");
                return;
            }
            m_SkinsToLoad.Clear();
            for (int i = 0; i < args.Length; i++)
            {
                if (ulong.TryParse(args[i], out ulong skinId))
                    m_SkinsToLoad.Add(skinId);
                else
                    SendReply(player, $"Invalid skin ID: {args[i]}");
            }
            if (m_SkinsToLoad.Count == 0)
            {
                SendReply(player, "No valid workshop skin IDs entered.");
                return;
            }
            SendWorkshopQuery();
            SendReply(player, $"Queued {m_SkinsToLoad.Count} skin(s) for import. They will be added to the shop for everyone at the category price for each item.");
        }
        internal void cmdSkin(BasePlayer player, string command, string[] args)
        {
            if (player == null || !player.IsValid())
                return;
            if (!m_IsReady)
            {
                SendReply(player, "Waiting for item icons to finish downloading...");
                return;
            }

            if (!m_UserData.Data.ContainsKey(player.userID))
                m_UserData.Data.Add(player.userID, new UserData());

            if (args == null || args.Length == 0)
            {
                if (!player.HasPermission(RESKIN_PERMISSION))
                {
                    SendReply(player, "You do not have permission to use this command");
                    return;
                }

                if (Configuration.Shop.GiveItemOnPurchase)
                {
                    SendReply(player, "Reskin menu is disabled when 'Give item on purchase' is enabled in the skin shop config.");
                    return;
                }

                if (Configuration.Reskin.DisableCommand)
                {
                    SendReply(player, "You can only access the re-skin menu via a re-skin NPC");
                    return;
                }
                // 0.2f delay allows previous loot state to clear before opening
                timer.In(0.2f, () => CreateReskinLootBox(player));
            }
            else
            {
                if (args[0].ToLower() != "shop")
                {
                    SendReply(player, "/skin - Open the re-skin menu");
                    SendReply(player, "/skin shop - Open the skin shop");
                    return;
                }

                if (!player.HasPermission(SHOP_PERMISSION))
                {
                    SendReply(player, "You do not have permission to use this command");
                    return;
                }

                if (Configuration.Shop.DisableCommand)
                {
                    SendReply(player, "You can only access the skin shop menu via a skin shop NPC");
                    return;
                }

                BaseContainer root = BaseContainer.Create(PS_UI_MOUSE, Layer.Hud, UIAnchor.Center, Offset.Default)
                    .NeedsCursor()
                    .NeedsKeyboard();
			    
                ChaosUI.Show(player, root);
                
                OpenSkinShop(player);
            }
        }

        internal void cmdReSkin(BasePlayer player, string command, string[] args)
        {
            if (!m_IsReady)
            {
                SendReply(player, "Waiting for item icons to finish downloading...");
                return;
            }

            if (!m_UserData.Data.ContainsKey(player.userID))
                m_UserData.Data.Add(player.userID, new UserData());

            if (!player.HasPermission(RESKIN_PERMISSION))
            {
                SendReply(player, "You do not have permission to use this command");
                return;
            }

            if (Configuration.Shop.GiveItemOnPurchase)
                return;

            if (Configuration.Reskin.DisableCommand)
            {
                SendReply(player, "You can only access the re-skin menu via a re-skin NPC");
                return;
            }
            // 0.2f delay before opening reskin loot
            timer.In(0.2f, () => CreateReskinLootBox(player));
        }

        internal void cmdSkinShop(BasePlayer player, string command, string[] args)
        {
            if (!m_IsReady)
            {
                SendReply(player, "Waiting for item icons to finish downloading...");
                return;
            }

            if (!m_UserData.Data.ContainsKey(player.userID))
                m_UserData.Data.Add(player.userID, new UserData());
            
            if (!player.HasPermission(SHOP_PERMISSION))
            {
                SendReply(player, "You do not have permission to use this command");
                return;
            }

            if (Configuration.Shop.DisableCommand)
            {
                SendReply(player, "You can only access the skin shop menu via a skin shop NPC");
                return;
            }
            
            BaseContainer root = BaseContainer.Create(PS_UI_MOUSE, Layer.Hud, UIAnchor.Center, Offset.Default)
                .NeedsCursor()
                .NeedsKeyboard();
			    
            ChaosUI.Show(player, root);

            OpenSkinShop(player);
        }
        #endregion

        #region Console Commands
        internal void ccmdSkinManager(ConsoleSystem.Arg arg)
        {            
            if (arg.Connection != null)
            {
                if (arg.Connection.authLevel < 2)
                {
                    SendReply(arg, "You do not have permission to use this command");
                    return;
                }
            }

            if (string.IsNullOrEmpty(Configuration.Workshop.SteamAPIKey))
            {
                SendReply(arg, "No steam API key has been set");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 3)
            {
                SendReply(arg, "playerskins.skins import skin <skin ID> - Import the specified workshop skin using its workshop ID. Type multiple ID's here to process them all at once");
                SendReply(arg, "playerskins.skins import collection <collection ID> <opt:permission> - Import the specified workshop skin collection. Optional add a permission to add to any new skins collected");
                SendReply(arg, "playerskins.skins remove skin <skin ID> - Remove the specified skin from the skin shop. Type multiple ID's here to process them all at once");
                SendReply(arg, "playerskins.skins remove collection <collection ID> - Remove the specified skin collection from the skin shop");
                return;
            }

            if (!Configuration.Workshop.Enabled)
            {
                SendReply(arg, "You have workshop disabled in your config. The playerskins.skins commands are unavailable when workshop is disabled");
                return;
            }

            switch (arg.GetString(0).ToLower())
            {
                case "import":
                    {
                        if (arg.Args.Length < 2)
                        {
                            SendReply(arg, "Invalid syntax. Type 'playerskins.skins' for more information");
                            return;
                        }

                        if (arg.Args.Length < 3)
                        {
                            SendReply(arg, "You must enter a workshop skin ID or collection ID");
                            return;
                        }

                        if (arg.GetString(1).ToLower() == "skin")
                        {
                            m_SkinsToLoad.Clear();

                            for (int i = 2; i < arg.Args.Length; i++)
                            {
                                if (ulong.TryParse(arg.GetString(i), out ulong skinId))
                                    m_SkinsToLoad.Add(skinId);
                                else SendReply(arg, $"Invalid skin ID : {arg.GetString(i)}");
                            }

                            if (m_SkinsToLoad.Count > 0)
                                SendWorkshopQuery();
                            else SendReply(arg, "No valid ID's entered");
                        }
                        else if (arg.GetString(1).ToLower() == "collection")
                        {
                            m_SkinsToLoad.Clear();
                            string perm = arg.Args.Length == 4 ? arg.GetString(3) : string.Empty;
                            if (ulong.TryParse(arg.GetString(2), out ulong collectionId))
                            {
                                if (!string.IsNullOrEmpty(perm))
                                {
                                    if (!perm.StartsWith("playerskins."))
                                        perm = "playerskins." + perm;

                                    if (!Configuration.Shop.Permissions.Contains(perm))
                                    {
                                        Configuration.Shop.Permissions.Add(perm);
                                        SaveConfiguration();
                                        
                                        PermissionsBridge.RegisterPermission(perm);
                                    }
                                }
                                
                                SendWorkshopCollectionQuery(collectionId, true, perm);
                            }
                            else SendReply(arg, "Invalid collection ID entered");
                        }
                        else
                        {
                            SendReply(arg, "Invalid syntax. Type 'playerskins.skins' for more information");
                            return;
                        }
                    }
                    return;
                case "remove":
                    {
                        if (arg.Args.Length < 2)
                        {
                            SendReply(arg, "Invalid syntax. Type 'playerskins.skins' for more information");
                            return;
                        }

                        if (arg.Args.Length < 3)
                        {
                            SendReply(arg, "You must enter a workshop skin ID or collection ID");
                            return;
                        }

                        if (arg.GetString(1).ToLower() == "skin")
                        {
                            m_SkinsToLoad.Clear();
                            for (int i = 2; i < arg.Args.Length; i++)
                            {
                                if (ulong.TryParse(arg.GetString(i), out ulong skinId))
                                    m_SkinsToLoad.Add(skinId);
                                else SendReply(arg, $"Invalid skin ID : {arg.GetString(i)}");
                            }

                            RemoveSkins();
                        }
                        else if (arg.GetString(1).ToLower() == "collection")
                        {
                            if (ulong.TryParse(arg.GetString(2), out ulong collectionId))
                            {
                                SendWorkshopCollectionQuery(collectionId, false);
                            }
                            else SendReply(arg, "Invalid collection ID entered");
                        }
                        else
                        {
                            SendReply(arg, "Invalid syntax. Type 'playerskins.skins' for more information");
                            return;
                        }
                    }
                    return;
                default:
                    SendReply(arg, "Invalid syntax!");
                    break;
            }
        }

        internal void ccmdSetSkinPrice(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
            {
                if (arg.Connection.authLevel < 2)
                {
                    SendReply(arg, "You do not have permission to use this command");
                    return;
                }
            }

            if (arg.Args == null || arg.Args.Length != 2)
            {
                SendReply(arg, "playerskins.setprice <item shortname> <amount> - Set the price for all skins for the specified item");
                SendReply(arg, "playerskins.setprice all <amount> - Set the price for all skins for all items");
                return;
            }

            if (!int.TryParse(arg.GetString(1), out int amount))
            {
                SendReply(arg, "You must enter a number to set the price");
                return;
            }

            string shortname = arg.GetString(0);

            if (shortname.ToLower() == "all")
            {
                foreach (Hash<ulong, SkinData> item in m_SkinData.Data.Values)
                {
                    foreach (SkinData skin in item.Values)
                        skin.cost = amount;
                }

                SendReply(arg, $"You have set all skin costs to {amount}");
            }
            else
            {
                if (!m_SkinData.Data.TryGetValue(shortname, out Hash<ulong, SkinData> data))
                {
                    SendReply(arg, $"Either an invalid shortname was entered, or there are no skins for the specified item : {shortname}");
                    return;
                }

                foreach (SkinData skin in data.Values)
                    skin.cost = amount;

                SendReply(arg, $"You have set all {shortname} skin costs to {amount}");
            }

            m_SkinData.Save();
        }

        internal void ccmdGiveSkin(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
            {
                if (arg.Connection.authLevel < 2)
                {
                    SendReply(arg, "You do not have permission to use this command");
                    return;
                }
            }

            if (arg.Args == null || arg.Args.Length != 3)
            {
                SendReply(arg, "playerskins.giveskin <userID> <item shortname> <skinID> - Give a user the specified skin");
                return;
            }

            ulong userID = arg.GetULong(0);
            string shortname = arg.GetString(1);
            ulong skinID = arg.GetULong(2);

            if (userID == 0UL)
            {
                SendReply(arg, "The user ID you entered is invalid");
                return;
            }

            if (!m_UserData.Data.ContainsKey(userID))
            {
                SendReply(arg, "The specified user does not have any stored data");
                return;
            }

            if (!ItemManager.itemDictionaryByName.ContainsKey(shortname))
            {
                SendReply(arg, "The item shortname you entered is invalid");
                return;
            }

            if (skinID == 0UL)
            {
                SendReply(arg, "The skin ID you entered is invalid");
                return;
            }

            if (!m_SkinData.Data.TryGetValue(shortname, out Hash<ulong, SkinData> itemData) || !itemData.ContainsKey(skinID))
            {
                SendReply(arg, "The skin ID you entered is not available in the skin shop");
                return;
            }

            if (!m_UserData.Data[userID].purchasedSkins.TryGetValue(shortname, out List<ulong> skins))            
                skins = m_UserData.Data[userID].purchasedSkins[shortname] = new List<ulong>();

            if (skins.Contains(skinID))
            {
                SendReply(arg, "The user has already purchased that skin");
                return;
            }

            skins.Add(skinID);

            m_UserData.Save();
        }
        #endregion
        
        #region UI
        public enum DisplayMode { None, Full, Minimalist }
        
        private CommandCallbackHandler m_CallbackHandler;

        private const string PS_UI = "playerskins.ui";
        private const string PS_UI_MOUSE = "playerskins.ui.mouse";
        private const string PS_UI_POPUP = "playerskins.ui.popup";
        private const string PS_LOOT_PANEL = "generic_resizable";
        private const string PS_OVERLAY = "playerskins.overlay";
        private const string PS_PAGE = "playerskins.pages";
        private const string PS_SEARCH = "playerskins.search";
        private const string PS_HEADER = "playerskins.header";
        private const int PS_RESKIN_SLOTS = 48;
        
        private string m_MagnifyImage;

        private readonly Hash<ulong, UIUser> m_UIUsers = new Hash<ulong, UIUser>();
        private readonly Hash<ulong, ReskinLootHandler> m_ActiveReskinLoot = new Hash<ulong, ReskinLootHandler>();

        private void SetupUIComponents()
        {
            m_CallbackHandler = new CommandCallbackHandler(this);

            m_BackgroundStyle = new Style
            {
                ImageColor = new Color(Configuration.Colors.Background.Hex, Configuration.Colors.Background.Alpha),
                Material = Materials.BackgroundBlur,
                Sprite = Sprites.Background_Rounded,
                ImageType = Image.Type.Tiled
            };

            m_PanelStyle = new Style
            {
                ImageColor = new Color(Configuration.Colors.Panel.Hex, Configuration.Colors.Panel.Alpha),
                Sprite = Sprites.Background_Rounded,
                ImageType = Image.Type.Tiled
            };
            
            m_OwnedPanelStyle = new Style
            {
                ImageColor = new Color(Configuration.Colors.Highlight.Hex, 0.35f),
                Sprite = Sprites.Background_Rounded,
                ImageType = Image.Type.Tiled
            };

            m_ButtonStyle = new Style
            {
                ImageColor = new Color(Configuration.Colors.Button.Hex, Configuration.Colors.Button.Alpha),
                Sprite = Sprites.Background_Rounded,
                ImageType = Image.Type.Tiled,
                Alignment = TextAnchor.MiddleCenter,
                FontSize = 14
            };
            
            m_ButtonDisabledStyle = new Style
            {
	            ImageColor = new Color(Configuration.Colors.Button.Hex, 0.8f),
	            Sprite = Sprites.Background_Rounded,
	            ImageType = Image.Type.Tiled,
	            Alignment = TextAnchor.MiddleCenter,
	            FontColor = new Color(1f, 1f, 1f, 0.2f),
	            FontSize = 14
            };
            
            m_TitleStyle = new Style
            {
                FontSize = 18,
                Font = Font.PermanentMarker,
                Alignment = TextAnchor.MiddleLeft,
                WrapMode = VerticalWrapMode.Overflow
            };
            
            m_ToggleLabelStyle = new Style
            {
                FontSize = 40,
                Alignment = TextAnchor.MiddleCenter,
                WrapMode = VerticalWrapMode.Overflow,
                FontColor = new Color(Configuration.Colors.Highlight.Hex, Configuration.Colors.Highlight.Alpha)
            };
            
            m_OutlineGreen = new OutlineComponent(new Color(Configuration.Colors.Highlight.Hex, Configuration.Colors.Highlight.Alpha));
            m_OutlineRed = new OutlineComponent(new Color(Configuration.Colors.Close.Hex, Configuration.Colors.Close.Alpha));
        }
        
        #region Styles
        private Style m_BackgroundStyle;
        private Style m_PanelStyle;
        private Style m_OwnedPanelStyle;
        private Style m_ButtonStyle;
        private Style m_ButtonDisabledStyle;
        private Style m_TitleStyle;
        private Style m_ToggleLabelStyle;
        
        private OutlineComponent m_OutlineGreen;
        private OutlineComponent m_OutlineRed;
        #endregion
        
        #region Layout Groups
        private VerticalLayoutGroup m_ItemListLayout = new VerticalLayoutGroup(18)
        {
	        Area = new Area(-70f, -253.5f, 70f, 253.5f),
	        Spacing = new Spacing(5f, 5f),
	        Padding = new Padding(5f, 0f, 5f, 0f),
	        Corner = Corner.TopLeft,
        };
        
        private readonly GridLayoutGroup m_ItemGridFull = new GridLayoutGroup(10, 6, Axis.Horizontal)
        {
            Area = new Area(-462.5f, -270f, 462.5f, 270f),
            Spacing = new Spacing(5f, 5f),
            Padding = new Padding(5f, 5f, 5f, 5f),
            Corner = Corner.Centered,
        };

        private readonly GridLayoutGroup m_ItemGridMinimal = new GridLayoutGroup(3, 6, Axis.Horizontal)
        {
            Area = new Area(-142.5f, -270f, 142.5f, 270f),
            Spacing = new Spacing(5f, 5f),
            Padding = new Padding(5f, 5f, 5f, 5f),
            Corner = Corner.Centered,
        };
        
        private GridLayoutGroup m_ReskinItemGrid = new GridLayoutGroup(5, 2, Axis.Horizontal)
        {
            Area = new Area(-185.5f, -77.5f, 185.5f, 77.5f),
            Spacing = new Spacing(5f, 5f),
            Padding = new Padding(5f, 5f, 5f, 5f),
            Corner = Corner.Centered,
        };
        
        private VerticalLayoutGroup m_PermissionLayout = new VerticalLayoutGroup()
        {
            Area = new Area(-120f, -272.5f, 120f, 272.5f),
            Spacing = new Spacing(0f, 5f),
            Padding = new Padding(0f, 0f, 0f, 0f),
            Corner = Corner.TopLeft,
            FixedSize = new Vector2(240, 20),
            FixedCount = new Vector2Int(1, 22)
        };
        #endregion

        private static int ClampLayoutPage(BaseLayoutGroup layoutGroup, int page, int count)
        {
            if (page <= 0 || count <= 0)
                return 0;

            layoutGroup.RecalculateSize();
            int perPage = Math.Max(1, layoutGroup.PerPage);
            int maxPage = (count - 1) / perPage;
            return page > maxPage ? maxPage : page;
        }
        
        #region UI User
        private class UIUser
        {
	        public readonly BasePlayer Player;

	        public DisplayMode DisplayMode;
	        public string ItemShortname = string.Empty;
	        public int CategoryPage = 0;
	        public int GridPage = 0;
	        public string SearchFilter = string.Empty;
            public bool ShowOwned = false;
            public bool ShowAvailable = false;
            public bool AdminMode = false;
            
	        public UIUser(BasePlayer player, DisplayMode userDisplayMode)
	        {
		        this.Player = player;
                DisplayMode = m_ForcedDisplayMode != DisplayMode.None ? m_ForcedDisplayMode : userDisplayMode;
            }

	        public void Reset()
	        {
                CategoryPage = 0;
                GridPage = 0;
		        ItemShortname = string.Empty;
		        SearchFilter = string.Empty;
                ShowOwned = false;
                ShowAvailable = false;
                AdminMode = false;
            }
        }
        #endregion
        
        #region UI

        private readonly Offset m_FullOffset = new Offset(-540f, -310f, 540f, 310f);
        private readonly Offset m_MinimalOffset = new Offset(-540f, -310f, -100f, 310f);
        
        #region Skin Shop
        private void OpenSkinShop(BasePlayer player)
        {
            UserData userData = m_UserData.Data[player.userID];

            if (!m_UIUsers.TryGetValue(player.userID, out UIUser uiUser))
		        uiUser = m_UIUsers[player.userID] = new UIUser(player, userData?.displayMode ?? DisplayMode.Full);
	        
	        // Layer.HudMenu, FullStretch root, DestroyExisting() for stable UI
	        BaseContainer root = BaseContainer.Create(PS_UI, Layer.HudMenu, UIAnchor.FullStretch, Offset.zero)
		        .WithChildren(parent =>
		        {
			        BaseContainer menu = BaseContainer.Create(parent, UIAnchor.FullStretch, new Offset(0f, 0f, 0f, 0f));
			        ImageContainer.Create(menu, UIAnchor.Center, uiUser.DisplayMode == DisplayMode.Full ? m_FullOffset : m_MinimalOffset)
				        .WithStyle(m_BackgroundStyle)
				        .WithChildren(panel =>
				        {
					        CreateTitleBar(uiUser, panel);
					        CreateItemSelector(uiUser, panel);
					        CreateItemGrid(uiUser, panel, userData);
				        });
		        })
		        .DestroyExisting();
                
			ChaosUI.Show(player, root);
        }

        private void CreateTitleBar(UIUser uiUser, BaseContainer parent)
        {
	        ImageContainer.Create(parent, UIAnchor.TopStretch, new Offset(5f, -35f, -5f, -5f))
		        .WithStyle(m_PanelStyle)
		        .WithChildren(titleBar =>
		        {
			        TextContainer.Create(titleBar, UIAnchor.CenterLeft, new Offset(5f, -15f, 205f, 15f))
				        .WithText(Title)
				        .WithStyle(m_TitleStyle);

			        ImageContainer.Create(titleBar, UIAnchor.CenterRight, new Offset(-55f, -10f, -5f, 10f))
				        .WithStyle(m_ButtonStyle)
				        .WithOutline(m_OutlineRed)
				        .WithChildren(exit =>
				        {
					        TextContainer.Create(exit, UIAnchor.FullStretch, Offset.zero)
						        .WithText(GetString("UI.Exit", uiUser.Player))
						        .WithAlignment(TextAnchor.MiddleCenter);

					        ButtonContainer.Create(exit, UIAnchor.FullStretch, Offset.zero)
						        .WithColor(Color.Clear)
						        .WithCallback(m_CallbackHandler, arg =>
						        {
							        ChaosUI.Destroy(uiUser.Player, PS_UI);
							        ChaosUI.Destroy(uiUser.Player, PS_UI_POPUP);
                                    ChaosUI.Destroy(uiUser.Player, PS_UI_MOUSE);
							        m_UIUsers.Remove(uiUser.Player.userID);
						        }, $"{uiUser.Player.userID}.exit");

				        });

                    if (m_ForcedDisplayMode == DisplayMode.None)
                    {
                        // Toggle small big UI
                        ImageContainer.Create(titleBar, UIAnchor.CenterRight, new Offset(-90f, -10f, -60f, 10f))
                            .WithStyle(m_ButtonStyle)
                            .WithChildren(backButton =>
                            {
                                TextContainer.Create(backButton, UIAnchor.FullStretch, Offset.zero)
                                    .WithText(uiUser.DisplayMode == DisplayMode.Full ? "<<<" : ">>>")
                                    .WithAlignment(TextAnchor.MiddleCenter);

                                ButtonContainer.Create(backButton, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg =>
                                    {
                                        uiUser.GridPage = 0;
                                        uiUser.DisplayMode = uiUser.DisplayMode == DisplayMode.Full ? DisplayMode.Minimalist : DisplayMode.Full;
                                        OpenSkinShop(uiUser.Player);
                                    }, $"{uiUser.Player.userID}.displaymode");
                            });
                    }
                });
        }

        private void CreateItemSelector(UIUser uiUser, BaseContainer parent)
        {
	        ImageContainer.Create(parent, UIAnchor.LeftStretch, new Offset(5f, 5f, 145f, -40f))
		        .WithStyle(m_PanelStyle)
		        .WithChildren(itemMenu =>
                {
                    List<string> list = Facepunch.Pool.Get<List<string>>();
                    GetSkinnableShortnames(list, uiUser.Player);
                    uiUser.CategoryPage = ClampLayoutPage(m_ItemListLayout, uiUser.CategoryPage, list.Count);
                    
			        ImageContainer.Create(itemMenu, UIAnchor.TopCenter, new Offset(-65f, -28.44444f, 65f, -5.000017f))
				        .WithStyle(uiUser.CategoryPage > 0 ? m_ButtonStyle : m_ButtonDisabledStyle)
				        .WithChildren(back =>
                        {
                            TextContainer.Create(back, UIAnchor.FullStretch, Offset.zero)
                                .WithText(GetString("UI.Button.Up", uiUser.Player))
                                .WithStyle(uiUser.CategoryPage > 0 ? m_ButtonStyle : m_ButtonDisabledStyle);

                            if (uiUser.CategoryPage > 0)
                            {
                                ButtonContainer.Create(back, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg =>
                                    {
                                        uiUser.CategoryPage--;
                                        OpenSkinShop(uiUser.Player);
                                    }, $"{uiUser.Player.UserIDString}.category.back");
                            }
                        });

			        BaseContainer.Create(itemMenu, UIAnchor.FullStretch, new Offset(0f, 34f, 0f, -34f))
				        .WithLayoutGroup(m_ItemListLayout, list, uiUser.CategoryPage, (int i, string t, BaseContainer itemList, UIAnchor anchor, Offset offset) =>
				        {
					        BaseContainer button = ImageContainer.Create(itemList, anchor, offset)
						        .WithStyle(m_ButtonStyle)
						        .WithChildren(commands =>
						        {
							        TextContainer.Create(commands, UIAnchor.FullStretch, Offset.zero)
								        .WithSize(13)
								        .WithText(GetString(t, uiUser.Player))
								        .WithAlignment(TextAnchor.MiddleCenter);

							        ButtonContainer.Create(commands, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Color.Clear)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            uiUser.GridPage = 0;
                                            uiUser.SearchFilter = string.Empty;
                                            uiUser.ItemShortname = t;
                                            OpenSkinShop(uiUser.Player);
                                        }, $"{uiUser.Player.UserIDString}.category.{t}");

						        });

                            if (t == uiUser.ItemShortname)
                                button.WithOutline(m_OutlineGreen);
                        });

                    bool hasNextPage = m_ItemListLayout.HasNextPage(uiUser.CategoryPage, list.Count);
                    
			        ImageContainer.Create(itemMenu, UIAnchor.BottomCenter, new Offset(-65f, 5.000002f, 65f, 28.44446f))
				        .WithStyle(hasNextPage ? m_ButtonStyle : m_ButtonDisabledStyle)
				        .WithChildren(next =>
				        {
					        TextContainer.Create(next, UIAnchor.FullStretch, Offset.zero)
						        .WithText(GetString("UI.Button.Down", uiUser.Player))
						        .WithStyle(hasNextPage ? m_ButtonStyle : m_ButtonDisabledStyle);

                            if (hasNextPage)
                            {
                                ButtonContainer.Create(next, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg =>
                                    {
                                        uiUser.CategoryPage++;
                                        OpenSkinShop(uiUser.Player);
                                    }, $"{uiUser.Player.UserIDString}.category.next");
                            }
                        });
                    
                    Facepunch.Pool.FreeUnmanaged(ref list);
                });
        }
       
        private void CreateItemGrid(UIUser uiUser, BaseContainer parent, UserData userData)
        {
            int count = 0;
            if (string.IsNullOrEmpty(uiUser.ItemShortname) && string.IsNullOrEmpty(uiUser.SearchFilter))
            {
                TextContainer.Create(parent, UIAnchor.FullStretch, new Offset(150f, 40f, -5f, -40f))
                    .WithText(GetString("UI.NoShortname", uiUser.Player))
                    .WithAlignment(TextAnchor.MiddleCenter);
            }
            else
            {
                List<KeyValuePair<string, ulong>> list = Facepunch.Pool.Get<List<KeyValuePair<string, ulong>>>();
                BaseLayoutGroup itemGridLayout = uiUser.DisplayMode == DisplayMode.Full ? m_ItemGridFull : m_ItemGridMinimal;

                if (!string.IsNullOrEmpty(uiUser.ItemShortname) || !string.IsNullOrEmpty(uiUser.SearchFilter))
                    GetValidSkins(list, uiUser, userData);

                uiUser.GridPage = ClampLayoutPage(itemGridLayout, uiUser.GridPage, list.Count);

                if (list.Count == 0)
                {
                    TextContainer.Create(parent, UIAnchor.FullStretch, new Offset(150f, 40f, -5f, -40f))
                        .WithText(GetString("UI.NoSkinsFound", uiUser.Player))
                        .WithAlignment(TextAnchor.MiddleCenter);
                }
                else
                {
                    count = list.Count;
                    
                    ImageContainer.Create(parent, UIAnchor.FullStretch, new Offset(150f, 40f, -5f, -40f))
                        .WithStyle(m_PanelStyle)
                        .WithLayoutGroup(itemGridLayout, list, uiUser.GridPage, (int i, KeyValuePair<string, ulong> t, BaseContainer itemGrid, UIAnchor anchor, Offset offset) =>
                        {
                            ImageContainer.Create(itemGrid, anchor, offset)
                                .WithStyle(((!Configuration.Shop.GiveItemOnPurchase && userData.IsOwned(t.Key, t.Value)) || UserOwnsSkin(uiUser.Player, t.Key, t.Value)) && !uiUser.ShowOwned ? m_OwnedPanelStyle : m_PanelStyle)
                                .WithChildren(template =>
                                {
                                    ImageContainer.Create(template, UIAnchor.Center, new Offset(-42f, -42f, 42f, 42f))
                                        .WithIcon(m_ShortnameToItemId[t.Key], t.Value);

                                    ButtonContainer.Create(template, UIAnchor.FullStretch, Offset.zero)
                                        .WithColor(Color.Clear)
                                        .WithCallback(m_CallbackHandler, arg => { CreateItemView(uiUser, userData, t); }, $"{uiUser.Player.userID}.selectskin.{i}");
                                });
                        });
                }
                
                Pool.FreeUnmanaged(ref list);
            }

            CreateFooterBar(uiUser, parent, count);
        }

        private void CreateFooterBar(UIUser uiUser, BaseContainer parent, int listCount)
        {
            ImageContainer.Create(parent, UIAnchor.BottomStretch, new Offset(150f, 5f, -5f, 35f))
                .WithStyle(m_PanelStyle)
                .WithChildren(footer =>
                {
                    ImageContainer.Create(footer, UIAnchor.CenterLeft, new Offset(5f, -10f, 35f, 10f))
                        .WithStyle(uiUser.GridPage > 0 ? m_ButtonStyle : m_ButtonDisabledStyle)
                        .WithChildren(backButton =>
                        {
                            TextContainer.Create(backButton, UIAnchor.FullStretch, Offset.zero)
                                .WithText("<<<")
                                .WithStyle(uiUser.GridPage > 0 ? m_ButtonStyle : m_ButtonDisabledStyle);

                            if (uiUser.GridPage > 0)
                            {
                                ButtonContainer.Create(backButton, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg =>
                                    {
                                        uiUser.GridPage--;
                                        OpenSkinShop(uiUser.Player);
                                    }, $"{uiUser.Player.UserIDString}.grid.back");

                            }
                        });

                    bool hasNextPage = (uiUser.DisplayMode == DisplayMode.Full ? m_ItemGridFull : m_ItemGridMinimal).HasNextPage(uiUser.GridPage, listCount);

                    ImageContainer.Create(footer, UIAnchor.CenterRight, new Offset(-35f, -10f, -5f, 10f))
                        .WithStyle(m_ButtonStyle)
                        .WithChildren(nextButton =>
                        {
                            TextContainer.Create(nextButton, UIAnchor.FullStretch, Offset.zero)
                                .WithText(">>>")
                                .WithStyle(hasNextPage ? m_ButtonStyle : m_ButtonDisabledStyle);

                            if (hasNextPage)
                            {
                                ButtonContainer.Create(nextButton, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg =>
                                    {
                                        uiUser.GridPage++;
                                        OpenSkinShop(uiUser.Player);
                                    }, $"{uiUser.Player.UserIDString}.grid.next");
                            }
                        });
                    
                    TextContainer.Create(footer, UIAnchor.CenterLeft, new Offset(45f, -10f, 205f, 10f))
                        .WithText(FormatString("UI.Balance", uiUser.Player, GetUserBalance(uiUser.Player), GetString(m_CurrencyType.ToString(), uiUser.Player)))
                        .WithAlignment(TextAnchor.MiddleLeft);

                    BaseContainer minimalFooter = null;
                    bool isMinimalMode = uiUser.DisplayMode == DisplayMode.Minimalist;
                    
                    if (isMinimalMode)
                    {
                        minimalFooter = ImageContainer.Create(parent, UIAnchor.BottomStretch, new Offset(0f, -35f, 0f, 5f))
                            .WithColor(m_BackgroundStyle.ImageColor)
                            .WithMaterial(Materials.BackgroundBlur)
                            .WithImageType(Image.Type.Tiled)
                            .WithChildren(minimal =>
                            {
                                ImageContainer.Create(minimal, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -5f))
                                    .WithStyle(m_PanelStyle);
                            });
                    }

                    CreateFooterSearchBar(uiUser, uiUser.DisplayMode == DisplayMode.Full ? footer : minimalFooter, isMinimalMode);
                    CreateFooterToggles(uiUser, uiUser.DisplayMode == DisplayMode.Full ? footer : minimalFooter, isMinimalMode);
                });
        }

        private void CreateFooterSearchBar(UIUser uiUser, BaseContainer parent, bool minimal)
        {
            if (!string.IsNullOrEmpty(m_MagnifyImage))
            {
                RawImageContainer.Create(parent, UIAnchor.CenterRight, minimal ? new Offset(-235f, -10f, -215f, 10f) : new Offset(-265f, -10f, -245f, 10f))
                    .WithPNG(m_MagnifyImage);
            }

            ImageContainer.Create(parent, UIAnchor.CenterRight, minimal ? new Offset(-210f, -10f, -10f, 10f) : new Offset(-240f, -10f, -40f, 10f))
                .WithStyle(m_ButtonStyle)
                .WithChildren(searchInput =>
                {
                    InputFieldContainer.Create(searchInput, UIAnchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
                        .WithText(uiUser.SearchFilter)
                        .WithAlignment(TextAnchor.MiddleLeft)
                        .WithCallback(m_CallbackHandler, arg =>
                        {
                            uiUser.SearchFilter = arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)) : string.Empty;
                            uiUser.GridPage = 0;
                            OpenSkinShop(uiUser.Player);
                        }, $"{uiUser.Player.UserIDString}.footer.search.input");
                });
        }

        private void CreateFooterToggles(UIUser uiUser, BaseContainer parent, bool minimal)
        {
            ImageContainer.Create(parent, UIAnchor.CenterLeft, minimal ? new Offset(10f, -10f, 30f, 10f) : new Offset(215f, -10f, 235f, 10f))
                .WithStyle(m_ButtonStyle)
                .WithChildren(ownedToggle =>
                {
                    if (uiUser.ShowAvailable)
                    {
                        TextContainer.Create(ownedToggle, UIAnchor.FullStretch, Offset.zero)
                            .WithText("•")
                            .WithStyle(m_ToggleLabelStyle);
                    }

                    ButtonContainer.Create(ownedToggle, UIAnchor.FullStretch, Offset.zero)
                        .WithColor(Color.Clear)
                        .WithCallback(m_CallbackHandler, arg =>
                            {
                                uiUser.ShowAvailable = !uiUser.ShowAvailable;
                                OpenSkinShop(uiUser.Player);
                            }, $"{uiUser.Player.UserIDString}.toggleavailable");

                    TextContainer.Create(ownedToggle, UIAnchor.CenterRight, new Offset(5f, -10f, 65f, 10f))
                        .WithText(GetString("UI.Popup.Available", uiUser.Player))
                        .WithAlignment(TextAnchor.MiddleLeft);

                });

            if (!Configuration.Shop.GiveItemOnPurchase)
            {
                ImageContainer.Create(parent, UIAnchor.CenterLeft, minimal ? new Offset(90f, -10f, 110f, 10f) : new Offset(295f, -10f, 315f, 10f))
                    .WithStyle(m_ButtonStyle)
                    .WithChildren(ownedToggle =>
                    {
                        if (uiUser.ShowOwned)
                        {
                            TextContainer.Create(ownedToggle, UIAnchor.FullStretch, Offset.zero)
                                .WithText("•")
                                .WithStyle(m_ToggleLabelStyle);
                        }

                        ButtonContainer.Create(ownedToggle, UIAnchor.FullStretch, Offset.zero)
                            .WithColor(Color.Clear)
                            .WithCallback(m_CallbackHandler, arg =>
                                {
                                    uiUser.ShowOwned = !uiUser.ShowOwned;
                                    OpenSkinShop(uiUser.Player);
                                }, $"{uiUser.Player.UserIDString}.toggleowner");

                        TextContainer.Create(ownedToggle, UIAnchor.CenterRight, new Offset(5f, -10f, 55f, 10f))
                            .WithText(GetString("UI.Popup.Owned", uiUser.Player))
                            .WithAlignment(TextAnchor.MiddleLeft);

                    });
            }
        }
        
        private void CreateItemView(UIUser uiUser, UserData userData, KeyValuePair<string, ulong> skin, int permissionPage = 0)
        {
            SkinData skinData = m_SkinData.Data[skin.Key][skin.Value];
            bool isAdmin = uiUser.Player.HasPermission(ADMIN_PERMISSION);
            
            // Layer.HudMenu, FullStretch root, DestroyExisting()
            BaseContainer root = BaseContainer.Create(PS_UI, Layer.HudMenu, UIAnchor.FullStretch, Offset.zero)
                .WithChildren(parent =>
                {
                    ImageContainer.Create(parent, UIAnchor.FullStretch, Offset.zero)
                        .WithStyle(m_BackgroundStyle);
                    ButtonContainer.Create(parent, UIAnchor.FullStretch, Offset.zero)
                        .WithColor(Color.Clear)
                        .WithCallback(m_CallbackHandler, arg => OpenSkinShop(uiUser.Player), $"{uiUser.Player.UserIDString}.itemview.exit");
                    
                    ImageContainer.Create(parent, UIAnchor.Center, new Offset(-100f, 72.5f, 100f, 102.5f))
                        .WithStyle(m_PanelStyle)
                        .WithChildren(titleBar =>
                        {
                            TextContainer.Create(titleBar, UIAnchor.FullStretch, Offset.zero)
                                .WithText(skinData.Title + (isAdmin ? $" ({skin.Value})" : ""))
                                .WithAlignment(TextAnchor.MiddleCenter);
                        });

                    bool userOwnsSkin = UserOwnsSkin(uiUser.Player, skin.Key, skin.Value);
                    bool isOwned = (!Configuration.Shop.GiveItemOnPurchase && userData.IsOwned(skin.Key, skin.Value)) || userOwnsSkin;
                    bool isDefaultSkin = userData.IsDefaultSkin(skin.Key, skin.Value);
                    
                    ImageContainer.Create(parent, UIAnchor.Center, new Offset(-100f, -97.5f, 100f, 67.5f))
                        .WithStyle(m_PanelStyle)
                        .WithChildren(icon =>
                        {
                            ImageContainer.Create(icon, UIAnchor.TopCenter, new Offset(-64f, -133f, 64f, -5f))
                                .WithIcon(m_ShortnameToItemId[skin.Key], skin.Value);
                           
                            if (!string.IsNullOrEmpty(skinData.permission))
                            {
                                ImageContainer.Create(parent, UIAnchor.Center, new Offset(-100f, 107.5f, 100f, 137.5f))
                                    .WithStyle(m_OwnedPanelStyle)
                                    .WithChildren(vipskin =>
                                    {
                                        TextContainer.Create(vipskin, UIAnchor.FullStretch, Offset.zero)
                                            .WithText(GetString($"UI.VIP.{skinData.permission}", uiUser.Player))
                                            .WithAlignment(TextAnchor.MiddleCenter);
                                    });
                            }

                            // Purchase
                            ImageContainer.Create(icon, UIAnchor.BottomStretch, new Offset(5f, 5f, -5f, 25f))
                                .WithStyle(m_ButtonStyle)
                                .WithChildren(button =>
                                {
                                    bool noPermission = !string.IsNullOrEmpty(skinData.permission) && !uiUser.Player.HasPermission(skinData.permission);
                                    bool isFree = !Configuration.Purchase.Enabled || uiUser.Player.HasPermission(NOCHARGE_PERMISSION) || userOwnsSkin;
                                    bool canAfford = isFree || GetUserBalance(uiUser.Player) >= skinData.cost;
                                    
                                    string buttonStr = 
                                        isOwned ? GetString("UI.Popup.Owned", uiUser.Player) :
                                        noPermission ? GetString("UI.Popup.NoPermission", uiUser.Player) :
                                        Configuration.Purchase.Enabled ? FormatString(canAfford ? "UI.Popup.PurchasePrice" : "UI.Popup.InsufficientFunds", uiUser.Player, skinData.cost, GetString(m_CurrencyType.ToString(), uiUser.Player)) :
                                        GetString("UI.Popup.Claim", uiUser.Player);
                                    
                                    TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
                                        .WithText(buttonStr)
                                        .WithAlignment(TextAnchor.MiddleCenter);

                                    if (isOwned || noPermission || !canAfford)
                                        return;
                                    
                                    ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
                                        .WithColor(Color.Clear)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            int cost = isFree ? 0 : skinData.cost;
                                            
                                            if (Configuration.Purchase.Enabled && cost > GetUserBalance(uiUser.Player))
                                                return;

                                            if (Configuration.Shop.GiveItemOnPurchase)
                                            {
                                                if (!Configuration.Purchase.Enabled || ChargeForPurchase(uiUser.Player, cost))
                                                {
                                                    Item item = ItemManager.CreateByName(skin.Key, 1, skin.Value);
                                                    item.name = skinData.Title;
                                                    uiUser.Player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);

                                                    if (uiUser.DisplayMode == DisplayMode.Full)
                                                        CreatePopupMessage(uiUser, FormatString("UI.Popup.Purchased", uiUser.Player, skinData.Title));
                                                }
                                            }
                                            else
                                            {
                                                if (!Configuration.Purchase.Enabled || ChargeForPurchase(uiUser.Player, cost))
                                                {
                                                    if (!userData.purchasedSkins.ContainsKey(skin.Key))
                                                        userData.purchasedSkins.Add(skin.Key, new List<ulong>());

                                                    if (!userData.purchasedSkins[skin.Key].Contains(skin.Value))
                                                        userData.purchasedSkins[skin.Key].Add(skin.Value);
                                                }
                                            }
                                            
                                            OpenSkinShop(uiUser.Player);
                                        }, $"{uiUser.Player.UserIDString}.purchase");
                                });


                        });

                    if (isOwned)
                    {
                        ImageContainer.Create(parent, UIAnchor.Center, new Offset(-100f, -132.5f, 100f, -102.5f))
                            .WithStyle(m_PanelStyle)
                            .WithChildren(setDefault =>
                            {
                                ImageContainer.Create(setDefault, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -5f))
                                    .WithStyle(m_ButtonStyle)
                                    .WithChildren(button =>
                                    {
                                        TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
                                            .WithText(GetString(isDefaultSkin ? "UI.Popup.RemoveDefault" : "UI.Popup.SetDefault", uiUser.Player))
                                            .WithAlignment(TextAnchor.MiddleCenter);

                                        ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
                                            .WithColor(Color.Clear)
                                            .WithCallback(m_CallbackHandler, arg =>
                                            {
                                                if (isDefaultSkin)
                                                    userData.defaultSkins.Remove(skin.Key);
                                                else userData.defaultSkins[skin.Key] = skin.Value;

                                                CreateItemView(uiUser, userData, skin, permissionPage);
                                            }, $"{uiUser.Player.UserIDString}.setdefault");

                                    });
                            });

                        bool isFree = uiUser.Player.HasPermission(NOCHARGE_PERMISSION) || userOwnsSkin;
                        
                        if (Configuration.Shop.SellSkins && Configuration.Purchase.Enabled && !isFree)
                        {
                            ImageContainer.Create(parent, UIAnchor.Center, new Offset(-100f, -167.5f, 100f, -137.5f))
                                .WithStyle(m_PanelStyle)
                                .WithChildren(sellSkin =>
                                {
                                    ImageContainer.Create(sellSkin, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -5f))
                                        .WithStyle(m_ButtonStyle)
                                        .WithOutline(m_OutlineRed)
                                        .WithChildren(button =>
                                        {
                                            int refundAmount = GetRefundAmount(skinData.cost);
                                            
                                            TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
                                                .WithText(FormatString("UI.Popup.Sell", uiUser.Player, refundAmount, GetString(m_CurrencyType.ToString(), uiUser.Player)))
                                                .WithAlignment(TextAnchor.MiddleCenter);

                                            ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
                                                .WithColor(Color.Clear)
                                                .WithCallback(m_CallbackHandler, arg =>
                                                {
                                                    userData.purchasedSkins[skin.Key].Remove(skin.Value);

                                                    if (isDefaultSkin)
                                                        userData.defaultSkins.Remove(skin.Key);

                                                    if (!uiUser.Player.HasPermission(NOCHARGE_PERMISSION))
                                                        RefundPurchase(uiUser.Player, refundAmount);

                                                    CreateItemView(uiUser, userData, skin, permissionPage);
                                                }, $"{uiUser.Player.UserIDString}.sell");
                                        });
                                });
                        }
                    }

                    if (isAdmin)
                        ShowAdminMode(uiUser, parent, userData, skin, skinData, permissionPage);
                })
                .DestroyExisting();
            
            ChaosUI.Show(uiUser.Player, root);
        }

        private void ShowAdminMode(UIUser uiUser, BaseContainer parent, UserData userData, KeyValuePair<string, ulong> skin, SkinData skinData, int permissionPage = 0)
        {
            ImageContainer.Create(parent, UIAnchor.TopRight, new Offset(-255f, -35f, -5f, -5f))
                .WithStyle(m_PanelStyle)
                .WithChildren(adminBar =>
                {
                    TextContainer.Create(adminBar, UIAnchor.FullStretch, Offset.zero)
                        .WithText(GetString("UI.Admin.Options", uiUser.Player))
                        .WithAlignment(TextAnchor.MiddleCenter);
                    
                    ImageContainer.Create(adminBar, UIAnchor.CenterLeft, new Offset(5f, -10f, 25f, 10f))
                        .WithStyle(m_ButtonStyle)
                        .WithChildren(toggle =>
                        {
                            if (uiUser.AdminMode)
                            {
                                TextContainer.Create(toggle, UIAnchor.FullStretch, Offset.zero)
                                    .WithText("•")
                                    .WithStyle(m_ToggleLabelStyle);
                            }

                            ButtonContainer.Create(toggle, UIAnchor.FullStretch, Offset.zero)
                                .WithColor(Color.Clear)
                                .WithCallback(m_CallbackHandler, arg =>
                                {
                                    uiUser.AdminMode = !uiUser.AdminMode;
                                    CreateItemView(uiUser, userData, skin, permissionPage);

                                }, $"{uiUser.Player.UserIDString}.toggleadmin");
                        });
                });

            if (!uiUser.AdminMode)
                return;
            
            BaseContainer.Create(parent, UIAnchor.RightStretch, new Offset(-255f, 5f, -5f, -40f))
                .WithChildren(adminOptions =>
                {
                    ImageContainer.Create(adminOptions, UIAnchor.TopStretch, new Offset(0f, -30f, 0f, 0f))
                        .WithStyle(m_PanelStyle)
                        .WithChildren(price =>
                        {
                            TextContainer.Create(price, UIAnchor.TopLeft, new Offset(5f, -25f, 105f, -5f))
                                .WithText(GetString("UI.Admin.Price", uiUser.Player))
                                .WithAlignment(TextAnchor.MiddleLeft);

                            ImageContainer.Create(price, UIAnchor.TopStretch, new Offset(105f, -25f, -5f, -5f))
                                .WithStyle(m_ButtonStyle)
                                .WithChildren(priceInput =>
                                {
                                    InputFieldContainer.Create(priceInput, UIAnchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
                                        .WithText(skinData.cost.ToString())
                                        .WithAlignment(TextAnchor.MiddleLeft)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            skinData.cost = arg.GetInt(1);
                                            m_SkinData.Save();

                                            CreateItemView(uiUser, userData, skin, permissionPage);
                                        }, $"{uiUser.Player.UserIDString}.setprice");
                                });

                        });

                    ImageContainer.Create(adminOptions, UIAnchor.FullStretch, new Offset(0f, 35f, 0f, -35f))
                        .WithStyle(m_PanelStyle)
                        .WithChildren(permissions =>
                        {
                            permissionPage = ClampLayoutPage(m_PermissionLayout, permissionPage, Configuration.Shop.Permissions.Count);

                            TextContainer.Create(permissions, UIAnchor.TopStretch, new Offset(5f, -25f, 0f, -5f))
                                .WithText(GetString("UI.Admin.Permissions", uiUser.Player))
                                .WithAlignment(TextAnchor.MiddleLeft);

                            BaseContainer.Create(permissions, UIAnchor.FullStretch, new Offset(5f, 30f, -5f, -30f))
                                .WithLayoutGroup(m_PermissionLayout, Configuration.Shop.Permissions, permissionPage, (int i, string t, BaseContainer layout, UIAnchor anchor, Offset offset) =>
                                {
                                    BaseContainer button = ImageContainer.Create(layout, anchor, offset)
                                        .WithStyle(m_ButtonStyle)
                                        .WithChildren(permissionButton =>
                                        {
                                            TextContainer.Create(permissionButton, UIAnchor.FullStretch, Offset.zero)
                                                .WithText(t)
                                                .WithAlignment(TextAnchor.MiddleCenter);

                                            ButtonContainer.Create(permissionButton, UIAnchor.FullStretch, Offset.zero)
                                                .WithColor(Color.Clear)
                                                .WithCallback(m_CallbackHandler, arg =>
                                                {
                                                    if (skinData.permission == t)
                                                        skinData.permission = string.Empty;
                                                    else skinData.permission = t;
                                                    
                                                    m_SkinData.Save();

                                                    CreateItemView(uiUser, userData, skin, permissionPage);
                                                }, $"{uiUser.Player.UserIDString}.permission.{i}");
                                        });

                                    if (skinData.permission == t)
                                        button.WithOutline(m_OutlineGreen);
                                });
                            
                            ImageContainer.Create(permissions, UIAnchor.BottomCenter, new Offset(-121.25f, 5f, -3.75f, 25f))
                                .WithStyle(permissionPage > 0 ? m_ButtonStyle : m_ButtonDisabledStyle)
                                .WithChildren(back =>
                                {
                                    TextContainer.Create(back, UIAnchor.FullStretch, Offset.zero)
                                        .WithText("<<<")
                                        .WithStyle(permissionPage > 0 ? m_ButtonStyle : m_ButtonDisabledStyle);

                                    if (permissionPage > 0)
                                    {
                                        ButtonContainer.Create(back, UIAnchor.FullStretch, Offset.zero)
                                            .WithColor(Color.Clear)
                                            .WithCallback(m_CallbackHandler, arg =>
                                            {
                                                CreateItemView(uiUser, userData, skin, permissionPage - 1);
                                            }, $"{uiUser.Player.UserIDString}.back");
                                    }
                                });

                            bool hasNextPage = m_PermissionLayout.HasNextPage(permissionPage, Configuration.Shop.Permissions.Count);
                            
                            ImageContainer.Create(permissions, UIAnchor.BottomCenter, new Offset(2.5f, 5f, 120f, 25f))
                                .WithStyle(hasNextPage ? m_ButtonStyle : m_ButtonDisabledStyle)
                                .WithChildren(next =>
                                {
                                    TextContainer.Create(next, UIAnchor.FullStretch, Offset.zero)
                                        .WithText(">>>")
                                        .WithStyle(hasNextPage ? m_ButtonStyle : m_ButtonDisabledStyle);

                                    if (hasNextPage)
                                    {
                                        ButtonContainer.Create(next, UIAnchor.FullStretch, Offset.zero)
                                        .WithColor(Color.Clear)
                                        .WithCallback(m_CallbackHandler, arg =>
                                        {
                                            CreateItemView(uiUser, userData, skin, permissionPage + 1);
                                        }, $"{uiUser.Player.UserIDString}.next");
                                    }
                                });
                        });

                    ImageContainer.Create(adminOptions, UIAnchor.BottomStretch, new Offset(0f, 0f, 0f, 30f))
                        .WithStyle(m_PanelStyle)
                        .WithChildren(remove =>
                        {
                            ImageContainer.Create(remove, UIAnchor.BottomStretch, new Offset(5f, 5f, -5f, 25f))
                                .WithStyle(m_ButtonStyle)
                                .WithOutline(m_OutlineRed)
                                .WithChildren(removeButton =>
                                {
                                    TextContainer.Create(removeButton, UIAnchor.FullStretch, Offset.zero)
                                        .WithText(GetString(skinData.isDisabled ? "UI.Admin.Enable" : "UI.Admin.Disable", uiUser.Player))
                                        .WithAlignment(TextAnchor.MiddleCenter);

                                    if (!skinData.isDisabled)
                                    {
                                        ButtonContainer.Create(removeButton, UIAnchor.FullStretch, Offset.zero)
                                            .WithColor(Color.Clear)
                                            .WithCallback(m_CallbackHandler, arg =>
                                            {
                                                skinData.isDisabled = !skinData.isDisabled;
                                                m_SkinData.Save();
                                                
                                                CreateItemView(uiUser, userData, skin, permissionPage);
                                            }, $"{uiUser.Player.UserIDString}.disableditem");
                                    }
                                });
                        });
                });
        }

        #endregion
        
        #region Reskin Loot Box

        private const bool RESKIN_DEBUG = false;
        private Timer _reskinDebugTimer;

        private void ReskinDebug(string msg)
        {
            if (RESKIN_DEBUG) Puts("[Reskin] " + msg);
        }

        private void ReskinDebug(string msg, BasePlayer player)
        {
            if (RESKIN_DEBUG) Puts($"[Reskin] {msg} | player={player?.displayName ?? "null"} ({player?.userID ?? 0})");
        }

        private void ReskinDebugLootState()
        {
            if (!RESKIN_DEBUG || m_ActiveReskinLoot.Count == 0) return;
            foreach (var kvp in m_ActiveReskinLoot)
            {
                BasePlayer p = kvp.Value?.Looter;
                if (p == null || !p.IsValid()) continue;
                var loot = p.inventory?.loot;
                if (loot == null) continue;
                var ent = loot.entitySource;
                float dist = ent != null ? ent.Distance(p.eyes.position) : -1f;
                bool canBeLooted = ent != null && ent.CanBeLooted(p);
                bool transferring = ent != null && ent.IsTransferring();
                ReskinDebug($"LootState containers={loot.containers?.Count ?? 0} entitySource={(ent != null ? ent.net?.ID.ToString() : "null")} entityDestroyed={ent?.IsDestroyed ?? true} PositionChecks={loot.PositionChecks} distance={dist:F1} CanBeLooted={canBeLooted} IsTransferring={transferring}", p);
            }
        }

        private void StartReskinDebugTimer()
        {
            _reskinDebugTimer?.Destroy();
            _reskinDebugTimer = timer.Every(2f, () =>
            {
                if (m_ActiveReskinLoot.Count == 0) { _reskinDebugTimer?.Destroy(); _reskinDebugTimer = null; return; }
                ReskinDebugLootState();
            });
        }

        private void StopReskinDebugTimer()
        {
            _reskinDebugTimer?.Destroy();
            _reskinDebugTimer = null;
        }

        private const string COFFIN_PREFAB = "assets/prefabs/misc/halloween/coffin/coffinstorage.prefab";
        private const string WOODBOX_PREFAB = "assets/content/props/wooden_crate/wooden_crate_storage.prefab";

        internal static bool ReskinLootHooksActive;

        private void ToggleReskinLootHooks()
        {
            ReskinLootHooksActive = m_ActiveReskinLoot.Count > 0;
            if (!ReskinLootHooksActive)
                StopReskinDebugTimer();
        }

        private void CreateReskinLootBox(BasePlayer player)
        {
            ReskinDebug("CreateReskinLootBox called", player);
            if (player == null || !player.IsValid())
                return;
            if (player.inventory?.loot?.IsLooting() ?? false)
            {
                ReskinDebug("Player already looting - abort", player);
                SendReply(player, "Close any open loot panel first.");
                return;
            }
            if (m_ActiveReskinLoot.ContainsKey(player.userID))
            {
                ReskinDebug("Already have reskin open, returning", player);
                SendReply(player, "You already have the reskin menu open.");
                return;
            }
            if (!m_UserData.Data.ContainsKey(player.userID))
                m_UserData.Data.Add(player.userID, new UserData());
            ReskinDebug($"Spawning container at pos -250 (under map)", player);
            StorageContainer container = GameManager.server.CreateEntity(COFFIN_PREFAB, player.transform.position + (Vector3.down * 250f)) as StorageContainer;
            if (container == null)
                container = GameManager.server.CreateEntity(WOODBOX_PREFAB, player.transform.position + (Vector3.down * 250f)) as StorageContainer;
            if (container == null)
            {
                SendReply(player, "Could not open reskin menu (storage prefab failed). Try again or contact an admin.");
                return;
            }
            container.limitNetworking = true;
            container.enableSaving = false;
            container.inventorySlots = PS_RESKIN_SLOTS;
            UnityEngine.Object.Destroy(container.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.Destroy(container.GetComponent<GroundWatch>());
            container.Spawn();
            ReskinDebug($"Container spawned netId={container?.net?.ID} IsDestroyed={container?.IsDestroyed}", player);
            ReskinLootHandler handler = container.gameObject.AddComponent<ReskinLootHandler>();
            player.inventory.loot.Clear();
            player.inventory.loot.SendImmediate();
            m_ActiveReskinLoot[player.userID] = handler;
            ToggleReskinLootHooks();
            if (RESKIN_DEBUG) StartReskinDebugTimer();
            timer.In(0.05f, () =>
            {
                ReskinDebug("Timer 0.05s fired", player);
                if (player == null || !player.IsValid()) { ReskinDebug("Player null/invalid in timer", player); return; }
                if (handler == null) { ReskinDebug("Handler null in timer", player); return; }
                if (container == null || container.IsDestroyed) { ReskinDebug($"Container null or destroyed in timer netId={container?.net?.ID}", player); return; }
                handler.Looter = player;
                ShowReskinLootOverlay(player);
                player.inventory.loot.PositionChecks = false;
                player.inventory.loot.entitySource = container;
                player.inventory.loot.itemSource = null;
                player.inventory.loot.MarkDirty();
                player.inventory.loot.AddContainer(container.inventory);
                player.inventory.loot.SendImmediate();
                player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), PS_LOOT_PANEL);
                container.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                ReskinDebug("Loot setup done", player);
                SendReply(player, "Drag an item from your inventory into the skin box slot, then pick a skin.");
            });
        }

        /// <summary>Shows the Chaos UI overlay so the loot panel area is visible on screen.</summary>
        private void ShowReskinLootOverlay(BasePlayer player)
        {
            ReskinDebug("ShowReskinLootOverlay called, building UI", player);
            if (!m_ActiveReskinLoot.ContainsKey(player.userID))
            {
                ReskinDebug("ShowReskinLootOverlay aborted - player not in m_ActiveReskinLoot (loot already closed?)", player);
                return;
            }
            Color headerColor = new Color(0.1019608f, 0.1019608f, 0.1019608f, 1f);
            BaseContainer root = BaseContainer.Create(PS_OVERLAY, Layer.HudMenu, UIAnchor.FullStretch, Offset.zero)
                .WithChildren(parent =>
                {
                    BaseContainer menu = BaseContainer.Create(parent, UIAnchor.FullStretch, new Offset(17.5f, 17.5f, -17.5f, -17.5f));
                    BaseContainer container = BaseContainer.Create(menu, UIAnchor.BottomCenter, new Offset(-580.5f, 64f, 580.5f, 635f));
                    BaseContainer right = BaseContainer.Create(container, UIAnchor.RightStretch, new Offset(-380f, 28f, 0f, 5f));
                    BaseContainer contents = BaseContainer.Create(right, UIAnchor.BottomStretch, new Offset(0f, 0f, 0f, 950.22f));
                    BaseContainer loot = BaseContainer.Create(contents, UIAnchor.TopLeft, new Offset(0f, -950.22f, 380f, -393.22f));
                    BaseContainer panel = BaseContainer.Create(loot, UIAnchor.TopLeft, new Offset(0f, -557f, 380f, -8f));
                    ImageContainer.Create(panel, UIAnchor.TopLeft, new Offset(-8f, -49f, 372f, -26f))
                        .WithColor(headerColor)
                        .WithName(PS_HEADER)
                        .WithChildren(header =>
                        {
                            TextContainer.Create(header, UIAnchor.FullStretch, new Offset(10f, 0f, 0f, 0f))
                                .WithText("Reskin - Drag an item into the box")
                                .WithStyle(m_TitleStyle)
                                .WithAlignment(TextAnchor.MiddleLeft);
                        });
                })
                .DestroyExisting();
            ChaosUI.Show(player, root);
            ReskinDebug("ShowReskinLootOverlay: ChaosUI.Show completed for PS_OVERLAY", player);
        }

        private void GetSkinsForReskin(BasePlayer player, string shortname, List<ulong> outList)
        {
            outList.Clear();
            if (!m_SkinData.Data.TryGetValue(shortname, out Hash<ulong, SkinData> skinLookup))
                return;
            if (!m_UserData.Data.TryGetValue(player.userID, out UserData userData))
                return;
            userData.purchasedSkins.TryGetValue(shortname, out List<ulong> purchasedSkins);
            if (purchasedSkins != null)
            {
                foreach (ulong skin in purchasedSkins)
                {
                    if (skinLookup.TryGetValue(skin, out SkinData skinData) && !skinData.isDisabled)
                        outList.Add(skin);
                }
            }
            // Only add paid/DLC skins the player owns on Steam; community workshop skins require purchase
            if (PlayerDlcApi.IsLoaded)
            {
                foreach (KeyValuePair<ulong, SkinData> kvp in skinLookup)
                {
                    if (kvp.Value.isDisabled || outList.Contains(kvp.Key))
                        continue;
                    if (PlayerDlcApi.IsPaidSkin(kvp.Key) && PlayerDlcApi.IsOwnedOrFreeSkin(player, kvp.Key))
                        outList.Add(kvp.Key);
                }
            }
            if (Configuration.Workshop.ApprovedIfOwned && PlayerDlcApi.IsLoaded)
                PlayerDlcApi.FilterOwnedOrFreeSkins(player, outList);
        }

        #endregion

        #region Reskin Menu
        
        private void OpenReskinMenu(BasePlayer player)
        {
            Item item = player.GetActiveItem();
            if (item == null)
            {
                item = player.inventory.containerBelt.GetSlot(0);
                if (item == null)
                {
                    player.LocalizedMessage(this, "Chat.Reskin.NoItem2");
                    return;
                }
            }

            if (!m_SkinData.Data.TryGetValue(item.info.shortname, out Hash<ulong, SkinData> skinData) || skinData.Count == 0)
            {
                player.LocalizedMessage(this, "Chat.Reskin.NoSkins");
                return;
            }

            if (!m_UserData.Data.TryGetValue(player.userID, out UserData userData))
            {
                player.LocalizedMessage(this, "Chat.Reskin.NoPurchases");
                return;
            }
            
            OpenReskinMenuUI(player, userData, item);
        }

        private void OpenReskinMenuUI(BasePlayer player, UserData userData, Item item)
        {
            if (!m_UIUsers.TryGetValue(player.userID, out UIUser uiUser))
                uiUser = m_UIUsers[player.userID] = new UIUser(player, DisplayMode.None);

            BaseContainer root = BaseContainer.Create(PS_UI, Layer.Overall, UIAnchor.FullStretch, new Offset(16f, 16f, -16f, -16f))
                .WithChildren(inset =>
                    {
                        BaseContainer.Create(inset, UIAnchor.BottomStretch, new Offset(0f, 0f, 0f, 64f))
                            .WithChildren(bottom =>
                            {
                                ImageContainer.Create(bottom, UIAnchor.BottomCenter, new Offset(-198.5f, 69f, 182.5f, 304f))
                                    .WithStyle(m_BackgroundStyle)
                                    .WithChildren(parent =>
                                    {
                                        CreateReskinTitleBar(uiUser, parent, item);

                                        CreateReskinItemGrid(uiUser, parent, userData, item);
                                    });
                            });
                    })
                .NeedsCursor()
                .NeedsKeyboard();

            ChaosUI.Destroy(player, PS_UI);
            ChaosUI.Show(player, root);
        }

        private void CreateReskinTitleBar(UIUser uiUser, BaseContainer parent, Item item)
        {
            ImageContainer.Create(parent, UIAnchor.TopStretch, new Offset(5f, -35f, -5f, -5f))
                .WithStyle(m_PanelStyle)
                .WithChildren(titleBar =>
                {
                    TextContainer.Create(titleBar, UIAnchor.CenterLeft, new Offset(4.999992f, -15f, 243.8218f, 15f))
                        .WithText(FormatString("UI.Reskin.SkinList", uiUser.Player, GetString(item.info.shortname, uiUser.Player)))
                        .WithAlignment(TextAnchor.MiddleLeft);

                    ImageContainer.Create(titleBar, UIAnchor.CenterRight, new Offset(-55f, -10f, -5f, 10f))
                        .WithStyle(m_ButtonStyle)
                        .WithOutline(m_OutlineRed)
                        .WithChildren(exit =>
                        {
                            TextContainer.Create(exit, UIAnchor.FullStretch, Offset.zero)
                                .WithText(GetString("UI.Exit", uiUser.Player))
                                .WithAlignment(TextAnchor.MiddleCenter);

                            ButtonContainer.Create(exit, UIAnchor.FullStretch, Offset.zero)
                                .WithColor(Color.Clear)
                                .WithCallback(m_CallbackHandler, arg =>
                                {
                                    m_UIUsers.Remove(uiUser.Player.userID);
                                    ChaosUI.Destroy(uiUser.Player, PS_UI);
                                }, $"{uiUser.Player.UserIDString}.exit");
                        });
                });
        }
        
        private void CreateReskinItemGrid(UIUser uiUser, BaseContainer parent, UserData userData, Item item)
        {
            List<ulong> skinList = Pool.Get<List<ulong>>();

            if (m_SkinData.Data.TryGetValue(item.info.shortname, out Hash<ulong, SkinData> skinLookup))
            {
                userData.purchasedSkins.TryGetValue(item.info.shortname, out List<ulong> purchasedSkins);

                if (purchasedSkins?.Count > 0)
                {
                    foreach (ulong skin in purchasedSkins)
                    {
                        if (skinLookup.TryGetValue(skin, out SkinData skinData) && !skinData.isDisabled)
                            skinList.Add(skin);
                    }
                }

                // Only add paid/DLC skins the player owns on Steam; community workshop skins require purchase
                if (PlayerDlcApi.IsLoaded)
                {
                    foreach (KeyValuePair<ulong, SkinData> kvp in skinLookup)
                    {
                        if (kvp.Value.isDisabled)
                            continue;
                        if (skinList.Contains(kvp.Key))
                            continue;
                        if (PlayerDlcApi.IsPaidSkin(kvp.Key) && PlayerDlcApi.IsOwnedOrFreeSkin(uiUser.Player, kvp.Key))
                            skinList.Add(kvp.Key);
                    }
                }
            }

            if (Configuration.Workshop.ApprovedIfOwned && PlayerDlcApi.IsLoaded)
                PlayerDlcApi.FilterOwnedOrFreeSkins(uiUser.Player, skinList);

            uiUser.GridPage = ClampLayoutPage(m_ReskinItemGrid, uiUser.GridPage, skinList.Count);
            
            if (skinList.Count == 0)
            {
                TextContainer.Create(parent, UIAnchor.FullStretch, new Offset(5f, 40f, -5f, -40f))
                    .WithText(GetString("Chat.Reskin.NoPurchases", uiUser.Player))
                    .WithAlignment(TextAnchor.MiddleCenter);
            }
            else
            {
                ImageContainer.Create(parent, UIAnchor.FullStretch, new Offset(5f, 40f, -5f, -40f))
                    .WithStyle(m_PanelStyle)
                    .WithLayoutGroup(m_ReskinItemGrid, skinList, uiUser.GridPage, (int i, ulong t, BaseContainer itemGrid, UIAnchor anchor, Offset offset) =>
                    {
                        ImageContainer.Create(itemGrid, anchor, offset)
                            .WithStyle(m_PanelStyle)
                            .WithChildren(template =>
                            {
                                ImageContainer.Create(template, UIAnchor.Center, new Offset(-32f, -32f, 32f, 32f))
                                    .WithIcon(item.info.itemid, t);

                                ButtonContainer.Create(template, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg =>
                                    {
                                        ChangeItemSkin(uiUser.Player, t);
                                        m_UIUsers.Remove(uiUser.Player.userID);
                                        ChaosUI.Destroy(uiUser.Player, PS_UI);
                                    }, $"{uiUser.Player.UserIDString}.reskin.{i}");
                            });
                    });
            }

            CreateReskinFooter(uiUser, parent, skinList.Count, userData, item);
            
            Pool.FreeUnmanaged(ref skinList);
        }

        private void CreateReskinFooter(UIUser uiUser, BaseContainer parent, int listCount, UserData userData, Item item)
        {
            ImageContainer.Create(parent, UIAnchor.BottomStretch, new Offset(5f, 5f, -5f, 35f))
                .WithStyle(m_PanelStyle)
                .WithChildren(footer =>
                {
                    ImageContainer.Create(footer, UIAnchor.CenterLeft, new Offset(5f, -10f, 35f, 10f))
                        .WithStyle(uiUser.GridPage > 0 ? m_ButtonStyle : m_ButtonDisabledStyle)
                        .WithChildren(backButton =>
                        {
                            TextContainer.Create(backButton, UIAnchor.FullStretch, Offset.zero)
                                .WithText("<<<")
                                .WithStyle(uiUser.GridPage > 0 ? m_ButtonStyle : m_ButtonDisabledStyle);

                            if (uiUser.GridPage > 0)
                            {
                                ButtonContainer.Create(backButton, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg =>
                                    {
                                        uiUser.GridPage--;
                                        OpenReskinMenuUI(uiUser.Player, userData, item);
                                    }, $"{uiUser.Player.UserIDString}.grid.back");
                            }

                        });

                    bool hasNextPage = m_ReskinItemGrid.HasNextPage(uiUser.GridPage, listCount);
                    
                    ImageContainer.Create(footer, UIAnchor.CenterRight, new Offset(-35f, -10f, -5f, 10f))
                        .WithStyle(hasNextPage ? m_ButtonStyle : m_ButtonDisabledStyle)
                        .WithChildren(nextButton =>
                        {
                            TextContainer.Create(nextButton, UIAnchor.FullStretch, Offset.zero)
                                .WithText(">>>")
                                .WithStyle(hasNextPage ? m_ButtonStyle : m_ButtonDisabledStyle);

                            if (hasNextPage)
                            {
                                ButtonContainer.Create(nextButton, UIAnchor.FullStretch, Offset.zero)
                                    .WithColor(Color.Clear)
                                    .WithCallback(m_CallbackHandler, arg =>
                                    {
                                        uiUser.GridPage++;
                                        OpenReskinMenuUI(uiUser.Player, userData, item);
                                    }, $"{uiUser.Player.UserIDString}.grid.next");
                            }
                        });
                });
        }
        #endregion
        
        #region Popup Message

        private Hash<ulong, Timer> m_PopupTimers = new Hash<ulong, Timer>();

        private void CreatePopupMessage(UIUser uiUser, string message)
        {
            // Layer.HudMenu, DestroyExisting()
            BaseContainer baseContainer = ImageContainer.Create(PS_UI_POPUP, Layer.HudMenu, UIAnchor.TopRight, new Offset(-315f, -65f, -5f, -5f))
                .WithStyle(m_PanelStyle)
                .WithChildren(popup =>
                {
                    TextContainer.Create(popup, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -5f))
                        .WithText(message)
                        .WithStyle(m_PanelStyle);
                    
                    ImageContainer.Create(popup, UIAnchor.TopRight, new Offset(-20f, -20f, 0f, 0f))
                        .WithStyle(m_ButtonStyle)
                        .WithChildren(button =>
                        {
                            TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
                                .WithStyle(m_ButtonStyle)
                                .WithText("✘");
                            ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
                                .WithColor(Color.Clear)
                                .WithCallback(m_CallbackHandler, arg => ChaosUI.Destroy(uiUser.Player, PS_UI_POPUP), $"{uiUser.Player.userID}.dismiss.popup");
                        });
                })
                .DestroyExisting();
			
            ChaosUI.Show(uiUser.Player, baseContainer);

            if (m_PopupTimers.TryGetValue(uiUser.Player.userID, out Timer t))
                t?.Destroy();

            m_PopupTimers[uiUser.Player.userID] = timer.Once(5f, () => ChaosUI.Destroy(uiUser.Player, PS_UI_POPUP));
        }
        #endregion
        #endregion
        #endregion

        #region Reskin LootHandler and InputItem

        private class ReskinLootHandler : MonoBehaviour
        {
            internal BasePlayer Looter { get; set; }
            internal StorageContainer Entity { get; private set; }
            internal bool HasItem { get; private set; }
            internal string InputShortname { get; private set; }
            private ReskinInputItem inputItem;
            private List<ulong> _availableSkins;
            private List<ulong> _filteredSkins;
            private string _searchString = string.Empty;
            private int _currentPage;
            private int _maximumPages;
            private int _itemsPerPage;
            private bool _isFillingContainer;
            private const int MAX_PAGES = 10;

            private void Awake()
            {
                _availableSkins = Pool.Get<List<ulong>>();
                _filteredSkins = Pool.Get<List<ulong>>();
                Entity = GetComponent<StorageContainer>();
                Entity.maxStackSize = 1;
                Entity.inventory.maxStackSize = 1;
                Entity.SetFlag(BaseEntity.Flags.Open, true, false);
            }

            private void OnDestroy()
            {
                if (s_Instance != null && RESKIN_DEBUG)
                    s_Instance.Puts($"[Reskin] ReskinLootHandler OnDestroy Looter={Looter?.userID ?? 0} Entity={Entity?.net?.ID} EntityDestroyed={Entity?.IsDestroyed}");
                ChaosUI.Destroy(Looter, PS_OVERLAY);
                ChaosUI.Destroy(Looter, PS_PAGE);
                ChaosUI.Destroy(Looter, PS_SEARCH);
                s_Instance?.m_ActiveReskinLoot?.Remove(Looter.userID);
                if (HasItem && inputItem != null && Looter != null)
                    Looter.GiveItem(inputItem.Create(), BaseEntity.GiveItemReason.PickedUp);
                Pool.FreeUnmanaged(ref _availableSkins);
                Pool.FreeUnmanaged(ref _filteredSkins);
                inputItem?.Dispose();
                if (Entity != null && !Entity.IsDestroyed)
                {
                    for (int i = Entity.inventory.itemList.Count - 1; i >= 0; i--)
                    {
                        Item it = Entity.inventory.itemList[i];
                        Entity.inventory.itemList.Remove(it);
                        it.parent = null;
                        it.Remove(0f);
                    }
                    Entity.Kill(BaseNetworkable.DestroyMode.None);
                }
                s_Instance?.ToggleReskinLootHooks();
            }

            internal void ReturnItemInstantly()
            {
                if (HasItem && Looter != null && inputItem != null)
                {
                    Looter.GiveItem(inputItem.Create(), BaseEntity.GiveItemReason.PickedUp);
                    HasItem = false;
                }
            }

            internal object CanAcceptItem(Item item)
            {
                if (HasItem)
                    return ItemContainer.CanAcceptResult.CannotAccept;
                if (item == null || item.info == null)
                    return ItemContainer.CanAcceptResult.CannotAccept;
                string shortname = item.info.shortname;
                if (s_Instance.m_IgnoreItems.Contains(shortname))
                    return ItemContainer.CanAcceptResult.CannotAccept;
                if (!s_Instance.m_SkinData.Data.TryGetValue(shortname, out Hash<ulong, SkinData> skinLookup) || skinLookup.Count == 0)
                    return ItemContainer.CanAcceptResult.CannotAccept;
                return null;
            }

            internal bool CanMoveItem(Item item, PlayerInventory inventory, ItemContainerId targetContainerID, int targetSlot)
            {
                return true;
            }

            internal void OnItemAdded(Item item)
            {
                if (HasItem)
                    return;
                HasItem = true;
                InputShortname = item.info.shortname;
                s_Instance.GetSkinsForReskin(Looter, InputShortname, _availableSkins);
                _availableSkins.Remove(0UL);
                if (item.skin != 0UL)
                    _availableSkins.Remove(item.skin);
                _filteredSkins.Clear();
                _filteredSkins.AddRange(_availableSkins);
                inputItem = new ReskinInputItem(InputShortname, item);
                _itemsPerPage = Entity.inventory.capacity - 2;
                _currentPage = 0;
                _maximumPages = Mathf.Min(MAX_PAGES, Mathf.Max(1, Mathf.CeilToInt((float)_filteredSkins.Count / _itemsPerPage)));
                CreateOverlay();
                CreatePageButtons();
                CreateSearchBar();
                RemoveItem(item);
                ClearContainer();
                StartCoroutine(FillContainer());
            }

            internal void OnItemRemoved(Item item)
            {
                if (!HasItem || inputItem == null)
                    return;
                ulong chosenSkin = item.skin;
                inputItem.CloneTo(item);
                item.skin = chosenSkin;
                item.MarkDirty();
                BaseEntity heldEntity = item.GetHeldEntity();
                if (heldEntity != null)
                {
                    heldEntity.skinID = chosenSkin;
                    heldEntity.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                }
                if (s_Instance.m_SkinData.Data.TryGetValue(InputShortname, out Hash<ulong, SkinData> skinLookup) && skinLookup.TryGetValue(chosenSkin, out SkinData skinData))
                    item.name = skinData.Title;
                inputItem.Dispose();
                inputItem = null;
                HasItem = false;
                ChaosUI.Destroy(Looter, PS_OVERLAY);
                ChaosUI.Destroy(Looter, PS_PAGE);
                ChaosUI.Destroy(Looter, PS_SEARCH);
                ClearContainer();
                Entity.inventory.MarkDirty();
            }

            private void CreateOverlay()
            {
                BaseContainer root = BaseContainer.Create(PS_OVERLAY, Layer.HudMenu, UIAnchor.FullStretch, Offset.zero)
                    .WithChildren(parent =>
                    {
                        BaseContainer menu = BaseContainer.Create(parent, UIAnchor.FullStretch, new Offset(17.5f, 17.5f, -17.5f, -17.5f));
                        BaseContainer container = BaseContainer.Create(menu, UIAnchor.BottomCenter, new Offset(-580.5f, 64f, 580.5f, 635f));
                        BaseContainer right = BaseContainer.Create(container, UIAnchor.RightStretch, new Offset(-380f, 28f, 0f, 5f));
                        BaseContainer contents = BaseContainer.Create(right, UIAnchor.BottomStretch, new Offset(0f, 0f, 0f, 950.22f));
                        BaseContainer loot = BaseContainer.Create(contents, UIAnchor.TopLeft, new Offset(0f, -950.22f, 380f, -393.22f));
                        BaseContainer panel = BaseContainer.Create(loot, UIAnchor.TopLeft, new Offset(0f, -557f, 380f, -8f));
                        ImageContainer.Create(panel, UIAnchor.TopLeft, new Offset(-8f, -49f, 372f, -26f))
                            .WithColor(new Color(0.1f, 0.1f, 0.1f, 1f))
                            .WithName(PS_HEADER);
                    })
                    .DestroyExisting();
                ChaosUI.Show(Looter, root);
            }

            private void CreatePageButtons()
            {
                ChaosUI.Destroy(Looter, PS_PAGE);
                if (_maximumPages <= 1)
                    return;
                BaseContainer root = BaseContainer.Create(PS_PAGE, Layer.HudMenu, UIAnchor.FullStretch, Offset.zero)
                    .WithParent(PS_HEADER)
                    .WithChildren(header =>
                    {
                        ImageContainer.Create(header, UIAnchor.CenterRight, new Offset(-73f, -10f, -43f, 10f))
                            .WithChildren(next =>
                            {
                                TextContainer.Create(next, UIAnchor.FullStretch, Offset.zero).WithText("▶");
                                ButtonContainer.Create(next, UIAnchor.FullStretch, Offset.zero).WithColor(Color.Clear)
                                    .WithCallback(s_Instance.m_CallbackHandler, arg => ChangePage(1), $"{Looter.userID}.nextpage");
                            });
                        TextContainer.Create(header, UIAnchor.CenterRight, new Offset(-123f, -10.5f, -73f, 10.5f))
                            .WithText($"{_currentPage + 1} / {_maximumPages}");
                        ImageContainer.Create(header, UIAnchor.CenterRight, new Offset(-163f, -10f, -123f, 10f))
                            .WithChildren(prev =>
                            {
                                TextContainer.Create(prev, UIAnchor.FullStretch, Offset.zero).WithText("◀");
                                ButtonContainer.Create(prev, UIAnchor.FullStretch, Offset.zero).WithColor(Color.Clear)
                                    .WithCallback(s_Instance.m_CallbackHandler, arg => ChangePage(-1), $"{Looter.userID}.prevpage");
                            });
                    });
                ChaosUI.Show(Looter, root);
            }

            private void SetSearchParameters(string s)
            {
                _searchString = s ?? string.Empty;
                _filteredSkins.Clear();
                if (string.IsNullOrEmpty(s))
                    _filteredSkins.AddRange(_availableSkins);
                else
                {
                    for (int i = 0; i < _availableSkins.Count; i++)
                    {
                        ulong skinId = _availableSkins[i];
                        string skinLabel = GetSkinSearchLabel(skinId);
                        if (!string.IsNullOrEmpty(skinLabel) && skinLabel.Contains(s, System.StringComparison.OrdinalIgnoreCase))
                            _filteredSkins.Add(skinId);
                    }
                }
                _currentPage = 0;
                _maximumPages = Mathf.Min(MAX_PAGES, Mathf.Max(1, Mathf.CeilToInt((float)_filteredSkins.Count / _itemsPerPage)));
                ChaosUI.Destroy(Looter, PS_PAGE);
                CreatePageButtons();
                StartCoroutine(RefillContainer());
            }

            private string GetSkinSearchLabel(ulong skinId)
            {
                if (s_Instance?.m_SkinData?.Data == null || string.IsNullOrEmpty(InputShortname))
                    return skinId.ToString();
                if (!s_Instance.m_SkinData.Data.TryGetValue(InputShortname, out Hash<ulong, SkinData> lookup) || !lookup.TryGetValue(skinId, out SkinData sd))
                    return skinId.ToString();
                return $"{skinId} {sd.Title ?? ""}";
            }

            private void CreateSearchBar()
            {
                BaseContainer root = ImageContainer.Create(PS_SEARCH, Layer.HudMenu, UIAnchor.CenterRight, new Offset(-153f, 13f, 0f, 33f))
                    .WithStyle(s_Instance.m_PanelStyle)
                    .WithParent(PS_HEADER)
                    .WithChildren(search =>
                    {
                        InputFieldContainer.Create(search, UIAnchor.FullStretch, new Offset(5f, 0f, -5f, 0f))
                            .WithText(_searchString)
                            .WithAlignment(TextAnchor.MiddleLeft)
                            .InHudMenu()
                            .WithCallback(s_Instance.m_CallbackHandler, arg =>
                                SetSearchParameters(arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)) : string.Empty), $"{Looter.userID}.search");
                        if (!string.IsNullOrEmpty(s_Instance.m_MagnifyImage))
                        {
                            RawImageContainer.Create(search, UIAnchor.CenterLeft, new Offset(-20f, -10f, 0f, 10f))
                                .WithPNG(s_Instance.m_MagnifyImage)
                                .WithColor(new Color(1f, 1f, 1f, 1f));
                        }
                    })
                    .DestroyExisting();
                ChaosUI.Show(Looter, root);
            }

            private void ChangePage(int delta)
            {
                if (_isFillingContainer)
                    return;
                _currentPage = (_currentPage + delta) % _maximumPages;
                if (_currentPage < 0)
                    _currentPage = _maximumPages - 1;
                CreatePageButtons();
                StartCoroutine(RefillContainer());
            }

            private IEnumerator RefillContainer()
            {
                ClearContainer();
                yield return FillContainer();
            }

            private IEnumerator FillContainer()
            {
                _isFillingContainer = true;
                ItemDefinition definition = inputItem?.itemDefinition;
                if (definition != null)
                {
                    CreateItem(definition, 0UL);
                    if (inputItem.skin != 0UL)
                        CreateItem(definition, inputItem.skin);
                    int start = _currentPage * _itemsPerPage;
                    int end = Mathf.Min(start + _itemsPerPage, _filteredSkins.Count);
                    for (int i = start; i < end; i++)
                    {
                        if (!HasItem)
                            break;
                        CreateItem(definition, _filteredSkins[i]);
                        if ((i - start) % 2 == 0)
                            yield return null;
                    }
                }
                _isFillingContainer = false;
            }

            private void ClearContainer()
            {
                for (int i = Entity.inventory.itemList.Count - 1; i >= 0; i--)
                    RemoveItem(Entity.inventory.itemList[i]);
            }

            private Item CreateItem(ItemDefinition definition, ulong skinId)
            {
                Item item = ItemManager.Create(definition, 1, skinId);
                item.contents?.SetFlag(ItemContainer.Flag.IsLocked, true);
                BaseProjectile bp = item.GetHeldEntity() as BaseProjectile;
                if (bp != null)
                    bp.primaryMagazine.contents = 0;
                if (skinId != 0UL && s_Instance.m_SkinData.Data.TryGetValue(definition.shortname, out Hash<ulong, SkinData> lookup) && lookup.TryGetValue(skinId, out SkinData sd))
                    item.name = sd.Title;
                if (!InsertItem(item))
                    item.Remove(0f);
                else
                    item.MarkDirty();
                return item;
            }

            private bool InsertItem(Item item)
            {
                if (Entity.inventory.itemList.Contains(item) || Entity.inventory.IsFull())
                    return false;
                Entity.inventory.itemList.Add(item);
                item.parent = Entity.inventory;
                if (!Entity.inventory.FindPosition(item))
                    return false;
                Entity.inventory.MarkDirty();
                return true;
            }

            private void RemoveItem(Item item)
            {
                if (!Entity.inventory.itemList.Contains(item))
                    return;
                Entity.inventory.itemList.Remove(item);
                item.parent = null;
                Entity.inventory.MarkDirty();
                item.Remove(0f);
            }
        }

        private class ReskinInputItem
        {
            internal ItemDefinition itemDefinition;
            internal string name;
            internal int amount;
            internal ulong skin;
            internal string text;
            internal float condition;
            internal float maxCondition;
            internal int magazineContents;
            internal int magazineCapacity;
            internal ItemDefinition ammoType;
            internal int itemModContainerArmourSlot;
            internal List<ReskinInputItem> contents;

            internal ReskinInputItem(string shortname, Item item)
            {
                itemDefinition = item.info.shortname == shortname ? item.info : ItemManager.FindItemDefinition(shortname);
                name = item.name;
                amount = Mathf.Max(item.amount, 1);
                skin = item.skin;
                text = item.text;
                if (item.hasCondition)
                {
                    condition = item.condition;
                    maxCondition = item.maxCondition;
                }
                BaseProjectile bp = item.GetHeldEntity() as BaseProjectile;
                if (bp != null)
                {
                    magazineContents = bp.primaryMagazine.contents;
                    magazineCapacity = bp.primaryMagazine.capacity;
                    ammoType = bp.primaryMagazine.ammoType;
                }
                itemModContainerArmourSlot = item.contents != null && FindItemMod<ItemModContainerArmorSlot>(item.info.itemMods) != null ? item.contents.capacity : 0;
                if (item.contents?.itemList?.Count > 0)
                {
                    contents = Pool.Get<List<ReskinInputItem>>();
                    foreach (Item content in item.contents.itemList)
                    {
                        if (content != null)
                            contents.Add(new ReskinInputItem(content.info.shortname, content));
                    }
                }
            }

            private static T FindItemMod<T>(ItemMod[] mods) where T : ItemMod
            {
                if (mods == null) return null;
                for (int i = 0; i < mods.Length; i++)
                    if (mods[i] is T t) return t;
                return null;
            }

            internal void Dispose()
            {
                if (contents != null)
                {
                    foreach (ReskinInputItem c in contents)
                        c.Dispose();
                    Pool.FreeUnmanaged(ref contents);
                }
            }

            internal Item Create()
            {
                Item item = ItemManager.Create(itemDefinition, amount, skin);
                item.name = name;
                item.text = text;
                if (item.hasCondition)
                {
                    item.condition = condition;
                    item.maxCondition = maxCondition;
                }
                BaseProjectile bp = item.GetHeldEntity() as BaseProjectile;
                if (bp != null)
                {
                    bp.primaryMagazine.contents = magazineContents;
                    bp.primaryMagazine.capacity = magazineCapacity;
                    bp.primaryMagazine.ammoType = ammoType;
                }
                if (itemModContainerArmourSlot > 0)
                {
                    var slot = FindItemMod<ItemModContainerArmorSlot>(item.info.itemMods);
                    if (slot != null)
                        slot.CreateAtCapacity(itemModContainerArmourSlot, item);
                }
                if (contents?.Count > 0 && item.contents != null)
                {
                    foreach (ReskinInputItem c in contents)
                    {
                        Item att = ItemManager.Create(c.itemDefinition, Mathf.Max(c.amount, 1), c.skin);
                        if (att != null)
                        {
                            if (att.hasCondition) { att.condition = c.condition; att.maxCondition = c.maxCondition; }
                            att.MoveToContainer(item.contents, -1, false);
                            att.MarkDirty();
                        }
                    }
                    item.contents.MarkDirty();
                }
                item.MarkDirty();
                return item;
            }

            internal void CloneTo(Item item)
            {
                item.contents?.SetFlag(ItemContainer.Flag.IsLocked, false);
                item.contents?.SetFlag(ItemContainer.Flag.NoItemInput, false);
                item.amount = amount;
                item.text = text;
                if (item.hasCondition)
                {
                    item.condition = condition;
                    item.maxCondition = maxCondition;
                }
                BaseProjectile bp = item.GetHeldEntity() as BaseProjectile;
                if (bp != null && bp.primaryMagazine != null)
                {
                    bp.primaryMagazine.contents = magazineContents;
                    bp.primaryMagazine.capacity = magazineCapacity;
                    bp.primaryMagazine.ammoType = ammoType;
                }
                if (itemModContainerArmourSlot > 0)
                {
                    var slot = FindItemMod<ItemModContainerArmorSlot>(item.info.itemMods);
                    if (slot != null)
                        slot.CreateAtCapacity(itemModContainerArmourSlot, item);
                }
                if (contents?.Count > 0 && item.contents != null)
                {
                    foreach (ReskinInputItem c in contents)
                    {
                        Item att = ItemManager.Create(c.itemDefinition, c.amount, c.skin);
                        if (att.hasCondition) { att.condition = c.condition; att.maxCondition = c.maxCondition; }
                        att.MoveToContainer(item.contents, -1, false);
                        att.MarkDirty();
                    }
                    item.contents.MarkDirty();
                }
                item.MarkDirty();
            }
        }

        #endregion
        
        #region Configuration
        internal ConfigData Configuration => ConfigurationData as ConfigData;

        internal CommandCallbackHandler CallbackHandler => m_CallbackHandler;

        protected override void OnConfigurationUpdated(VersionNumber oldVersion)
        {
            ConfigData baseConfigData = GenerateDefaultConfiguration();

            if (oldVersion < new VersionNumber(3, 0, 0))
            {
                Configuration.Colors = baseConfigData.Colors;
                Configuration.Purchase.Type = "Scrap";
            }

            if (oldVersion < new VersionNumber(3, 0, 4))
                Configuration.Shop.SkinRefund = 100;

            if (oldVersion < new VersionNumber(3, 0, 10))
            {
                Configuration.Workshop.UseApproved = true;
                Configuration.Workshop.ApprovedIfOwned = true;
            }
        }

        protected override ConfigurationFile OnLoadConfig(ref ConfigurationFile configurationFile) =>
            configurationFile = new ConfigurationFile<ConfigData>(Config);

        protected override T GenerateDefaultConfiguration<T>() => GenerateDefaultConfiguration() as T;

        private static ConfigData GenerateDefaultConfiguration()
        {
            return new ConfigData
            {
                Announcements = new ConfigData.AnnouncementOptions
                {
                    Enabled = true,
                    Interval = 10
                },
                Commands = new ConfigData.CommandOptions
                {
                    DefaultCommand = "skin",
                    ReskinCommand = "reskin",
                    ShopCommand = "skinshop",
                    AddSkinCommand = "addskin"
                },
                Reskin = new ConfigData.ReskinOptions
                {
                    DisableCommand = false,
                    NPCs = new string[0]
                },
                Shop = new ConfigData.ShopOptions
                {
                    BlockedItems = new string[0],
                    DisableCommand = false,
                    ForcedMode = "None",
                    GiveItemOnPurchase = false,
                    NPCs = new string[0],
                    Permissions = new List<string> { "playerskins.vip1", "playerskins.vip2", "playerskins.vip3" },
                    SellSkins = true,
                    SkinRefund = 100,
                    HelpOnExit = true
                },
                Purchase = new ConfigData.PurchaseOptions
                {
                    Type = "Scrap",
                    Enabled = true,
                    DefaultCosts = new Hash<string, int>(),
                },
                Workshop = new ConfigData.WorkshopOptions
                {
                    UseApproved = true,
                    ApprovedIfOwned = true,
                    Enabled = true,
                    Filter = new string[0],
                    SteamAPIKey = string.Empty
                },
                Colors = new ConfigData.UIColors
                {
                    Background = new ConfigData.UIColors.Color
                    {
                        Hex = "151515",
                        Alpha = 0.94f
                    },
                    Panel = new ConfigData.UIColors.Color
                    {
                        Hex = "FFFFFF",
                        Alpha = 0.165f
                    },
                    Button = new ConfigData.UIColors.Color
                    {
                        Hex = "2A2E32",
                        Alpha = 1f
                    },
                    Highlight = new ConfigData.UIColors.Color
                    {
                        Hex = "C4FF00",
                        Alpha = 1f
                    },
                    Close = new ConfigData.UIColors.Color
                    {
                        Hex = "CE422B",
                        Alpha = 1f
                    }
                },
            };
        }
        
        internal class ConfigData : BaseConfigData
        {
            [JsonProperty(PropertyName = "Announcement Options")]
            public AnnouncementOptions Announcements { get; set; }

            [JsonProperty(PropertyName = "Command Options")]
            public CommandOptions Commands { get; set; }

            [JsonProperty(PropertyName = "Purchase Options")]
            public PurchaseOptions Purchase { get; set; }

            [JsonProperty(PropertyName = "Skin Shop Options")]
            public ShopOptions Shop { get; set; }

            [JsonProperty(PropertyName = "Re-skin Options")]
            public ReskinOptions Reskin { get; set; }

            [JsonProperty(PropertyName = "Workshop Options")]
            public WorkshopOptions Workshop { get; set; }

            [JsonProperty(PropertyName = "UI Colors")]
            public UIColors Colors { get; set; }

            public class AnnouncementOptions
            {
                [JsonProperty(PropertyName = "Display help information to players")]
                public bool Enabled { get; set; }

                [JsonProperty(PropertyName = "Information display interval (minutes)")]
                public int Interval { get; set; }
            }

            public class PurchaseOptions
            {
                [JsonProperty(PropertyName = "Enable purchase system")]
                public bool Enabled { get; set; }

                [JsonProperty(PropertyName = "Currency used to purchase skins (ServerRewards, Economics, Scrap)")]
                public string Type { get; set; }

                [JsonProperty(PropertyName = "Default Skin Costs")]
                public Hash<string, int> DefaultCosts { get; set; }
            }

            public class ShopOptions
            {
                [JsonProperty(PropertyName = "Custom permissions which can be assigned to skins")]
                public List<string> Permissions { get; set; }

                [JsonProperty(PropertyName = "NPC user IDs that players can interact with to open the skin shop")]
                public string[] NPCs { get; set; }

                [JsonProperty(PropertyName = "Disable the '/skin shop' command and force players to access it via a NPC")]
                public bool DisableCommand { get; set; }

                [JsonProperty(PropertyName = "Allow players to sell unwanted skins back to the skin store")]
                public bool SellSkins { get; set; }
                
                [JsonProperty(PropertyName = "Selling skin gives back % amount of purchase cost (0 - 100)")]
                public float SkinRefund { get; set; }

                [JsonProperty(PropertyName = "Give player the item when they purchase a skin (this disables the reskin menu)")]
                public bool GiveItemOnPurchase { get; set; }

                [JsonProperty(PropertyName = "Forced display mode for skin shop (Full, Minimalist, None)")]
                public string ForcedMode { get; set; }

                [JsonProperty(PropertyName = "Send a help message to players when exiting the skin shop")]
                public bool HelpOnExit { get; set; }
                
                [JsonProperty(PropertyName = "Hide VIP skins for player who do not have the permission to use it (admins excluded)")]
                public bool HideVIPSkins { get; set; }

                [JsonProperty(PropertyName = "List of shortnames for items to be blocked from appearing in the skin shop")]
                public string[] BlockedItems { get; set; }
            }

            public class ReskinOptions
            {
                [JsonProperty(PropertyName = "NPC user IDs that players can interact with to open the re-skin menu")]
                public string[] NPCs { get; set; }

                [JsonProperty(PropertyName = "Disable the '/skin' command and force players to access it via a NPC")]
                public bool DisableCommand { get; set; }
            }

            public class WorkshopOptions
            {
                [JsonProperty(PropertyName = "Include approved skins (WARNING! Allowing users to use paid content they don't own is against Rusts TOS)")]
                public bool UseApproved { get; set; }
                
                [JsonProperty(PropertyName = "Only show approved skins that the player owns")]
                public bool ApprovedIfOwned { get; set; }

                [JsonProperty(PropertyName = "Enable workshop skins in the skin shop")]
                public bool Enabled { get; set; }

                [JsonProperty(PropertyName = "Word filter for workshop skins. If the skin title partially contains any of these words it will not be available as a potential skin")]
                public string[] Filter { get; set; }

                [JsonProperty(PropertyName = "Steam API key (get one here https://steamcommunity.com/dev/apikey)")]
                public string SteamAPIKey { get; set; }
            }

            public class CommandOptions
            {
                [JsonProperty(PropertyName = "Default chat command")]
                public string DefaultCommand { get; set; }

                [JsonProperty(PropertyName = "Re-skin direct command")]
                public string ReskinCommand { get; set; }

                [JsonProperty(PropertyName = "Skin shop direct command")]
                public string ShopCommand { get; set; }

                [JsonProperty(PropertyName = "Add workshop skin command (players with playerskins.addskin permission)")]
                public string AddSkinCommand { get; set; }
            }

            public class UIColors
            {                
                public Color Background { get; set; }

                public Color Panel { get; set; }
                
                public Color Button { get; set; }

                public Color Highlight { get; set; }

                public Color Close { get; set; }
                
                public class Color
                {
                    public string Hex { get; set; }

                    public float Alpha { get; set; }
                }
            }
        }
        
        private void UpdateDefaultCosts()
        {
            bool hasChanged = false;

            foreach (ItemDefinition itemDefinition in m_SkinnableItems)
            {
                if (!Configuration.Purchase.DefaultCosts.ContainsKey(itemDefinition.shortname))
                {
                    Configuration.Purchase.DefaultCosts[itemDefinition.shortname] = Mathf.Max((int)itemDefinition.rarity, 1) * 10;
                    hasChanged = true;
                    continue;
                }
            }

            foreach(string shortname in Configuration.Purchase.DefaultCosts.Keys.ToList())
            {
                if (!m_SkinnableItems.Any((ItemDefinition itemDefintion) => itemDefintion.shortname.Equals(shortname)))
                {
                    Configuration.Purchase.DefaultCosts.Remove(shortname);
                    hasChanged = true;
                }
            }

            if (hasChanged)
                SaveConfiguration();
        }

        private void UpdateLocalization()
        {
            m_Messages = new Dictionary<string, string>
            {
                ["ServerRewards"] = "RP",
                ["Economics"] = "Coins",
                ["Scrap"] = "Scrap",
                ["UI.Balance"] = "Balance : {0} {1}",
                ["UI.Exit"] = "EXIT",
                ["UI.Popup.Owned"] = "Owned",
                ["UI.Popup.Available"] = "Available",
                ["UI.Popup.Sell"] = "Sell skin ({0} {1})",
                ["UI.Popup.RemoveDefault"] = "Remove as default",
                ["UI.Popup.SetDefault"] = "Set as default",
                ["UI.Popup.PurchasePrice"] = "Purchase ({0} {1})",
                ["UI.Popup.Purchased"] = "Purchased {0}",
                ["UI.Popup.NoPermission"] = "You dont have permission",
                ["UI.Popup.InsufficientFunds"] = "Not Enough ({0} {1})",
                ["UI.Popup.Claim"] = "Claim",
                ["UI.Admin.Permissions"] = "Skin Permission",
                ["Chat.Reskin.NoItem2"] = "You need to hold a item in your hands, or have it equipped in the first slot of your hotbar to open the re-skin menu",
                ["Chat.Reskin.NoSkins"] = "There are no skins available for this item",
                ["Chat.Reskin.NoPurchases"] = "You have not purchased any skins from the skin shop",
                ["UI.Reskin.SkinList"] = "Skins purchased for {0}",
                ["UI.Button.Up"] = "▲ ▲ ▲",
                ["UI.Button.Down"] = "▼ ▼ ▼",
                ["UI.Admin.Enable"] = "Enable skin in store",
                ["UI.Admin.Disable"] = "Disable skin in store",
                ["UI.Admin.Options"] = "Admin Options",
                ["UI.Admin.Price"] = "Price",
                ["Help.Shop.NPC"] = "You can access the skin shop by visiting a skin shop NPC!",
                ["Help.Shop.Command"] = "You can access the skin shop by typing '/skin shop'",
                ["Help.Reskin.NPC"] = "You can apply purchased skins by visiting a reskin NPC!",
                ["Help.Reskin.Command"] = "You can apply purchased skins by typing '/skin' while holding the item in your hands!",
                ["UI.NoSkinsFound"] = "No skins found matching your criteria",
                ["UI.NoShortname"] = "Select a item on the left to continue"
            };
            
            foreach (ItemDefinition itemDefinition in m_SkinnableItems)
            {
                m_Messages[itemDefinition.shortname] = itemDefinition.displayName.english;
            }

            foreach (string perm in Configuration.Shop.Permissions)
            {
                m_Messages[$"UI.VIP.{perm}"] = "VIP skin only";
            }
            
            PlayerSkinsHost.Instance.Lang.RegisterMessages(m_Messages);
        }
        
        #endregion

        #region Data
        private class SkinData
        {
            public string permission = string.Empty;
            public int cost = 1;
            public bool isDisabled = false;
            public bool isApproved = false;

            [JsonIgnore]
            public string Title { get; set; } = string.Empty;

            //[JsonIgnore]
            //public string URL { get; set; } = string.Empty;

            [JsonIgnore]
            public bool IsValid { get; set; } = false;
        }

        private class UserData
        {
            public Dictionary<string, ulong> defaultSkins = new Dictionary<string, ulong>();
            public Dictionary<string, List<ulong>> purchasedSkins = new Dictionary<string, List<ulong>>();
            public DisplayMode displayMode = DisplayMode.Full;

            public UserData() { }

            public bool IsDefaultSkin(string shortname, ulong skinId)
            {
                if (!defaultSkins.ContainsKey(shortname))
                    return false;

                if (defaultSkins[shortname] == skinId)
                    return true;

                return false;
            }

            public bool IsOwned(string shortname, ulong skinId)
            {
                if (!purchasedSkins.ContainsKey(shortname))
                    return false;

                if (purchasedSkins[shortname].Contains(skinId))
                    return true;

                return false;
            }
        }
        
        private class WorkshopItem
        {
            public string title;
            public string description;
            public string imageUrl;

            public WorkshopItem() { }

            public WorkshopItem(PublishedFileDetails item)
            {
                if (item == null)
                    return;

                title = item.title;
                description = item.file_description;
                imageUrl = item.preview_url.Replace("https", "http");
            }
                     

            public WorkshopItem(InventoryDef item, string url)
            {
                if (item == null)
                    return;

                title = item.Name;
                description = item.Description;
                imageUrl = url == null ? string.Empty : url.Replace("https", "http");
            }
        }

        public class QueryResponse
        {
            public Response response;
        }

        public class Response
        {
            public int total;
            public PublishedFileDetails[] publishedfiledetails;
        }

        public class PublishedFileDetails
        {
            public int result;
            public string publishedfileid;
            public string creator;
            public int creator_appid;
            public int consumer_appid;
            public int consumer_shortcutid;
            public string filename;
            public string file_size;
            public string preview_file_size;
            public string file_url;
            public string preview_url;
            public string url;
            public string hcontent_file;
            public string hcontent_preview;
            public string title;
            public string file_description;
            public int time_created;
            public int time_updated;
            public int visibility;
            public int flags;
            public bool workshop_file;
            public bool workshop_accepted;
            public bool show_subscribe_all;
            public int num_comments_public;
            public bool banned;
            public string ban_reason;
            public string banner;
            public bool can_be_deleted;
            public string app_name;
            public int file_type;
            public bool can_subscribe;
            public int subscriptions;
            public int favorited;
            public int followers;
            public int lifetime_subscriptions;
            public int lifetime_favorited;
            public int lifetime_followers;
            public string lifetime_playtime;
            public string lifetime_playtime_sessions;
            public int views;
            public int num_children;
            public int num_reports;
            public Preview[] previews;
            public Tag[] tags;
            public int language;
            public bool maybe_inappropriate_sex;
            public bool maybe_inappropriate_violence;

            public class Tag
            {
                public string tag;
                public bool adminonly;
            }

        }

        public class Preview
        {
            public string previewid;
            public int sortorder;
            public string url;
            public int size;
            public string filename;
            public int preview_type;
            public string youtubevideoid;
            public string external_reference;
        }

        public class CollectionQueryResponse
        {
            public CollectionResponse response { get; set; }
        }

        public class CollectionResponse
        {
            public int result { get; set; }
            public int resultcount { get; set; }
            public CollectionDetails[] collectiondetails { get; set; }
        }

        public class CollectionDetails
        {
            public string publishedfileid { get; set; }
            public int result { get; set; }
            public CollectionChild[] children { get; set; }
        }

        public class CollectionChild
        {
            public string publishedfileid { get; set; }
            public int sortorder { get; set; }
            public int filetype { get; set; }
        }
        #endregion 

        // ---- Harmony lifecycle (replaces Oxide Loaded / OnServerInitialized / Unload) ----
        public override void HarmonyInit()
        {
            s_Instance = this;
            LoadConfig();
	        m_UserData = new Datafile<Hash<ulong, UserData>>("PlayerSkins/userdata");
	        m_SkinData = new Datafile<Hash<string, Hash<ulong, SkinData>>>("PlayerSkins/skinlist");
	        m_ExcludedSkins = new Datafile<List<ulong>>("PlayerSkins/excludedskins");
	        
            SetupUIComponents();
            PlayerSkinsHost.Instance?.ReloadLanguage();
        }

        public override void HarmonyServerInitialized()
        {
            OnServerInitialized();
        }

        public override void HarmonyUnload()
        {
            Unload();
        }
    }       
}    
        