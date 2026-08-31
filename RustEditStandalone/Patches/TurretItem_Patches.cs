using HarmonyLib;
using RustEditStandalone.Components;
using RustEditStandalone.Features;

namespace RustEditStandalone.Patches;

[HarmonyPatch(typeof(AutoTurret), "AddSelfAuthorize", new[] { typeof(BasePlayer) })]
public static class AutoTurret_AddSelfAuthorize_Patch
{
    static bool Prefix(AutoTurret __instance, BasePlayer player)
    {
        if (__instance == null || !IoFeature.IsMapIo(__instance)) return true;
        if (player != null && player.IsAdmin) return true;
        return false;
    }
}

[HarmonyPatch(typeof(Item), nameof(Item.UseItem))]
public static class Item_UseItem_Patch
{
    static void Prefix(Item __instance, ref int amountToConsume)
    {
        if (__instance?.parent?.entityOwner is not AutoTurret turret) return;
        if (!IoFeature.IsUnlimitedTurret(turret)) return;
        var mgr = turret.GetComponent<AutoTurretManager>();
        if (mgr != null && mgr.ShouldRefundAmmoUse())
            amountToConsume = 0;
    }
}
