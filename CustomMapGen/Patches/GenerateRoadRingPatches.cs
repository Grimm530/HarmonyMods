using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace CustomMapGen.Patches
{
    // Patch GenerateRoadRing.Process to control ring road generation
    [HarmonyPatch(typeof(GenerateRoadRing), nameof(GenerateRoadRing.Process))]
    public static class GenerateRoadRing_Process_Patch
    {
        /// <summary>
        /// Vanilla uses <c>World.Size &gt;= 5000</c> to pick the 8-node ring instead of the 4-node ring.
        /// Returning 0 makes that comparison always succeed when the ring is wanted.
        /// </summary>
        public static int RingSizeGate()
        {
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return 5000;
            var config = CustomMapGen.Instance?.GetConfig();
            if (config != null && config.GenerateRingRoad == "Wanted")
                return 0;
            return 5000;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var gate = AccessTools.Method(typeof(GenerateRoadRing_Process_Patch), nameof(RingSizeGate));
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                if (replaced == 0 && instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int value && value == 5000)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = gate;
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced == 0)
                UnityEngine.Debug.LogWarning("[CustomMapGen] GenerateRoadRing: size gate 5000 was not found. Maps under 5000 keep the 4-node ring.");
        }

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
