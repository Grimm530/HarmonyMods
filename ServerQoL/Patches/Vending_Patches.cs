using HarmonyLib;
using UnityEngine;

namespace ServerQoL.Patches
{
    [HarmonyPatch(typeof(VendingMachine), nameof(VendingMachine.BuyItem))]
    internal static class VendingMachine_BuyItem_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(VendingMachine __instance)
        {
            ServerQoLService service = ServerQoLMod.Service;
            if (service == null || __instance == null) return;
            if (!(__instance is NPCVendingMachine)) return;

            try
            {
                BasePlayer buyer = __instance.vend_Player;
                if (buyer == null) return;
                service.OnBuyVendingItem(__instance, buyer, __instance.vend_sellOrderID, __instance.vend_numberOfTransactions);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[ServerQoL] BuyItem: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(VendingMachine), nameof(VendingMachine.CompletePendingOrder))]
    internal static class VendingMachine_CompletePendingOrder_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(VendingMachine __instance)
        {
            ServerQoLService service = ServerQoLMod.Service;
            if (service == null || __instance == null) return;
            if (!(__instance is NPCVendingMachine npc)) return;

            try
            {
                service.OnVendingTransaction(npc);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[ServerQoL] CompletePendingOrder: " + ex.Message);
            }
        }
    }
}
