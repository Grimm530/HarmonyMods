using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace PlayerSkinsHarmony.Patches
{
    [HarmonyPatch(typeof(ItemCrafter), "FinishCrafting", new[] { typeof(ItemCraftTask) })]
    internal static class ItemCrafter_FinishCrafting_Patch
    {
        [ThreadStatic]
        private static Item _craftedItem;

        private static void CaptureCraftedItem(Item item) => _craftedItem = item;

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            var capture = AccessTools.Method(typeof(ItemCrafter_FinishCrafting_Patch), nameof(CaptureCraftedItem));

            for (int i = 0; i < list.Count; i++)
            {
                var ci = list[i];
                if (ci.opcode != OpCodes.Call && ci.opcode != OpCodes.Callvirt) continue;
                if (!(ci.operand is MethodInfo mi) || mi.Name != "CreateByItemID") continue;
                if (mi.DeclaringType != typeof(ItemManager) && mi.DeclaringType?.Name != "ItemManager") continue;

                list.Insert(i + 1, new CodeInstruction(OpCodes.Dup));
                list.Insert(i + 2, new CodeInstruction(OpCodes.Call, capture));
                break;
            }

            return list;
        }

        [HarmonyPostfix]
        private static void Postfix(ItemCrafter __instance, ItemCraftTask task)
        {
            var item = _craftedItem;
            _craftedItem = null;
            if (task == null || __instance == null || item == null) return;
            try { PlayerSkinsMod.Instance?.Plugin?.OnItemCraftFinished(task, item, __instance); }
            catch (Exception ex) { Debug.LogWarning("[PlayerSkins] OnItemCraftFinished: " + ex.Message); }
        }
    }
}
