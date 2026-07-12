using HarmonyLib;

namespace CustomMapGen.Patches
{
    // Patch GenerateRoadRing.Process to control ring road generation
    [HarmonyPatch(typeof(GenerateRoadRing), nameof(GenerateRoadRing.Process))]
    public static class GenerateRoadRing_Process_Patch
    {
        static bool Prefix(GenerateRoadRing __instance, ref uint seed)
        {
            if (CustomMapGen.IsCustomMapGenEnabled())
            {
                var config = CustomMapGen.Instance.GetConfig();
                
                // Skip ring road generation if not wanted
                if (config.GenerateRingRoad == "NotWanted")
                {
                    UnityEngine.Debug.Log("[CustomMapGen] Ring road disabled - skipping GenerateRoadRing.Process");
                    return false; // Skip original method
                }
                
                // If "Wanted", ensure MainRoads is enabled and ring isn't skipped on smaller maps
                if (config.GenerateRingRoad == "Wanted")
                {
                    if (!World.Config.MainRoads)
                    {
                        UnityEngine.Debug.Log("[CustomMapGen] Ring road wanted - enabling MainRoads");
                        World.Config.MainRoads = true;
                    }
                    // Allow ring road on any map size (game skips when World.Size < MinWorldSize, e.g. 3500/5000)
                    __instance.MinWorldSize = 0;
                }
            }
            
            return true; // Continue with original method
        }
    }
}
