using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Facepunch;
using HarmonyLib;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Rust;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("IndustrialRecycler", "Marte6", "1.9.1")]
    [Description("Automate recycling by attaching storage and adapters to recyclers.")]
    public partial class IndustrialRecycler : RustPlugin
    {
        [PluginReference]
        Plugin Friends,
            NoEscape;
        private static IndustrialRecycler _instance;
        private const string RecyclerPrefabPath = "assets/bundled/prefabs/static/recycler_static.prefab";
        private const string InputStoragePrefabPath = "assets/content/vehicles/boats/rowboat/subents/rowboat_storage.prefab";
        private const string OutputStoragePrefabPath = "assets/prefabs/deployable/hot air balloon/subents/hab_storage.prefab";
        private const string AdapterPrefabPath = "assets/prefabs/deployable/playerioents/industrialadaptors/storageadaptor.deployed.prefab";
        private const string PermissionUseRecycler = "industrialrecycler.give";
        private const string PermissionVip1 = "industrialrecycler.vip";
        private const string PermissionVip2 = "industrialrecycler.vip2";
        private const string PermissionAdmin = "industrialrecycler.admin";
        private const string PermissionBuyStandard = "industrialrecycler.buystandard";
        private const string PermissionBuyIndustrial = "industrialrecycler.buyindustrial";
        private const string PermissionToggle = "industrialrecycler.toggle";
        private const string PermissionPickup = "industrialrecycler.pickup";
        private const string PermissionSpeed1 = "industrialrecycler.speed1";
        private const string PermissionSpeed2 = "industrialrecycler.speed2";
        private const string PermissionSpeed3 = "industrialrecycler.speed3";
        private const string PermissionSpeed4 = "industrialrecycler.speed4";
        private const string PermissionVirtualSpeed1 = "industrialrecycler.virtualspeed1";
        private const string PermissionVirtualSpeed2 = "industrialrecycler.virtualspeed2";
        private const string PermissionVirtualSpeed3 = "industrialrecycler.virtualspeed3";
        private const string PermissionVirtualSpeed4 = "industrialrecycler.virtualspeed4";
        private const string PermissionEfficiency1 = "industrialrecycler.efficiency1";
        private const string PermissionEfficiency2 = "industrialrecycler.efficiency2";
        private const string PermissionEfficiency3 = "industrialrecycler.efficiency3";
        private const string PermissionEfficiency4 = "industrialrecycler.efficiency4";
        private const string PermissionVirtualEfficiency1 = "industrialrecycler.virtualefficiency1";
        private const string PermissionVirtualEfficiency2 = "industrialrecycler.virtualefficiency2";
        private const string PermissionVirtualEfficiency3 = "industrialrecycler.virtualefficiency3";
        private const string PermissionVirtualEfficiency4 = "industrialrecycler.virtualefficiency4";
        private const string PermissionVirtual = "industrialrecycler.virtual";
        private const string UiOverlayName = "IndustrialRecyclerMainPanel";
        private const string StorageUiOverlayName = "IndustrialRecyclerStorageNav";
        private const string PurchaseUiOverlayName = "PurchaseUIScreen";
        private readonly Dictionary<ulong, ulong> _recyclerOwners = new();
        private readonly HashSet<ulong> _recyclerItemContainers = new();
        private readonly HashSet<ulong> _inputItemContainers = new();
        private readonly HashSet<ulong> _storageAdapters = new();
        private readonly HashSet<ulong> _playersWithUi = new();
        private const string RunEffect = "assets/prefabs/npc/autoturret/effects/offline.prefab";
        private const string StartEffect = "assets/prefabs/gamemodes/objects/capturepoint/effects/capturepoint_progress_beep.prefab";
        private const string DroppedItemPrefab = "assets/prefabs/misc/item drop/item_drop.prefab";
        private readonly Dictionary<ulong, Recycler> _playerVirtualRecycler = new();
        private readonly List<RecyclerSaveEntry> _savedRecyclers = new List<RecyclerSaveEntry>();
        private readonly HashSet<ulong> _industrialRecyclers = new HashSet<ulong>();
        private readonly HashSet<ulong> _standardRecyclers = new HashSet<ulong>();
        private readonly HashSet<ulong> _pickupInProgress = new HashSet<ulong>();
        private readonly HashSet<ulong> _purchaseInProgress = new HashSet<ulong>();
        private readonly Dictionary<ulong, ulong> _pendingOutputTransfers = new Dictionary<ulong, ulong>();
        private Configuration _config;

        private class RecyclerSaveEntry
        {
            public ulong OwnerID;
            public Vector3 Position;
            public bool IsIndustrial;
            public int Layout;
        }

        void Init()
        {
            permission.RegisterPermission(PermissionUseRecycler, this);
            permission.RegisterPermission(PermissionVip1, this);
            permission.RegisterPermission(PermissionVip2, this);
            permission.RegisterPermission(PermissionAdmin, this);
            permission.RegisterPermission(PermissionToggle, this);
            permission.RegisterPermission(PermissionBuyStandard, this);
            permission.RegisterPermission(PermissionBuyIndustrial, this);
            permission.RegisterPermission(PermissionPickup, this);
            permission.RegisterPermission(PermissionSpeed1, this);
            permission.RegisterPermission(PermissionSpeed2, this);
            permission.RegisterPermission(PermissionSpeed3, this);
            permission.RegisterPermission(PermissionSpeed4, this);
            permission.RegisterPermission(PermissionVirtual, this);
            permission.RegisterPermission(PermissionVirtualSpeed1, this);
            permission.RegisterPermission(PermissionVirtualSpeed2, this);
            permission.RegisterPermission(PermissionVirtualSpeed3, this);
            permission.RegisterPermission(PermissionVirtualSpeed4, this);
            permission.RegisterPermission(PermissionEfficiency1, this);
            permission.RegisterPermission(PermissionEfficiency2, this);
            permission.RegisterPermission(PermissionEfficiency3, this);
            permission.RegisterPermission(PermissionEfficiency4, this);
            permission.RegisterPermission(PermissionVirtualEfficiency1, this);
            permission.RegisterPermission(PermissionVirtualEfficiency2, this);
            permission.RegisterPermission(PermissionVirtualEfficiency3, this);
            permission.RegisterPermission(PermissionVirtualEfficiency4, this);
            CustomizeCommands();
        }

        void OnServerInitialized()
        {
            _instance = this;
            DeleteDataFile("IndustrialRecycler_Industrial");
            DeleteDataFile("IndustrialRecycler_Standard");
            ServerMgr.Instance.StartCoroutine(InitializeRecyclers());
        }

        void Unload()
        {
            foreach (var comp in BaseNetworkable.serverEntities.OfType<RecyclerComponent>())
                UnityEngine.Object.Destroy(comp);
            _savedRecyclers.Clear();
            _industrialRecyclers.Clear();
            _standardRecyclers.Clear();
            _inputItemContainers.Clear();
            _recyclerItemContainers.Clear();
            _storageAdapters.Clear();
            _playersWithUi.Clear();
            _purchaseInProgress.Clear();
            _pendingOutputTransfers.Clear();
            _industrialRecyclers.Clear();
            _standardRecyclers.Clear();
            foreach (var r in _playerVirtualRecycler.Values.Where(r => r != null && !r.IsDestroyed).ToList())
                r.Kill();
            _playerVirtualRecycler.Clear();
            foreach (var p in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(p, UiOverlayName);
                CuiHelper.DestroyUi(p, PurchaseUiOverlayName);
                CuiHelper.DestroyUi(p, StorageUiOverlayName);
            }
        }

        void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (container == null || item == null || !item.IsValid())
                return;
            if (_pendingOutputTransfers.TryGetValue(item.uid.Value, out ulong recyclerId))
            {
                ServerMgr.Instance.StartCoroutine(RedirectPendingItemToOutput(item, recyclerId));
            }
            Recycler recycler = GetRecyclerFromItemContainer(container);
            if (recycler == null)
                return;
            var rc = recycler.GetComponent<RecyclerComponent>();
            if (rc == null)
                return;
            ServerMgr.Instance.StartCoroutine(ItemAddedToContainer(container, item, rc));
        }

        void OnItemRemovedFromContainer(ItemContainer container, Item item)
        {
            if (container == null || item == null || !item.IsValid())
                return;
            Item parentItem = container.parent;
            ItemContainer parentContainer = parentItem?.parent;
            if (parentItem == null || parentContainer == null)
                return;
            Recycler recycler = GetRecyclerFromItemContainer(parentContainer);
            if (recycler == null || recycler.GetComponent<RecyclerComponent>() == null || recycler.net == null)
                return;
            ulong itemId = item.uid.Value;
            ulong recyclerId = recycler.net.ID.Value;
            _pendingOutputTransfers[itemId] = recyclerId;
            ServerMgr.Instance.StartCoroutine(RedirectPendingItemToOutput(item, recyclerId));
            timer.Once(
                2f,
                () =>
                {
                    if (_pendingOutputTransfers.TryGetValue(itemId, out ulong pendingRecyclerId) && pendingRecyclerId == recyclerId)
                        _pendingOutputTransfers.Remove(itemId);
                }
            );
        }

        void OnEntityBuilt(Planner planner, GameObject entityObject)
        {
            var player = planner?.GetOwnerPlayer();
            var baseEntity = entityObject?.GetComponent<BaseEntity>();
            if (player == null || baseEntity == null)
                return;
            if (baseEntity.skinID != _config.IndustrialRecyclerSkinId && baseEntity.skinID != _config.StandardRecyclerSkinId)
                return;
            bool isIndustrial = baseEntity.skinID == _config.IndustrialRecyclerSkinId;
            int maxRecyclers = GetMaxRecyclers(player, isIndustrial);
            int currentCount = CountPlayerRecyclers(player, isIndustrial);
            if (currentCount >= maxRecyclers)
            {
                player.ChatMessage(lang.GetMessage("RecyclerLimitReached", this, player.UserIDString));
                timer.Once(1f, () => GiveRecyclerItem(player, !isIndustrial));
                NextTick(() =>
                {
                    if (baseEntity != null && !baseEntity.IsDestroyed)
                        baseEntity.Kill();
                });
                return;
            }
            if (!InitializeRecyclerPlacement(player, baseEntity))
            {
                player.ChatMessage(lang.GetMessage("InvalidPlacement", this, player.UserIDString));
                timer.Once(1f, () => GiveRecyclerItem(player, !isIndustrial));
            }
            else
            {
                player.ChatMessage(lang.GetMessage("RecyclerPlaced", this, player.UserIDString));
            }
            NextTick(() =>
            {
                if (baseEntity != null && !baseEntity.IsDestroyed)
                    baseEntity.Kill();
            });
        }

        object CanPickupEntity(BasePlayer player, IndustrialStorageAdaptor adapter)
        {
            if (adapter?.net?.ID.Value != null && _storageAdapters.Contains(adapter.net.ID.Value))
            {
                if (!HasAccess(player, adapter))
                {
                    player.ChatMessage(lang.GetMessage("NoAccess", this, player.UserIDString));
                    return false;
                }
                if (!permission.UserHasPermission(player.UserIDString, PermissionPickup))
                {
                    player.ChatMessage(lang.GetMessage("NoPickupPermission", this, player.UserIDString));
                    return false;
                }
                adapter.GetParentEntity()?.GetParentEntity()?.Kill();
                GiveRecyclerItem(player, false);
                player.ChatMessage(lang.GetMessage("RecyclerPickedUp", this, player.UserIDString));
                return false;
            }
            return null;
        }

        object canRemove(BasePlayer player, IndustrialStorageAdaptor adapter)
        {
            if (adapter?.net?.ID.Value != null && _storageAdapters.Contains(adapter.net.ID.Value))
                return false;
            return null;
        }

        object OnEntityKill(Recycler recycler)
        {
            if (recycler == null || recycler.net == null)
                return null;
            if (_playerVirtualRecycler.ContainsValue(recycler))
            {
                return null;
            }
            bool wasIndustrial = _industrialRecyclers.Contains(recycler.net.ID.Value);
            bool suppressDropItem = _pickupInProgress.Contains(recycler.net.ID.Value);
            if (wasIndustrial || _standardRecyclers.Contains(recycler.net.ID.Value))
                UnregisterRecyclerOwnership(recycler);
            if (_config.DropRecyclerItemOnDestroy && !suppressDropItem)
            {
                bool isStandard = !wasIndustrial;
                ulong skinId = isStandard ? _config.StandardRecyclerSkinId : _config.IndustrialRecyclerSkinId;
                var recyclerItem = ItemManager.CreateByName(_config.BaseItem, 1, skinId);
                if (recyclerItem != null)
                {
                    recyclerItem.name = isStandard ? "Standard Recycler" : "Industrial Recycler";
                    recyclerItem.Drop(recycler.transform.position + Vector3.up, Vector3.zero);
                }
            }
            DropRecyclerItems(recycler);
            _pickupInProgress.Remove(recycler.net.ID.Value);
            return null;
        }

        object CanLootEntity(BasePlayer player, Recycler recycler)
        {
            if (player == null || recycler == null || recycler.net == null)
                return null;
            if (recycler.OwnerID != 0UL && _recyclerOwners.ContainsKey(recycler.net.ID.Value) && !HasAccess(player, recycler))
            {
                player.ChatMessage(lang.GetMessage("NoAccess", this, player.UserIDString));
                return false;
            }
            return null;
        }

        object CanLootEntity(BasePlayer player, StorageContainer storage)
        {
            if (player == null || storage == null)
                return null;
            Recycler recycler = storage.GetParentEntity() as Recycler;
            if (recycler != null && recycler.OwnerID != 0UL && _recyclerOwners.ContainsKey(recycler.net.ID.Value) && !HasAccess(player, recycler))
            {
                player.ChatMessage(lang.GetMessage("NoAccess", this, player.UserIDString));
                return false;
            }
            return null;
        }

        void OnLootEntity(BasePlayer player, Recycler recycler)
        {
            if (player == null || recycler == null || recycler.net == null)
                return;
            if (recycler.OwnerID != 0UL && _recyclerOwners.ContainsKey(recycler.net.ID.Value) && IsRecyclerIndustrial(recycler))
            {
                _playersWithUi.Add(player.userID);
                string recyclerId = recycler.net.ID.Value.ToString();
                CreateUi(player, recyclerId);
            }
        }

        void OnPlayerLootEnd(PlayerLoot playerLoot)
        {
            if (playerLoot == null)
                return;
            BasePlayer player = playerLoot.baseEntity;
            if (player == null || player.IsDestroyed)
                return;
            if (_playersWithUi.Contains(player.userID))
            {
                _playersWithUi.Remove(player.userID);
                CuiHelper.DestroyUi(player, UiOverlayName);
            }
            CuiHelper.DestroyUi(player, StorageUiOverlayName);
        }

        object OnHammerHit(BasePlayer player, HitInfo hitInfo)
        {
            if (player == null || hitInfo == null || hitInfo.HitEntity == null)
                return null;
            BaseEntity target = hitInfo.HitEntity;
            Recycler recycler = target as Recycler ?? target.GetParentEntity() as Recycler ?? target.GetParentEntity()?.GetParentEntity() as Recycler;
            if (recycler == null)
                return null;
            if (recycler.OwnerID == 0UL)
                return null;
            if (recycler.net == null)
                return null;
            if (!HasAccess(player, recycler))
            {
                player.ChatMessage(lang.GetMessage("NoAccess", this, player.UserIDString));
                return null;
            }
            if (!permission.UserHasPermission(player.UserIDString, PermissionPickup))
            {
                player.ChatMessage(lang.GetMessage("NoPickupPermission", this, player.UserIDString));
                return false;
            }
            bool isIndustrial = IsRecyclerIndustrial(recycler);
            ulong id = recycler.net.ID.Value;
            _pickupInProgress.Add(id);
            recycler.Kill();
            GiveRecyclerItem(player, !isIndustrial);
            player.ChatMessage(lang.GetMessage("RecyclerPickedUp", this, player.UserIDString));
            _pickupInProgress.Remove(id);
            return null;
        }

        void OnLootEntityEnd(BasePlayer player, Recycler recycler)
        {
            if (player != null)
                CloseVirtualRecycler(player);
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player != null)
                CloseVirtualRecycler(player);
        }

        object OnRecyclerToggle(Recycler recycler, BasePlayer player)
        {
            if (!_config.PlaySoundOnVirtual)
                return null;
            if (player == null || recycler == null)
                return null;
            if (!_playerVirtualRecycler.TryGetValue(player.userID, out var mine) || mine != recycler)
                return null;
            if (!recycler.IsOn())
                Effect.server.Run(StartEffect, player, 0u, Vector3.zero, Vector3.zero);
            return null;
        }

        object OnItemRecycle(Item item, Recycler recycler)
        {
            if (!_config.PlaySoundOnVirtual)
                return null;
            var owner = GetOwner(recycler);
            if (owner != null)
                Effect.server.Run(RunEffect, owner, 0u, Vector3.zero, Vector3.zero);
            return null;
        }

        private void CustomizeCommands()
        {
            var registeredChatCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            RegisterChatCommands(_config.StandardPurchaseCommands ?? Enumerable.Empty<string>(), nameof(CommandOpenStandardPurchaseUI), "standard purchase", registeredChatCommands);
            RegisterChatCommands(_config.IndustrialPurchaseCommands ?? Enumerable.Empty<string>(), nameof(CommandOpenIndustrialPurchaseUI), "industrial purchase", registeredChatCommands);
            RegisterChatCommands(_config.VirtualUseCommands ?? Array.Empty<string>(), nameof(CommandIrec), "virtual recycler", registeredChatCommands);
            RegisterChatCommands(new[] { "upgraderecycler" }, nameof(CommandUpgradeRecycler), "upgrade", registeredChatCommands);
            RegisterChatCommands(new[] { "togglerecycler" }, nameof(ToggleRecyclerConfig), "toggle", registeredChatCommands);
            RegisterChatCommands(_config.RecyclerCommands ?? Enumerable.Empty<string>(), nameof(CommandGetRecycler), "industrial give", registeredChatCommands);
            RegisterChatCommands(_config.StandardRecyclerCommands ?? Enumerable.Empty<string>(), nameof(CommandGetStandardRecycler), "standard give", registeredChatCommands);
            cmd.AddConsoleCommand("industrialrecycler.openinput", this, nameof(OpenInputStorage));
            cmd.AddConsoleCommand("industrialrecycler.openoutput", this, nameof(OpenOutputStorage));
            cmd.AddConsoleCommand("giveindustrialrecycler", this, nameof(ConsoleCommandGiveRecycler));
            cmd.AddConsoleCommand("givestandardrecycler", this, nameof(ConsoleCommandGiveStandardRecycler));
            cmd.AddConsoleCommand("buystandard.confirm", this, nameof(ConfirmPurchaseStandard));
            cmd.AddConsoleCommand("buyindustrial.confirm", this, nameof(ConfirmPurchaseIndustrial));
            cmd.AddConsoleCommand("buyrecycler.close", this, nameof(ClosePurchaseUI));
            cmd.AddConsoleCommand("industrialrecycler.openrecycler", this, nameof(OpenRecyclerInventory));
        }

        private void RegisterChatCommands(IEnumerable<string> commands, string handler, string purpose, HashSet<string> registeredCommands)
        {
            if (commands == null)
                return;
            foreach (string configuredCommand in commands)
            {
                string command = configuredCommand?.Trim().TrimStart('/');
                if (string.IsNullOrEmpty(command))
                    continue;
                if (!registeredCommands.Add(command))
                {
                    PrintWarning($"Skipping duplicate chat command '/{command}' configured for {purpose}.");
                    continue;
                }
                cmd.AddChatCommand(command, this, handler);
            }
        }

        private void DeleteDataFile(string file)
        {
            string filePath = Path.Combine(Interface.Oxide.DataFileSystem.Directory, $"{file}.json");
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception) { }
            }
        }

        private bool IsRecyclerIndustrial(Recycler recycler)
        {
            if (recycler == null || recycler.IsDestroyed)
                return false;
            string inputShort = Path.GetFileNameWithoutExtension(InputStoragePrefabPath);
            string outputShort = Path.GetFileNameWithoutExtension(OutputStoragePrefabPath);
            return recycler
                .GetComponentsInChildren<StorageContainer>(true)
                .Any(sc =>
                {
                    if (sc == null || sc.IsDestroyed)
                        return false;
                    var pref = sc.PrefabName ?? string.Empty;
                    var shortName = sc.ShortPrefabName ?? string.Empty;
                    return pref.Equals(InputStoragePrefabPath, StringComparison.OrdinalIgnoreCase)
                        || pref.Equals(OutputStoragePrefabPath, StringComparison.OrdinalIgnoreCase)
                        || shortName.Equals(inputShort, StringComparison.OrdinalIgnoreCase)
                        || shortName.Equals(outputShort, StringComparison.OrdinalIgnoreCase);
                });
        }

        private Recycler GetRecyclerFromItemContainer(ItemContainer container)
        {
            if (container == null)
                return null;
            BaseEntity entityOwner = container.entityOwner;
            if (entityOwner is Recycler recycler)
                return recycler;
            BaseEntity parent = entityOwner?.GetParentEntity();
            return parent as Recycler ?? parent?.GetParentEntity() as Recycler;
        }

        private IEnumerator RedirectPendingItemToOutput(Item item, ulong recyclerId)
        {
            yield return CoroutineEx.waitForEndOfFrame;
            if (item == null)
                yield break;
            ulong itemId = item.uid.Value;
            if (!item.IsValid() || item.info == null || item.amount <= 0)
            {
                _pendingOutputTransfers.Remove(itemId);
                yield break;
            }
            if (!_pendingOutputTransfers.TryGetValue(itemId, out ulong pendingRecyclerId) || pendingRecyclerId != recyclerId)
                yield break;
            var recycler = BaseNetworkable.serverEntities.Find(new NetworkableId(recyclerId)) as Recycler;
            var component = recycler?.GetComponent<RecyclerComponent>();
            var outputInventory = component?.OutputStorage?.inventory;
            if (recycler == null || component == null || outputInventory == null)
            {
                _pendingOutputTransfers.Remove(itemId);
                yield break;
            }
            if (item.parent == outputInventory)
            {
                _pendingOutputTransfers.Remove(itemId);
                yield break;
            }
            item.RemoveFromContainer();
            if (!SafeMoveToContainer(item, outputInventory))
                item.Drop(recycler.transform.position + Vector3.up, recycler.transform.forward * 2f);
            _pendingOutputTransfers.Remove(itemId);
        }

        private IEnumerator InitializeRecyclers()
        {
            yield return CoroutineEx.waitForEndOfFrame;
            yield return new WaitForSeconds(1f);
            foreach (Recycler recycler in BaseNetworkable.serverEntities.OfType<Recycler>())
            {
                if (recycler == null || recycler.IsDestroyed || recycler.net == null)
                    continue;
                if (_recyclerOwners.ContainsKey(recycler.net.ID.Value))
                    continue;
                bool isIndustrial = IsRecyclerIndustrial(recycler);
                if (isIndustrial)
                {
                    if (recycler.GetComponent<RecyclerComponent>() == null)
                        recycler.gameObject.AddComponent<RecyclerComponent>();
                    ApplyEfficiency(recycler, GetConfiguredEfficiencyFor(recycler.OwnerID, false));
                    _industrialRecyclers.Add(recycler.net.ID.Value);
                }
                else
                {
                    ApplyEfficiency(recycler, GetConfiguredEfficiencyFor(recycler.OwnerID, false));
                    _standardRecyclers.Add(recycler.net.ID.Value);
                }
                _recyclerOwners[recycler.net.ID.Value] = recycler.OwnerID;
            }
        }

        private IEnumerator ItemAddedToContainer(ItemContainer container, Item item, RecyclerComponent rc)
        {
            yield return CoroutineEx.waitForEndOfFrame;
            if (container == null || rc == null || item == null || !item.IsValid())
                yield break;
            bool isRecyclerInv = container.entityOwner is Recycler;
            bool isInputStorage = !isRecyclerInv && _inputItemContainers.Contains(container.uid.Value);
            if (isRecyclerInv && _recyclerItemContainers.Contains(container.uid.Value))
                ProcessRecyclerItems(rc, item);
            else if (isInputStorage)
                rc.HandleItemTransfers();
        }

        private void UnregisterRecyclerOwnership(Recycler recycler)
        {
            _recyclerOwners.Remove(recycler.net.ID.Value);
            _industrialRecyclers.Remove(recycler.net.ID.Value);
            _standardRecyclers.Remove(recycler.net.ID.Value);
            _savedRecyclers.RemoveAll(e => Vector3.Distance(e.Position, recycler.transform.position) < 0.05f);
        }

        private void RegisterRecyclerOwnership(Recycler recycler, bool isIndustrial)
        {
            _recyclerOwners[recycler.net.ID.Value] = recycler.OwnerID;
            int layout = 2;
            if (isIndustrial)
            {
                var comp = recycler.GetComponent<RecyclerComponent>();
                if (comp != null)
                    layout = comp.CurrentConfigState;
            }
            _savedRecyclers.RemoveAll(e => Vector3.Distance(e.Position, recycler.transform.position) < 0.05f);
            _savedRecyclers.Add(
                new RecyclerSaveEntry
                {
                    OwnerID = recycler.OwnerID,
                    Position = recycler.transform.position,
                    IsIndustrial = isIndustrial,
                    Layout = layout,
                }
            );
            _industrialRecyclers.Remove(recycler.net.ID.Value);
            _standardRecyclers.Remove(recycler.net.ID.Value);
            if (isIndustrial)
                _industrialRecyclers.Add(recycler.net.ID.Value);
            else
                _standardRecyclers.Add(recycler.net.ID.Value);
        }

        private void UpdateRecyclerLayout(Recycler recycler, int layout)
        {
            foreach (var entry in _savedRecyclers)
                if (Vector3.Distance(entry.Position, recycler.transform.position) < 0.05f)
                {
                    entry.Layout = layout;
                    break;
                }
        }

        private bool HasAccess(BasePlayer player, BaseEntity entity)
        {
            ulong ownerId = entity.OwnerID;
            if (ownerId == player.userID || permission.UserHasPermission(player.UserIDString, PermissionAdmin))
                return true;
            if (!_config.OnlyOwnerAccess)
                return true;
            if (_config.AllowTeamAccess && player.currentTeam != 0)
            {
                var team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                if (team != null && team.members.Contains(ownerId))
                    return true;
            }
            if (_config.AllowFriendsAccess && Friends != null)
            {
                bool areFriends = (bool)(Friends.CallHook("AreFriends", ownerId, player.userID) ?? false);
                if (areFriends)
                    return true;
            }
            return false;
        }

        private List<KeyValuePair<string, ulong>> GetItemsForImagePreload()
        {
            var itemIcons = new List<KeyValuePair<string, ulong>>();
            foreach (ResourceItem item in _config.PurchaseIndustrialCost ?? new List<ResourceItem>())
                if (item != null && !string.IsNullOrWhiteSpace(item.Shortname))
                    itemIcons.Add(new KeyValuePair<string, ulong>(item.Shortname, 0));
            foreach (ResourceItem item in _config.PurchaseStandardCost ?? new List<ResourceItem>())
                if (item != null && !string.IsNullOrWhiteSpace(item.Shortname))
                    itemIcons.Add(new KeyValuePair<string, ulong>(item.Shortname, 0));
            return itemIcons;
        }

        private bool SafeMoveToContainer(Item item, ItemContainer target)
        {
            if (item == null || target == null || !item.IsValid() || item.info == null || item.amount <= 0)
                return false;
            if (item.parent == target)
                return true;
            return item.MoveToContainer(target);
        }

        private bool CanPlayerAccessRecycler(BasePlayer player)
        {
            if (player.IsSwimming())
            {
                player.ChatMessage(lang.GetMessage("Denied Swimming", this, player.UserIDString));
            }
            else if (!player.IsOnGround() || player.IsFlying)
            {
                player.ChatMessage(lang.GetMessage("Denied Falling", this, player.UserIDString));
            }
            else if (player.isMounted || player.GetParentEntity() is BaseMountable)
            {
                player.ChatMessage(lang.GetMessage("Denied Mounted", this, player.UserIDString));
            }
            else if (player.GetComponentInParent<CargoShip>())
            {
                player.ChatMessage(lang.GetMessage("Denied Ship", this, player.UserIDString));
            }
            else if (player.GetComponentInParent<HotAirBalloon>())
            {
                player.ChatMessage(lang.GetMessage("Denied Balloon", this, player.UserIDString));
            }
            else if (player.GetComponentInParent<Lift>())
            {
                player.ChatMessage(lang.GetMessage("Denied Elevator", this, player.UserIDString));
            }
            else
            {
                return true;
            }
            return false;
        }

        private void CommandIrec(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;
            if (!permission.UserHasPermission(player.UserIDString, PermissionVirtual))
            {
                player.ChatMessage(lang.GetMessage("NoPermission", this, player.UserIDString));
                return;
            }
            if (NoEscape != null)
            {
                if (_config.DisableVirtualDuringCombatBlock && NoEscape.Call<bool>("IsCombatBlocked", player))
                {
                    player.ChatMessage(lang.GetMessage("IsCombatBlocked", this, player.UserIDString));
                    return;
                }
                if (_config.DisableVirtualDuringRaidBlock && NoEscape.Call<bool>("IsRaidBlocked", player))
                {
                    player.ChatMessage(lang.GetMessage("IsRaidBlocked", this, player.UserIDString));
                    return;
                }
            }
            if (!CanPlayerAccessRecycler(player))
                return;
            OpenVirtualRecycler(player);
        }

        private void OpenVirtualRecycler(BasePlayer player)
        {
            CloseVirtualRecycler(player);
            var pos = player.transform.position + Vector3.down * 1.5f;
            var entity = GameManager.server.CreateEntity(RecyclerPrefabPath, pos);
            var recycler = entity as Recycler;
            if (recycler == null)
                return;
            recycler.enableSaving = false;
            recycler.OwnerID = player.userID;
            recycler.requireAuthIfNotLocked = false;
            foreach (var c in recycler.GetComponentsInChildren<Collider>())
                UnityEngine.Object.Destroy(c);
            recycler._limitedNetworking = true;
            recycler.SetFlag(BaseEntity.Flags.Disabled, true);
            recycler.Spawn();
            UnityEngine.Object.DestroyImmediate(recycler.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.DestroyImmediate(recycler.GetComponent<GroundWatch>());
            recycler.GetComponent<DecayEntity>().decay = null;
            BaseEntity.Query.Server.Remove(recycler);
            recycler._limitedNetworking = false;
            recycler.SetFlag(BaseEntity.Flags.Locked, true);
            recycler.UpdateNetworkGroup();
            recycler.gameObject.layer = 0;
            recycler.SendNetworkUpdateImmediate();
            ApplyEfficiency(recycler, GetConfiguredEfficiencyFor(player.userID, true));
            recycler.gameObject.AddComponent<VirtualRecyclerBehaviour>().Init(player, recycler);
            _playerVirtualRecycler[player.userID] = recycler;
        }

        private void CloseVirtualRecycler(BasePlayer player)
        {
            if (!_playerVirtualRecycler.TryGetValue(player.userID, out var recycler))
                return;
            if (recycler == null || recycler.IsDestroyed)
            {
                _playerVirtualRecycler.Remove(player.userID);
                return;
            }
            if (recycler.inventory?.itemList?.Count > 0)
                DropLeftoverItems(recycler, player);
            recycler.Kill();
            _playerVirtualRecycler.Remove(player.userID);
        }

        private void DropLeftoverItems(Recycler recycler, BasePlayer player)
        {
            var drop = GameManager.server.CreateEntity(DroppedItemPrefab, player.transform.position + Vector3.up) as DroppedItemContainer;
            if (drop == null)
                return;
            drop.enableSaving = false;
            drop.lootPanelName = "generic_resizable";
            drop.playerSteamID = player.userID;
            drop.TakeFrom(new[] { recycler.inventory }, 0f);
            drop.Spawn();
            player.ChatMessage(lang.GetMessage("ItemsDropped", this, player.UserIDString));
        }

        private BasePlayer GetOwner(Recycler recycler)
        {
            foreach (var pair in _playerVirtualRecycler)
                if (pair.Value == recycler)
                    return BasePlayer.FindByID(pair.Key);
            return null;
        }

        private class VirtualRecyclerBehaviour : FacepunchBehaviour
        {
            private BasePlayer _owner;
            private Recycler _recycler;

            public void Init(BasePlayer owner, Recycler recycler)
            {
                _owner = owner;
                _recycler = recycler;
                _owner.Invoke(OpenLoot, 0.2f);
            }

            private void OpenLoot()
            {
                if (_owner == null || _owner.IsDestroyed || _recycler == null || _recycler.IsDestroyed)
                    return;
                _owner.EndLooting();
                if (!_owner.inventory.loot.StartLootingEntity(_recycler, false))
                    return;
                _owner.inventory.loot.AddContainer(_recycler.inventory);
                _owner.inventory.loot.SendImmediate();
                _owner.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", _owner), _recycler.panelName);
                _owner.SendNetworkUpdate();
            }

            private void OnDestroy()
            {
                if (_instance != null && _owner != null)
                    _instance._playerVirtualRecycler.Remove(_owner.userID);
            }
        }

        private void CreateUi(BasePlayer player, string recyclerId)
        {
            var container = new CuiElementContainer();
            container.Add(
                new CuiPanel
                {
                    CursorEnabled = true,
                    Image =
                    {
                        Color = "1 1 1 0.15",
                        Sprite = "assets/content/ui/ui.background.transparent.radial.psd",
                        Material = "assets/content/ui/uibackgroundblur.mat",
                    },
                    RectTransform =
                    {
                        AnchorMin = _config.AnchorMin,
                        AnchorMax = _config.AnchorMax,
                        OffsetMin = "341.64 405.595",
                        OffsetMax = "554.76 505.605",
                    },
                },
                "Overlay",
                UiOverlayName
            );
            container.Add(
                new CuiElement
                {
                    Name = "Text",
                    Parent = UiOverlayName,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "INDUSTRIAL RECYCLER",
                            Font = "robotocondensed-bold.ttf",
                            FontSize = 17,
                            Align = TextAnchor.MiddleCenter,
                            Color = "1 1 1 1",
                        },
                        new CuiOutlineComponent { Color = "0 0 0 1", Distance = "1 1" },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = "-86.493 0",
                            OffsetMax = "86.492 34.875",
                        },
                    },
                }
            );
            container.Add(
                new CuiButton
                {
                    Button =
                    {
                        Color = "0.3960785 0.4666667 0.2627451 1",
                        Command = $"industrialrecycler.openinput {recyclerId}",
                        Close = UiOverlayName,
                    },
                    Text =
                    {
                        Text = lang.GetMessage("InputButton", this, player.UserIDString),
                        Font = "robotocondensed-regular.ttf",
                        FontSize = 18,
                        Align = TextAnchor.MiddleCenter,
                        Color = "1 1 1 1",
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-104.206 -49.58",
                        OffsetMax = "-2.394 -17.22",
                    },
                },
                UiOverlayName,
                "Input"
            );
            container.Add(
                new CuiButton
                {
                    Button =
                    {
                        Color = "0.3960785 0.4666667 0.2627451 1",
                        Command = $"industrialrecycler.openoutput {recyclerId}",
                        Close = UiOverlayName,
                    },
                    Text =
                    {
                        Text = lang.GetMessage("OutputButton", this, player.UserIDString),
                        Font = "robotocondensed-regular.ttf",
                        FontSize = 18,
                        Align = TextAnchor.MiddleCenter,
                        Color = "1 1 1 1",
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "1.994 -49.58",
                        OffsetMax = "103.806 -17.22",
                    },
                },
                UiOverlayName,
                "Output"
            );
            if (permission.UserHasPermission(player.UserIDString, PermissionToggle))
            {
                container.Add(
                    new CuiButton
                    {
                        Button =
                        {
                            Color = "0.3960785 0.7666667 0.2627451 1",
                            Command = $"chat.say /togglerecycler",
                            Close = "ToggleConfig",
                        },
                        Text =
                        {
                            Text = lang.GetMessage("ToggleConfig", this),
                            Font = "robotocondensed-regular.ttf",
                            FontSize = 14,
                            Align = TextAnchor.MiddleCenter,
                            Color = "1 1 1 1",
                        },
                        RectTransform =
                        {
                            AnchorMin = "1 1",
                            AnchorMax = "1 1",
                            OffsetMin = "-110 -20",
                            OffsetMax = "0 0",
                        },
                    },
                    UiOverlayName,
                    "ToggleConfig"
                );
            }
            CuiHelper.DestroyUi(player, UiOverlayName);
            CuiHelper.AddUi(player, container);
        }

        private void OpenRecyclerInventory(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || arg.Args.Length == 0)
                return;
            if (!uint.TryParse(arg.Args[0], out uint recyclerId))
                return;
            var recycler = BaseNetworkable.serverEntities.Find(new NetworkableId(recyclerId)) as Recycler;
            if (recycler != null && HasAccess(player, recycler))
            {
                player.inventory.loot.StartLootingEntity(recycler, true);
                player.inventory.loot.AddContainer(recycler.inventory);
                player.inventory.loot.SendImmediate();
                player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), recycler.panelName);
            }
        }

        private void ConsoleCommandGiveRecycler(ConsoleSystem.Arg arg)
        {
            GiveRecyclerFromConsole(arg, isStandard: false);
        }

        private void ConsoleCommandGiveStandardRecycler(ConsoleSystem.Arg arg)
        {
            GiveRecyclerFromConsole(arg, isStandard: true);
        }

        private void GiveRecyclerFromConsole(ConsoleSystem.Arg arg, bool isStandard)
        {
            BasePlayer caller = arg?.Player();
            if (caller != null && !permission.UserHasPermission(caller.UserIDString, PermissionAdmin))
            {
                caller.ChatMessage(lang.GetMessage("NoPermission", this, caller.UserIDString));
                return;
            }
            if (arg?.Args == null || arg.Args.Length == 0 || !ulong.TryParse(arg.Args[0], out ulong userId))
                return;
            BasePlayer targetPlayer = BasePlayer.FindByID(userId);
            if (targetPlayer == null)
            {
                Puts("Player Not Found");
                return;
            }
            GiveRecyclerItem(targetPlayer, isStandard);
        }

        private void OpenInputStorage(ConsoleSystem.Arg arg)
        {
            OpenStorage(arg, true);
        }

        private void OpenOutputStorage(ConsoleSystem.Arg arg)
        {
            OpenStorage(arg, false);
        }

        private void OpenStorage(ConsoleSystem.Arg arg, bool isInput)
        {
            var player = arg.Player();
            if (player == null || arg.Args.Length == 0)
                return;
            if (!uint.TryParse(arg.Args[0], out uint recyclerId))
                return;
            var networkableId = new NetworkableId(recyclerId);
            var recycler = BaseNetworkable.serverEntities.Find(networkableId) as Recycler;
            if (recycler == null || !HasAccess(player, recycler))
                return;
            var comp = recycler.GetComponent<RecyclerComponent>();
            if (comp == null)
                return;
            var storage = isInput ? comp.InputStorage : comp.OutputStorage;
            player.inventory.loot.StartLootingEntity(storage, true);
            player.inventory.loot.AddContainer(storage.inventory);
            player.inventory.loot.SendImmediate();
            player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), storage.panelName);
            CreateStorageNavUi(player, recyclerId, isInput);
        }

        private void CreateStorageNavUi(BasePlayer player, uint recyclerId, bool viewingInput)
        {
            CuiHelper.DestroyUi(player, StorageUiOverlayName);
            const float panelWidthPx = 385f;
            const float panelHeightPx = 60f;
            const float offsetTopPx = 53f;
            const float offsetXPx = 382f;
            const float buttonWidthPx = 120f;
            const float buttonHeightPx = 25f;
            const float buttonGapPx = 10f;
            float btnW = buttonWidthPx / panelWidthPx;
            float btnH = buttonHeightPx / panelHeightPx;
            float gapPct = buttonGapPx / panelWidthPx;
            float labelW = 80f / panelWidthPx;
            var c = new CuiElementContainer();
            c.Add(
                new CuiPanel
                {
                    Image =
                    {
                        Color = "0.5 0.5 0.5 0.99",
                        Sprite = "assets/content/ui/ui.background.transparent.radial.psd",
                        Material = "assets/content/ui/uibackgroundblur.mat",
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.5 1",
                        AnchorMax = "0.5 1",
                        OffsetMin = $"{-panelWidthPx * 0.5f + offsetXPx} -{offsetTopPx + panelHeightPx}",
                        OffsetMax = $"{panelWidthPx * 0.5f + offsetXPx} -{offsetTopPx}",
                    },
                },
                "Overlay",
                StorageUiOverlayName
            );
            string headerText = viewingInput ? lang.GetMessage("InputButton", this, player.UserIDString) : lang.GetMessage("OutputButton", this, player.UserIDString);
            string otherText = viewingInput ? lang.GetMessage("GoToOutput", this, player.UserIDString) : lang.GetMessage("GoToInput", this, player.UserIDString);
            string recyclerText = lang.GetMessage("GoToRecycler", this, player.UserIDString);
            string otherCmd = viewingInput ? $"industrialrecycler.openoutput {recyclerId}" : $"industrialrecycler.openinput  {recyclerId}";
            c.Add(
                new CuiLabel
                {
                    Text =
                    {
                        Text = headerText,
                        FontSize = 14,
                        Align = TextAnchor.MiddleLeft,
                        Color = "1 1 1 1",
                    },
                    RectTransform = { AnchorMin = "0.02 0.15", AnchorMax = $"{0.02f + labelW:F3} 0.85" },
                },
                StorageUiOverlayName
            );
            float firstMinX = 0.04f + labelW;
            float firstMaxX = firstMinX + btnW;
            c.Add(
                new CuiButton
                {
                    Button =
                    {
                        Color = "0.4 0.6 0.4 0.95",
                        Command = otherCmd,
                        Material = "assets/content/ui/namefontmaterial.mat",
                    },
                    RectTransform = { AnchorMin = $"{firstMinX:F3} {(1 - btnH) / 2:F3}", AnchorMax = $"{firstMaxX:F3} {(1 + btnH) / 2:F3}" },
                    Text =
                    {
                        Text = otherText,
                        FontSize = 12,
                        Align = TextAnchor.MiddleCenter,
                    },
                },
                StorageUiOverlayName
            );
            float secondMinX = firstMaxX + gapPct;
            float secondMaxX = secondMinX + btnW;
            if (secondMaxX > 0.98f)
            {
                secondMaxX = 0.98f;
                secondMinX = secondMaxX - btnW;
            }
            c.Add(
                new CuiButton
                {
                    Button =
                    {
                        Color = "0.4 0.6 0.4 0.95",
                        Command = $"industrialrecycler.openrecycler {recyclerId}",
                        Material = "assets/content/ui/namefontmaterial.mat",
                    },
                    RectTransform = { AnchorMin = $"{secondMinX:F3} {(1 - btnH) / 2:F3}", AnchorMax = $"{secondMaxX:F3} {(1 + btnH) / 2:F3}" },
                    Text =
                    {
                        Text = recyclerText,
                        FontSize = 12,
                        Align = TextAnchor.MiddleCenter,
                    },
                },
                StorageUiOverlayName
            );
            CuiHelper.AddUi(player, c);
        }

        private void CommandOpenStandardPurchaseUI(BasePlayer player, string command, string[] args)
        {
            OpenPurchaseUi(player, isStandard: true);
        }

        private void CommandOpenIndustrialPurchaseUI(BasePlayer player, string command, string[] args)
        {
            OpenPurchaseUi(player, isStandard: false);
        }

        private void OpenPurchaseUi(BasePlayer player, bool isStandard)
        {
            if (player == null)
                return;
            string requiredPermission = isStandard ? PermissionBuyStandard : PermissionBuyIndustrial;
            if (!permission.UserHasPermission(player.UserIDString, requiredPermission))
            {
                player.ChatMessage(lang.GetMessage("NoPermission", this, player.UserIDString));
                return;
            }
            if (CountPlayerOwnedRecyclers(player, !isStandard) >= GetMaxRecyclers(player, !isStandard))
            {
                player.ChatMessage(lang.GetMessage("RecyclerLimitReached", this, player.UserIDString));
                return;
            }
            CreatePurchaseUi(player, isStandard ? "standard" : "industrial");
        }

        private void CreatePurchaseUi(BasePlayer player, string type)
        {
            if (player == null)
                return;
            CuiHelper.DestroyUi(player, PurchaseUiOverlayName);
            var container = new CuiElementContainer();
            var parent = PurchaseUiOverlayName;
            AddPanel(container, "0.3 0.3", "0.7 0.7", parent, "Overlay", "0 0 0 0.8", true);
            if (type == "standard")
            {
                AddText(container, parent, "0.1 0.9", "0.9 1", lang.GetMessage("PurchaseRecyclerTitle", this, player.UserIDString), 20, TextAnchor.MiddleCenter, "1 1 1 1");
                AddText(container, parent, "0.05 0.75", "0.9 1", lang.GetMessage("PurchaseCosts", this, player.UserIDString), 16, TextAnchor.MiddleLeft, "1 1 1 1");
                float initialY = 0.85f;
                AddDictionaryWithSpacing(container, parent, _config.PurchaseStandardCost, 0.10f, initialY, 0.15f, "1 1 1 1");
                AddButton(container, parent, lang.GetMessage("ConfirmPurchase", this, player.UserIDString), "buystandard.confirm", "0.25", "0.1", "0.45", "0.18", "0.4 0.6 0.4 0.95", "1 1 1 1");
            }
            else if (type == "industrial")
            {
                AddText(container, parent, "0.1 0.9", "0.9 1", lang.GetMessage("PurchaseIndustrialRecyclerTitle", this, player.UserIDString), 20, TextAnchor.MiddleCenter, "1 1 1 1");
                AddText(container, parent, "0.05 0.75", "0.9 1", lang.GetMessage("PurchaseCosts", this, player.UserIDString), 16, TextAnchor.MiddleLeft, "1 1 1 1");
                float initialY = 0.85f;
                AddDictionaryWithSpacing(container, parent, _config.PurchaseIndustrialCost, 0.10f, initialY, 0.15f, "1 1 1 1");
                AddButton(container, parent, lang.GetMessage("ConfirmPurchase", this, player.UserIDString), "buyindustrial.confirm", "0.25", "0.1", "0.45", "0.18", "0.4 0.6 0.4 0.95", "1 1 1 1");
            }
            AddButton(container, parent, lang.GetMessage("Close", this, player.UserIDString), "buyrecycler.close", "0.55", "0.1", "0.75", "0.18", "1 0 0 1", "1 1 1 1");
            CuiHelper.AddUi(player, container);
        }

        private void AddDictionaryWithSpacing(CuiElementContainer container, string parent, List<ResourceItem> items, float initialAnchorX, float initialAnchorY, float spacing, string fontColor)
        {
            if (items == null)
                return;
            int i = 0;
            foreach (ResourceItem resourceItem in items)
            {
                if (resourceItem == null || string.IsNullOrWhiteSpace(resourceItem.Shortname) || resourceItem.Amount <= 0)
                    continue;
                i++;
                float anchorX = initialAnchorX;
                float anchorY = initialAnchorY - (spacing * i);
                if (i >= 5 && i < 9)
                {
                    anchorY = initialAnchorY - (spacing * (i - 4));
                    anchorX = initialAnchorX + 0.23f;
                }
                else if (i >= 9)
                {
                    anchorY = initialAnchorY - (spacing * (i - 8));
                    anchorX = initialAnchorX + 0.47f;
                }
                float imageSize = 0.10f;
                float imageAnchorMinX = anchorX - (imageSize / 2);
                float imageAnchorMaxX = anchorX + (imageSize / 2);
                float imageAnchorMinY = anchorY - 0.01f - (imageSize / 2);
                float imageAnchorMaxY = anchorY + 0.05f + (imageSize / 2);
                AddImage(container, parent, resourceItem.Shortname, $"{imageAnchorMinX} {imageAnchorMinY}", $"{imageAnchorMaxX} {imageAnchorMaxY}", $"ResourceImage{i}");
                AddText(container, parent, $"{anchorX + 0.08f} {anchorY}", $"{anchorX + 0.85f} {anchorY + 0.06f}", resourceItem.Amount.ToString(), 12, TextAnchor.MiddleLeft, fontColor);
            }
        }

        private static void AddPanel(CuiElementContainer container, string anchorMin, string anchorMax, string panelName, string parent, string color, bool isCursorEnabled = false)
        {
            var panel = new CuiPanel
            {
                Image = { Color = color },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                CursorEnabled = isCursorEnabled,
            };
            container.Add(panel, parent, panelName);
        }

        private static void AddText(CuiElementContainer container, string parent, string anchorMin, string anchorMax, string text, int fontSize, TextAnchor alignment, string textColor)
        {
            container.Add(
                new CuiLabel
                {
                    Text =
                    {
                        Text = text,
                        FontSize = fontSize,
                        Align = alignment,
                        Color = textColor,
                    },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                },
                parent
            );
        }

        private void AddImage(CuiElementContainer container, string parent, string resourceName, string anchorMin, string anchorMax, string imageName)
        {
            var itemDefinition = ItemManager.FindItemDefinition(resourceName);
            if (itemDefinition == null)
            {
                PrintWarning($"Item definition not found for {resourceName}");
                return;
            }
            container.Add(
                new CuiElement
                {
                    Name = imageName,
                    Parent = parent,
                    Components =
                    {
                        new CuiImageComponent { ItemId = itemDefinition.itemid, SkinId = 0UL },
                        new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax },
                    },
                }
            );
        }

        private void AddButton(
            CuiElementContainer container,
            string parent,
            string text,
            string command,
            string anchorMinX,
            string anchorMinY,
            string anchorMaxX,
            string anchorMaxY,
            string bgColor,
            string textColor
        )
        {
            container.Add(
                new CuiButton
                {
                    Button = { Color = bgColor, Command = command },
                    RectTransform = { AnchorMin = $"{anchorMinX} {anchorMinY}", AnchorMax = $"{anchorMaxX} {anchorMaxY}" },
                    Text =
                    {
                        Text = text,
                        FontSize = 14,
                        Align = TextAnchor.MiddleCenter,
                        Color = textColor,
                    },
                },
                parent
            );
        }

        private void ConfirmPurchaseStandard(ConsoleSystem.Arg arg)
        {
            ConfirmPurchase(arg, isStandard: true);
        }

        private void ConfirmPurchaseIndustrial(ConsoleSystem.Arg arg)
        {
            ConfirmPurchase(arg, isStandard: false);
        }

        private void ConfirmPurchase(ConsoleSystem.Arg arg, bool isStandard)
        {
            var player = arg?.Player();
            if (player == null)
                return;
            string requiredPermission = isStandard ? PermissionBuyStandard : PermissionBuyIndustrial;
            if (!permission.UserHasPermission(player.UserIDString, requiredPermission))
            {
                player.ChatMessage(lang.GetMessage("NoPermission", this, player.UserIDString));
                return;
            }
            if (!_purchaseInProgress.Add(player.userID))
                return;
            try
            {
                bool isIndustrial = !isStandard;
                if (CountPlayerOwnedRecyclers(player, isIndustrial) >= GetMaxRecyclers(player, isIndustrial))
                {
                    player.ChatMessage(lang.GetMessage("RecyclerLimitReached", this, player.UserIDString));
                    return;
                }
                Item recyclerItem = CreateRecyclerItem(isStandard);
                if (recyclerItem == null)
                {
                    PrintError($"Unable to create recycler item using base item '{_config.BaseItem}'.");
                    player.ChatMessage(lang.GetMessage("PurchaseDeliveryFailed", this, player.UserIDString));
                    return;
                }
                List<ResourceItem> costs = isStandard ? _config.PurchaseStandardCost : _config.PurchaseIndustrialCost;
                if (!CheckAndRemoveBuyItems(player, costs))
                {
                    if (recyclerItem.IsValid())
                        recyclerItem.Remove();
                    player.ChatMessage(lang.GetMessage("PurchaseFailed", this, player.UserIDString));
                    return;
                }
                if (!DeliverRecyclerItem(player, recyclerItem, isStandard))
                {
                    if (recyclerItem.IsValid())
                        recyclerItem.Remove();
                    RefundBuyItems(player, costs);
                    player.ChatMessage(lang.GetMessage("PurchaseDeliveryFailed", this, player.UserIDString));
                    return;
                }
                CuiHelper.DestroyUi(player, PurchaseUiOverlayName);
                player.ChatMessage(lang.GetMessage(isStandard ? "PurchaseSuccessStandard" : "PurchaseSuccessIndustrial", this, player.UserIDString));
            }
            finally
            {
                _purchaseInProgress.Remove(player.userID);
            }
        }

        private void ClosePurchaseUI(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player != null)
                CuiHelper.DestroyUi(player, PurchaseUiOverlayName);
        }

        private void CommandGetRecycler(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;
            if (!permission.UserHasPermission(player.UserIDString, PermissionUseRecycler))
            {
                player.ChatMessage(lang.GetMessage("NoPermission", this, player.UserIDString));
                return;
            }
            BasePlayer targetPlayer = player;
            if (args != null && args.Length > 0)
            {
                targetPlayer = BasePlayer.Find(args[0]);
                if (targetPlayer == null)
                {
                    player.ChatMessage(lang.GetMessage("PlayerNotFound", this, player.UserIDString));
                    return;
                }
            }
            if (CountPlayerOwnedRecyclers(targetPlayer, isIndustrial: true) >= GetMaxRecyclers(targetPlayer, isIndustrial: true))
            {
                player.ChatMessage(lang.GetMessage("RecyclerLimitReached", this, player.UserIDString));
                if (targetPlayer != player)
                    targetPlayer.ChatMessage(lang.GetMessage("RecyclerLimitReached", this, targetPlayer.UserIDString));
                return;
            }
            if (GiveRecyclerItem(targetPlayer, false))
            {
                player.ChatMessage(lang.GetMessage("RecyclerGiven", this, player.UserIDString));
                if (targetPlayer != player)
                {
                    targetPlayer.ChatMessage(lang.GetMessage("RecyclerReceived", this, targetPlayer.UserIDString));
                }
            }
            else
            {
                player.ChatMessage(lang.GetMessage("RecyclerNotGiven", this, player.UserIDString));
                if (targetPlayer != player)
                {
                    targetPlayer.ChatMessage(lang.GetMessage("RecyclerNotReceived", this, targetPlayer.UserIDString));
                }
            }
        }

        private Item CreateRecyclerItem(bool isStandard)
        {
            if (string.IsNullOrWhiteSpace(_config.BaseItem))
                return null;
            ulong skinId = isStandard ? _config.StandardRecyclerSkinId : _config.IndustrialRecyclerSkinId;
            Item recyclerItem = ItemManager.CreateByName(_config.BaseItem, 1, skinId);
            if (recyclerItem != null)
                recyclerItem.name = isStandard ? "Standard Recycler" : "Industrial Recycler";
            return recyclerItem;
        }

        private bool DeliverRecyclerItem(BasePlayer player, Item recyclerItem, bool isStandard)
        {
            if (player == null || player.inventory == null || recyclerItem == null || !recyclerItem.IsValid())
                return false;
            if (!player.inventory.GiveItem(recyclerItem))
            {
                recyclerItem.Drop(player.transform.position + Vector3.up, Vector3.zero);
                player.ChatMessage(lang.GetMessage("InventoryFull", this, player.UserIDString));
            }
            player.ChatMessage(lang.GetMessage(isStandard ? "StandardRecyclerReceived" : "IndustrialRecyclerReceived", this, player.UserIDString));
            return true;
        }

        private bool GiveRecyclerItem(BasePlayer player, bool isStandard)
        {
            Item recyclerItem = CreateRecyclerItem(isStandard);
            return recyclerItem != null && DeliverRecyclerItem(player, recyclerItem, isStandard);
        }

        private void CommandGetStandardRecycler(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;
            if (!permission.UserHasPermission(player.UserIDString, PermissionUseRecycler))
            {
                player.ChatMessage(lang.GetMessage("NoPermission", this, player.UserIDString));
                return;
            }
            BasePlayer targetPlayer = player;
            if (args != null && args.Length > 0)
            {
                targetPlayer = BasePlayer.Find(args[0]);
                if (targetPlayer == null)
                {
                    player.ChatMessage(lang.GetMessage("PlayerNotFound", this, player.UserIDString));
                    return;
                }
            }
            if (CountPlayerOwnedRecyclers(targetPlayer, isIndustrial: false) >= GetMaxRecyclers(targetPlayer, isIndustrial: false))
            {
                player.ChatMessage(lang.GetMessage("RecyclerLimitReached", this, player.UserIDString));
                if (targetPlayer != player)
                    targetPlayer.ChatMessage(lang.GetMessage("RecyclerLimitReached", this, targetPlayer.UserIDString));
                return;
            }
            bool wasGiven = GiveRecyclerItem(targetPlayer, true);
            if (wasGiven)
            {
                player.ChatMessage(lang.GetMessage("RecyclerGiven", this, player.UserIDString));
            }
            else
            {
                player.ChatMessage(lang.GetMessage("RecyclerNotGiven", this, player.UserIDString));
            }
            if (targetPlayer != player)
            {
                if (wasGiven)
                {
                    targetPlayer.ChatMessage(lang.GetMessage("RecyclerReceived", this, targetPlayer.UserIDString));
                }
                else
                {
                    targetPlayer.ChatMessage(lang.GetMessage("RecyclerNotReceived", this, targetPlayer.UserIDString));
                }
            }
        }

        private void CommandUpgradeRecycler(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;
            if (!permission.UserHasPermission(player.UserIDString, PermissionUseRecycler))
            {
                player.ChatMessage(lang.GetMessage("NoPermission", this, player.UserIDString));
                return;
            }
            if (CountPlayerRecyclers(player, isIndustrial: true) >= GetMaxRecyclers(player, isIndustrial: true))
            {
                player.ChatMessage(lang.GetMessage("RecyclerLimitReached", this, player.UserIDString));
                return;
            }
            if (!UpgradeToIndustrialRecycler(player))
            {
                player.ChatMessage(lang.GetMessage("NoRecyclerInView", this, player.UserIDString));
            }
            else
            {
                player.ChatMessage(lang.GetMessage("RecyclerUpgraded", this, player.UserIDString));
            }
        }

        private int GetMaxRecyclers(BasePlayer player, bool isIndustrial)
        {
            if (player == null)
                return 0;
            int maxRecyclers = isIndustrial ? _config.IndustrialMaxRecyclers : _config.StandardMaxRecyclers;
            if (permission.UserHasPermission(player.UserIDString, PermissionAdmin))
                return int.MaxValue;
            if (permission.UserHasPermission(player.UserIDString, PermissionVip2))
                maxRecyclers = isIndustrial ? _config.Vip2IndustrialMaxRecyclers : _config.Vip2StandardMaxRecyclers;
            else if (permission.UserHasPermission(player.UserIDString, PermissionVip1))
                maxRecyclers = isIndustrial ? _config.Vip1IndustrialMaxRecyclers : _config.Vip1StandardMaxRecyclers;
            return Math.Max(0, maxRecyclers);
        }

        private int CountPlayerRecyclers(BasePlayer player, bool isIndustrial)
        {
            if (player == null)
                return 0;
            int count = 0;
            var trackedIds = (isIndustrial ? _industrialRecyclers : _standardRecyclers).ToList();
            foreach (ulong entityId in trackedIds)
            {
                var recycler = BaseNetworkable.serverEntities.Find(new NetworkableId(entityId)) as Recycler;
                if (recycler == null || recycler.IsDestroyed)
                {
                    _recyclerOwners.Remove(entityId);
                    _industrialRecyclers.Remove(entityId);
                    _standardRecyclers.Remove(entityId);
                    continue;
                }
                if (recycler.OwnerID == player.userID)
                    count++;
            }
            return count;
        }

        private int CountPlayerRecyclerItems(BasePlayer player, bool isIndustrial)
        {
            if (player?.inventory == null)
                return 0;
            ulong expectedSkin = isIndustrial ? _config.IndustrialRecyclerSkinId : _config.StandardRecyclerSkinId;
            int count = 0;
            var allItems = Pool.Get<List<Item>>();
            try
            {
                player.inventory.GetAllItems(allItems);
                foreach (Item item in allItems)
                {
                    if (item == null || !item.IsValid() || item.info == null || item.amount <= 0)
                        continue;
                    if (!string.Equals(item.info.shortname, _config.BaseItem, StringComparison.OrdinalIgnoreCase) || item.skin != expectedSkin)
                        continue;
                    count += item.amount;
                }
            }
            finally
            {
                Pool.FreeUnmanaged(ref allItems);
            }
            return count;
        }

        private int CountPlayerOwnedRecyclers(BasePlayer player, bool isIndustrial)
        {
            int placed = CountPlayerRecyclers(player, isIndustrial);
            if (placed == int.MaxValue)
                return placed;
            return placed + CountPlayerRecyclerItems(player, isIndustrial);
        }

        private Recycler FindRecyclerInView(BasePlayer player)
        {
            if (Physics.Raycast(player.eyes.HeadRay(), out RaycastHit hit, 5f))
            {
                BaseEntity entity = hit.GetEntity();
                return entity as Recycler;
            }
            return null;
        }

        private Recycler SetupStandardRecycler(BasePlayer player, BaseEntity baseEntity, BaseEntity foundation, int turn = 0)
        {
            var recycler = SpawnEntity(RecyclerPrefabPath, foundation, player, baseEntity.transform.position, baseEntity.transform.rotation * Quaternion.Euler(0, turn, 0)) as Recycler;
            if (recycler == null)
                return null;
            recycler.OwnerID = player.userID;
            ApplyEfficiency(recycler, GetConfiguredEfficiencyFor(player.userID, false));
            RegisterRecyclerOwnership(recycler, false);
            return recycler;
        }

        private Tugboat IsPlacingOnTugboat(BaseEntity baseEntity)
        {
            Vector3 origin = baseEntity.transform.position + new Vector3(0f, 0.1f, 0f);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hitData, 1f, Layers.Mask.Vehicle_Detailed | Layers.Mask.Vehicle_Large))
            {
                var hitEntity = hitData.GetEntity();
                if (hitEntity is Tugboat)
                {
                    return hitEntity as Tugboat;
                }
            }
            return null;
        }

        private bool InitializeRecyclerPlacement(BasePlayer player, BaseEntity baseEntity, int turn = 0)
        {
            if (IsPlacingOnTugboat(baseEntity) is Tugboat tugboat)
            {
                BaseEntity recycler = null;
                if (baseEntity.skinID == _config.StandardRecyclerSkinId)
                {
                    recycler = SetupStandardRecycler(player, baseEntity, tugboat, turn);
                }
                else if (baseEntity.skinID == _config.IndustrialRecyclerSkinId)
                {
                    recycler = SpawnRecyclerWithComponents(player, baseEntity, tugboat, turn);
                }
                if (recycler != null)
                {
                    baseEntity.Kill();
                    return true;
                }
                return false;
            }
            var supportingFoundation = FindSupportingFoundation(baseEntity);
            if (supportingFoundation == null)
            {
                return false;
            }
            Recycler spawnedRecycler;
            if (baseEntity.skinID == _config.StandardRecyclerSkinId)
            {
                spawnedRecycler = SetupStandardRecycler(player, baseEntity, supportingFoundation, turn);
            }
            else if (baseEntity.skinID == _config.IndustrialRecyclerSkinId)
            {
                spawnedRecycler = SpawnRecyclerWithComponents(player, baseEntity, supportingFoundation, turn);
            }
            else
            {
                return false;
            }
            if (spawnedRecycler != null && baseEntity.skinID == _config.IndustrialRecyclerSkinId)
            {
                spawnedRecycler.gameObject.AddComponent<RecyclerComponent>();
                RegisterRecyclerOwnership(spawnedRecycler, true);
            }
            return spawnedRecycler != null;
        }

        private bool InitializeUpgradeRecycler(BasePlayer player, Recycler recycler, int turn = 0)
        {
            var spawnedRecycler = UpgradeRecyclerWithComponents(player, recycler, turn);
            if (spawnedRecycler == null)
                return false;
            spawnedRecycler.gameObject.AddComponent<RecyclerComponent>();
            RegisterRecyclerOwnership(spawnedRecycler, true);
            return true;
        }

        private BuildingBlock FindSupportingFoundation(BaseEntity baseEntity)
        {
            Vector3 origin = baseEntity.transform.position + Vector3.up * 0.1f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hitData, 4f, LayerMask.GetMask("Construction")))
            {
                return hitData.GetEntity() as BuildingBlock;
            }
            return null;
        }

        private Recycler SetupStorageAndAdapters(Recycler recycler, BasePlayer player)
        {
            bool useAlt = _config.AdapterLayout == 2;
            bool hide = _config.StorageVisibility == 1;
            Vector3 inputOffset,
                inputAdapterOffset,
                outputOffset,
                outputAdapterOffset;
            Quaternion inputRotation,
                outputRotation,
                inputAdapterRotation,
                outputAdapterRotation;
            if (useAlt)
            {
                if (hide)
                {
                    inputOffset = new Vector3(0.0f, 0.72f, 0.11f);
                    inputAdapterOffset = new Vector3(0.32f, -0.6f, -0.03f);
                    outputOffset = new Vector3(-0.4f, 0.8f, 0.2f);
                    outputAdapterOffset = new Vector3(-0.07f, 0.535f, 0.397f);
                    inputRotation = Quaternion.Euler(0, 180, 191);
                    outputRotation = Quaternion.Euler(169, 90, 180);
                    inputAdapterRotation = Quaternion.Euler(0, 0, 0);
                    outputAdapterRotation = Quaternion.Euler(180, 90, 0);
                }
                else
                {
                    inputAdapterOffset = new Vector3(0.00f - 0.19f, -1.43f, -0.25f - 0.27f);
                    inputAdapterRotation = Quaternion.Euler(180, 90, 270 + 189);
                    outputAdapterOffset = new Vector3(0.19f, 0.52f, -0.01f);
                    outputAdapterRotation = Quaternion.Euler(180, 90, -9);
                    inputOffset = new Vector3(-1.0f, 0.72f, 0.31f);
                    outputOffset = new Vector3(-0.9f, 0.72f, -0.09f);
                    inputRotation = Quaternion.Euler(90, 90, 180);
                    outputRotation = Quaternion.Euler(180, 90, 180);
                }
            }
            else
            {
                if (hide)
                {
                    inputOffset = new Vector3(0.0f, 0.72f, 0.11f);
                    inputAdapterOffset = new Vector3(0.836f, -0.3301f, 0.18f);
                    outputOffset = new Vector3(-0.4f, 0.72f, 0.2f);
                    outputAdapterOffset = new Vector3(-0.227f, 0.33f, 0.434f);
                    inputRotation = Quaternion.Euler(0, 0, 180);
                    outputRotation = Quaternion.Euler(180, 90, 180);
                    inputAdapterRotation = Quaternion.Euler(0, 0, 0);
                    outputAdapterRotation = Quaternion.Euler(180, 90, 0);
                }
                else
                {
                    inputOffset = new Vector3(-1.0f, 0.72f, 0.31f);
                    inputAdapterOffset = new Vector3(0.00f, -0.15f, -0.25f);
                    outputOffset = new Vector3(-0.9f, 0.72f, -0.09f);
                    outputAdapterOffset = new Vector3(0.00f, 0.26f, -0.05f);
                    inputRotation = Quaternion.Euler(90, 90, 180);
                    outputRotation = Quaternion.Euler(180, 90, 180);
                    inputAdapterRotation = Quaternion.Euler(180, 90, 270);
                    outputAdapterRotation = Quaternion.Euler(0, 90, 0);
                }
            }
            var inputStorage = SpawnEntity(InputStoragePrefabPath, recycler, player, inputOffset, inputRotation);
            SpawnEntity(AdapterPrefabPath, inputStorage, player, inputAdapterOffset, inputAdapterRotation);
            var outputStorage = SpawnEntity(OutputStoragePrefabPath, recycler, player, outputOffset, outputRotation);
            SpawnEntity(AdapterPrefabPath, outputStorage, player, outputAdapterOffset, outputAdapterRotation);
            return recycler;
        }

        private Recycler UpgradeRecyclerWithComponents(BasePlayer player, Recycler recycler, int turn = 0)
        {
            ApplyEfficiency(recycler, GetConfiguredEfficiencyFor(player.userID, false));
            return SetupStorageAndAdapters(recycler, player);
        }

        private Recycler SpawnRecyclerWithComponents(BasePlayer player, BaseEntity baseEntity, BaseEntity foundation, int turn = 0)
        {
            if (foundation is Tugboat)
                return null;
            var recycler = SpawnEntity(RecyclerPrefabPath, foundation, player, baseEntity.transform.position, baseEntity.transform.rotation * Quaternion.Euler(0, turn, 0)) as Recycler;
            if (recycler == null)
                return null;
            ApplyEfficiency(recycler, GetConfiguredEfficiencyFor(player.userID, false));
            recycler = SetupStorageAndAdapters(recycler, player);
            return recycler;
        }

        private BaseEntity SpawnEntity(string prefabPath, BaseEntity parent, BasePlayer ownerPlayer, Vector3 position, Quaternion rotation)
        {
            var ent = GameManager.server.CreateEntity(prefabPath, Vector3.zero, Quaternion.identity, true);
            if (ent == null)
                return null;
            ent.enableSaving = true;
            ent.OwnerID = ownerPlayer != null ? ownerPlayer.userID : (parent != null ? parent.OwnerID : 0UL);
            if (parent != null)
                ent.SetParent(parent, true, false);
            if (parent is BuildingBlock || parent is Tugboat)
            {
                ent.transform.position = position;
                ent.transform.rotation = rotation;
            }
            else
            {
                ent.transform.localPosition = position;
                ent.transform.localRotation = rotation;
            }
            ent.Spawn();
            ent.SendNetworkUpdateImmediate();
            return ent;
        }

        private void ProcessRecyclerItems(RecyclerComponent recyclerComponent, Item item)
        {
            Recycler recycler = recyclerComponent?.Recycler;
            ItemContainer outputInventory = recyclerComponent?.OutputStorage?.inventory;
            if (recycler == null || recycler.IsDestroyed || recycler.inventory == null || outputInventory == null)
                return;
            if (item == null || !item.IsValid() || item.info == null || item.amount <= 0)
                return;
            Interface.Oxide.CallHook("CanRecycle", recycler, item);
            if (item.info.category == ItemCategory.Weapon)
            {
                BaseProjectile projectile = item.GetHeldEntity() as BaseProjectile;
                ItemDefinition ammoType = projectile?.primaryMagazine?.ammoType;
                if (projectile?.primaryMagazine != null && ammoType != null)
                {
                    while (projectile.primaryMagazine.contents > 0)
                    {
                        int ammoToUnload = Mathf.Min(projectile.primaryMagazine.contents, Math.Max(1, ammoType.stackable));
                        if (ammoToUnload <= 0)
                            break;
                        Item ammoItem = ItemManager.Create(ammoType, ammoToUnload, 0uL);
                        if (ammoItem == null)
                            break;
                        projectile.primaryMagazine.contents -= ammoToUnload;
                        MoveItemToOutputOrDrop(ammoItem, recyclerComponent);
                    }
                }
                if (item.contents?.itemList != null && item.contents.itemList.Count > 0)
                {
                    foreach (Item attachment in item.contents.itemList.ToList())
                    {
                        if (attachment == null || !attachment.IsValid() || attachment.info == null)
                            continue;
                        attachment.RemoveFromContainer();
                        MoveItemToOutputOrDrop(attachment, recyclerComponent);
                    }
                }
            }
            if (!item.IsValid() || item.parent != recycler.inventory)
                return;
            if (item.position < 6)
            {
                BasePlayer ownerPlayer = BasePlayer.FindByID(recycler.OwnerID);
                if (ownerPlayer != null)
                {
                    recycler.SetFlag(BaseEntity.Flags.On, false);
                    Interface.CallHook("OnRecyclerToggle", recycler, ownerPlayer);
                }
                recycler.StartRecycling();
            }
            else if (!MoveItemToOutputOrDrop(item, recyclerComponent, dropOnFailure: false))
            {
                recycler.StopRecycling();
            }
        }

        private bool MoveItemToOutputOrDrop(Item item, RecyclerComponent recyclerComponent, bool dropOnFailure = true)
        {
            Recycler recycler = recyclerComponent?.Recycler;
            ItemContainer outputInventory = recyclerComponent?.OutputStorage?.inventory;
            if (item == null || !item.IsValid() || recycler == null || outputInventory == null)
                return false;
            if (SafeMoveToContainer(item, outputInventory))
                return true;
            if (!dropOnFailure)
                return false;
            item.RemoveFromContainer();
            item.Drop(recycler.transform.position + Vector3.up, recycler.transform.forward * 2f);
            return true;
        }

        private bool UpgradeToIndustrialRecycler(BasePlayer player)
        {
            if (Physics.Raycast(player.eyes.HeadRay(), out RaycastHit hit, 5f))
            {
                BaseEntity baseEntity = hit.GetEntity();
                if (baseEntity is Recycler recycler && recycler.OwnerID == player.userID && !IsRecyclerIndustrial(recycler))
                {
                    if (!InitializeUpgradeRecycler(player, recycler, 90))
                    {
                        player.ChatMessage(lang.GetMessage("InvalidPlacement", this, player.UserIDString));
                        timer.Once(1, () => GiveRecyclerItem(player, true));
                        return false;
                    }
                    else
                    {
                        player.ChatMessage(lang.GetMessage("RecyclerPlaced", this, player.UserIDString));
                    }
                    return true;
                }
            }
            return false;
        }

        private void ToggleRecyclerConfig(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionToggle))
            {
                player.ChatMessage(lang.GetMessage("NoPermission", this, player.UserIDString));
                return;
            }
            Recycler recycler = FindRecyclerInView(player);
            if (recycler == null)
            {
                player.ChatMessage(lang.GetMessage("NoRecyclerInView", this, player.UserIDString));
                return;
            }
            var recyclerComponent = recycler.GetComponent<RecyclerComponent>();
            if (recyclerComponent == null)
            {
                player.ChatMessage(lang.GetMessage("RecyclerComponentNotFound", this, player.UserIDString));
                return;
            }
            if (recyclerComponent.ToggleConfig())
                player.ChatMessage(lang.GetMessage("RecyclerConfigToggled", this, player.UserIDString));
            else
                player.ChatMessage(lang.GetMessage("RecyclerNotConfigToggled", this, player.UserIDString));
        }

        private bool CheckAndRemoveBuyItems(BasePlayer player, List<ResourceItem> requiredItems)
        {
            if (player?.inventory == null)
                return false;
            if (!TryBuildRequiredItemMap(requiredItems, out Dictionary<string, int> requiredItemMap))
                return false;
            if (requiredItemMap.Count == 0)
                return true;
            var allItems = Pool.Get<List<Item>>();
            try
            {
                player.inventory.GetAllItems(allItems);
                return RemoveItems(allItems, requiredItemMap);
            }
            finally
            {
                Pool.FreeUnmanaged(ref allItems);
            }
        }

        private bool TryBuildRequiredItemMap(List<ResourceItem> requiredItems, out Dictionary<string, int> requiredItemMap)
        {
            requiredItemMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (requiredItems == null)
                return true;
            foreach (ResourceItem configuredItem in requiredItems)
            {
                string shortname = configuredItem?.Shortname?.Trim();
                int amount = configuredItem?.Amount ?? 0;
                if (string.IsNullOrEmpty(shortname) || amount < 0)
                {
                    PrintError("Purchase cost contains an invalid shortname or a negative amount.");
                    return false;
                }
                if (amount == 0)
                    continue;
                if (ItemManager.FindItemDefinition(shortname) == null)
                {
                    PrintError($"Purchase cost item definition not found: '{shortname}'.");
                    return false;
                }
                if (requiredItemMap.TryGetValue(shortname, out int currentAmount))
                {
                    long combinedAmount = (long)currentAmount + amount;
                    if (combinedAmount > int.MaxValue)
                    {
                        PrintError($"Purchase cost for '{shortname}' is too large.");
                        return false;
                    }
                    requiredItemMap[shortname] = (int)combinedAmount;
                }
                else
                {
                    requiredItemMap[shortname] = amount;
                }
            }
            return true;
        }

        private bool RemoveItems(List<Item> allItems, Dictionary<string, int> requiredItemMap)
        {
            if (allItems == null || requiredItemMap == null)
                return false;
            var removalPlan = new List<KeyValuePair<Item, int>>();
            foreach (var requirement in requiredItemMap)
            {
                int amountToRemove = requirement.Value;
                foreach (Item item in allItems.ToList())
                {
                    if (amountToRemove <= 0)
                        break;
                    if (item == null || !item.IsValid() || item.info == null || item.amount <= 0)
                        continue;
                    if (!string.Equals(item.info.shortname, requirement.Key, StringComparison.OrdinalIgnoreCase))
                        continue;
                    int amountFromStack = Math.Min(item.amount, amountToRemove);
                    removalPlan.Add(new KeyValuePair<Item, int>(item, amountFromStack));
                    amountToRemove -= amountFromStack;
                }
                if (amountToRemove > 0)
                    return false;
            }
            foreach (var plannedRemoval in removalPlan)
            {
                Item item = plannedRemoval.Key;
                int amount = plannedRemoval.Value;
                if (item == null || !item.IsValid() || item.info == null || item.amount < amount || amount <= 0)
                    return false;
            }
            foreach (var plannedRemoval in removalPlan)
            {
                Item item = plannedRemoval.Key;
                int amount = plannedRemoval.Value;
                item.UseItem(amount);
            }
            return true;
        }

        private void RefundBuyItems(BasePlayer player, List<ResourceItem> requiredItems)
        {
            if (player == null || !TryBuildRequiredItemMap(requiredItems, out Dictionary<string, int> requiredItemMap))
                return;
            foreach (var requirement in requiredItemMap)
            {
                ItemDefinition definition = ItemManager.FindItemDefinition(requirement.Key);
                if (definition == null)
                    continue;
                int remaining = requirement.Value;
                int stackSize = Math.Max(1, definition.stackable);
                while (remaining > 0)
                {
                    int amount = Math.Min(stackSize, remaining);
                    Item refundItem = ItemManager.Create(definition, amount, 0UL);
                    if (refundItem == null)
                        break;
                    if (!player.inventory.GiveItem(refundItem))
                        refundItem.Drop(player.transform.position + Vector3.up, Vector3.zero);
                    remaining -= amount;
                }
            }
        }

        private void DropRecyclerItems(Recycler recycler)
        {
            if (recycler == null)
                return;
            var recyclerComponent = recycler.GetComponent<RecyclerComponent>();
            IEnumerable<Item> sourceItems = recyclerComponent != null ? recyclerComponent.GatherAllItems() : recycler.inventory?.itemList ?? Enumerable.Empty<Item>();
            List<Item> itemsToDrop = sourceItems
                .Where(item => item != null && item.IsValid() && item.info != null && item.amount > 0)
                .GroupBy(item => item.uid.Value)
                .Select(group => group.First())
                .ToList();
            if (itemsToDrop.Count == 0)
                return;
            Vector3 position = recycler.transform.position;
            Quaternion rotation = recycler.transform.rotation;
            foreach (Item item in itemsToDrop)
            {
                if (item.parent != null)
                    item.RemoveFromContainer();
            }
            for (int i = 0; i < itemsToDrop.Count; i += 6)
            {
                List<Item> batch = itemsToDrop.Skip(i).Take(6).ToList();
                SpawnDroppedItemContainer(position, rotation, batch);
            }
        }

        private DroppedItemContainer SpawnDroppedItemContainer(Vector3 position, Quaternion rotation, List<Item> itemsToDrop)
        {
            List<Item> validItems = itemsToDrop?.Where(item => item != null && item.IsValid() && item.info != null && item.amount > 0).ToList();
            if (validItems == null || validItems.Count == 0)
                return null;
            var droppedItemContainer = GameManager.server.CreateEntity(DroppedItemPrefab, position, rotation) as DroppedItemContainer;
            if (droppedItemContainer == null)
                return null;
            InitializeItemContainer(droppedItemContainer, validItems);
            droppedItemContainer.Spawn();
            return droppedItemContainer;
        }

        private void InitializeItemContainer(DroppedItemContainer container, List<Item> items)
        {
            if (container == null)
                return;
            List<Item> validItems = items?.Where(item => item != null && item.IsValid() && item.info != null && item.amount > 0).ToList() ?? new List<Item>();
            container.inventory = new ItemContainer();
            container.inventory.ServerInitialize(null, Math.Max(1, validItems.Count));
            container.inventory.GiveUID();
            container.inventory.entityOwner = container;
            foreach (Item item in validItems)
            {
                if (!SafeMoveToContainer(item, container.inventory) && item.IsValid())
                    item.Drop(container.transform.position + Vector3.up, Vector3.zero);
            }
        }

        private class Configuration
        {
            [JsonProperty(PropertyName = "Recycler Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> RecyclerCommands { get; set; } = new List<string> { "industrialrecycler", "giveindustrialrecycler" };

            [JsonProperty(PropertyName = "Standard Recycler Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> StandardRecyclerCommands { get; set; } = new List<string> { "standardrecycler", "givestandardrecycler" };

            [JsonProperty(PropertyName = "Standard Recycler Purchase Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> StandardPurchaseCommands { get; set; } = new List<string> { "buyrecycler" };

            [JsonProperty(PropertyName = "Industrial Recycler Purchase Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> IndustrialPurchaseCommands { get; set; } = new List<string> { "buyindustrialrecycler" };

            [JsonProperty(PropertyName = "Play Sound Effect On Virtual Recycler")]
            public bool PlaySoundOnVirtual { get; set; } = true;

            [JsonProperty(PropertyName = "Speed 1 Seconds (Permission speed1)")]
            public float Speed1Duration { get; set; } = 4f;

            [JsonProperty(PropertyName = "Speed 2 Seconds (Permission speed2)")]
            public float Speed2Duration { get; set; } = 3f;

            [JsonProperty(PropertyName = "Speed 3 Seconds (Permission speed3)")]
            public float Speed3Duration { get; set; } = 2f;

            [JsonProperty(PropertyName = "Speed 4 Seconds (Permission speed4)")]
            public float Speed4Duration { get; set; } = 1f;

            [JsonProperty(PropertyName = "Virtual Speed 1 Seconds (Permission virtualspeed1)")]
            public float VirtualSpeed1Duration { get; set; } = 4f;

            [JsonProperty(PropertyName = "Virtual Speed 2 Seconds (Permission virtualspeed2)")]
            public float VirtualSpeed2Duration { get; set; } = 3f;

            [JsonProperty(PropertyName = "Virtual Speed 3 Seconds (Permission virtualspeed3)")]
            public float VirtualSpeed3Duration { get; set; } = 2f;

            [JsonProperty(PropertyName = "Virtual Speed 4 Seconds (Permission virtualspeed4)")]
            public float VirtualSpeed4Duration { get; set; } = 1f;

            [JsonProperty(PropertyName = "Efficiency 1 (Permission efficiency1)")]
            public float StandardEfficiency1 { get; set; } = 0.5f;

            [JsonProperty(PropertyName = "Efficiency 2 (Permission efficiency2)")]
            public float StandardEfficiency2 { get; set; } = 0.6f;

            [JsonProperty(PropertyName = "Efficiency 3 (Permission efficiency3)")]
            public float StandardEfficiency3 { get; set; } = 0.7f;

            [JsonProperty(PropertyName = "Efficiency 4 (Permission efficiency4)")]
            public float StandardEfficiency4 { get; set; } = 0.8f;

            [JsonProperty(PropertyName = "Virtual Efficiency Tier 1 (Permission virtualefficiency1)")]
            public float VirtualEfficiency1 { get; set; } = 0.5f;

            [JsonProperty(PropertyName = "Virtual Efficiency Tier 2 (Permission virtualefficiency2)")]
            public float VirtualEfficiency2 { get; set; } = 0.6f;

            [JsonProperty(PropertyName = "Virtual Efficiency Tier 3 (Permission virtualefficiency3)")]
            public float VirtualEfficiency3 { get; set; } = 0.7f;

            [JsonProperty(PropertyName = "Virtual Efficiency Tier 4 (Permission virtualefficiency4)")]
            public float VirtualEfficiency4 { get; set; } = 0.8f;

            [JsonProperty(PropertyName = "BaseItem")]
            public string BaseItem { get; set; } = "box.wooden.large";

            [JsonProperty(PropertyName = "Drop Recycler Item On Destroy")]
            public bool DropRecyclerItemOnDestroy { get; set; } = false;

            [JsonProperty(PropertyName = "Standard MaxRecyclers")]
            public int StandardMaxRecyclers { get; set; } = 1;

            [JsonProperty(PropertyName = "Vip1 Standard MaxRecyclers (Permission vip)")]
            public int Vip1StandardMaxRecyclers { get; set; } = 2;

            [JsonProperty(PropertyName = "Vip2 Standard MaxRecyclers (Permission vip2)")]
            public int Vip2StandardMaxRecyclers { get; set; } = 3;

            [JsonProperty(PropertyName = "Industrial MaxRecyclers")]
            public int IndustrialMaxRecyclers { get; set; } = 1;

            [JsonProperty(PropertyName = "Vip1 Industrial MaxRecyclers (Permission vip)")]
            public int Vip1IndustrialMaxRecyclers { get; set; } = 2;

            [JsonProperty(PropertyName = "Vip2 Industrial MaxRecyclers (Permission vip2)")]
            public int Vip2IndustrialMaxRecyclers { get; set; } = 3;

            [JsonProperty(PropertyName = "Standard Recycler SkinId")]
            public ulong StandardRecyclerSkinId { get; set; } = 3363257468;

            [JsonProperty(PropertyName = "Industrial Recycler Item SkinId")]
            public ulong IndustrialRecyclerSkinId { get; set; } = 3373542609;

            [JsonProperty(PropertyName = "AnchorMin")]
            public string AnchorMin { get; set; } = "0.5 0";

            [JsonProperty(PropertyName = "AnchorMax")]
            public string AnchorMax { get; set; } = "0.5 0";

            [JsonProperty(PropertyName = "Adapter Layout (1 Default | 2 Alternative)")]
            public int AdapterLayout { get; set; } = 2;

            [JsonProperty(PropertyName = "Storage Visibility (1 Hide | 2 Show)")]
            public int StorageVisibility { get; set; } = 1;

            [JsonProperty(PropertyName = "Only owner can access recycler")]
            public bool OnlyOwnerAccess { get; set; } = true;

            [JsonProperty(PropertyName = "Allow team members to access recycler")]
            public bool AllowTeamAccess { get; set; } = true;

            [JsonProperty(PropertyName = "Allow friends to access recycler")]
            public bool AllowFriendsAccess { get; set; } = true;

            [JsonProperty(PropertyName = "Standard Recycler Purchase Cost", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ResourceItem> PurchaseStandardCost { get; set; } =
                new List<ResourceItem>
                {
                    new ResourceItem { Shortname = "scrap", Amount = 1000 },
                    new ResourceItem { Shortname = "sheetmetal", Amount = 50 },
                    new ResourceItem { Shortname = "metal.refined", Amount = 100 },
                    new ResourceItem { Shortname = "metal.fragments", Amount = 500 },
                };

            [JsonProperty(PropertyName = "Industrial Recycler Purchase Cost", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ResourceItem> PurchaseIndustrialCost { get; set; } =
                new List<ResourceItem>
                {
                    new ResourceItem { Shortname = "scrap", Amount = 2000 },
                    new ResourceItem { Shortname = "sheetmetal", Amount = 100 },
                    new ResourceItem { Shortname = "metal.refined", Amount = 200 },
                    new ResourceItem { Shortname = "metal.fragments", Amount = 1000 },
                };

            [JsonProperty("Virtual Recycler Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public string[] VirtualUseCommands { get; set; } = { "irec", "virtualrec" };

            [JsonProperty("Disable Virtual Recycler During Raid Block (NoEscape)")]
            public bool DisableVirtualDuringRaidBlock { get; set; } = true;

            [JsonProperty("Disable Virtual Recycler During Combat Block (NoEscape)")]
            public bool DisableVirtualDuringCombatBlock { get; set; } = true;

            [JsonProperty("Version")]
            public VersionNumber Version { get; set; }
        }

        private class ResourceItem
        {
            [JsonProperty(PropertyName = "Shortname")]
            public string Shortname { get; set; }

            [JsonProperty(PropertyName = "Amount")]
            public int Amount { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                bool configChanged = false;
                _config = Config.ReadObject<Configuration>() ?? new Configuration();
                if (_config.Version == null || _config.Version < Version)
                {
                    PrintWarning("Updating configuration file to the new version.");
                    _config.Version = Version;
                    configChanged = true;
                }
                if (configChanged)
                {
                    PrintWarning("Configuration changes detected, saving changes.");
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                PrintError($"Error reading config: {ex.Message}");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            _config = new Configuration { Version = Version };
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(
                new Dictionary<string, string>
                {
                    ["NoPermission"] = "You do not have permission to use this command.",
                    ["RecyclerLimitReached"] = "You have reached the recycler limit.",
                    ["InvalidPlacement"] = "The recycler must be placed on a construction block.",
                    ["RecyclerPlaced"] = "Recycler placed successfully.",
                    ["RecyclerPickedUp"] = "Recycler picked up, item returned to inventory.",
                    ["RecyclerGiven"] = "Recycler given to your inventory.",
                    ["RecyclerReceived"] = "You have received a recycler.",
                    ["RecyclerNotGiven"] = "Failed to give recycler.",
                    ["RecyclerNotReceived"] = "Failed to receive recycler.",
                    ["NoRecyclerInView"] = "No recycler in sight to upgrade.",
                    ["RecyclerComponentNotFound"] = "Recycler component not found.",
                    ["RecyclerConfigToggled"] = "Recycler configuration toggled successfully.",
                    ["RecyclerNotConfigToggled"] = "Recycler has pipes connected, disconnect before toggling.",
                    ["PlayerNotFound"] = "The specified player could not be found.",
                    ["ToggleConfig"] = "Toggle",
                    ["InputButton"] = "Input",
                    ["OutputButton"] = "Output",
                    ["NoAccess"] = "You do not have access to this recycler.",
                    ["StandardRecyclerGiven"] = "Standard recycler added to your inventory.",
                    ["StandardRecyclerNotGiven"] = "Failed to give standard recycler.",
                    ["RecyclerUpgraded"] = "Recycler successfully upgraded to industrial.",
                    ["InventoryFull"] = "Your inventory is full! The recycler has been dropped on the ground.",
                    ["StandardRecyclerReceived"] = "You have received a standard recycler!",
                    ["IndustrialRecyclerReceived"] = "You have received an industrial recycler!",
                    ["PurchaseRecyclerTitle"] = "Purchase Recycler",
                    ["PurchaseIndustrialRecyclerTitle"] = "Purchase Industrial Recycler",
                    ["PurchaseCosts"] = "Purchase Costs:",
                    ["ConfirmPurchase"] = "Confirm Purchase",
                    ["Close"] = "Close",
                    ["PurchaseSuccessStandard"] = "You have purchased a Recycler!",
                    ["PurchaseSuccessIndustrial"] = "You have purchased an Industrial Recycler!",
                    ["PurchaseFailed"] = "You do not have the required items to purchase this Recycler.",
                    ["PurchaseDeliveryFailed"] = "The recycler could not be delivered. Any charged resources were refunded.",
                    ["NoPickupPermission"] = "You do not have permission to pick up the recycler.",
                    ["ItemsDropped"] = "You left items in the recycler and dropped them on the ground!",
                    ["IsRaidBlocked"] = "You cannot recycle while Raid Blocked",
                    ["IsCombatBlocked"] = "You cannot recycle while Combat Blocked",
                    ["GoToInput"] = "GO TO INPUT",
                    ["GoToOutput"] = "GO TO OUTPUT",
                    ["GoToRecycler"] = "GO TO RECYCLER",
                    ["Denied Swimming"] = "You cannot recycle while swimming",
                    ["Denied Falling"] = "You cannot recycle while falling",
                    ["Denied Mounted"] = "You cannot recycle while mounted",
                    ["Denied Ship"] = "You cannot recycle while on a ship",
                    ["Denied Elevator"] = "You cannot recycle while on an elevator",
                    ["Denied Balloon"] = "You cannot recycle while on a balloon",
                },
                this
            );
            lang.RegisterMessages(
                new Dictionary<string, string>
                {
                    ["NoPermission"] = "Você não tem permissão para usar este comando.",
                    ["RecyclerLimitReached"] = "Você atingiu o limite de recicladores.",
                    ["InvalidPlacement"] = "O reciclador deve ser colocado sobre um bloco de construção.",
                    ["RecyclerPlaced"] = "Reciclador colocado com sucesso.",
                    ["RecyclerPickedUp"] = "Reciclador recolhido, item devolvido ao inventário.",
                    ["RecyclerGiven"] = "Reciclador fornecido ao seu inventário.",
                    ["RecyclerReceived"] = "Você recebeu um reciclador.",
                    ["RecyclerNotGiven"] = "Falha ao fornecer o reciclador.",
                    ["RecyclerNotReceived"] = "Falha ao receber o reciclador.",
                    ["NoRecyclerInView"] = "Nenhum reciclador à vista para atualizar.",
                    ["RecyclerComponentNotFound"] = "Componente de reciclador não encontrado.",
                    ["RecyclerConfigToggled"] = "Configuração do reciclador alternada com sucesso.",
                    ["RecyclerNotConfigToggled"] = "O reciclador tem tubos conectados, desconecte antes de alternar.",
                    ["PlayerNotFound"] = "O jogador especificado não pôde ser encontrado.",
                    ["ToggleConfig"] = "Alterar",
                    ["InputButton"] = "Entrada",
                    ["OutputButton"] = "Saída",
                    ["NoAccess"] = "Você não tem acesso a este reciclador.",
                    ["StandardRecyclerGiven"] = "Reciclador padrão adicionado ao seu inventário.",
                    ["StandardRecyclerNotGiven"] = "Falha ao fornecer o reciclador padrão.",
                    ["RecyclerUpgraded"] = "Reciclador atualizado com sucesso para industrial.",
                    ["InventoryFull"] = "Seu inventário está cheio! O reciclador foi dropado no chão.",
                    ["StandardRecyclerReceived"] = "Você recebeu um reciclador padrão!",
                    ["IndustrialRecyclerReceived"] = "Você recebeu um reciclador industrial!",
                    ["PurchaseRecyclerTitle"] = "Comprar Reciclador",
                    ["PurchaseIndustrialRecyclerTitle"] = "Comprar Reciclador Industrial",
                    ["PurchaseCosts"] = "Custos de Compra:",
                    ["ConfirmPurchase"] = "Confirmar Compra",
                    ["Close"] = "Fechar",
                    ["PurchaseSuccessStandard"] = "Você comprou um Reciclador!",
                    ["PurchaseSuccessIndustrial"] = "Você comprou um Reciclador Industrial!",
                    ["PurchaseFailed"] = "Você não tem os itens necessários para comprar este Reciclador.",
                    ["PurchaseDeliveryFailed"] = "Não foi possível entregar o reciclador. Os recursos cobrados foram reembolsados.",
                    ["NoPickupPermission"] = "Você não tem permissão para pegar este reciclador.",
                    ["ItemsDropped"] = "Você deixou itens no reciclador e eles foram dropados no chão!",
                    ["IsRaidBlocked"] = "Você não pode reciclar enquanto estiver com Bloqueio de Raid",
                    ["IsCombatBlocked"] = "Você não pode reciclar enquanto estiver com Bloqueio de Combate",
                    ["GoToInput"] = "IR PARA ENTRADA",
                    ["GoToOutput"] = "IR PARA SAÍDA",
                    ["GoToRecycler"] = "IR PARA RECICLADOR",
                    ["Denied Swimming"] = "Você não pode reciclar enquanto está nadando",
                    ["Denied Falling"] = "Você não pode reciclar enquanto está caindo",
                    ["Denied Mounted"] = "Você não pode reciclar enquanto está montado",
                    ["Denied Ship"] = "Você não pode reciclar enquanto está em um navio",
                    ["Denied Elevator"] = "Você não pode reciclar enquanto está em um elevador",
                    ["Denied Balloon"] = "Você não pode reciclar enquanto está em um balão",
                },
                this,
                "pt-BR"
            );
            lang.RegisterMessages(
                new Dictionary<string, string>
                {
                    ["NoPermission"] = "Sie haben keine Erlaubnis, diesen Befehl zu verwenden.",
                    ["RecyclerLimitReached"] = "Sie haben die Grenze für Recycler erreicht.",
                    ["InvalidPlacement"] = "Der Recycler muss auf einem Baublock platziert werden.",
                    ["RecyclerPlaced"] = "Recycler erfolgreich platziert.",
                    ["RecyclerPickedUp"] = "Recycler aufgenommen, Artikel in das Inventar zurückgegeben.",
                    ["RecyclerGiven"] = "Recycler wurde Ihrem Inventar hinzugefügt.",
                    ["RecyclerReceived"] = "Sie haben einen Recycler erhalten.",
                    ["RecyclerNotGiven"] = "Fehler beim Geben des Recyclers.",
                    ["RecyclerNotReceived"] = "Fehler beim Empfangen des Recyclers.",
                    ["NoRecyclerInView"] = "Kein Recycler in Sicht, um aufzurüsten.",
                    ["RecyclerComponentNotFound"] = "Recycler-Komponente nicht gefunden.",
                    ["RecyclerConfigToggled"] = "Recycler-Konfiguration erfolgreich umgeschaltet.",
                    ["RecyclerNotConfigToggled"] = "Der Recycler hat angeschlossene Rohre, bitte vor dem Umschalten trennen.",
                    ["PlayerNotFound"] = "Der angegebene Spieler konnte nicht gefunden werden.",
                    ["ToggleConfig"] = "Umschalten",
                    ["InputButton"] = "Eingang",
                    ["OutputButton"] = "Ausgang",
                    ["NoAccess"] = "Sie haben keinen Zugriff auf diesen Recycler.",
                    ["StandardRecyclerGiven"] = "Standard-Recycler wurde Ihrem Inventar hinzugefügt.",
                    ["StandardRecyclerNotGiven"] = "Standard-Recycler konnte nicht gegeben werden.",
                    ["RecyclerUpgraded"] = "Recycler erfolgreich zu industriellem Recycler aufgerüstet.",
                    ["InventoryFull"] = "Dein Inventar ist voll! Der Recycler wurde auf den Boden geworfen.",
                    ["StandardRecyclerReceived"] = "Du hast einen Standard-Recycler erhalten!",
                    ["IndustrialRecyclerReceived"] = "Du hast einen industriellen Recycler erhalten!",
                    ["PurchaseRecyclerTitle"] = "Recycler kaufen",
                    ["PurchaseIndustrialRecyclerTitle"] = "Industriellen Recycler kaufen",
                    ["PurchaseCosts"] = "Kaufkosten:",
                    ["ConfirmPurchase"] = "Kauf bestätigen",
                    ["Close"] = "Schließen",
                    ["PurchaseSuccessStandard"] = "Sie haben einen Recycler gekauft!",
                    ["PurchaseSuccessIndustrial"] = "Sie haben einen Industriellen Recycler gekauft!",
                    ["PurchaseFailed"] = "Sie haben nicht die erforderlichen Gegenstände, um diesen Recycler zu kaufen.",
                    ["PurchaseDeliveryFailed"] = "Der Recycler konnte nicht zugestellt werden. Berechnete Ressourcen wurden zurückerstattet.",
                    ["NoPickupPermission"] = "Sie haben keine Berechtigung, diesen Recycler aufzuheben.",
                    ["ItemsDropped"] = "Du hast Gegenstände im Recycler gelassen und sie wurden auf den Boden geworfen!",
                    ["IsRaidBlocked"] = "Du kannst während eines Raids nicht recyceln",
                    ["IsCombatBlocked"] = "Du kannst während des Kampfes nicht recyceln",
                    ["GoToInput"] = "ZU EINGANG",
                    ["GoToOutput"] = "ZU AUSGANG",
                    ["GoToRecycler"] = "ZU RECYCLER",
                    ["Denied Swimming"] = "Du kannst nicht recyceln, während du schwimmst",
                    ["Denied Falling"] = "Du kannst nicht recyceln, während du fällst",
                    ["Denied Mounted"] = "Du kannst nicht recyceln, während du reitest",
                    ["Denied Ship"] = "Du kannst nicht recyceln, während du auf einem Schiff bist",
                    ["Denied Elevator"] = "Du kannst nicht recyceln, während du in einem Aufzug bist",
                    ["Denied Balloon"] = "Du kannst nicht recyceln, während du in einem Ballon bist",
                },
                this,
                "de"
            );
            lang.RegisterMessages(
                new Dictionary<string, string>
                {
                    ["NoPermission"] = "No tienes permiso para usar este comando.",
                    ["RecyclerLimitReached"] = "Has alcanzado el límite de recicladores.",
                    ["InvalidPlacement"] = "El reciclador debe colocarse en un bloque de construcción.",
                    ["RecyclerPlaced"] = "Reciclador colocado con éxito.",
                    ["RecyclerPickedUp"] = "Reciclador recogido, artículo devuelto al inventario.",
                    ["RecyclerGiven"] = "Reciclador añadido a tu inventario.",
                    ["RecyclerReceived"] = "Has recibido un reciclador.",
                    ["RecyclerNotGiven"] = "Error al entregar el reciclador.",
                    ["RecyclerNotReceived"] = "Error al recibir el reciclador.",
                    ["NoRecyclerInView"] = "No hay reciclador a la vista para mejorar.",
                    ["RecyclerComponentNotFound"] = "Componente de reciclador no encontrado.",
                    ["RecyclerConfigToggled"] = "Configuración del reciclador alternada con éxito.",
                    ["RecyclerNotConfigToggled"] = "El reciclador tiene tuberías conectadas, desconéctalas antes de alternar.",
                    ["PlayerNotFound"] = "No se pudo encontrar al jugador especificado.",
                    ["ToggleConfig"] = "Alternar",
                    ["InputButton"] = "Entrada",
                    ["OutputButton"] = "Salida",
                    ["NoAccess"] = "No tienes acceso a este reciclador.",
                    ["StandardRecyclerGiven"] = "Reciclador estándar añadido a tu inventario.",
                    ["StandardRecyclerNotGiven"] = "Error al entregar el reciclador estándar.",
                    ["RecyclerUpgraded"] = "Reciclador mejorado con éxito a industrial.",
                    ["InventoryFull"] = "¡Tu inventario está lleno! El reciclador se dejó caer al suelo.",
                    ["StandardRecyclerReceived"] = "¡Has recibido un reciclador estándar!",
                    ["IndustrialRecyclerReceived"] = "¡Has recibido un reciclador industrial!",
                    ["PurchaseRecyclerTitle"] = "Comprar Reciclador",
                    ["PurchaseIndustrialRecyclerTitle"] = "Comprar Reciclador Industrial",
                    ["PurchaseCosts"] = "Costos de Compra:",
                    ["ConfirmPurchase"] = "Confirmar Compra",
                    ["Close"] = "Cerrar",
                    ["PurchaseSuccessStandard"] = "¡Has comprado un Reciclador!",
                    ["PurchaseSuccessIndustrial"] = "¡Has comprado un Reciclador Industrial!",
                    ["PurchaseFailed"] = "No tienes los objetos necesarios para comprar este Reciclador.",
                    ["PurchaseDeliveryFailed"] = "No se pudo entregar el reciclador. Los recursos cobrados fueron reembolsados.",
                    ["NoPickupPermission"] = "No tienes permiso para recoger este reciclador.",
                    ["ItemsDropped"] = "¡Has dejado objetos en el reciclador y fueron tirados al suelo!",
                    ["IsRaidBlocked"] = "No puedes reciclar mientras estás bloqueado por redada",
                    ["IsCombatBlocked"] = "No puedes reciclar mientras estás bloqueado por combate",
                    ["GoToInput"] = "IR A ENTRADA",
                    ["GoToOutput"] = "IR A SALIDA",
                    ["GoToRecycler"] = "IR AL RECICLADOR",
                    ["Denied Swimming"] = "No puedes reciclar mientras estás nadando",
                    ["Denied Falling"] = "No puedes reciclar mientras estás cayendo",
                    ["Denied Mounted"] = "No puedes reciclar mientras estás montado",
                    ["Denied Ship"] = "No puedes reciclar mientras estás en un barco",
                    ["Denied Elevator"] = "No puedes reciclar mientras estás en un ascensor",
                    ["Denied Balloon"] = "No puedes reciclar mientras estás en un globo",
                },
                this,
                "es"
            );
        }

        private class RecyclerComponent : MonoBehaviour
        {
            public Recycler Recycler { get; private set; }
            public StorageContainer InputStorage { get; private set; }
            public StorageContainer OutputStorage { get; private set; }
            public int CurrentConfigState { get; private set; }
            private const int MaxStorageCapacity = 48;
            private const float TransferInterval = 8f;

            private enum RecyclerConfig
            {
                Default_Hide = 1,
                Default_Show = 2,
                Alternative_Hide = 3,
                Alternative_Show = 4,
            }

            private void Awake()
            {
                Recycler = GetComponent<Recycler>();
                if (_instance == null || Recycler == null || !_instance.IsRecyclerIndustrial(Recycler))
                {
                    enabled = false;
                    return;
                }
                InitializeRecyclerComponents();
                if (!enabled || InputStorage == null || OutputStorage == null)
                    return;
                Vector3 defHideIn = new Vector3(0.0f, 0.72f, 0.11f);
                Vector3 defHideOut = new Vector3(-0.4f, 0.72f, 0.2f);
                Vector3 defShowIn = new Vector3(-1.0f, 0.72f, 0.31f);
                Vector3 defShowOut = new Vector3(-0.9f, 0.72f, -0.09f);
                Vector3 altHideIn = new Vector3(0.0f, 0.72f, 0.11f);
                Vector3 altHideOut = new Vector3(-0.4f, 0.8f, 0.2f);
                Vector3 altShowIn = new Vector3(-1.0f, 0.72f, 0.31f);
                Vector3 altShowOut = new Vector3(-0.9f, 0.8f, -0.09f);
                float d1 = Vector3.Distance(InputStorage.transform.localPosition, defHideIn) + Vector3.Distance(OutputStorage.transform.localPosition, defHideOut);
                float d2 = Vector3.Distance(InputStorage.transform.localPosition, defShowIn) + Vector3.Distance(OutputStorage.transform.localPosition, defShowOut);
                float d3 = Vector3.Distance(InputStorage.transform.localPosition, altHideIn) + Vector3.Distance(OutputStorage.transform.localPosition, altHideOut);
                float d4 = Vector3.Distance(InputStorage.transform.localPosition, altShowIn) + Vector3.Distance(OutputStorage.transform.localPosition, altShowOut);
                float min = d1;
                int state = (int)RecyclerConfig.Default_Hide;
                if (d2 < min)
                {
                    min = d2;
                    state = (int)RecyclerConfig.Default_Show;
                }
                if (d3 < min)
                {
                    min = d3;
                    state = (int)RecyclerConfig.Alternative_Hide;
                }
                if (d4 < min)
                {
                    min = d4;
                    state = (int)RecyclerConfig.Alternative_Show;
                }
                CurrentConfigState = state;
            }

            private void InitializeRecyclerComponents()
            {
                foreach (MonoBehaviour mb in GetComponents<MonoBehaviour>())
                {
                    var type = mb.GetType();
                    if (type != typeof(Recycler) && type != typeof(RecyclerComponent) && type.Name != "Ground_Watch")
                    {
                        Destroy(mb);
                    }
                }
                if (!ValidateStorageContainers())
                {
                    enabled = false;
                    return;
                }
                SetupRecycler();
                RegisterEntities();
                InvokeRepeating(nameof(HandleItemTransfers), 0f, TransferInterval);
            }

            private bool ValidateStorageContainers()
            {
                if (Recycler?.children == null)
                    return false;
                string inputShort = Path.GetFileNameWithoutExtension(InputStoragePrefabPath);
                string outputShort = Path.GetFileNameWithoutExtension(OutputStoragePrefabPath);
                InputStorage = Recycler
                    .children.OfType<StorageContainer>()
                    .FirstOrDefault(c =>
                        c != null
                        && (
                            (c.PrefabName ?? string.Empty).Equals(InputStoragePrefabPath, StringComparison.OrdinalIgnoreCase)
                            || (c.ShortPrefabName ?? string.Empty).Equals(inputShort, StringComparison.OrdinalIgnoreCase)
                        )
                    );
                OutputStorage = Recycler
                    .children.OfType<StorageContainer>()
                    .FirstOrDefault(c =>
                        c != null
                        && (
                            (c.PrefabName ?? string.Empty).Equals(OutputStoragePrefabPath, StringComparison.OrdinalIgnoreCase)
                            || (c.ShortPrefabName ?? string.Empty).Equals(outputShort, StringComparison.OrdinalIgnoreCase)
                        )
                    );
                return InputStorage != null && OutputStorage != null;
            }

            private void RegisterEntities()
            {
                if (_instance == null || Recycler?.inventory == null || InputStorage?.inventory == null || OutputStorage?.inventory == null)
                    return;
                _instance._inputItemContainers.Add(InputStorage.inventory.uid.Value);
                _instance._recyclerItemContainers.Add(Recycler.inventory.uid.Value);
                var inputAdapter = InputStorage.children?.OfType<IndustrialStorageAdaptor>().FirstOrDefault();
                if (inputAdapter?.net != null)
                    _instance._storageAdapters.Add(inputAdapter.net.ID.Value);
                var outputAdapter = OutputStorage.children?.OfType<IndustrialStorageAdaptor>().FirstOrDefault();
                if (outputAdapter?.net != null)
                    _instance._storageAdapters.Add(outputAdapter.net.ID.Value);
            }

            private void UnregisterEntities()
            {
                if (_instance == null)
                    return;
                if (InputStorage?.inventory != null)
                    _instance._inputItemContainers.Remove(InputStorage.inventory.uid.Value);
                if (Recycler?.inventory != null)
                    _instance._recyclerItemContainers.Remove(Recycler.inventory.uid.Value);
                if (InputStorage?.children != null)
                {
                    foreach (var adapter in InputStorage.children.OfType<IndustrialStorageAdaptor>())
                        if (adapter?.net != null)
                            _instance._storageAdapters.Remove(adapter.net.ID.Value);
                }
                if (OutputStorage?.children != null)
                {
                    foreach (var adapter in OutputStorage.children.OfType<IndustrialStorageAdaptor>())
                        if (adapter?.net != null)
                            _instance._storageAdapters.Remove(adapter.net.ID.Value);
                }
            }

            private void SetupRecycler()
            {
                if (Recycler?.net == null || Recycler.inventory == null || InputStorage?.inventory == null || OutputStorage?.inventory == null)
                    return;
                _instance._recyclerOwners[Recycler.net.ID.Value] = Recycler.OwnerID;
                InputStorage.inventory.capacity = MaxStorageCapacity;
                OutputStorage.inventory.capacity = MaxStorageCapacity;
                SanitizeContainer(Recycler.inventory);
                SanitizeContainer(InputStorage.inventory);
                SanitizeContainer(OutputStorage.inventory);
            }

            public void HandleItemTransfers()
            {
                if (Recycler == null || Recycler.IsDestroyed || Recycler.inventory == null || OutputStorage?.inventory == null || InputStorage?.inventory == null)
                {
                    CancelInvoke(nameof(HandleItemTransfers));
                    return;
                }
                SanitizeContainer(Recycler.inventory);
                SanitizeContainer(InputStorage.inventory);
                SanitizeContainer(OutputStorage.inventory);
                int availableRecyclerSlots = CountAvailableRecyclerInputSlots();
                TransferItemsFromRecyclerToOutput();
                availableRecyclerSlots = CountAvailableRecyclerInputSlots();
                TransferItemsFromInputToRecycler(ref availableRecyclerSlots);
                SanitizeContainer(Recycler.inventory);
                SanitizeContainer(InputStorage.inventory);
                SanitizeContainer(OutputStorage.inventory);
                if (CountAvailableRecyclerInputSlots() < 6 && Recycler.inventory.itemList.Any(IsUsableItem))
                    Recycler.StartRecycling();
                else if (Recycler.inventory.itemList.Count == 0)
                    Recycler.StopRecycling();
            }

            private int CountAvailableRecyclerInputSlots()
            {
                if (Recycler?.inventory == null)
                    return 0;
                int available = 0;
                for (int slot = 0; slot < 6; slot++)
                    if (Recycler.inventory.GetSlot(slot) == null)
                        available++;
                return available;
            }

            private void TransferItemsFromRecyclerToOutput()
            {
                if (Recycler?.inventory == null || OutputStorage?.inventory == null)
                    return;
                for (int slot = 6; slot < Recycler.inventory.capacity; slot++)
                {
                    Item itemInRecycler = Recycler.inventory.GetSlot(slot);
                    if (!IsUsableItem(itemInRecycler))
                        continue;
                    _instance.SafeMoveToContainer(itemInRecycler, OutputStorage.inventory);
                }
            }

            private void TransferItemsFromInputToRecycler(ref int availableRecyclerSlots)
            {
                if (InputStorage?.inventory == null || OutputStorage?.inventory == null || Recycler?.inventory == null)
                    return;
                var inputItems = new List<Item>(InputStorage.inventory.itemList.Where(IsUsableItem));
                foreach (Item item in inputItems)
                {
                    if (!IsUsableItem(item))
                        continue;
                    if (item.info.Blueprint != null)
                    {
                        MoveItemToRecycler(item, ref availableRecyclerSlots);
                    }
                    else
                    {
                        _instance.SafeMoveToContainer(item, OutputStorage.inventory);
                    }
                }
            }

            private void MoveItemToRecycler(Item item, ref int availableRecyclerSlots)
            {
                if (availableRecyclerSlots <= 0 || !IsUsableItem(item) || Recycler?.inventory == null)
                    return;
                for (int slot = 0; slot < 6; slot++)
                {
                    if (Recycler.inventory.GetSlot(slot) == null && item.MoveToContainer(Recycler.inventory, slot))
                    {
                        availableRecyclerSlots--;
                        break;
                    }
                }
            }

            private static bool IsUsableItem(Item item)
            {
                return item != null && item.IsValid() && item.info != null && item.amount > 0;
            }

            private static void SanitizeContainer(ItemContainer container)
            {
                if (container?.itemList == null)
                    return;
                foreach (Item item in container.itemList.ToList())
                {
                    if (item == null || !item.IsValid() || item.info == null)
                        continue;
                    if (item.amount <= 0)
                        item.Remove();
                }
                container.itemList.RemoveAll(item => item == null || !item.IsValid() || item.info == null || item.amount <= 0);
            }

            public bool ToggleConfig()
            {
                if (Recycler == null || Recycler.IsDestroyed || AdaptersHavePipesConnected())
                    return false;
                CancelInvoke(nameof(HandleItemTransfers));
                SanitizeContainer(InputStorage?.inventory);
                SanitizeContainer(OutputStorage?.inventory);
                List<Item> inputItems = DetachContainerItems(InputStorage?.inventory);
                List<Item> outputItems = DetachContainerItems(OutputStorage?.inventory);
                bool isAlt = CurrentConfigState == (int)RecyclerConfig.Alternative_Hide || CurrentConfigState == (int)RecyclerConfig.Alternative_Show;
                bool isHide = CurrentConfigState == (int)RecyclerConfig.Default_Hide || CurrentConfigState == (int)RecyclerConfig.Alternative_Hide;
                if (isAlt)
                    CurrentConfigState = isHide ? (int)RecyclerConfig.Default_Hide : (int)RecyclerConfig.Default_Show;
                else
                    CurrentConfigState = isHide ? (int)RecyclerConfig.Alternative_Hide : (int)RecyclerConfig.Alternative_Show;
                ClearAttachments();
                ApplyLayout();
                if (InputStorage?.inventory == null || OutputStorage?.inventory == null)
                {
                    RestoreContainerItems(inputItems, null, Recycler.transform.position);
                    RestoreContainerItems(outputItems, null, Recycler.transform.position);
                    InvokeRepeating(nameof(HandleItemTransfers), 0f, TransferInterval);
                    return false;
                }
                RestoreContainerItems(inputItems, InputStorage.inventory, Recycler.transform.position);
                RestoreContainerItems(outputItems, OutputStorage.inventory, Recycler.transform.position);
                _instance.UpdateRecyclerLayout(Recycler, CurrentConfigState);
                InvokeRepeating(nameof(HandleItemTransfers), 0f, TransferInterval);
                return true;
            }

            private static List<Item> DetachContainerItems(ItemContainer container)
            {
                if (container?.itemList == null)
                    return new List<Item>();
                List<Item> items = container.itemList.Where(IsUsableItem).ToList();
                foreach (Item item in items)
                    item.RemoveFromContainer();
                return items;
            }

            private static void RestoreContainerItems(IEnumerable<Item> items, ItemContainer target, Vector3 fallbackPosition)
            {
                if (items == null)
                    return;
                foreach (Item item in items.Where(IsUsableItem))
                {
                    if (target == null || !_instance.SafeMoveToContainer(item, target))
                        item.Drop(fallbackPosition + Vector3.up, Vector3.zero);
                }
            }

            private bool AdaptersHavePipesConnected()
            {
                IndustrialStorageAdaptor ia = null;
                IndustrialStorageAdaptor oa = null;
                if (InputStorage != null && InputStorage.children != null)
                    ia = InputStorage.children.OfType<IndustrialStorageAdaptor>().FirstOrDefault();
                if (OutputStorage != null && OutputStorage.children != null)
                    oa = OutputStorage.children.OfType<IndustrialStorageAdaptor>().FirstOrDefault();
                return IsAdapterConnected(ia) || IsAdapterConnected(oa);
            }

            private bool IsAdapterConnected(IOEntity io)
            {
                if (io?.inputs == null)
                    return false;
                foreach (var input in io.inputs)
                {
                    IOEntity connectedEntity = input?.connectedTo?.Get(true);
                    if (connectedEntity != null && connectedEntity.IsValid())
                        return true;
                }
                return false;
            }

            private void ClearAttachments()
            {
                UnregisterEntities();
                if (InputStorage != null && !InputStorage.IsDestroyed)
                    InputStorage.Kill();
                if (OutputStorage != null && !OutputStorage.IsDestroyed)
                    OutputStorage.Kill();
                InputStorage = null;
                OutputStorage = null;
            }

            public void SetLayout(int layout)
            {
                if (
                    layout != (int)RecyclerConfig.Default_Hide
                    && layout != (int)RecyclerConfig.Default_Show
                    && layout != (int)RecyclerConfig.Alternative_Hide
                    && layout != (int)RecyclerConfig.Alternative_Show
                )
                    layout = (int)RecyclerConfig.Default_Hide;
                if (CurrentConfigState == layout)
                    return;
                CurrentConfigState = layout;
                ApplyLayout();
            }

            private void ApplyLayout()
            {
                bool useAlt = CurrentConfigState == (int)RecyclerConfig.Alternative_Hide || CurrentConfigState == (int)RecyclerConfig.Alternative_Show;
                bool hide = CurrentConfigState == (int)RecyclerConfig.Default_Hide || CurrentConfigState == (int)RecyclerConfig.Alternative_Hide;
                Vector3 inputOffset,
                    outputOffset,
                    inputAdapterOffset,
                    outputAdapterOffset;
                Quaternion inputRot,
                    outputRot,
                    inputAdapterRot,
                    outputAdapterRot;
                if (useAlt)
                {
                    if (hide)
                    {
                        inputOffset = new Vector3(0.0f, 0.72f, 0.11f);
                        outputOffset = new Vector3(-0.4f, 0.8f, 0.2f);
                        inputAdapterOffset = new Vector3(0.32f, -0.6f, -0.03f);
                        outputAdapterOffset = new Vector3(-0.07f, 0.535f, 0.397f);
                        inputRot = Quaternion.Euler(0, 180, 191);
                        outputRot = Quaternion.Euler(169, 90, 180);
                        inputAdapterRot = Quaternion.Euler(0, 0, 0);
                        outputAdapterRot = Quaternion.Euler(180, 90, 0);
                    }
                    else
                    {
                        inputAdapterOffset = new Vector3(0.00f - 0.19f, -1.43f, -0.25f - 0.27f);
                        inputAdapterRot = Quaternion.Euler(180, 90, 270 + 189);
                        outputAdapterOffset = new Vector3(0.19f, 0.52f, -0.01f);
                        outputAdapterRot = Quaternion.Euler(180, 90, -9);
                        inputOffset = new Vector3(-1.0f, 0.72f, 0.31f);
                        outputOffset = new Vector3(-0.9f, 0.72f, -0.09f);
                        inputRot = Quaternion.Euler(90, 90, 180);
                        outputRot = Quaternion.Euler(180, 90, 180);
                    }
                }
                else
                {
                    if (hide)
                    {
                        inputOffset = new Vector3(0.0f, 0.72f, 0.11f);
                        outputOffset = new Vector3(-0.4f, 0.72f, 0.2f);
                        inputAdapterOffset = new Vector3(0.836f, -0.3301f, 0.18f);
                        outputAdapterOffset = new Vector3(-0.227f, 0.33f, 0.434f);
                        inputRot = Quaternion.Euler(0, 0, 180);
                        outputRot = Quaternion.Euler(180, 90, 180);
                        inputAdapterRot = Quaternion.Euler(0, 0, 0);
                        outputAdapterRot = Quaternion.Euler(180, 90, 0);
                    }
                    else
                    {
                        inputOffset = new Vector3(-1.0f, 0.72f, 0.31f);
                        outputOffset = new Vector3(-0.9f, 0.72f, -0.09f);
                        inputAdapterOffset = new Vector3(0.00f, -0.15f, -0.25f);
                        outputAdapterOffset = new Vector3(0.00f, 0.26f, -0.05f);
                        inputRot = Quaternion.Euler(90, 90, 180);
                        outputRot = Quaternion.Euler(180, 90, 180);
                        inputAdapterRot = Quaternion.Euler(180, 90, 270);
                        outputAdapterRot = Quaternion.Euler(0, 90, 0);
                    }
                }
                if (Recycler == null || Recycler.IsDestroyed)
                    return;
                BasePlayer owner = BasePlayer.FindByID(Recycler.OwnerID) ?? BasePlayer.FindSleeping(Recycler.OwnerID);
                if (InputStorage == null || InputStorage.IsDestroyed)
                    InputStorage = _instance.SpawnEntity(InputStoragePrefabPath, Recycler, owner, inputOffset, inputRot) as StorageContainer;
                if (InputStorage != null && !InputStorage.IsDestroyed && InputStorage.inventory != null)
                {
                    InputStorage.transform.localPosition = inputOffset;
                    InputStorage.transform.localRotation = inputRot;
                    InputStorage.inventory.capacity = MaxStorageCapacity;
                    var ia =
                        (InputStorage.children?.OfType<IndustrialStorageAdaptor>().FirstOrDefault())
                        ?? _instance.SpawnEntity(AdapterPrefabPath, InputStorage, owner, inputAdapterOffset, inputAdapterRot) as IndustrialStorageAdaptor;
                    if (ia != null)
                    {
                        ia.transform.localPosition = inputAdapterOffset;
                        ia.transform.localRotation = inputAdapterRot;
                        ia.SendNetworkUpdateImmediate();
                    }
                }
                if (OutputStorage == null || OutputStorage.IsDestroyed)
                    OutputStorage = _instance.SpawnEntity(OutputStoragePrefabPath, Recycler, owner, outputOffset, outputRot) as StorageContainer;
                if (OutputStorage != null && !OutputStorage.IsDestroyed && OutputStorage.inventory != null)
                {
                    OutputStorage.transform.localPosition = outputOffset;
                    OutputStorage.transform.localRotation = outputRot;
                    OutputStorage.inventory.capacity = MaxStorageCapacity;
                    var oa =
                        (OutputStorage.children?.OfType<IndustrialStorageAdaptor>().FirstOrDefault())
                        ?? _instance.SpawnEntity(AdapterPrefabPath, OutputStorage, owner, outputAdapterOffset, outputAdapterRot) as IndustrialStorageAdaptor;
                    if (oa != null)
                    {
                        oa.transform.localPosition = outputAdapterOffset;
                        oa.transform.localRotation = outputAdapterRot;
                        oa.SendNetworkUpdateImmediate();
                    }
                }
                InputStorage?.SendNetworkUpdateImmediate();
                OutputStorage?.SendNetworkUpdateImmediate();
                RegisterEntities();
            }

            internal void DropItems()
            {
                _instance.DropRecyclerItems(Recycler);
            }

            public List<Item> GatherAllItems()
            {
                var items = new List<Item>();
                if (InputStorage?.inventory?.itemList != null)
                    items.AddRange(InputStorage.inventory.itemList.Where(IsUsableItem));
                if (OutputStorage?.inventory?.itemList != null)
                    items.AddRange(OutputStorage.inventory.itemList.Where(IsUsableItem));
                if (Recycler?.inventory?.itemList != null)
                    items.AddRange(Recycler.inventory.itemList.Where(IsUsableItem));
                return items.GroupBy(item => item.uid.Value).Select(group => group.First()).ToList();
            }

            private void OnDestroy()
            {
                CancelInvoke(nameof(HandleItemTransfers));
                UnregisterEntities();
            }
        }

        private float GetConfiguredEfficiencyFor(ulong ownerId, bool isVirtual)
        {
            string uid = ownerId.ToString();
            if (isVirtual)
            {
                if (permission.UserHasPermission(uid, PermissionVirtualEfficiency4))
                    return Mathf.Clamp01(_config.VirtualEfficiency4);
                if (permission.UserHasPermission(uid, PermissionVirtualEfficiency3))
                    return Mathf.Clamp01(_config.VirtualEfficiency3);
                if (permission.UserHasPermission(uid, PermissionVirtualEfficiency2))
                    return Mathf.Clamp01(_config.VirtualEfficiency2);
                if (permission.UserHasPermission(uid, PermissionVirtualEfficiency1))
                    return Mathf.Clamp01(_config.VirtualEfficiency1);
                return 0.6f;
            }
            if (permission.UserHasPermission(uid, PermissionEfficiency4))
                return Mathf.Clamp01(_config.StandardEfficiency4);
            if (permission.UserHasPermission(uid, PermissionEfficiency3))
                return Mathf.Clamp01(_config.StandardEfficiency3);
            if (permission.UserHasPermission(uid, PermissionEfficiency2))
                return Mathf.Clamp01(_config.StandardEfficiency2);
            if (permission.UserHasPermission(uid, PermissionEfficiency1))
                return Mathf.Clamp01(_config.StandardEfficiency1);
            return 0.6f;
        }

        private void ApplyEfficiency(Recycler recycler, float efficiency)
        {
            if (recycler == null)
                return;
            recycler.lastFetchedEfficiency = Mathf.Clamp01(efficiency);
        }

        [AutoPatch]
        [HarmonyPatch(typeof(Recycler), nameof(Recycler.GetRecyclerStats))]
        private class Recycler_GetRecyclerStats_Patch
        {
            static void Postfix(Recycler __instance, ref float efficiency, ref float duration)
            {
                IndustrialRecycler plugin = IndustrialRecycler._instance;
                if (plugin == null || __instance == null || __instance.net == null)
                {
                    return;
                }
                bool isVirtual = plugin._playerVirtualRecycler.Values.Contains(__instance);
                bool isManaged = plugin._industrialRecyclers.Contains(__instance.net.ID.Value) || plugin._standardRecyclers.Contains(__instance.net.ID.Value) || isVirtual;
                if (!isManaged)
                    return;
                ulong ownerId = __instance.OwnerID;
                string ownerIdString = ownerId.ToString();
                efficiency = plugin.GetConfiguredEfficiencyFor(ownerId, isVirtual);
                if (isVirtual)
                {
                    if (plugin.permission.UserHasPermission(ownerIdString, PermissionVirtualSpeed4))
                    {
                        duration = plugin._config.VirtualSpeed4Duration;
                    }
                    else if (plugin.permission.UserHasPermission(ownerIdString, PermissionVirtualSpeed3))
                    {
                        duration = plugin._config.VirtualSpeed3Duration;
                    }
                    else if (plugin.permission.UserHasPermission(ownerIdString, PermissionVirtualSpeed2))
                    {
                        duration = plugin._config.VirtualSpeed2Duration;
                    }
                    else if (plugin.permission.UserHasPermission(ownerIdString, PermissionVirtualSpeed1))
                    {
                        duration = plugin._config.VirtualSpeed1Duration;
                    }
                    else if (plugin.permission.UserHasPermission(ownerIdString, PermissionSpeed4))
                    {
                        duration = plugin._config.VirtualSpeed4Duration;
                    }
                    else if (plugin.permission.UserHasPermission(ownerIdString, PermissionSpeed3))
                    {
                        duration = plugin._config.VirtualSpeed3Duration;
                    }
                    else if (plugin.permission.UserHasPermission(ownerIdString, PermissionSpeed2))
                    {
                        duration = plugin._config.VirtualSpeed2Duration;
                    }
                    else if (plugin.permission.UserHasPermission(ownerIdString, PermissionSpeed1))
                    {
                        duration = plugin._config.VirtualSpeed1Duration;
                    }
                    return;
                }
                if (plugin.permission.UserHasPermission(ownerIdString, PermissionSpeed4))
                {
                    duration = plugin._config.Speed4Duration;
                }
                else if (plugin.permission.UserHasPermission(ownerIdString, PermissionSpeed3))
                {
                    duration = plugin._config.Speed3Duration;
                }
                else if (plugin.permission.UserHasPermission(ownerIdString, PermissionSpeed2))
                {
                    duration = plugin._config.Speed2Duration;
                }
                else if (plugin.permission.UserHasPermission(ownerIdString, PermissionSpeed1))
                {
                    duration = plugin._config.Speed1Duration;
                }
            }
        }

        [AutoPatch]
        [HarmonyPatch(typeof(Recycler), "MoveItemToOutput")]
        private class Recycler_MoveItemToOutput_Patch
        {
            static bool Prefix(Recycler __instance, Item newItem, ref bool __result)
            {
                if (_instance == null || __instance == null || __instance.IsDestroyed)
                    return true;
                if (!IsUsable(newItem))
                {
                    __result = false;
                    return false;
                }
                var component = __instance.GetComponent<RecyclerComponent>();
                ItemContainer outputInventory = component?.OutputStorage?.inventory;
                if (outputInventory == null)
                    return true;
                Sanitize(outputInventory);
                if (TryStackInto(outputInventory, newItem))
                {
                    __result = true;
                    return false;
                }
                if (IsUsable(newItem) && newItem.MoveToContainer(outputInventory))
                {
                    __result = true;
                    return false;
                }
                ItemContainer recyclerInventory = __instance.inventory;
                if (recyclerInventory != null && IsUsable(newItem))
                {
                    Sanitize(recyclerInventory);
                    int maximumSlot = Math.Min(12, recyclerInventory.capacity);
                    for (int slotIndex = 6; slotIndex < maximumSlot && IsUsable(newItem); slotIndex++)
                    {
                        Item slotItem = recyclerInventory.GetSlot(slotIndex);
                        if (slotItem == null)
                        {
                            if (newItem.MoveToContainer(recyclerInventory, slotIndex))
                            {
                                __result = true;
                                return false;
                            }
                            continue;
                        }
                        if (!IsUsable(slotItem) || !slotItem.CanStack(newItem))
                            continue;
                        MoveIntoStack(newItem, slotItem);
                    }
                    if (!IsUsable(newItem))
                    {
                        __result = true;
                        return false;
                    }
                }
                __instance.StopRecycling();
                __result = false;
                return false;
            }

            private static bool TryStackInto(ItemContainer container, Item sourceItem)
            {
                if (container == null || !IsUsable(sourceItem))
                    return false;
                for (int slotIndex = 0; slotIndex < container.capacity && IsUsable(sourceItem); slotIndex++)
                {
                    Item slotItem = container.GetSlot(slotIndex);
                    if (!IsUsable(slotItem) || !slotItem.CanStack(sourceItem))
                        continue;
                    MoveIntoStack(sourceItem, slotItem);
                }
                return !IsUsable(sourceItem);
            }

            private static void MoveIntoStack(Item sourceItem, Item targetItem)
            {
                if (!IsUsable(sourceItem) || !IsUsable(targetItem))
                    return;
                int availableSpace = targetItem.MaxStackable() - targetItem.amount;
                int moveAmount = Math.Min(availableSpace, sourceItem.amount);
                if (moveAmount <= 0)
                    return;
                targetItem.amount += moveAmount;
                targetItem.MarkDirty();
                sourceItem.amount -= moveAmount;
                if (sourceItem.amount <= 0)
                {
                    sourceItem.Remove();
                    return;
                }
                sourceItem.MarkDirty();
            }

            private static bool IsUsable(Item item)
            {
                return item != null && item.IsValid() && item.info != null && item.amount > 0;
            }

            private static void Sanitize(ItemContainer container)
            {
                if (container?.itemList == null)
                    return;
                foreach (Item item in container.itemList.ToList())
                {
                    if (item != null && item.IsValid() && item.info != null && item.amount <= 0)
                        item.Remove();
                }
                container.itemList.RemoveAll(item => item == null || !item.IsValid() || item.info == null || item.amount <= 0);
            }
        }
    }
}
 