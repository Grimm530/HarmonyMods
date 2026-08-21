using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace InfiniteVendingStock
{
    /// <summary>
    /// Oxide InfiniteVendingStock 1.0.2 logic: set every NPC vendor inventory stack to a huge amount
    /// so buy sliders are not capped by remaining stock.
    /// </summary>
    public sealed class InfiniteVendingStockService
    {
        public const int DefaultStockAmount = 10000000;

        private readonly string _configPath;
        private Configuration _config;

        public Configuration Config => _config;

        public InfiniteVendingStockService(string serverRoot)
        {
            _configPath = Path.Combine(serverRoot, "HarmonyConfig", "InfiniteVendingStock.json");
        }

        public class Configuration
        {
            [JsonProperty("Enabled")]
            public bool Enabled { get; set; } = true;

            [JsonProperty("NPC vending stock amount")]
            public int StockAmount { get; set; } = DefaultStockAmount;
        }

        public void LoadConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(_configPath))
                    _config = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(_configPath));
                else
                    Debug.Log("[InfiniteVendingStock] Creating HarmonyConfig/InfiniteVendingStock.json");

                if (_config == null)
                    _config = new Configuration();
                if (_config.StockAmount <= 0)
                    _config.StockAmount = DefaultStockAmount;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[InfiniteVendingStock] FAIL: load config — using defaults. " + ex.Message);
                _config = new Configuration();
            }

            SaveConfig();
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
                Debug.LogWarning("[InfiniteVendingStock] FAIL: save config: " + ex.Message);
            }
        }

        public void RestockAllNpcVendors()
        {
            if (_config == null || !_config.Enabled) return;
            if (BaseNetworkable.serverEntities == null) return;

            int count = 0;
            foreach (BaseNetworkable entity in BaseNetworkable.serverEntities)
            {
                NPCVendingMachine vendor = entity as NPCVendingMachine;
                if (vendor == null || vendor.IsDestroyed) continue;
                RestockItems(vendor);
                count++;
            }

            Debug.Log("[InfiniteVendingStock] Restocked " + count + " NPC vending machines.");
        }

        public void OnVendingOrdersInstalled(NPCVendingMachine vendingMachine)
        {
            if (_config == null || !_config.Enabled) return;
            RestockItems(vendingMachine);
        }

        public void OnVendingTransaction(NPCVendingMachine vendingMachine)
        {
            if (_config == null || !_config.Enabled || vendingMachine == null) return;

            InfiniteVendingStockRunner runner = InfiniteVendingStockMod.Instance?.Runner;
            NPCVendingMachine captured = vendingMachine;
            if (runner != null)
            {
                runner.NextTick(() =>
                {
                    if (captured != null && !captured.IsDestroyed)
                        RestockItems(captured);
                });
                return;
            }

            RestockItems(vendingMachine);
        }

        public void RestockItems(NPCVendingMachine vendingMachine)
        {
            if (vendingMachine == null || vendingMachine.IsDestroyed) return;
            ItemContainer inventory = vendingMachine.inventory;
            if (inventory?.itemList == null) return;

            int stock = _config != null && _config.StockAmount > 0 ? _config.StockAmount : DefaultStockAmount;
            List<Item> items = inventory.itemList;
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item == null || item.amount == stock) continue;
                item.amount = stock;
                item.MarkDirty();
            }
        }
    }
}
