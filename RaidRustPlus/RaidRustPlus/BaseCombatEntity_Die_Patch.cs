using HarmonyLib;

namespace RaidRustPlus;

[HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), typeof(HitInfo))]
public static class BaseCombatEntity_Die_Patch
{
    private static void Postfix(BaseCombatEntity __instance, HitInfo info)
    {
        RaidRustPlusMod.Instance?.OnEntityDeath(__instance, info);
    }
}
