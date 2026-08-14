using System;
using HarmonyLib;
using UnityEngine;

namespace MinimapHarmony.Patches
{
    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Save), typeof(bool))]
    public static class ServerSave_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(bool __result)
        {
            if (!__result) return;
            try
            {
                MinimapHarmonyMod.Instance?.Plugin?.OnServerSave();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] OnServerSave: " + ex.Message);
            }
        }
    }
}
