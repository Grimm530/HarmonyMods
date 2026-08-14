using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ServerQoL
{
    /// <summary>
    /// Combined logic from UnlockInventory, InfiniteBurn, ElectricGeneratorTweaker, InfiniteVendingStock.
    /// </summary>
    public sealed class ServerQoLService
    {
        public const string GeneratorPerm = "electricgeneratortweaker.tweak";
        private const int NpcVendingStock = 10000000;
        private const string AdminGroup = "admin";

        private readonly string _serverRoot;
        private readonly string _configPath;
        private readonly string _castleVendingPath;
        private Configuration _config;
        private readonly Dictionary<ulong, Dictionary<string, int>> _buyLimits = new Dictionary<ulong, Dictionary<string, int>>();
        private FieldInfo _vendNumberOfTransactionsField;

        public Configuration Config => _config;

        public ServerQoLService(string serverRoot)
        {
            _serverRoot = serverRoot;
            _configPath = Path.Combine(serverRoot, "HarmonyConfig", "ServerQoL.json");
            _castleVendingPath = Path.Combine(serverRoot, "HarmonyConfig", "CastleVendingSetup.json");
            _vendNumberOfTransactionsField = typeof(VendingMachine).GetField("vend_numberOfTransactions", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        #region Config

        public class Configuration
        {
            [JsonProperty("Unlock player inventories")]
            public bool UnlockInventories { get; set; } = true;

            [JsonProperty("Infinite candles and torches")]
            public bool InfiniteBurn { get; set; } = true;

            [JsonProperty("Electric Generator")]
            public GeneratorSettings Generator { get; set; } = new GeneratorSettings();

            [JsonProperty("NPC Vending")]
            public VendingSettings Vending { get; set; } = new VendingSettings();
        }

        public class GeneratorSettings
        {
            [JsonProperty("Setting for all World")]
            public bool ElectricGeneratorWorld { get; set; } = true;

            [JsonProperty("Enable Debug option to console output (default false)")]
            public bool EnableDebug { get; set; }

            [JsonProperty("Amount of electricity (100 by default)")]
            public float ElectricAmount { get; set; } = 100f;
        }

        public class VendingSettings
        {
            [JsonProperty("Restock NPC vending machines")]
            public bool RestockNpcVendors { get; set; } = true;

            [JsonProperty("Apply CastleVendingSetup buy limits")]
            public bool ApplyBuyLimits { get; set; } = true;
        }

        public void LoadConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(_configPath))
                {
                    _config = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(_configPath));
                }
                else
                {
                    _config = BuildFromOxideConfigs();
                    Debug.Log("[ServerQoL] Creating HarmonyConfig/ServerQoL.json from Oxide configs (or defaults).");
                }

                if (_config == null)
                    _config = new Configuration();
                if (_config.Generator == null)
                    _config.Generator = new GeneratorSettings();
                if (_config.Vending == null)
                    _config.Vending = new VendingSettings();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerQoL] FAIL: load config — using defaults. " + ex.Message);
                _config = new Configuration();
            }

            SaveConfig();
            TryCopyCastleVendingSetup();
        }

        private Configuration BuildFromOxideConfigs()
        {
            var cfg = new Configuration();
            try
            {
                string genPath = Path.Combine(_serverRoot, "oxide", "config", "ElectricGeneratorTweaker.json");
                if (File.Exists(genPath))
                {
                    JObject jo = JObject.Parse(File.ReadAllText(genPath));
                    cfg.Generator.ElectricGeneratorWorld = jo["Electric Generator"]?["Setting for all World"]?.Value<bool>() ?? true;
                    cfg.Generator.EnableDebug = jo["Electric Generator"]?["Enable Debug option to console output (default false)"]?.Value<bool>() ?? false;
                    cfg.Generator.ElectricAmount = jo["Electric Generator Attributes"]?["Amount of electricity (100 by default)"]?.Value<float>() ?? 100f;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerQoL] Oxide ElectricGeneratorTweaker.json: " + ex.Message);
            }
            return cfg;
        }

        public void SaveConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(_config ?? new Configuration(), Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerQoL] FAIL: save config: " + ex.Message);
            }
        }

        private void TryCopyCastleVendingSetup()
        {
            try
            {
                if (File.Exists(_castleVendingPath)) return;
                string oxide = Path.Combine(_serverRoot, "oxide", "config", "CastleVendingSetup.json");
                if (!File.Exists(oxide)) return;
                string dir = Path.GetDirectoryName(_castleVendingPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.Copy(oxide, _castleVendingPath, false);
                Debug.Log("[ServerQoL] Copied oxide/config/CastleVendingSetup.json -> HarmonyConfig/CastleVendingSetup.json");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerQoL] CastleVendingSetup copy: " + ex.Message);
            }
        }

        #endregion

        public void RegisterPermissions()
        {
            PermissionsBridge.RegisterPermission(GeneratorPerm);
            if (!PermissionsBridge.IsAvailable) return;
            if (!PermissionsBridge.GroupExists(AdminGroup))
                PermissionsBridge.CreateGroup(AdminGroup, "Administrators", 0);
            PermissionsBridge.GrantGroupPermission(AdminGroup, GeneratorPerm);
        }

        public void ApplyExistingWorld()
        {
            if (_config.UnlockInventories)
            {
                foreach (BasePlayer player in BasePlayer.allPlayerList)
                    UnlockInventory(player);
            }

            foreach (BaseNetworkable entity in BaseNetworkable.serverEntities)
            {
                if (entity == null || entity.IsDestroyed) continue;
                if (_config.InfiniteBurn)
                {
                    if (entity is Candle candle)
                        ApplyCandle(candle);
                    else if (entity is TorchWeapon torch)
                        ApplyTorch(torch);
                }
                if (entity is ElectricGenerator generator)
                    ApplyGenerator(generator);
                if (_config.Vending.RestockNpcVendors && entity is NPCVendingMachine npcVendor)
                    RestockItems(npcVendor);
            }

            if (_config.Vending.ApplyBuyLimits)
                LoadBuyLimitsFromCastleConfig();
        }

        public void OnPlayerInit(BasePlayer player)
        {
            if (_config.UnlockInventories)
                UnlockInventory(player);
        }

        public void OnEntitySpawned(BaseNetworkable entity)
        {
            if (entity == null || entity.IsDestroyed) return;

            if (_config.InfiniteBurn)
            {
                if (entity is Candle || entity is TorchWeapon)
                {
                    BaseNetworkable captured = entity;
                    InvokeNext(captured, 1f, () =>
                    {
                        if (captured == null || captured.IsDestroyed) return;
                        if (captured is Candle c)
                            ApplyCandle(c);
                        else if (captured is TorchWeapon t)
                            ApplyTorch(t);
                    });
                }
            }

            if (entity is ElectricGenerator generator)
                ApplyGenerator(generator);

            if (_config.Vending.RestockNpcVendors && entity is NPCVendingMachine npcVendor)
                RestockItems(npcVendor);
        }

        public void OnVendingTransaction(NPCVendingMachine vendingMachine)
        {
            if (!_config.Vending.RestockNpcVendors || vendingMachine == null) return;
            NPCVendingMachine captured = vendingMachine;
            InvokeNext(captured, 0f, () =>
            {
                if (captured != null && !captured.IsDestroyed)
                    RestockItems(captured);
            });
        }

        /// <summary>
        /// Called after <see cref="VendingMachine.BuyItem"/> has already converted the visible
        /// sell-order index and called SetPendingOrder. <paramref name="sellOrderIndex"/> is the actual index.
        /// </summary>
        public void OnBuyVendingItem(VendingMachine vendingMachine, BasePlayer player, int sellOrderIndex, int numberOfTransactions)
        {
            if (!_config.Vending.ApplyBuyLimits) return;
            if (vendingMachine == null || player == null) return;
            if (!(vendingMachine is NPCVendingMachine npc)) return;
            if (npc.net == null) return;
            if (numberOfTransactions <= 0) return;

            if (!_buyLimits.TryGetValue(npc.net.ID.Value, out Dictionary<string, int> limits))
                return;

            int actualSellOrderIndex = sellOrderIndex;
            if (actualSellOrderIndex < 0 || actualSellOrderIndex >= vendingMachine.sellOrders.sellOrders.Count)
                return;

            ProtoBuf.VendingMachine.SellOrder targetSellOrder = vendingMachine.sellOrders.sellOrders[actualSellOrderIndex];
            ItemDefinition itemDefinition = ItemManager.FindItemDefinition(targetSellOrder.itemToSellID);
            if (itemDefinition == null) return;
            if (!limits.TryGetValue(itemDefinition.shortname, out int maxBuyAmount)) return;
            if (numberOfTransactions <= maxBuyAmount) return;

            player.ChatMessage($"Buy amount limited to {maxBuyAmount} (configured limit). Requested: {numberOfTransactions}");

            VendingMachine captured = vendingMachine;
            int cap = maxBuyAmount;
            InvokeNext(captured, 0.01f, () =>
            {
                if (captured == null || captured.IsDestroyed) return;
                if (_vendNumberOfTransactionsField == null) return;
                object current = _vendNumberOfTransactionsField.GetValue(captured);
                if (current is int currentValue && currentValue > cap)
                    _vendNumberOfTransactionsField.SetValue(captured, cap);
            });
        }

        private static void UnlockInventory(BasePlayer player)
        {
            if (player?.inventory == null) return;
            player.inventory.containerMain?.SetLocked(false);
            player.inventory.containerBelt?.SetLocked(false);
            player.inventory.containerWear?.SetLocked(false);
        }

        private static void ApplyCandle(Candle entity)
        {
            if (entity == null || entity.IsDestroyed) return;
            entity.lifeTimeSeconds = float.MaxValue;
            entity.Heal(entity.MaxHealth());
        }

        private static void ApplyTorch(TorchWeapon entity)
        {
            if (entity == null || entity.IsDestroyed) return;
            entity.SetIsOn(true);
            entity.CancelInvoke(entity.UseFuel);
        }

        private void ApplyGenerator(ElectricGenerator generator)
        {
            if (generator == null || generator.IsDestroyed) return;
            if (generator.OwnerID == 0) return;

            bool world = _config.Generator.ElectricGeneratorWorld;
            bool ownerHas = PermissionsBridge.UserHasPermission(generator.OwnerID.ToString(), GeneratorPerm);
            if (!world && !ownerHas) return;

            generator.electricAmount = _config.Generator.ElectricAmount;
            if (_config.Generator.EnableDebug)
                Debug.Log($"[ServerQoL] electricAmount {generator.electricAmount}");
        }

        private static void RestockItems(NPCVendingMachine vendingMachine)
        {
            if (vendingMachine?.inventory?.itemList == null) return;
            List<Item> items = vendingMachine.inventory.itemList;
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item == null || item.amount == NpcVendingStock) continue;
                item.amount = NpcVendingStock;
                item.MarkDirty();
            }
        }

        private void LoadBuyLimitsFromCastleConfig()
        {
            _buyLimits.Clear();
            try
            {
                if (!File.Exists(_castleVendingPath))
                {
                    Debug.LogWarning("[ServerQoL] CastleVendingSetup.json not found. Buy limits will not be applied.");
                    return;
                }

                JObject config = JObject.Parse(File.ReadAllText(_castleVendingPath));
                bool useMedieval = config["Use Medieval profile"]?.Value<bool>() ?? false;
                string profileKey = useMedieval ? "Medieval profile" : "Normal profile";
                if (config[profileKey] == null)
                {
                    Debug.LogWarning($"[ServerQoL] Profile '{profileKey}' not found in CastleVendingSetup.json");
                    return;
                }

                JObject blockerItems = config[profileKey]["BlockerID And Vending Items"] as JObject;
                if (blockerItems == null) return;

                foreach (JProperty blockerEntry in blockerItems.Properties())
                {
                    if (!int.TryParse(blockerEntry.Name, out int branchAmount)) continue;
                    JArray items = blockerEntry.Value["items"] as JArray;
                    if (items == null) continue;

                    NPCVendingMachine vendingMachine = FindVendingMachineByBranchAmount(branchAmount);
                    if (vendingMachine?.net == null) continue;

                    var limits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < items.Count; i++)
                    {
                        string sellId = items[i]["sellId"]?.Value<string>();
                        int refillAmount = items[i]["refillAmount"]?.Value<int>() ?? 0;
                        if (!string.IsNullOrEmpty(sellId) && refillAmount > 0)
                            limits[sellId] = refillAmount;
                    }

                    if (limits.Count > 0)
                    {
                        _buyLimits[vendingMachine.net.ID.Value] = limits;
                        Debug.Log($"[ServerQoL] Loaded buy limits for vending {vendingMachine.net.ID.Value} (Branch {branchAmount}): {limits.Count} items");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerQoL] CastleVendingSetup load: " + ex.Message);
            }
        }

        private static NPCVendingMachine FindVendingMachineByBranchAmount(int branchAmount)
        {
            foreach (BaseNetworkable entity in BaseNetworkable.serverEntities)
            {
                ElectricalBranch branch = entity?.GetComponent<ElectricalBranch>();
                if (branch == null || branch.OwnerID != 0 || branch.branchAmount != branchAmount)
                    continue;
                if (entity is NPCVendingMachine vending)
                    return vending;
            }
            return null;
        }

        private static void InvokeNext(BaseNetworkable entity, float delay, Action action)
        {
            if (entity == null || action == null) return;
            if (delay <= 0f)
                entity.Invoke(action, 0.01f);
            else
                entity.Invoke(action, delay);
        }
    }
}
