using HarmonyLib;

namespace RustLeagueHarmony.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    internal static class BaseCombatEntity_Hurt_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            var plugin = RustLeagueMod.Instance?.Plugin;
            if (plugin == null || __instance == null || info == null) return;
            plugin.TryNegateEventDamage(__instance, info);
        }
    }

    // ModularCar splash hits modules first. ModuleHurt → DoExplosionForce runs with a 2.5
    // upwards modifier BEFORE BaseCombatEntity.Hurt, which is what roofs the shooter.
    [HarmonyPatch(typeof(BaseVehicle), nameof(BaseVehicle.DoExplosionForce))]
    internal static class BaseVehicle_DoExplosionForce_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseVehicle __instance)
        {
            if (__instance == null) return true;
            return __instance.GetComponent<RustLeaguePlugin.rustLeagueCar>() == null;
        }
    }
}
