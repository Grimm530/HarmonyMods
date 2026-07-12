using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// When Retrieval is enabled, allow crafting to use ingredients from the backpack.
/// Temporarily adds backpack container to the craft source list.
/// </summary>
[HarmonyPatch(typeof(ItemCrafter), "CollectIngredient", new Type[] { typeof(int), typeof(int), typeof(List<Item>), typeof(bool) })]
internal class ItemCrafter_CollectIngredient_Patch
{
    [HarmonyPrefix]
    private static void Prefix(ItemCrafter __instance, ref bool __state)
    {
        __state = false;
        var owner = __instance.owner;
        if (owner == null) return;

        if (!(BetterBackpackConfig.Config?.RetrievalEnabled ?? true)) return;
        var mod = BetterBackpackMod.Instance;
        if (mod == null) return;
        var prefs = mod.GetOrCreatePrefs(owner);
        if (prefs == null || !prefs.RetrievalEnabled) return;

        var backpack = owner.inventory.GetBackpackWithInventory();
        if (backpack?.contents == null) return;

        var list = __instance.containers;
        if (list == null) return;
        list.Insert(0, backpack.contents);
        __state = true;
    }

    [HarmonyPostfix]
    private static void Postfix(ItemCrafter __instance, bool __state)
    {
        if (!__state) return;
        var owner = __instance.owner;
        if (owner == null) return;

        var backpack = owner.inventory.GetBackpackWithInventory();
        if (backpack?.contents == null) return;

        __instance.containers?.Remove(backpack.contents);
    }
}
