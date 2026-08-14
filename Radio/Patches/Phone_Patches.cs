using HarmonyLib;
using UnityEngine;

namespace RadioHarmony.Patches
{
    [HarmonyPatch(typeof(PhoneController), nameof(PhoneController.CallPhone))]
    internal static class Phone_CallPhone_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(PhoneController __instance, int number)
        {
            try
            {
                var receiver = TelephoneManager.GetTelephone(number);
                var mod = RadioMod.Instance;
                if (mod != null && receiver != null &&
                    mod.TryHandleGlobalRadioDial(__instance, receiver, __instance.currentPlayer))
                    return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Radio] CallPhone: " + ex.Message);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PhoneController), nameof(PhoneController.OnDialFailed))]
    internal static class Phone_OnDialFailed_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(PhoneController __instance, Telephone.DialFailReason reason)
        {
            try { RadioMod.Instance?.OnPhoneDialFailed(__instance, reason, __instance?.currentPlayer); }
            catch (System.Exception ex) { Debug.LogWarning("[Radio] OnDialFailed: " + ex.Message); }
        }
    }
}
