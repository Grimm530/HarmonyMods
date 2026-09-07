using HarmonyLib;
using BNPlugin = Harmony.Plugins.BetterNpc;

namespace BetterNpc.Patches
{
    /// <summary>
    /// CargoShip.SpawnCrate is void in current Rust — fire BetterNPC hook after crates spawn.
    /// (Oxide OnCargoShipSpawnCrate could cancel; Harmony port observes only.)
    /// </summary>
    [HarmonyPatch(typeof(CargoShip), nameof(CargoShip.SpawnCrate))]
    public static class Patch_CargoShip_SpawnCrate
    {
        [HarmonyPostfix]
        public static void Postfix(CargoShip __instance)
        {
            BNPlugin.Dispatch_OnCargoShipSpawnCrate(__instance);
        }
    }

    [HarmonyPatch(typeof(CargoShip), nameof(CargoShip.OnArrivedAtHarbor))]
    public static class Patch_CargoShip_OnArrivedAtHarbor
    {
        [HarmonyPostfix]
        public static void Postfix(CargoShip __instance)
        {
            BNPlugin.Dispatch_OnCargoShipHarborArrived(__instance);
        }
    }
}
