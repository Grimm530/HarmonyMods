using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// Removes the cleanup-time unused-asset unload from WorldSetup.InitCoroutine so the server
    /// never hits the step that freezes (on both map generation and on loading a saved map).
    /// Pre-update builds called Resources.UnloadUnusedAssets() directly; current builds call
    /// ConVar.GC.unload() which wraps the same API. Match either call site.
    /// </summary>
    [HarmonyPatch(typeof(WorldSetup), nameof(WorldSetup.InitCoroutine), new[] { typeof(CancellationToken) })]
    public static class WorldSetup_InitCoroutine_SkipUnloadUnusedAssets_Transpiler
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo resourcesUnload = AccessTools.Method(typeof(Resources), nameof(Resources.UnloadUnusedAssets));
            // Resolve by name so we don't need a Facepunch.Console compile reference for ConVar.GC.
            MethodInfo gcUnload = AccessTools.Method(AccessTools.TypeByName("ConVar.GC"), "unload");

            for (int i = 0; i < list.Count; i++)
            {
                bool isResourcesUnload = resourcesUnload != null && list[i].Calls(resourcesUnload);
                bool isGcUnload = gcUnload != null && list[i].Calls(gcUnload);
                if (!isResourcesUnload && !isGcUnload)
                    continue;

                list.RemoveAt(i);
                // Resources.UnloadUnusedAssets returns AsyncOperation and is followed by Pop.
                // ConVar.GC.unload is void and has no Pop.
                if (isResourcesUnload && i < list.Count && list[i].opcode == OpCodes.Pop)
                    list.RemoveAt(i);

                if (CustomMapGen.Instance?.GetConfig()?.DebugLogging == true)
                {
                    string which = isGcUnload ? "ConVar.GC.unload()" : "Resources.UnloadUnusedAssets()";
                    UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Transpiler: removed {which} from WorldSetup.InitCoroutine to prevent freeze on load.");
                }
                break;
            }
            return list;
        }
    }
}
