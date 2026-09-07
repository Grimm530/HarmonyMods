using HarmonyLib;

namespace RustLeagueHarmony.Patches
{
    // ModularCar splash hits modules first. ModuleHurt → DoExplosionForce runs with a 2.5
    // upwards modifier BEFORE BaseCombatEntity.Hurt, which is what roofs the shooter.
    [HarmonyPatch(typeof(BaseVehicle), nameof(BaseVehicle.DoExplosionForce))]
    internal static class BaseVehicle_DoExplosionForce_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseVehicle __instance)
        {
            return __instance.GetComponent<RustLeaguePlugin.rustLeagueCar>() == null;
        }
    }
}
