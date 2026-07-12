using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// When Retrieval is enabled, include backpack when finding ammo (gun reload).
/// Uses reflection to avoid AmmoTypes type resolution at compile time.
/// </summary>
internal static class PlayerInventory_FindAmmo_Patch
{
    private static Type _ammoTypes;
    private static MethodInfo _findAmmoSingle;
    private static MethodInfo _hasAmmo;

    internal static void Patch(Harmony harmony)
    {
        _ammoTypes = ResolveAmmoTypes();
        if (_ammoTypes == null)
        {
            UnityEngine.Debug.LogWarning("[BetterBackpack] AmmoTypes not found; reload-from-backpack will rely on client inventory sync only.");
            return;
        }

        var findAmmo1 = typeof(PlayerInventory).GetMethod("FindAmmo", new[] { _ammoTypes });
        var findAmmo2 = typeof(PlayerInventory).GetMethod("FindAmmo", new[] { typeof(List<Item>), _ammoTypes });
        var hasAmmo = typeof(PlayerInventory).GetMethod("HasAmmo", new[] { _ammoTypes });

        if (findAmmo1 != null)
            harmony.Patch(findAmmo1, postfix: new HarmonyMethod(typeof(PlayerInventory_FindAmmo_Patch).GetMethod(nameof(FindAmmo_Single_Postfix), BindingFlags.Static | BindingFlags.NonPublic)));
        if (findAmmo2 != null)
            harmony.Patch(findAmmo2, postfix: new HarmonyMethod(typeof(PlayerInventory_FindAmmo_Patch).GetMethod(nameof(FindAmmo_List_Postfix), BindingFlags.Static | BindingFlags.NonPublic)));
        if (hasAmmo != null)
            harmony.Patch(hasAmmo, postfix: new HarmonyMethod(typeof(PlayerInventory_FindAmmo_Patch).GetMethod(nameof(HasAmmo_Postfix), BindingFlags.Static | BindingFlags.NonPublic)));

        _findAmmoSingle = typeof(ItemContainer).GetMethod("FindAmmo", new[] { _ammoTypes });
        _hasAmmo = typeof(ItemContainer).GetMethod("HasAmmo", new[] { _ammoTypes });
    }

    private static Type ResolveAmmoTypes()
    {
        var t = Type.GetType("Rust.AmmoTypes, Assembly-CSharp")
            ?? Type.GetType("AmmoTypes, Assembly-CSharp")
            ?? Type.GetType("Rust.AmmoTypes, Rust.Data");
        if (t != null) return t;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                t = asm.GetType("Rust.AmmoTypes") ?? asm.GetType("AmmoTypes");
                if (t != null) return t;
            }
            catch { /* ignore */ }
        }
        return null;
    }

    private static void FindAmmo_Single_Postfix(PlayerInventory __instance, object ammoType, ref Item __result)
    {
        if (__result != null) return;
        if (!RetrievalEnabled(__instance)) return;

        var backpack = __instance.GetBackpackWithInventory();
        if (backpack?.contents == null || _findAmmoSingle == null) return;

        try
        {
            __result = _findAmmoSingle.Invoke(backpack.contents, new[] { ammoType }) as Item;
        }
        catch { }
    }

    private static void FindAmmo_List_Postfix(PlayerInventory __instance, List<Item> list, object ammoType)
    {
        if (!RetrievalEnabled(__instance)) return;

        var backpack = __instance.GetBackpackWithInventory();
        if (backpack?.contents == null) return;

        try
        {
            var findAmmoList = typeof(ItemContainer).GetMethod("FindAmmo", new[] { typeof(List<Item>), _ammoTypes });
            findAmmoList?.Invoke(backpack.contents, new object[] { list, ammoType });
        }
        catch { }
    }

    private static void HasAmmo_Postfix(PlayerInventory __instance, object ammoType, ref bool __result)
    {
        if (__result) return;
        if (!RetrievalEnabled(__instance)) return;

        var backpack = __instance.GetBackpackWithInventory();
        if (backpack?.contents == null || _hasAmmo == null) return;

        try
        {
            __result = (bool)(_hasAmmo.Invoke(backpack.contents, new[] { ammoType }) ?? false);
        }
        catch { }
    }

    private static bool RetrievalEnabled(PlayerInventory inventory)
    {
        if (!(BetterBackpackConfig.Config?.RetrievalEnabled ?? true)) return false;
        var mod = BetterBackpackMod.Instance;
        if (mod == null) return false;
        var player = inventory.GetComponent<BasePlayer>();
        if (player == null) return false;
        var prefs = mod.GetOrCreatePrefs(player);
        return prefs != null && prefs.RetrievalEnabled;
    }
}
