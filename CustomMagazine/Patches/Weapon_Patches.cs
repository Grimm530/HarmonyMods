using HarmonyLib;
using UnityEngine;

namespace CustomMagazineHarmony.Patches
{
    [HarmonyPatch(typeof(BaseProjectile), "StartReload")]
    internal static class BaseProjectile_StartReload_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseProjectile __instance)
        {
            var plugin = CustomMagazineMod.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.ApplyMagazineScale(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[CustomMagazine] OnWeaponReload: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseProjectile), nameof(BaseProjectile.DelayedModsChanged))]
    internal static class BaseProjectile_DelayedModsChanged_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseProjectile __instance)
        {
            var plugin = CustomMagazineMod.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.ApplyMagazineScale(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[CustomMagazine] OnWeaponModChange: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(LootContainer), nameof(LootContainer.SpawnLoot))]
    internal static class LootContainer_SpawnLoot_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(LootContainer __instance)
        {
            var plugin = CustomMagazineMod.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnLootSpawn(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[CustomMagazine] OnLootSpawn: " + ex.Message); }
        }
    }
}
