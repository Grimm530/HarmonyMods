using System.Collections.Generic;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// Removes Resources.UnloadUnusedAssets() from WorldSetup.InitCoroutine so the server
    /// never hits the step that freezes (on both map generation and on loading a saved map).
    /// There is no good reason for 200k+ asset unload on server load; memory is reclaimed on process exit.
    /// </summary>
    [HarmonyPatch(typeof(WorldSetup), nameof(WorldSetup.InitCoroutine), new[] { typeof(CancellationToken) })]
    public static class WorldSetup_InitCoroutine_SkipUnloadUnusedAssets_Transpiler
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var unloadMethod = AccessTools.Method(typeof(Resources), nameof(Resources.UnloadUnusedAssets));
            if (unloadMethod == null)
                return instructions;

            var list = new List<CodeInstruction>(instructions);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Calls(unloadMethod))
                {
                    list.RemoveAt(i);
                    if (i < list.Count && list[i].opcode == OpCodes.Pop)
                        list.RemoveAt(i);
                    if (CustomMapGen.Instance?.GetConfig()?.DebugLogging == true)
                        UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] Transpiler: removed Resources.UnloadUnusedAssets() from WorldSetup.InitCoroutine to prevent freeze on load.");
                    break;
                }
            }
            return list;
        }
    }
}
