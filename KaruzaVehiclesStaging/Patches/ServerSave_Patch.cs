using HarmonyLib;
using UnityEngine;

namespace KaruzaVehicles.Patches
{
    [HarmonyPatch(typeof(SaveRestore), "DoAutomatedSave", typeof(bool))]
    internal static class SaveRestore_DoAutomatedSave_Patch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            var ce = KaruzaVehiclesMod.Instance?.CustomEntities;
            if (ce == null) return;
            try { ce.OnServerSave(); }
            catch (System.Exception ex) { Debug.LogWarning("[KaruzaVehicles] OnServerSave: " + ex.Message); }
        }
    }
}
