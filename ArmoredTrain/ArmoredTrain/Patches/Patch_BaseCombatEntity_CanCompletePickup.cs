using HarmonyLib;
using ATPlugin = Oxide.Plugins.ArmoredTrain;

namespace ArmoredTrain.Patches
{
    /// <summary>Oxide CanPickupEntity — block picking up event brake switch / counters.</summary>
    [HarmonyPatch(typeof(BaseCombatEntity), "CanCompletePickup")]
    public static class Patch_BaseCombatEntity_CanCompletePickup
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseCombatEntity __instance, BasePlayer player, ref bool __result)
        {
            object result = ATPlugin.Dispatch_CanPickup(player, __instance);
            if (result == null)
                return true;
            __result = false;
            return false;
        }
    }
}
