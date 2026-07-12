using HarmonyLib;
using UnityEngine;

namespace FurnaceSplitter.Patches
{
    /// <summary>
    /// When a cookable is added to an oven via default game logic (not our split),
    /// auto-add fuel from the player who has the oven open.
    /// </summary>
    [HarmonyPatch(typeof(ItemContainer), "Insert", typeof(Item))]
    internal static class ItemContainer_Insert_Patch
    {
        internal static BasePlayer PendingOvenMovePlayer;
        internal static BaseOven PendingOvenMoveOven;

        [HarmonyPostfix]
        private static void Postfix(ItemContainer __instance, Item item, bool __result)
        {
            bool debug = FurnaceSplitterConfig.Config?.debug == true;

            if (!__result || item == null)
                return;

            BaseOven oven = __instance.entityOwner as BaseOven;
            if (oven == null || !oven.allowByproductCreation)
                return;

            var cookable = item.info.GetComponent<ItemModCookable>();
            if (cookable == null)
                return;

            int inputMin = oven.fuelSlots; // _inputSlotIndex equals fuelSlots per BaseOven.PreInitShared
            int inputMax = oven.fuelSlots + oven.inputSlots;
            if (item.position < inputMin || item.position >= inputMax)
            {
                if (debug) FurnaceSplitterConfig.Log($"Insert POSTFIX: item {item.info.shortname} pos={item.position} outside input range [{inputMin},{inputMax})");
                return;
            }

            float ovenTemp = FurnaceSplitterMod.GetOvenDesignTemperature(oven);
            if (cookable.lowTemp > ovenTemp || cookable.highTemp < ovenTemp)
                return;

            var cfg = FurnaceSplitterConfig.Config.GetOvenConfig(oven.ShortPrefabName);
            if (cfg == null)
                return;

            BasePlayer player = PendingOvenMovePlayer;
            PendingOvenMovePlayer = null;
            PendingOvenMoveOven = null;

            if (debug)
                FurnaceSplitterConfig.Log($"Insert POSTFIX: cookable {item.info.shortname} x{item.amount} added at pos={item.position} (default game path) - PendingPlayer={player != null}");

            if (player == null || player.IsDestroyed || player.inventory == null)
            {
                if (debug) FurnaceSplitterConfig.Log($"Insert POSTFIX: skip AutoAddFuel - no PendingPlayer (MoveToContainer may not have set it)");
                return;
            }

            FurnaceSplitterLogic.AutoAddFuel(player.inventory, oven);
        }
    }
}
