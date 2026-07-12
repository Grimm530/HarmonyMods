using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FurnaceSplitter.Patches
{
    [HarmonyPatch(typeof(Item), "MoveToContainer", new[] {
        typeof(ItemContainer),
        typeof(int),
        typeof(bool),
        typeof(bool),
        typeof(BasePlayer),
        typeof(bool)
    })]
    internal static class Item_MoveToContainer_Patch
    {
        private static bool _handledByUs;
        private static readonly MethodInfo IsOutputItemMethod = AccessTools.Method(typeof(BaseOven), "IsOutputItem");

        [HarmonyPrefix]
        private static bool Prefix(Item __instance, ItemContainer newcontainer, int iTargetPos,
            bool allowStack, bool ignoreStackLimit, BasePlayer sourcePlayer, bool allowSwap,
            ref bool __result)
        {
            _handledByUs = false;

            if (sourcePlayer == null || newcontainer == null)
                return true;

            // Skip when moving within same container
            if (__instance.GetRootContainer() == newcontainer)
                return true;

            BaseOven oven = newcontainer.entityOwner as BaseOven;
            if (oven == null || !oven.allowByproductCreation)
                return true;

            bool debug = FurnaceSplitterConfig.Config?.debug == true;
            if (debug)
                FurnaceSplitterConfig.Log($"MoveToContainer: item={__instance.info.shortname} x{__instance.amount} -> {oven.ShortPrefabName} targetPos={iTargetPos}");

            // Skip when moving fuel - we don't want to intercept our own AutoAddFuel
            if (__instance.info == oven.fuelType)
            {
                if (debug) FurnaceSplitterConfig.Log($"  -> SKIP: item is fuel ({__instance.info.shortname})");
                return true;
            }

            var cookable = __instance.info.GetComponent<ItemModCookable>();
            // IsOutputItem is private/protected in BaseOven - use reflection to avoid MethodAccessException
            bool isOutputItem = IsOutputItemMethod != null && (bool)(IsOutputItemMethod.Invoke(oven, new object[] { __instance }) ?? false);
            if (cookable == null || isOutputItem)
            {
                if (debug) FurnaceSplitterConfig.Log($"  -> SKIP: cookable={cookable != null} isOutputItem={isOutputItem}");
                return true;
            }

            // When oven is off, GetTemperature(0) returns 15 - use design temp from oven.temperature instead
            float ovenTemp = FurnaceSplitterMod.GetOvenDesignTemperature(oven);
            if (cookable.lowTemp > ovenTemp || cookable.highTemp < ovenTemp)
            {
                if (debug) FurnaceSplitterConfig.Log($"  -> SKIP: temp mismatch ovenTemp={ovenTemp} cookable need {cookable.lowTemp}-{cookable.highTemp}");
                return true;
            }

            var cfg = FurnaceSplitterConfig.Config.GetOvenConfig(oven.ShortPrefabName);
            if (cfg == null)
            {
                if (debug) FurnaceSplitterConfig.Log($"  -> SKIP: no config for oven {oven.ShortPrefabName}");
                return true;
            }

            int totalSlots = oven.inputSlots;
            int splitAmount = __instance.amount;

            if (debug) FurnaceSplitterConfig.Log($"  -> Trying split: oven={oven.ShortPrefabName} inputSlots={totalSlots} splitAmount={splitAmount} autoFuel={cfg.autoFuelTransfer}");

            var result = FurnaceSplitterMod.TryFurnaceSplit(__instance, oven, totalSlots, splitAmount);
            if (result != FurnaceSplitterMod.MoveResult.Ok && result != FurnaceSplitterMod.MoveResult.SlotsFilled)
            {
                if (debug) FurnaceSplitterConfig.Log($"  -> Split returned {result} - handing to default game, Insert patch will try AutoAddFuel");
                ItemContainer_Insert_Patch.PendingOvenMovePlayer = sourcePlayer;
                ItemContainer_Insert_Patch.PendingOvenMoveOven = oven;
                return true;
            }

            _handledByUs = true;
            if (debug) FurnaceSplitterConfig.Log($"  -> HANDLED: split {result}, running AutoAddFuel");
            FurnaceSplitterLogic.AutoAddFuel(sourcePlayer.inventory, oven);
            __result = true;
            return false;
        }

        [HarmonyPostfix]
        private static void Postfix(bool __runOriginal, ref bool __result)
        {
            if (_handledByUs && !__runOriginal)
                __result = true;
            // Clear pending when we let default run (for Insert patch)
            if (__runOriginal)
            {
                ItemContainer_Insert_Patch.PendingOvenMovePlayer = null;
                ItemContainer_Insert_Patch.PendingOvenMoveOven = null;
            }
        }
    }
}
