using System;
using System.Collections.Generic;
using UnityEngine;

namespace FurnaceSplitter
{
    internal static class FurnaceSplitterLogic
    {
        public static void AutoAddFuel(PlayerInventory playerInventory, BaseOven oven)
        {
            bool debug = FurnaceSplitterConfig.Config?.debug == true;

            if (playerInventory == null || oven?.inventory == null || oven.fuelType == null)
            {
                if (debug) FurnaceSplitterConfig.Log($"AutoAddFuel: skip - null inventory/fuelType");
                return;
            }

            var cfg = FurnaceSplitterConfig.Config.GetOvenConfig(oven.ShortPrefabName);
            if (cfg == null || !cfg.autoFuelTransfer)
            {
                if (debug) FurnaceSplitterConfig.Log($"AutoAddFuel: skip - cfg=null or autoFuelTransfer=false (cfg={(cfg != null).ToString()})");
                return;
            }

            var info = FurnaceSplitterMod.GetOvenInfo(oven);

            int neededFuel = (int)Math.Ceiling(info.FuelNeeded);
            // Explicit 3-arg call — ItemContainer.GetAmount(int,bool,bool) after Facepunch API change
            int fuelInOven = oven.inventory.GetAmount(oven.fuelType.itemid, false, false);
            neededFuel -= fuelInOven;

            var playerFuel = new List<Item>();
            playerInventory.FindItemsByItemID(playerFuel, oven.fuelType.itemid);

            if (debug)
                FurnaceSplitterConfig.Log($"AutoAddFuel: oven={oven.ShortPrefabName} FuelNeeded={info.FuelNeeded:F0} ETA={info.ETA:F0}s fuelInOven={fuelInOven} needed={neededFuel} playerHas={playerFuel.Count} stacks");

            if (neededFuel <= 0 || playerFuel.Count == 0)
            {
                if (debug) FurnaceSplitterConfig.Log($"AutoAddFuel: skip - neededFuel={neededFuel} playerFuel stacks={playerFuel.Count}");
                return;
            }

            int fuelSlotIndex = 0;
            int capacity = oven.fuelType.stackable * oven.fuelSlots;
            int totalTransferred = 0;

            foreach (Item fuelItem in playerFuel)
            {
                if (neededFuel <= 0) break;

                var existingFuel = oven.inventory.GetSlot(fuelSlotIndex);
                if (existingFuel != null && existingFuel.amount >= existingFuel.info.stackable)
                {
                    if (fuelSlotIndex < oven.fuelSlots - 1)
                        fuelSlotIndex++;
                    else
                        break;
                }

                int currentTotal = oven.inventory.GetAmount(oven.fuelType.itemid, false, false);
                if (currentTotal >= capacity)
                    break;

                int toTake = Mathf.Min(neededFuel, capacity - currentTotal, fuelItem.amount);
                if (toTake <= 0) continue;

                neededFuel -= toTake;
                totalTransferred += toTake;

                if (toTake >= fuelItem.amount)
                    fuelItem.MoveToContainer(oven.inventory, fuelSlotIndex);
                else
                {
                    Item splitItem = fuelItem.SplitItem(toTake);
                    if (splitItem != null && !splitItem.MoveToContainer(oven.inventory, fuelSlotIndex))
                        break;
                }

                if (neededFuel <= 0)
                    break;
            }

            if (debug && totalTransferred > 0)
                FurnaceSplitterConfig.Log($"AutoAddFuel: transferred {totalTransferred} {oven.fuelType.shortname} to oven");
        }
    }
}
