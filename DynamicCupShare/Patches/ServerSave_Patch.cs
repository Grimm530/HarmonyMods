using HarmonyLib;
using UnityEngine;

namespace DynamicCupShareHarmony.Patches
{
    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.DoAutomatedSave), typeof(bool))]
    internal static class ServerSave_Patch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return;
            try { plugin.OnServerSave(); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] OnServerSave: " + ex.Message); }
        }
    }
}
