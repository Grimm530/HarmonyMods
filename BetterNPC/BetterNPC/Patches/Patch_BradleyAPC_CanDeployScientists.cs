using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using BNPlugin = Harmony.Plugins.BetterNpc;

namespace BetterNpc.Patches
{
    [HarmonyPatch(typeof(BradleyAPC), nameof(BradleyAPC.CanDeployScientists))]
    public static class Patch_BradleyAPC_CanDeployScientists
    {
        [HarmonyPrefix]
        public static bool Prefix(BradleyAPC __instance, BaseEntity attacker, List<GameObjectRef> scientistPrefabs, List<Vector3> spawnPositions, ref bool __result)
        {
            object r = BNPlugin.Dispatch_CanDeployScientists(__instance, attacker, scientistPrefabs, spawnPositions);
            if (r is bool b && !b)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
