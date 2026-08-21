using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace IndustrialTransferSpeed
{
    public class PlanterIndustrialHarvester : MonoBehaviour
    {
        // GrowableEntity.harvests is private on current Rust assemblies.
        private static readonly FieldInfo HarvestsField = AccessTools.Field(typeof(GrowableEntity), "harvests");

        private PlanterBox _planter;
        private string _harvestMode;
        private PlantProperties.State _harvestStage;
        private PlantProperties.State _cloneStage;
        private int _harvestStageThresholdPercent;
        private int _cloneStageThresholdPercent;

        public void Init(PlanterBox planter)
        {
            _planter = planter;
            CancelInvoke(nameof(HarvestReadyPlants));

            IndustrialTransferSpeedConfig config = IndustrialTransferSpeedConfig.Config;
            _harvestMode = config.PlanterAutoHarvestMode;
            _harvestStage = ParseStage(config.PlanterAutoHarvestStage, PlantProperties.State.Ripe);
            _cloneStage = ParseStage(config.PlanterAutoCloneStage, PlantProperties.State.Sapling);
            _harvestStageThresholdPercent = config.PlanterAutoHarvestStageThresholdPercent;
            _cloneStageThresholdPercent = config.PlanterAutoCloneStageThresholdPercent;
            if (config.PlanterAutoHarvestEnabled)
            {
                InvokeRepeating(nameof(HarvestReadyPlants), config.PlanterAutoHarvestIntervalSeconds, config.PlanterAutoHarvestIntervalSeconds);
            }
        }

        private void HarvestReadyPlants()
        {
            if (_planter == null || _planter.IsDestroyed || _planter.inventory == null)
            {
                CancelInvoke(nameof(HarvestReadyPlants));
                return;
            }

            _harvestMode = PlanterProductionSettings.GetMode(_planter);
            PullFertilizerFromConnectedStorage();

            if (_planter.children == null || _planter.children.Count == 0)
            {
                return;
            }

            if (!HasConnectedOutput())
            {
                return;
            }

            List<GrowableEntity> readyPlants = new List<GrowableEntity>();
            foreach (BaseEntity child in _planter.children)
            {
                if (child is GrowableEntity growable && CanHarvest(growable))
                {
                    readyPlants.Add(growable);
                }
            }

            foreach (GrowableEntity growable in readyPlants)
            {
                TryHarvestToPlanter(growable);
            }
        }

        private void PullFertilizerFromConnectedStorage()
        {
            IndustrialStorageAdaptor adaptor = GetManagedAdaptor();
            if (adaptor == null || adaptor.inputs == null)
            {
                return;
            }

            foreach (IOEntity.IOSlot input in adaptor.inputs)
            {
                if (input == null || input.type != IOEntity.IOType.Industrial)
                {
                    continue;
                }

                IOEntity source = input.connectedTo.Get(true);
                if (source is IndustrialStorageAdaptor sourceAdaptor)
                {
                    MoveFertilizerFrom(sourceAdaptor.Container);
                }
            }
        }

        private IndustrialStorageAdaptor GetManagedAdaptor()
        {
            if (_planter.children == null)
            {
                return null;
            }

            foreach (BaseEntity child in _planter.children)
            {
                if (child is IndustrialStorageAdaptor adaptor && ComposterStorageAdaptor.IsManagedAdaptor(adaptor))
                {
                    return adaptor;
                }
            }

            return null;
        }

        private bool HasConnectedOutput()
        {
            IndustrialStorageAdaptor adaptor = GetManagedAdaptor();
            if (adaptor?.outputs == null)
            {
                return false;
            }

            foreach (IOEntity.IOSlot output in adaptor.outputs)
            {
                if (output != null && output.type == IOEntity.IOType.Industrial && output.connectedTo.Get(true) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void MoveFertilizerFrom(ItemContainer sourceContainer)
        {
            if (sourceContainer == null || sourceContainer.IsEmpty() || _planter.inventory.IsFull())
            {
                return;
            }

            int remaining = IndustrialTransferSpeedConfig.Config.MaxStackSizePerMove;
            List<Item> fertilizerItems = new List<Item>();
            foreach (Item item in sourceContainer.itemList)
            {
                if (item != null && item.info != null && item.info.shortname == "fertilizer")
                {
                    fertilizerItems.Add(item);
                }
            }

            foreach (Item fertilizer in fertilizerItems)
            {
                if (remaining <= 0 || fertilizer == null || fertilizer.IsRemoved())
                {
                    break;
                }

                int amountToMove = Mathf.Min(remaining, fertilizer.amount);
                Item movingItem = fertilizer;
                if (fertilizer.amount > amountToMove)
                {
                    movingItem = fertilizer.SplitItem(amountToMove);
                }

                if (movingItem == null)
                {
                    continue;
                }

                if (movingItem.MoveToContainer(_planter.inventory, -1, allowStack: true, ignoreStackLimit: false, null, allowSwap: false))
                {
                    remaining -= amountToMove;
                }
                else
                {
                    if (movingItem != fertilizer)
                    {
                        fertilizer.amount += movingItem.amount;
                        fertilizer.MarkDirty();
                        movingItem.Remove();
                    }
                    break;
                }
            }
        }

        private bool CanHarvest(GrowableEntity growable)
        {
            // currentStage is private; CanPick exposes the same resources > 0 check.
            if (growable == null || growable.IsDestroyed || growable.Properties == null || !growable.CanPick(null))
            {
                return false;
            }

            bool cloneMode = ShouldHarvestClone(growable);
            if (cloneMode)
            {
                return IsAtConfiguredStage(growable, _cloneStage, _cloneStageThresholdPercent)
                    && growable.Properties.CloneItem != null
                    && GetCloneCount(growable) > 0;
            }

            if (ShouldHarvestSeed(growable))
            {
                return IsAtConfiguredStage(growable, _harvestStage, _harvestStageThresholdPercent)
                    && growable.Properties.SeedItem != null
                    && growable.CurrentPickAmount > 0;
            }

            return ShouldHarvestFruit(growable)
                && IsAtConfiguredStage(growable, _harvestStage, _harvestStageThresholdPercent)
                && growable.Properties.pickupItem != null
                && growable.CurrentPickAmount > 0;
        }

        private bool TryHarvestToPlanter(GrowableEntity growable)
        {
            bool harvestClone = ShouldHarvestClone(growable);
            if (harvestClone)
            {
                return TryCopyClonesToPlanter(growable);
            }

            List<Item> harvestItems = ShouldHarvestSeed(growable)
                ? CreateSeedItems(growable)
                : CreateFruitItems(growable);
            if (harvestItems.Count == 0)
            {
                return false;
            }

            if (!MoveHarvestItemsToPlanter(harvestItems))
            {
                foreach (Item item in harvestItems)
                {
                    item?.Remove();
                }
                return false;
            }

            CompleteFruitHarvest(growable);
            return true;
        }

        private bool TryCopyClonesToPlanter(GrowableEntity growable)
        {
            List<Item> cloneItems = CreateCloneItems(growable);
            if (cloneItems.Count == 0)
            {
                return false;
            }

            if (!MoveHarvestItemsToPlanter(cloneItems))
            {
                foreach (Item item in cloneItems)
                {
                    item?.Remove();
                }
                return false;
            }

            return true;
        }

        private bool ShouldHarvestClone(GrowableEntity growable)
        {
            if (string.Equals(_harvestMode, "Clone", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(_harvestMode, "Fruit", System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(_harvestMode, "Seed", System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return false;
        }

        private bool ShouldHarvestSeed(GrowableEntity growable)
        {
            return string.Equals(_harvestMode, "Seed", System.StringComparison.OrdinalIgnoreCase);
        }

        private bool ShouldHarvestFruit(GrowableEntity growable)
        {
            if (string.Equals(_harvestMode, "Clone", System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(_harvestMode, "Seed", System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(_harvestMode, "Fruit", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return growable.IsFood();
        }

        private static bool IsAtConfiguredStage(GrowableEntity growable, PlantProperties.State stage, int thresholdPercent)
        {
            if (growable.State < stage || growable.State == PlantProperties.State.Dying)
            {
                return false;
            }

            if (growable.State == stage && thresholdPercent > 0)
            {
                int progress = Mathf.FloorToInt(growable.StageProgressFraction * 100f);
                return progress >= Mathf.Clamp(thresholdPercent, 0, 100);
            }

            return true;
        }

        private static PlantProperties.State ParseStage(string stage, PlantProperties.State fallback)
        {
            if (string.Equals(stage, "Sapling", System.StringComparison.OrdinalIgnoreCase))
            {
                return PlantProperties.State.Sapling;
            }

            if (string.Equals(stage, "Mature", System.StringComparison.OrdinalIgnoreCase))
            {
                return PlantProperties.State.Mature;
            }

            if (string.Equals(stage, "Fruiting", System.StringComparison.OrdinalIgnoreCase))
            {
                return PlantProperties.State.Fruiting;
            }

            if (string.Equals(stage, "Ripe", System.StringComparison.OrdinalIgnoreCase))
            {
                return PlantProperties.State.Ripe;
            }

            return fallback;
        }

        private static List<Item> CreateFruitItems(GrowableEntity growable)
        {
            List<Item> items = new List<Item>();
            int amount = growable.CurrentPickAmount;
            bool hasCondition = growable.Properties.pickupItem.condition.enabled;

            if (hasCondition)
            {
                for (int i = 0; i < amount; i++)
                {
                    Item item = CreateHarvestItem(growable, 1, applyCondition: true);
                    if (item != null)
                    {
                        items.Add(item);
                    }
                }
            }
            else
            {
                Item item = CreateHarvestItem(growable, amount, applyCondition: false);
                if (item != null)
                {
                    items.Add(item);
                }
            }

            return items;
        }

        private static List<Item> CreateSeedItems(GrowableEntity growable)
        {
            List<Item> items = new List<Item>();
            int amount = growable.CurrentPickAmount;
            if (amount <= 0 || growable.Properties.SeedItem == null)
            {
                return items;
            }

            Item item = ItemManager.Create(growable.Properties.SeedItem, amount, 0uL);
            if (item != null)
            {
                items.Add(item);
            }

            return items;
        }

        private static List<Item> CreateCloneItems(GrowableEntity growable)
        {
            List<Item> items = new List<Item>();
            int amount = GetCloneCount(growable);
            if (amount <= 0)
            {
                return items;
            }

            Item item = ItemManager.Create(growable.Properties.CloneItem, amount, 0uL);
            if (item != null)
            {
                GrowableGeneEncoding.EncodeGenesToItem(growable, item);
                items.Add(item);
            }

            return items;
        }

        private static int GetCloneCount(GrowableEntity growable)
        {
            return growable.Properties.BaseCloneCount + growable.Genes.GetGeneTypeCount(GrowableGenetics.GeneType.Yield) / 2;
        }

        private static Item CreateHarvestItem(GrowableEntity growable, int amount, bool applyCondition)
        {
            Item item = ItemManager.Create(growable.Properties.pickupItem, amount, 0uL);
            if (item != null && applyCondition)
            {
                item.conditionNormalized = growable.Properties.fruitVisualScaleCurve.Evaluate(growable.StageProgressFraction);
            }
            return item;
        }

        private bool MoveHarvestItemsToPlanter(List<Item> items)
        {
            ItemContainer container = _planter.inventory;
            var originalCanAcceptItem = container.canAcceptItem;
            ItemDefinition[] originalOnlyAllowedItems = container.onlyAllowedItems;

            try
            {
                container.canAcceptItem = null;
                container.onlyAllowedItems = null;

                foreach (Item item in items)
                {
                    if (item == null || !item.MoveToContainer(container, -1, allowStack: true, ignoreStackLimit: false, null, allowSwap: false))
                    {
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                container.canAcceptItem = originalCanAcceptItem;
                container.onlyAllowedItems = originalOnlyAllowedItems;
            }
        }

        private void CompleteFruitHarvest(GrowableEntity growable)
        {
            int harvests = 0;
            if (HarvestsField != null)
            {
                harvests = (int)HarvestsField.GetValue(growable) + 1;
                HarvestsField.SetValue(growable, harvests);
            }
            else
            {
                harvests = growable.Properties.maxHarvests;
            }

            growable.ResetSeason();

            if (harvests >= growable.Properties.maxHarvests)
            {
                if (growable.Properties.disappearAfterHarvest)
                {
                    _planter.OnPlantRemoved(growable, null);
                    growable.Kill();
                }
                else
                {
                    growable.ChangeState(PlantProperties.State.Dying, resetAge: true);
                }
            }
            else
            {
                growable.ChangeState(PlantProperties.State.Mature, resetAge: true);
            }
        }

    }
}
