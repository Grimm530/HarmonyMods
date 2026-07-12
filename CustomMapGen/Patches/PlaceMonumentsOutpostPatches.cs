using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// Outpost is not created here — it is moved to map center by redirecting the position
    /// in World_AddPrefab_Patch when the game adds the outpost. This patch is kept for
    /// compatibility; no extra placement logic runs.
    /// </summary>
    [HarmonyPatch(typeof(PlaceMonuments), nameof(PlaceMonuments.Process))]
    public static class PlaceMonuments_OutpostCenter_Patch
    {
        static void Postfix(PlaceMonuments __instance, uint seed)
        {
            // Outpost position is redirected to map center in World_AddPrefab_Patch
            // when the game calls World.AddPrefab for the outpost (move only, no second outpost).
        }
    }
}
