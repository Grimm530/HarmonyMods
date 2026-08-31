using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Network;
using Newtonsoft.Json;
using UnityEngine;

namespace Trade
{
    public class TradePlugin
    {
        private const int CurrentConfigRevision = 2;
        private const string PermAllowUse = "trade.use";
        private const uint VisChkAcceptClick = 1159607245u;
        private const uint VisChkCancelClick = 3168107540u;
        private const uint ShopFrontPrefabId = 1180657261u;
        private const string ShopFrontPrefab = "assets/prefabs/building/wall.frame.shopfront/wall.frame.shopfront.metal.prefab";
        private static readonly object FalseObj = false;
        private static readonly Effect EffectInstance = new Effect();

        private readonly Dictionary<ulong, DateTime> _cooldowns = new Dictionary<ulong, DateTime>();
        private readonly Dictionary<NetworkableId, TradeController> _trades = new Dictionary<NetworkableId, TradeController>();
        private readonly Dictionary<ulong, PendingTrade> _pendingTrades = new Dictionary<ulong, PendingTrade>();
        private readonly Dictionary<string, string> _lang = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly string _configPath;
        private readonly string _langPath;
        private readonly string _dataDir;
        private Cfg _cfg;

        public TradePlugin(string serverRoot)
        {
            _configPath = Path.Combine(serverRoot, "HarmonyConfig", "Trade.json");
            _langPath = Path.Combine(serverRoot, "HarmonyLanguage", "Trade.json");
            _dataDir = Path.Combine(serverRoot, "HarmonyData", "Trade");
        }

        public void Load()
        {
            LoadDefaultMessages();
            LoadLangFile();
            LoadConfig();
            RegisterPermissions();
        }

        public void RegisterPermissions()
        {
            PermissionsBridge.RegisterPermission(PermAllowUse);
            if (_cfg.Permissions == null || _cfg.Permissions.Count <= 1) return;
            _cfg.Permissions.Sort((a, b) => a.Order.CompareTo(b.Order));
            for (int i = 1; i < _cfg.Permissions.Count; i++)
            {
                _cfg.Permissions[i].PermName = "trade." + _cfg.Permissions[i].Name;
                PermissionsBridge.RegisterPermission(_cfg.Permissions[i].PermName);
            }
        }

        public void Unload()
        {
            var list = new List<TradeController>(_trades.Values);
            for (int i = 0; i < list.Count; i++)
                InterruptTrade(list[i]);
            _trades.Clear();
            _cooldowns.Clear();
            _pendingTrades.Clear();
            EffectInstance.Clear();
        }

        public bool IsTradeBox(BaseNetworkable bn) =>
            bn != null && bn.net != null && _trades.ContainsKey(bn.net.ID);

        public object OnEntityVisibilityCheck(BaseEntity entity, BasePlayer player, uint rpcId, string debugName, float _)
        {
            if (rpcId != VisChkAcceptClick && rpcId != VisChkCancelClick)
                return null;
            if (entity.net == null) return null;
            var trade = GetTrade(entity.net.ID);
            if (!trade) return null;
            return !trade.ProcessUiClick(rpcId, player) ? FalseObj : true;
        }

        public void OnShopCompleteTrade(ShopFront shop)
        {
            if (shop?.net == null) return;
            if (shop.vendorPlayer == null || shop.customerPlayer == null) return;
            if (!shop.HasFlag(BaseEntity.Flags.Reserved1) || !shop.HasFlag(BaseEntity.Flags.Reserved2)) return;
            var trade = GetTrade(shop.net.ID);
            if (trade == null) return;
            trade.isCompleted = true;
            Message(trade.vendor, _("Msg.TradeSuccessful", trade.vendor, SanitizeName(trade.customer.displayName)));
            Message(trade.customer, _("Msg.TradeSuccessful", trade.customer, SanitizeName(trade.vendor.displayName)));
            _cooldowns[GetUserId(trade.vendor)] = DateTime.Now.AddSeconds(GetPermission(trade.vendor).Cooldown);
            _cooldowns[GetUserId(trade.customer)] = DateTime.Now.AddSeconds(GetPermission(trade.customer).Cooldown);
            SendEffect(shop.transactionCompleteEffect.resourcePath, trade.vendor, trade.customer);
            TradeMod.Instance?.Delay(() => DropTrade(trade), 0f);
            trade.OnTransactionCompleted();
        }

        public void OnPlayerLootEnd(PlayerLoot inventory)
        {
            if (inventory.entitySource == null || !inventory.entitySource.IsValid() || inventory.entitySource.prefabID != ShopFrontPrefabId)
                return;
            if (inventory.entitySource.net == null) return;
            var trade = GetTrade(inventory.entitySource.net.ID);
            if (trade == null || trade.isDestroying) return;
            InterruptTrade(trade);
        }

        public object CanMoveItem(Item item, PlayerInventory playerLoot, ItemContainerId targetContainer)
        {
            if (playerLoot == null || !(playerLoot.loot.entitySource is ShopFront))
                return null;
            var rootCont = item.GetRootContainer();
            if (rootCont == null) return null;
            var player = playerLoot.containerMain.playerOwner;
            if (player == null) return null;
            var shopFront = (ShopFront)playerLoot.loot.entitySource;
            var tc = playerLoot.FindContainer(targetContainer);
            if (tc?.parent != null)
                targetContainer = tc.parent.GetRootContainer()?.uid ?? targetContainer;
            if (targetContainer == shopFront.vendorInventory.uid && !shopFront.IsPlayerVendor(player)
                || targetContainer == shopFront.customerInventory.uid && !shopFront.IsPlayerCustomer(player))
                return FalseObj;
            if (rootCont == shopFront.vendorInventory && !shopFront.IsPlayerVendor(player)
                || rootCont == shopFront.customerInventory && !shopFront.IsPlayerCustomer(player))
                return FalseObj;
            shopFront.ResetTrade();
            return null;
        }

        public void CmdChatTrade(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (!PermissionsBridge.UserHasPermission(player.UserIDString, PermAllowUse))
            {
                Message(player, _("Error.NoPerm", player, _("Msg.You", player)));
                return;
            }
            if (args == null || args.Length == 0)
            {
                Message(player, _("Msg.TradeIntro", player));
                return;
            }

            PendingTrade pendingTrade;
            if (!PendingTradeExists(player))
            {
                var name = string.Join(" ", args);
                var targets = FindOnline(name);
                if (targets.Count == 0)
                {
                    Message(player, _("Error.NoSuchPlayer", player));
                    return;
                }
                if (targets.Count > 1)
                {
                    Message(player, _("Error.MultiplePlayers", player));
                    return;
                }
                var target = targets[0];
                if (target == player)
                {
                    Message(player, _("Error.SelfTrade", player));
                    return;
                }
                if (IsIgnored(player, target))
                {
                    Message(player, _("Error.Ignored", player));
                    return;
                }
                if (!CanPlayerTrade(player, player) || !CanPlayerTrade(target, player))
                    return;
                pendingTrade = new PendingTrade(player);
                pendingTrade.Customer = target;
                _pendingTrades[GetUserId(player)] = pendingTrade;
                _pendingTrades[GetUserId(target)] = pendingTrade;
                Message(player, _("Msg.TradeRequestSent", player, SanitizeName(target.displayName)));
                Message(target, _("Msg.TradeRequestReceived", target, SanitizeName(player.displayName)));
                SendEffect("assets/bundled/prefabs/fx/invite_notice.prefab", target);
                pendingTrade.CreateRequestTimer(_cfg.RequestTimeout, this);
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "accept":
                case "yes":
                case "+":
                    pendingTrade = GetPendingTrade(player);
                    if (pendingTrade == null || pendingTrade.Vendor == player)
                    {
                        Message(player, _("Error.NoPendingRequest", player));
                        return;
                    }
                    if (!CanPlayerTrade(player, player) || !CanPlayerTrade(pendingTrade.Vendor, player))
                        return;
                    pendingTrade.Remove(this);
                    var vendor = pendingTrade.Vendor;
                    var customer = player;
                    TradeMod.Instance?.Delay(() => OpenTrade(vendor, customer), 0.2f);
                    return;
                case "cancel":
                case "no":
                case "-":
                    pendingTrade = GetPendingTrade(player);
                    if (pendingTrade == null)
                    {
                        Message(player, _("Error.NoPendingRequest", player));
                        return;
                    }
                    pendingTrade.Remove(this);
                    Message(player, _("Msg.TradeCancelledCustomer", player));
                    var opposite = pendingTrade.GetOppositeTrader(player);
                    if (opposite != null && opposite.IsValid())
                        Message(opposite, _("Msg.TradeCancelledVendor", pendingTrade.Vendor, SanitizeName(player.displayName)));
                    return;
                default:
                    Message(player, _("Error.UnknownCommand", player));
                    return;
            }
        }

        internal PermissionDefinition GetPermission(BasePlayer player)
        {
            var highest = _cfg.Permissions[0];
            for (int i = 1; i < _cfg.Permissions.Count; i++)
            {
                var perm = _cfg.Permissions[i];
                if (string.IsNullOrEmpty(perm.PermName)) continue;
                if (!PermissionsBridge.UserHasPermission(player.UserIDString, perm.PermName))
                    continue;
                if (perm.Order > highest.Order)
                    highest = perm;
            }
            return highest;
        }

        private List<BasePlayer> FindOnline(string nameOrUserId)
        {
            var result = new List<BasePlayer>();
            var list = BasePlayer.activePlayerList;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p == null || p.limitNetworking) continue;
                if (p.UserIDString == nameOrUserId
                    || p.displayName.StartsWith(nameOrUserId, StringComparison.InvariantCultureIgnoreCase)
                    || p.displayName.IndexOf(nameOrUserId, StringComparison.InvariantCultureIgnoreCase) >= 0)
                    result.Add(p);
            }
            return result;
        }

        private TradeController GetTrade(NetworkableId shopFrontId)
        {
            TradeController tradeCtrl;
            return !_trades.TryGetValue(shopFrontId, out tradeCtrl) ? null : tradeCtrl;
        }

        private ShopFront GetShopFront(NetworkableId id)
        {
            var t = GetTrade(id);
            return t == null ? null : t.shop;
        }

        private ShopFront CreateShopFront(BasePlayer vendorPlayer, BasePlayer customerPlayer)
        {
            var bounds = SingletonComponent<ValidBounds>.Instance;
            var pos = new Vector3(bounds != null ? bounds.worldBounds.extents.x : 8000f, TerrainMeta.LowestPoint.y, 0);
            var shopFront = (ShopFront)GameManager.server.CreateEntity(ShopFrontPrefab, pos);
            shopFront.syncPosition = false;
            shopFront.enableSaving = false;
            shopFront.globalBroadcast = true;
            shopFront.Spawn();
            shopFront.decay = null;
            BuildingManager.server.Remove(shopFront);
            UnityEngine.Object.Destroy(shopFront.GetComponent<GroundWatch>());
            UnityEngine.Object.Destroy(shopFront.GetComponent<DestroyOnGroundMissing>());
            var vendorPerms = GetPermission(vendorPlayer);
            var customerPerms = GetPermission(customerPlayer);
            shopFront.vendorInventory.capacity = Mathf.Clamp(vendorPerms.TradingSlots, 1, 12);
            shopFront.customerInventory.capacity = Mathf.Clamp(_cfg.EnableIndividualTradeSlots ? customerPerms.TradingSlots : vendorPerms.TradingSlots, 1, 12);
            shopFront.vendorInventory.canAcceptItem = (player, item, i) => shopFront.ItemFilter(player, item, i) && CanAcceptVendorItem(shopFront, item, i) && TradeItemFilter(item, vendorPerms);
            shopFront.customerInventory.canAcceptItem = (player, item, i) => shopFront.ItemFilter(player, item, i) && CanAcceptCustomerItem(shopFront, item, i) && TradeItemFilter(item, customerPerms);
            SendEntitiesSnapshot(vendorPlayer, customerPlayer);
            SendEntitiesSnapshot(customerPlayer, vendorPlayer);
            vendorPlayer.EndLooting();
            customerPlayer.EndLooting();
            var netId = shopFront.net.ID;
            TradeMod.Instance?.Delay(() =>
            {
                StartLooting(netId, vendorPlayer);
                StartLooting(netId, customerPlayer);
            }, 0.2f);
            return shopFront;
        }

        internal void InterruptTrade(TradeController trade)
        {
            if (trade == null) return;
            if (!trade.isCompleted)
            {
                if (trade.vendor != null && trade.vendor.IsValid() && trade.vendor.IsConnected)
                    Message(trade.vendor, _("Msg.TradeInterrupted", trade.vendor));
                if (trade.customer != null && trade.customer.IsValid() && trade.customer.IsConnected)
                    Message(trade.customer, _("Msg.TradeInterrupted", trade.customer));
            }
            DropTrade(trade);
        }

        private void DropTrade(TradeController trade)
        {
            if (trade == null) return;
            trade.isDestroying = true;
            _trades.Remove(trade.shopId);
            trade.CancelInvoke(trade.CheckShop);
            if (trade.vendor != null) trade.vendor.EndLooting();
            if (trade.customer != null) trade.customer.EndLooting();
            var shop = trade.shop;
            if (shop != null && !shop.IsDestroyed)
            {
                shop.customerInventory?.Kill();
                shop.Kill();
            }
        }

        private void OpenTrade(BasePlayer vendorPlayer, BasePlayer customerPlayer)
        {
            var shopFront = CreateShopFront(vendorPlayer, customerPlayer);
            var tradeCtrl = shopFront.gameObject.AddComponent<TradeController>();
            tradeCtrl.plugin = this;
            tradeCtrl.Init(vendorPlayer, customerPlayer, _cfg);
            _trades.Add(tradeCtrl.shopId, tradeCtrl);
        }

        private void StartLooting(NetworkableId shopId, BasePlayer player)
        {
            var shopFront = GetShopFront(shopId);
            if (shopFront == null) return;
            player.EndLooting();
            player.inventory.loot.StartLootingEntity(shopFront, false);
            player.inventory.loot.AddContainer(shopFront.vendorInventory);
            player.inventory.loot.SendImmediate();
            player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), "shopfront");
            player.inventory.loot.AddContainer(shopFront.customerInventory);
            player.inventory.loot.SendImmediate();
            if (shopFront.vendorPlayer == null)
                shopFront.vendorPlayer = player;
            else
                shopFront.customerPlayer = player;
            shopFront.ResetTrade();
            shopFront.UpdatePlayers();
        }

        internal bool CanPlayerTrade(BasePlayer player, BasePlayer requestor)
        {
            var appeal = _(player == requestor ? "Msg.You" : "Msg.YourPartner", requestor);
            if (!PermissionsBridge.UserHasPermission(player.UserIDString, PermAllowUse))
            {
                Message(requestor, _("Error.NoPerm", requestor, appeal));
                return false;
            }
            if (!player.IsValid() || !player.IsConnected)
            {
                Message(requestor, _("Error.CantTradeOffline", requestor, _("Msg.YourPartner", player)));
                return false;
            }
            if (player.IsDead())
            {
                Message(requestor, _("Error.CantTradeDead", requestor, appeal));
                return false;
            }
            if (player.IsSleeping())
            {
                Message(requestor, _("Error.CantTradeSleeping", requestor, appeal));
                return false;
            }
            if (_cfg.DisallowInWater && player.IsSwimming())
            {
                Message(requestor, _("Error.CantTradeInWater", requestor, appeal));
                return false;
            }
            if (_cfg.DisallowRequestInBuildingBlock && _cfg.DisallowAcceptInBuildingBlock)
            {
                if (player.limitNetworking || !player.CanBuild())
                {
                    Message(requestor, _("Error.CantTradeInBuildingBlock", requestor, appeal));
                    return false;
                }
            }
            if (_cfg.DisallowInAir && (!player.IsOnGround() || player.IsFlying))
            {
                Message(requestor, _("Error.CantTradeInAir", requestor, appeal));
                return false;
            }
            if (_cfg.DisallowInWound && player.IsWounded())
            {
                Message(requestor, _("Error.CantTradeWounded", requestor, appeal));
                return false;
            }
            if ((_cfg.DisallowInTransport && player.GetMountedVehicle()) || player.GetParentEntity())
            {
                Message(requestor, _("Error.CantTradeInVehicle", requestor, appeal));
                return false;
            }
            DateTime until;
            if (_cooldowns.TryGetValue(GetUserId(requestor), out until))
            {
                var seconds = until.Subtract(DateTime.Now).TotalSeconds;
                if (seconds >= 0)
                {
                    Message(requestor, _("Error.TradeCooldown", requestor, TimeSpan.FromSeconds(seconds)));
                    return false;
                }
            }
            var requestorPerms = GetPermission(requestor);
            if (requestorPerms.MaxDist > 0f && Vector3.Distance(requestor.transform.position, player.transform.position) > requestorPerms.MaxDist)
            {
                Message(requestor, _("Error.TooFar", requestor, _("Msg.YourPartner", player)));
                return false;
            }
            return true;
        }

        private static void SendEffect(string effectStr, params BasePlayer[] players)
        {
            for (int i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player == null || !player.IsConnected) continue;
                EffectInstance.Init(Effect.Type.Generic, player, 0, Vector3.one, Vector3.zero);
                EffectInstance.pooledString = effectStr;
                EffectNetwork.Send(EffectInstance, player.net.connection);
                EffectInstance.Clear();
            }
        }

        private static string SanitizeName(string name)
        {
            if (name.Length > 24)
                name = name.Substring(0, 24).Trim();
            return name.EscapeRichText();
        }

        private static bool TradeItemFilter(Item item, PermissionDefinition perms) =>
            perms.BannedItems == null || !perms.BannedItems.Contains(item.info.shortname);

        private static bool CanAcceptVendorItem(ShopFront shop, Item item, int targetSlot) =>
            (shop.vendorPlayer != null && item.GetOwnerPlayer() == shop.vendorPlayer) || shop.vendorInventory.itemList.Contains(item) || item.parent == null;

        private static bool CanAcceptCustomerItem(ShopFront shop, Item item, int targetSlot) =>
            (shop.customerPlayer != null && item.GetOwnerPlayer() == shop.customerPlayer) || shop.customerInventory.itemList.Contains(item) || item.parent == null;

        private static void SendEntitiesSnapshot(BasePlayer recipientPlayer, params BaseNetworkable[] ents)
        {
            if (!recipientPlayer.IsConnected) return;
            for (int i = 0; i < ents.Length; i++)
                SendEntitySnapshotEx(recipientPlayer, ents[i]);
        }

        private static void SendEntitySnapshotEx(BaseNetworkable receiver, BaseNetworkable ent)
        {
            if (ent == null || ent.net == null) return;
            ++receiver.net.connection.validate.entityUpdates;
            var saveInfo = new BaseNetworkable.SaveInfo
            {
                forConnection = receiver.net.connection,
                forDisk = false
            };
            var nw = Net.sv.StartWrite();
            nw.PacketID(Network.Message.Type.Entities);
            nw.UInt32(receiver.net.connection.validate.entityUpdates);
            ent.ToStreamForNetwork(nw, saveInfo);
            nw.Send(new SendInfo(receiver.net.connection));
        }

        private static string GetItemName(Item item) => string.IsNullOrEmpty(item.name) ? item.info.displayName.english : item.name;

        private bool IsIgnored(BasePlayer player, BasePlayer target)
        {
            try
            {
                var type = AppDomain.CurrentDomain.GetData("Ignore_ApiType") as Type;
                if (type == null) return false;
                var mi = type.GetMethod("IsIgnored", BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                      ?? type.GetMethod("IsIgnoredS", BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                if (mi == null) return false;
                object inst = mi.IsStatic ? null : type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var rslt = mi.Invoke(inst, new object[] { player.UserIDString, target.UserIDString });
                return rslt is bool b && b;
            }
            catch { return false; }
        }

        private void Message(BasePlayer player, string text)
        {
            if (player == null || !player.IsConnected) return;
            player.SendConsoleCommand("chat.add", 2, _cfg.ChatIconId, text);
        }

        internal bool PendingTradeExists(BasePlayer player) => _pendingTrades.ContainsKey(GetUserId(player));

        internal PendingTrade GetPendingTrade(BasePlayer player)
        {
            PendingTrade pt;
            _pendingTrades.TryGetValue(GetUserId(player), out pt);
            return pt;
        }

        internal void RemovePending(ulong id) => _pendingTrades.Remove(id);

        internal static ulong GetUserId(BasePlayer player)
        {
            try { return player.userID.Get(); }
            catch { return (ulong)player.userID; }
        }

        public class TradeController : FacepunchBehaviour
        {
            public NetworkableId shopId;
            public bool isCompleted, isDestroying;
            public ShopFront shop;
            public BasePlayer vendor, customer;
            internal TradePlugin plugin;
            private bool _checkDist;
            private bool _tempLocked;
            private Vector3 _vendorLoc, _customerLoc;
            private Cfg _cfg;

            public void Init(BasePlayer vendorPlayer, BasePlayer customerPlayer, Cfg cfg)
            {
                vendor = vendorPlayer;
                customer = customerPlayer;
                _cfg = cfg;
                if (_cfg.MaxTradeSpotDistance > 0f)
                {
                    _vendorLoc = vendorPlayer.ServerPosition;
                    _customerLoc = customerPlayer.ServerPosition;
                    _checkDist = true;
                }
                InvokeRepeating(CheckShop, 1f, 0.40f);
            }

            public bool ProcessUiClick(uint rpcId, BasePlayer caller)
            {
                switch (rpcId)
                {
                    case VisChkAcceptClick:
                        if (shop.vendorPlayer != null && !plugin.CanPlayerTrade(shop.vendorPlayer, caller)
                            || shop.customerPlayer != null && !plugin.CanPlayerTrade(shop.customerPlayer, caller))
                        {
                            SendEffect("assets/prefabs/locks/keypad/effects/lock.code.denied.prefab", caller);
                            return false;
                        }
                        if (_tempLocked)
                            return false;
                        return true;
                    case VisChkCancelClick:
                        return true;
                    default:
                        return false;
                }
            }

            public void CheckShop()
            {
                if (vendor == null || !vendor.IsConnected || customer == null || !customer.IsConnected)
                {
                    plugin.InterruptTrade(this);
                    return;
                }
                if (_checkDist)
                {
                    if (Vector3.Distance(vendor.ServerPosition, _vendorLoc) > _cfg.MaxTradeSpotDistance ||
                        Vector3.Distance(customer.ServerPosition, _customerLoc) > _cfg.MaxTradeSpotDistance)
                        plugin.InterruptTrade(this);
                }
            }

            public void OnTransactionCompleted()
            {
                vendor.SignalBroadcast(BaseEntity.Signal.Gesture, "victory");
                customer.SignalBroadcast(BaseEntity.Signal.Gesture, "victory");
                if (!_cfg.AllowTradeLogs) return;
                var vendorItems = DescribeItems(shop.vendorInventory);
                var customerItems = DescribeItems(shop.customerInventory);
                var sb = new StringBuilder($"[{DateTime.Now:G}] Trade between: {vendor.displayName}({vendor.userID}) and {customer.displayName}({customer.userID}):{Environment.NewLine}");
                sb.AppendLine($"{shop.vendorPlayer.displayName}'s offer: {vendorItems}");
                sb.AppendLine($"{shop.customerPlayer.displayName}'s offer: {customerItems}");
                plugin.LogTrade(sb.ToString());
            }

            private static string DescribeItems(ItemContainer inv)
            {
                if (inv == null || inv.itemList == null || inv.itemList.Count == 0)
                    return "(Empty)";
                var sb = new StringBuilder();
                for (int i = 0; i < inv.itemList.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var item = inv.itemList[i];
                    sb.Append(GetItemName(item)).Append(" x").Append(item.amount);
                }
                return sb.ToString();
            }

            private void Awake()
            {
                shop = GetComponent<ShopFront>();
                shopId = shop.net.ID;
            }

            private void OnDestroy()
            {
                shop = null;
                vendor = null;
                customer = null;
            }
        }

        internal class PendingTrade
        {
            public readonly BasePlayer Vendor;
            public BasePlayer Customer;

            public PendingTrade(BasePlayer vendor) { Vendor = vendor; }

            public void CreateRequestTimer(float requestTimeout, TradePlugin plugin)
            {
                var captured = this;
                TradeMod.Instance?.Delay(() =>
                {
                    if (captured.Customer == null || !plugin.PendingTradeExists(captured.Customer))
                        return;
                    captured.Remove(plugin);
                    if (captured.Vendor != null && captured.Vendor.IsConnected)
                        plugin.Message(captured.Vendor, plugin._("Msg.TradeTimeoutVendor", captured.Vendor, SanitizeName(captured.Customer.displayName)));
                    if (captured.Customer != null && captured.Customer.IsConnected)
                        plugin.Message(captured.Customer, plugin._("Msg.TradeTimeoutCustomer", captured.Customer, SanitizeName(captured.Vendor.displayName)));
                }, requestTimeout);
            }

            public void Remove(TradePlugin plugin)
            {
                if (Vendor != null) plugin.RemovePending(GetUserId(Vendor));
                if (Customer != null) plugin.RemovePending(GetUserId(Customer));
            }

            public BasePlayer GetOppositeTrader(BasePlayer player)
            {
                if (Vendor != null && Vendor != player) return Vendor;
                if (Customer != null && Customer != player) return Customer;
                return null;
            }
        }

        internal void LogTrade(string text)
        {
            try
            {
                Directory.CreateDirectory(_dataDir);
                File.AppendAllText(Path.Combine(_dataDir, "Log.txt"), text + Environment.NewLine);
            }
            catch (Exception ex) { Debug.LogWarning("[Trade] Log: " + ex.Message); }
        }

        public class PermissionDefinition
        {
            [JsonProperty] public int Order;
            [JsonProperty] public string Name;
            [JsonIgnore] internal string PermName;
            [JsonProperty] public float Cooldown;
            [JsonProperty] public float MaxDist;
            [JsonProperty] public int TradingSlots;
            [JsonProperty("Banned Items")] public List<string> BannedItems;
        }

        public class Cfg
        {
            [JsonProperty("Disable accepting requests in Block zone")]
            public bool DisallowAcceptInBuildingBlock;
            [JsonProperty("Disable sending requests in Block zone")]
            public bool DisallowRequestInBuildingBlock;
            [JsonProperty("Disable trading in air")]
            public bool DisallowInAir;
            [JsonProperty("Disable trading in water")]
            public bool DisallowInWater;
            [JsonProperty("Disable trading while wounded")]
            public bool DisallowInWound;
            [JsonProperty("Disable trading in transport")]
            public bool DisallowInTransport;
            [JsonProperty("Enable individual trade slot count")]
            public bool EnableIndividualTradeSlots;
            [JsonProperty("Trade request timeout (seconds)")]
            public float RequestTimeout;
            [JsonProperty("Max distance from trade spot (0 - disabled)")]
            public float MaxTradeSpotDistance;
            [JsonProperty("AntiScam trade accept delay (0 - disabled)")]
            public float AntiScamDelay;
            [JsonProperty("Chat icon id")]
            public ulong ChatIconId;
            [JsonProperty("Allow trade logging")]
            public bool AllowTradeLogs;
            [JsonProperty("Permissions (first one is always default)")]
            public List<PermissionDefinition> Permissions;
            [JsonProperty("Config revision (do not edit)")]
            public int ConfigRev;
        }

        private string _(string key, BasePlayer player = null, params object[] args)
        {
            string message;
            if (!_lang.TryGetValue(key, out message) || message == null) message = key;
            return args != null && args.Length > 0 ? string.Format(message, args) : message;
        }

        private void LoadDefaultMessages()
        {
            void Add(string k, string v) { if (!_lang.ContainsKey(k)) _lang[k] = v; }
            Add("Msg.TradeIntro", "To begin trade, type <color=#81B67A>/trade <Partial name or Steam ID></color>.");
            Add("Msg.TradeRequestSent", "You've sent a trade request to <color=#81B67A>{0}</color>.");
            Add("Msg.TradeRequestReceived", "<color=#81B67A>{0}</color> wants to trade with you!\n<color=#81B67a>/trade yes</color> - Accept request.\n<color=#DA5757>/trade no</color> - Deny request.");
            Add("Msg.TradeSuccessful", "Your trade with <color=#81B67A>{0}</color> succeed.");
            Add("Msg.TradeTimeoutVendor", "<color=#81B67A>{0}</color> didn't anwered to your trade request.");
            Add("Msg.TradeTimeoutCustomer", "You haven't answered to <color=#81B67A>{0}</color>'s trade request.");
            Add("Msg.TradeCancelledVendor", "<color=#81B67A>{0}</color> has cancelled a trade request.");
            Add("Msg.TradeCancelledCustomer", "You have cancelled a trade request.");
            Add("Msg.TradeInterrupted", "Trade was interrupted!");
            Add("Msg.You", "<color=#81B67A>You</color>");
            Add("Msg.YourPartner", "<color=#81B67A>Your partner</color>");
            Add("Error.NoPerm", "{0} don't have permission to trade.");
            Add("Error.NoSuchPlayer", "No such player found or he is offline.");
            Add("Error.Ignored", "That player is ignoring you.");
            Add("Error.MultiplePlayers", "Found multiple players with this name!\nRefine your search please or use SteamID.");
            Add("Error.SelfTrade", "Obviously, you can't trade with yourself :)");
            Add("Error.NoPendingRequest", "You have no pending requests.");
            Add("Error.CantTradeInWater", "{0} can't trade while in water!");
            Add("Error.CantTradeInBuildingBlock", "{0} can't trade while in Building Block zone.");
            Add("Error.CantTradeInAir", "{0} can't trade while flying.");
            Add("Error.CantTradeWounded", "{0} can't trade while wounded.");
            Add("Error.CantTradeSleeping", "{0} can't trade while sleeping.");
            Add("Error.CantTradeInVehicle", "{0} can't trade in transport.");
            Add("Error.CantTradeDead", "{0} can't trade while dead.");
            Add("Error.CantTradeOffline", "{0} is offline.");
            Add("Error.CantTradeRightNow", "{0} can't trade right now.");
            Add("Error.TradeCooldown", "Trade is on cooldown. Please wait <color=#81B67A>{0:mm\\:ss}</color>.");
            Add("Error.TooFar", "{0} is too far away from you.");
            Add("Error.UnknownCommand", "Unrecognized command.\nType either <color=#81B67a>/trade yes</color> or <color=#DA5757>/trade no</color>");
        }

        private void LoadLangFile()
        {
            if (!File.Exists(_langPath)) return;
            try
            {
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(_langPath));
                if (loaded == null) return;
                foreach (var kv in loaded)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                        _lang[kv.Key] = kv.Value;
                }
            }
            catch { }
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                    _cfg = JsonConvert.DeserializeObject<Cfg>(File.ReadAllText(_configPath));
            }
            catch (Exception ex) { Debug.LogWarning("[Trade] Config: " + ex.Message); }
            if (_cfg == null)
            {
                _cfg = new Cfg
                {
                    DisallowAcceptInBuildingBlock = true,
                    DisallowRequestInBuildingBlock = true,
                    DisallowInAir = true,
                    DisallowInWater = true,
                    DisallowInWound = true,
                    RequestTimeout = 30f,
                    MaxTradeSpotDistance = 5f,
                    AllowTradeLogs = true,
                    Permissions = new List<PermissionDefinition>
                    {
                        new PermissionDefinition { Order = 1, Name = "default", Cooldown = 30f, TradingSlots = 6, BannedItems = new List<string> { "note" } },
                        new PermissionDefinition { Order = 2, Name = "vip_example", Cooldown = 10f, TradingSlots = 12 }
                    },
                    ConfigRev = CurrentConfigRevision
                };
            }
            _cfg.Permissions ??= new List<PermissionDefinition>
            {
                new PermissionDefinition { Order = 1, Name = "default", Cooldown = 30f, TradingSlots = 6 }
            };
        }
    }
}
