/*
 * FurnaceSplitter Harmony Mod - Standalone
 * Furnace item splitting + auto fuel from inventory.
 * Patches: Item.MoveToContainer, ItemContainer.Insert
 * Config: HarmonyConfig/FurnaceSplitter.json
 */

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FurnaceSplitter
{
    public class FurnaceSplitterMod : IHarmonyModHooks
    {
        public static FurnaceSplitterMod Instance { get; private set; }

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            FurnaceSplitterConfig.Load();
            UnityEngine.Debug.Log("[FurnaceSplitter] Harmony mod loaded - split + auto fuel active. Config: HarmonyConfig/FurnaceSplitter.json");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            Instance = null;
            UnityEngine.Debug.Log("[FurnaceSplitter] Harmony mod unloaded.");
        }

        /// <summary>Result of a furnace split attempt.</summary>
        public enum MoveResult
        {
            Ok,
            SlotsFilled,
            NotEnoughSlots
        }

        /// <summary>ETA and fuel info for an oven.</summary>
        public class OvenInfo
        {
            public float ETA;
            public float FuelNeeded;
        }

        /// <summary>Get oven temperature for compatibility/fuel calc. When off, GetTemperature returns 15 - use design temp from oven.temperature instead.</summary>
        public static float GetOvenDesignTemperature(BaseOven oven)
        {
            float t = oven.GetTemperature(0);
            if (t > 15f) return t;
            switch (oven.temperature)
            {
                case BaseOven.TemperatureType.Fractioning: return 1500f;
                case BaseOven.TemperatureType.Cooking: return 200f;
                case BaseOven.TemperatureType.Smelting: return 1000f;
                case BaseOven.TemperatureType.Warming: return 50f;
                default: return 15f;
            }
        }

        /// <summary>Get ETA and fuel needed for an oven. Returns exact amount needed (no multiplier).</summary>
        public static OvenInfo GetOvenInfo(BaseOven oven)
        {
            var result = new OvenInfo();
            if (oven == null || oven.IsDestroyed) return result;

            float eta = GetTotalSmeltTime(oven) / oven.smeltSpeed;
            var burnable = oven.fuelType.GetComponent<ItemModBurnable>();
            float fuelUnits = burnable != null ? burnable.fuelAmount : 1800f;
            float ovenTemp = GetOvenDesignTemperature(oven);
            float neededFuel = (float)Math.Ceiling(eta * (ovenTemp / 200f) / fuelUnits);

            result.FuelNeeded = neededFuel;
            result.ETA = eta;
            return result;
        }

        /// <summary>Split item across oven slots. Returns MoveResult. Oxide plugin blocks default move when Ok or SlotsFilled.</summary>
        public static MoveResult TryFurnaceSplit(Item item, BaseOven oven, int totalSlots, int splitAmount)
        {
            bool debug = FurnaceSplitterConfig.Config?.debug == true;

            if (item == null || oven == null || oven.inventory == null || totalSlots <= 0)
            {
                if (debug) FurnaceSplitterConfig.Log($"TryFurnaceSplit: NotEnoughSlots (null or totalSlots={totalSlots})");
                return MoveResult.NotEnoughSlots;
            }

            var container = oven.inventory;
            int itemAmount = item.amount > splitAmount ? splitAmount : item.amount;
            int existingTotal = 0;
            int count = 0;
            for (int i = 0; i < container.itemList.Count && count < totalSlots; i++)
            {
                var slotItem = container.itemList[i];
                if (slotItem != null && slotItem.info == item.info)
                {
                    existingTotal += slotItem.amount;
                    count++;
                }
            }
            int totalAmount = Math.Min(itemAmount + existingTotal, Math.Abs(item.info.stackable * totalSlots));

            int totalStackSize = Math.Min(totalAmount / totalSlots, item.info.stackable);
            int remaining = totalAmount - (totalAmount / totalSlots) * totalSlots;

            if (debug)
                FurnaceSplitterConfig.Log($"TryFurnaceSplit: itemAmount={itemAmount} existingTotal={existingTotal} totalAmount={totalAmount} totalSlots={totalSlots} totalStackSize={totalStackSize} remaining={remaining} matchingSlots={count}");

            var addedSlots = new List<int>();
            var ovenSlots = new List<OvenSlot>();

            for (int i = 0; i < totalSlots; i++)
            {
                if (!FindMatchingSlotIndex(oven, container, item.info, addedSlots, out Item existingItem, out int slot))
                {
                    if (debug) FurnaceSplitterConfig.Log($"TryFurnaceSplit: FindMatchingSlotIndex failed for slot i={i} - NotEnoughSlots (mixed ore types?)");
                    return MoveResult.NotEnoughSlots;
                }

                addedSlots.Add(slot);

                int currentAmount = existingItem?.amount ?? 0;
                int missingAmount = totalStackSize - currentAmount + (i < remaining ? 1 : 0);

                if (currentAmount + missingAmount <= 0) continue;

                ovenSlots.Add(new OvenSlot
                {
                    Position = existingItem?.position,
                    Index = slot,
                    Item = existingItem,
                    DeltaAmount = missingAmount
                });
            }

            int totalMoved = 0;
            foreach (var os in ovenSlots)
            {
                if (os.Item == null)
                {
                    var newItem = ItemManager.Create(item.info, os.DeltaAmount, item.skin);
                    newItem?.MoveToContainer(container, os.Position ?? os.Index);
                }
                else
                {
                    os.Item.amount += os.DeltaAmount;
                }
                totalMoved += os.DeltaAmount;
            }

            container.MarkDirty();

            if (totalMoved >= item.amount)
            {
                item.Remove();
                item.GetRootContainer()?.MarkDirty();
                if (debug) FurnaceSplitterConfig.Log($"TryFurnaceSplit: Ok - moved {totalMoved} across {ovenSlots.Count} slots");
                return MoveResult.Ok;
            }
            else
            {
                item.amount -= totalMoved;
                item.GetRootContainer()?.MarkDirty();
                if (debug) FurnaceSplitterConfig.Log($"TryFurnaceSplit: SlotsFilled - moved {totalMoved}, {item.amount} left in hand");
                return MoveResult.SlotsFilled;
            }
        }

        private static float GetTotalSmeltTime(BaseOven oven)
        {
            float eta = 0f;
            int inputMin = oven.fuelSlots; // _inputSlotIndex equals fuelSlots per BaseOven.PreInitShared
            int inputMax = oven.fuelSlots + oven.inputSlots;

            for (int i = inputMin; i < inputMax; i++)
            {
                var inputItem = oven.inventory.GetSlot(i);
                if (inputItem == null) continue;

                var cookable = inputItem.info.GetComponent<ItemModCookable>();
                if (cookable == null) continue;

                eta += cookable.cookTime * inputItem.amount;
            }
            return eta;
        }

        private static bool FindMatchingSlotIndex(BaseOven oven, ItemContainer container, ItemDefinition itemType,
            List<int> indexBlacklist, out Item existingItem, out int slotIndex)
        {
            existingItem = null;
            slotIndex = -1;
            int firstIndex = -1;
            int inputMin = oven.fuelSlots; // _inputSlotIndex equals fuelSlots per BaseOven.PreInitShared
            int inputMax = oven.fuelSlots + oven.inputSlots;
            var existingItems = new Dictionary<int, Item>();

            for (int i = inputMin; i < inputMax; i++)
            {
                if (indexBlacklist.Contains(i)) continue;

                var itemSlot = container.GetSlot(i);
                if (itemSlot == null || (itemType != null && itemSlot.info == itemType))
                {
                    if (itemSlot != null)
                        existingItems[i] = itemSlot;

                    if (firstIndex == -1)
                    {
                        existingItem = itemSlot;
                        firstIndex = i;
                    }
                }
            }

            if (existingItems.Count == 0 && firstIndex != -1)
            {
                existingItem = container.GetSlot(firstIndex);
                slotIndex = firstIndex;
                return true;
            }
            if (existingItems.Count > 0)
            {
                int maxAmount = -1;
                foreach (var kv in existingItems)
                {
                    if (kv.Value != null && kv.Value.amount > maxAmount)
                    {
                        maxAmount = kv.Value.amount;
                        existingItem = kv.Value;
                        slotIndex = kv.Value.position;
                    }
                }
                return true;
            }
            return false;
        }

        private struct OvenSlot
        {
            public int? Position;
            public int Index;
            public Item Item;
            public int DeltaAmount;
        }
    }
}
