using HarmonyLib;
using UnityEngine;

namespace RustLeagueHarmony.Patches
{
    [HarmonyPatch(typeof(AntiHack), nameof(AntiHack.AddViolation))]
    internal static class AntiHack_AddViolation_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BasePlayer ply, AntiHackType type)
        {
            var plugin = RustLeagueMod.Instance?.Plugin;
            if (plugin == null || ply == null) return true;
            try
            {
                if (plugin.TryBlockViolation(ply, type))
                    return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[RustLeague] OnPlayerViolation: " + ex.Message);
            }
            return true;
        }
    }
}
