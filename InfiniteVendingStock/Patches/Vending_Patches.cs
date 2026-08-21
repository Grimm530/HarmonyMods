using HarmonyLib;
using UnityEngine;

namespace InfiniteVendingStock.Patches
{
    [HarmonyPatch(typeof(NPCVendingMachine), nameof(NPCVendingMachine.InstallFromVendingOrders))]
    internal static class NPCVendingMachine_InstallFromVendingOrders_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(NPCVendingMachine __instance)
        {
            InfiniteVendingStockService service = InfiniteVendingStockMod.Service;
            if (service == null || __instance == null || __instance.IsDestroyed) return;

            try
            {
                service.OnVendingOrdersInstalled(__instance);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[InfiniteVendingStock] InstallFromVendingOrders: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(VendingMachine), nameof(VendingMachine.DoTransaction))]
    internal static class VendingMachine_DoTransaction_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(VendingMachine __instance)
        {
            InfiniteVendingStockService service = InfiniteVendingStockMod.Service;
            if (service == null || __instance == null) return;

            NPCVendingMachine npc = __instance as NPCVendingMachine;
            if (npc == null) return;

            try
            {
                service.OnVendingTransaction(npc);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[InfiniteVendingStock] DoTransaction: " + ex.Message);
            }
        }
    }
}
