using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace CustomMapGen.Patches
{
    // Patch CargoNotifier private Start method to control cargo ship path embedding
    [HarmonyPatch]
    public static class CargoNotifier_Start_Patch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            return typeof(CargoNotifier).GetMethod("Start", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        }
        
        static bool Prefix(CargoNotifier __instance)
        {
            if (CustomMapGen.IsCustomMapGenEnabled())
            {
                var config = CustomMapGen.Instance.GetConfig();
                if (!config.EmbedCargoShipPath)
                {
                    UnityEngine.Debug.Log("[CustomMapGen] Cargo ship path embedding disabled - skipping CargoNotifier registration");
                    return false; // Skip original method
                }
            }
            
            return true; // Continue with original method
        }
    }
}
