namespace Oxide.Plugins
{
    public partial class WaterBases
    {
        public static WaterBases GetModInstance() => Instance;

        public void HarmonyOnServerInitialized() => OnServerInitialized();

        public void HarmonyUnload() => Unload();

        public object HarmonyOnItemSkinChange(int inventoryId, Item slot, RepairBench bench, BasePlayer player)
            => OnItemSkinChange(inventoryId, slot, bench, player);

        public void HarmonyOnPlayerDeath(BasePlayer player, HitInfo info) => OnPlayerDeath(player, info);

        public object HarmonyOnDispenserGather(ResourceDispenser dispenser, BasePlayer player, Item item)
            => OnDispenserGather(dispenser, player, item);

        public void HarmonyOnItemRemovedFromContainer(ItemContainer container, Item item)
            => OnItemRemovedFromContainer(container, item);

        public void HarmonyOnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
            => OnActiveItemChanged(player, oldItem, newItem);

        public object HarmonyOnItemRecycle(Item item, Recycler recycler) => OnItemRecycle(item, recycler);

        public object HarmonyOnStructureUpgrade(BuildingBlock block, BasePlayer player, BuildingGrade.Enum grade)
            => OnStructureUpgrade(block, player, grade);

        public void HarmonyOnEntitySpawned(SimpleShark shark) => OnEntitySpawned(shark);

        public void HarmonyOnEntityKill(SimpleShark shark) => OnEntityKill(shark);

        public object HarmonyOnHammerHit(BasePlayer player, HitInfo info) => OnHammerHit(player, info);

        public void HarmonyOnEntityBuilt(Planner planner, UnityEngine.GameObject gameObject)
            => OnEntityBuilt(planner, gameObject);

        public void HarmonyOnItemAddedToContainer(ItemContainer container, Item item)
            => OnItemAddedToContainer(container, item);
    }
}
