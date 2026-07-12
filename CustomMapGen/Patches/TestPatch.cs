using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    // Test patch to verify Harmony is working
    [HarmonyPatch(typeof(WorldSetup), nameof(WorldSetup.InitCoroutine))]
    public static class TestPatch
    {
        static void Prefix()
        {
            if (CustomMapGen.IsLoadingExistingMap)
                return;
            UnityEngine.Debug.Log("[CustomMapGen] ===== TEST PATCH WORKING - WorldSetup.InitCoroutine called =====");
        }
    }
}
