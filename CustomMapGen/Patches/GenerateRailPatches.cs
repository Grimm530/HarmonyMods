using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// When "Wanted": forces AboveGroundRails and MinWorldSize so rails run on any map size,
    /// and the 5000 size check is replaced so maps under 5000 get the 8-node ring.
    /// When "NotWanted": sets MinWorldSize = int.MaxValue so the rail ring component skips.
    /// </summary>
    [HarmonyPatch(typeof(GenerateRailRing), nameof(GenerateRailRing.Process))]
    public static class GenerateRailRing_Process_Patch
    {
        public static int RingSizeGate()
        {
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return 5000;
            var config = CustomMapGen.Instance?.GetConfig();
            if (config == null || config.DisableRailPatch)
                return 5000;
            if (config.GenerateAboveGroundTrainTracks == "Wanted")
                return 0;
            return 5000;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var gate = AccessTools.Method(typeof(GenerateRailRing_Process_Patch), nameof(RingSizeGate));
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
                UnityEngine.Debug.LogWarning("[CustomMapGen] GenerateRailRing: size gate 5000 was not found. Maps under 5000 keep the 4-node ring.");
        }

        static void Prefix(GenerateRailRing __instance)
        {
            if (!CustomMapGen.IsCustomMapGenEnabled() || World.Config == null)
                return;
            var config = CustomMapGen.Instance.GetConfig();
            if (config.GenerateAboveGroundTrainTracks == "NotWanted")
            {
                __instance.MinWorldSize = int.MaxValue;
                return;
            }
            if (config.GenerateAboveGroundTrainTracks != "Wanted")
                return;
            World.Config.AboveGroundRails = true;
            // Allow rails on any map size: game skips when World.Size < MinWorldSize (e.g. 3500/4000)
            if (World.Size < (uint)__instance.MinWorldSize)
                __instance.MinWorldSize = (int)World.Size;
        }
    }

    [HarmonyPatch(typeof(GenerateRailLayout), nameof(GenerateRailLayout.Process))]
    public static class GenerateRailLayout_Process_Patch
    {
        static void Prefix()
        {
            if (!CustomMapGen.IsCustomMapGenEnabled() || World.Config == null)
                return;
            var config = CustomMapGen.Instance.GetConfig();
            if (config.DisableRailPatch)
                return;
            if (config.GenerateAboveGroundTrainTracks == "Wanted")
                World.Config.AboveGroundRails = true;
        }
    }
}
