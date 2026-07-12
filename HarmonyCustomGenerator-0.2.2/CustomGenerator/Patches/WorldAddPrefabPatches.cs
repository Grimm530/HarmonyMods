using CustomGenerator.Utility;
using HarmonyLib;
using UnityEngine;

using static CustomGenerator.ExtConfig;

namespace CustomGenerator.Patches
{
    /// <summary>
    /// Blocks powerlines, ziplines, bandit camp (optional), and prefabs listed in BlockedPrefabs from being added during map generation.
    /// Optionally forces the single Outpost monument to spawn at map center instead of anywhere.
    /// </summary>
    [HarmonyPatch(typeof(World), nameof(World.AddPrefab), typeof(string), typeof(Prefab), typeof(Vector3), typeof(Quaternion), typeof(Vector3))]
    internal static class World_AddPrefab_Patch
    {
        static bool Prefix(string category, Prefab prefab, ref Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (prefab?.Name == null)
                return true;

            string nameLower = prefab.Name.ToLowerInvariant();

            // Block Bandit Camp when enabled (map has 1 bandit that can spawn anywhere; user may not want it)
            if (Config.Generator.BlockBanditCamp && nameLower.Contains("bandit"))
            {
                return false;
            }

            // Force the single Outpost/safe-zone monument to map center when enabled.
            // The center safe zone can be "compound" or "outpost" depending on game version — redirect both.
            // Never block this prefab: return immediately so BlockedPrefabs / other rules cannot block it.
            if (Config.Generator.ForceOutpostToCenter &&
                (nameLower.Contains("outpost") || nameLower.Contains("compound")))
            {
                float half = World.Size * 0.5f;
                position = new Vector3(half, position.y, half);
                Logging.Generation($"[OUTPOST] Forced {prefab.Name} to map center: ({half}, {position.y}, {half})");
                return true;
            }

            // Block powerline poles when powerlines are disabled
            if (Config.Generator.RemovePowerlines &&
                (nameLower.Contains("powerline_pole") || (category != null && category.IndexOf("owerline", System.StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                return false;
            }

            // Block zipline prefabs when ziplines are disabled
            if (Config.Generator.RemoveZiplines && nameLower.Contains("zipline"))
            {
                return false;
            }

            // Block any prefab matching BlockedPrefabs (e.g. coastal_rocks, rock_formation_small)
            if (Config.Generator.BlockedPrefabs != null && Config.Generator.BlockedPrefabs.Count > 0)
            {
                foreach (var blocked in Config.Generator.BlockedPrefabs)
                {
                    if (!string.IsNullOrEmpty(blocked) && nameLower.Contains(blocked.ToLowerInvariant()))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
