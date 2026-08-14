using System.IO;
using HarmonyLib;
using UnityEngine;
using QPlugin = Oxide.Plugins.Quest;

namespace QuestHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { QPlugin.Dispatch_OnPlayerConnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnPlayerConnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { QPlugin.Dispatch_OnPlayerDisconnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnPlayerDisconnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Save), typeof(string), typeof(bool))]
    public static class SaveRestore_Save_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { QPlugin.Dispatch_OnServerSave(); }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnServerSave: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Load))]
    public static class SaveRestore_Load_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(string strFilename)
        {
            try
            {
                if (string.IsNullOrEmpty(strFilename))
                    strFilename = World.SaveFolderName + "/" + World.SaveFileName;
                if (File.Exists(strFilename)) return;
                QPlugin.Dispatch_OnNewSave();
            }
            catch (System.Exception ex) { Debug.LogWarning("[Quest] OnNewSave: " + ex.Message); }
        }
    }
}
