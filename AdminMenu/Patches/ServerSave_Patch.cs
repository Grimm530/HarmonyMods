using System;
using HarmonyLib;
using UnityEngine;

namespace AdminMenuHarmony.Patches
{
    /// <summary>Oxide OnServerSave → SaveRestore.Save(bool) postfix.</summary>
    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Save), typeof(bool))]
    public static class ServerSave_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(bool __result)
        {
            if (!__result) return;
            try
            {
                AdminMenuHarmonyMod.Instance?.Plugin?.OnServerSave();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AdminMenu] OnServerSave: " + ex.Message);
            }
        }
    }
}
