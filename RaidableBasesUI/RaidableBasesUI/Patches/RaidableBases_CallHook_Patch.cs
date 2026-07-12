using System;
using UnityEngine;

namespace RaidableBasesBuyableUI.Patches
{
    /// <summary>
    /// Prefix for RaidableBases.Interface.CallHook - applied manually once RaidableBases is loaded.
    /// Intercepts OnPurchaseBase / OnPurchaseTakePayments / OnRaidableBasePurchased.
    /// </summary>
    public static class RaidableBases_CallHook_Patch
    {
        public static bool Prefix(string name, object[] args, ref object __result)
        {
            var plugin = RaidableBasesBuyableUIMod.Plugin;
            if (plugin == null || string.IsNullOrEmpty(name))
                return true;

            try
            {
                if (name == "OnPurchaseBase")
                {
                    var r = plugin.HandleOnPurchaseBase(args);
                    if (r != null)
                    {
                        __result = r;
                        return false;
                    }
                    return true;
                }

                if (name == "OnPurchaseTakePayments")
                {
                    var r = plugin.HandleOnPurchaseTakePayments(args);
                    if (r != null)
                    {
                        __result = r;
                        return false;
                    }
                    return true;
                }

                if (name == "OnRaidableBasePurchased")
                {
                    plugin.HandleOnRaidableBasePurchased(args);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBasesBuyableUI] CallHook " + name + ": " + ex.Message);
            }

            return true;
        }
    }
}
