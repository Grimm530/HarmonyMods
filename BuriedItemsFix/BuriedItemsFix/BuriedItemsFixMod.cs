using HarmonyLib;
using UnityEngine;

namespace BuriedItemsFix;

public class BuriedItemsFixMod : IHarmonyModHooks
{
    void IHarmonyModHooks.OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Debug.Log("[BuriedItemsFix] Loaded - skipping bury for dropped items with null/invalid ItemDefinition.");
    }

    void IHarmonyModHooks.OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        Debug.Log("[BuriedItemsFix] Unloaded.");
    }
}

[HarmonyPatch(typeof(BuriedItems), nameof(BuriedItems.Register))]
internal static class BuriedItems_Register_Patch
{
    [HarmonyPrefix]
    private static bool Prefix(Item item)
    {
        if (item == null || item.info == null || !item.uid.IsValid)
            return false;

        return true;
    }
}
