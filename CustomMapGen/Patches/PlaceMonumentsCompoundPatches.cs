using HarmonyLib;
using System.Collections.Generic;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// Compound entity spawning removed: monuments are only replaced via SwapMonuments (custom .map prefabs).
    /// This patch is a no-op; ClearDeferredList is kept for ProcgenConfigApplyPatches.
    /// </summary>
    [HarmonyPatch(typeof(PlaceMonuments), nameof(PlaceMonuments.Process))]
    public static class PlaceMonumentsCompound_Patch
    {
        public struct DeferredCompoundEntityData
        {
            public string PrefabName;
            public float WX, WY, WZ;
            public float RX, RY, RZ, RW;
            public float SX, SY, SZ;
        }

        private static readonly List<DeferredCompoundEntityData> DeferredCompoundEntities = new List<DeferredCompoundEntityData>();

        /// <summary>Clear deferred list at start of map gen. Kept for ProcgenConfigApplyPatches.</summary>
        public static void ClearDeferredList()
        {
            DeferredCompoundEntities.Clear();
        }

        static void Postfix(PlaceMonuments __instance, uint seed)
        {
            // No-op: compound entities no longer added; only monument swap (custom .map) is used
        }
    }
}
