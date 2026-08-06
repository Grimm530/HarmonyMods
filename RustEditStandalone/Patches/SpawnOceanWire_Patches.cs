using System.Collections.Generic;
using HarmonyLib;
using RustEditStandalone.Features;
using UnityEngine;

namespace RustEditStandalone.Patches;

[HarmonyPatch(typeof(ServerMgr), nameof(ServerMgr.FindSpawnPoint))]
public static class ServerMgr_FindSpawnPoint_Patch
{
    static void Postfix(ref BasePlayer.SpawnPoint __result)
    {
        if (!SpawnFeature.HasCustomSpawns) return;
        if (SpawnFeature.TryGetSpawnPoint(out var sp))
            __result = sp;
    }
}

[HarmonyPatch(typeof(BaseBoat), nameof(BaseBoat.GenerateOceanPatrolPath))]
public static class BaseBoat_GenerateOceanPatrolPath_Patch
{
    static bool Prefix(ref List<Vector3> __result)
    {
        if (!OceanFeature.TryGetCustomPath(out var path)) return true;
        __result = path;
        return false;
    }
}

[HarmonyPatch(typeof(WireTool), nameof(WireTool.AttemptClearSlot))]
public static class WireTool_AttemptClearSlot_Patch
{
    static bool Prefix(BaseNetworkable clearEnt, BasePlayer ply, ref bool __result)
    {
        if (clearEnt is IOEntity io && IoFeature.IsMapIo(io))
        {
            if (ply == null || !ply.IsAdmin)
            {
                __result = false;
                return false;
            }
        }
        return true;
    }
}
