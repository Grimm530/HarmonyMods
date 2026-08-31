using HarmonyLib;
using ATPlugin = Oxide.Plugins.ArmoredTrain;

namespace ArmoredTrain.Patches
{
    /// <summary>Oxide OnTurretAuthorize — block authorizing event train turrets.</summary>
    [HarmonyPatch(typeof(AutoTurret), "AddSelfAuthorize", new[] { typeof(BaseEntity.RPCMessage) })]
    public static class Patch_AutoTurret_AddSelfAuthorize
    {
        [HarmonyPrefix]
        public static bool Prefix(AutoTurret __instance, BaseEntity.RPCMessage rpc)
        {
            object result = ATPlugin.Dispatch_OnTurretAuthorize(__instance, rpc.player);
            return result == null;
        }
    }
}
