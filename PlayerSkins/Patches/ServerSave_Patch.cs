using HarmonyLib;
using UnityEngine;

namespace PlayerSkinsHarmony.Patches
{
    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.DoAutomatedSave), typeof(bool))]
    internal static class ServerSave_Patch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            try { PlayerSkinsMod.Instance?.OnServerSave(); }
            catch (System.Exception ex) { Debug.LogWarning("[PlayerSkins] OnServerSave: " + ex.Message); }
        }
    }
}
