using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace DynamicCupShareHarmony.Patches
{
    [HarmonyPatch(typeof(ItemModStudyBlueprint), nameof(ItemModStudyBlueprint.ServerCommand))]
    internal static class ItemModStudyBlueprint_ServerCommand_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Item item, string command, BasePlayer player, ref int __state)
        {
            __state = -1;
            if (command != "study" || player == null || item == null || !item.IsBlueprint())
                return;
            __state = player.PersistantPlayerInfo?.unlockedItems?.Count ?? 0;
        }

        [HarmonyPostfix]
        private static void Postfix(Item item, string command, BasePlayer player, int __state)
        {
            if (__state < 0 || player == null || item == null) return;
            int after = player.PersistantPlayerInfo?.unlockedItems?.Count ?? 0;
            if (after <= __state) return;

            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return;
            try { plugin.OnStudiedBlueprint(player, item.blueprintTargetDef); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] OnStudiedBlueprint: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Workbench), nameof(Workbench.RPC_TechTreeUnlock))]
    internal static class Workbench_RPC_TechTreeUnlock_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseEntity.RPCMessage msg)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null || msg.player == null) return;
            try { plugin.BeginTechTreeUnlock(msg.player); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] BeginTechTreeUnlock: " + ex.Message); }
        }

        [HarmonyPostfix]
        private static void Postfix(BaseEntity.RPCMessage msg)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null || msg.player == null) return;
            try { plugin.FinishTechTreeUnlock(msg.player); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] FinishTechTreeUnlock: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PlayerBlueprints), nameof(PlayerBlueprints.Unlock), typeof(ItemDefinition))]
    internal static class PlayerBlueprints_Unlock_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(PlayerBlueprints __instance, ItemDefinition itemDef)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null || __instance == null || itemDef == null) return;
            try { plugin.NoteTechTreeUnlock(__instance.baseEntity, itemDef); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] NoteTechTreeUnlock: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PlayerBlueprints), nameof(PlayerBlueprints.UnlockList), typeof(List<ItemDefinition>))]
    internal static class PlayerBlueprints_UnlockList_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(PlayerBlueprints __instance, List<ItemDefinition> itemDefList)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null || __instance == null || itemDefList == null) return;
            try
            {
                BasePlayer player = __instance.baseEntity;
                for (int i = 0; i < itemDefList.Count; i++)
                    plugin.NoteTechTreeUnlock(player, itemDefList[i]);
            }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] NoteTechTreeUnlockList: " + ex.Message); }
        }
    }

    /// <summary>
    /// Oxide OnNewSave — SaveRestore.Load when the save file does not exist (wipe / fresh map).
    /// </summary>
    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Load))]
    internal static class SaveRestore_Load_BlueprintWipe_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(string strFilename)
        {
            try
            {
                if (string.IsNullOrEmpty(strFilename))
                    strFilename = World.SaveFolderName + "/" + World.SaveFileName;
                if (File.Exists(strFilename))
                    return;

                var plugin = DynamicCupShareMod.Instance?.Plugin;
                plugin?.OnNewSave();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[DynamicCupShare] OnNewSave: " + ex.Message);
            }
        }
    }
}
