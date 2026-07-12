using HarmonyLib;

namespace CustomMapGen.Patches
{
    // Patch ModularCar.OnDied to prevent car wrecks if configured
    [HarmonyPatch(typeof(ModularCar), nameof(ModularCar.OnDied))]
    public static class ModularCar_OnDied_Patch
    {
        static void Prefix(ModularCar __instance, HitInfo info)
        {
            if (CustomMapGen.IsCustomMapGenEnabled())
            {
                var config = CustomMapGen.Instance.GetConfig();
                if (config.RemoveCarWrecks)
                {
                    // Temporarily disable carwrecks for this car
                    // The original code checks vehicle.carwrecks, so we'll handle it in Postfix
                }
            }
        }
        
        static void Postfix(ModularCar __instance, HitInfo info)
        {
            if (CustomMapGen.IsCustomMapGenEnabled())
            {
                var config = CustomMapGen.Instance.GetConfig();
                if (config.RemoveCarWrecks && __instance != null && !__instance.IsDestroyed)
                {
                    // Force gib instead of wreck
                    __instance.Kill(BaseNetworkable.DestroyMode.Gib);
                }
            }
        }
    }
}
