using HarmonyLib;
using UnityEngine;

namespace AirbourneSpawnHarmony.Patches
{
    /// <summary>
    /// Prefix target for a delayed Harmony.Patch of KitsHarmony.Kits.OnPlayerRespawned.
    /// Skips Kits autokit when the player is spawning onto the plane with a configured spawn kit.
    /// </summary>
    internal static class Kits_OnPlayerRespawned_Patch
    {
        public static bool Prefix(BasePlayer player)
        {
            try
            {
                if (AirbourneSpawnPlugin.ShouldSkipKitsAutoKit(player))
                    return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[AirbourneSpawn] Kits autokit skip: " + ex.Message);
            }
            return true;
        }
    }
}
