using HarmonyLib;

namespace Prodigy.Patches;

[HarmonyPatch(typeof(BaseEntity), nameof(BaseEntity.OnPlaced), new[] { typeof(BasePlayer) })]
public static class Patch_BaseEntity_OnPlaced
{
    static void Postfix(BaseEntity __instance, BasePlayer player)
    {
        if (__instance == null || player == null) return;
        if (!(__instance is BuildingPrivlidge or BuildingBlock)) return;

        var mod = ProdigyMod.Instance;
        if (mod == null) return;

        mod.OnEntityPlaced(__instance, player);
    }
}
