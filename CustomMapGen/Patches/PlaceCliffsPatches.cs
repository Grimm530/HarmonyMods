using HarmonyLib;

namespace CustomMapGen.Patches
{
    [HarmonyPatch(typeof(PlaceCliffs), nameof(PlaceCliffs.Process))]
    public static class PlaceCliffs_Process_Patch
    {
        static bool Prefix(PlaceCliffs __instance, ref uint seed)
        {
            if (CustomMapGen.IsCustomMapGenEnabled())
            {
                var config = CustomMapGen.Instance.GetConfig();
                if (config.DisablePlaceCliffsPatch)
                    return true; // Run original (vanilla cliff behavior)
                if (!config.EnableCliffs)
                {
                    UnityEngine.Debug.Log("[CustomMapGen] Cliffs disabled - skipping PlaceCliffs.Process()");
                    return false;
                }
            }
            return true;
        }
    }
}
