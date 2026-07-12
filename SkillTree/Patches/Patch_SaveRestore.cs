// OnServerSave / OnNewSave — mirrors Backpacks pattern.
using HarmonyLib;
using UnityEngine;
using STPlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony.Patches
{
    /// <summary>OnServerSave — postfix on SaveRestore.Save.</summary>
    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Save), typeof(string), typeof(bool))]
    public static class SaveRestore_Save_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { STPlugin.Dispatch_OnServerSave(); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnServerSave: " + ex.Message); }
        }
    }

    /// <summary>
    /// OnNewSave — postfix on SaveRestore.Load.
    /// Fires only when the WipeId changes between loads (wipe detection).
    /// </summary>
    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Load))]
    public static class SaveRestore_Load_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(string strFilename, bool __result)
        {
            if (!__result) return;
            try
            {
                var wipeId = SaveRestore.WipeId ?? "";
                var prev   = AppDomain.GetData("SkillTree_LastWipeId") as string;
                if (prev != null && prev == wipeId) return;
                AppDomain.SetData("SkillTree_LastWipeId", wipeId);
                if (prev == null) return; // first load, not a wipe
                STPlugin.Dispatch_OnNewSave(strFilename ?? "");
            }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnNewSave: " + ex.Message); }
        }

        // AppDomain wrappers for convenience.
        private static System.AppDomain AppDomain => System.AppDomain.CurrentDomain;
    }
}
