using HarmonyLib;
using UnityEngine;

namespace RustEditStandalone.Patches;

[HarmonyPatch(typeof(World), nameof(World.TrackSpawnedPrefab))]
public static class World_TrackSpawnedPrefab_Patch
{
    static void Postfix(string category, GameObject instance)
    {
        if (RustEditStandaloneMod.Instance == null) return;
        RustEditStandaloneMod.Instance.OnPrefabSpawned(instance, category);
    }
}
