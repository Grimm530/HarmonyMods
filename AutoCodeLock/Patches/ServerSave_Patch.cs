using HarmonyLib;
using UnityEngine;

namespace AutoCodeLockHarmony.Patches
{
    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.DoAutomatedSave), typeof(bool))]
    internal static class ServerSave_Patch
    {
        // Do not bind AndWait by name — unused; wrong names break Harmony patching.
        [HarmonyPostfix]
        private static void Postfix()
        {
            var plugin = AutoCodeLockMod.Instance?.Plugin;
            if (plugin == null) return;
            try { plugin.OnServerSave(); }
            catch (System.Exception ex) { Debug.LogWarning("[AutoCodeLock] OnServerSave: " + ex.Message); }
        }
    }
}
