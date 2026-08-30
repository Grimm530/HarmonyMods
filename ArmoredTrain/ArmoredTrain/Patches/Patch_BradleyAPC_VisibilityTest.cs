using HarmonyLib;
using UnityEngine;
using ATPlugin = Oxide.Plugins.ArmoredTrain;

namespace ArmoredTrain.Patches
{
    /// <summary>
    /// Facepunch leftover: BradleyAPC.VisibilityTest logs "Standard vis test!" whenever the target
    /// is not a BasePlayer (train Bradleys hitting player auto turrets spam the server log).
    /// Default: skip that warning and still run the same non-player vis check.
    /// Config: Main Setting → "Log Facepunch Bradley standard vis test [true/false]".
    /// Player vis-tests are left to vanilla so other mods' VisibilityTest prefixes keep working.
    /// </summary>
    [HarmonyPatch(typeof(BradleyAPC), nameof(BradleyAPC.VisibilityTest))]
    public static class Patch_BradleyAPC_VisibilityTest
    {
        [HarmonyPrefix]
        public static bool Prefix(BradleyAPC __instance, BaseEntity ent, ref bool __result)
        {
            if (ATPlugin.Dispatch_LogBradleyStandardVisTest())
                return true;

            if (ent is BasePlayer)
                return true;

            if (__instance == null || ent == null)
            {
                __result = false;
                return false;
            }

            if (Vector3.Distance(ent.transform.position, __instance.transform.position) >= __instance.viewDistance)
            {
                __result = false;
                return false;
            }

            __result = __instance.IsVisible(ent.CenterPoint());
            return false;
        }
    }
}
