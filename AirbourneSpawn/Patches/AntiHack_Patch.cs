using HarmonyLib;
using UnityEngine;

namespace AirbourneSpawnHarmony.Patches
{
    [HarmonyPatch(typeof(AntiHack), nameof(AntiHack.AddViolation))]
    internal static class AntiHack_AddViolation_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BasePlayer ply, AntiHackType type)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || ply == null) return true;
            try
            {
                if (plugin.TryBlockViolation(ply, type))
                    return false;
            }
            catch (System.Exception ex) { Hooks.Warn("OnPlayerViolation", ex); }
            return true;
        }
    }
}
