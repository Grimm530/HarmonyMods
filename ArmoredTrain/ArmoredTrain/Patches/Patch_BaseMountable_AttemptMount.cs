using HarmonyLib;
using ATPlugin = Oxide.Plugins.ArmoredTrain;

namespace ArmoredTrain.Patches
{
    /// <summary>
    /// Port of ArmoredTrain CanMountEntity(BasePlayer, BaseVehicleSeat): deny real players from
    /// mounting event train seats. Non-null result -> block AttemptMount.
    /// </summary>
    [HarmonyPatch(typeof(BaseMountable), nameof(BaseMountable.AttemptMount), new[] { typeof(BasePlayer), typeof(bool) })]
    public static class Patch_BaseMountable_AttemptMount
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseMountable __instance, BasePlayer player)
        {
            object result = ATPlugin.Dispatch_CanMount(__instance, player);
            return result == null;
        }
    }
}
